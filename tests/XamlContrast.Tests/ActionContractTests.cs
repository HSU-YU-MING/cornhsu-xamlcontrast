using System.Text.Json;
using System.Text.RegularExpressions;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// GitHub Action（action.yml）與工具之間的接縫。Action 的 inputs 是 1.0 凍結面之一，
/// 而它的 jq 直接讀 JSON／SARIF 的欄位 —— CLAUDE.md：「改了 JSON 輸出的形狀 → 確認
/// action.yml 裡那幾段 jq 還讀得到欄位」。這裡把那一步變成自動的：從 action.yml 讀出
/// jq 用到的欄位與它組的旗標，對真的輸出驗一次（不需要 jq／bash，任何 OS 都跑得動）。
/// </summary>
public class ActionContractTests
{
    private static readonly string ActionYml = File.ReadAllText(Repo.PathOf("action.yml"));

    private static AuditResult FailAndWarn()
    {
        using var fx = new Fixture();
        fx.File("Views/Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="fail"/>
              <TextBlock Foreground="#8A8A8A" Text="warn"/>
              <TextBlock Foreground="#111111" Text="ok"/>
            </Grid>
            """);
        return fx.Run();
    }

    [Fact] // 行內註記（::error file=…,line=…::…）的 jq 讀的每一個欄位，fail／warn 的 finding 上都要有
    public void AnnotationJqReadsFieldsTheJsonReportHas()
    {
        var program = Regex.Match(ActionYml, """jq -r --arg root "\$prefix" '(?<p>[^']+)'""").Groups["p"].Value;
        Assert.False(string.IsNullOrWhiteSpace(program), "action.yml 的註記 jq 找不到了 —— 改了寫法就同步更新這條測試的定位");

        var fields = Regex.Matches(program, @"\.(?<f>[A-Za-z]\w*)").Select(m => m.Groups["f"].Value).ToHashSet();
        Assert.Contains("findings", fields);
        fields.Remove("findings");
        Assert.NotEmpty(fields);

        var root = JsonDocument.Parse(Report.ToJson(FailAndWarn())).RootElement;
        var gated = root.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("category").GetString() is "fail" or "warn").ToList();
        Assert.Equal(2, gated.Count);
        foreach (var f in gated)
            foreach (var field in fields)
                Assert.True(f.TryGetProperty(field, out _), $"action.yml 的 jq 讀 .{field}，但 findings 上沒有這個欄位");

        // jq 比對的 category 字面值要是工具真的會輸出的標籤
        var literals = Regex.Matches(program, @"\.category == ""(?<v>\w+)""").Select(m => m.Groups["v"].Value).ToHashSet();
        Assert.Equal(new HashSet<string> { "fail", "warn" }, literals);
        Assert.Contains(Report.CategoryLabel(Category.Fail), literals);
        Assert.Contains(Report.CategoryLabel(Category.Warn), literals);
    }

    [Fact] // SARIF 路徑改寫（root 相對 → repo 相對）的 jq 指到的欄位，真的 SARIF 裡要有、而且是檔案路徑
    public void SarifRewriteJqTargetsTheUriTheReportCarries()
    {
        var path = Regex.Match(ActionYml, @"'\((?<p>\.runs[^)]*)\) \|=").Groups["p"].Value;
        Assert.Equal(".runs[].results[].locations[].physicalLocation.artifactLocation.uri", path);

        IEnumerable<JsonElement> current = [JsonDocument.Parse(Report.ToSarif(FailAndWarn(), "0.0.0")).RootElement];
        foreach (var seg in path.TrimStart('.').Split('.'))
        {
            var name = seg.TrimEnd('[', ']');
            current = current.Select(e => e.GetProperty(name)).ToList();
            if (seg.EndsWith("[]")) current = current.SelectMany(e => e.EnumerateArray()).ToList();
        }
        var uris = current.Select(e => e.GetString()).ToList();
        Assert.Equal(2, uris.Count);
        Assert.All(uris, u => Assert.Equal("Views/Main.xaml", u));
    }

    [Theory] // 註記與 SARIF 改寫都把 working-directory + root 接成 workspace 相對前綴，再接上 finding 的
             // root 相對路徑。前綴裡的 "./"、重複的 "/"、結尾的 "/" 不收乾淨，GitHub 就對不上檔案 ——
             // 註記標不到 PR 的行上、code scanning 對不到檔（而且兩者都不報錯，是安靜地失效）
    [InlineData(".", "src/MyApp", "src/MyApp/Views/Main.xaml")]     // 預設 working-directory
    [InlineData(".", "src/MyApp/", "src/MyApp/Views/Main.xaml")]    // 結尾斜線
    [InlineData("app", "src//MyApp", "app/src/MyApp/Views/Main.xaml")] // 重複斜線
    [InlineData("./app/", "src", "app/src/Views/Main.xaml")]        // 開頭 ./ + 接縫處的雙斜線
    [InlineData("a//b", "c//d", "a/b/c/d/Views/Main.xaml")]         // 不只收第一處
    public void ActionPrefixesFindingPathsIntoWorkspacePaths(string workingDirectory, string root, string expected)
    {
        // 兩個步驟（Annotate failing pairs、Upload SARIF to code scanning）的前綴算法必須一字不差
        var prefixLines = Regex.Matches(ActionYml, @"prefix=\$\(echo ""(?<echo>[^""]+)"" \| sed '(?<sed>[^']+)'\)")
            .Select(m => (Echo: m.Groups["echo"].Value, Sed: m.Groups["sed"].Value)).ToList();
        Assert.Equal(2, prefixLines.Count);
        var (echo, sed) = Assert.Single(prefixLines.Distinct());
        Assert.Equal("${{ inputs.working-directory }}/${{ inputs.root }}", echo);

        // 前綴之後接 "/" + finding 的 file —— 註記的 jq 與 SARIF 改寫的 jq 都是這樣接
        Assert.Contains("file=\\($root)/\\(.file)", ActionYml);
        Assert.Contains("($root + \"/\" + .)", ActionYml);

        var prefix = Sed(sed, $"{workingDirectory}/{root}");
        Assert.Equal(expected, $"{prefix}/Views/Main.xaml");
    }

    /// <summary>只支援 action.yml 實際用到的子集：以 ; 串接的 s|BRE|替換|旗標。
    /// 遇到子集以外的寫法就紅 —— 擴充 sed 的人要同步擴充這裡，而不是讓測試默默算錯。</summary>
    private static string Sed(string script, string input)
    {
        foreach (var raw in script.Split(';'))
        {
            var cmd = raw.Trim();
            Assert.True(cmd.Length > 2 && cmd[0] == 's', $"不支援的 sed 指令：{cmd}");
            var parts = cmd[2..].Split(cmd[1]);
            Assert.True(parts.Length == 3, $"不支援的 sed 指令：{cmd}");
            var (pattern, replacement, flags) = (parts[0], parts[1], parts[2]);
            // BRE 與 .NET 正則在 ^ $ . * \. 上同義；+ ? ( ) { } 在 BRE 是字面值、在 .NET 不是
            Assert.DoesNotMatch(@"[+?(){}]", pattern);
            Assert.DoesNotMatch(@"[&\\]", replacement);
            Assert.True(flags is "" or "g", $"不支援的 sed 旗標：{flags}");
            var re = new Regex(pattern);
            input = flags == "g" ? re.Replace(input, replacement) : re.Replace(input, replacement, 1);
        }
        return input;
    }

    [Fact] // inputs 的名稱、必填與預設值（凍結面）。改名、刪除、改預設都是破壞性變更 —— 這條紅了就是在提醒你
    public void ActionInputsArePinned()
    {
        var inputs = new Dictionary<string, (string? Required, string? Default)>();
        string? current = null;
        var inInputs = false;
        foreach (var line in ActionYml.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line == "inputs:") { inInputs = true; continue; }
            if (!inInputs) continue;
            if (line.Length > 0 && !line.StartsWith(' ')) break; // 下一個頂層區段（runs:）
            if (Regex.Match(line, @"^  (?<n>[a-z-]+):\s*$") is { Success: true } n)
            {
                current = n.Groups["n"].Value;
                inputs[current] = (null, null);
            }
            else if (current is not null && Regex.Match(line, @"^    (?<k>required|default):\s*(?<v>.*)$") is { Success: true } kv)
            {
                var v = kv.Groups["v"].Value.Trim();
                inputs[current] = kv.Groups["k"].Value == "required"
                    ? (v, inputs[current].Default)
                    : (inputs[current].Required, v);
            }
        }

        Assert.Equal(new Dictionary<string, (string?, string?)>
        {
            ["root"] = ("true", null),
            ["working-directory"] = ("false", "'.'"),
            ["version"] = ("false", "''"),
            ["fail-on"] = ("false", "'fail'"),
            ["baseline"] = ("false", "''"),
            ["strict-palette"] = ("false", "'false'"),
            ["comment"] = ("false", "'true'"),
            ["upload-report"] = ("false", "'true'"),
            ["sarif"] = ("false", "'false'"),
        }, inputs);
    }

    [Fact] // Action 組給 CLI 的每一個旗標，CLI 都要認得：把所有 input 都打開時組出的參數陣列跑一次，不能是 exit 2
    public void EveryFlagTheActionPassesIsAcceptedByTheCli()
    {
        var script = Regex.Match(ActionYml, @"- name: Run audit[\s\S]*?xamlcontrast ""\$\{args\[@\]\}""").Value;
        Assert.False(string.IsNullOrEmpty(script), "action.yml 的 Run audit 步驟找不到了");
        var flags = Regex.Matches(script, @"--[a-z][a-z-]*").Select(m => m.Value).ToHashSet();
        // 新增 input → 新的旗標 → 這裡要同步（並在下面的實跑裡加上它）
        Assert.Equal(new HashSet<string> { "--json", "--md", "--sarif", "--fail-on", "--strict-palette", "--baseline" }, flags);

        using var fx = CliProjects.Passing().WithPalette();
        fx.File("baseline.json", """{ "entries": [] }""");
        string P(string name) => Path.Combine(fx.Root, name);
        var r = Cli.Run(fx.Root, "--json", P("report.json"), "--md", P("report.md"), "--sarif", P("report.sarif"),
                        "--fail-on", "warn", "--strict-palette", "--baseline", P("baseline.json"));
        Assert.True(r.Exit == 0, r.ToString());
        Assert.True(File.Exists(P("report.json")) && File.Exists(P("report.md")) && File.Exists(P("report.sarif")));
    }
}

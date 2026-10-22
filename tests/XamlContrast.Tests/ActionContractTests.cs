using System.Diagnostics;
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
    private static readonly string ActionYml = File.ReadAllText(Repo.PathOf("action.yml")).Replace("\r\n", "\n");

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

    [Fact] // 行內註記（::error file=…,line=…::…）的 jq 讀的每一個欄位，fail／warn 的 finding 上都要有；
           // 等級依 summary.failOn（gate 擋的是 error）與 findings[].baseline（既有債是 notice）
    public void AnnotationJqReadsFieldsTheJsonReportHas()
    {
        var program = Regex.Match(ActionYml, """jq -r --arg root "\$prefix" '(?<p>[^']+)'""").Groups["p"].Value;
        Assert.False(string.IsNullOrWhiteSpace(program), "action.yml 的註記 jq 找不到了 —— 改了寫法就同步更新這條測試的定位");

        var fields = Regex.Matches(program, @"\.(?<f>[A-Za-z]\w*)").Select(m => m.Groups["f"].Value).ToHashSet();
        Assert.Contains("findings", fields);
        Assert.Contains(".summary.failOn", program);
        fields.ExceptWith(["findings", "summary", "failOn"]);
        Assert.NotEmpty(fields);

        var root = JsonDocument.Parse(Report.ToJson(FailAndWarn())).RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("summary").GetProperty("failOn").ValueKind);
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

        // gate 與 baseline 判定的字面值也要是工具真的會輸出的（docs/report.schema.json 的列舉）
        var schema = JsonDocument.Parse(File.ReadAllText(Repo.PathOf("docs/report.schema.json"))).RootElement.GetProperty("properties");
        string?[] EnumOf(JsonElement e) => e.GetProperty("enum").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray();
        var gates = Regex.Matches(program, @"\$gate == ""(?<v>\w+)""").Select(m => m.Groups["v"].Value).ToList();
        Assert.NotEmpty(gates);
        Assert.All(gates, g => Assert.Contains(g, EnumOf(schema.GetProperty("summary").GetProperty("properties").GetProperty("failOn"))));
        var marks = Regex.Matches(program, @"\.baseline == ""(?<v>\w+)""").Select(m => m.Groups["v"].Value).ToList();
        Assert.NotEmpty(marks);
        Assert.All(marks, b => Assert.Contains(b, EnumOf(schema.GetProperty("findings").GetProperty("items").GetProperty("properties").GetProperty("baseline"))));
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
    // root 是 "."（XAML 就在 repo 頂層，常見寫法）：舊版 sed 把 "./." 只收成 "."，接上 finding 變成
    // "./Views/Main.xaml"，正是上面說的「./ 沒收乾淨」形狀；"src/." 原樣保留；"./" + "./src" 甚至收成 "/./src"
    [InlineData(".", ".", "Views/Main.xaml")]
    [InlineData(".", "src/.", "src/Views/Main.xaml")]
    [InlineData("./", "./src", "src/Views/Main.xaml")]
    [InlineData(".", "./app/./src", "app/src/Views/Main.xaml")]
    public void ActionPrefixesFindingPathsIntoWorkspacePaths(string workingDirectory, string root, string expected)
    {
        // 兩個步驟（Annotate failing pairs、Upload SARIF to code scanning）的前綴算法必須一字不差
        var prefixLines = Regex.Matches(ActionYml, @"prefix=\$\(echo ""(?<echo>[^""]+)"" \| sed '(?<sed>[^']+)'\)")
            .Select(m => (Echo: m.Groups["echo"].Value, Sed: m.Groups["sed"].Value)).ToList();
        Assert.Equal(2, prefixLines.Count);
        var (echo, sed) = Assert.Single(prefixLines.Distinct());
        Assert.Equal("$XC_WORKDIR/$XC_ROOT", echo);
        foreach (var step in new[] { "Annotate failing pairs", "Upload SARIF to code scanning" })
        {
            Assert.Matches(new Regex(@"^        XC_WORKDIR: \$\{\{ inputs\.working-directory \}\}$", RegexOptions.Multiline), Step(step));
            Assert.Matches(new Regex(@"^        XC_ROOT: \$\{\{ inputs\.root \}\}$", RegexOptions.Multiline), Step(step));
        }

        // 前綴非空才補 "/"（root 就是 workspace 頂層時前綴是空的，不能接出 "/Views/Main.xaml"），
        // 之後直接接 finding 的 file —— 註記的 jq 與 SARIF 改寫的 jq 都是這樣接
        Assert.Equal(2, Regex.Matches(ActionYml, @"if \[ -n ""\$prefix"" \]; then prefix=""\$prefix/""; fi").Count);
        Assert.Contains("file=\\($root)\\(.file)", ActionYml);
        Assert.Contains("($root + .)", ActionYml);

        var prefix = Sed(sed, $"{workingDirectory}/{root}");
        if (prefix.Length > 0) prefix += "/";
        Assert.Equal(expected, $"{prefix}Views/Main.xaml");
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

    /// <summary>action.yml 的 inputs：名稱 → (required, default)，值照 YAML 原文（含引號）。</summary>
    private static Dictionary<string, (string? Required, string? Default)> Inputs()
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
        return inputs;
    }

    [Fact] // inputs 的名稱、必填與預設值（凍結面）。改名、刪除、改預設都是破壞性變更 —— 這條紅了就是在提醒你。
           // 1.0：fail-on 預設 '' = 交給 config 的 failOn，沒有就是內建的 warn（0.x 是 'fail'，config 在 Action 裡蓋不掉）；
           // ＋min-coverage（'' = config 的 minCoverage，沒有就是 50）
    public void ActionInputsArePinned()
    {
        Assert.Equal(new Dictionary<string, (string?, string?)>
        {
            ["root"] = ("true", null),
            ["working-directory"] = ("false", "'.'"),
            ["version"] = ("false", "''"),
            ["fail-on"] = ("false", "''"),
            ["min-coverage"] = ("false", "''"),
            ["baseline"] = ("false", "''"),
            ["strict-palette"] = ("false", "'false'"),
            ["comment"] = ("false", "'true'"),
            ["upload-report"] = ("false", "'true'"),
            ["sarif"] = ("false", "'false'"),
        }, Inputs());
    }

    [Fact] // 沒有 outputs（凍結的現況）。新增 output 是 1.x 的 minor —— 退出碼只是內部的 step output
    public void ThereAreNoOutputs() => Assert.DoesNotMatch(new Regex("^outputs:", RegexOptions.Multiline), ActionYml);

    [Fact] // Action 組給 CLI 的每一個旗標，CLI 都要認得：把所有 input 都打開時組出的參數陣列跑一次，不能是 exit 2
    public void EveryFlagTheActionPassesIsAcceptedByTheCli()
    {
        var script = Regex.Match(ActionYml, @"- name: Run audit[\s\S]*?xamlcontrast ""\$\{args\[@\]\}""").Value;
        Assert.False(string.IsNullOrEmpty(script), "action.yml 的 Run audit 步驟找不到了");
        var flags = Regex.Matches(script, @"--[a-z][a-z-]*").Select(m => m.Value).ToHashSet();
        // 新增 input → 新的旗標 → 這裡要同步（並在下面的實跑裡加上它）
        Assert.Equal(new HashSet<string> { "--json", "--md", "--sarif", "--fail-on", "--min-coverage", "--strict-palette", "--baseline" }, flags);

        using var fx = CliProjects.Passing().WithPalette();
        fx.File("baseline.json", """{ "schemaVersion": 1, "entries": [] }""");
        string P(string name) => Path.Combine(fx.Root, name);
        var r = Cli.Run(fx.Root, "--json", P("report.json"), "--md", P("report.md"), "--sarif", P("report.sarif"),
                        "--fail-on", "warn", "--min-coverage", "0", "--strict-palette", "--baseline", P("baseline.json"));
        Assert.True(r.Exit == 0, r.ToString());
        Assert.True(File.Exists(P("report.json")) && File.Exists(P("report.md")) && File.Exists(P("report.sarif")));
    }

    // ---------- 1.0：input 一律經 env、先驗再用；工具版本跟著 action 的 major（照 Parity 的 ActionYmlTests）----------

    [Fact] // ${{ }} 是 shell 看到指令之前的字串替換：input 接到不可信的來源（例如 issue 內文）就變成 runner 上的指令。
           // run 區塊一律經 env 讀 input、事件欄位與 step output
    public void RunScriptsNeverInterpolateExpressionsDirectly()
    {
        var runBlocks = Regex.Matches(ActionYml,
                @"^(?<indent> +)run: (?:\|\n(?<body>(?:\k<indent> {2}.*\n|\n)*)|(?<line>.+)\n)", RegexOptions.Multiline)
            .Select(m => m.Groups["body"].Success && m.Groups["body"].Length > 0 ? m.Groups["body"].Value : m.Groups["line"].Value)
            .ToList();
        Assert.True(runBlocks.Count >= 7, $"只找到 {runBlocks.Count} 個 run: 區塊，解析可能壞了");
        Assert.All(runBlocks, body => Assert.DoesNotMatch(@"\$\{\{\s*(inputs\.|github\.event|steps\.)", body));
    }

    [Theory] // 沒給 version：裝這個 action 自己那個 major 的最新版（@v1 只跟隨 1.x —— 介面凍結的承諾）。
             // 0.x 不帶範圍，裝的是 NuGet 上最新的穩定版：下一個 major 一發佈，所有 @v1 的 workflow 就會跨過去。
             // "1.*" 取最高的 1.x 穩定版（NuGet 的 "[1.0.0,2.0.0)" 會取最低的 1.0.0，不能用）
    [InlineData("", "tool|install|--global|Cornhsu.XamlContrast|--version|1.*|\n")]
    [InlineData("1.0.0", "tool|install|--global|Cornhsu.XamlContrast|--version|1.0.0|\n")]
    public void InstallPassesTheVersionOrTheFloatingMajor(string version, string expected)
    {
        var r = RunStep("Install XamlContrast", new() { ["version"] = version });
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Equal(expected, r.Calls);
    }

    [Fact] // 鎖的 major = CHANGELOG 最新發佈版的 major。發 2.0.0 時這裡會紅，提醒把 action.yml 一起改成 "2.*"
    public void TheFloatingMajorIsTheLatestReleasesMajor()
    {
        var floating = Regex.Match(Step("Install XamlContrast"), @"--version ""(?<major>\d+)\.\*""");
        Assert.True(floating.Success, "沒指定 version 時要用 \"<major>.*\" 鎖住 major");
        var latest = Regex.Match(File.ReadAllText(Repo.PathOf("CHANGELOG.md")), @"^## \[?(?<major>\d+)\.\d+\.\d+", RegexOptions.Multiline);
        Assert.True(latest.Success, "CHANGELOG.md 找不到版本標題");
        Assert.Equal(latest.Groups["major"].Value, floating.Groups["major"].Value);
    }

    [Theory] // 布林與列舉 input 先驗再用：0.x 的 `fail-on: Warn`、`strict-palette: True`、`sarif: yes` 都靜默變成預設值。
             // 未知值 → exit 2（與 CLI 的用法錯誤同一碼）並點名是哪個 input。
             // 帶換行的值一律拒絕：CLI／dotnet 的錯誤訊息會把它原樣印出，換行後的內容落在行首就成了 runner 會解析的
             // workflow command。訊息不印 input 本身
    [InlineData("fail-on", "Warn", "fail-on must be")]
    [InlineData("fail-on", "error", "fail-on must be")]
    [InlineData("strict-palette", "True", "strict-palette must be")]
    [InlineData("sarif", "yes", "sarif must be")]
    [InlineData("comment", "1", "comment must be")]
    [InlineData("upload-report", "", "upload-report must be")]
    [InlineData("min-coverage", "50%", "min-coverage must be")]
    [InlineData("min-coverage", "-1", "min-coverage must be")]
    [InlineData("version", "1.*\n::error title=x::INJECTED", "line break")]
    [InlineData("baseline", "baseline.json\r", "line break")]
    [InlineData("root", "src\n::add-mask::INJECTED", "line break")]
    [InlineData("working-directory", ".\n::warning::INJECTED", "line break")]
    public void UnknownInputValuesFailBeforeAnythingRuns(string input, string value, string message)
    {
        var r = RunStep("Validate inputs", new() { [input] = value });
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(message, r.Stdout);
        Assert.DoesNotContain("INJECTED", r.Stdout + r.Stderr);
        Assert.Equal("", r.Calls);
    }

    [Theory]
    [InlineData("fail-on", "")]
    [InlineData("fail-on", "warn")]
    [InlineData("fail-on", "fail")]
    [InlineData("min-coverage", "")]
    [InlineData("min-coverage", "0")]
    [InlineData("min-coverage", "72.5")]
    [InlineData("strict-palette", "true")]
    [InlineData("sarif", "true")]
    [InlineData("comment", "false")]
    [InlineData("version", "1.0.0")]
    public void ValidInputValuesPass(string input, string value)
    {
        var r = RunStep("Validate inputs", new() { [input] = value });
        Assert.True(r.Exit == 0, r.ToString());
    }

    [Fact] // fail-on／min-coverage 空白就不傳旗標（交給 config，沒有就是內建預設 warn／50）—— 0.x 的 fail-on 預設 'fail'、
           // 只在 warn 時才傳旗標：config 的 failOn 在 Action 裡永遠蓋不掉，--fail-on fail 也傳不出去
    public void AuditStepLeavesTheGateToTheConfigUnlessAnInputSetsIt()
    {
        var plain = RunStep("Run audit");
        Assert.True(plain.Exit == 0, plain.ToString());
        Assert.StartsWith("src/App|--json|", plain.Calls);
        foreach (var flag in new[] { "--fail-on", "--min-coverage", "--strict-palette", "--sarif", "--baseline" })
            Assert.DoesNotContain(flag, plain.Calls);

        var all = RunStep("Run audit", new()
        {
            ["fail-on"] = "fail",
            ["min-coverage"] = "30",
            ["strict-palette"] = "true",
            ["sarif"] = "true",
            ["baseline"] = "xc-baseline.json",
        });
        foreach (var part in new[] { "|--fail-on|fail|", "|--min-coverage|30|", "|--strict-palette|", "|--sarif|", "|--baseline|xc-baseline.json|" })
            Assert.Contains(part, all.Calls);
    }

    [Theory] // 稽核的退出碼原樣交給最後一步（先產報告與註記，最後才擋）；這一步本身不失敗
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AuditStepRecordsTheExitCodeForTheGate(int exit)
    {
        var r = RunStep("Run audit", stubExit: exit);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Equal($"exit={exit}\n", r.Output);
    }

    /// <summary>一個 step（`    - name: …`）到下一個 step 之間的內容。</summary>
    private static string Step(string name)
    {
        var m = Regex.Match(ActionYml, $@"^    - name: {Regex.Escape(name)}\n(?<body>(?:(?!    - name: ).*\n)*)", RegexOptions.Multiline);
        Assert.True(m.Success, $"action.yml 找不到 step `{name}`");
        return m.Groups["body"].Value;
    }

    /// <summary>step 的 `run: |` 內容（去掉 8 格縮排）。</summary>
    private static string RunScript(string step)
    {
        var m = Regex.Match(Step(step), @"^      run: \|\n(?<body>(?:(?:        .*)?\n)*)", RegexOptions.Multiline);
        Assert.True(m.Success, $"step `{step}` 沒有 run: | 區塊");
        return string.Join("\n", m.Groups["body"].Value.Split('\n').Select(l => l.Length >= 8 ? l[8..] : l.TrimStart()));
    }

    /// <summary>bash：Linux/macOS 用 PATH 上的；Windows 用 Git Bash（從 PATH 上的 git.exe 反推，避開 System32 的 WSL 啟動器）。</summary>
    private static string BashPath()
    {
        if (!OperatingSystem.IsWindows()) return "bash";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(Path.Combine(dir, "git.exe")))
                foreach (var relative in new[] { @"..\bin", "", @"..\..\bin" }) // Git\cmd、Git\bin、Git\mingw64\bin
                    if (Path.GetFullPath(Path.Combine(dir, relative, "bash.exe")) is var bash && File.Exists(bash)) return bash;
        var fallback = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? @"C:\Program Files", "Git", "bin", "bash.exe");
        return File.Exists(fallback) ? fallback : throw new InvalidOperationException("找不到 Git Bash，這條測試需要它");
    }

    private sealed record StepRun(int Exit, string Stdout, string Stderr, string Output, string Calls)
    {
        public override string ToString() => $"exit {Exit}\n--- stdout\n{Stdout}\n--- stderr\n{Stderr}\n--- calls\n{Calls}";
    }

    /// <summary>
    /// 用 runner 的方式（bash -eo pipefail）跑一個 step 的 run 區塊。env 照那個 step 的 env: 區塊給：
    /// 沒指定的 input 用 action.yml 的預設值（root 給 src/App）。dotnet 與 xamlcontrast 換成 stub，
    /// 把收到的參數一行一筆記到 $RUNNER_TEMP/calls（xamlcontrast 回 <paramref name="stubExit"/>）。
    /// </summary>
    private static StepRun RunStep(string step, Dictionary<string, string>? inputs = null, int stubExit = 0)
    {
        var values = Inputs().ToDictionary(kv => kv.Key, kv => kv.Value.Default?.Trim('\'') ?? "");
        values["root"] = "src/App";
        foreach (var (k, v) in inputs ?? []) values[k] = v;

        using var dir = new Fixture();
        string Unix(string p) => p.Replace('\\', '/');
        var temp = Directory.CreateDirectory(Path.Combine(dir.Root, "_temp")).FullName;
        var output = Path.Combine(temp, "github_output");
        File.WriteAllText(output, "");
        var stub = $$"""
            dotnet() { printf '%s|' "$@" >> "$RUNNER_TEMP/calls"; printf '\n' >> "$RUNNER_TEMP/calls"; }
            xamlcontrast() { printf '%s|' "$@" >> "$RUNNER_TEMP/calls"; printf '\n' >> "$RUNNER_TEMP/calls"; return {{stubExit}}; }

            """;
        var script = Path.Combine(temp, "step.sh");
        File.WriteAllText(script, stub + RunScript(step));

        var psi = new ProcessStartInfo(BashPath())
        {
            WorkingDirectory = dir.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "--noprofile", "--norc", "-eo", "pipefail", Unix(script) }) psi.ArgumentList.Add(a);
        psi.Environment["RUNNER_TEMP"] = Unix(temp);
        psi.Environment["GITHUB_OUTPUT"] = Unix(output);
        psi.Environment["GITHUB_PATH"] = Unix(Path.Combine(temp, "github_path"));
        psi.Environment["HOME"] = Unix(dir.Root);
        foreach (Match m in Regex.Matches(Step(step), @"^        (?<v>[A-Z_]+): \$\{\{ inputs\.(?<i>[a-z-]+) \}\}$", RegexOptions.Multiline))
            psi.Environment[m.Groups["v"].Value] = values[m.Groups["i"].Value];
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(30_000), "bash 沒有在 30 秒內結束");
        var calls = Path.Combine(temp, "calls");
        return new StepRun(p.ExitCode, stdout.Result, stderr.Result, File.ReadAllText(output).Replace("\r\n", "\n"),
            File.Exists(calls) ? File.ReadAllText(calls).Replace("\r\n", "\n") : "");
    }
}

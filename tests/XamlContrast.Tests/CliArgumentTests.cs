using System.Diagnostics;

namespace XamlContrast.Tests;

/// <summary>
/// CLI 的用法面：exit 2 = 用法或設定錯誤（契約）。每一條錯誤用法都要落在 2 並說人話，
/// 不能噴 .NET 堆疊、也不能被當成「稽核跑完了」的 0／1 —— CI 分不出「工具沒跑」與
/// 「跑了沒問題」就是謊報健康。
/// </summary>
public class CliArgumentTests
{
    [Theory] // 旗標解析階段的每一條 exit 2：退出碼 + stderr 要說出是哪個旗標錯了
    [InlineData(new[] { "{root}", "--bogus" }, "unknown argument: --bogus")]
    [InlineData(new[] { "{root}", "second-root" }, "unknown argument: second-root")]
    [InlineData(new[] { "{root}", "--json" }, "--json takes a file path")]
    [InlineData(new[] { "{root}", "--sarif" }, "--sarif takes a file path")]
    [InlineData(new[] { "{root}", "--md" }, "--md takes a file path")]
    [InlineData(new[] { "{root}", "--fail-on" }, "--fail-on takes 'warn' or 'fail'")]
    [InlineData(new[] { "{root}", "--fail-on", "WARN" }, "--fail-on takes 'warn' or 'fail'")] // 值分大小寫
    [InlineData(new[] { "{root}", "--fail-on", "error" }, "--fail-on takes 'warn' or 'fail'")]
    [InlineData(new[] { "{root}", "--min-coverage" }, "--min-coverage takes a number between 0 and 100")]
    [InlineData(new[] { "{root}", "--min-coverage", "half" }, "--min-coverage takes a number between 0 and 100")]
    [InlineData(new[] { "{root}", "--min-coverage", "-1" }, "--min-coverage takes a number between 0 and 100")]
    [InlineData(new[] { "{root}", "--min-coverage", "100.5" }, "--min-coverage takes a number between 0 and 100")]
    [InlineData(new[] { "{root}", "--min-coverage", "Infinity" }, "--min-coverage takes a number between 0 and 100")]
    [InlineData(new[] { "{root}/no-such-dir" }, "root not found: ")]
    [InlineData(new[] { "{root}", "--baseline", "{root}/no-such-baseline.json" }, "baseline not found: ")]
    // 1.0：--baseline／--write-baseline 的路徑必填（0.x 可省略、預設 xamlcontrast-baseline.json，
    // 下一個字不以 - 開頭就被吃成路徑 —— `--baseline src/MyApp` 把 root 吃掉）
    [InlineData(new[] { "{root}", "--baseline" }, "--baseline takes a file path")]
    [InlineData(new[] { "{root}", "--write-baseline" }, "--write-baseline takes a file path")]
    [InlineData(new[] { "{root}", "--baseline", "--show-ok" }, "--baseline takes a file path")]
    [InlineData(new[] { "{root}", "--write-baseline", "--strict-palette" }, "--write-baseline takes a file path")]
    // 兩個模式並用沒有意義（寫出新的已知債，同時又跟舊的比？）—— 0.x 是 --write-baseline 勝、--baseline 被靜默忽略
    [InlineData(new[] { "{root}", "--baseline", "{root}/a.json", "--write-baseline", "{root}/b.json" }, "--baseline and --write-baseline cannot be used together")]
    public void UsageErrorsExitTwoWithAMessage(string[] args, string expectInStderr)
    {
        using var fx = CliProjects.Passing();
        var r = Cli.Run(args.Select(a => a.Replace("{root}", fx.Root)).ToArray());
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
        Assert.DoesNotContain("   at ", r.Err); // 好訊息，不是堆疊
    }

    [Fact] // console 是 workflow command 的通道：GitHub 的 runner 把以 :: 開頭的行當成指令（審查 1.0 最終驗證 S2，0.6.1 就有）。
           // 被稽核的 XAML 裡一個 &#10; 就能讓後面的內容落在行首 —— 鍵名、元素名、檔名、值一律把控制字元換成看得見的跳脫
    public void ScannedTextCannotStartAWorkflowCommandOnTheConsole()
    {
        using var fx = new Fixture();
        fx.File("Views/Main.xaml", """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <Style x:Key="Chip&#10;::error file=README.md,line=1::INJECTED annotation from scanned XAML" TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#777777"/><Setter Property="Background" Value="#888888"/>
                </Style>
              </UserControl.Resources>
              <Grid Background="#FFFFFF">
                <TextBlock Foreground="{DynamicResource Key&#13;&#10;::warning::INJECTED}" Text="unknown key with a line break"/>
                <TextBlock Foreground="#000000" Text="ok"/>
              </Grid>
            </UserControl>
            """);
        fx.File("baseline.json", """{ "schemaVersion": 1, "entries": [] }""");
        foreach (var args in new[]
        {
            new[] { fx.Root, "--show-unresolved", "--show-ok" },
            new[] { fx.Root, "--baseline", Path.Combine(fx.Root, "baseline.json") }, // NEW failure 那幾行
        })
        {
            var r = Cli.Run(args);
            Assert.DoesNotContain(r.Out.Split('\n'), l => l.TrimStart().StartsWith("::"));
            Assert.DoesNotContain(r.Err.Split('\n'), l => l.TrimStart().StartsWith("::"));
            Assert.Contains("\\n::error file=README.md", r.Out); // 看得見，只是不在行首
        }
        Assert.Contains("Key\\r\\n::warning::INJECTED", Cli.Run(fx.Root, "--show-unresolved").Out);

        // 使用者輸入（config 的屬性名）也一樣：錯誤訊息印在 stderr
        fx.File("xamlcontrast.config.json", "{ \"a\\n::error::INJECTED\": 1 }");
        var bad = Cli.Run(fx.Root);
        Assert.Equal(2, bad.Exit);
        Assert.DoesNotContain(bad.Err.Split('\n'), l => l.TrimStart().StartsWith("::"));
        Assert.Contains("a\\n::error::INJECTED", bad.Err);
    }

    [Fact] // `--` 結束旗標：之後的字一律是位置參數（審查 1.0 A3）。root 以 - 開頭時沒有別的寫法 ——
           // 舊版把 `-weird` 當成未知旗標（exit 2），把 `--version` 當成旗標（印版本、exit 0，什麼都沒稽核）
    public void DoubleDashEndsTheOptions()
    {
        using var parent = new Fixture();
        Directory.CreateDirectory(Path.Combine(parent.Root, "--version"));
        File.WriteAllText(Path.Combine(parent.Root, "--version", "Main.xaml"), """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="fail"/>
            </Grid>
            """);
        var withoutDashes = Cli.RunIn(parent.Root, "--version");
        Assert.True(withoutDashes.Exit == 0 && !withoutDashes.Out.Contains("pairs"), withoutDashes.ToString()); // 前提：那是旗標
        var r = Cli.RunIn(parent.Root, "--json", "r.json", "--", "--version");
        Assert.True(r.Exit == 1, r.ToString()); // 稽核了那個目錄：1 組 fail
        Assert.Contains("text-on-background pairs 1", r.Out);
        Assert.Equal(1, Cli.ReadJson(Path.Combine(parent.Root, "r.json")).GetProperty("summary").GetProperty("counts").GetProperty("fail").GetInt32());
    }

    [Theory] // `--` 之後只能有一個位置參數（root）；`--` 前面已經給了 root 就不能再給
    [InlineData(new[] { "--", "{root}", "extra" }, "unknown argument: extra")]
    [InlineData(new[] { "{root}", "--", "{root}" }, "unknown argument: ")]
    [InlineData(new[] { "--" }, "usage: xamlcontrast")]
    public void DoubleDashTakesExactlyOneRoot(string[] args, string expectInStderr)
    {
        using var fx = CliProjects.Passing();
        var r = Cli.Run(args.Select(a => a.Replace("{root}", fx.Root)).ToArray());
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
    }

    [Theory] // --json／--sarif／--md 的值必填，而且不能是下一個旗標：舊版只檢查「後面還有沒有字」，
             // `--json --strict-palette` 會寫出一個叫 --strict-palette 的檔、色盤退化保險靜默失效。
             // 以 - 開頭的檔名沒有正當用途（真的要就寫 ./-name）；「-」也不是 stdout，舊版會寫出一個叫 - 的檔
    [InlineData("--json", "--strict-palette")]
    [InlineData("--json", "--show-ok")]
    [InlineData("--sarif", "--md")]
    [InlineData("--md", "-")]
    public void OutputFlagsDoNotSwallowTheNextFlag(string flag, string next)
    {
        using var fx = CliProjects.Failing();
        var cwd = Path.Combine(fx.Root, "cwd");
        Directory.CreateDirectory(cwd);
        var r = Cli.RunIn(cwd, fx.Root, flag, next);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains($"{flag} takes a file path", r.Err);
        Assert.Contains("./", r.Err); // 指出真的要以 - 開頭的檔名該怎麼寫
        Assert.Empty(Directory.EnumerateFileSystemEntries(cwd)); // 沒有寫出名叫旗標的檔
    }

    [Theory] // NaN 對任何比較都是 false：`mc is < 0 or > 100` 擋不住，`覆蓋率 < NaN` 也永遠不成立 →
             // 覆蓋率下限靜默失效（25% 覆蓋率的專案印 all pairs meet AA、exit 0）。Infinity 原本就被擋
    [InlineData("NaN")]
    [InlineData("-NaN")]
    public void MinCoverageRejectsNaN(string value)
    {
        using var fx = CliProjects.QuarterCoverage();
        var r = Cli.Run(fx.Root, "--min-coverage", value);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("--min-coverage takes a number between 0 and 100", r.Err);
    }

    [Fact] // 沒給 root → usage + exit 2（不是 0：什麼都沒稽核）。用法錯誤的 usage 印到 stderr ——
           // 舊版印到 stdout，CI 抓 stdout 當報告的人拿到的是一段說明文字；--help 才印到 stdout
    public void MissingRootPrintsUsageToStderrAndExitsTwo()
    {
        var r = Cli.Run();
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("usage: xamlcontrast <project-root> [options]", r.Err);
        Assert.DoesNotContain("usage:", r.Out);
    }

    [Fact] // 路徑必填之後，`xamlcontrast --baseline src/MyApp`（把 root 寫在後面、漏了 baseline 路徑）仍然是把 root
           // 當成 baseline 檔、root 落空 → usage + exit 2。說出發生了什麼（0.x 時 stderr 沒有任何說明）
    public void BaselineFlagSwallowsTheFollowingPositional()
    {
        using var fx = CliProjects.Passing();
        var r = Cli.Run("--baseline", fx.Root);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("usage: xamlcontrast", r.Err);
        Assert.Contains($"--baseline took '{fx.Root}' as the baseline file", r.Err);
    }

    [Theory] // -h／--help 掃到就結束、exit 0；旗標由左到右一次掃過，錯誤旗標在前就先 exit 2
    [InlineData(new[] { "--help" }, 0)]
    [InlineData(new[] { "-h" }, 0)]
    [InlineData(new[] { "--help", "--bogus" }, 0)]
    [InlineData(new[] { "--bogus", "--help" }, 2)]
    public void HelpShortCircuitsLeftToRight(string[] args, int expectedExit)
    {
        var r = Cli.Run(args);
        Assert.True(r.Exit == expectedExit, r.ToString());
        if (expectedExit == 0)
        {
            Assert.Contains("usage: xamlcontrast <project-root> [options]", r.Out);
            // help 要列出每一個旗標 —— 凍結面（CLI 旗標）的對外說明
            foreach (var flag in new[] { "--json", "--sarif", "--md", "--html", "--fail-on", "--strict-palette",
                                         "--min-coverage", "--baseline", "--write-baseline", "--show-ok", "--show-unresolved",
                                         "--version", "-h, --help" })
                Assert.Contains(flag, r.Out);
            // --fail-on 吃 warn 與 fail 兩個值（warn 是 1.0 的預設）—— 舊版只寫 "--fail-on warn"，讓人以為 fail 不是合法值
            Assert.Contains("--fail-on <warn|fail>", r.Out);
        }
    }

    [Fact] // --version 印 InformationalVersion 去掉 +sha（SourceLink 會附上 commit）——
           // 原始碼建置是 0.0.0-dev，release.yml 以 -p:Version= 覆寫成 tag 版本，所以比對組件本身而不寫死
    public void VersionPrintsInformationalVersionWithoutBuildMetadata()
    {
        var r = Cli.Run("--version");
        Assert.True(r.Exit == 0, r.ToString());
        var printed = r.Out.Trim();
        var informational = FileVersionInfo.GetVersionInfo(Cli.DllPath).ProductVersion!;
        Assert.Equal(informational.Split('+')[0], printed);
        Assert.DoesNotContain("+", printed);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", printed);
    }

    [Theory] // 壞掉的 baseline 要有好訊息並指出下一步，不是裸拋堆疊
    [InlineData("""{ "nope": [] }""")]
    [InlineData("this is not json")]
    [InlineData("""{ "schemaVersion": 1, "entries": [ 42 ] }""")]
    public void InvalidBaselineFileExitsTwoAndSaysHowToRegenerate(string content)
    {
        using var fx = CliProjects.Failing();
        fx.File("baseline.json", content);
        var r = Cli.Run(fx.Root, "--baseline", Path.Combine(fx.Root, "baseline.json"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("baseline is not valid (", r.Err);
        Assert.Contains("regenerate it with --write-baseline", r.Err);
    }

    [Theory] // config 寫錯（含強制模式指到不存在的檔 —— 那是 Detect 擲的）→ exit 2 + 欄位級訊息
    [InlineData("""{ not json """, "config error: xamlcontrast.config.json is not valid JSON")]
    [InlineData("""{ "failOn": "never" }""", "config error: failOn must be fail|warn")]
    [InlineData("""{ "palette": { "mode": "single", "darkFile": "Nope.xaml" } }""", "config error: palette.darkFile not found: Nope.xaml")]
    [InlineData("""{ "palette": { "mode": "csharp", "csharpFile": "Nope.cs" } }""", "config error: palette.csharpFile not found: Nope.cs")]
    // 區段、陣列或陣列元素是 null：舊版是未處理的 NullReferenceException／ArgumentNullException，
    // 印 .NET 堆疊、退出碼跳出 0/1/2 契約（Windows −1073741819／−532462766）
    [InlineData("""{ "palette": null }""", "config error: palette must be an object")]
    [InlineData("""{ "thresholds": null }""", "config error: thresholds must be an object")]
    [InlineData("""{ "classification": null }""", "config error: classification must be an object")]
    [InlineData("""{ "ignore": null }""", "config error: ignore must be an object")]
    [InlineData("""{ "classification": { "extraText": null } }""", "config error: classification.extraText must be an array")]
    [InlineData("""{ "classification": { "extraNonText": [null] } }""", "config error: classification.extraNonText must not contain")]
    // 空字串的元素名：後綴比對時 EndsWith("") 恆真 —— extraNonText 裡一個 "" 會把所有不在內建表的元素
    // 都判成裝飾、不受文字門檻檢查（靜默放行），所以跟 null 一樣拒絕
    [InlineData("""{ "classification": { "extraNonText": [""] } }""", "config error: classification.extraNonText must not contain")]
    [InlineData("""{ "classification": { "extraText": ["  "] } }""", "config error: classification.extraText must not contain")]
    // 路徑含 NUL：GetFullPath 丟 ArgumentException，舊版只接 ConfigException
    [InlineData("""{ "palette": { "mode": "single", "darkFile": "a\u0000b.xaml" } }""", "config error: palette.darkFile is not a valid path")]
    public void InvalidConfigExitsTwo(string config, string expectInStderr)
    {
        using var fx = CliProjects.Passing();
        fx.File("xamlcontrast.config.json", config);
        fx.File("Fancy.xaml", """<Grid Background="#000000"><FancyLabel Foreground="#FFFFFF"/></Grid>"""); // 走後綴比對的元素名
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
        Assert.DoesNotContain("   at ", r.Err); // 好訊息，不是堆疊
    }

    // ── 讀不到的檔案：被 IDE／建置鎖住的 *.cs 或 config。舊版是未處理的 IOException，退出碼跳出契約
    //    （−532462766）。自動偵測會讀樹裡每一個 .cs 找 C# 色盤 —— 跟色盤無關的檔被鎖也會中。
    //    FileShare.None 只有 Windows 會跨行程強制；其他平台這兩條標成「略過」（看得出來沒驗），不是 return 算通過

    private const string FileShareNoneOnlyOnWindows =
        "FileShare.None is enforced across processes only on Windows — elsewhere the lock cannot be reproduced";

    [WindowsOnlyFact(FileShareNoneOnlyOnWindows)] // 讀不到的 .cs：跳過並列入 parse errors（喊出來、不靜默），稽核照常跑完
    public void UnreadableCSharpFileIsReportedNotFatal()
    {
        using var fx = CliProjects.Passing();
        fx.File("Services/Locked.cs", "class Locked {}");
        using (File.Open(Path.Combine(fx.Root, "Services", "Locked.cs"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var r = Cli.Run(fx.Root);
            Assert.True(r.Exit == 0, r.ToString());
            Assert.Contains("!! parse failed: Services/Locked.cs: ", r.Out);
        }
    }

    [WindowsOnlyFact(FileShareNoneOnlyOnWindows)] // 讀不到的 config：設定錯誤 → exit 2 並說人話
    public void UnreadableConfigExitsTwo()
    {
        using var fx = CliProjects.Passing();
        fx.File("xamlcontrast.config.json", "{}");
        using (File.Open(Path.Combine(fx.Root, "xamlcontrast.config.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var r = Cli.Run(fx.Root);
            Assert.True(r.Exit == 2, r.ToString());
            Assert.Contains("config error: cannot read xamlcontrast.config.json", r.Err);
            Assert.DoesNotContain("   at ", r.Err);
        }
    }

    [Theory] // rootBackground 解析不出顏色（不存在的鍵、空字串、Binding、畸形色碼）是設定錯誤 ——
             // 使用者以為地板鋪好了，實際上覆蓋率一點都沒回來（謊報健康的親戚）
    [InlineData("Nope")]
    [InlineData("")]
    [InlineData("{Binding WindowBg}")]
    [InlineData("#12")]
    public void UnresolvableRootBackgroundExitsTwo(string rootBackground)
    {
        using var fx = CliProjects.Passing().WithPalette();
        fx.File("xamlcontrast.config.json",
            System.Text.Json.JsonSerializer.Serialize(new { rootBackground }));
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains($"config error: rootBackground '{rootBackground}' is not a colour or a key in the detected palette (3 keys)", r.Err);
    }

    [Theory] // 對照組：鍵、色碼、具名色、markup 都是合法的地板 —— 不能被上面那道驗證誤擋
    [InlineData("Bg")]
    [InlineData("#202020")]
    [InlineData("Black")]
    [InlineData("{DynamicResource Bg}")]
    public void ResolvableRootBackgroundIsAccepted(string rootBackground)
    {
        using var fx = CliProjects.Passing().WithPalette();
        fx.File("xamlcontrast.config.json",
            System.Text.Json.JsonSerializer.Serialize(new { rootBackground }));
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.DoesNotContain("config error", r.Err);
    }
}

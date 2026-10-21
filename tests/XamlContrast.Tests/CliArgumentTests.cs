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
    public void UsageErrorsExitTwoWithAMessage(string[] args, string expectInStderr)
    {
        using var fx = CliProjects.Passing();
        var r = Cli.Run(args.Select(a => a.Replace("{root}", fx.Root)).ToArray());
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
        Assert.DoesNotContain("   at ", r.Err); // 好訊息，不是堆疊
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

    [Fact] // 沒給 root → usage + exit 2（不是 0：什麼都沒稽核）
    public void MissingRootPrintsUsageAndExitsTwo()
    {
        var r = Cli.Run();
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("usage: xamlcontrast <project-root> [options]", r.Out);
    }

    [Fact] // --baseline 的路徑可省略：下一個字不以 - 開頭就被當成 baseline 路徑 ——
           // 所以 `--baseline <root>` 會把 root 吃掉、root 落空 → usage + exit 2。
           // 這是現況（公開介面盤點 2.3/2.6 建議 1.0 前改），改了形狀這條要跟著改。
    public void BaselineFlagSwallowsTheFollowingPositional()
    {
        using var fx = CliProjects.Passing();
        var r = Cli.Run("--baseline", fx.Root);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("usage: xamlcontrast", r.Out);
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
            foreach (var flag in new[] { "--json", "--sarif", "--md", "--fail-on", "--strict-palette",
                                         "--min-coverage", "--baseline", "--write-baseline", "--show-ok", "--show-unresolved" })
                Assert.Contains(flag, r.Out);
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
    [InlineData("""{ "entries": [ 42 ] }""")]
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
    public void InvalidConfigExitsTwo(string config, string expectInStderr)
    {
        using var fx = CliProjects.Passing();
        fx.File("xamlcontrast.config.json", config);
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
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

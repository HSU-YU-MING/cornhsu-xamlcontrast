namespace XamlContrast.Tests;

/// <summary>
/// DecideExit 的判定（Program.cs）：0 組配對、色盤退化、覆蓋率下限、fail、fail-on warn，
/// 以及旗標與 config 的優先序。
///
/// ⚠ 判定順序是地雷（CLAUDE.md「退出碼契約與檢查順序」）：--strict-palette 與覆蓋率下限
///   曾擺在 --baseline／--write-baseline 分支之後，baseline 兩個分支都直接 return，
///   於是永遠走不到 —— 主題檔被搬走 → 配對全變 unresolved → ratchet 判成「已還債」→ 綠燈。
/// </summary>
public class CliExitDecisionTests
{
    [Fact] // 乾淨通過 → 0；fail → 1 並說出幾組
    public void PassAndFailMapToZeroAndOne()
    {
        using var ok = CliProjects.Passing();
        var r0 = Cli.Run(ok.Root);
        Assert.True(r0.Exit == 0, r0.ToString());
        Assert.Contains("exit 0: all pairs meet AA", r0.Out);

        using var bad = CliProjects.Failing();
        var r1 = Cli.Run(bad.Root);
        Assert.True(r1.Exit == 1, r1.ToString());
        Assert.Contains("exit 1: 1 pair(s) below threshold x 0.67 (0 warn)", r1.Out);
    }

    [Fact] // 結論行印實際生效的 failFactor：舊版寫死「x 2/3」—— 改了 thresholds.failFactor（0.9）之後 console 同時印
           // "fail-factor 0.9" 與 "below threshold x 2/3"；預設值本身也是 0.67 不是 2/3（3.00～3.01 會被判成 fail）
    public void ExitLineNamesTheFailFactorActuallyInEffect()
    {
        using var fx = CliProjects.Failing();
        fx.File("xamlcontrast.config.json", """{ "thresholds": { "failFactor": 0.9 } }""");
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("exit 1: 1 pair(s) below threshold x 0.9 (0 warn)", r.Out);
        Assert.DoesNotContain("2/3", r.Out);
        Assert.DoesNotContain("2/3", Cli.Run("--help").Out); // help 不寫死係數
    }

    [Fact] // 0 組配對、而且全是 no-ancestor-background（整個視窗沒有底）：最需要 rootBackground 提示的情況。
           // 舊版 0 配對那個分支先 return，走不到求救提示 —— 只印 "resolved 0 pairs"
    public void ZeroPairsCaseStillPrintsTheRootBackgroundHint()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window><TextBlock Foreground="#FFFFFF" Text="no ancestor background"/></Window>
            """);
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("exit 1: resolved 0 pairs", r.Out);
        Assert.Contains("hint: 1 of them are 'no-ancestor-background'", r.Out);
        Assert.Contains("rootBackground", r.Out);
    }

    [Fact] // 0 組配對不可設定 —— 空掃綠燈就是謊報健康
    public void ZeroPairsExitsOne()
    {
        using var fx = CliProjects.ZeroPairs();
        var r = Cli.Run(fx.Root, "--min-coverage", "0");
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("exit 1: resolved 0 pairs", r.Out);
    }

    [Theory] // warn 預設不擋；--fail-on warn 或 config failOn:"warn" 才擋。
             // 旗標只能加嚴：config 寫了 warn，--fail-on fail 放不寬（Program.cs 的 flag || config）
    [InlineData(null, new string[0], 0)]
    [InlineData(null, new[] { "--fail-on", "fail" }, 0)]
    [InlineData(null, new[] { "--fail-on", "warn" }, 1)]
    [InlineData("""{ "failOn": "warn" }""", new string[0], 1)]
    [InlineData("""{ "failOn": "warn" }""", new[] { "--fail-on", "fail" }, 1)]
    public void FailOnWarnFlagAndConfigCanOnlyTighten(string? config, string[] flags, int expectedExit)
    {
        using var fx = CliProjects.WarnOnly();
        if (config is not null) fx.File("xamlcontrast.config.json", config);
        var r = Cli.Run([fx.Root, .. flags]);
        Assert.True(r.Exit == expectedExit, r.ToString());
        Assert.Contains(expectedExit == 1
            ? "exit 1: 1 pair(s) below AA (--fail-on warn)"
            : "exit 0: no fail, but 1 pair(s) below AA", r.Out);
    }

    [Theory] // 色盤退化：預設只喊（!! palette），--strict-palette 或 config strictPalette 才擋；
             // config 明選 mode:none 是使用者的決定，不算退化
    [InlineData(null, false, 0)]
    [InlineData(null, true, 1)]
    [InlineData("""{ "strictPalette": true }""", false, 1)]
    [InlineData("""{ "palette": { "mode": "none" } }""", true, 0)]
    public void StrictPaletteFailsOnlyOnDegradedDetection(string? config, bool strictFlag, int expectedExit)
    {
        using var fx = CliProjects.Passing(); // 寫死色碼、沒有色盤
        if (config is not null) fx.File("xamlcontrast.config.json", config);
        var r = strictFlag ? Cli.Run(fx.Root, "--strict-palette") : Cli.Run(fx.Root);
        Assert.True(r.Exit == expectedExit, r.ToString());
        if (expectedExit == 1) Assert.Contains("exit 1: palette detection failed and --strict-palette is set", r.Out);
        else Assert.Contains("exit 0: all pairs meet AA", r.Out);
    }

    [Fact] // 覆蓋率下限預設 50：只看懂 25% 的「通過」不是通過
    public void CoverageBelowDefaultFloorExitsOne()
    {
        using var fx = CliProjects.QuarterCoverage();
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("exit 1: only 25.0% of colour pairs could be resolved (1 of 4), below the 50% floor", r.Out);
        Assert.Contains("pass --min-coverage 0 to proceed anyway", r.Out);
        // bound-or-gradient 是硬邊界，rootBackground 救不了 —— 不印求救指引
        Assert.DoesNotContain("hint:", r.Out);
    }

    [Theory] // --min-coverage 的邊界：恰等於下限算通過（< 而非 <=）；接受指數記法；0 = 關閉。
             // （程式用 InvariantCulture 解析，但這裡驗不到：zh-TW／en-US 下 CurrentCulture 解析 "25"、"2.5e1"
             //  的結果相同，而 Windows 上無法替子行程指定 culture）
    [InlineData("25", 0)]
    [InlineData("2.5e1", 0)]
    [InlineData("0", 0)]
    [InlineData("25.01", 1)]
    [InlineData("100", 1)]
    public void MinCoverageFlagBoundary(string value, int expectedExit)
    {
        using var fx = CliProjects.QuarterCoverage();
        var r = Cli.Run(fx.Root, "--min-coverage", value);
        Assert.True(r.Exit == expectedExit, r.ToString());
        if (expectedExit == 1) Assert.Contains("exit 1: only 25.0% of colour pairs could be resolved", r.Out);
    }

    [Theory] // config minCoverage 生效；旗標兩個方向都能覆寫 config（唯一能放寬 config 的項目）
    [InlineData("""{ "minCoverage": 20 }""", new string[0], 0)]
    [InlineData("""{ "minCoverage": 30 }""", new string[0], 1)]
    [InlineData("""{ "minCoverage": 100 }""", new[] { "--min-coverage", "0" }, 0)]
    [InlineData("""{ "minCoverage": 0 }""", new[] { "--min-coverage", "30" }, 1)]
    public void MinCoverageFlagOverridesConfigBothWays(string config, string[] flags, int expectedExit)
    {
        using var fx = CliProjects.QuarterCoverage();
        fx.File("xamlcontrast.config.json", config);
        var r = Cli.Run([fx.Root, .. flags]);
        Assert.True(r.Exit == expectedExit, r.ToString());
    }

    [Fact] // 求救指引：no-ancestor-background 過半時，exit 1 那一刻要指出 rootBackground 這個逃生口
    public void CoverageHintIsPrintedWhenRootBackgroundWouldHelp()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window>
              <Grid Background="#000000"><TextBlock Foreground="#FFFFFF" Text="resolved"/></Grid>
              <TextBlock Foreground="#FFFFFF" Text="no ancestor background 1"/>
              <TextBlock Foreground="#EEEEEE" Text="no ancestor background 2"/>
            </Window>
            """);
        var r = Cli.Run(fx.Root);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("exit 1: only 33.3% of colour pairs could be resolved (1 of 3)", r.Out);
        Assert.Contains("hint: 2 of them are 'no-ancestor-background'", r.Out);
        Assert.Contains("rootBackground", r.Out);
    }

    // ── 判定順序：「這份結果不可信」的三道保險必須擋在 baseline 兩個模式之前 ──
    //   每一組都準備一個「如果保險被跳過就會綠燈」的 baseline（空的 entries、或要寫出的新檔），
    //   所以 exit 1 只可能來自保險本身。

    [Theory]
    [InlineData("zero-pairs", "--baseline", "exit 1: resolved 0 pairs")]
    [InlineData("zero-pairs", "--write-baseline", "exit 1: resolved 0 pairs")]
    [InlineData("degraded-strict", "--baseline", "exit 1: palette detection failed and --strict-palette is set")]
    [InlineData("degraded-strict", "--write-baseline", "exit 1: palette detection failed and --strict-palette is set")]
    [InlineData("low-coverage", "--baseline", "exit 1: only 25.0% of colour pairs could be resolved")]
    [InlineData("low-coverage", "--write-baseline", "exit 1: only 25.0% of colour pairs could be resolved")]
    public void UntrustworthyResultGuardsRunBeforeBaselineModes(string shape, string mode, string expectInStdout)
    {
        using var fx = shape switch
        {
            "zero-pairs" => CliProjects.ZeroPairs(),
            "low-coverage" => CliProjects.QuarterCoverage(),
            _ => CliProjects.Passing(), // 寫死色碼 → 色盤退化
        };
        var baselineFile = Path.Combine(fx.Root, "baseline.json");
        string[] args = [fx.Root, mode, baselineFile];
        if (mode == "--baseline") fx.File("baseline.json", """{ "entries": [] }"""); // 沒有已知債 → 單看 ratchet 會過
        if (shape == "degraded-strict") args = [.. args, "--strict-palette"];

        var r = Cli.Run(args);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains(expectInStdout, r.Out);
        Assert.DoesNotContain("baseline:", r.Out); // 根本沒進 baseline 分支
        if (mode == "--write-baseline")
            Assert.False(File.Exists(baselineFile), "不可信的結果不能被凍成已知債");
    }

    [Fact] // CHANGELOG 0.6.0 修掉的失效鏈本身：主題檔被搬走 → 色票配對全變 unresolved →
           // 從 findings 消失 → ratchet 印「paid off 1」→ 綠燈。--strict-palette 必須在這裡擋下。
    public void MovingThemeFilesAwayCannotLookLikePayingOffDebt()
    {
        using var fx = new Fixture().WithPalette().File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="寫死色碼 1"/>
              <TextBlock Foreground="#FFFFFF" Text="寫死色碼 2"/>
              <Border Background="{DynamicResource Bg}">
                <TextBlock Foreground="{DynamicResource Dim}" Text="色票配對：兩主題都破"/>
              </Border>
            </Grid>
            """);
        var baselineFile = Path.Combine(fx.Root, "baseline.json");
        var write = Cli.Run(fx.Root, "--write-baseline", baselineFile);
        Assert.True(write.Exit == 0, write.ToString());
        Assert.Contains("(1 known failure pair(s))", write.Out);

        Directory.Delete(Path.Combine(fx.Root, "Themes"), recursive: true);

        // 前提：沒有 --strict-palette 時，這正是那條會綠燈的鏈（覆蓋率 2/3 過了下限）
        var lenient = Cli.Run(fx.Root, "--baseline", baselineFile);
        Assert.True(lenient.Exit == 0, lenient.ToString());
        Assert.Contains("paid off 1", lenient.Out);

        var strict = Cli.Run(fx.Root, "--baseline", baselineFile, "--strict-palette");
        Assert.True(strict.Exit == 1, strict.ToString());
        Assert.Contains("exit 1: palette detection failed and --strict-palette is set", strict.Out);
        Assert.DoesNotContain("paid off", strict.Out);
    }

    [Fact] // samples/demo 是刻意做壞的示範：exit 1 + 計數與 CI（ci.yml 的 jq）一致。
           // 本機 dotnet test 也守得到，不必等 push 上去被 CI 打回來
    public void DemoProjectFailsWithTheCountsCiExpects()
    {
        using var tmp = new Fixture();
        var json = Path.Combine(tmp.Root, "demo.json");
        var r = Cli.Run(Repo.PathOf("samples/demo"), "--json", json);
        Assert.True(r.Exit == 1, r.ToString());
        var summary = Cli.ReadJson(json).GetProperty("summary");
        Assert.Equal(3, summary.GetProperty("counts").GetProperty("fail").GetInt32());
        Assert.Equal(1, summary.GetProperty("disabledExempt").GetInt32());
        Assert.Equal(1, summary.GetProperty("suppressed").GetInt32());
    }
}

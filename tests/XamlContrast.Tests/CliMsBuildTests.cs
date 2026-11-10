namespace XamlContrast.Tests;

/// <summary>
/// <c>--msbuild</c>（1.4）：每一組低於 AA 的配對另外印成 MSBuild 標準診斷格式，Visual Studio 的錯誤清單吃得到。
/// 真的 <c>dotnet build</c> 實測（2026-11-10）：README 的 csproj 片段跑 samples/demo，MSBuild 記下 3 個 error XC0001，
/// 各帶完整路徑與行號，建置失敗 —— 這裡釘住格式與 error／warning 的分法。
/// </summary>
public class CliMsBuildTests
{
    private static string[] Diagnostics(Cli.Result r)
        => [.. r.Out.Split('\n').Where(l => l.Contains(": error XC") || l.Contains(": warning XC")).Select(l => l.TrimEnd('\r'))];

    [Fact]
    public void AFailIsAnErrorWithTheFullPathAndLine()
    {
        using var fx = CliProjects.Failing();

        var r = Cli.Run(fx.Root, "--msbuild");

        Assert.Equal(1, r.Exit);
        var line = Assert.Single(Diagnostics(r));
        Assert.StartsWith(Path.GetFullPath(Path.Combine(fx.Root, "Main.xaml")) + "(2,1): error XC0001: TextBlock: #EEEEEE on #FFFFFF is ", line);
        Assert.EndsWith("needs 4.5:1 (fail)", line);
    }

    [Theory] // 預設 gate（warn）擋 warn → error；--fail-on fail 不擋 → warning，退出碼 0
    [InlineData(new string[0], "error XC0001", 1)]
    [InlineData(new[] { "--fail-on", "fail" }, "warning XC0002", 0)]
    public void AWarnFollowsTheGate(string[] extra, string expected, int exit)
    {
        using var fx = CliProjects.WarnOnly();

        var r = Cli.Run([fx.Root, "--msbuild", .. extra]);

        Assert.Equal(exit, r.Exit);
        Assert.Contains(expected, Assert.Single(Diagnostics(r)));
    }

    [Fact] // baseline 模式：已知債是 warning，只有新增的是 error
    public void WithABaselineOnlyNewPairsAreErrors()
    {
        using var fx = CliProjects.Failing();
        var baseline = Path.Combine(fx.Root, "baseline.json");
        Assert.Equal(0, Cli.Run(fx.Root, "--write-baseline", baseline).Exit);
        fx.File("Other.xaml", """<Grid Background="#FFFFFF"><TextBlock Foreground="#F0F0F0" Text="new"/></Grid>""");

        var r = Cli.Run(fx.Root, "--baseline", baseline, "--msbuild");

        Assert.Equal(1, r.Exit);
        var lines = Diagnostics(r);
        Assert.Contains(lines, l => l.Contains("Main.xaml(2,1): warning XC0002:") && l.EndsWith("(fail, known debt in the baseline)"));
        Assert.Contains(lines, l => l.Contains("Other.xaml(1,1): error XC0001:"));
    }

    [Fact] // --write-baseline 是凍結現況：一律 warning，不讓建置失敗
    public void WritingABaselineOnlyWarns()
    {
        using var fx = CliProjects.Failing();

        var r = Cli.Run(fx.Root, "--write-baseline", Path.Combine(fx.Root, "b.json"), "--msbuild");

        Assert.Equal(0, r.Exit);
        Assert.Contains("warning XC0002", Assert.Single(Diagnostics(r)));
    }

    [Fact] // 結果不可信（exit 3）：不然建置會在「工具其實什麼都沒看到」時安靜地過
    public void AnUntrustworthyResultIsAnError()
    {
        using var fx = CliProjects.ZeroPairs();

        var r = Cli.Run(fx.Root, "--msbuild");

        Assert.Equal(3, r.Exit);
        Assert.Contains("XamlContrast : error XC0003: the audit result can't be trusted (exit 3)", r.Out);
    }

    [Fact]
    public void WithoutTheFlagNothingChanges()
    {
        using var fx = CliProjects.Failing();

        Assert.Empty(Diagnostics(Cli.Run(fx.Root)));
    }

    [Fact]
    public void PushDoesNotAudit()
    {
        var r = Cli.Run("--push", "report.json", "--msbuild");

        Assert.Equal(2, r.Exit);
        Assert.Contains("--msbuild", r.Err);
    }
}

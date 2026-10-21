namespace XamlContrast.Tests;

/// <summary>
/// CLI 的輸出面：--json／--sarif／--md／baseline 檔的寫出、寫檔失敗、--show-*，
/// 以及 baseline 模式在 console 上的結論行。
/// </summary>
public class CliOutputTests
{
    [Theory] // 輸出寫檔失敗必須落在退出碼契約內（CHANGELOG 0.6.0：之前裸 File.WriteAllText，
             // 目錄不存在就噴 .NET 堆疊、exit 127）。不代建目錄 —— 安靜生一個目錄比報錯更難查
    [InlineData("--json", "json")]
    [InlineData("--sarif", "sarif")]
    [InlineData("--md", "markdown")]
    [InlineData("--write-baseline", "baseline")]
    public void UnwritableOutputPathExitsTwoWithoutStackTrace(string flag, string label)
    {
        using var fx = CliProjects.Failing();
        var target = Path.Combine(fx.Root, "no-such-dir", "out.txt");
        var r = Cli.Run(fx.Root, flag, target);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains($"cannot write {label} to ", r.Err);
        Assert.DoesNotContain("   at ", r.Err);
        Assert.DoesNotContain("Unhandled exception", r.Err);
        Assert.False(Directory.Exists(Path.Combine(fx.Root, "no-such-dir")));
    }

    [Theory] // --md 在判定「之後」才寫（報告要標 PASS/FAIL）—— 寫不出去時，已決定的 0 或 1 都要被蓋成 2
    [InlineData(true)]
    [InlineData(false)]
    public void MarkdownWriteFailureOverridesTheDecidedExitCode(bool failing)
    {
        using var fx = failing ? CliProjects.Failing() : CliProjects.Passing();
        var r = Cli.Run(fx.Root, "--md", Path.Combine(fx.Root, "no-such-dir", "report.md"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(failing ? "exit 1: " : "exit 0: ", r.Out); // 判定本身照常印出
        Assert.Contains("cannot write markdown to ", r.Err);
    }

    [Fact] // --json／--sarif 在判定之前寫出：exit 1 的執行也拿得到報告（Action 的註記與上傳靠這個）
    public void JsonAndSarifAreWrittenEvenWhenTheRunFails()
    {
        using var fx = CliProjects.Failing();
        var json = Path.Combine(fx.Root, "report.json");
        var sarif = Path.Combine(fx.Root, "report.sarif");
        var r = Cli.Run(fx.Root, "--json", json, "--sarif", sarif);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Matches(@"json written: .*report\.json", r.Out);   // 路徑可能含非 ASCII，只比檔名
        Assert.Matches(@"sarif written: .*report\.sarif", r.Out);

        var report = Cli.ReadJson(json);
        Assert.Equal(5, report.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, report.GetProperty("summary").GetProperty("counts").GetProperty("fail").GetInt32());

        // SARIF 的 driver.version 就是 --version 印的那個（去掉 +sha）—— 兩處自稱的版本不能分家
        var version = Cli.Run("--version").Out.Trim();
        var run = Cli.ReadJson(sarif).GetProperty("runs")[0];
        Assert.Equal(version, run.GetProperty("tool").GetProperty("driver").GetProperty("version").GetString());
        Assert.Equal(1, run.GetProperty("results").GetArrayLength());
    }

    [Theory] // Markdown 的 PASS/FAIL 跟著「最終」退出碼走 —— --write-baseline 把現況凍成已知債、exit 0，報告就是 PASS
    [InlineData(new string[0], 1, "**FAIL**")]
    [InlineData(new[] { "--write-baseline", "{root}/baseline.json" }, 0, "**PASS**")]
    public void MarkdownVerdictFollowsTheFinalExitCode(string[] extra, int expectedExit, string verdict)
    {
        using var fx = CliProjects.Failing();
        var md = Path.Combine(fx.Root, "report.md");
        var r = Cli.Run([fx.Root, "--md", md, .. extra.Select(a => a.Replace("{root}", fx.Root))]);
        Assert.True(r.Exit == expectedExit, r.ToString());
        Assert.Contains(verdict, File.ReadAllText(md));
    }

    [Fact] // --show-unresolved 逐筆列出工具沒看到的地方；沒給旗標就不印；沒有 unresolved 時連標題都不印
    public void ShowUnresolvedListsBlindSpotsOnlyWhenAskedAndPresent()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="resolved"/>
              <Border Background="{Binding UserColour}"><TextBlock Foreground="#FFFFFF" Text="bound"/></Border>
            </Grid>
            """);
        var shown = Cli.Run(fx.Root, "--show-unresolved");
        Assert.True(shown.Exit == 0, shown.ToString()); // 50% 恰等於下限 → 過
        Assert.Contains("===== unresolved (1) =====", shown.Out);
        Assert.Contains("Main.xaml:3", shown.Out);
        Assert.Contains("bound-or-gradient", shown.Out);

        Assert.DoesNotContain("===== unresolved", Cli.Run(fx.Root).Out);

        using var clean = CliProjects.Passing();
        Assert.DoesNotContain("===== unresolved", Cli.Run(clean.Root, "--show-unresolved").Out);
    }

    [Fact] // --show-ok 由 CLI 傳到報告層（原型時代 -ShowOk 曾是 exit 後的死碼）
    public void ShowOkListsPassingPairs()
    {
        using var fx = CliProjects.Passing();
        Assert.Contains("===== ok (1), grouped by pair", Cli.Run(fx.Root, "--show-ok").Out);
        Assert.DoesNotContain("===== ok", Cli.Run(fx.Root).Out);
    }

    // ── baseline：預設路徑相對 CWD、兩個模式的優先序、ratchet 的結論行 ──

    [Fact] // 路徑可省略 → xamlcontrast-baseline.json，相對「執行時的工作目錄」而不是 root；
           // 下一個字以 - 開頭就不當路徑（--show-ok 不會被吃成檔名）
    public void BaselineDefaultPathIsRelativeToWorkingDirectory()
    {
        using var proj = CliProjects.Failing();
        using var cwd = new Fixture();

        var write = Cli.RunIn(cwd.Root, proj.Root, "--write-baseline", "--show-ok");
        Assert.True(write.Exit == 0, write.ToString());
        Assert.Contains("baseline written: xamlcontrast-baseline.json (1 known failure pair(s))", write.Out);
        Assert.Contains("===== ok (0)", write.Out);  // --show-ok 照樣生效，沒被當成路徑
        Assert.True(File.Exists(Path.Combine(cwd.Root, "xamlcontrast-baseline.json")));
        Assert.False(File.Exists(Path.Combine(proj.Root, "xamlcontrast-baseline.json")));

        var check = Cli.RunIn(cwd.Root, proj.Root, "--baseline");
        Assert.True(check.Exit == 0, check.ToString());
        Assert.Contains("baseline: known debt 1, paid off 0 (debt may only shrink)", check.Out);
        Assert.Contains("exit 0: no new or worsened failures vs baseline", check.Out);
    }

    [Fact] // 兩個模式並用時 --write-baseline 勝（先判）—— 指到不存在的 --baseline 也不會被讀
    public void WriteBaselineWinsOverBaseline()
    {
        using var fx = CliProjects.Failing();
        var outFile = Path.Combine(fx.Root, "new-baseline.json");
        var r = Cli.Run(fx.Root, "--baseline", Path.Combine(fx.Root, "missing.json"), "--write-baseline", outFile);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.True(File.Exists(outFile));
        Assert.DoesNotContain("baseline not found", r.Err);
    }

    [Fact] // ratchet 的三種結論：新增 → 逐處點名、惡化（同鍵處數變多）→ 點名、修好 → paid off 看得見
    public void BaselineRatchetReportsNewWorsenedAndPaidOff()
    {
        using var fx = CliProjects.Failing();
        var baseline = Path.Combine(fx.Root, "baseline.json");
        Assert.Equal(0, Cli.Run(fx.Root, "--write-baseline", baseline).Exit);

        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="fail"/>
              <TextBlock Foreground="#DDDDDD" Text="new fail"/>
            </Grid>
            """);
        var added = Cli.Run(fx.Root, "--baseline", baseline);
        Assert.True(added.Exit == 1, added.ToString());
        Assert.Contains("NEW failure: Main.xaml:3 TextBlock fg=#DDDDDD bg=#FFFFFF", added.Out);
        Assert.Contains("exit 1: 1 new / 0 worsened failure(s) vs baseline", added.Out);

        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="fail"/>
              <TextBlock Foreground="#EEEEEE" Text="same pair, second place"/>
            </Grid>
            """);
        var worsened = Cli.Run(fx.Root, "--baseline", baseline);
        Assert.True(worsened.Exit == 1, worsened.ToString());
        Assert.Contains("WORSENED: Main.xaml TextBlock fg=#EEEEEE bg=#FFFFFF (now 2 occurrence(s))", worsened.Out);
        Assert.Contains("exit 1: 0 new / 1 worsened failure(s) vs baseline", worsened.Out);

        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#111111" Text="fixed"/>
            </Grid>
            """);
        var paid = Cli.Run(fx.Root, "--baseline", baseline);
        Assert.True(paid.Exit == 0, paid.ToString());
        Assert.Contains("baseline: known debt 0, paid off 1 (debt may only shrink)", paid.Out);
    }

    [Fact] // warn 不進 ratchet：--baseline 通過之後，--fail-on warn 仍然對「所有」warn 生效（含既有的）
    public void FailOnWarnStillGatesInBaselineMode()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="fail (known debt)"/>
              <TextBlock Foreground="#8A8A8A" Text="warn"/>
            </Grid>
            """);
        var baseline = Path.Combine(fx.Root, "baseline.json");
        Assert.Equal(0, Cli.Run(fx.Root, "--write-baseline", baseline).Exit);

        var plain = Cli.Run(fx.Root, "--baseline", baseline);
        Assert.True(plain.Exit == 0, plain.ToString());

        var strict = Cli.Run(fx.Root, "--baseline", baseline, "--fail-on", "warn");
        Assert.True(strict.Exit == 1, strict.ToString());
        Assert.Contains("exit 1: 1 pair(s) below AA (--fail-on warn)", strict.Out);
    }
}

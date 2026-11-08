using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// baseline 的 unresolved 段與 --fail-on-new-unresolved（1.3）。
///
/// 要堵的洞：ratchet 只比 fail／warn 時，把一組已知的 fail 改成 <c>Foreground="{Binding …}"</c>，那一組會從 findings 消失、
/// 印成「paid off 1」，新的 <c>{Binding}</c> 也一聲不吭地通過 —— 只有整體覆蓋率掉到 50% 以下才會被 exit 3 攔下。
/// 工具照樣不猜（unresolved 永遠不評分），擋的是「看不到的地方變多了」。
///
/// 預設不擋（改預設要等 2.0），所以「沒打開就跟 1.2 一樣」本身也是要守的行為。
/// </summary>
public class UnresolvedBaselineTests
{
    private const string Ok = """<TextBlock Foreground="#FFFFFF" Text="21:1"/>""";
    private const string Bound = """<TextBlock Foreground="{Binding Fg}" Text="bound"/>""";

    /// <summary>黑底：通過的字加上 extra（覆蓋率要留在 50% 以上，不然先被 exit 3 攔下）。</summary>
    private static string OnBlack(params string[] children) => $"""
        <Grid Background="#000000">
          {Ok}
          {Ok}
          {Ok}
          {string.Join("\n  ", children)}
        </Grid>
        """;

    private static Fixture Project(params string[] children) => new Fixture().File("Main.xaml", OnBlack(children));

    /// <summary>用目前的專案內容寫 baseline 到 root/baseline.json，回傳路徑。</summary>
    private static string Freeze(Fixture fx)
    {
        var path = Path.Combine(fx.Root, "baseline.json");
        File.WriteAllText(path, Baseline.Write(fx.Run()));
        return path;
    }

    // ---------- 格式 ----------

    [Fact] // 寫出：每組 file|reason|value 一筆、count 是處數、沒有行號；ordinal 排序（跨 OS 逐字相同）
    public void WriteRecordsUnresolvedSitesAggregatedWithoutLineNumbers()
    {
        using var fx = new Fixture()
            .File("b.xaml", OnBlack(Bound, Bound, """<TextBlock Foreground="{Binding Other}"/>"""))
            .File("A.xaml", OnBlack(Bound));
        var root = JsonDocument.Parse(Baseline.Write(fx.Run())).RootElement;

        var entries = root.GetProperty("unresolved").EnumerateArray().ToList();
        Assert.All(entries, e => Assert.Equal(["file", "reason", "value", "count"], e.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(
            ["A.xaml|bound-or-gradient|{Binding Fg}|1", "b.xaml|bound-or-gradient|{Binding Fg}|2", "b.xaml|bound-or-gradient|{Binding Other}|1"],
            entries.Select(e => $"{e.GetProperty("file").GetString()}|{e.GetProperty("reason").GetString()}|" +
                                $"{e.GetProperty("value").GetString()}|{e.GetProperty("count").GetInt32()}"));
    }

    [Fact] // 1.3 之前寫的檔沒有這一段：照讀（fail／warn 的 ratchet 照舊），只是沒有東西可比 —— null，不是「原本一個都沒有」
    public void BaselineWithoutTheSectionIsReadAndNotCompared()
    {
        using var fx = Project(Bound);
        var known = Baseline.Parse("""{ "schemaVersion": 1, "keyVersion": 1, "entries": [] }""");
        Assert.Null(known.Unresolved);

        var cmp = Baseline.Compare(fx.Run(), known);
        Assert.Null(cmp.UnresolvedChanges);
        Assert.Empty(cmp.UnresolvedStatus);
        Assert.True(cmp.Passes);
    }

    [Theory] // 有這一段就跟 entries 一樣嚴：缺欄補 0 的話每一處都會變成「多出來的」，重複的鍵是手改或合併衝突的痕跡
    [InlineData("""{ }""", "is not an array")]
    [InlineData("""null""", "is not an array")]
    [InlineData("""[ 1 ]""", "unresolved entry 1 is not an object")]
    [InlineData("""[ { "file": "a.xaml", "reason": "bound-or-gradient", "value": "x" } ]""", "unresolved entry 1 has no 'count'")]
    [InlineData("""[ { "file": "a.xaml", "reason": "bound-or-gradient", "count": 1 } ]""", "unresolved entry 1 has no 'value'")]
    [InlineData("""[ { "file": "a.xaml", "reason": 3, "value": "x", "count": 1 } ]""", "'reason' 3 — it must be a string")]
    [InlineData("""[ { "file": "a.xaml", "reason": "r", "value": "x", "count": "2" } ]""", "it must be a whole number")]
    [InlineData("""[ { "file": "a.xaml", "reason": "r", "value": "x", "count": 1.5 } ]""", "it must be a whole number")]
    [InlineData("""[ { "file": "a.xaml", "reason": "r", "value": "x", "count": 0 } ]""", "'count' 0 — it must be at least 1")]
    [InlineData("""[ { "file": "a.xaml", "reason": "r", "value": "x", "count": 1 }, { "file": "a.xaml", "reason": "r", "value": "x", "count": 2 } ]""",
                "unresolved entries 1 and 2 have the same key")]
    public void InvalidSectionIsAnError(string section, string expect)
    {
        var ex = Assert.ThrowsAny<FormatException>(() =>
            Baseline.Parse($$"""{ "schemaVersion": 1, "keyVersion": 1, "entries": [], "unresolved": {{section}} }"""));
        Assert.Contains(expect, ex.Message);
    }

    // ---------- 比對 ----------

    [Fact] // 沒變 → 全部 known；換行號（上面插一行）也一樣 —— 鍵不含行號
    public void UnchangedOrMovedSitesAreKnown()
    {
        using var fx = Project(Bound);
        var baseline = Baseline.Write(fx.Run());
        fx.File("Main.xaml", "<!-- a new first line -->\n" + OnBlack(Bound));

        var current = fx.Run();
        var cmp = Baseline.Compare(current, baseline);
        Assert.Empty(cmp.UnresolvedChanges!);
        Assert.Equal(1, cmp.KnownUnresolved);
        Assert.Equal(0, cmp.GoneUnresolved);
        Assert.Equal("known", cmp.UnresolvedStatus[current.UnresolvedSites.Single()]);
    }

    [Fact] // 新的鍵 → new；同鍵處數變多 → 整組 worsened（分不出多的是哪一處）；少掉的算 resolved
    public void NewKeysAndHigherCountsAreChanges()
    {
        using var fx = Project(Bound, """<TextBlock Foreground="{Binding Gone}"/>""");
        var baseline = Baseline.Write(fx.Run());
        fx.File("Main.xaml", OnBlack(Bound, Bound, """<TextBlock Foreground="{Binding Brand}"/>"""));

        var current = fx.Run();
        var cmp = Baseline.Compare(current, baseline);
        var changes = cmp.UnresolvedChanges!;
        Assert.Equal(2, changes.Count);

        var grown = changes.Single(c => c.Value == "{Binding Fg}");
        Assert.Equal((1, 2, 1), (grown.Was, grown.Sites.Count, grown.Added));
        var added = changes.Single(c => c.Value == "{Binding Brand}");
        Assert.Equal((0, 1, 1), (added.Was, added.Sites.Count, added.Added));
        Assert.Equal("bound-or-gradient", added.Reason);

        Assert.Equal(0, cmp.KnownUnresolved);
        Assert.Equal(1, cmp.GoneUnresolved); // {Binding Gone} 不見了
        Assert.All(current.UnresolvedSites.Where(s => s.Value == "{Binding Fg}"), s => Assert.Equal("worsened", cmp.UnresolvedStatus[s]));
        Assert.Equal("new", cmp.UnresolvedStatus[current.UnresolvedSites.Single(s => s.Value == "{Binding Brand}")]);
        Assert.True(cmp.Passes); // fail／warn 的判定不受影響：擋不擋是 CLI 的選項
    }

    [Fact] // 鍵的每一段都算數：同一個值換了檔案也是新的
    public void TheFileIsPartOfTheKey()
    {
        using var fx = Project(Bound);
        var baseline = Baseline.Write(fx.Run());
        fx.File("Main.xaml", OnBlack()).File("Other.xaml", OnBlack(Bound));

        var cmp = Baseline.Compare(fx.Run(), baseline);
        Assert.Equal("Other.xaml", Assert.Single(cmp.UnresolvedChanges!).File);
        Assert.Equal(1, cmp.GoneUnresolved);
    }

    [Fact] // 要堵的那個洞：已知的 fail 改成 {Binding} —— fail 那邊算還債，unresolved 這邊多一處，兩邊的帳都要看得到
    public void AKnownFailureTurnedIntoABindingShowsUpAsNewUnresolved()
    {
        using var fx = Project("""<TextBlock Foreground="#111111" Text="fail"/>""");
        var baseline = Baseline.Write(fx.Run());
        fx.File("Main.xaml", OnBlack("""<TextBlock Foreground="{Binding Fg}" Text="fail"/>"""));

        var cmp = Baseline.Compare(fx.Run(), baseline);
        Assert.True(cmp.Passes);
        Assert.Equal(1, cmp.PaidDebt);
        Assert.Equal(1, Assert.Single(cmp.UnresolvedChanges!).Added);
    }

    // ---------- CLI ----------

    [Fact] // 沒打開：跟 1.2 一樣通過，但數字照報、說怎麼打開
    public void WithoutTheSwitchNewUnresolvedIsReportedButNotGated()
    {
        using var fx = Project();
        var baseline = Freeze(fx);
        fx.File("Main.xaml", OnBlack(Bound));

        var r = Cli.Run(fx.Root, "--baseline", baseline);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Contains("baseline: unresolved known 0, resolved 0, new 1 (not gated)", r.Out);
        Assert.Contains("pass --fail-on-new-unresolved", r.Out);
        Assert.Contains("exit 0: no new or worsened failures vs baseline", r.Out);
    }

    [Fact] // 打開：新的鍵逐處列出 file:line、處數變多的列出全部行號；exit 1
    public void TheFlagFailsOnNewAndGrownUnresolved()
    {
        using var fx = Project(Bound);
        var baseline = Freeze(fx);
        fx.File("Main.xaml", OnBlack(Bound, Bound, """<TextBlock Foreground="{Binding Brand}"/>"""));

        var r = Cli.Run(fx.Root, "--baseline", baseline, "--fail-on-new-unresolved");
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("baseline: unresolved known 0, resolved 0, new 2", r.Out);
        Assert.DoesNotContain("not gated", r.Out);
        Assert.Contains("  NEW unresolved: Main.xaml:7 bound-or-gradient {Binding Brand}", r.Out);
        Assert.Contains("  MORE unresolved: Main.xaml bound-or-gradient {Binding Fg} (now 2, baseline 1; line 5, 6)", r.Out);
        Assert.Contains("exit 1: 2 new unresolved pair(s) vs baseline", r.Out);
    }

    [Fact] // 打開而沒有多出來的：0，結論行說兩件事都看過了
    public void TheFlagPassesWhenNothingIsNew()
    {
        using var fx = Project(Bound, Bound);
        var baseline = Freeze(fx);
        fx.File("Main.xaml", OnBlack(Bound)); // 少一處：resolved 1

        var r = Cli.Run(fx.Root, "--baseline", baseline, "--fail-on-new-unresolved");
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Contains("baseline: unresolved known 1, resolved 1, new 0", r.Out);
        Assert.Contains("exit 0: no new or worsened failures and no new unresolved pairs vs baseline", r.Out);
    }

    [Fact] // 新的 fail 與新的 unresolved 同時出現：結論行兩個都講，fail 那半句維持 1.2 的字樣
    public void BothKindsAreNamedInTheExitLine()
    {
        using var fx = Project();
        var baseline = Freeze(fx);
        fx.File("Main.xaml", OnBlack(Bound, """<TextBlock Foreground="#111111" Text="fail"/>"""));

        var r = Cli.Run(fx.Root, "--baseline", baseline, "--fail-on-new-unresolved");
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("exit 1: 1 new / 0 worsened failure(s), 1 new unresolved pair(s) vs baseline", r.Out);
    }

    [Fact] // config 的 failOnNewUnresolved 跟旗標一樣（Action 的使用者走這條：不加 Action input，input 一加就凍結）
    public void TheConfigPropertyGatesToo()
    {
        using var fx = Project();
        var baseline = Freeze(fx);
        fx.File("Main.xaml", OnBlack(Bound)).File(ToolConfig.FileName, """{ "failOnNewUnresolved": true }""");

        var r = Cli.Run(fx.Root, "--baseline", baseline);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains("exit 1: 1 new unresolved pair(s) vs baseline", r.Out);
    }

    [Theory] // 旗標沒有 --baseline：沒有比較的對象，用法錯誤（--write-baseline 也一樣：它一律會記 unresolved，不需要這個旗標）
    [InlineData(false)]
    [InlineData(true)]
    public void TheFlagNeedsABaseline(bool writing)
    {
        using var fx = Project();
        var args = new List<string> { fx.Root, "--fail-on-new-unresolved" };
        if (writing) args.AddRange(["--write-baseline", Path.Combine(fx.Root, "b.json")]);

        var r = Cli.Run([.. args]);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("--fail-on-new-unresolved compares against a baseline: add --baseline <file>", r.Err);
        Assert.False(File.Exists(Path.Combine(fx.Root, "b.json")));
    }

    [Fact] // config 寫了、這次沒有 --baseline：照常稽核（同一份 config 也給 --write-baseline 與本機的一般稽核用），stderr 說一聲
    public void TheConfigPropertyWithoutABaselineIsANote()
    {
        using var fx = Project(Bound).File(ToolConfig.FileName, """{ "failOnNewUnresolved": true }""");

        var plain = Cli.Run(fx.Root);
        Assert.True(plain.Exit == 0, plain.ToString());
        Assert.Contains("only applies with --baseline", plain.Err);

        var writing = Cli.Run(fx.Root, "--write-baseline", Path.Combine(fx.Root, "b.json"));
        Assert.True(writing.Exit == 0, writing.ToString());
        Assert.DoesNotContain("only applies", writing.Err);
    }

    [Theory] // 打開了而 baseline 是 1.3 之前寫的：稽核之前就 exit 2 —— 當成「原本沒有」會整片紅，當成「不比」等於沒打開
    [InlineData(true, "--fail-on-new-unresolved has nothing to compare against")]
    [InlineData(false, "\"failOnNewUnresolved\" in xamlcontrast.config.json has nothing to compare against")]
    public void AnOldBaselineIsRejectedWhenTheGateIsOn(bool flag, string expect)
    {
        using var fx = Project(Bound);
        fx.File("baseline.json", """{ "schemaVersion": 1, "keyVersion": 1, "entries": [] }""");
        if (!flag) fx.File(ToolConfig.FileName, """{ "failOnNewUnresolved": true }""");
        var args = new List<string> { fx.Root, "--baseline", Path.Combine(fx.Root, "baseline.json") };
        if (flag) args.Add("--fail-on-new-unresolved");

        var r = Cli.Run([.. args]);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("baseline has no 'unresolved' section", r.Err);
        Assert.Contains(expect, r.Err);
        Assert.Contains("regenerate it with --write-baseline", r.Err);
        Assert.Equal("", r.Out); // 稽核之前：沒有任何報告輸出

        // 沒打開：照讀，跟 1.2 一樣
        if (!flag) File.Delete(Path.Combine(fx.Root, ToolConfig.FileName));
        var off = Cli.Run(fx.Root, "--baseline", Path.Combine(fx.Root, "baseline.json"));
        Assert.True(off.Exit == 0, off.ToString());
        Assert.DoesNotContain("baseline: unresolved", off.Out);
    }

    [Fact] // 「結果不可信」的三道保險照樣排在前面：配對全變 unresolved（覆蓋率掉到下限以下）是 3，不是「多了 N 個 unresolved」的 1
    public void UntrustworthyResultsStillExitThreeFirst()
    {
        using var fx = Project();
        var baseline = Freeze(fx);
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF"/>
              <TextBlock Foreground="{Binding A}"/>
              <TextBlock Foreground="{Binding B}"/>
              <TextBlock Foreground="{Binding C}"/>
            </Grid>
            """);

        var r = Cli.Run(fx.Root, "--baseline", baseline, "--fail-on-new-unresolved");
        Assert.True(r.Exit == 3, r.ToString());
        Assert.DoesNotContain("NEW unresolved", r.Out);
    }

    [Fact] // JSON：summary 記實際生效的設定；unresolvedSites[].baseline 是比對結果（不打開也記）；沒有 --baseline 時是 null
    public void JsonCarriesTheSettingAndThePerSiteVerdict()
    {
        using var fx = Project(Bound);
        var baseline = Freeze(fx);
        fx.File("Main.xaml", OnBlack(Bound, """<TextBlock Foreground="{Binding Brand}"/>"""));
        var json = Path.Combine(fx.Root, "report.json");

        var on = Cli.Run(fx.Root, "--baseline", baseline, "--fail-on-new-unresolved", "--json", json);
        Assert.True(on.Exit == 1, on.ToString());
        var report = Cli.ReadJson(json);
        Assert.True(report.GetProperty("summary").GetProperty("failOnNewUnresolved").GetBoolean());
        Assert.Equal(1, report.GetProperty("exitCode").GetInt32());
        Assert.Equal(["{Binding Brand}=new", "{Binding Fg}=known"],
            report.GetProperty("unresolvedSites").EnumerateArray()
                .Select(s => $"{s.GetProperty("value").GetString()}={s.GetProperty("baseline").GetString()}").Order(StringComparer.Ordinal));

        var off = Cli.Run(fx.Root, "--baseline", baseline, "--json", json);
        Assert.True(off.Exit == 0, off.ToString());
        report = Cli.ReadJson(json);
        Assert.False(report.GetProperty("summary").GetProperty("failOnNewUnresolved").GetBoolean());
        Assert.Contains(report.GetProperty("unresolvedSites").EnumerateArray(), s => s.GetProperty("baseline").GetString() == "new");

        var none = Cli.Run(fx.Root, "--json", json);
        Assert.True(none.Exit == 0, none.ToString());
        Assert.All(Cli.ReadJson(json).GetProperty("unresolvedSites").EnumerateArray(),
            s => Assert.Equal(JsonValueKind.Null, s.GetProperty("baseline").ValueKind));
    }

    [Fact] // --write-baseline 寫出的檔帶 unresolved 段，之後打開旗標就能直接用，不必重寫
    public void WriteBaselineFromTheCliRecordsTheSection()
    {
        using var fx = Project(Bound);
        var path = Path.Combine(fx.Root, "b.json");
        var w = Cli.Run(fx.Root, "--write-baseline", path);
        Assert.True(w.Exit == 0, w.ToString());
        Assert.Equal(1, Assert.Single(Cli.ReadJson(path).GetProperty("unresolved").EnumerateArray()).GetProperty("count").GetInt32());

        var r = Cli.Run(fx.Root, "--baseline", path, "--fail-on-new-unresolved");
        Assert.True(r.Exit == 0, r.ToString());
    }
}

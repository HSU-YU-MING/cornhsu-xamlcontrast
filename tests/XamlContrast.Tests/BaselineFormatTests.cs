using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// baseline 檔的格式與比對邊界。這個檔被 commit 進使用者的 repo、由 CI 讀 ——
/// 形狀變了，使用者的 CI 第二天就紅（或更糟：靜默放行）。
/// 鍵 = file|element|fg|bg，刻意不含行號；Count 與 Worst 少任何一欄都有一整類惡化靜默放行
/// （CLAUDE.md「baseline 的鍵」）。行號漂移、處數變多、色盤漂移三條在 IgnoreAndBaselineTests。
/// </summary>
public class BaselineFormatTests
{
    private static AuditResult Run(params (string File, string Xaml)[] files)
    {
        using var fx = new Fixture();
        foreach (var (f, x) in files) fx.File(f, x);
        return fx.Run();
    }

    private static string OnWhite(params string[] children) => $"""
        <Grid Background="#FFFFFF">
          {string.Join("\n  ", children)}
        </Grid>
        """;

    private const string FailText = """<TextBlock Foreground="#EEEEEE" Text="fail"/>""";

    /// <summary>手寫一份 baseline（數值經 JsonSerializer，不吃 locale）。</summary>
    private static string BaselineJson(params object[] entries) => JsonSerializer.Serialize(new { entries });

    [Fact] // 寫出格式：根只有 comment + entries；欄位 camelCase；只收 fail；同鍵聚合（count／worst=最差）；
           // 依 file 再依 element 以 ordinal 排序 —— 跨 OS 寫出的檔逐字相同，diff 才看得懂
    public void WriteEmitsOnlyFailuresAggregatedAndSortedOrdinally()
    {
        var r = Run(
            ("a.xaml", OnWhite(
                """<TextBlock Foreground="#EEEEEE" Text="fail 1"/>""",
                """<TextBlock Foreground="#EEEEEE" Text="fail 1 again, same key"/>""",
                """<TextBlock Foreground="#8A8A8A" Text="warn — not debt"/>""",
                """<TextBlock Foreground="#111111" Text="ok"/>""",
                """<Label Foreground="#DDDDDD" Content="fail 2"/>""")),
            ("B.xaml", OnWhite(FailText)));
        var root = JsonDocument.Parse(Baseline.Write(r)).RootElement;

        Assert.Equal(["comment", "entries"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("XamlContrast baseline — known debt; may only shrink", root.GetProperty("comment").GetString());

        var entries = root.GetProperty("entries").EnumerateArray().ToList();
        Assert.All(entries, e => Assert.Equal(
            ["file", "element", "fg", "bg", "count", "worst"], e.EnumerateObject().Select(p => p.Name)));

        // ordinal：大寫 B 排在小寫 a 前面（掃描順序是不分大小寫的 a → B，排序不是照抄掃描順序）；
        // 同檔內 Label 排在 TextBlock 前面（文件順序是 TextBlock 先）
        Assert.Equal(
            ["B.xaml|TextBlock|#EEEEEE", "a.xaml|Label|#DDDDDD", "a.xaml|TextBlock|#EEEEEE"],
            entries.Select(e => $"{e.GetProperty("file").GetString()}|{e.GetProperty("element").GetString()}|{e.GetProperty("fg").GetString()}"));

        var aggregated = entries.Single(e => e.GetProperty("file").GetString() == "a.xaml" &&
                                             e.GetProperty("element").GetString() == "TextBlock");
        Assert.Equal(2, aggregated.GetProperty("count").GetInt32());
        var worst = r.Findings.Where(f => f.File == "a.xaml" && f.Fg == "#EEEEEE").Min(f => f.Worst);
        Assert.Equal(worst, aggregated.GetProperty("worst").GetDouble());
        Assert.Equal("#FFFFFF", aggregated.GetProperty("bg").GetString());
    }

    [Theory] // 鍵的每一段都算數：同一組顏色換了檔案、換了元素、換了底，都是新的破 ——
             // 鍵少任何一段，別處的新問題就會被當成已知債放行
    [InlineData("Other.xaml", "TextBlock", "#FFFFFF")]
    [InlineData("Main.xaml", "Label", "#FFFFFF")]
    [InlineData("Main.xaml", "TextBlock", "#F5F5F5")]
    public void EveryKeyComponentDistinguishesFailures(string file, string element, string bg)
    {
        var baseline = Baseline.Write(Run(("Main.xaml", OnWhite(FailText))));
        var current = Run((file, $"""
            <Grid Background="{bg}">
              <{element} Foreground="#EEEEEE"/>
            </Grid>
            """));
        var cmp = Baseline.Compare(current, baseline);
        Assert.False(cmp.Passes);
        Assert.Single(cmp.NewFailures);
        Assert.Equal(1, cmp.PaidDebt); // 原本那一筆不見了 —— 算還債，但不抵銷新的
    }

    [Fact] // 缺 entries → 例外（CLI 轉成 exit 2「baseline is not valid」）—— 不是當成「沒有債」
    public void MissingEntriesIsAnError()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, """{ "comment": "no entries" }"""));
        Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, """{ "entries": null }"""));
    }

    [Fact] // 欄位名不分大小寫：手寫或其他工具產出的 PascalCase 檔照樣讀得進
    public void EntryFieldsAreCaseInsensitive()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        var cmp = Baseline.Compare(r, JsonSerializer.Serialize(new
        {
            entries = new[] { new { File = f.File, Element = f.Element, Fg = f.Fg, Bg = f.Bg, Count = 1, Worst = f.Worst } },
        }));
        Assert.True(cmp.Passes);
        Assert.Equal(1, cmp.KnownDebt);
    }

    [Fact] // Worst 的容差是 0.01：四捨五入的雜訊不算惡化，超過才算
    public void WorstToleranceIsOneHundredth()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        object Entry(double worst) => new { file = f.File, element = f.Element, fg = f.Fg, bg = f.Bg, count = 1, worst };

        Assert.True(Baseline.Compare(r, BaselineJson(Entry(f.Worst + 0.009))).Passes);
        var drifted = Baseline.Compare(r, BaselineJson(Entry(f.Worst + 0.011)));
        Assert.False(drifted.Passes);
        Assert.Equal(f.Worst, Assert.Single(drifted.WorsenedFailures).Worst); // 回報的是現在的比值
    }

    [Fact] // 同鍵處數下降（3 → 1）：還在債內、照過；paid off 只算整個鍵消失的處數，部分償還不計
    public void PartialPaydownPassesButIsNotCountedAsPaid()
    {
        var baseline = Baseline.Write(Run(("Main.xaml", OnWhite(FailText, FailText, FailText))));
        var cmp = Baseline.Compare(Run(("Main.xaml", OnWhite(FailText))), baseline);
        Assert.True(cmp.Passes);
        Assert.Equal(1, cmp.KnownDebt);
        Assert.Equal(0, cmp.PaidDebt);
    }

    // ── 缺欄時的現況（寬鬆：補預設值 0）。公開介面盤點 9.3 建議 1.0 前改成報錯 ——
    //    改的時候這兩條要跟著翻成「期望例外」，那是刻意的格式收緊，不是回歸 ──

    [Fact] // 缺 worst → 0：色盤漂移檢查靜默失效（比值再差也不會 < 0 − 0.01）
    public void MissingWorstCurrentlyDisablesTheDriftCheck()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        var cmp = Baseline.Compare(r, BaselineJson(new { file = f.File, element = f.Element, fg = f.Fg, bg = f.Bg, count = 1 }));
        Assert.True(cmp.Passes);
    }

    [Fact] // 缺 count → 0：每一處都算惡化（吵，但方向是安全的）
    public void MissingCountCurrentlyFlagsEveryOccurrenceAsWorsened()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        var cmp = Baseline.Compare(r, BaselineJson(new { file = f.File, element = f.Element, fg = f.Fg, bg = f.Bg, worst = f.Worst }));
        Assert.False(cmp.Passes);
        Assert.Equal(1, Assert.Single(cmp.WorsenedFailures).Count);
    }
}

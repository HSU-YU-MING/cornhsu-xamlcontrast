using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>M4 導入模式（規劃書 4.6）：ignore 註解與 baseline ratchet。</summary>
public class IgnoreAndBaselineTests
{
    private const string LowContrastText = """<TextBlock Foreground="#EEEEEE" Text="浮水印"/>""";

    [Fact] // ignore 管下一個元素（含子樹），且壓掉要計數 —— 靜默退化等於謊報
    public void IgnoreSuppressesNextElementAndCounts()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore: 浮水印文字，刻意低對比 -->
              {LowContrastText}
              <TextBlock Foreground="#DDDDDD" Text="這個沒被 ignore，要報"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Suppressed);                    // 壓掉的有計數
        var f = Assert.Single(r.Findings);                // 只壓「下一個元素」，不是整個檔
        Assert.Equal("#DDDDDD", f.Fg);
        Assert.Equal(Category.Fail, f.Category);
        Assert.Contains("suppressed 1 pair(s)", Report.ToConsole(r));
        Assert.Contains("\"suppressed\": 1", Report.ToJson(r));
    }

    [Fact] // 理由必填 —— 沒理由的 ignore 無效、不壓、要警告
    public void IgnoreWithoutReasonIsInvalidAndDoesNotSuppress()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore -->
              {LowContrastText}
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Suppressed);
        Assert.Single(r.Findings);                        // 照報
        Assert.Single(r.InvalidIgnores);
        Assert.Contains("without a reason", Report.ToConsole(r));
    }

    [Fact] // ignore 也能壓 Style 配對
    public void IgnoreSuppressesStylePair()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <!-- xamlcontrast-ignore: 停用態樣式，刻意低對比 -->
                <Style x:Key="Disabled" TargetType="Button">
                  <Setter Property="Background" Value="#303030"/>
                  <Setter Property="Foreground" Value="#404040"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Suppressed);
        Assert.Empty(r.Findings);
    }

    // ── baseline ratchet：承認既有債、只擋新增與惡化 ──

    private static AuditResult RunWith(params string[] extraTextBlocks)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              {string.Join("\n  ", extraTextBlocks)}
            </Grid>
            """);
        return fx.Run();
    }

    [Fact]
    public void KnownDebtPassesNewFailureBlocks()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="既有債"/>""");
        var baseline = Baseline.Write(before);

        // 同一筆債 → 過
        var same = RunWith("""<TextBlock Foreground="#EEEEEE" Text="既有債"/>""");
        var cmp1 = Baseline.Compare(same, baseline);
        Assert.True(cmp1.Passes);
        Assert.Equal(1, cmp1.KnownDebt);

        // 新增一筆不同的破 → 擋
        var worse = RunWith(
            """<TextBlock Foreground="#EEEEEE" Text="既有債"/>""",
            """<TextBlock Foreground="#DDDDDD" Text="新的破"/>""");
        var cmp2 = Baseline.Compare(worse, baseline);
        Assert.False(cmp2.Passes);
        var nf = Assert.Single(cmp2.NewFailures);
        Assert.Equal("#DDDDDD", nf.Fg);
    }

    [Fact] // 同鍵的處數變多也是惡化（行號不進鍵 —— 行號會漂移）
    public void SameKeyMoreOccurrencesIsWorsening()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="a"/>""");
        var baseline = Baseline.Write(before);

        var doubled = RunWith(
            """<TextBlock Foreground="#EEEEEE" Text="a"/>""",
            """<TextBlock Foreground="#EEEEEE" Text="b"/>""");
        var cmp = Baseline.Compare(doubled, baseline);
        Assert.False(cmp.Passes);
        var w = Assert.Single(cmp.WorsenedFailures);
        Assert.Equal(2, w.Count);
    }

    [Fact] // 債只准減：修掉之後 paid off 要看得見
    public void PaidDebtIsVisible()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="待修"/>""");
        var baseline = Baseline.Write(before);

        var fixedUp = RunWith("""<TextBlock Foreground="#111111" Text="修好了"/>""");
        var cmp = Baseline.Compare(fixedUp, baseline);
        Assert.True(cmp.Passes);
        Assert.Equal(0, cmp.KnownDebt);
        Assert.Equal(1, cmp.PaidDebt);
    }

    [Fact] // paid off 的單位要跟 known debt 一致（處數，不是鍵數）——
           // 同鍵 3 處一次修完只顯示「還了 1」會低報進度
    public void PaidDebtCountsOccurrencesNotKeys()
    {
        var before = RunWith(
            """<TextBlock Foreground="#EEEEEE" Text="a"/>""",
            """<TextBlock Foreground="#EEEEEE" Text="b"/>""",
            """<TextBlock Foreground="#EEEEEE" Text="c"/>""");
        var baseline = Baseline.Write(before);
        // 同一組字底配對 3 處 —— 鍵只有一個，債是 3
        Assert.Equal(3, Baseline.Compare(before, baseline).KnownDebt);

        var fixedUp = RunWith("""<TextBlock Foreground="#111111" Text="全修好了"/>""");
        var cmp = Baseline.Compare(fixedUp, baseline);
        Assert.True(cmp.Passes);
        Assert.Equal(0, cmp.KnownDebt);
        Assert.Equal(3, cmp.PaidDebt); // 舊版數鍵 → 1，跟 KnownDebt 的 3 對不上
    }

    [Fact] // 色盤漂移：鍵不變（{DynamicResource Fg}）但色盤值調暗 → 比值惡化要擋
    public void PaletteDriftWorseningIsCaught()
    {
        static AuditResult RunWithPalette(string fgColor)
        {
            using var fx = new Fixture();
            fx.File("Styles/DarkTheme.xaml", $"""
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
                  <SolidColorBrush x:Key="Fg" Color="{fgColor}"/>
                  <SolidColorBrush x:Key="Other" Color="#123456"/>
                </ResourceDictionary>
                """);
            fx.File("Main.xaml", """
                <Grid Background="{DynamicResource Bg}">
                  <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
                </Grid>
                """);
            return fx.Run();
        }

        var baseline = Baseline.Write(RunWithPalette("#DDDDDD")); // 1.35:1，破，入債
        var drifted = RunWithPalette("#EEEEEE");                  // 更淡 → 1.18:1，鍵沒變
        var cmp = Baseline.Compare(drifted, baseline);
        Assert.False(cmp.Passes); // 鍵相同、處數相同 —— 但比值惡化不能靜默放行
        Assert.Single(cmp.WorsenedFailures);
    }

    [Fact] // 行號漂移不觸發假性新增：同鍵不同行仍是已知債
    public void LineDriftDoesNotCreateFalseNewFailures()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="x"/>""");
        var baseline = Baseline.Write(before);

        // 前面插了別的元素，行號整個位移
        var shifted = RunWith(
            """<Border Background="#FFFFFF"><TextBlock Foreground="#111111" Text="新的、過的"/></Border>""",
            """<TextBlock Foreground="#EEEEEE" Text="x"/>""");
        var cmp = Baseline.Compare(shifted, baseline);
        Assert.True(cmp.Passes);
    }
}

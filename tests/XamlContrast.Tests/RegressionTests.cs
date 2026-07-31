using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 八個「謊報健康」形狀的回歸測試（規劃書 8.1 四個＋ 8.2 四個）。
/// 這個工具的歷史說明它最容易壞在報告層，不是解析層 ——
/// 每一個形狀都曾經讓真實問題消失在報告裡。
/// </summary>
public class RegressionTests
{
    // 8.1 #1：「兩主題都低」曾被判成『刻意低對比』藏起來（Kindling 52 組）。
    // 對稱不是意圖的證據 —— 兩邊一樣糟就只是兩邊都糟，一定要報。
    [Fact]
    public void SymmetricallyLowPairIsStillReported()
    {
        using var fx = new Fixture();
        fx.File("Themes/DarkTheme.xaml", """
            <ResourceDictionary>
              <SolidColorBrush x:Key="Bg" Color="#303030" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
              <SolidColorBrush x:Key="Fg" Color="#606060" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
              <SolidColorBrush x:Key="Other" Color="#111111" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/LightTheme.xaml", """
            <ResourceDictionary>
              <SolidColorBrush x:Key="Bg" Color="#D0D0D0" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
              <SolidColorBrush x:Key="Fg" Color="#A0A0A0" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
              <SolidColorBrush x:Key="Other" Color="#EEEEEE" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="兩邊都低"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.True(f.RatioDark < 4.5 && f.RatioLight < 4.5);
        Assert.NotEqual(Category.Ok, f.Category);       // 不准藏
        Assert.Equal(Symmetry.BothLow, f.Symmetry);      // 對稱是獨立維度，不是放行理由
    }

    // 8.1 #2：單一主題深淺同值 → 差距恆為 0 → 曾全部被吃掉（CelFlow 96 組）
    [Fact]
    public void SingleThemeProjectStillReportsLowContrast()
    {
        using var fx = new Fixture();
        fx.File("Styles/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="BgColor">#303030</Color>
              <Color x:Key="FgColor">#606060</Color>
              <Color x:Key="OtherColor">#111111</Color>
              <SolidColorBrush x:Key="Bg" Color="{StaticResource BgColor}"/>
              <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
              <SolidColorBrush x:Key="Other" Color="{StaticResource OtherColor}"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="單一主題"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(PaletteMode.Single, r.Detection.Mode);
        var f = Assert.Single(r.Findings);
        Assert.Equal(Category.Fail, f.Category);         // gap=0 不是放行理由
        Assert.Equal(Symmetry.SingleTheme, f.Symmetry);
    }

    // 8.1 #3：ProgressBar 的 Foreground 是進度條填色不是文字，
    // 用「有沒有 Foreground」判斷會拿 4.5 去要求一條進度條
    [Fact]
    public void ProgressBarForegroundIsDecorative()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#202020">
              <ProgressBar Foreground="#303030"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal(Category.Decorative, f.Category);
        Assert.False(f.IsText);
        Assert.Equal(0.0, f.Need);
    }

    // 8.1 #4：PS 5.1 的 .Count 單一結果失準 →「破 1 組」曾回報「全部達標」。
    // C# 沒有這個坑，但守門員數字本身要有測試釘住。
    [Fact]
    public void SingleFailureIsCountedAsOne()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="白底淺灰字"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.CountOf(Category.Fail));       // 守門員靜默放行比沒有守門員更糟
        Assert.Contains("===== fail (1) =====", Report.ToConsole(r));
    }

    // 8.2 #5：Style 配對曾因缺 Need 欄位全部被靜默歸為裝飾、不受檢
    [Fact]
    public void StylePairIsGradedNotDecorative()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="LowContrast" TargetType="Button">
                  <Setter Property="Background" Value="#303030"/>
                  <Setter Property="Foreground" Value="#404040"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal(Category.Fail, f.Category);         // 不是 Decorative —— 那是 bug #5 的形狀
        Assert.Equal(4.5, f.Need);
    }

    // 8.2 #6：「低於 AA」曾把裝飾算進去（CelFlow 報 80 = 破1+偏低1+裝飾78）
    [Fact]
    public void BelowAaCountExcludesDecorative()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="真的破"/>
              <Rectangle Fill="#F0F0F0"/>
              <Rectangle Fill="#F5F5F5"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(2, r.CountOf(Category.Decorative));
        Assert.Contains("below AA: 1,", Report.ToConsole(r)); // 1 不是 3 —— 裝飾不適用 AA
    }

    // 8.2 #7：標題計數曾印成「（ 組）」；#8：-ShowOk 曾是 exit 後的死碼
    [Fact]
    public void ShowOkSectionActuallyRenders()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="通過"/>
            </Grid>
            """);
        var r = fx.Run();
        var console = Report.ToConsole(r, showOk: true);
        Assert.Contains("===== ok (1),", console);       // 計數印得出來、區塊跑得到
        Assert.DoesNotContain("===== ok", Report.ToConsole(r, showOk: false));
    }

    // 移植自查抓到的：XAML 解析失敗不能讓檔案靜默消失（原型會警告，移植時一度漏掉）
    [Fact]
    public void ParseFailureIsReportedNotSwallowed()
    {
        using var fx = new Fixture();
        fx.File("Broken.xaml", "<Grid><未閉合");
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="ok"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Single(r.ParseErrors);
        Assert.Contains("Broken.xaml", r.ParseErrors[0]);
        Assert.Contains("!! parse failed: Broken.xaml", Report.ToConsole(r));
        Assert.Contains("\"parseErrors\": 1", Report.ToJson(r));
    }

    // SARIF：只輸出 fail/warn（ok/decorative 灌進 code scanning 是噪音），
    // level 對應 error/warning，路徑是 root 相對正斜線
    [Fact]
    public void SarifCarriesOnlyFailAndWarnWithLevels()
    {
        using var fx = new Fixture();
        fx.File("Views/Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="fail"/>
              <TextBlock Foreground="#8A8A8A" Text="warn ~3.4"/>
              <TextBlock Foreground="#111111" Text="ok"/>
              <Rectangle Fill="#F5F5F5"/>
            </Grid>
            """);
        var sarif = Report.ToSarif(fx.Run(), "9.9.9");
        Assert.Contains("\"version\": \"2.1.0\"", sarif);
        Assert.Contains("\"level\": \"error\"", sarif);
        Assert.Contains("\"level\": \"warning\"", sarif);
        Assert.Contains("\"uri\": \"Views/Main.xaml\"", sarif);
        // ok 與裝飾不進 SARIF —— 數 results 的個數
        var doc = System.Text.Json.JsonDocument.Parse(sarif);
        Assert.Equal(2, doc.RootElement.GetProperty("runs")[0].GetProperty("results").GetArrayLength());
    }

    // 4.5 輸出合約：退化要機器可讀 —— summary 區塊要有 paletteSource / unresolved / skipped
    [Fact]
    public void JsonSummaryCarriesDegradationCounters()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="{Binding X}">
              <TextBlock Foreground="#FFFFFF" Text="無法解析"/>
            </Grid>
            """);
        var r = fx.Run();
        var json = Report.ToJson(r);
        Assert.Contains("\"paletteSource\": \"none\"", json);
        Assert.Contains("\"unresolved\": 1", json);
        Assert.Contains("\"pairs\": 0", json);
    }
}

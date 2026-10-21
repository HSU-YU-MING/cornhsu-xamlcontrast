using System.Globalization;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// CLAUDE.md「地雷（改了會安靜壞掉的地方）」逐條的回歸測試。已有測試在守的條目只列出來，
/// 這裡補的是「同一個地雷的其他計算路徑」—— 改一處忘了另一處正是這幾條的歷史。
///
///   FontSize 是 DIP ×0.75（三處）  樹走訪：ParseRuleTests.LargeTextThresholdUsesDipConversion；
///                                  Style 鏈逐狀態、WalkStyles：這裡
///   數值一律 InvariantCulture        這裡（逗號小數點的 culture 下跑三條路徑）
///   BoldWeight 錨定整字比對          樹走訪 SemiBold：NewRuleTests.SemiBoldIsNotBoldForLargeTextExemption；
///                                  全部字重與另外兩條路徑：這裡
///   色票只收長度 7／9               brush 字面值：PaletteDetectorTests.MalformedHexIsNotAcceptedIntoPalette；
///                                  &lt;Color&gt; 元素、C# 三元組：這裡；強制 single／csharp：ConfigTests
///   GetFullPath 正規化               PaletteDetectorTests.MultiAppRepo…（正斜線 root）；共用區退路：這裡
///   dark/light 提示只看相對路徑      合併式色盤那條路：這裡（PaletteDetectorTests 那條走不到合併路徑的差異）
///   三段式合併（brush 表與 Color 表） 字面值 brush：PaletteDetectorTests.NeutralFileSortingFirst…；
///                                  Color 表：PaletteDetectorTests.BrushesInOneFile…；引用型 brush 表：這裡
///   分類後綴比對取最長               單邊命中：NewRuleTests.DerivedControlNamesClassifyBySuffix；兩邊都中：這裡
///   baseline 鍵與 Count／Worst       IgnoreAndBaselineTests、BaselineFormatTests
///   判定順序、寫檔失敗的退出碼       CliExitDecisionTests、CliOutputTests
/// </summary>
public class LandmineTests
{
    // ── FontSize 是 DIP（1/96 吋），換 point 要 ×0.75：18px = 13.5pt 不是大字級，24px = 18pt 是 ──

    [Fact] // Style 鏈逐狀態路徑：FontSize 來自元素屬性或 Style 的 Setter 都要換算
    public void FontSizeIsConvertedFromDipOnTheStyleChainPath()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Card" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
                <Style x:Key="BigCard" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="FontSize" Value="24"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Card}" Foreground="#FFFFFF" FontSize="18" Content="18px"/>
              <Button Style="{StaticResource Card}" Foreground="#FFFFFF" FontSize="24" Content="24px"/>
              <Button Style="{StaticResource BigCard}" Foreground="#FFFFFF" Content="24px from the Style"/>
            </Grid>
            """);
        var r = fx.Run();
        var small = Assert.Single(r.Findings, f => f.Element == "Button⊕Style[Card]/base" && f.Size == "18px");
        Assert.False(small.Large);
        Assert.Equal(4.5, small.Need);
        var large = Assert.Single(r.Findings, f => f.Element == "Button⊕Style[Card]/base" && f.Size == "24px");
        Assert.True(large.Large);
        Assert.Equal(3.0, large.Need);
        var fromStyle = Assert.Single(r.Findings, f => f.Element == "Button⊕Style[BigCard]/base");
        Assert.True(fromStyle.Large);
        Assert.Equal("24px", fromStyle.Size);
    }

    [Fact] // WalkStyles（Style 定義處）：Setter 的 FontSize／FontWeight 一樣換算、一樣錨定
    public void FontSizeAndWeightAreHandledInStyleDefinitions()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Px18" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="FontSize" Value="18"/>
                </Style>
                <Style x:Key="Px24" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="FontSize" Value="24"/>
                </Style>
                <Style x:Key="SemiBold19" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="FontSize" Value="19"/>
                  <Setter Property="FontWeight" Value="SemiBold"/>
                </Style>
                <Style x:Key="Bold19" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="FontSize" Value="19"/>
                  <Setter Property="FontWeight" Value="Bold"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Finding Of(string key) => Assert.Single(r.Findings, f => f.Element == $"Style[{key}]/base");
        Assert.Equal(4.5, Of("Px18").Need);        // 13.5pt
        Assert.Equal(3.0, Of("Px24").Need);        // 18pt
        Assert.Equal(4.5, Of("SemiBold19").Need);  // 14.25pt、600 —— 不是粗體
        Assert.Equal(3.0, Of("Bold19").Need);      // 14.25pt、700 —— 粗體 ≥ 14pt
    }

    [Fact] // Style 鏈路徑的字重：元素屬性優先、其次 Style 的 Setter；SemiBold 一樣不算
    public void BoldWeightOnTheStyleChainPath()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Card" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
                <Style x:Key="BoldCard" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="FontWeight" Value="Bold"/>
                </Style>
                <Style x:Key="SemiCard" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="FontWeight" Value="SemiBold"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Card}" Foreground="#FFFFFF" FontSize="19" FontWeight="Bold" Content="element"/>
              <Button Style="{StaticResource BoldCard}" Foreground="#FFFFFF" FontSize="19" Content="style"/>
              <Button Style="{StaticResource SemiCard}" Foreground="#FFFFFF" FontSize="19" Content="semibold"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.True(Assert.Single(r.Findings, f => f.Element.Contains("Style[Card]")).Large);
        Assert.True(Assert.Single(r.Findings, f => f.Element.Contains("Style[BoldCard]")).Large);
        Assert.False(Assert.Single(r.Findings, f => f.Element.Contains("Style[SemiCard]")).Large);
    }

    [Theory] // 粗體 = weight ≥ 700 的整字比對：子字串或未錨定的比對會把 SemiBold／Bolder 也放寬到 3:1
    [InlineData("Bold", true)]
    [InlineData("ExtraBold", true)]
    [InlineData("UltraBold", true)]
    [InlineData("Black", true)]
    [InlineData("Heavy", true)]
    [InlineData("ExtraBlack", true)]
    [InlineData("UltraBlack", true)]
    [InlineData("700", true)]
    [InlineData("950", true)]
    [InlineData(" Bold ", true)]
    [InlineData("SemiBold", false)]
    [InlineData("DemiBold", false)]
    [InlineData("Medium", false)]
    [InlineData("Normal", false)]
    [InlineData("600", false)]
    [InlineData("Bolder", false)]
    public void BoldWeightIsAnAnchoredWholeWordMatch(string weight, bool isBold)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#000000">
              <TextBlock FontSize="19" FontWeight="{weight}" Foreground="#FFFFFF" Text="14.25pt"/>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal(isBold, f.Large);
        Assert.Equal(isBold ? 3.0 : 4.5, f.Need);
    }

    // ── 所有數值屬性一律 InvariantCulture：德語系 locale 的 CI 上 Opacity="0.5" 不能靜默解析失敗 ──

    [Fact]
    public void NumericAttributesParseInvariantlyUnderACommaDecimalCulture()
    {
        // 自製一個逗號小數點的 culture（不依賴機器上有沒有 de-DE 的 ICU 資料）
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";

        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#000000">
              <Grid.Resources>
                <Style x:Key="Card" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
                <Style x:Key="Defined" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="FontSize" Value="24.0"/>
                </Style>
              </Grid.Resources>
              <TextBlock Opacity="0.5" Foreground="#FFFFFF" Text="half opacity"/>
              <TextBlock FontSize="18.67" FontWeight="Bold" Foreground="#FFFFFF" Text="14pt bold"/>
              <Button Style="{StaticResource Card}" Foreground="#FFFFFF" FontSize="24.0" Content="chain"/>
            </Grid>
            """);

        var prev = CultureInfo.CurrentCulture;
        AuditResult r;
        try
        {
            CultureInfo.CurrentCulture = comma;
            // 前提：在這個 culture 下 "0.5" 確實解析不出來 —— 否則這條測不到任何東西
            Assert.False(double.TryParse("0.5", NumberStyles.Float, CultureInfo.CurrentCulture, out _));
            r = fx.Run();
        }
        finally { CultureInfo.CurrentCulture = prev; }

        var faded = Assert.Single(r.Findings, f => f.Element == "TextBlock" && f.Size == "");
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#FFFFFF", 0.5, "#000000"), "#000000"), faded.RatioDark); // 樹走訪 Opacity
        Assert.True(Assert.Single(r.Findings, f => f.Size == "19px").Large);                               // 樹走訪 FontSize
        Assert.True(Assert.Single(r.Findings, f => f.Element == "Style[Defined]/base").Large);              // WalkStyles
        Assert.True(Assert.Single(r.Findings, f => f.Element == "Button⊕Style[Card]/base").Large);          // Style 鏈
    }

    // ── 色票值只收長度 7（#RRGGBB）或 9（#AARRGGBB）：七位數的打字錯誤不能被默默猜前半段 ──

    [Fact] // <Color> 元素那條解析路徑（ParseColorDefs）
    public void MalformedHexInColorElementsIsNotAccepted()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Bg">#FFFFFF</Color>
              <Color x:Key="Fg">#222222</Color>
              <Color x:Key="Accent">#2196F3</Color>
              <Color x:Key="Typo">#FF0000A</Color>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.True(d.Palette.Entries.ContainsKey("Fg"));
        Assert.False(d.Palette.Entries.ContainsKey("Typo"));
    }

    [Fact] // C# 三元組自動偵測那條路徑（CsCandidates）：正則是 {6,8}，七位數要另外擋
    public void MalformedHexInCSharpTuplesIsNotAccepted()
    {
        using var fx = new Fixture();
        fx.File("Services/ThemeService.cs", """
            ("Bg", "#111111", "#EAEAEA"),
            ("Fg", "#EEEEEE", "#1B1B1B"),
            ("Accent", "#42A5F5", "#1D6FC4"),
            ("Typo", "#FF0000A", "#FFFFFF"),
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.CSharp, d.Mode);
        Assert.Equal(3, d.Palette.Count);
        Assert.False(d.Palette.Entries.ContainsKey("Typo"));
    }

    // ── 檔名的 dark／light 提示只看「專案內相對路徑」──

    [Theory] // 合併式色盤（自動偵測的第一條路）：專案放在 …\xc-darkapp-…\ 底下時，用完整路徑判斷
             // 會讓中性檔染上 dark 提示、搶走深色側 —— 深淺兩欄變成同一套值
    [InlineData("xc-darkapp-", "Colors.xaml", "ColorsDark.xaml", false)]   // 中性檔 = 淺色值（HandyControl 形狀）
    [InlineData("xc-lightapp-", "ColorsLight.xaml", "Colors.xaml", true)]  // 中性檔 = 深色值
    public void ThemeHintIgnoresWhereTheProjectLives(string dirPrefix, string lightFile, string darkFile, bool neutralIsDark)
    {
        using var fx = new Fixture(dirPrefix + Guid.NewGuid().ToString("N"));
        fx.File($"Themes/Colors/{lightFile}", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="BackgroundColor">#FAFAFA</Color>
              <Color x:Key="TextColor">#212121</Color>
              <Color x:Key="AccentColor">#2196F3</Color>
            </ResourceDictionary>
            """);
        fx.File($"Themes/Colors/{darkFile}", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="BackgroundColor">#111111</Color>
              <Color x:Key="TextColor">#EEEEEE</Color>
              <Color x:Key="AccentColor">#42A5F5</Color>
            </ResourceDictionary>
            """);
        fx.File("Themes/Brushes.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="BackgroundBrush" Color="{DynamicResource BackgroundColor}"/>
              <SolidColorBrush x:Key="TextBrush" Color="{DynamicResource TextColor}"/>
              <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentColor}"/>
            </ResourceDictionary>
            """);
        Assert.Contains(neutralIsDark ? "light" : "dark", fx.Root); // 前提：專案所在路徑真的帶著提示字樣
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.Equal(("#111111", "#FAFAFA"), d.Palette.Entries["BackgroundBrush"]);
        Assert.Equal(("#EEEEEE", "#212121"), d.Palette.Entries["TextBrush"]);
    }

    // ── 三段式合併：主題檔先進（各佔自己那側），中性檔只補洞 ──

    [Fact] // 引用型 brush（Color="{StaticResource X}"）那張表：字典序排在前面的中性檔
           // 不能搶走主題檔對同一個 brush 鍵的引用（字面值 brush 那張表的同型回歸見 PaletteDetectorTests）
    public void NeutralBrushReferenceDoesNotStealThemedReference()
    {
        using var fx = new Fixture();
        fx.File("Extra/Shared.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="SubAppText">#123456</Color>
              <SolidColorBrush x:Key="Text" Color="{StaticResource SubAppText}"/>
              <SolidColorBrush x:Key="Sub.One" Color="#654321"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="DarkText">#EEEEEE</Color>
              <SolidColorBrush x:Key="Text" Color="{StaticResource DarkText}"/>
              <SolidColorBrush x:Key="Surface" Color="#111111"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="LightText">#212121</Color>
              <SolidColorBrush x:Key="Text" Color="{StaticResource LightText}"/>
              <SolidColorBrush x:Key="Surface" Color="#FAFAFA"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(("#EEEEEE", "#212121"), d.Palette.Entries["Text"]);   // 不是子 App 的 #123456
        Assert.Equal(("#654321", "#654321"), d.Palette.Entries["Sub.One"]); // 中性檔補洞仍有效
        Assert.Contains("conflicting values", d.Description);               // 同鍵不同值要明講
        Assert.Contains("Text", d.Description);
    }

    // ── 元素分類：後綴比對，兩邊都中時取最長的後綴（名字愈長愈具體）──

    [Theory]
    [InlineData("""{ "classification": { "extraNonText": ["ShapeLabel"] } }""", "IconShapeLabel", Category.Decorative)] // ShapeLabel(10) > Label(5)
    [InlineData("""{ "classification": { "extraText": ["DataLine"] } }""", "ChartDataLine", Category.Fail)]           // DataLine(8) > Line(4)
    public void LongestSuffixWinsWhenTextAndNonTextBothMatch(string config, string element, Category expected)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <{element} Foreground="#EEEEEE"/>
            </Grid>
            """);
        fx.File(ToolConfig.FileName, config);
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(expected, Assert.Single(r.Findings).Category);
    }

    // ── 多 App repo 的作用域：不屬於任何 App 目錄的檔案用「共用區」色盤，看不到 App 自己的鍵 ──

    [Fact]
    public void FileOutsideEveryAppUsesTheSharedPalette()
    {
        using var fx = new Fixture();
        fx.File("MainApp/App.xaml", """<Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="A.App"/>""");
        fx.File("MainApp/Theme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        fx.File("Tools/SubApp/App.xaml", """<Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="B.App"/>""");
        fx.File("Tools/SubApp/Theme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Fg" Color="#212121"/>
              <SolidColorBrush x:Key="Accent" Color="#2196F3"/>
            </ResourceDictionary>
            """);
        fx.File("Shared/Lib.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Lib.Bg" Color="#000000"/>
              <SolidColorBrush x:Key="Lib.Fg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Lib.Accent" Color="#FF9800"/>
            </ResourceDictionary>
            """);
        fx.File("Shared/Control.xaml", """
            <UserControl Background="{DynamicResource Lib.Bg}">
              <TextBlock Foreground="{DynamicResource Lib.Fg}" Text="shared key"/>
              <TextBlock Foreground="{DynamicResource Fg}" Text="an app's key — not visible from the shared area"/>
            </UserControl>
            """);
        var fwd = fx.Root.Replace(Path.DirectorySeparatorChar, '/'); // CLI 實際這樣傳（見 GetFullPath 那條地雷）
        var r = Auditor.Run(fwd, PaletteDetector.Detect(fwd));
        var shared = Assert.Single(r.Findings, f => f.File == "Shared/Control.xaml");
        Assert.Equal(21.0, shared.RatioDark);
        var site = Assert.Single(r.UnresolvedSites);
        Assert.Equal(UnresolvedReason.UnknownPaletteKey, site.Reason);
        Assert.Equal("Fg", site.Value);
    }
}

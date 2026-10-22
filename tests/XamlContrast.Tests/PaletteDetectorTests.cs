using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>四種偵測形狀（QuillNest／Kindling／CelFlow／Cornea）＋路徑提示的回歸。</summary>
public class PaletteDetectorTests
{
    private const string DarkDict = """
        <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Color x:Key="BgColor">#111111</Color>
          <Color x:Key="FgColor">#EEEEEE</Color>
          <Color x:Key="AccentColor">#42A5F5</Color>
          <SolidColorBrush x:Key="Bg" Color="{StaticResource BgColor}"/>
          <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
          <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
        </ResourceDictionary>
        """;

    private const string LightDict = """
        <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Color x:Key="BgColor">#FAFAFA</Color>
          <Color x:Key="FgColor">#212121</Color>
          <Color x:Key="AccentColor">#2196F3</Color>
          <SolidColorBrush x:Key="Bg" Color="{StaticResource BgColor}"/>
          <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
          <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
        </ResourceDictionary>
        """;

    [Fact] // QuillNest 形狀：Dark+Light 兩檔配對
    public void PairShape()
    {
        using var fx = new Fixture();
        fx.File("Resources/DarkTheme.xaml", DarkDict)
          .File("Resources/LightTheme.xaml", LightDict);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.Equal(3, d.Palette.Count);
        Assert.Equal(("#111111", "#FAFAFA"), d.Palette.Entries["Bg"]);
        Assert.False(d.Palette.IsSingleTheme);
        Assert.Equal(2, d.ExcludedFiles.Count); // 色盤定義檔不是畫面
    }

    [Fact] // Kindling 形狀：單一 Dark XAML（只有深色值）＋ C# 三元組 → C# 勝出
    public void CSharpSourceOfTruthBeatsSingleThemeXaml()
    {
        using var fx = new Fixture();
        // XAML 靜態複本只有深色值 —— 天真地選它，淺色值就全是錯的
        fx.File("Themes/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        fx.File("Services/ThemeService.cs", """
            static readonly (string, string, string)[] Palette =
            {
                ("Bg", "#111111", "#EAEAEA"),
                ("Fg", "#EEEEEE", "#1B1B1B"),
                ("Accent", "#42A5F5", "#1D6FC4"),
            };
            """);
        var d = PaletteDetector.Detect(fx.Root);
        // 提供兩套主題的來源優先於只有一套的
        Assert.Equal(PaletteMode.CSharp, d.Mode);
        Assert.Equal(("#111111", "#EAEAEA"), d.Palette.Entries["Bg"]);
        // 鍵集高度重疊的 XAML 靜態複本一併排除出 UI 掃描
        Assert.Contains(d.ExcludedFiles, f => f.EndsWith("DarkTheme.xaml"));
    }

    [Fact] // CelFlow 形狀：單一 Dark XAML，無配對無 C# → 單一主題
    public void SingleThemeShape()
    {
        using var fx = new Fixture();
        fx.File("Styles/DarkTheme.xaml", DarkDict);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Single, d.Mode);
        Assert.True(d.Palette.IsSingleTheme);
        Assert.Equal(("#111111", "#111111"), d.Palette.Entries["Bg"]); // 深淺同值
    }

    [Fact] // 應用程式層作用域:repo 裡有兩個 App、鍵名相同值不同(ScreenToGif Translator /
           // Playnite Desktop+Fullscreen 形狀)。單一全域色盤會把 A App 的字配上 B App 的底。
    public void MultiAppRepoResolvesEachAppAgainstItsOwnPalette()
    {
        using var fx = new Fixture();
        fx.File("MainApp/App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="A.App"/>
            """);
        fx.File("MainApp/Theme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        fx.File("MainApp/Main.xaml", """
            <Window Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="深色 App"/>
            </Window>
            """);
        fx.File("Tools/SubApp/App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="B.App"/>
            """);
        // 同鍵不同值 —— 淺色的第二個 App
        fx.File("Tools/SubApp/Theme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Fg" Color="#212121"/>
              <SolidColorBrush x:Key="Accent" Color="#2196F3"/>
            </ResourceDictionary>
            """);
        fx.File("Tools/SubApp/Main.xaml", """
            <Window Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="淺色 App"/>
            </Window>
            """);
        // ⚠ 刻意用正斜線 root —— CLI 實際就這樣傳。GetDirectoryName 會把作用域目錄
        //   正規化成反斜線,混用時 StartsWith 永遠不成立,作用域曾因此靜默退化成全域
        var fwd = fx.Root.Replace(Path.DirectorySeparatorChar, '/');
        var r = Auditor.Run(fwd, PaletteDetector.Detect(fwd));
        Assert.Equal(2, r.Findings.Count);
        var dark = Assert.Single(r.Findings, f => f.File.StartsWith("MainApp"));
        var light = Assert.Single(r.Findings, f => f.File.StartsWith("Tools"));
        // 各用各的值:#EEEEEE 疊 #111111 與 #212121 疊 #FAFAFA 都是高對比 ——
        // 混用作用域的話其中一筆會變成淺字疊淺底的假 fail
        Assert.True(dark.RatioDark > 10, $"MainApp ratio={dark.RatioDark}");
        Assert.True(light.RatioDark > 10, $"SubApp ratio={light.RatioDark}");
    }

    [Fact] // HandyControl 形狀：brush 名稱與主題無關、顏色值分主題，兩者在不同檔案，
           // 而且用 DynamicResource 跨檔引用。舊版（只挑一個檔＋只認同檔 StaticResource）完全找不到色盤。
    public void BrushesInOneFileReferencingColoursInThemeFilesAreMerged()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="PrimaryTextColor">#212121</Color>
              <Color x:Key="BackgroundColor">#FAFAFA</Color>
              <Color x:Key="AccentColor">#2196F3</Color>
            </ResourceDictionary>
            """);
        fx.File("Themes/Colors/ColorsDark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="PrimaryTextColor">#EEEEEE</Color>
              <Color x:Key="BackgroundColor">#111111</Color>
              <Color x:Key="AccentColor">#42A5F5</Color>
            </ResourceDictionary>
            """);
        // ⚠ o:Freeze 排在 x:Key 前面 —— 舊正則假設 x:Key 緊接標籤名，整份會漏掉
        fx.File("Themes/Theme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush o:Freeze="True" x:Key="PrimaryTextBrush" Color="{DynamicResource PrimaryTextColor}"/>
              <SolidColorBrush o:Freeze="True" x:Key="BackgroundBrush" Color="{DynamicResource BackgroundColor}"/>
              <SolidColorBrush o:Freeze="True" x:Key="AccentBrush" Color="{DynamicResource AccentColor}"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.False(d.Palette.IsSingleTheme);
        // 深色來自 ColorsDark.xaml，淺色來自沒有 light 字樣的預設 Colors.xaml
        Assert.Equal(("#111111", "#FAFAFA"), d.Palette.Entries["BackgroundBrush"]);
        Assert.Equal(("#EEEEEE", "#212121"), d.Palette.Entries["PrimaryTextBrush"]);
        Assert.Equal(3, d.ExcludedFiles.Count); // 三個字典都不是畫面
    }

    [Fact] // ScreenToGif 形狀：Dark/Light 兩檔各自「字面值」brush(不經 Color 引用)。
           // 合併式色盤第一版把字面值一律當深淺同值收,Dark 檔字典序在前 → 深色值
           // 佔據兩欄、淺色檔整份被忽略,456 筆 findings 全部 dark==light —— 淺色欄是編造的。
    public void LiteralBrushThemePairKeepsPerThemeValues()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Panel.Background" Color="#FF202020"/>
              <SolidColorBrush x:Key="Element.Foreground" Color="#FFEEEEEE"/>
              <SolidColorBrush x:Key="Element.Accent" Color="#FF42A5F5"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Colors/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Panel.Background" Color="#FFFFFFFF"/>
              <SolidColorBrush x:Key="Element.Foreground" Color="#FF212121"/>
              <SolidColorBrush x:Key="Element.Accent" Color="#FF2196F3"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.False(d.Palette.IsSingleTheme);
        Assert.Equal(("#FF202020", "#FFFFFFFF"), d.Palette.Entries["Panel.Background"]);
        Assert.Equal(("#FFEEEEEE", "#FF212121"), d.Palette.Entries["Element.Foreground"]);
    }

    [Fact] // Translator 形狀:repo 裡有第二個 App,自帶同鍵不同值的中性色盤檔,
           // 且路徑字典序排在主題檔前面。中性檔只能補洞、不能搶主題檔的值;衝突要明講。
    public void NeutralFileSortingFirstDoesNotStealThemedValues()
    {
        using var fx = new Fixture();
        // "Extra" < "Themes" 字典序 —— 第一版照檔案順序 TryAdd 會讓它搶走兩側
        fx.File("Extra/SubApp/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Panel.Background" Color="#FFFFFFFF"/>
              <SolidColorBrush x:Key="SubApp.Only" Color="#FF123456"/>
              <SolidColorBrush x:Key="SubApp.Two" Color="#FF654321"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Panel.Background" Color="#FF202020"/>
              <SolidColorBrush x:Key="Element.Foreground" Color="#FFE8E8E8"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Panel.Background" Color="#FFF5F5F5"/>
              <SolidColorBrush x:Key="Element.Foreground" Color="#FF212121"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        // 主題檔的值必須贏 —— 深色側是 #202020,不是子 App 的白
        Assert.Equal(("#FF202020", "#FFF5F5F5"), d.Palette.Entries["Panel.Background"]);
        Assert.True(d.Palette.Entries.ContainsKey("SubApp.Only")); // 中性檔補洞仍有效
        // 同鍵不同值的衝突不可默默取其一 —— 要在偵測描述裡明講(規劃書 4.2)
        Assert.Contains("conflicting values", d.Description);
        Assert.Contains("Panel.Background", d.Description);
    }

    [Fact] // HandyControl 深色檔寫 <Color>White</Color>(具名色)。只認 #hex 會讓深色值
           // 缺失、跨側退回淺色值 —— 深字疊深底,報 1.17:1 的假 fail(實際 White 疊深底 ~13.8 合格)
    public void NamedColoursInPaletteDefinitionsAreResolved()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="PrimaryTextColor">#212121</Color>
              <Color x:Key="RegionColor">#EEEEEE</Color>
              <Color x:Key="AccentColor">#2196F3</Color>
            </ResourceDictionary>
            """);
        fx.File("Themes/ColorsDark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="PrimaryTextColor">White</Color>
              <Color x:Key="RegionColor">#2D2D30</Color>
              <Color x:Key="AccentColor">#42A5F5</Color>
            </ResourceDictionary>
            """);
        fx.File("Themes/Theme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="PrimaryTextBrush" Color="{DynamicResource PrimaryTextColor}"/>
              <SolidColorBrush x:Key="RegionBrush" Color="{DynamicResource RegionColor}"/>
              <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentColor}"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        // 深色值是 White=#FFFFFF,不是退回淺色的 #212121
        Assert.Equal(("#FFFFFF", "#212121"), d.Palette.Entries["PrimaryTextBrush"]);
    }

    [Fact] // 測試素材不是色盤 —— ILSpy 實證：偵測器挑中反編譯測試的假資料檔當色盤
    public void FixtureDirectoriesAreNotPaletteCandidates()
    {
        using var fx = new Fixture();
        fx.File("Tests/Cases/Decompiled.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Junk1" Color="#010101"/>
              <SolidColorBrush x:Key="Junk2" Color="#020202"/>
              <SolidColorBrush x:Key="Junk3" Color="#030303"/>
              <SolidColorBrush x:Key="Junk4" Color="#040404"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/App.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.True(d.Palette.Entries.ContainsKey("Bg"));
        Assert.False(d.Palette.Entries.ContainsKey("Junk1")); // 測試目錄不進色盤
    }

    [Fact] // C# 三元組那條路也要排除測試／範例路徑：測試檔的 [InlineData("Bg", "#..", "#..")] 正好符合三元組格式，
           // 會搶走色盤、報出 1:1 的假 fail；搶到假色盤也讓偵測看起來沒有退化，--strict-palette 因此不會觸發
    public void CSharpTuplesInTestProjectsAreNotPaletteCandidates()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        fx.File("App.Tests/ContrastTests.cs", """
            [InlineData("Bg", "#FFFFFF", "#FFFFFF")]
            [InlineData("Fg", "#FFFFFF", "#FFFFFF")]
            [InlineData("Accent", "#FFFFFF", "#FFFFFF")]
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Fg}"/></Grid>
            """);
        var r = fx.Run();
        Assert.NotEqual(PaletteMode.CSharp, r.Detection.Mode);
        Assert.DoesNotContain("ContrastTests.cs", r.Detection.Description);
        Assert.Equal(Category.Ok, Assert.Single(r.Findings).Category); // 不是 1:1 的假 fail
    }

    [Theory] // 測試／範例路徑的判準：路徑段依 . - _ 與大小寫邊界切成詞逐一比對 —— .NET 的測試專案慣用
             // MyApp.Tests／UnitTests 命名，舊版的「路徑段以 test 開頭」對不上，放在那裡的假色盤照樣被選中
    [InlineData("src/MyApp.Tests/Themes/Theme.xaml", true)]
    [InlineData("src/UnitTests/Theme.xaml", true)]
    [InlineData("MyApp_IntegrationTests/Theme.xaml", true)]
    [InlineData("src/App-Samples/Theme.xaml", true)]
    [InlineData("Tests/Theme.xaml", true)]                  // 舊判準本來就涵蓋的形狀照舊
    [InlineData("Views/Mocks/Theme.xaml", true)]
    [InlineData("src/MyApp/Themes/Theme.xaml", false)]
    [InlineData("src/Contest/Theme.xaml", false)]           // 詞中間的 test 不算
    [InlineData("src/LatestNews/Theme.xaml", false)]
    public void TestAndSamplePathsAreRecognisedWordByWord(string path, bool isFixture)
    {
        using var fx = new Fixture();
        fx.File(path, """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(isFixture, d.IsDegraded); // 唯一的色盤在測試路徑裡 → 沒有色盤（退化要喊）
    }

    [Fact] // 帶點的色票鍵（Panel.Background）是 WPF 最普遍的命名慣例。
           // 定義端用 \w+（不含點）、使用端用 [\w.]+（含點）—— 兩邊標準不一致，
           // 於是「看得到有人在用，卻找不到定義」。ScreenToGif 實測：93 個色票認出 0 個。
    public void DottedPaletteKeysAreDetected()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Panel.Background.Color">#202020</Color>
              <SolidColorBrush x:Key="Panel.Background" Color="{StaticResource Panel.Background.Color}"/>
              <SolidColorBrush x:Key="Element.Text.Hover" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Element.Text" Color="#303030"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal("#202020", d.Palette.Entries["Panel.Background"].Dark);  // 經由帶點的 Color 鍵解出
        Assert.Equal("#FAFAFA", d.Palette.Entries["Element.Text.Hover"].Dark); // 多層點
        Assert.Equal("#303030", d.Palette.Entries["Element.Text"].Dark);
    }

    [Fact] // 色票值只有 #RRGGBB / #AARRGGBB 兩種長度有意義；正則的 {6,8} 會吃下七位數的打字錯誤
    public void MalformedHexIsNotAcceptedIntoPalette()
    {
        using var fx = new Fixture();
        // 正則是 {6,8}，七位數的打字錯誤也會過；Wcag.Luminance 只讀前六位 ——
        // 收下畸形值再默默猜前半段，等於給出一個看起來很篤定的錯答案
        fx.File("Themes/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Typo" Color="#FF0000A"/>
              <SolidColorBrush x:Key="Good" Color="#FF0000"/>
              <SolidColorBrush x:Key="Alpha" Color="#80FF0000"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.True(d.Palette.Entries.ContainsKey("Good"));   // 6 位數
        Assert.True(d.Palette.Entries.ContainsKey("Alpha"));  // 8 位數（半透明色票）
        Assert.False(d.Palette.Entries.ContainsKey("Typo"));  // 7 位數 —— 不收
    }

    [Theory] // 合併式色盤：某一側的值不合法時，舊版把缺的一側用另一主題的值補上（「缺的一側退回另一側」）——
             // 比值是編出來的（demo 的 Fg 深色值打成 #EEEEEEE：unresolved 0、fail 3 → 5，深色欄拿到淺色的 #1B1B1B
             // 報 1.09:1）。有定義但值不合法 ≠ 沒有定義：整個鍵剔除、使用處走 unknown-palette-key，偵測描述點名
    [InlineData("Color=\"#EEEEEEE\"", "dark")]                 // 七位數打字錯誤
    [InlineData("Color=\"{Binding Accent}\"", "dark")]         // 色值是 Binding：讀不懂
    [InlineData("Color=\"#GGGGGG\"", "dark")]                 // 長度對、字元不對：舊版收下之後算對比時直接崩潰
    [InlineData("Color=\"#EEEEEEE\"", "light")]
    public void InvalidValueOnOneSideIsNotFilledFromTheOtherTheme(string fgAttr, string side)
    {
        string Dict(string fg, string bg) => $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="{bg}"/>
              <SolidColorBrush x:Key="Fg" {fg}/>
              <SolidColorBrush x:Key="Accent" Color="#3A78C3"/>
              <SolidColorBrush x:Key="Line" Color="#808080"/>
            </ResourceDictionary>
            """;
        using var fx = new Fixture();
        fx.File("Themes/DarkTheme.xaml", Dict(side == "dark" ? fgAttr : "Color=\"#EEEEEE\"", "#121212"))
          .File("Themes/LightTheme.xaml", Dict(side == "light" ? fgAttr : "Color=\"#1B1B1B\"", "#FAFAFA"))
          .File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.False(r.Detection.Palette.Entries.ContainsKey("Fg"));
        Assert.Contains("1 key(s) dropped", r.Detection.Description);
        Assert.Contains(": Fg", r.Detection.Description);
        Assert.Empty(r.Findings);
        var site = Assert.Single(r.UnresolvedSites);
        Assert.Equal(UnresolvedReason.UnknownPaletteKey, site.Reason);
        Assert.Equal("Fg", site.Value);
        // console 點名時要說清楚是「定義讀不懂而剔除」，不是叫人去找打字錯誤或死引用
        var console = Report.ToConsole(r);
        Assert.Contains("unknown palette keys (top): Fg x1 (dropped: unreadable definition)", console);
        Assert.DoesNotContain("typo", console);
    }

    [Fact] // 色盤檔是用正則掃原始文字的：註解掉的舊定義也被收進色盤，還蓋掉現役值（同鍵後者覆蓋前者）——
           // 報告用一個已經失效的值算出篤定的比值，舊值比較亮時就藏掉真實的 fail
    public void CommentedOutDefinitionsAreNotPartOfThePalette()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#212121"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <!-- 舊值：<SolidColorBrush x:Key="Fg" Color="#333333"/> -->
              <Color x:Key="AccentColor">#42A5F5</Color>
              <!--
                <Color x:Key="AccentColor">#000000</Color>
                <SolidColorBrush x:Key="Ghost" Color="#123456"/>
              -->
              <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Fg" Color="#212121"/>
              <SolidColorBrush x:Key="Accent" Color="#1D6FC4"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(("#EEEEEE", "#212121"), d.Palette.Entries["Fg"]);
        Assert.Equal(("#42A5F5", "#1D6FC4"), d.Palette.Entries["Accent"]);
        Assert.False(d.Palette.Entries.ContainsKey("Ghost"));
        Assert.DoesNotContain("conflicting", d.Description); // 註解不是第二份定義
    }

    [Fact] // C# 三元組同一個根因：註解掉的舊列不是色盤
    public void CommentedOutCSharpTuplesAreNotPartOfThePalette()
    {
        using var fx = new Fixture();
        fx.File("Services/ThemeService.cs", """
            ("Bg", "#111111", "#EAEAEA"),
            ("Fg", "#EEEEEE", "#1B1B1B"),
            // ("Fg", "#333333", "#CCCCCC"),   舊值
            ("Accent", "#42A5F5", "#1D6FC4"),
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.CSharp, d.Mode);
        Assert.Equal(("#EEEEEE", "#1B1B1B"), d.Palette.Entries["Fg"]);
    }

    // ── SolidColorBrush 的 Opacity：整個 brush 變半透明。舊版只取 x:Key 與 Color，
    //    <SolidColorBrush x:Key="Hint" Color="#FFFFFF" Opacity="0.2"/> 疊黑底實際約 1.66:1，被當成不透明白字報 21:1 ok

    private static Fixture OpacityPalette(string darkExtra, string lightExtra) => new Fixture()
        .File("Themes/DarkTheme.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#000000"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Line" Color="#808080"/>
              <Color x:Key="InkColor">#000000</Color>
              {darkExtra}
            </ResourceDictionary>
            """)
        .File("Themes/LightTheme.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Line" Color="#808080"/>
              <Color x:Key="InkColor">#000000</Color>
              {lightExtra}
            </ResourceDictionary>
            """);

    [Fact] // 字色：Opacity 0.2 的白字疊黑底實際約 1.66:1 —— 不能被當成不透明色報 21:1 ok，
           // 也不能歸「跳過」讓它從報告消失（第一版修正就是這樣：21:1 ok 變成 skipped，fail 照樣不在報告裡）
    public void PaletteBrushOpacityForegroundIsGradedComposited()
    {
        using var fx = OpacityPalette("""<SolidColorBrush x:Key="Hint" Color="#FFFFFF" Opacity="0.2"/>""",
                                      """<SolidColorBrush x:Key="Hint" Color="#000000" Opacity="0.2"/>""");
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Hint}" Text="hint"/></Grid>
            """);
        var r = fx.Run();
        Assert.Equal(("#33FFFFFF", "#33000000"), r.Detection.Palette.Entries["Hint"]);
        Assert.Equal(0, r.Skipped);
        var f = Assert.Single(r.Findings);
        Assert.Equal(1.66, f.RatioDark);  // 20% 白疊 #000000
        Assert.Equal(1.61, f.RatioLight); // 20% 黑疊 #FFFFFF
        Assert.Equal(Category.Fail, f.Category);
    }

    [Theory] // 底色：半透明色票走既有的合成路徑（80% 黑疊白 = #333333，黑字 1.66:1）—— 字面值與引用 Color 鍵兩種寫法
    [InlineData("""<SolidColorBrush x:Key="Scrim" Color="#000000" Opacity="0.8"/>""")]
    [InlineData("""<SolidColorBrush x:Key="Scrim" Color="{StaticResource InkColor}" Opacity="0.8"/>""")]
    public void PaletteBrushOpacityIsCompositedAsABackground(string scrim)
    {
        using var fx = OpacityPalette(scrim, scrim);
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border Background="{DynamicResource Scrim}"><TextBlock Foreground="#000000" Text="x"/></Border>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(("#CC000000", "#CC000000"), r.Detection.Palette.Entries["Scrim"]);
        var f = Assert.Single(r.Findings);
        Assert.Equal(1.66, f.RatioDark);
        Assert.Equal(1.66, f.RatioLight);
    }

    [Fact] // 讀不懂的 Opacity（Binding 之類）：那個 brush 的實際顏色執行期才知道 —— 剔除、使用處計入 unresolved，不當成不透明
    public void UnreadablePaletteBrushOpacityDropsTheKey()
    {
        using var fx = OpacityPalette("""<SolidColorBrush x:Key="Hint" Color="#FFFFFF" Opacity="{Binding HintOpacity}"/>""",
                                      """<SolidColorBrush x:Key="Hint" Color="#000000" Opacity="0.5"/>""");
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Hint}" Text="hint"/></Grid>
            """);
        var r = fx.Run();
        Assert.False(r.Detection.Palette.Entries.ContainsKey("Hint"));
        Assert.Equal(UnresolvedReason.UnknownPaletteKey, Assert.Single(r.UnresolvedSites).Reason);
    }

    [Fact] // 同一族：brush 引用的 Color 鍵在某一側找不到（只有淺色檔定義了 FgColor）—— 深色主題下這個 brush
           // 解不出顏色，舊版仍把缺的一側用淺色的值補上，比值是編出來的。找不到的引用比照「讀不懂」處理
    public void ColorReferenceMissingOnOneSideIsNotFilledFromTheOtherTheme()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="FgColor">#1B1B1B</Color>
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.False(d.Palette.Entries.ContainsKey("Fg")); // 舊版：("#1B1B1B", "#1B1B1B")
        Assert.Contains("1 key(s) dropped", d.Description);
        Assert.Contains(": Fg", d.Description);
    }

    [Fact] // 反方向：找不到的是淺色那側（審查 mutation M2b：淺色側的 Color 引用缺失沒當成讀不懂時存活）
    public void ColorReferenceMissingOnTheLightSideIsNotFilledFromTheDarkTheme()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="FgColor">#EEEEEE</Color>
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.False(d.Palette.Entries.ContainsKey("Fg")); // 不是 ("#EEEEEE", "#EEEEEE")
        Assert.Contains(": Fg", d.Description);
    }

    [Fact] // 剔除讀不懂的鍵之後色盤掉到 3 鍵門檻以下：舊版因此放棄合併式色盤、落到「單一主題檔」那條路，
           // 整欄深色值改用淺色檔的值（Bg 深色欄拿到 #FFFFFF）—— 編造的程度比原本的 bug 還大，而且沒標退化、
           // --strict-palette 照樣 exit 0。門檻要算「定義了的鍵」，讀不懂的那個也算一個
    public void DroppingAnUnreadableKeyDoesNotKnockTheMergedPaletteBelowTheThreshold()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEEE"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.False(d.IsDegraded);
        Assert.Equal(("#111111", "#FFFFFF"), d.Palette.Entries["Bg"]); // 不是淺色值佔據兩欄
        Assert.False(d.Palette.Entries.ContainsKey("Fg"));
        Assert.Contains("1 key(s) dropped", d.Description);
    }

    [Fact] // 極端情況：某個主題的值全部讀不懂、一個鍵都沒剩 —— 不能退到「挑一個檔」讓另一個主題的值佔據兩欄；
           // 這是色盤偵測失敗，要標成退化（--strict-palette 才擋得住）並點名
    public void AllKeysUnreadableIsADegradedDetectionNotASingleThemeGuess()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#11111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEEE"/>
              <SolidColorBrush x:Key="Line" Color="{Binding Line}"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """<Grid Background="#000000"><TextBlock Foreground="#FFFFFF"/></Grid>""");
        var d = PaletteDetector.Detect(fx.Root);
        Assert.True(d.IsDegraded);
        Assert.Equal(0, d.Palette.Count);
        Assert.Contains("3 key(s) dropped", d.Description);

        var cli = Cli.Run(fx.Root, "--strict-palette");
        Assert.True(cli.Exit == 1, cli.ToString());
        Assert.Contains("exit 1: palette detection failed and --strict-palette is set", cli.Out);
    }

    // ── 色盤分組的主題提示要寬鬆（審查最後一輪 1：素材 hint-lowercase、hint-capfirst、hint-digits、hint-chinesemix、hint-deepdir）──
    //    18575a3 讓合併式色盤也改用整詞的 ThemeHint 分組：認不得的主題檔名讓兩個主題的字典都變成「中性」、先出現的贏，
    //    整個色盤塌成單一主題（mode=single，淺色 1.36:1 的真 fail 消失、exit 0）—— 而且不算退化，--strict-palette 擋不住。
    //    v0.6.1 與 02a0e1b 都偵測成 pair。整詞判定只該用在「只評單欄」那張表（認錯會藏 fail），分組要寬鬆

    private static Fixture TwoThemeFiles(string darkFile, string lightFile) => new Fixture()
        .File(darkFile, """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Muted" Color="#888888"/>
            </ResourceDictionary>
            """)
        .File(lightFile, """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Muted" Color="#DDDDDD"/>
            </ResourceDictionary>
            """)
        .File("Views/Main.xaml", """<Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Muted}" Text="muted"/></Grid>""");

    [Theory]
    [InlineData("Themes/darktheme.xaml", "Themes/lighttheme.xaml")]
    [InlineData("Themes/Darktheme.xaml", "Themes/Lighttheme.xaml")]
    [InlineData("Themes/Dark2.xaml", "Themes/Light2.xaml")]
    [InlineData("Themes/深色Dark.xaml", "Themes/淺色Light.xaml")]
    [InlineData("Themes/Dark/Colors/Palette.xaml", "Themes/Light/Colors/Palette.xaml")]
    public void ThemeFilesTheWholeWordHintCannotNameStillFormAPair(string darkFile, string lightFile)
    {
        using var fx = TwoThemeFiles(darkFile, lightFile);
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.Equal(("#888888", "#DDDDDD"), r.Detection.Palette.Entries["Muted"]);
        var f = Assert.Single(r.Findings, x => x.File == "Views/Main.xaml");
        Assert.Equal(Wcag.Contrast("#DDDDDD", "#FFFFFF"), f.RatioLight); // 淺色 1.36:1
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // 檔名完全看不出主題（Ocean／Nord、純中文檔名）時沒辦法分組 —— 與 v0.6.1 相同。但兩個中性字典以不同值定義
           // 同一批鍵，就是「看不出來的兩套主題」：先出現的贏等於只評一套。判成退化並點名，--strict-palette 擋得住
    public void NeutralDictionariesDefiningTheSameKeysDifferentlyAreADegradedDetection()
    {
        using var fx = TwoThemeFiles("Themes/Nord.xaml", "Themes/Ocean.xaml");
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Single, d.Mode);
        Assert.True(d.IsDegraded);
        Assert.Contains("3 key(s) defined with conflicting values", d.Description);
        Assert.Contains("themes the file names don't tell apart", d.Description);

        var cli = Cli.Run(fx.Root, "--strict-palette");
        Assert.True(cli.Exit == 1, cli.ToString());
        Assert.Contains("exit 1: palette detection failed and --strict-palette is set", cli.Out);
    }

    [Fact] // 反方向：兩個中性字典只是各自定義不同的鍵、或同鍵同值 —— 正常的拆檔，不是退化
    public void NeutralDictionariesWithoutConflictsAreNotDegraded()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/><SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Brushes.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/><SolidColorBrush x:Key="Muted" Color="#888888"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.False(d.IsDegraded);
    }

    [Fact] // 同上，字典裡另外有 <Color> 定義：brush 有定義、只是全部讀不懂時不退回 Color 表 —— 那些 brush 才是色盤，
           // 拿 Color 鍵充當色票會讓偵測看起來成功（審查 mutation B3c：「沒有 brush 才用 Color 表」的條件拿掉時存活）
    public void AllBrushesUnreadableDoesNotFallBackToTheColourTable()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="C1">#111111</Color>
              <Color x:Key="C2">#222222</Color>
              <Color x:Key="C3">#333333</Color>
              <SolidColorBrush x:Key="Bg" Color="#11111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEEE"/>
              <SolidColorBrush x:Key="Line" Color="{Binding Line}"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="C1">#EEEEEE</Color>
              <Color x:Key="C2">#DDDDDD</Color>
              <Color x:Key="C3">#CCCCCC</Color>
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.True(d.IsDegraded);
        Assert.Equal(0, d.Palette.Count);
        Assert.Contains("3 key(s) dropped", d.Description);
    }

    [Fact] // 同上，<Color> 字典那條路（純 Color 字典直接當色票時）
    public void InvalidColorValueOnOneSideIsNotFilledFromTheOtherTheme()
    {
        using var fx = new Fixture();
        fx.File("Themes/ColorsDark.xaml", ColorDict("Bg=#111111", "Fg=#EEEEEEE", "Accent=#42A5F5", "Line=#333333"))
          .File("Themes/ColorsLight.xaml", ColorDict("Bg=#FAFAFA", "Fg=#212121", "Accent=#2196F3", "Line=#DDDDDD"));
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(("#111111", "#FAFAFA"), d.Palette.Entries["Bg"]);
        Assert.False(d.Palette.Entries.ContainsKey("Fg"));
        Assert.Contains("1 key(s) dropped", d.Description);
    }

    [Fact] // 畸形色票的使用處要走 unresolved 喊出來，不是靜默算出一個錯的比值
    public void UsageOfMalformedPaletteKeyCountsAsUnresolved()
    {
        using var fx = new Fixture();
        fx.File("Themes/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Typo" Color="#FF0000A"/>
              <SolidColorBrush x:Key="Good" Color="#222222"/>
              <SolidColorBrush x:Key="Spare" Color="#333333"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Typo}" Text="打錯的色票"/>
              <TextBlock Foreground="{DynamicResource Good}" Text="正常"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Unresolved);      // 打錯的那個：喊出來
        Assert.Single(r.Findings);          // 只有正常的那筆進報告
    }

    [Fact] // Cornea 形狀：什麼都沒有 → 退化，要喊
    public void NothingFoundIsDegradedLoudly()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", "<Grid/>");
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.None, d.Mode);
        Assert.True(d.IsDegraded);
        Assert.Contains("no palette found", d.Description);
    }

    [Fact] // 回頭檢視抓到的 bug 回歸：根目錄路徑含 dark 不可污染提示
    public void DarkInRootPathDoesNotPoisonHints()
    {
        using var fx = new Fixture("xc-darkapp-" + Guid.NewGuid().ToString("N"));
        fx.File("Resources/DarkTheme.xaml", DarkDict)
          .File("Resources/LightTheme.xaml", LightDict);
        var d = PaletteDetector.Detect(fx.Root);
        // 提示只看專案內相對路徑 —— 看完整路徑的話 Light 檔會同時染上 dark 提示、配對失效
        Assert.Equal(PaletteMode.Pair, d.Mode);
    }

    [Fact] // obj/bin 底下的複本不可入選
    public void ObjAndBinAreIgnored()
    {
        using var fx = new Fixture();
        fx.File("obj/Debug/DarkTheme.xaml", DarkDict)
          .File("bin/Release/LightTheme.xaml", LightDict);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.None, d.Mode);
    }

    [Fact] // bin／obj 只看 root 以下的相對路徑：舊版比對含 root 的完整路徑，專案本身放在叫 bin 或 obj 的目錄底下
           // （root 用絕對路徑）就整個專案 0 個檔 → 「resolved 0 pairs」exit 1，訊息誤導、工具在那裡等於不能用；
           // 同一個目錄改傳相對路徑又正常 —— 結果跟著 root 的寫法變
    public void ProjectLivingUnderABinDirectoryIsStillScanned()
    {
        using var fx = new Fixture(Path.Combine("xc-" + Guid.NewGuid().ToString("N"), "bin", "MyApp"));
        fx.File("Main.xaml", """<Grid Background="#000000"><TextBlock Foreground="#FFFFFF"/></Grid>""");
        try
        {
            var r = fx.Run();
            Assert.Equal(1, r.FileCount);
            Assert.Single(r.Findings);
        }
        finally { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(fx.Root))!, recursive: true); }
    }

    [Theory] // 而且不分大小寫（Windows 的檔案系統不分）：Bin／OBJ 底下的建置輸出照樣排除；binder 這種名字不算
    [InlineData("Bin/Release/Main.xaml", 0)]
    [InlineData("OBJ/Debug/Main.xaml", 0)]
    [InlineData("binder/Main.xaml", 1)]
    [InlineData("Views/objects/Main.xaml", 1)]
    public void BinAndObjSegmentsAreMatchedWholeAndCaseInsensitively(string path, int files)
    {
        using var fx = new Fixture();
        fx.File(path, """<Grid Background="#000000"><TextBlock Foreground="#FFFFFF"/></Grid>""");
        Assert.Equal(files, fx.Run().FileCount);
    }

    // ── 合併式色盤湊不到 3 個鍵時的退路：挑檔（偵測順序的「theme pair」與「single theme file」）──
    //    合併式只從 brush 取鍵，全專案一個 brush 都沒有才改用 <Color> 鍵 —— 所以「主題檔只有 <Color>、
    //    別處零星一個 brush」的專案，合併式不成立，交給這兩條挑檔的路。它們沒接住就是整個退化成無色盤。

    private static string ColorDict(params string[] keyHex) => $"""
        <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          {string.Join("\n  ", keyHex.Select(kv => kv.Split('=')).Select(kv => $"<Color x:Key=\"{kv[0]}\">{kv[1]}</Color>"))}
        </ResourceDictionary>
        """;

    private const string StrayBrush = """
        <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="Stray" Color="#FF0000"/>
        </ResourceDictionary>
        """;

    [Fact] // 深淺兩檔鍵集重疊 ≥ 50% → 配對。專案放在帶 dark 字樣的目錄底下 —— 這條路的檔名提示
           // （XamlCandidates）也只能看專案內相對路徑，否則淺色檔同時染上 dark、配對整個失效
    public void ThemePairFallbackWhenTheMergedPaletteIsTooSmall()
    {
        using var fx = new Fixture("xc-darkapp-" + Guid.NewGuid().ToString("N"));
        fx.File("Themes/ColorsDark.xaml", ColorDict("Bg=#111111", "Fg=#EEEEEE", "Accent=#42A5F5"))
          .File("Themes/ColorsLight.xaml", ColorDict("Bg=#FAFAFA", "Fg=#212121", "Other=#123456"))
          .File("Controls/Stray.xaml", StrayBrush);
        Assert.Contains("dark", fx.Root); // 前提：專案所在路徑真的帶著提示字樣

        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.False(d.IsDegraded);
        Assert.StartsWith("auto-detected theme pair: ", d.Description);
        Assert.Equal(2, d.Palette.Count);                                   // 只收兩邊都有的鍵（2/3 ≥ 50%）
        Assert.Equal(("#111111", "#FAFAFA"), d.Palette.Entries["Bg"]);
        Assert.Equal(("#EEEEEE", "#212121"), d.Palette.Entries["Fg"]);
        Assert.Equal(["ColorsDark.xaml", "ColorsLight.xaml"], d.ExcludedFiles.Select(Path.GetFileName).Order());
    }

    [Fact] // 重疊 < 50% 不配對（1/3）→ 單一主題檔：挑鍵最多的那個（不是掃描順序的第一個），
           // 其餘候選要點名 —— 沒被採用的色盤檔不能靜默消失
    public void LargestSingleFileFallbackWhenNoPairOverlapsEnough()
    {
        using var fx = new Fixture();
        fx.File("Themes/ColorsDark.xaml", ColorDict("Bg=#111111", "Fg=#EEEEEE", "Accent=#42A5F5"))
          .File("Themes/ColorsLight.xaml", ColorDict("Bg=#FAFAFA", "L1=#212121", "L2=#2196F3", "L3=#E0E0E0"))
          .File("Controls/Stray.xaml", StrayBrush);

        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(PaletteMode.Single, d.Mode);
        Assert.False(d.IsDegraded);
        Assert.StartsWith("auto-detected single-theme palette: ", d.Description);
        Assert.Contains("ColorsLight.xaml (4 keys); unused candidates: ", d.Description);
        Assert.EndsWith("ColorsDark.xaml", d.Description);
        Assert.Equal(("#FAFAFA", "#FAFAFA"), d.Palette.Entries["Bg"]);     // 單一主題：深淺同值
        Assert.Equal(["ColorsLight.xaml"], d.ExcludedFiles.Select(Path.GetFileName));
    }

    [Fact] // 字面值 brush 也可以寫具名色（Color="White"）—— 合併式的 brush 表要認得，
           // 否則這一側缺值、退回另一側的值，深淺兩欄變成同一個編造的顏色
    public void NamedColourLiteralBrushesAreResolved()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="White"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Fg" Color="#212121"/>
              <SolidColorBrush x:Key="Accent" Color="#1D6FC4"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(("#FFFFFF", "#212121"), d.Palette.Entries["Fg"]);
    }

    [Theory] // 同鍵不同值要明講，但「同鍵同值」不是衝突（兩個字典重複定義同一個品牌色很常見，報出來只是雜訊）；
             // 衝突清單最多點名 5 個，其餘以 … 表示 —— 數量照實印
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void ConflictNoteCountsRealConflictsAndNamesAtMostFive(int conflicting, bool truncated)
    {
        string Dict(string hex) => $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Same" Color="#42A5F5"/>
              {string.Join("\n  ", Enumerable.Range(1, conflicting).Select(i => $"<SolidColorBrush x:Key=\"K{i}\" Color=\"{hex}\"/>"))}
            </ResourceDictionary>
            """;
        using var fx = new Fixture();
        fx.File("Lib/A.xaml", Dict("#111111")).File("Lib/B.xaml", Dict("#222222"));

        var d = PaletteDetector.Detect(fx.Root);
        Assert.Contains($"!! {conflicting} key(s) defined with conflicting values", d.Description);
        Assert.Contains("wins): K1, K2, K3, K4, K5", d.Description);
        Assert.Equal(truncated, d.Description.Contains("K5, …")); // 衝突提示之後可能還接著「分不出主題」的退化提示
        Assert.DoesNotContain("K6", d.Description);
        Assert.DoesNotContain("Same", d.Description);
        Assert.Equal(("#111111", "#111111"), d.Palette.Entries["K1"]); // 先出現的贏（字典序 A 在 B 前）
    }

    [Fact] // 畫面檔的區域定義只是那個畫面的：Badge.xaml 的 UserControl.Resources 把 Fg 綁到 Binding，舊版當成
           // 中性的字面值灌進全域色盤，蓋掉主題用 Color 引用定義的同名鍵 —— 整個專案的 Fg 都變成 unknown-palette-key，
           // 提示還說是「dead reference, typo」（審查素材 h4poison）。全域色盤只讓畫面檔補「字典沒定義的鍵」，
           // 讀不懂的區域值不進全域；在那個畫面裡，區域定義蓋過全域（綁定的就是執行期才知道）
    public void ScreenLocalDefinitionsDoNotPolluteTheGlobalPalette()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="FgColor">#EEEEEE</Color>
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="FgColor">#1B1B1B</Color>
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="{StaticResource FgColor}"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        fx.File("Views/Badge.xaml", """
            <UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <SolidColorBrush x:Key="Fg" Color="{Binding BadgeColor}"/>
                <SolidColorBrush x:Key="Line" Color="#FF0000"/>
                <SolidColorBrush x:Key="BadgeOnly" Color="#000000"/>
              </UserControl.Resources>
              <StackPanel Background="#FFFFFF">
                <TextBlock Foreground="{DynamicResource Fg}" Text="local Fg is bound"/>
                <TextBlock Foreground="{DynamicResource Line}" Text="local Line wins here"/>
              </StackPanel>
            </UserControl>
            """);
        fx.File("Views/Main.xaml", """
            <StackPanel Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="theme Fg"/>
              <TextBlock Foreground="{DynamicResource Line}" Text="theme Line"/>
              <TextBlock Foreground="{DynamicResource BadgeOnly}" Text="key only a screen defines"/>
            </StackPanel>
            """);
        var r = fx.Run();
        Assert.Equal(("#EEEEEE", "#1B1B1B"), r.Detection.Palette.Entries["Fg"]);   // 沒被區域的 Binding 蓋掉
        Assert.Equal(("#333333", "#CCCCCC"), r.Detection.Palette.Entries["Line"]); // 也沒被區域的字面值蓋掉
        Assert.DoesNotContain("dropped", r.Detection.Description);

        Assert.Equal(2, r.Findings.Count(f => f.File == "Views/Main.xaml"));
        var badgeLine = Assert.Single(r.Findings, f => f.File == "Views/Badge.xaml");
        Assert.Equal(Wcag.Contrast("#FF0000", "#FFFFFF"), badgeLine.RatioDark);     // 畫面裡區域定義蓋過全域
        var bound = Assert.Single(r.UnresolvedSites, u => u.File == "Views/Badge.xaml");
        Assert.Equal(UnresolvedReason.BoundOrGradient, bound.Reason);              // 不是 unknown-palette-key（不是打字錯誤）
        // 只有 Badge.xaml 定義的鍵，執行期 Main.xaml 看不到（DynamicResource 找不到資源）—— 不能拿 Badge 的值去評
        // （舊版把它寫成預期：Main.xaml 的 BadgeOnly 報 #000000 的比值，等於把猜測寫進規格）
        var foreign = Assert.Single(r.UnresolvedSites, u => u.File == "Views/Main.xaml");
        Assert.Equal(UnresolvedReason.UnknownPaletteKey, foreign.Reason);
        Assert.Equal("BadgeOnly", foreign.Value);
    }

    // ── 畫面的區域 brush 引用 Color（審查素材 m3、m3lib）──
    //    舊版的區域覆蓋只收字面值：A.xaml 的區域 Accent 引用 WarnColor（黃），報告卻用全域 Accent 的藍，
    //    白底上實際 1.2:1 給 8.63 ok；B.xaml 自己的區域 Badge 拿到的是 A 畫面的 Badge（區域層的引用補進了全域）。
    //    引用外部函式庫 Color 的區域 brush（MahApps.Colors.Accent）更被描述成「主題定義了讀不懂的值」

    private static Fixture ScreensWithLocalColorReferences() => new Fixture()
        .File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="AccentColor">#0D47A1</Color>
              <Color x:Key="WarnColor">#FFEB3B</Color>
              <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
            </ResourceDictionary>
            """)
        .File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="AccentColor">#0D47A1</Color>
              <Color x:Key="WarnColor">#FFEB3B</Color>
              <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
            </ResourceDictionary>
            """)
        .File("Views/A.xaml", """
            <UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <SolidColorBrush x:Key="Accent" Color="{StaticResource WarnColor}"/>
                <SolidColorBrush x:Key="Badge" Color="{StaticResource AccentColor}"/>
              </UserControl.Resources>
              <Grid Background="{DynamicResource Bg}">
                <TextBlock Foreground="{DynamicResource Accent}" Text="yellow on white"/>
                <TextBlock Foreground="{DynamicResource Badge}" Text="blue on white"/>
              </Grid>
            </UserControl>
            """)
        .File("Views/B.xaml", """
            <UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <SolidColorBrush x:Key="Badge" Color="{StaticResource WarnColor}"/>
              </UserControl.Resources>
              <Grid Background="{DynamicResource Bg}">
                <TextBlock Foreground="{DynamicResource Badge}" Text="yellow on white"/>
              </Grid>
            </UserControl>
            """);

    [Fact]
    public void LocalColorReferenceBrushesAreResolvedInTheirOwnScreen()
    {
        using var fx = ScreensWithLocalColorReferences();
        var r = fx.Run();
        Assert.Equal(("#0D47A1", "#0D47A1"), r.Detection.Palette.Entries["Accent"]); // 全域的 Accent 沒被畫面的引用蓋掉
        Assert.DoesNotContain("dropped", r.Detection.Description);
        var yellow = Wcag.Contrast("#FFEB3B", "#FFFFFF");
        var blue = Wcag.Contrast("#0D47A1", "#FFFFFF");
        var a = r.Findings.Where(f => f.File == "Views/A.xaml").ToList();
        Assert.Equal(yellow, Assert.Single(a, f => f.Fg == "{DynamicResource Accent}").RatioDark); // 區域引用蓋過全域
        Assert.Equal(blue, Assert.Single(a, f => f.Fg == "{DynamicResource Badge}").RatioDark);
        Assert.Equal(yellow, Assert.Single(r.Findings, f => f.File == "Views/B.xaml").RatioDark);  // B 用自己的 Badge
        Assert.Equal(2, r.CountOf(Category.Fail));
    }

    [Fact] // 區域 brush 引用的 Color 在畫面與專案裡都找不到（外部函式庫的 MahApps.Colors.Accent）：使用處記
           // unknown-palette-key、點名的是找不到的那個 Color；偵測描述不能說成「主題定義了讀不懂的值」
    public void LocalBrushReferencingALibraryColourNamesTheMissingColour()
    {
        using var fx = ScreensWithLocalColorReferences();
        fx.File("Views/C.xaml", """
            <UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <SolidColorBrush x:Key="Halo" Color="{StaticResource MahApps.Colors.Accent}"/>
              </UserControl.Resources>
              <Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Halo}" Text="lib ref"/></Grid>
            </UserControl>
            """);
        var r = fx.Run();
        Assert.DoesNotContain("dropped", r.Detection.Description);
        Assert.Empty(r.Detection.DroppedKeys);
        var u = Assert.Single(r.UnresolvedSites, x => x.File == "Views/C.xaml");
        Assert.Equal(UnresolvedReason.UnknownPaletteKey, u.Reason);
        Assert.Equal("MahApps.Colors.Accent", u.Value);
        Assert.DoesNotContain(r.Findings, f => f.File == "Views/C.xaml");
    }

    [Fact] // 「當色盤來源」與「排除出 UI 掃描」是兩件事：畫面檔在 UserControl.Resources 放一個區域 brush，
           // 它的鍵照樣進色盤，但檔案本身仍要稽核。舊版合併式色盤把任何含 keyed brush 的 XAML 都當色盤檔
           // 整檔排除 —— 這份白底淺灰字（1.16:1）從報告消失，CLI 印 files 0／exit 1「0 配對」，混在大專案裡就是靜默漏報
    public void ScreenWithALocalBrushIsAPaletteSourceButStillAudited()
    {
        using var fx = new Fixture().WithPalette();
        fx.File("Views/B.xaml", """
            <UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <SolidColorBrush x:Key="LocalAccent" Color="#FF0000"/>
              </UserControl.Resources>
              <Grid Background="#FFFFFF">
                <TextBlock Foreground="#EEEEEE" Text="1.16:1"/>
              </Grid>
            </UserControl>
            """);
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.True(r.Detection.Palette.Entries.ContainsKey("LocalAccent")); // 色盤來源照收
        Assert.DoesNotContain(r.Detection.ExcludedFiles, f => f.EndsWith("B.xaml"));
        Assert.Equal(2, r.Detection.ExcludedFiles.Count);                    // 只有兩個 ResourceDictionary
        Assert.Equal(1, r.FileCount);
        var f = Assert.Single(r.Findings);
        Assert.Equal("Views/B.xaml", f.File);
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // App.xaml 的 Application.Resources 放色票是最常見的寫法 —— 它是色盤來源，但裡面的 Style 一樣要稽核
    public void ApplicationResourcesBrushesDoNotHideTheAppFile()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources>
                <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
                <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
                <SolidColorBrush x:Key="Accent" Color="#2196F3"/>
                <Style x:Key="Pale" TargetType="Button">
                  <Setter Property="Background" Value="{DynamicResource Bg}"/>
                  <Setter Property="Foreground" Value="{DynamicResource Fg}"/>
                </Style>
              </Application.Resources>
            </Application>
            """);
        var r = fx.Run();
        Assert.Equal(3, r.Detection.Palette.Count);
        Assert.Empty(r.Detection.ExcludedFiles);
        var f = Assert.Single(r.Findings);
        Assert.Equal("Style[Pale]/base", f.Element);
        Assert.Equal(Category.Fail, f.Category);
    }

    // ── Application 的子類別也是 App（審查最後一輪 2：素材 loc-prism、loc-prism2）──
    //    00c18b8 的「畫面檔」判定是「根元素不是 ResourceDictionary 也不是 Application」：Prism 的 <prism:PrismApplication>、
    //    任何以自訂 Application 子類別當根元素的 App.xaml 被當成畫面，Application.Resources 的鍵全變成那個「畫面」的區域鍵 ——
    //    其他畫面的使用處一律 unknown-palette-key，訊息還說是「dead reference, typo」；真 fail 消失、exit 1 → 0

    [Theory]
    [InlineData("""<prism:PrismApplication xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:prism="http://prismlibrary.com/">""", "prism:PrismApplication")]
    [InlineData("""<local:App xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Demo">""", "local:App")]
    public void ApplicationSubclassRootIsTheAppNotAScreen(string open, string close)
    {
        using var fx = new Fixture();
        fx.File("App.xaml", $"""
            {open}
              <Application.Resources>
                <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
                <SolidColorBrush x:Key="Fg" Color="#111111"/>
                <SolidColorBrush x:Key="Muted" Color="#DDDDDD"/>
              </Application.Resources>
            </{close}>
            """);
        fx.File("Views/Main.xaml", """
            <Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Muted}" Text="hint"/></Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.UnresolvedSites);
        var f = Assert.Single(r.Findings, x => x.File == "Views/Main.xaml");
        Assert.Equal(Wcag.Contrast("#DDDDDD", "#FFFFFF"), f.RatioDark); // 1.36:1
        Assert.Equal(Category.Fail, f.Category);
        Assert.False(PaletteDetector.IsScreenFile(Path.Combine(fx.Root, "App.xaml")));
    }

    [Fact] // 多 App 作用域的邊界一樣要認得 Application 子類別
    public void ApplicationSubclassRootsAreScopeBoundaries()
    {
        using var fx = new Fixture();
        foreach (var app in new[] { "A", "B" })
            fx.File($"{app}/App.xaml", """
                <prism:PrismApplication xmlns:prism="http://prismlibrary.com/"><Application.Resources/></prism:PrismApplication>
                """);
        Assert.Equal(2, PaletteDetector.ApplicationScopeRoots(fx.Root).Count);
    }

    [Fact] // 根部就不是 XML 的檔案（樣板殘骸、編碼壞掉）不能讓偵測整個崩潰（找 <Application> 根元素那一步也要擋）；
           // 它交給稽核端喊 parse failed —— 不是讓檔案從報告裡消失
    public void UnparseableXamlNeitherBreaksDetectionNorDisappears()
    {
        using var fx = new Fixture().WithPalette();
        fx.File("Broken.xaml", "this is not xml at all");
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.StartsWith("Broken.xaml: ", Assert.Single(r.ParseErrors));
        Assert.Equal("Main.xaml", Assert.Single(r.Findings).File);
        Assert.Equal(2, r.FileCount); // 兩個主題檔是色盤，不進 UI 掃描；壞檔照算一個
    }
}

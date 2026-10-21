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
        Assert.Equal(truncated, d.Description.EndsWith(", …"));
        Assert.DoesNotContain("K6", d.Description);
        Assert.DoesNotContain("Same", d.Description);
        Assert.Equal(("#111111", "#111111"), d.Palette.Entries["K1"]); // 先出現的贏（字典序 A 在 B 前）
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

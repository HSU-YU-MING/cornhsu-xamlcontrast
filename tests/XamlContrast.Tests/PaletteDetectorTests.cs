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
}

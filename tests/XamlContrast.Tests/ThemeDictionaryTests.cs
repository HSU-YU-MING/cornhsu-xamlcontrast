using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// WinUI／UWP（1.4）：<c>{ThemeResource K}</c> 與 <c>&lt;ResourceDictionary.ThemeDictionaries&gt;</c>。
/// 之前 ThemeResource 落到 bound-or-gradient（「執行期才知道」）、同一個檔裡的深淺兩份字典被當成同名鍵衝突
/// （先出現的贏，另一個主題整欄是編的）。GitHub 上約 134,000 個 XAML 檔用 ThemeResource、約 13,000 個有 ThemeDictionaries。
/// </summary>
public class ThemeDictionaryTests
{
    private const string WinUiColors = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Default">
              <SolidColorBrush x:Key="PageBg" Color="#1E1E1E"/>
              <SolidColorBrush x:Key="Fg" Color="#F0F0F0"/>
              <SolidColorBrush x:Key="Muted" Color="#5A5A5A"/>
            </ResourceDictionary>
            <ResourceDictionary x:Key="Light">
              <SolidColorBrush x:Key="PageBg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Muted" Color="#767676"/>
            </ResourceDictionary>
            <ResourceDictionary x:Key="HighContrast">
              <SolidColorBrush x:Key="Fg" Color="{ThemeResource SystemColorWindowTextColor}"/>
            </ResourceDictionary>
          </ResourceDictionary.ThemeDictionaries>
          <SolidColorBrush x:Key="Accent" Color="#0078D4"/>
        </ResourceDictionary>
        """;

    private const string WinUiPage = """
        <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid Background="{ThemeResource PageBg}">
            <TextBlock Foreground="{ThemeResource Fg}" Text="a"/>
            <TextBlock Foreground="{ThemeResource Muted}" Text="b"/>
          </Grid>
        </Page>
        """;

    [Fact]
    public void ThemeDictionariesInOneFileBecomeADarkLightPair()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", WinUiColors).File("MainPage.xaml", WinUiPage);

        var d = PaletteDetector.Detect(fx.Root);

        Assert.Equal(PaletteMode.Pair, d.Mode);
        Assert.False(d.IsDegraded);
        Assert.Equal(("#1E1E1E", "#FFFFFF"), d.Palette.Entries["PageBg"]);   // Default = 深色主題
        Assert.Equal(("#F0F0F0", "#1B1B1B"), d.Palette.Entries["Fg"]);       // HighContrast 那一份不評
        Assert.Equal(("#0078D4", "#0078D4"), d.Palette.Entries["Accent"]);   // ThemeDictionaries 以外的兩個主題共用
        Assert.DoesNotContain("conflict", d.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThemeResourceIsResolvedPerTheme()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", WinUiColors).File("MainPage.xaml", WinUiPage);

        var r = fx.Run();

        Assert.Equal(0, r.Unresolved);
        var fg = Assert.Single(r.Findings, f => f.ColorsDark!.Fg == "#F0F0F0");
        Assert.Equal(Wcag.Contrast("#F0F0F0", "#1E1E1E"), fg.RatioDark);
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#FFFFFF"), fg.RatioLight);
        // 只有深色主題不合格：舊版只看得到其中一份字典，這種「一個主題壞掉」根本報不出來
        var muted = Assert.Single(r.Findings, f => f.ColorsDark!.Fg == "#5A5A5A");
        Assert.True(muted.RatioDark < 4.5);
        Assert.True(muted.RatioLight >= 4.5);
    }

    [Theory] // 缺 Dark 或 Light 時那個主題用 Default；只有 Dark／Light 時各用各的
    [InlineData("Dark", "Light", "#111111", "#EEEEEE")]
    [InlineData("Default", "Light", "#111111", "#EEEEEE")]
    [InlineData("Dark", "Default", "#111111", "#EEEEEE")]
    public void DefaultFillsTheThemeThatIsMissing(string first, string second, string dark, string light)
    {
        var text = $"""
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="{first}"><SolidColorBrush x:Key="A" Color="#111111"/></ResourceDictionary>
              <ResourceDictionary x:Key="{second}"><SolidColorBrush x:Key="A" Color="#EEEEEE"/></ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            """;

        var s = PaletteDetector.ThemeDictionarySections(text)!.Value;

        Assert.Contains(dark, s.Dark);
        Assert.Contains(light, s.Light);
    }

    [Fact] // 主題字典裡合併進來的檔（自我結束的 <ResourceDictionary Source="…"/>）不是主題字典本身；沒有 ThemeDictionaries 就是 null
    public void SectionsIgnoreMergedSourcesAndPlainFiles()
    {
        var text = """
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="Light">
                <ResourceDictionary.MergedDictionaries><ResourceDictionary Source="Extra.xaml"/></ResourceDictionary.MergedDictionaries>
                <SolidColorBrush x:Key="A" Color="#EEEEEE"/>
              </ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            <SolidColorBrush x:Key="B" Color="#123456"/>
            """;

        var s = PaletteDetector.ThemeDictionarySections(text)!.Value;

        Assert.Null(s.Dark);
        Assert.Contains("#EEEEEE", s.Light);
        Assert.Contains("#123456", s.Neutral);
        Assert.DoesNotContain("#EEEEEE", s.Neutral);
        Assert.Null(PaletteDetector.ThemeDictionarySections("""<SolidColorBrush x:Key="B" Color="#123456"/>"""));
    }

    [Fact] // 專案裡找不到的 ThemeResource 鍵（WinUI 內建的 TextFillColorPrimaryBrush 之類）跟 DynamicResource 一樣是 unknown-palette-key
    public void ThemeResourceToAKeyTheProjectDoesNotDefineIsAnUnknownKey()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", WinUiColors).File("MainPage.xaml", """
            <Page><Grid Background="{ThemeResource PageBg}"><TextBlock Foreground="{ThemeResource TextFillColorPrimaryBrush}" Text="a"/></Grid></Page>
            """);

        var r = fx.Run();

        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.UnknownPaletteKey]);
        Assert.Equal(0, r.UnresolvedBy.GetValueOrDefault(UnresolvedReason.BoundOrGradient));
    }
}

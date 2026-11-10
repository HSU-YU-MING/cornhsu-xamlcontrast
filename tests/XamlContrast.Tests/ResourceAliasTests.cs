using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 1.4：<c>&lt;StaticResource x:Key="A" ResourceKey="B"/&gt;</c> 別名，與 brush 裡直接寫 Color 鍵
/// （<c>&lt;SolidColorBrush Color="{StaticResource FgColor}"/&gt;</c>）。之前兩者都是 unknown-palette-key
/// （WinUI Gallery：GalleryBackgroundBrush ×28、TextFillColorPrimary ×21）。
/// </summary>
public class ResourceAliasTests
{
    private const string TwoThemes = """
        <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Color x:Key="FgColor">{0}</Color>
          <SolidColorBrush x:Key="Base" Color="{1}"/>
          <SolidColorBrush x:Key="Other" Color="#808080"/>
          <SolidColorBrush x:Key="Third" Color="#404040"/>
          <StaticResource x:Key="PageBg" ResourceKey="Base"/>
          <StaticResource x:Key="CardBg" ResourceKey="PageBg"/>
          <StaticResource x:Key="Ghost" ResourceKey="Nowhere"/>
        </ResourceDictionary>
        """;

    private static Fixture Project(string page)
    {
        var fx = new Fixture();
        fx.File("Themes/Dark.xaml", string.Format(TwoThemes, "#EEEEEE", "#202020"))
          .File("Themes/Light.xaml", string.Format(TwoThemes, "#111111", "#FFFFFF"))
          .File("Main.xaml", page);
        return fx;
    }

    [Fact]
    public void AColourKeyInsideABrushResolvesPerTheme()
    {
        using var fx = Project("""
            <Grid Background="{DynamicResource Base}">
              <TextBlock Text="a"><TextBlock.Foreground><SolidColorBrush Color="{DynamicResource FgColor}"/></TextBlock.Foreground></TextBlock>
            </Grid>
            """);

        var f = Assert.Single(fx.Run().Findings);

        Assert.Equal(new ContrastColors("#EEEEEE", "#202020"), f.ColorsDark);
        Assert.Equal(new ContrastColors("#111111", "#FFFFFF"), f.ColorsLight);
    }

    [Fact]
    public void AliasesResolveIncludingChainsAndNameTheMissingTarget()
    {
        using var fx = Project("""
            <StackPanel>
              <Border Background="{DynamicResource CardBg}"><TextBlock Foreground="White" Text="a"/></Border>
              <Border Background="{DynamicResource Ghost}"><TextBlock Foreground="White" Text="b"/></Border>
            </StackPanel>
            """);

        var r = fx.Run();

        var f = Assert.Single(r.Findings);
        Assert.Equal("#202020", f.ColorsDark!.Bg);    // CardBg → PageBg → Base
        Assert.Equal("#FFFFFF", f.ColorsLight!.Bg);
        Assert.Equal(("#202020", "#FFFFFF"), r.Detection.Palette.Entries["CardBg"]);
        var ghost = Assert.Single(r.UnresolvedSites, u => u.Reason == UnresolvedReason.UnknownPaletteKey);
        Assert.Equal("Nowhere", ghost.Value);          // 點名它指向、卻找不到的鍵
    }

    [Fact]
    public void AWinUiAliasToAFrameworkKeyGetsTheFrameworkDefault_AndAnAccentAliasStaysRuntime()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><Application.Resources><ResourceDictionary>
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <StaticResource x:Key="GalleryBg" ResourceKey="SolidBackgroundFillColorBaseBrush"/>
                  <StaticResource x:Key="GalleryAccent" ResourceKey="AccentFillColorDefaultBrush"/>
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <StaticResource x:Key="GalleryBg" ResourceKey="SolidBackgroundFillColorBaseBrush"/>
                  <StaticResource x:Key="GalleryAccent" ResourceKey="AccentFillColorDefaultBrush"/>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary></Application.Resources></Application>
            """).File("MainPage.xaml", """
            <Page><Grid Background="{ThemeResource GalleryBg}">
              <TextBlock Foreground="{ThemeResource TextFillColorPrimaryBrush}" Text="a"/>
              <Border Background="{ThemeResource GalleryAccent}"><TextBlock Foreground="White" Text="b"/></Border>
            </Grid></Page>
            """);

        var r = fx.Run();

        var bg = WinUiTheme.Current.Entries["SolidBackgroundFillColorBaseBrush"];
        var f = Assert.Single(r.Findings);
        Assert.Equal(bg[0], f.ColorsDark!.Bg);
        Assert.Equal(bg[1], f.ColorsLight!.Bg);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);   // 強調色的別名：跟著使用者設定，不猜
    }

    [Fact]
    public void APagesOwnAliasPointsAtTheGlobalPalette()
    {
        using var fx = Project("""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources><StaticResource x:Key="Local" ResourceKey="Base"/></Grid.Resources>
              <Border Background="{DynamicResource Local}"><TextBlock Foreground="White" Text="a"/></Border>
            </Grid>
            """);

        var f = Assert.Single(fx.Run().Findings);

        Assert.Equal("#202020", f.ColorsDark!.Bg);
        Assert.Equal("#FFFFFF", f.ColorsLight!.Bg);
    }

    [Fact]
    public void AliasDefinitionsParse()
    {
        var aliases = PaletteDetector.ParseAliasDefsText("""
            <StaticResource x:Key="A" ResourceKey="B"/>
            <ThemeResource x:Key="C" ResourceKey="D" />
            <StaticResource ResourceKey="NoKey"/>
            <StaticResource x:Key="Self" ResourceKey="Self"/>
            """);

        Assert.Equal(new Dictionary<string, string> { ["A"] = "B", ["C"] = "D" }, aliases);
    }
}

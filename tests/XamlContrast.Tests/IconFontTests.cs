using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 圖示字型（1.2）：用圖示字型畫的 TextBlock／Run／Button 內容是圖形，照 WCAG 1.4.11 用 3:1 要求，JSON 的 isText 是 false。
/// ⚠ 不是 decorative：那會讓 1.2:1 的圖示整個從報告消失。認不出來的字型照舊當文字（4.5:1）—— 往嚴格那邊錯。
/// 素材全是自己寫的最小重現。#949494 疊白底是 3.03:1：文字是 warn、圖示是 ok —— 這一條線就是這個 PR 改的東西。
/// </summary>
public class IconFontTests
{
    [Theory] // 認得的寫法：字型名、/Fonts/#、./Fonts/#、pack URI（本身帶逗號）、後備清單（每一個都要是圖示字型）、引號、大小寫
    [InlineData("Segoe MDL2 Assets", true)]
    [InlineData("segoe mdl2 assets", true)]
    [InlineData("Segoe Fluent Icons", true)]
    [InlineData("Segoe UI Symbol", true)]
    [InlineData("Material Icons", true)]
    [InlineData("Material Icons Outlined", true)]
    [InlineData("Material Symbols Rounded", true)]
    [InlineData("Font Awesome 6 Free Solid", true)]
    [InlineData("FontAwesome", true)]
    [InlineData("/Fonts/#Material Symbols Sharp", true)]
    [InlineData("./Resources/Fonts/#Font Awesome 5 Free", true)]
    [InlineData("pack://application:,,,/MyApp;component/Fonts/#Font Awesome 6 Free Solid", true)]
    [InlineData("pack://application:,,,/Fonts/#Segoe Fluent Icons, Segoe MDL2 Assets", true)]
    [InlineData("Segoe Fluent Icons, Segoe MDL2 Assets", true)]
    [InlineData("'Segoe MDL2 Assets'", true)]
    [InlineData("Segoe UI", false)]
    [InlineData("Segoe UI Emoji", false)]                        // 不在清單裡就不算
    [InlineData("Segoe UI, Segoe MDL2 Assets", false)]          // 混了一般字型：可能有字，當文字
    [InlineData("Segoe MDL2 Assets, Segoe UI", false)]
    [InlineData("/Fonts/#Inter", false)]
    [InlineData("pack://application:,,,/MyApp;component/Fonts/#Inter", false)]
    [InlineData("{Binding IconFont}", false)]                    // 執行期才知道 → 當文字
    [InlineData("{StaticResource NoSuchFont}", false)]          // 找不到的資源 → 當文字
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IconFontFamiliesAreRecognised(string? value, bool icon) => Assert.Equal(icon, IconFonts.IsIconFont(value));

    [Fact] // FontFamily 資源：<FontFamily x:Key="K">值</FontFamily> 只解一層；值是圖示字型才算
    public void FontFamilyResourcesAreResolvedOneLevel()
    {
        var resources = new Dictionary<string, string>
        {
            ["SymbolFont"] = "Segoe Fluent Icons, Segoe MDL2 Assets",
            ["BodyFont"] = "Segoe UI",
            ["Indirect"] = "{StaticResource SymbolFont}",
        };
        Assert.True(IconFonts.IsIconFont("{StaticResource SymbolFont}", resources));
        Assert.True(IconFonts.IsIconFont("{DynamicResource SymbolFont}", resources));
        Assert.False(IconFonts.IsIconFont("{StaticResource BodyFont}", resources));
        Assert.False(IconFonts.IsIconFont("{StaticResource Indirect}", resources)); // 資源裡再引用資源：認不出來 → 當文字
        Assert.False(IconFonts.IsIconFont("{StaticResource SymbolFont}"));         // 沒有資源表
    }

    [Theory] // 每一種寫法都走到稽核：圖示是 3:1、isText false；同一組顏色的一般文字是 4.5:1、warn
    [InlineData("""<TextBlock FontFamily="Segoe MDL2 Assets" Foreground="#949494" Text="&#xE768;"/>""")]
    [InlineData("""<TextBlock FontFamily="/Fonts/#Material Symbols Rounded" Foreground="#949494" Text="x"/>""")]
    [InlineData("""<TextBlock FontFamily="pack://application:,,,/App;component/Fonts/#Font Awesome 6 Free Solid" Foreground="#949494" Text="x"/>""")]
    [InlineData("""<TextBlock FontFamily="{StaticResource SymbolFont}" Foreground="#949494" Text="x"/>""")]
    [InlineData("""<TextBlock TextElement.FontFamily="Segoe Fluent Icons" Foreground="#949494" Text="x"/>""")]
    [InlineData("""<TextBlock FontFamily="Segoe MDL2 Assets"><Run Foreground="#949494" Text="&#xE768;"/></TextBlock>""")]       // Run 繼承 TextBlock 的字型
    [InlineData("""<Button FontFamily="Segoe MDL2 Assets" Background="#FFFFFF"><TextBlock Foreground="#949494" Text="x"/></Button>""")]
    [InlineData("""<Button FontFamily="Segoe MDL2 Assets" Foreground="#949494" Content="&#xE710;"/>""")]
    public void TextInAnIconFontIsANonTextGraphicAtThreeToOne(string element)
    {
        var f = Single(element);
        Assert.False(f.IsText);
        Assert.False(f.Large);
        Assert.Equal(3.0, f.Need);
        Assert.Equal(3.03, f.RatioDark);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Theory] // 對照組：同一組顏色當文字 —— 沒有字型、一般字型、混了一般字型的後備清單、子元素改回一般字型、Binding、找不到的資源
    [InlineData("""<TextBlock Foreground="#949494" Text="plain"/>""")]
    [InlineData("""<TextBlock FontFamily="Segoe UI" Foreground="#949494" Text="plain"/>""")]
    [InlineData("""<TextBlock FontFamily="Segoe UI, Segoe MDL2 Assets" Foreground="#949494" Text="mixed"/>""")]
    [InlineData("""<TextBlock FontFamily="Segoe MDL2 Assets"><Run FontFamily="Segoe UI" Foreground="#949494" Text="text"/></TextBlock>""")]
    [InlineData("""<TextBlock FontFamily="{Binding IconFont}" Foreground="#949494" Text="?"/>""")]
    [InlineData("""<TextBlock FontFamily="{StaticResource NoSuchFont}" Foreground="#949494" Text="?"/>""")]
    public void TextInAnyOtherFontIsStillTextAtFourPointFive(string element)
    {
        var f = Single(element);
        Assert.True(f.IsText);
        Assert.Equal(4.5, f.Need);
        Assert.Equal(Category.Warn, f.Category);
    }

    [Theory] // 圖示照樣會被報：3:1 以下是 warn、3×0.67 以下是 fail —— 不是 decorative（那會不設門檻、整個消失）
    [InlineData("#B0B0B0", Category.Warn)]  // 2.15:1
    [InlineData("#D0D0D0", Category.Fail)]  // 1.56:1
    public void IconsBelowThreeToOneAreStillReported(string fg, Category expected)
    {
        var f = Single($"""<TextBlock FontFamily="Segoe MDL2 Assets" Foreground="{fg}" Text="&#xE768;"/>""");
        Assert.Equal(expected, f.Category);
        Assert.NotEqual(Category.Decorative, f.Category);
    }

    [Fact] // 模板的內容繼承的是套用它的地方的字型（不是定義處的祖先）：DataTemplate 裡的字當文字
    public void TemplateContentDoesNotInheritTheDefinitionSitesFont()
    {
        var f = Single("""
            <ContentControl FontFamily="Segoe MDL2 Assets">
              <ContentControl.ContentTemplate>
                <DataTemplate><Border Background="#FFFFFF"><TextBlock Foreground="#949494" Text="{Binding}"/></Border></DataTemplate>
              </ContentControl.ContentTemplate>
            </ContentControl>
            """);
        Assert.True(f.IsText);
        Assert.Equal(4.5, f.Need);
    }

    [Fact] // Style 的 Setter 設的字型：定義處（字與底都出自這個 Style）與使用處（元素自己的字色配 Style 的底）都認得
    public void IconFontFromAStyleSetterCounts()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style x:Key="IconBtn" TargetType="Button">
                  <Setter Property="FontFamily" Value="Segoe Fluent Icons"/>
                  <Setter Property="Background" Value="#FFFFFF"/>
                  <Setter Property="Foreground" Value="#949494"/>
                </Style>
                <Style x:Key="IconOnly" TargetType="Button">
                  <Setter Property="FontFamily" Value="Segoe Fluent Icons"/>
                  <Setter Property="Background" Value="#FFFFFF"/>
                </Style>
              </Window.Resources>
              <StackPanel>
                <Button Style="{StaticResource IconBtn}" Content="&#xE710;"/>
                <Button Style="{StaticResource IconOnly}" Foreground="#949494" Content="&#xE711;"/>
              </StackPanel>
            </Window>
            """);
        var r = fx.Run();
        var definition = Assert.Single(r.Findings, f => f.Element == "Style[IconBtn]/base");
        var use = Assert.Single(r.Findings, f => f.Element == "Button⊕Style[IconOnly]/base");
        foreach (var f in new[] { definition, use })
        {
            Assert.False(f.IsText);
            Assert.Equal(3.0, f.Need);
            Assert.Equal(Category.Ok, f.Category);
        }
    }

    [Fact] // JSON：isText false、threshold 3、isLargeText false（圖示不是大字級文字）；console 的標籤寫 non-text
    public void ReportsShowTheIconAsANonTextGraphic()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock FontFamily="Segoe MDL2 Assets" FontSize="32" Foreground="#B0B0B0" Text="&#xE768;"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(JsonDocument.Parse(Report.ToJson(r)).RootElement.GetProperty("findings").EnumerateArray());
        Assert.False(f.GetProperty("isText").GetBoolean());
        Assert.False(f.GetProperty("isLargeText").GetBoolean());
        Assert.Equal(3.0, f.GetProperty("threshold").GetDouble());
        Assert.Equal("warn", f.GetProperty("category").GetString());
        Assert.Contains("need 3 non-text", Report.ToConsole(r), StringComparison.Ordinal);
        Assert.Contains("| 3 (non-text) |", Report.ToMarkdown(r, 1), StringComparison.Ordinal);
    }

    /// <summary>白底 Grid 裡放一個元素（含一個 SymbolFont 資源），跑稽核、取唯一一筆 finding。</summary>
    private static Finding Single(string element)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <FontFamily x:Key="SymbolFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
              </Grid.Resources>
              {element}
            </Grid>
            """);
        var r = fx.Run();
        return Assert.Single(r.Findings);
    }
}

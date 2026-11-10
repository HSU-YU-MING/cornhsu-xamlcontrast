using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 1.4：非文字對比（WCAG 1.4.11），config 的 <c>nonText.uiComponents</c>，預設關。輸入控制項的框線或底色要跟周圍的底有 3:1，
/// 焦點態的框線與 FocusVisualStyle 的焦點框也是。
/// </summary>
public class UiComponentContrastTests
{
    private const string On = """{ "nonText": { "uiComponents": true } }""";

    private static AuditResult Run(Fixture fx, string? config = On)
    {
        if (config is not null) fx.File(ToolConfig.FileName, config);
        return Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root), ToolConfig.Load(fx.Root));
    }

    private static List<Finding> Components(AuditResult r) => [.. r.Findings.Where(f => f.Component is not null)];

    [Fact]
    public void OffByDefault()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBox BorderBrush="#E0E0E0" Foreground="#000000"/></Grid>""");

        var r = Run(fx, config: null);

        Assert.Empty(Components(r));
        Assert.Equal(0, r.UiComponentPairs);
    }

    [Fact]
    public void AFaintBorderOnTheBackgroundFailsAt3To1()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBox BorderBrush="#E0E0E0" Foreground="#000000"/></Grid>""");

        var r = Run(fx);

        var f = Assert.Single(Components(r));
        Assert.Equal("TextBox border/base", f.Element);
        Assert.Equal("border", f.Component);
        Assert.Equal("#E0E0E0", f.Fg);
        Assert.Equal(3.0, f.Need);
        Assert.False(f.IsText);
        Assert.Equal(Category.Fail, f.Category);
        Assert.Equal(Wcag.Contrast("#E0E0E0", "#FFFFFF"), f.RatioDark, 2);
        Assert.Equal(1, r.UiComponentPairs);
        Assert.Contains(r.Findings, x => x.Component is null && x.IsText); // 字照樣評
    }

    [Fact] // 條文要的是認得出控制項：底色夠就不必靠框線
    public void AContrastingFillIsEnough()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBox BorderBrush="#EEEEEE" Background="#767676" Foreground="#FFFFFF"/></Grid>""");

        var f = Assert.Single(Components(Run(fx)));

        Assert.Equal("fill", f.Component);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Fact]
    public void TheImplicitStyleAndItsFocusStateAreGraded()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Application.Resources>
              <Style TargetType="TextBox">
                <Setter Property="BorderBrush" Value="#CCCCCC"/>
                <Style.Triggers>
                  <Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="BorderBrush" Value="#0078D4"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="BorderBrush" Value="#F5F5F5"/></Trigger>
                </Style.Triggers>
              </Style>
            </Application.Resources></Application>
            """).File("Main.xaml", """<Grid Background="#FFFFFF"><TextBox Foreground="#000000"/></Grid>""");

        var r = Run(fx);

        var found = Components(r);
        Assert.Equal(2, found.Count);
        var bas = Assert.Single(found, f => f.Element == "TextBox border⊕Style[implicit TextBox]/base");
        Assert.Equal(Category.Fail, bas.Category);
        var focus = Assert.Single(found, f => f.Element == "TextBox border⊕Style[implicit TextBox]/focus");
        Assert.Equal("#0078D4", focus.Fg);
        Assert.Equal(Category.Ok, focus.Category);
        Assert.Equal(1, r.DisabledExempt); // 停用態：1.4.11 豁免，計數
    }

    [Fact]
    public void AnExplicitStyleReplacesTheImplicitOne()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><Application.Resources>
              <Style TargetType="TextBox"><Setter Property="BorderBrush" Value="#CCCCCC"/></Style>
              <Style x:Key="Strong" TargetType="TextBox"><Setter Property="BorderBrush" Value="#333333"/></Style>
            </Application.Resources></Application>
            """).File("Main.xaml", """<Grid Background="#FFFFFF"><TextBox Style="{StaticResource Strong}"/></Grid>""");

        var f = Assert.Single(Components(Run(fx)));

        Assert.Equal("TextBox border⊕Style[Strong]/base", f.Element);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Theory] // 沒有框線：BorderThickness 0、透明的框線；也沒有底 → 不評（系統主題畫的不猜）
    [InlineData("""BorderBrush="#E0E0E0" BorderThickness="0" """)]
    [InlineData("""BorderBrush="Transparent" """)]
    [InlineData("")]
    public void NoVisibleBorderOrFillIsNotGraded(string attrs)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""<Grid Background="#FFFFFF"><CheckBox {attrs} Foreground="#000000"/></Grid>""");

        var r = Run(fx);

        Assert.Empty(Components(r));
        Assert.Equal(0, r.UiComponentPairs);
    }

    [Fact] // 條文不要求滑鼠移過、按下看得出來；選取（IsChecked）要
    public void PointerStatesAreNotGradedButCheckedIs()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Application.Resources>
              <Style TargetType="RadioButton">
                <Setter Property="BorderBrush" Value="#333333"/>
                <Style.Triggers>
                  <Trigger Property="UIElement.IsMouseOver" Value="True"><Setter Property="BorderBrush" Value="#F0F0F0"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter Property="BorderBrush" Value="#F1F1F1"/></Trigger>
                  <Trigger Property="IsChecked" Value="True"><Setter Property="BorderBrush" Value="#F2F2F2"/></Trigger>
                </Style.Triggers>
              </Style>
            </Application.Resources></Application>
            """).File("Main.xaml", """<Grid Background="#FFFFFF"><RadioButton Foreground="#000000" Content="a"/></Grid>""");

        var found = Components(Run(fx));

        Assert.Equal(["#333333", "#F2F2F2"], found.Select(f => f.Fg).Order().ToArray());
        Assert.Contains(found, f => f.Element.EndsWith("/trigger") && f.Category == Category.Fail);
    }

    [Fact] // CelFlow 的形狀：ComboBox 的 Style 裡，下拉項目（ItemContainerStyle）與內部 ToggleButton 模板的觸發器是別的元素的狀態
    public void TriggersOfNestedStylesAndTemplatesAreNotTheControlsStates()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Application.Resources>
              <Style TargetType="ComboBox">
                <Setter Property="BorderBrush" Value="#333333"/>
                <Setter Property="Template"><Setter.Value>
                  <ControlTemplate TargetType="ComboBox"><Grid>
                    <ToggleButton><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border/>
                      <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter Property="BorderBrush" Value="#F0F0F0"/></Trigger></ControlTemplate.Triggers>
                    </ControlTemplate></ToggleButton.Template></ToggleButton>
                  </Grid></ControlTemplate>
                </Setter.Value></Setter>
                <Setter Property="ItemContainerStyle"><Setter.Value>
                  <Style TargetType="ComboBoxItem"><Style.Triggers>
                    <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#F1F1F1"/></Trigger>
                  </Style.Triggers></Style>
                </Setter.Value></Setter>
              </Style>
            </Application.Resources></Application>
            """).File("Main.xaml", """<Grid Background="#FFFFFF"><ComboBox Foreground="#000000"/></Grid>""");

        var f = Assert.Single(Components(Run(fx)));

        Assert.Equal("#333333", f.Fg);
        Assert.EndsWith("/base", f.Element);
    }

    [Fact] // 透明的框線就是沒有框線：底找不到也不記 unresolved
    public void ATransparentBorderWithoutABackgroundIsNotUnresolved()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid><TextBox BorderBrush="Transparent"/></Grid>""");

        var r = Run(fx);

        Assert.Empty(Components(r));
        Assert.DoesNotContain(r.UnresolvedSites, u => u.Value == "Transparent");
    }

    [Fact] // Margin 是負的焦點框畫在控制項外面：旁邊是周圍的底
    public void AFocusRingOutsideTheControlIsGradedAgainstTheSurroundings()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><Application.Resources>
              <Style x:Key="Fv"><Setter Property="Control.Template"><Setter.Value>
                <ControlTemplate><Border Margin="-3" BorderThickness="2" BorderBrush="#3A3A3A"/></ControlTemplate>
              </Setter.Value></Setter></Style>
            </Application.Resources></Application>
            """).File("Main.xaml", """<Grid Background="#FFFFFF"><Button Background="#202020" Foreground="#FFFFFF" FocusVisualStyle="{StaticResource Fv}" Content="OK"/></Grid>""");

        var f = Assert.Single(Components(Run(fx)));

        Assert.Equal("#FFFFFF", f.ColorsDark!.Bg);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Fact] // wpfui 的形狀：Style 的 Background 是強調色，模板只拿它畫圓點、根是 Transparent —— 焦點框旁邊是周圍的底
    public void AColourHardCodedOnTheTemplateRootWinsOverTheStylesSetter()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><Application.Resources>
              <Style x:Key="Fv"><Setter Property="Control.Template"><Setter.Value>
                <ControlTemplate><Rectangle Stroke="#202020"/></ControlTemplate>
              </Setter.Value></Setter></Style>
              <Style TargetType="RadioButton">
                <Setter Property="FocusVisualStyle" Value="{DynamicResource Fv}"/>
                <Setter Property="Background" Value="#1A1A1A"/>
                <Setter Property="Template"><Setter.Value>
                  <ControlTemplate TargetType="RadioButton"><Grid Background="Transparent"><Ellipse Fill="{TemplateBinding Background}"/></Grid></ControlTemplate>
                </Setter.Value></Setter>
              </Style>
            </Application.Resources></Application>
            """).File("Main.xaml", """<Grid Background="#FFFFFF"><RadioButton Foreground="#000000" Content="a"/></Grid>""");

        var f = Assert.Single(Components(Run(fx)));

        Assert.Equal("focus-visual", f.Component);  // 底色是 Transparent（沒有），只剩焦點框
        Assert.Equal("#FFFFFF", f.ColorsDark!.Bg);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Fact]
    public void AFocusVisualStyleStrokeIsGradedAgainstTheControlsFill()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><Application.Resources>
              <Style x:Key="Fv"><Setter Property="Control.Template"><Setter.Value>
                <ControlTemplate><Rectangle Margin="2" Stroke="#3A3A3A" StrokeDashArray="1 2"/></ControlTemplate>
              </Setter.Value></Setter></Style>
            </Application.Resources></Application>
            """).File("Main.xaml", """<Grid Background="#FFFFFF"><Button Background="#202020" Foreground="#FFFFFF" FocusVisualStyle="{StaticResource Fv}" Content="OK"/></Grid>""");

        var f = Assert.Single(Components(Run(fx)));

        Assert.Equal("Button focus-visual/focus", f.Element);
        Assert.Equal("#3A3A3A", f.Fg);
        Assert.Equal("#202020", f.ColorsDark!.Bg); // 焦點框旁邊是按鈕自己的底，不是頁面的白
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact]
    public void BothThemesAreGraded()
    {
        using var fx = new Fixture();
        const string Theme = """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="PageBg" Color="{0}"/>
              <SolidColorBrush x:Key="Line" Color="{1}"/>
              <SolidColorBrush x:Key="Fg" Color="{2}"/>
            </ResourceDictionary>
            """;
        fx.File("Themes/Dark.xaml", string.Format(Theme, "#202020", "#9A9A9A", "#FFFFFF"))
          .File("Themes/Light.xaml", string.Format(Theme, "#FFFFFF", "#E5E5E5", "#000000"))
          .File("Main.xaml", """<Grid Background="{DynamicResource PageBg}"><ComboBox BorderBrush="{DynamicResource Line}" Foreground="{DynamicResource Fg}"/></Grid>""");

        var f = Assert.Single(Components(Run(fx)));

        Assert.Equal(Wcag.Contrast("#9A9A9A", "#202020"), f.RatioDark, 2);
        Assert.Equal(Wcag.Contrast("#E5E5E5", "#FFFFFF"), f.RatioLight, 2);
        Assert.Equal(Symmetry.LightFails, f.Symmetry);
    }

    [Fact] // 看不懂的那一個可能就是認得出控制項的那一個：評得到的不夠時不下結論
    public void AnUnreadableIndicatorIsUnresolvedWhenTheOtherOneFails()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBox BorderBrush="{DynamicResource Nowhere}" Background="#FAFAFA" Foreground="#000000"/></Grid>""");

        var r = Run(fx);

        Assert.Empty(Components(r));
        Assert.Contains(r.UnresolvedSites, u => u.Reason == UnresolvedReason.UnknownPaletteKey && u.Value == "Nowhere");
    }

    [Fact]
    public void TheJsonReportCarriesTheComponentAndTheCount()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBox BorderBrush="#E0E0E0" Foreground="#000000"/></Grid>""");

        var root = JsonDocument.Parse(Report.ToJson(Run(fx))).RootElement;

        Assert.Equal(1, root.GetProperty("summary").GetProperty("uiComponentPairs").GetInt32());
        var comps = root.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("component")).ToList();
        Assert.Contains(comps, c => c.ValueKind == JsonValueKind.String && c.GetString() == "border");
        Assert.Contains(comps, c => c.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void ANullSectionIsAConfigError()
    {
        using var fx = new Fixture();
        fx.File(ToolConfig.FileName, """{ "nonText": null }""");

        Assert.Contains("nonText", Assert.Throws<ConfigException>(() => ToolConfig.Load(fx.Root)).Message);
    }
}

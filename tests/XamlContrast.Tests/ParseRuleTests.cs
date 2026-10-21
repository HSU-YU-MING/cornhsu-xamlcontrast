using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>樹走訪那一層的解析規則各一個最小案例（規劃書 4.1，共十六條；
/// 樣式與外部專案實查出來的幾條在 NewRuleTests）。抄公式的實作會全部漏掉它們。</summary>
public class ParseRuleTests
{
    [Fact] // 規則 1：祖先繼承
    public void BackgroundInheritsFromAncestor()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <StackPanel>
                <TextBlock Foreground="#FFFFFF" Text="x"/>
              </StackPanel>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal(21.0, f.RatioDark);
        Assert.Equal("#000000", f.Bg);
    }

    [Fact] // 規則 2：Transparent 穿透
    public void TransparentBackgroundIsPassthrough()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <Border Background="Transparent">
                <TextBlock Foreground="#FFFFFF" Text="x"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal(21.0, f.RatioDark); // 穿透到最外層的黑，不是 Transparent 本身
    }

    [Fact] // 規則 3：半透明 ARGB 背景與下層合成
    public void AlphaBackgroundCompositesWithLayerBelow()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border Background="#80000000">
                <TextBlock Foreground="#000000" Text="x"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        var effectiveBg = Wcag.Composite("#000000", 0x80 / 255.0, "#FFFFFF");
        Assert.Equal(Wcag.Contrast("#000000", effectiveBg), f.RatioDark);
    }

    [Fact] // 規則 4：Opacity 沿樹累乘（0.5 × 0.5 = 0.25）
    public void OpacityAccumulatesDownTheTree()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <Border Opacity="0.5">
                <TextBlock Opacity="0.5" Foreground="#FFFFFF" Text="x"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        var effectiveFg = Wcag.Composite("#FFFFFF", 0.25, "#000000");
        Assert.Equal(Wcag.Contrast(effectiveFg, "#000000"), f.RatioDark);
        Assert.Contains("×0.25", f.Fg);
    }

    [Fact] // 規則 4 附：Opacity 0 是淡入動畫的初始狀態，跳過而不是報 1:1
    public void OpacityZeroIsSkippedNotReported()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Opacity="0" Foreground="#000000" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.Skipped);
    }

    [Theory] // 透明／半透明的字色（裝飾元素的 Fill 同理）是合法跳過 —— 但要計數，不是從報告消失；
             // 也不能塞進 unresolved（那是「工具看不懂」的桶，這裡工具看得懂）
    [InlineData("""<TextBlock Foreground="Transparent" Text="x"/>""")]
    [InlineData("""<TextBlock Foreground="#80FFFFFF" Text="x"/>""")]
    [InlineData("""<Rectangle Fill="Transparent"/>""")]
    public void TransparentOrTranslucentForegroundIsSkippedAndCounted(string element)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#000000">
              {element}
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal(0, r.Pairs);
        Assert.Equal(1, r.Skipped);
        Assert.Equal(0, r.Unresolved);
    }

    [Fact] // 規則 5：ControlTemplate 自成子樹（模板根的背景管到模板內的文字）
    public void ControlTemplateRootGovernsItsSubtree()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FF0000">
              <Button>
                <Button.Template>
                  <ControlTemplate>
                    <Border Background="#000000">
                      <TextBlock Foreground="#FFFFFF" Text="x"/>
                    </Border>
                  </ControlTemplate>
                </Button.Template>
              </Button>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("#000000", f.Bg); // 模板根的黑，不是外面的紅
    }

    [Fact] // 規則 6：Style 的 Background+Foreground 兩個 Setter 是一組配對；單邊不成對
    public void StyleSetterPairIsAudited()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Paired" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                </Style>
                <Style x:Key="OneSided" TargetType="Button">
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings); // 單邊那個不在樹上，套用位置由型別決定 → 不猜
        Assert.Equal("Style[Paired]/base", f.Element);
        Assert.Equal(21.0, f.RatioDark);
    }

    [Fact] // 規則 7：Trigger 內的 Setter 以所屬 Style 的另一半為對照
    public void TriggerSetterPairsWithStyleBase()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="S" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Foreground" Value="#404040"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(2, r.Findings.Count); // 基礎 + 觸發態
        var trigger = Assert.Single(r.Findings, f => f.Element == "Style[S]/trigger");
        Assert.Equal(Wcag.Contrast("#404040", "#000000"), trigger.RatioDark); // 觸發字 × 基礎底
    }

    [Fact] // 規則 8：模板無 Foreground 消費者 → 死 setter，整組排除並計數
    public void DeadForegroundSetterIsExcludedAndCounted()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Dead" TargetType="CheckBox">
                  <Setter Property="Background" Value="#FFFFFF"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="CheckBox">
                        <Border Background="#FFFFFF">
                          <Path Data="M 2,8 L 6,12 L 14,4" Stroke="#FFFFFF"/>
                        </Border>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
                <Style x:Key="Alive" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border><ContentPresenter/></Border>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        // Dead 的白配白 1:1 不報（Foreground 不會被渲染），但排除要計數 —— 過濾不靜默
        Assert.Equal(1, r.DeadForeground);
        var f = Assert.Single(r.Findings);
        Assert.Equal("Style[Alive]/base", f.Element);
    }

    [Fact] // 不猜比猜錯好：Binding 底色不能往上繼承祖先背景
    public void BindingBackgroundBlocksAncestorInheritance()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border Background="{Binding UserColor}">
                <TextBlock Foreground="#FFFFFF" Text="x"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        // 白字疊使用者選的底色 —— 繼承祖先會誤判成白配白 1:1。標無法解析，不猜。
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.Unresolved);
    }

    // ── 屬性元素語法（<Border.Background>…</Border.Background>）──
    // 舊版只讀屬性：漸層底被忽略、文字配到祖先的底，給出篤定的 21:1 ok；
    // <TextBlock.Foreground> 那組更是整組消失（pairs、unresolved、skipped 全是 0）

    [Fact]
    public void PropertyElementGradientBackgroundIsUnresolvedNotTheAncestor()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <Border>
                <Border.Background>
                  <LinearGradientBrush><GradientStop Color="#FFFFFF" Offset="0"/><GradientStop Color="#EEEEEE" Offset="1"/></LinearGradientBrush>
                </Border.Background>
                <TextBlock Foreground="#FFFFFF" Text="x"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings); // 不是祖先的黑 → 21:1
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);
    }

    [Fact]
    public void PropertyElementSolidForegroundIsAudited()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Text="x">
                <TextBlock.Foreground><SolidColorBrush Color="#EEEEEE"/></TextBlock.Foreground>
              </TextBlock>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("#EEEEEE", f.Fg);
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // 屬性元素裡的 SolidColorBrush 帶 Opacity：字面色折成 #AARRGGBB，走半透明合成
    public void PropertyElementBrushOpacityIsComposited()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border>
                <Border.Background><SolidColorBrush Color="#000000" Opacity="0.8"/></Border.Background>
                <TextBlock Foreground="#000000" Text="x"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("{#CC000000 over #FFFFFF}", f.Bg); // 80% 黑疊白 = #333333
        Assert.Equal(1.66, f.RatioDark);
    }

    [Fact] // 屬性元素是 Binding／資源引用以外的寫法（資源鍵 + Opacity）：靜態可知的部分才算，其餘誠實計入 unresolved
    public void PropertyElementResourceBrushWithOpacityIsUnresolved()
    {
        using var fx = new Fixture().WithPalette();
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <Border>
                <Border.Background><SolidColorBrush Color="{DynamicResource Fg}" Opacity="0.5"/></Border.Background>
                <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
              </Border>
              <Border>
                <Border.Background><DynamicResource ResourceKey="Dim"/></Border.Background>
                <TextBlock Foreground="{DynamicResource Fg}" Text="y"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);
        var f = Assert.Single(r.Findings); // 元素語法的 <DynamicResource ResourceKey=…/> 照常解析
        Assert.Equal("{Dim}", f.Bg);
    }

    [Fact] // Style 的 <Setter.Value> 屬性元素（同一個根因：只讀 Value 屬性，整組 Setter 被靜默丟掉）
    public void SetterValuePropertyElementIsRead()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid>
              <Grid.Resources>
                <Style x:Key="Pale" TargetType="Button" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Setter Property="Background"><Setter.Value><SolidColorBrush Color="#FFFFFF"/></Setter.Value></Setter>
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("Style[Pale]/base", f.Element);
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // 大字級門檻：WPF FontSize 是 DIP，要 ×0.75 換 point
    public void LargeTextThresholdUsesDipConversion()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock FontSize="24" Foreground="#FFFFFF" Text="24px = 18pt，大字級"/>
              <TextBlock FontSize="18" Foreground="#FFFFFF" Text="18px = 13.5pt，不是大字級"/>
            </Grid>
            """);
        var r = fx.Run();
        var large = Assert.Single(r.Findings, f => f.Size == "24px");
        var normal = Assert.Single(r.Findings, f => f.Size == "18px");
        Assert.True(large.Large);
        Assert.Equal(3.0, large.Need);
        // ⚠ 少了 ×0.75 就會把 18px 誤判成大字級而放過它
        Assert.False(normal.Large);
        Assert.Equal(4.5, normal.Need);
    }
}

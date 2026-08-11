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

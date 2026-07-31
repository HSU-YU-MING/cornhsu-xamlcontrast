using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>4929b69 三條新規則的最小案例：具名 Style 底色、停用態豁免、半透明色票合成。</summary>
public class NewRuleTests
{
    [Fact] // 具名 Style 的底色：元素的字配 Style 來源的底，不誤落祖先
    public void NamedStyleBackgroundIsResolvedNotAncestor()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FF0000">
              <Grid.Resources>
                <Style x:Key="DarkBtn" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource DarkBtn}" Foreground="#FFFFFF" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings, x => x.Element.Contains("⊕"));
        Assert.Equal("#000000", f.Bg);   // Style 的黑，不是祖先的紅
        Assert.Equal(21.0, f.RatioDark);
    }

    [Fact] // BasedOn 鏈＋hover 觸發態：基礎與觸發各檢一次（CelFlow 刪除鈕實證形狀）
    public void BasedOnChainAndHoverTriggerAreCheckedPerState()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="BaseBtn" TargetType="Button">
                  <Setter Property="Background" Value="#303030"/>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Background" Value="#404040"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
                <Style x:Key="DangerBtn" TargetType="Button" BasedOn="{StaticResource BaseBtn}"/>
              </Grid.Resources>
              <Button Style="{StaticResource DangerBtn}" Foreground="#E24B4A" Content="刪除"/>
            </Grid>
            """);
        var r = fx.Run();
        // 基礎（#303030）＋ hover（#404040）兩個狀態都要檢 —— 舊版整組漏掉或誤落祖先
        Assert.Equal(2, r.Findings.Count(x => x.Element.Contains("⊕")));
        Assert.Contains(r.Findings, x => x.Bg == "#303030");
        Assert.Contains(r.Findings, x => x.Bg == "#404040");
    }

    [Fact] // 停用態豁免：IsEnabled=False 觸發不評分，但要計數 —— 放行憑據是 WCAG 1.4.3
    public void DisabledStateIsExemptedAndCounted()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Style.Triggers>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Foreground" Value="#303030"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.DisabledExempt);                       // 豁免要計數，不是靜默消失
        var f = Assert.Single(r.Findings);                        // 只剩基礎態（WalkStyles）
        Assert.Equal(Category.Ok, f.Category);
        Assert.Contains("exempted 1 disabled-state", Report.ToConsole(r));
    }

    [Fact] // 半透明色票：#AARRGGBB 的 palette 鍵要疊底合成，不能剝掉 alpha 當不透明色
    public void TranslucentPaletteKeyCompositesOverBackground()
    {
        using var fx = new Fixture();
        // BlueOverlay 形狀：16% 藍疊在深底上，實際接近深底而不是純藍
        fx.File("Styles/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="BlueOverlay" Color="#29378ADD"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <Grid Background="{DynamicResource BlueOverlay}">
                <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
              </Grid>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        var effective = Wcag.Composite("#378ADD", 0x29 / 255.0, "#111111");
        Assert.Equal(Wcag.Contrast("#EEEEEE", effective), f.RatioDark); // 疊底合成
        Assert.NotEqual(Wcag.Contrast("#EEEEEE", "#378ADD"), f.RatioDark); // 不是純藍
    }

    [Fact] // 分工：字與底同出一個 Style 節點 → WalkStyles 定義處已檢，元素端不重複
    public void SameStyleNodePairIsNotDoubleCounted()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="S" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource S}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings); // 只有 WalkStyles 那筆，元素端跳過
        Assert.StartsWith("Style[", f.Element);
    }
}

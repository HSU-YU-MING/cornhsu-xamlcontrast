using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 元素 × Style 鏈的逐狀態路徑（Auditor.Walk 的 chain 分支）。元素一套用 Style，Foreground
/// 就整個改由這條路接手 —— 所以樹走訪的每一條規則（祖先底、半透明合成、Opacity、看不懂要計數）
/// 在這裡都要再成立一次。「改一處忘了另一處」是這個 repo 反覆出現的回歸形狀。
/// </summary>
public class StyleChainTests
{
    // Pad：不碰顏色的 Style（只為了讓元素走鏈路徑）；Dark：只給底色
    private const string PadStyle = """
        <Grid.Resources>
          <Style x:Key="Pad" TargetType="Button">
            <Setter Property="Padding" Value="4"/>
          </Style>
          <Style x:Key="Dark" TargetType="Button">
            <Setter Property="Background" Value="#000000"/>
          </Style>
        </Grid.Resources>
        """;

    private static AuditResult RunMain(string body, string background = "#000000")
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{background}">
            {PadStyle}
              {body}
            </Grid>
            """);
        return fx.Run();
    }

    [Fact] // 具名 Style 的查找：同檔優先，其次全域字典（ResourceDictionary／Application 裡的 Style）——
           // CelFlow 的對話框有自己的 DarkBtn，與主題檔的全域 DarkBtn 同名不同值
    public void NamedStyleIsFoundInDictionariesButSameFileWins()
    {
        using var fx = new Fixture();
        fx.File("Themes/Styles.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style x:Key="Card" TargetType="Button">
                <Setter Property="Background" Value="#000000"/>
              </Style>
            </ResourceDictionary>
            """);
        fx.File("Views/UsesGlobal.xaml", """
            <Grid Background="#FFFFFF">
              <Button Style="{StaticResource Card}" Foreground="#FFFFFF" Content="global Card"/>
            </Grid>
            """);
        fx.File("Views/HasOwn.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Card" TargetType="Button">
                  <Setter Property="Background" Value="#333333"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Card}" Foreground="#FFFFFF" Content="local Card"/>
            </Grid>
            """);
        var r = fx.Run();
        var global = Assert.Single(r.Findings, f => f.File == "Views/UsesGlobal.xaml");
        Assert.Equal("Button⊕Style[Card]/base", global.Element);
        Assert.Equal("#000000", global.Bg);
        var own = Assert.Single(r.Findings, f => f.File == "Views/HasOwn.xaml");
        Assert.Equal("#333333", own.Bg);
    }

    [Fact] // 行內 Style（<Button.Style><Style>…）也是鏈：沒有 BasedOn 標 Style[inline]，有 BasedOn 標 Style[→基底]
    public void InlineStyleElementIsResolved()
    {
        var r = RunMain("""
            <Button Foreground="#FFFFFF" Content="inline">
              <Button.Style>
                <Style TargetType="Button"><Setter Property="Background" Value="#000000"/></Style>
              </Button.Style>
            </Button>
            <Button Foreground="#FFFFFF" Content="inline, based on">
              <Button.Style>
                <Style TargetType="Button" BasedOn="{StaticResource Dark}"/>
              </Button.Style>
            </Button>
            """, background: "#FFFFFF");
        Assert.Equal("#000000", Assert.Single(r.Findings, f => f.Element == "Button⊕Style[inline]/base").Bg);
        Assert.Equal("#000000", Assert.Single(r.Findings, f => f.Element == "Button⊕Style[→Dark]/base").Bg);
        Assert.DoesNotContain(r.Findings, f => f.Bg == "#FFFFFF"); // 不是誤落祖先的白
    }

    [Fact] // Style 鏈沒有給底 → 退回祖先背景（不是 no-ancestor-background）
    public void ChainWithoutBackgroundFallsBackToTheAncestor()
    {
        var r = RunMain("""<Button Style="{StaticResource Pad}" Foreground="#FFFFFF" Content="x"/>""");
        var f = Assert.Single(r.Findings);
        Assert.Equal("Button⊕Style[Pad]/base", f.Element);
        Assert.Equal("#000000", f.Bg);
        Assert.Equal(0, r.Unresolved);
    }

    [Fact] // ROADMAP「Style＋半透明底的逐狀態路徑重複合成修正」：半透明底只能疊在「更新前」的祖先底上一次。
           // 用已經合成過的底再合成一次，等於疊兩層（50% 白疊黑 = #808080，疊兩次就變 #C0C0C0）
    public void ChainTranslucentBackgroundIsCompositedOnce()
    {
        var r = RunMain("""<Button Style="{StaticResource Pad}" Background="#80FFFFFF" Foreground="#000000" Content="x"/>""");
        var f = Assert.Single(r.Findings);
        var once = Wcag.Composite("#FFFFFF", 0x80 / 255.0, "#000000");
        Assert.Equal(Wcag.Contrast("#000000", once), f.RatioDark);
        Assert.Equal("{#80FFFFFF over #000000}", f.Bg);
    }

    [Fact] // Opacity 在鏈路徑一樣要把字色疊到底上（CelFlow 提示文字 Opacity=0.3 的盲區）
    public void ChainAppliesAccumulatedOpacity()
    {
        var r = RunMain("""
            <Border Opacity="0.5">
              <Button Style="{StaticResource Pad}" Opacity="0.5" Foreground="#FFFFFF" Content="x"/>
            </Border>
            """);
        var f = Assert.Single(r.Findings);
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#FFFFFF", 0.25, "#000000"), "#000000"), f.RatioDark);
    }

    [Fact] // 鏈路徑上看不懂的字色（Binding）→ unresolved，不是 skipped、更不是消失
    public void ChainUnresolvableForegroundIsCountedAsUnresolved()
    {
        var r = RunMain("""<Button Style="{StaticResource Pad}" Foreground="{Binding Accent}" Content="x"/>""");
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);
        Assert.Equal(0, r.Skipped);
    }

    [Fact] // 同條件的觸發器跨節點合併（StyleIndex.MergeSameCondition）：衍生 Style 的 style trigger 設字色、
           // 基底模板的 template trigger 設模板根的底 —— 是同一個 hover 狀態。不合併的話會拆成
           // 「基礎字色 × hover 底」與「hover 字色 × 模板根底」兩個各缺一半的幻影組合，各報一筆假 fail
    public void SameConditionTriggersAcrossStylesMergeIntoOneState()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Base" TargetType="Button">
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border x:Name="root" Background="#EEEEEE"><ContentPresenter/></Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="root" Property="Background" Value="#000000"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
                <Style x:Key="Derived" TargetType="Button" BasedOn="{StaticResource Base}">
                  <Setter Property="Foreground" Value="#111111"/>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Foreground" Value="#FFFFFF"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Derived}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(2, r.Findings.Count);
        Assert.Contains(r.Findings, f => f.Element == "Button⊕Style[Derived]/base" && f.Fg == "#111111" && f.Bg == "#EEEEEE");
        Assert.Contains(r.Findings, f => f.Element == "Button⊕Style[Derived]/trigger" && f.Fg == "#FFFFFF" && f.Bg == "#000000");
        Assert.Equal(0, r.CountOf(Category.Fail));
    }

    [Fact] // 合併時的優先序：style trigger 勝過 template trigger —— template trigger 只補 style trigger 沒設的屬性
    public void TemplateTriggerOnlyFillsWhatTheStyleTriggerLeftUnset()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Base" TargetType="Button">
                  <Setter Property="Foreground" Value="#111111"/>
                  <Setter Property="Background" Value="#EEEEEE"/>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Foreground" Value="#FFFFFF"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
                <Style x:Key="Retemplated" TargetType="Button" BasedOn="{StaticResource Base}">
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}"><ContentPresenter/></Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground" Value="#222222"/>
                            <Setter Property="Background" Value="#000000"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Retemplated}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        // 只看元素端的鏈路徑那筆（WalkStyles 在 Style 定義處各自另報，不在這條的範圍）
        var hover = Assert.Single(r.Findings, f => f.Element == "Button⊕Style[Retemplated]/trigger");
        Assert.Equal("#FFFFFF", hover.Fg);   // style trigger 的字色，不是 template trigger 的 #222222
        Assert.Equal("#000000", hover.Bg);   // 底由 template trigger 補上
    }
}

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

    [Theory] // 字與底出自同一個 Style、但底是透明／半透明：定義處沒有底下那層可疊（只能記 skipped），
             // 鏈路徑又看到「同源」就略過、交給定義處 —— 這組在哪裡都沒被評分，也不在覆蓋率裡。
             // 透明／半透明的底要在使用處對著祖先的底評
    [InlineData("Transparent", "#FFFFFF")]
    [InlineData("#80000000", "{#80000000 over #FFFFFF}")]
    public void SameSourceStylePairWithASeeThroughBackgroundIsGradedWhereUsed(string background, string expectedBg)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $$"""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="SeeThrough" TargetType="Button">
                  <Setter Property="Background" Value="{{background}}"/>
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource SeeThrough}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("Button⊕Style[SeeThrough]/base", f.Element);
        Assert.Equal(expectedBg, f.Bg);
        Assert.Equal(1, r.Skipped); // 定義處那一筆照舊記 skipped（那裡沒有底可疊）
    }

    [Fact] // 同上，祖先的底是 Binding：unresolved 點名的是卡住的祖先底，不是只負責穿透的 "Transparent"
    public void SeeThroughStyleOverABoundBackgroundNamesTheBoundValue()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="SeeThrough" TargetType="Button">
                  <Setter Property="Background" Value="Transparent"/>
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                </Style>
              </Grid.Resources>
              <Border Background="{Binding NoteColour}"><Button Style="{StaticResource SeeThrough}" Content="x"/></Border>
            </Grid>
            """);
        var site = Assert.Single(fx.Run().UnresolvedSites);
        Assert.Equal(UnresolvedReason.BoundOrGradient, site.Reason);
        Assert.Equal("{Binding NoteColour}", site.Value);
    }

    // ── Style 定義處（WalkStyles）也要合併同條件的觸發器、看 BasedOn ──
    //    舊版每個觸發器各自跟基礎值合併：style trigger 設字色、同條件的 template trigger 設模板根的底，
    //    被拆成兩個各缺一半的幻影組合各報一筆假 fail（實際 hover 是 21:1）；衍生 Style 重新套模板時，
    //    模板觸發器的底被配上衍生 Style 自己的字色單獨評分，看不到基底 style trigger 在同一狀態蓋過的字色

    [Fact]
    public void DefinitionSiteMergesSameConditionTriggers()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Background" Value="#EEEEEE"/>
                  <Setter Property="Foreground" Value="#111111"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border x:Name="root" Background="{TemplateBinding Background}"><ContentPresenter/></Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="root" Property="Background" Value="#000000"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Foreground" Value="#FFFFFF"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.CountOf(Category.Fail)); // 沒有「#FFFFFF 疊 #EEEEEE」「#111111 疊 #000000」兩個幻影
        Assert.Equal(2, r.Findings.Count);
        Assert.Contains(r.Findings, f => f.Element == "Style[Btn]/base" && f.Fg == "#111111" && f.Bg == "#EEEEEE");
        Assert.Contains(r.Findings, f => f.Element == "Style[Btn]/trigger" && f.Fg == "#FFFFFF" && f.Bg == "#000000");
    }

    [Fact]
    public void DefinitionSiteOfARetemplatedDerivedStyleSeesTheInheritedStyleTrigger()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
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
            </Grid>
            """);
        var r = fx.Run();
        // hover 的實際字色是基底 style trigger 的 #FFFFFF（style trigger 勝過 template trigger），不是 #222222；
        // 這個 hover 狀態的字來自 Base、底來自 Retemplated —— 跨來源的組合由使用處評，定義處不另報
        Assert.DoesNotContain(r.Findings, f => f.Fg == "#222222");
        Assert.DoesNotContain(r.Findings, f => f.Element.StartsWith("Style[Retemplated]"));
        // Base 自己（沒被換模板時）的 hover 是 #FFFFFF 疊 #EEEEEE —— 那是真的，照報
        Assert.Equal(Category.Fail, Assert.Single(r.Findings, f => f.Element == "Style[Base]/trigger").Category);
    }

    // ── 鏈路徑上「評不了」的每一種情況都要落進對的桶：樹走訪那邊有的計數紀律，這條路一樣要有 ──
    //    之前 Style 配對評不了是裸 continue，配對從報告徹底消失、連計數都沒有（NewRuleTests 那條的教訓）

    [Theory]
    [InlineData("""<Button Style="{StaticResource Pad}" Foreground="#FFFFFF" Content="x"/>""", "Transparent", "no-ancestor-background")]
    [InlineData("""<Button Style="{StaticResource Pad}" Background="#80FFFFFF" Foreground="#000000" Content="半透明底、底下沒東西"/>""", "Transparent", "no-ancestor-background")]
    [InlineData("""<Button Style="{StaticResource Pad}" Foreground="#FFFFFF" Content="祖先底綁執行期"/>""", "{Binding WindowBg}", "bound-or-gradient")]
    [InlineData("""<Button Style="{StaticResource Pad}" Background="{Binding Fill}" Foreground="#FFFFFF" Content="自己的底綁執行期"/>""", "#000000", "bound-or-gradient")]
    [InlineData("""<Button Style="{StaticResource Pad}" Background="{DynamicResource Nope}" Foreground="#FFFFFF" Content="底是死引用"/>""", "#000000", "unknown-palette-key")]
    [InlineData("""<Image Source="a.png"/><Button Style="{StaticResource Pad}" Foreground="#FFFFFF" Content="疊在兄弟圖片上"/>""", "#000000", "over-sibling-content")]
    [InlineData("""<Button Style="{StaticResource Pad}" Opacity="0" Foreground="#FFFFFF" Content="淡入動畫的初始態"/>""", "#000000", "skipped")]
    [InlineData("""<Button Style="{StaticResource Pad}" Foreground="Transparent" Content="透明字"/>""", "#000000", "skipped")]
    public void ChainPairsThatCannotBeGradedAreCountedInTheirBucket(string body, string background, string bucket)
    {
        var r = RunMain(body, background);
        Assert.Empty(r.Findings);
        Assert.Equal(0, r.Pairs);
        if (bucket == "skipped")
        {
            Assert.Equal(1, r.Skipped);
            Assert.Equal(0, r.Unresolved);
        }
        else
        {
            Assert.Equal(0, r.Skipped);
            Assert.Equal(bucket, Report.ReasonLabel(Assert.Single(r.UnresolvedSites).Reason));
        }
    }

    [Theory] // Style 引用解不到（打錯鍵、{x:Null}）→ 沒有鏈可走，元素回到樹走訪照常稽核 ——
             // 不能因為「有寫 Style 屬性」就把 Foreground 交給一條不存在的鏈、整組消失
    [InlineData("""Style="{StaticResource Missing}" """)]
    [InlineData("""Style="{x:Null}" """)]
    public void UnresolvableStyleReferenceFallsBackToTheTreeWalk(string style)
    {
        var r = RunMain($"""<Button {style} Foreground="#FFFFFF" Content="x"/>""");
        var f = Assert.Single(r.Findings);
        Assert.Equal("Button", f.Element);
        Assert.Equal("#000000", f.Bg);
    }

    [Fact] // ignore 註解壓得住「套了 Style 的元素」—— 鏈路徑接手 Foreground 之後，壓抑與計數也要跟著接手
    public void IgnoreCommentSuppressesAStyledElementAndCounts()
    {
        var r = RunMain("""
            <!-- xamlcontrast-ignore: 品牌色按鈕，設計確認過 -->
            <Button Style="{StaticResource Dark}" Foreground="#111111" Content="被壓掉"/>
            <Button Style="{StaticResource Dark}" Foreground="#222222" Content="照報"/>
            """);
        Assert.Equal(2, r.Pairs);
        Assert.Equal(1, r.Suppressed);
        var f = Assert.Single(r.Findings);
        Assert.Equal("#222222", f.Fg);
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // 停用態豁免在鏈路徑也成立（元素自帶字色、Style 的 IsEnabled=False 觸發換底），而且要計數；
           // 豁免範圍不能順便吃掉其他觸發態 —— hover 照常評分
    public void DisabledTriggerOnTheChainIsExemptedAndCounted()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#000000">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Style.Triggers>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Background" Value="#FFFFFF"/>
                    </Trigger>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Background" Value="#EEEEEE"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Btn}" Foreground="#FFFFFF" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.DisabledExempt);
        Assert.Equal(2, r.Findings.Count);                                 // 基礎態 + hover
        Assert.DoesNotContain(r.Findings, f => f.Bg == "#FFFFFF");         // 停用態不評分（白字白底）
        var hover = Assert.Single(r.Findings, f => f.Element == "Button⊕Style[Btn]/trigger");
        Assert.Equal("#EEEEEE", hover.Bg);
        Assert.Equal(Category.Fail, hover.Category);
    }

    [Fact] // 主題字典（色盤檔，不進 UI 掃描）裡字與底兩個 Setter 都有的 Style：鏈路徑看到「字與底出自同一個
           // Style 節點」就略過，假設 WalkStyles 會在定義處檢查 —— 但色盤檔從來沒被 WalkStyles 走到，
           // 這組 1.2:1 的按鈕不在 findings、也不在任何計數器裡（CelFlow 主題檔裡放按鈕 Style 是實際寫法）
    public void StyleDefinedInAThemeDictionaryIsAuditedAtItsDefinition()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Dim" Color="#222222"/>
              <Style x:Key="DimBtn" TargetType="Button">
                <Setter Property="Background" Value="{DynamicResource Bg}"/>
                <Setter Property="Foreground" Value="{DynamicResource Dim}"/>
              </Style>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Fg" Color="#212121"/>
              <SolidColorBrush x:Key="Dim" Color="#E8E8E8"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="ok"/>
              <Button Style="{StaticResource DimBtn}" Content="1.2:1"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Contains(r.Detection.ExcludedFiles, f => f.EndsWith("Dark.xaml")); // 前提：主題檔不進 UI 掃描
        var btn = Assert.Single(r.Findings, f => f.Element == "Style[DimBtn]/base");
        Assert.Equal("Themes/Dark.xaml", btn.File);
        Assert.Equal(Category.Fail, btn.Category);
        Assert.Equal(2, r.Pairs);
        Assert.Equal(1, r.FileCount); // 主題檔仍不算畫面檔
    }

    [Fact] // 同上，色盤檔解析失敗時不能靜默消失：WalkStyles 要讀它，讀不了就喊 parse failed
    public void UnparseablePaletteDictionaryIsReported()
    {
        using var fx = new Fixture().WithPalette();
        fx.File("Themes/Extra.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="A1" Color="#111111"/>
              <SolidColorBrush x:Key="A2" Color="#222222"/>
              <Style x:Key="Broken" TargetType="Button">
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """<Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Fg}"/></Grid>""");
        var r = fx.Run();
        Assert.StartsWith("Themes/Extra.xaml: ", Assert.Single(r.ParseErrors));
    }

    [Fact] // BasedOn 成環（A↔B、自己指自己）與指到不存在的鍵：照樣解析，不能無窮遞迴 ——
           // 那會是 StackOverflow，整支程式崩潰、連退出碼契約都守不住
    public void BasedOnCyclesAndMissingBasesDoNotRecurseForever()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="A" TargetType="Button" BasedOn="{StaticResource B}">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
                <Style x:Key="B" TargetType="Button" BasedOn="{StaticResource A}">
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                </Style>
                <Style x:Key="Self" TargetType="Button" BasedOn="{StaticResource Self}">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
                <Style x:Key="Orphan" TargetType="Button" BasedOn="{StaticResource Missing}">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource A}" Content="字色來自環的另一端"/>
              <Button Style="{StaticResource Self}" Foreground="#FFFFFF" Content="自己指自己"/>
              <Button Style="{StaticResource Orphan}" Foreground="#FFFFFF" Content="基底不存在"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(
            ["Button⊕Style[A]/base", "Button⊕Style[Self]/base", "Button⊕Style[Orphan]/base"],
            r.Findings.Select(f => f.Element));
        Assert.All(r.Findings, f => Assert.Equal(21.0, f.RatioDark)); // 白字黑底：底來自 Style，不是祖先的白
    }
}

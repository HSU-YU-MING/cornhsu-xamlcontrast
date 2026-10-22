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

    [Fact] // 合成底的寫法（baseline 鍵的一部分）：每一層用同一套正規化寫法 —— 色票 {Key}、字面色 #RRGGBB／#AARRGGBB ——
           // 以 " over " 串起來，不包外層大括號。1.0 之前是原始寫法與 {Key} 混用、外面再包一層：
           // {{DynamicResource SelectionHi} over {Bg1}}、{#8000 over #FFFFFF}（審查 1.0 A2）
    public void CompositeBackgroundsUseOneNormalisedSpelling()
    {
        using var fx = new Fixture();
        foreach (var (side, bg, hi) in new[] { ("Dark", "#111111", "#80FFFFFF"), ("Light", "#FFFFFF", "#80000000") })
            fx.File($"Themes/{side}.xaml", $$"""
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="{{bg}}"/><SolidColorBrush x:Key="Hi" Color="{{hi}}"/><SolidColorBrush x:Key="Fg" Color="#777777"/>
                </ResourceDictionary>
                """);
        fx.File("Views/Main.xaml", """
            <StackPanel Background="{DynamicResource Bg}">
              <Grid><Border Background="{DynamicResource Hi}"/><TextBlock Foreground="{DynamicResource Fg}" Text="key over key"/></Grid>
              <Grid><Border Background="#8000"/><TextBlock Foreground="{StaticResource Fg}" Text="literal over key"/></Grid>
            </StackPanel>
            """);
        var r = fx.Run();
        Assert.Equal(["{Hi} over {Bg}", "#88000000 over {Bg}"], r.Findings.Select(f => f.Bg).ToArray());
        Assert.All(r.Findings, f => Assert.DoesNotContain("{{", f.Bg));
        Assert.All(r.Findings, f => Assert.DoesNotContain("Resource", f.Bg));
    }

    [Fact] // ROADMAP「Style＋半透明底的逐狀態路徑重複合成修正」：半透明底只能疊在「更新前」的祖先底上一次。
           // 用已經合成過的底再合成一次，等於疊兩層（50% 白疊黑 = #808080，疊兩次就變 #C0C0C0）
    public void ChainTranslucentBackgroundIsCompositedOnce()
    {
        var r = RunMain("""<Button Style="{StaticResource Pad}" Background="#80FFFFFF" Foreground="#000000" Content="x"/>""");
        var f = Assert.Single(r.Findings);
        var once = Wcag.Composite("#FFFFFF", 0x80 / 255.0, "#000000");
        Assert.Equal(Wcag.Contrast("#000000", once), f.RatioDark);
        Assert.Equal("#80FFFFFF over #000000", f.Bg);
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
    [InlineData("#80000000", "#80000000 over #FFFFFF")]
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
        Assert.Equal(0, r.Skipped);
        // 定義處不另外計：有元素用到這個具名 Style，這組在使用處評（就是上面那筆）——
        // 定義處再記一筆 unresolved 就是同一組算兩次（審查最後一輪 3）
        Assert.Empty(r.UnresolvedSites);
    }

    // ── Style 定義處的透明底：只有「不會在任何使用處被評到」的才記 unresolved（審查最後一輪 3）──
    //    ac7e903 讓定義處一律記 unresolved，但具名 Style 與行內 Style 在使用處都評了 —— 同一組被重複計算，
    //    QuillNest 的覆蓋率被多扣。隱含樣式（沒有 x:Key）、以及沒有任何元素用 Style= 引用的具名 Style
    //    （只透過 ItemContainerStyle 之類套用、或根本沒用到）在使用處不會被評到，這些才記
    [Theory]
    [InlineData("""<Style x:Key="Ghost" TargetType="Button"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#EEEEEE"/></Style>""",
                """<Button Style="{StaticResource Ghost}" Content="x"/>""", 0)]                   // 用到了：使用處評
    [InlineData("""<Style x:Key="Base" TargetType="Button"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#EEEEEE"/></Style><Style x:Key="Derived" TargetType="Button" BasedOn="{StaticResource Base}"><Setter Property="FontSize" Value="20"/></Style>""",
                """<Button Style="{StaticResource Derived}" Content="x"/>""", 0)]                 // 經 BasedOn 用到：使用處評
    [InlineData("""<Style x:Key="Ghost" TargetType="Button"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#EEEEEE"/></Style>""",
                """<ListBox ItemContainerStyle="{StaticResource Ghost}"/>""", 1)]                 // 沒有元素用 Style= 引用它：哪裡都評不到
    [InlineData("""<Style TargetType="ToggleButton"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#EEEEEE"/></Style>""",
                """<ToggleButton Content="x"/>""", 1)]                                            // 隱含樣式：使用處不評
    public void SeeThroughStyleDefinitionIsUnresolvedOnlyWhenNoUseSiteGradesIt(string styles, string use, int unresolved)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>{styles}</Grid.Resources>
              {use}
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(unresolved, r.UnresolvedSites.Count(u => u.Reason == UnresolvedReason.TranslucentUncomposited));
        Assert.Equal(0, r.Skipped);
    }

    [Fact] // 行內 Style（<Button.Style><Style>…）一定被那個元素用到：定義處不另外計
    public void InlineSeeThroughStyleIsGradedOnlyWhereItSits()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Button Content="x">
                <Button.Style><Style TargetType="Button"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#EEEEEE"/></Style></Button.Style>
              </Button>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.UnresolvedSites);
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#FFFFFF"), Assert.Single(r.Findings).RatioDark);
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
        var site = Assert.Single(fx.Run().UnresolvedSites, s => s.Line == 8); // 第 3 行是定義處（沒有底可疊）
        Assert.Equal(UnresolvedReason.BoundOrGradient, site.Reason);
        Assert.Equal("{Binding NoteColour}", site.Value);
    }

    [Fact] // 半透明的狀態底（hover 的 #22000000）疊在 Binding 的祖先底上：卡住的是那個 Binding —— 不是「找不到祖先底」
           // （QuillNest StickyNoteWindow 的標題列按鈕就是這個形狀：便利貼底色綁使用者選的顏色）
    public void TranslucentStateBackgroundOverABoundBackgroundNamesTheBoundValue()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Title" TargetType="Button">
                  <Setter Property="Background" Value="#22000000"/>
                </Style>
              </Grid.Resources>
              <Border Background="{Binding NoteColour}"><Button Style="{StaticResource Title}" Foreground="#333333" Content="x"/></Border>
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
    // 半透明底、底下沒東西：1.0 起是 translucent-uncomposited（決策檔 L2 選項 A），不是「祖先沒有底」
    [InlineData("""<Button Style="{StaticResource Pad}" Background="#80FFFFFF" Foreground="#000000" Content="半透明底、底下沒東西"/>""", "Transparent", "translucent-uncomposited")]
    // 半透明的字、沒有底：一樣是 no-ancestor-background，不是 skipped（B1 的核心；審查 mutation F6b：鏈路徑沒有這條測試）
    [InlineData("""<Button Style="{StaticResource Pad}" Foreground="#80FFFFFF" Content="半透明字、沒有底"/>""", "Transparent", "no-ancestor-background")]
    [InlineData("""<Button Style="{StaticResource Pad}" Foreground="#FFFFFF" Content="祖先底綁執行期"/>""", "{Binding WindowBg}", "bound-or-gradient")]
    [InlineData("""<Button Style="{StaticResource Pad}" Background="{Binding Fill}" Foreground="#FFFFFF" Content="自己的底綁執行期"/>""", "#000000", "bound-or-gradient")]
    [InlineData("""<Button Style="{StaticResource Pad}" Background="{DynamicResource Nope}" Foreground="#FFFFFF" Content="底是死引用"/>""", "#000000", "unknown-palette-key")]
    [InlineData("""<Image Source="a.png"/><Button Style="{StaticResource Pad}" Foreground="#FFFFFF" Content="疊在兄弟圖片上"/>""", "#000000", "over-sibling-content")]
    [InlineData("""<Button Style="{StaticResource Pad}" Opacity="0" Foreground="#FFFFFF" Content="淡入動畫的初始態"/>""", "#000000", "skipped")]
    [InlineData("""<Button Style="{StaticResource Pad}" Foreground="Transparent" Content="透明字"/>""", "#000000", "skipped")]
    // Opacity 0 的字不管底是什麼都是看不見（審查最後一輪 4：底的判斷不能先把它攔成 unresolved）
    [InlineData("""<Button Style="{StaticResource Pad}" Opacity="0" Foreground="#FFFFFF" Content="淡入動畫的初始態，沒有底"/>""", "Transparent", "skipped")]
    [InlineData("""<Button Style="{StaticResource Pad}" Opacity="0" Foreground="#FFFFFF" Content="淡入動畫的初始態，底綁執行期"/>""", "{Binding WindowBg}", "skipped")]
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

    // ── 色盤來自分開的深／淺 XAML 字典時，主題檔裡的 Style 只屬於那一個主題 ──
    //    Dark.xaml 與 Light.xaml 各定義一個 Chip：Dark.xaml 的 Chip 只在深色主題下存在，拿它配淺色色票算出的
    //    「light=1.16:1」是執行期不存在的組合（反之亦然）—— 審查素材 h3pair，報出兩筆編造的 fail

    private static Fixture ThemePairWithChips(string darkChipFg, string lightChipFg,
        string darkFile = "Themes/Dark.xaml", string lightFile = "Themes/Light.xaml") => new Fixture()
        .File(darkFile, $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
              <Style x:Key="Chip" TargetType="Button">
                <Setter Property="Background" Value="{"{"}DynamicResource Bg{"}"}"/>
                <Setter Property="Foreground" Value="{darkChipFg}"/>
              </Style>
            </ResourceDictionary>
            """)
        .File(lightFile, $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
              <Style x:Key="Chip" TargetType="Button">
                <Setter Property="Background" Value="{"{"}DynamicResource Bg{"}"}"/>
                <Setter Property="Foreground" Value="{lightChipFg}"/>
              </Style>
            </ResourceDictionary>
            """)
        .File("Views/Main.xaml", """
            <Grid Background="{DynamicResource Bg}"><Button Style="{StaticResource Chip}" Content="x"/></Grid>
            """);

    [Fact]
    public void StyleInAOneThemeDictionaryIsGradedAgainstThatThemeOnly()
    {
        using var fx = ThemePairWithChips("#EEEEEE", "#1B1B1B");
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.Equal(0, r.CountOf(Category.Fail)); // 沒有 Light.xaml 的 dark=1.1、Dark.xaml 的 light=1.16 兩筆編造
        var dark = Assert.Single(r.Findings, f => f.File == "Themes/Dark.xaml");
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#111111"), dark.RatioDark);
        Assert.Equal(dark.RatioDark, dark.RatioLight);              // 只有深色主題這一欄
        var light = Assert.Single(r.Findings, f => f.File == "Themes/Light.xaml");
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#FFFFFF"), light.RatioLight);
        Assert.Equal(light.RatioLight, light.RatioDark);
    }

    [Fact] // 真的不合格照樣報，而且標 single-theme（只屬於一個主題，「換主題救不救得回來」不適用）
    public void OneThemeDictionaryFailureIsStillReported()
    {
        using var fx = ThemePairWithChips("#333333", "#1B1B1B");
        var r = fx.Run();
        var f = Assert.Single(r.Findings, x => x.Category == Category.Fail);
        Assert.Equal("Themes/Dark.xaml", f.File);
        Assert.Equal(Wcag.Contrast("#333333", "#111111"), f.RatioDark);
        Assert.Equal(Symmetry.SingleTheme, f.Symmetry);
    }

    [Theory] // 主題提示要能認出常見的寫法：檔名本身、檔名裡的一個詞、或所在的最後一段目錄
    [InlineData("Themes/Dark.xaml", "Themes/Light.xaml")]
    [InlineData("Themes/DarkTheme.xaml", "Themes/LightTheme.xaml")]
    [InlineData("Themes/ColorsDark.xaml", "Themes/ColorsLight.xaml")]
    [InlineData("Themes/Theme.Dark.xaml", "Themes/Theme.Light.xaml")]
    [InlineData("Themes/dark-theme.xaml", "Themes/light_theme.xaml")]
    [InlineData("Themes/Dark/Colors.xaml", "Themes/Light/Colors.xaml")]
    public void ThemeDictionaryNamingConventionsAreRecognised(string darkFile, string lightFile)
    {
        using var fx = ThemePairWithChips("#EEEEEE", "#1B1B1B", darkFile, lightFile);
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.Equal(0, r.CountOf(Category.Fail));
        var dark = Assert.Single(r.Findings, f => f.File == darkFile);
        Assert.Equal(dark.RatioDark, dark.RatioLight);
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#111111"), dark.RatioDark);
        var light = Assert.Single(r.Findings, f => f.File == lightFile);
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#FFFFFF"), light.RatioLight);
    }

    // ── 主題提示是「整個詞」，不是子字串（審查素材 b4hl、b4dir）──
    //    舊版對整條相對路徑做 Contains("dark")／Contains("light")：Highlight、Lightweight、Spotlight 都命中 light，
    //    中性檔被當成「只屬於淺色主題」只評淺色那欄 —— 深色主題下 1.04:1 的真 fail 消失。資料夾叫 Starlight/
    //    的話整個專案每個檔都帶 light 提示（Action 的預設 root 是 repo 根，CI 上最容易踩到）

    [Theory]
    [InlineData("", "Themes/HighlightStyles.xaml")]
    [InlineData("", "Themes/Lightweight.xaml")]
    [InlineData("", "Themes/Spotlight.xaml")]
    [InlineData("", "Themes/Darkroom.xaml")]
    [InlineData("Starlight/", "Themes/Shared.xaml")]
    [InlineData("", "Dark/Themes/Shared.xaml")] // 只看最後一段目錄
    public void ThemeHintIsAWholeWordNotASubstring(string prefix, string sharedFile)
    {
        using var fx = new Fixture();
        fx.File(prefix + "Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
            </ResourceDictionary>
            """);
        fx.File(prefix + "Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        fx.File(prefix + sharedFile, """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="NoteBg" Color="#FFF59D"/>
              <Style x:Key="Note" TargetType="Button">
                <Setter Property="Background" Value="{DynamicResource NoteBg}"/>
                <Setter Property="Foreground" Value="{DynamicResource Fg}"/>
              </Style>
            </ResourceDictionary>
            """);
        fx.File(prefix + "Views/Main.xaml", """<Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Fg}"/></Grid>""");
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.Equal(("#111111", "#FFFFFF"), r.Detection.Palette.Entries["Bg"]);
        Assert.Null(r.Detection.SideOf(Path.Combine(fx.Root, prefix + sharedFile)));
        var note = Assert.Single(r.Findings, f => f.Element == "Style[Note]/base");
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#FFF59D"), note.RatioDark); // 深色主題 1.04:1
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#FFF59D"), note.RatioLight);
        Assert.Equal(Category.Fail, note.Category);
        Assert.Equal(Symmetry.DarkFails, note.Symmetry);
    }

    [Theory] // 直接驗提示本身：整詞、只看檔名與最後一段目錄、兩個都有就不確定
    [InlineData("Dark.xaml", ThemeSide.Dark)]
    [InlineData("Themes/LightTheme.xaml", ThemeSide.Light)]
    [InlineData("Themes/Colors.DARK.xaml", ThemeSide.Dark)]
    [InlineData("Themes\\Light\\Brushes.xaml", ThemeSide.Light)]
    [InlineData("Themes/HighlightStyles.xaml", null)]
    [InlineData("Themes/Lightweight.xaml", null)]
    [InlineData("Starlight/Themes/Shared.xaml", null)]
    [InlineData("Dark/Themes/Shared.xaml", null)]
    [InlineData("Themes/Dark/LightOverrides.xaml", null)]
    [InlineData("Themes/DarkLight.xaml", null)]
    public void ThemeHintOfAPath(string rel, ThemeSide? expected)
        => Assert.Equal(expected, PaletteDetector.ThemeHint(rel));

    // ── 使用處：同 key 的 Style 依主題各自解析（審查素材 b4use）──
    //    Dark.xaml 與 Light.xaml 各定義一個 Chip（Background=Transparent，定義處沒有底可疊、交給使用處評）。
    //    舊版使用處的 Style 查找是「全專案先出現的贏」：解析到 Dark 的 Chip，再拿淺色欄去評 —— 報出
    //    light=1.16 的 fail，這個組合在執行期不存在。只設 Foreground 的 Tag Style 同樣形狀

    private static Fixture ThemePairWithTransparentChips(string darkChipFg) => new Fixture()
        .File("Themes/Dark.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Line" Color="#333333"/>
              <Style x:Key="Chip" TargetType="Button">
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="Foreground" Value="{darkChipFg}"/>
              </Style>
              <Style x:Key="Tag" TargetType="TextBlock">
                <Setter Property="Foreground" Value="#EEEEEE"/>
              </Style>
            </ResourceDictionary>
            """)
        .File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
              <Style x:Key="Chip" TargetType="Button">
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="Foreground" Value="#1B1B1B"/>
              </Style>
              <Style x:Key="Tag" TargetType="TextBlock">
                <Setter Property="Foreground" Value="#1B1B1B"/>
              </Style>
            </ResourceDictionary>
            """)
        .File("Views/Main.xaml", """
            <Grid Background="{DynamicResource Bg}"><StackPanel>
              <Button Style="{StaticResource Chip}" Content="x"/>
              <TextBlock Style="{StaticResource Tag}" Text="y"/>
            </StackPanel></Grid>
            """);

    [Fact]
    public void SameKeyStylesInThemeDictionariesAreResolvedPerThemeWhereUsed()
    {
        using var fx = ThemePairWithTransparentChips("#EEEEEE");
        var r = fx.Run();
        Assert.Equal(0, r.CountOf(Category.Fail)); // 沒有 Dark 的 Chip／Tag 配淺色底的 1.16:1
        var uses = r.Findings.Where(f => f.File == "Views/Main.xaml").ToList();
        Assert.Equal(4, uses.Count); // Chip、Tag 各在深淺兩個主題下評一次
        Assert.All(uses, f => Assert.True(f.OneThemeOnly));
        Assert.All(uses, f => Assert.Equal(f.RatioDark, f.RatioLight));
        var dark = Wcag.Contrast("#EEEEEE", "#111111");
        var light = Wcag.Contrast("#1B1B1B", "#FFFFFF");
        Assert.Equal(dark, Assert.Single(uses, f => f.Element == "Button⊕Style[Chip]@dark/base").RatioDark);
        Assert.Equal(light, Assert.Single(uses, f => f.Element == "Button⊕Style[Chip]@light/base").RatioDark);
        Assert.Equal(dark, Assert.Single(uses, f => f.Element == "TextBlock⊕Style[Tag]@dark/base").RatioDark);
        Assert.Equal(light, Assert.Single(uses, f => f.Element == "TextBlock⊕Style[Tag]@light/base").RatioDark);
    }

    [Fact] // 真的不合格照樣報：只報那一個主題的那一筆，標 single-theme
    public void PerThemeStyleFailureAtTheUseSiteIsReportedForItsThemeOnly()
    {
        using var fx = ThemePairWithTransparentChips("#333333");
        var r = fx.Run();
        var f = Assert.Single(r.Findings, x => x.Category == Category.Fail);
        Assert.Equal("Button⊕Style[Chip]@dark/base", f.Element);
        Assert.Equal(Wcag.Contrast("#333333", "#111111"), f.RatioDark);
        Assert.Equal(Symmetry.SingleTheme, f.Symmetry);
    }

    // ── 只在一個主題定義的 Style key（審查最後一輪 5：素材 per-onlydark_dyn、per-onlydark_fg）──
    //    92520b2 之後另一個主題解析不到這個 Style，那一側整組靜默消失 —— 沒評分、也不在任何計數器裡
    //    （舊版拿 Dark 的 Style 配淺色欄，報出淺色 fail）。缺的那一側記 unknown-palette-key，點名 Style key
    [Theory]
    [InlineData("""<Style x:Key="OnlyDark" TargetType="Button"><Setter Property="Background" Value="Transparent"/></Style>""",
                """<Button Style="{DynamicResource OnlyDark}" Foreground="{DynamicResource Muted}" Content="x"/>""", "Button")]
    [InlineData("""<Style x:Key="OnlyDark" TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource Muted}"/></Style>""",
                """<TextBlock Style="{DynamicResource OnlyDark}" Text="x"/>""", "TextBlock")]
    public void StyleDefinedInOneThemeOnlyCountsTheOtherThemeAsUnresolved(string style, string use, string element)
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Muted" Color="#888888"/>
              {style}
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Muted" Color="#DDDDDD"/>
            </ResourceDictionary>
            """);
        fx.File("Views/Main.xaml", $"""<Grid Background="{"{"}DynamicResource Bg{"}"}">{use}</Grid>""");
        var r = fx.Run();
        var dark = Assert.Single(r.Findings, f => f.File == "Views/Main.xaml");
        Assert.Equal($"{element}⊕Style[OnlyDark]@dark/base", dark.Element);
        var light = Assert.Single(r.UnresolvedSites, u => u.File == "Views/Main.xaml");
        Assert.Equal(UnresolvedReason.UnknownPaletteKey, light.Reason);
        Assert.Equal("OnlyDark", light.Value);
    }

    [Fact] // 中性檔的 Style BasedOn 主題檔的 Style：基底依主題各自解析
    public void NeutralStyleBasedOnAPerThemeStyleIsResolvedPerTheme()
    {
        using var fx = ThemePairWithTransparentChips("#EEEEEE");
        fx.File("Views/Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
              <Grid.Resources>
                <Style x:Key="BigChip" TargetType="Button" BasedOn="{StaticResource Chip}">
                  <Setter Property="FontSize" Value="20"/>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource BigChip}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.CountOf(Category.Fail));
        var uses = r.Findings.Where(f => f.File == "Views/Main.xaml").ToList();
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#111111"), Assert.Single(uses, f => f.Element.Contains("@dark")).RatioDark);
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#FFFFFF"), Assert.Single(uses, f => f.Element.Contains("@light")).RatioDark);
    }

    [Fact] // Style 放在不含色票的主題檔（Themes/Dark/Styles.xaml 搭配 Themes/Dark/Colors.xaml，審查素材 b4split）：
           // 舊版的主題歸屬只看「貢獻了色票的檔」，Styles.xaml 被當成兩個主題共用，Dark 的 Chip 配淺色底報 1.16:1
    public void StylesInAThemeFolderWithoutColourKeysBelongToThatTheme()
    {
        using var fx = new Fixture();
        foreach (var (theme, bg, fg, line) in new[] { ("Dark", "#111111", "#EEEEEE", "#333333"), ("Light", "#FFFFFF", "#1B1B1B", "#CCCCCC") })
        {
            fx.File($"Themes/{theme}/Colors.xaml", $"""
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="{bg}"/>
                  <SolidColorBrush x:Key="Fg" Color="{fg}"/>
                  <SolidColorBrush x:Key="Line" Color="{line}"/>
                </ResourceDictionary>
                """);
            fx.File($"Themes/{theme}/Styles.xaml", $$"""
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Style x:Key="Chip" TargetType="Button">
                    <Setter Property="Background" Value="{DynamicResource Bg}"/>
                    <Setter Property="Foreground" Value="{{fg}}"/>
                  </Style>
                </ResourceDictionary>
                """);
        }
        fx.File("Views/Main.xaml", """<Grid Background="{DynamicResource Bg}"><Button Style="{StaticResource Chip}" Content="x"/></Grid>""");
        var r = fx.Run();
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.Equal(ThemeSide.Dark, r.Detection.SideOf(Path.Combine(fx.Root, "Themes/Dark/Styles.xaml")));
        Assert.Equal(0, r.CountOf(Category.Fail));
        var dark = Assert.Single(r.Findings, f => f.File == "Themes/Dark/Styles.xaml");
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#111111"), dark.RatioDark);
        Assert.Equal(dark.RatioDark, dark.RatioLight);
        var light = Assert.Single(r.Findings, f => f.File == "Themes/Light/Styles.xaml");
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#FFFFFF"), light.RatioLight);
    }

    // ── 只放 Style 的字典要有對應檔才算主題專屬（審查最後一輪 6：素材 hint-falsepos）──
    //    69912a5 把不含色票的字典也納入主題歸屬：兩個主題共用的 Styles/DarkButton.xaml（按鈕叫 DarkButton）被判成只屬於深色，
    //    只評深色那欄，淺色 1.36:1 的 fail 消失；Controls/LightBox.xaml 反過來只評淺色。只評單欄認錯就會藏 fail ——
    //    要「同一路徑、dark↔light 互換後的檔」存在（Themes/Dark/Styles.xaml ↔ Themes/Light/Styles.xaml）才算

    private static Fixture ThemePairPlusStyleFiles(params string[] styleFiles)
    {
        var fx = new Fixture()
            .File("Themes/Dark.xaml", """
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#111111"/>
                  <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
                  <SolidColorBrush x:Key="Muted" Color="#888888"/>
                </ResourceDictionary>
                """)
            .File("Themes/Light.xaml", """
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
                  <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
                  <SolidColorBrush x:Key="Muted" Color="#DDDDDD"/>
                </ResourceDictionary>
                """);
        foreach (var f in styleFiles)
            fx.File(f, $$"""
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Style x:Key="{{Path.GetFileNameWithoutExtension(f)}}Style" TargetType="Button">
                    <Setter Property="Foreground" Value="{DynamicResource Muted}"/>
                    <Setter Property="Background" Value="{DynamicResource Bg}"/>
                  </Style>
                </ResourceDictionary>
                """);
        return fx;
    }

    [Fact]
    public void StyleOnlyDictionaryWithoutACounterpartIsSharedByBothThemes()
    {
        using var fx = ThemePairPlusStyleFiles("Styles/DarkButton.xaml", "Controls/LightBox.xaml");
        var r = fx.Run();
        Assert.Null(r.Detection.SideOf(Path.Combine(fx.Root, "Styles/DarkButton.xaml")));
        Assert.Null(r.Detection.SideOf(Path.Combine(fx.Root, "Controls/LightBox.xaml")));
        foreach (var file in new[] { "Styles/DarkButton.xaml", "Controls/LightBox.xaml" })
        {
            var f = Assert.Single(r.Findings, x => x.File == file);
            Assert.Equal(Wcag.Contrast("#888888", "#111111"), f.RatioDark);
            Assert.Equal(Wcag.Contrast("#DDDDDD", "#FFFFFF"), f.RatioLight); // 淺色 1.36:1 照報
            Assert.Equal(Category.Fail, f.Category);
        }
    }

    [Theory] // 有對應檔就是一組主題字典 —— 連嚴格提示認不得的寫法（全小寫黏在一起）也一樣
    [InlineData("Themes/Dark/Styles.xaml", "Themes/Light/Styles.xaml")]
    [InlineData("Styles/DarkButtons.xaml", "Styles/LightButtons.xaml")]
    [InlineData("Styles/darkbuttons.xaml", "Styles/lightbuttons.xaml")]
    public void StyleOnlyDictionaryWithACounterpartBelongsToItsTheme(string darkFile, string lightFile)
    {
        using var fx = ThemePairPlusStyleFiles(darkFile, lightFile);
        var r = fx.Run();
        Assert.Equal(ThemeSide.Dark, r.Detection.SideOf(Path.Combine(fx.Root, darkFile)));
        Assert.Equal(ThemeSide.Light, r.Detection.SideOf(Path.Combine(fx.Root, lightFile)));
        var dark = Assert.Single(r.Findings, x => x.File == darkFile);
        Assert.Equal(dark.RatioDark, dark.RatioLight);
    }

    [Fact] // config 強制 pair：指定的兩個檔就是各自主題的字典（檔名沒有 dark/light 提示也一樣），裡面的 Style 只評那一側
    public void ForcedPairFilesAreOneThemeDictionaries()
    {
        using var fx = ThemePairWithChips("#EEEEEE", "#1B1B1B", "Colors/A.xaml", "Colors/B.xaml");
        fx.File(ToolConfig.FileName, """
            { "palette": { "mode": "pair", "darkFile": "Colors/A.xaml", "lightFile": "Colors/B.xaml" } }
            """);
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(PaletteMode.Pair, r.Detection.Mode);
        Assert.Equal(0, r.CountOf(Category.Fail));
        var a = Assert.Single(r.Findings, f => f.File == "Colors/A.xaml");
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#111111"), a.RatioDark);
        Assert.Equal(a.RatioDark, a.RatioLight);
        Assert.True(a.OneThemeOnly);
    }

    // ── 鏈路徑的去重：「觸發器只動了無關屬性 → 與基礎同組」要比兩個主題合成後的實際顏色（審查素材 dedup）──
    //    舊版的鍵只看深色值（與深色 alpha）：hover 換成深色同值、淺色不同的色票時，淺色 1.16:1 的真 fail 被當成
    //    重複丟掉，結果 pairs 1、exit 0。半透明色票（深色同 alpha、淺色不同 alpha）同樣被吞
    [Theory]
    [InlineData("#FFFFFF", "#FFFFFF", "#333333", "#EEEEEE")] // 不透明：深色同值，淺色不同
    [InlineData("#80FFFFFF", "#80FFFFFF", "#E0000000", "#20000000")] // 半透明：深色同 alpha，淺色 alpha 不同
    public void ChainStatesThatDifferOnlyOnTheLightSideAreNotDeduplicated(string aDark, string bDark, string aLight, string bLight)
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#000000"/>
              <SolidColorBrush x:Key="A" Color="{aDark}"/>
              <SolidColorBrush x:Key="B" Color="{bDark}"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="A" Color="{aLight}"/>
              <SolidColorBrush x:Key="B" Color="{bLight}"/>
            </ResourceDictionary>
            """);
        fx.File("Views/Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
              <Grid.Resources>
                <Style x:Key="S" TargetType="Button">
                  <Setter Property="Foreground" Value="{DynamicResource A}"/>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="{DynamicResource B}"/></Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource S}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(2, r.Pairs);
        var hover = Assert.Single(r.Findings, f => f.Element == "Button⊕Style[S]/trigger");
        Assert.Equal(Category.Fail, hover.Category);
        Assert.Equal(Symmetry.LightFails, hover.Symmetry);
    }

    [Fact] // 反方向：只動了無關屬性的觸發器（停用態以外，例如只設 Cursor）照樣與基礎同組，不重複
    public void TriggerThatChangesNothingVisibleIsStillDeduplicated()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="S" TargetType="Button">
                  <Setter Property="Foreground" Value="#000000"/>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter Property="Cursor" Value="Hand"/></Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource S}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Pairs);
    }

    [Fact] // 根元素的隱含樣式背景（地板）也一樣：Dark.xaml 與 Light.xaml 各自的隱含 UserControl 樣式只在自己的主題下生效
    public void ImplicitRootBackgroundFromThemeDictionariesIsResolvedPerTheme()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="A" Color="#111111"/>
              <SolidColorBrush x:Key="B" Color="#222222"/>
              <Style TargetType="UserControl"><Setter Property="Background" Value="#111111"/></Style>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="A" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="B" Color="#EEEEEE"/>
              <Style TargetType="UserControl"><Setter Property="Background" Value="#FFFFFF"/></Style>
            </ResourceDictionary>
            """);
        fx.File("Views/Main.xaml", """
            <UserControl><TextBlock Foreground="{DynamicResource Fg}" Text="x"/></UserControl>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings, x => x.File == "Views/Main.xaml");
        Assert.Equal(Wcag.Contrast("#EEEEEE", "#111111"), f.RatioDark);
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#FFFFFF"), f.RatioLight);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Fact] // C# 真相源時，XAML 主題檔是兩個主題共用的靜態複本（Kindling 形狀）—— 裡面的 Style 兩欄都要評，
           // Kindling DarkTheme.xaml:149 DarkBtn 按下態的淺色 2.49:1 就是這樣抓到的，不能因為檔名有 Dark 就只評深色
    public void StyleInACSharpPaletteStaticCopyIsGradedForBothThemes()
    {
        using var fx = new Fixture();
        fx.File("Services/ThemeService.cs", """
            ("Bg3", "#2A2A2A", "#F0F0F0"),
            ("Fg0", "#EEEEEE", "#1B1B1B"),
            ("PurpleD", "#534AB7", "#534AB7"),
            """);
        fx.File("Themes/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg3" Color="#2A2A2A"/>
              <SolidColorBrush x:Key="Fg0" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="PurpleD" Color="#534AB7"/>
              <Style x:Key="Pressed" TargetType="Button">
                <Setter Property="Background" Value="{DynamicResource PurpleD}"/>
                <Setter Property="Foreground" Value="{DynamicResource Fg0}"/>
              </Style>
            </ResourceDictionary>
            """);
        var r = fx.Run();
        Assert.Equal(PaletteMode.CSharp, r.Detection.Mode);
        var f = Assert.Single(r.Findings);
        Assert.Equal(Wcag.Contrast("#1B1B1B", "#534AB7"), f.RatioLight); // 淺色主題 2.49:1
        Assert.Equal(Category.Fail, f.Category);
        Assert.Equal(Symmetry.LightFails, f.Symmetry);
    }

    [Fact] // 色盤字典不只有 Style：DataTemplate／ControlTemplate 也可能跟 keyed brush 住在同一份 ResourceDictionary。
           // 舊版色盤字典整檔不走樹、也沒有任何計數器 —— 同一份字典只因為多了一個 brush，裡面 1.16:1 的模板就消失
           // （審查素材 h1dt 對 h1dt2）。色盤字典照樣走樹，只是不算進 files（它不是畫面）
    public void TemplatesInAPaletteDictionaryAreStillWalked()
    {
        using var fx = new Fixture().WithPalette();
        fx.File("Views/Templates.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="CardAccent" Color="#FF0000"/>
              <DataTemplate x:Key="Card">
                <Grid Background="#FFFFFF"><TextBlock Foreground="#EEEEEE" Text="1.16:1"/></Grid>
              </DataTemplate>
            </ResourceDictionary>
            """);
        fx.File("Views/Main.xaml", """<Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Fg}"/></Grid>""");
        var r = fx.Run();
        Assert.Contains(r.Detection.ExcludedFiles, f => f.EndsWith("Templates.xaml")); // 前提：它是色盤字典
        Assert.Equal(1, r.FileCount);
        var f = Assert.Single(r.Findings, x => x.File == "Views/Templates.xaml");
        Assert.Equal(Category.Fail, f.Category);
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

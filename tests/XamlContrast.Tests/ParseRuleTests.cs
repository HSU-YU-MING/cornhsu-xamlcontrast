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

    [Theory] // 完全透明的字色（裝飾元素的 Fill 同理）是合法跳過 —— 但要計數，不是從報告消失；
             // 也不能塞進 unresolved（那是「工具看不懂」的桶，這裡工具看得懂）
    [InlineData("""<TextBlock Foreground="Transparent" Text="x"/>""")]
    [InlineData("""<TextBlock Foreground="#00FFFFFF" Text="alpha 0 — invisible"/>""")]
    [InlineData("""<Rectangle Fill="Transparent"/>""")]
    public void TransparentForegroundIsSkippedAndCounted(string element)
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

    [Fact] // 半透明的字色（#AARRGGBB）疊在已解析的不透明底上 —— 跟 Opacity 一樣是確定性的合成，不是猜。
           // 舊設計一律「合法跳過」，而 skipped 不進覆蓋率分母：80% 透明的白字疊黑底照樣不在報告裡、覆蓋率仍 100%
    public void TranslucentForegroundIsCompositedOverItsBackground()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#33FFFFFF" Text="20% white"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Skipped);
        var f = Assert.Single(r.Findings);
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#FFFFFF", 0x33 / 255.0, "#000000"), "#000000"), f.RatioDark); // 1.66:1
        Assert.Equal(Category.Fail, f.Category);
        Assert.Equal("#33FFFFFF", f.Fg);
    }

    [Fact] // 元素 Opacity 與字色本身的 alpha 相乘（兩者都是「以 alpha 疊上去」）
    public void TranslucentForegroundAndElementOpacityMultiply()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Opacity="0.5" Foreground="#80FFFFFF" Text="x"/>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#FFFFFF", 0x80 / 255.0 * 0.5, "#000000"), "#000000"), f.RatioDark);
        Assert.Equal(f.RatioDark, f.RatioLight); // 淺色欄一樣要乘 Opacity（審查 mutation F2b：只斷言深色欄時存活）
    }

    [Fact] // 底沒解析出來時，半透明字色照舊落進 unresolved（進覆蓋率分母），不是「跳過」讓覆蓋率虛高
    public void TranslucentForegroundWithoutABackgroundIsUnresolved()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid><TextBlock Foreground="#80FFFFFF" Text="x"/></Grid>""");
        var r = fx.Run();
        Assert.Equal(0, r.Skipped);
        Assert.Equal(UnresolvedReason.NoAncestorBackground, Assert.Single(r.UnresolvedSites).Reason);
    }

    // ── Opacity 0 的字是「看不見」，不管底是什麼（審查最後一輪 4：素材 op-zero-root、op-zero-inner）──
    //    4202b4c 之後，Opacity 0 的群組疊到「什麼都沒有」或 Binding 的底上時，先被底的判斷攔下來記成
    //    unresolved（no-ancestor-background／bound-or-gradient）—— 與 skipped 的標籤、README 的說法矛盾
    [Theory]
    [InlineData("""<UserControl Opacity="0" Background="#FFFFFF"><Grid><TextBlock Foreground="#000000" Text="a"/></Grid></UserControl>""")]
    [InlineData("""<Grid Background="{Binding Bg}"><Border Opacity="0" Background="#333333"><TextBlock Foreground="#FFFFFF" Text="toast"/></Border></Grid>""")]
    [InlineData("""<Grid><TextBlock Opacity="0" Foreground="#FFFFFF" Text="no background at all"/></Grid>""")]
    public void InvisibleTextIsSkippedWhateverTheBackground(string xaml)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", xaml);
        var r = fx.Run();
        Assert.Equal(1, r.Skipped);
        Assert.Equal(0, r.Unresolved);
        Assert.Empty(r.Findings);
    }

    // ── 元素 Opacity 在提供底色的元素上或更上層（審查素材 math 第 6 行）──
    //    WPF 的 Opacity 是整棵子樹先畫成一層、再整層疊到底下。底畫在那一層裡時，底也跟著變淡：白底上
    //    Opacity 0.5 的黑 Border 裡的白字，實際是白字配 #808080（3.95:1）。舊版只把字疊上去、底仍當純黑，
    //    報 5.32 ok —— 真的不合格被藏掉

    [Theory]
    [InlineData("""<Grid Background="#FFFFFF"><Border Background="#000000" Opacity="0.5"><TextBlock Foreground="#FFFFFF" Text="x"/></Border></Grid>""")]
    [InlineData("""<Grid Background="#FFFFFF"><StackPanel Opacity="0.5"><Border Background="#000000"><TextBlock Foreground="#FFFFFF" Text="x"/></Border></StackPanel></Grid>""")]
    [InlineData("""<Grid Background="#FFFFFF"><Grid Opacity="0.5"><Border Background="#000000"/><TextBlock Foreground="#FFFFFF" Text="x"/></Grid></Grid>""")] // 兄弟背板
    public void OpacityOnOrAboveTheBackgroundOwnerFadesTheBackgroundToo(string xaml)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", xaml);
        var f = Assert.Single(fx.Run().Findings);
        var bg = Wcag.Composite("#000000", 0.5, "#FFFFFF");
        Assert.Equal(Wcag.Contrast("#FFFFFF", bg), f.RatioDark); // 3.95，不是 5.32
        Assert.Equal(f.RatioDark, f.RatioLight);
        Assert.Equal(Category.Warn, f.Category);
    }

    [Fact] // 兩層 Opacity 群組都畫了底（審查最後一輪 8 的 O3，素材 op-nested2）：內層的底與字先疊到外層那一層的底上，
           // 外層再整層疊到白底上。每一層以 8 位元合成（WPF 的 Opacity 層是 8 位元的中間繪製面），所以是 2.49:1；
           // 不逐層取整的手算是 2.47:1
    public void NestedFadedLayersThatBothPaintABackgroundCompositeLayerByLayer()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF"><Border Background="#000000" Opacity="0.6"><Border Background="#000080" Opacity="0.5">
              <TextBlock Foreground="#FFFF00" Text="a"/>
            </Border></Border></Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        var text = Wcag.Composite(Wcag.Composite("#FFFF00", 0.5, "#000000"), 0.6, "#FFFFFF");
        var bg = Wcag.Composite(Wcag.Composite("#000080", 0.5, "#000000"), 0.6, "#FFFFFF");
        Assert.Equal(Wcag.Contrast(text, bg), f.RatioDark);
        Assert.Equal(2.49, f.RatioDark);
        Assert.Equal(f.RatioDark, f.RatioLight);
    }

    [Fact] // 字自己也有 Opacity：先在那一層裡疊到黑底上，再整層疊到白底上
    public void TextOpacityInsideAFadedLayerIsCompositedLayerByLayer()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF"><Border Background="#000000" Opacity="0.5">
              <TextBlock Foreground="#FFFFFF" Opacity="0.5" Text="x"/>
            </Border></Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        var text = Wcag.Composite(Wcag.Composite("#FFFFFF", 0.5, "#000000"), 0.5, "#FFFFFF");
        Assert.Equal(Wcag.Contrast(text, Wcag.Composite("#000000", 0.5, "#FFFFFF")), f.RatioDark);
    }

    [Fact] // Style 給的底、元素自己有 Opacity（鏈路徑）：一樣整層變淡
    public void OpacityOnAStyledElementFadesTheStyleBackground()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Dark" TargetType="Button"><Setter Property="Background" Value="#000000"/></Style>
              </Grid.Resources>
              <Button Style="{StaticResource Dark}" Foreground="#FFFFFF" Opacity="0.5" Content="x"/>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal(Wcag.Contrast("#FFFFFF", Wcag.Composite("#000000", 0.5, "#FFFFFF")), f.RatioDark);
    }

    [Theory] // 變淡的那一層底下是什麼不知道：沒有底 → translucent-uncomposited；綁定的底 → bound-or-gradient
    [InlineData("""<Grid><Border Background="#000000" Opacity="0.5"><TextBlock Foreground="#FFFFFF" Text="x"/></Border></Grid>""", UnresolvedReason.TranslucentUncomposited)]
    [InlineData("""<Grid Background="{Binding X}"><Border Background="#000000" Opacity="0.5"><TextBlock Foreground="#FFFFFF" Text="x"/></Border></Grid>""", UnresolvedReason.BoundOrGradient)]
    public void FadedLayerOverAnUnknownBackgroundIsUnresolved(string xaml, UnresolvedReason reason)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", xaml);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal(reason, Assert.Single(r.UnresolvedSites).Reason);
    }

    [Fact] // 底在 Opacity 群組外面（群組只包住字）：維持原本的算法 —— 字以累乘的 alpha 疊在底上
    public void OpacityBelowTheBackgroundOwnerOnlyFadesTheText()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000"><StackPanel Opacity="0.5"><TextBlock Foreground="#FFFFFF" Text="x"/></StackPanel></Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#FFFFFF", 0.5, "#000000"), "#000000"), f.RatioDark);
        Assert.Equal("#000000", f.Bg);
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

    // ── 短色碼與完整的 WPF 具名色表 ──
    // 舊版只認 #RRGGBB／#AARRGGBB 與 14 個內建具名色，WPF 合法的 #RGB／#ARGB 與表外的具名色（Navy…）
    // 全被歸成 bound-or-gradient：仍計入 unresolved（沒被藏），但原因標錯（標成「執行期才知道」，
    // 其實可以靜態解析），覆蓋率因此被低估、可能誤觸覆蓋率下限

    [Theory]
    [InlineData("Navy", "#000080")]
    [InlineData("navy", "#000080")]                // WPF 的色名不分大小寫
    [InlineData("CornflowerBlue", "#6495ED")]
    [InlineData("#AAA", "#AAAAAA")]                // #RGB
    [InlineData("#f0a", "#FF00AA")]
    [InlineData("#FAAA", "#AAAAAA")]               // #ARGB，alpha F = 不透明
    public void ShortHexAndEveryWpfColourNameAreResolved(string value, string hex)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF"><TextBlock Foreground="{value}" Text="x"/></Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Unresolved);
        Assert.Equal(Wcag.Contrast(hex, "#FFFFFF"), Assert.Single(r.Findings).RatioDark);
    }

    [Fact] // #ARGB 的半透明走既有路徑（字色半透明 → 合法跳過、計數；底色半透明 → 合成）；transparent 不分大小寫
    public void ShortTranslucentHexAndLowercaseTransparent()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <StackPanel Background="#FFFFFF">
              <Border Background="#8000"><TextBlock Foreground="#000" Text="x"/></Border>
              <TextBlock Foreground="#8000" Text="translucent text"/>
              <Border Background="transparent"><TextBlock Foreground="#000" Text="y"/></Border>
            </StackPanel>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Unresolved);
        Assert.Equal(0, r.Skipped);
        Assert.Contains(r.Findings, f => f.Bg == "{#8000 over #FFFFFF}");
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#000000", 0x88 / 255.0, "#FFFFFF"), "#FFFFFF"),
                     Assert.Single(r.Findings, f => f.Fg == "#8000").RatioDark); // 半透明字疊到白底上
        Assert.Contains(r.Findings, f => f.Bg == "#FFFFFF" && f.Fg == "#000");
    }

    [Fact] // 表外的字（不是任何 WPF 色名）照樣是 bound-or-gradient，不能被當成顏色
    public void UnknownColourNameStaysUnresolved()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBlock Foreground="RebeccaPurple" Text="x"/></Grid>""");
        Assert.Equal(1, fx.Run().UnresolvedBy[UnresolvedReason.BoundOrGradient]); // CSS 有、WPF 沒有
    }

    [Fact] // 色盤那一側同一套：短色碼與表外具名色的色票照常收
    public void PaletteAcceptsShortHexAndWpfColourNames()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFF"/>
              <SolidColorBrush x:Key="Fg" Color="Navy"/>
              <Color x:Key="AccentColor">Teal</Color>
              <SolidColorBrush x:Key="Accent" Color="{StaticResource AccentColor}"/>
            </ResourceDictionary>
            """);
        var d = PaletteDetector.Detect(fx.Root);
        Assert.Equal(("#FFFFFF", "#FFFFFF"), d.Palette.Entries["Bg"]);
        Assert.Equal(("#000080", "#000080"), d.Palette.Entries["Fg"]);
        Assert.Equal(("#008080", "#008080"), d.Palette.Entries["Accent"]);
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

    [Fact] // 屬性元素可以用基底類別命名（<Panel.Background>、<Control.Foreground>）—— WPF 照樣認，
           // 舊版只認「元素自己的型別名.屬性」，白字白底報成 21:1（審查素材 h2panel）
    public void PropertyElementNamedAfterABaseClassIsRead()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <Grid>
                <Panel.Background><SolidColorBrush Color="#FFFFFF"/></Panel.Background>
                <TextBlock Foreground="#FFFFFF" Text="white on white via Panel.Background"/>
              </Grid>
              <Border Background="#FFFFFF">
                <Button Content="x"><Control.Foreground><SolidColorBrush Color="#EEEEEE"/></Control.Foreground></Button>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1.0, Assert.Single(r.Findings, f => f.Element == "TextBlock").RatioDark);
        var button = Assert.Single(r.Findings, f => f.Element == "Button");
        Assert.Equal("#EEEEEE", button.Fg);
        Assert.Equal(Category.Fail, button.Category);
    }

    [Fact] // 同上，屬性元素用基底類別的名字（Grid 裡的 <Panel.Background>）：一樣是屬性值、不走成視覺子樹
           // （審查 mutation M6b：只認「本元素型別.屬性」時存活 —— 裡面的浮水印字被當成疊在祖先底上的文字）
    public void BaseClassColorPropertyElementsAreNotWalkedAsVisualChildren()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Grid>
                <Panel.Background>
                  <VisualBrush><VisualBrush.Visual><TextBlock Foreground="#EEEEEE" Text="watermark pattern"/></VisualBrush.Visual></VisualBrush>
                </Panel.Background>
                <TextBlock Foreground="#000000" Text="real text"/>
              </Grid>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal("<VisualBrush>", Assert.Single(r.UnresolvedSites).Value);
    }

    [Fact] // 屬性元素是屬性值、不是視覺子樹：VisualBrush 裡畫的字是背景圖樣的一部分，不能當成疊在祖先底上的文字評分
    public void ColorPropertyElementsAreNotWalkedAsVisualChildren()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border>
                <Border.Background>
                  <VisualBrush><VisualBrush.Visual><TextBlock Foreground="#EEEEEE" Text="watermark pattern"/></VisualBrush.Visual></VisualBrush>
                </Border.Background>
                <TextBlock Foreground="#000000" Text="real text"/>
              </Border>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings); // 沒有「#EEEEEE 疊 #FFFFFF」那筆假的 fail
        var site = Assert.Single(r.UnresolvedSites); // 真正的文字疊在 VisualBrush 上 → 執行期才知道
        Assert.Equal("<VisualBrush>", site.Value);
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

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

    [Fact] // 規則 12：模板根元素自帶背景 —— Style 層沒 Background setter 時的真正底色
    public void TemplateRootBackgroundIsUsedNotAncestor()
    {
        using var fx = new Fixture();
        // Kindling CellDeleteBtn 實證形狀：白 ✕ 疊模板根 Border 的紅底，
        // 少了這條規則會誤配到祖先的白（報 1:1 假警報）
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="CellDeleteBtn" TargetType="Button">
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border Background="#CC3A3A">
                          <TextBlock Text="✕" Foreground="{TemplateBinding Foreground}"/>
                        </Border>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource CellDeleteBtn}"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings, x => x.Element.Contains("⊕"));
        Assert.Equal("#CC3A3A", f.Bg);                          // 模板根的紅，不是祖先的白
        Assert.Equal(Wcag.Contrast("#FFFFFF", "#CC3A3A"), f.RatioDark);
    }

    [Fact] // 規則 13：TargetName=模板根 的觸發 Setter 等同設在宿主上
    public void RootTargetedTriggerBackgroundIsResolved()
    {
        using var fx = new Fixture();
        // Kindling CellDeleteBtn hover 形狀：觸發器用 TargetName 把「模板根」換底＋白字。
        // 規則 13 前：工具只看到白字、誤配祖先白底報 1:1 假警報。
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Foreground" Value="#111111"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border x:Name="bg" Background="#EEEEEE">
                          <ContentPresenter/>
                        </Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="bg" Property="Background" Value="#CC3A3A"/>
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Btn}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        // hover 的字底同出一個 Style 節點 → WalkStyles 定義處檢（正確配對），
        // 鏈路徑讓位 —— 不能再出現「白字疊祖先白底」的假警報
        Assert.DoesNotContain(r.Findings, f => f.Fg == "#FFFFFF" && f.Bg == "#FFFFFF");
        var hover = Assert.Single(r.Findings, f => f.Fg == "#FFFFFF");
        Assert.Equal("#CC3A3A", hover.Bg);
    }

    [Fact] // 規則 13 的邊界：TargetName 指向「非根」的內部元素 → 維持不做
    public void NonRootTargetNameIsStillIgnored()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#000000">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border x:Name="root" Background="#000000">
                          <Border x:Name="inner"><ContentPresenter/></Border>
                        </Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="inner" Property="Background" Value="#FFFFFF"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Btn}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        // inner 的換底解析不到（要模板內部樹的版面關係）—— 不猜，
        // hover 狀態沒有可歸屬的底變更 → 與基礎同組（白字疊模板根黑底）
        Assert.All(r.Findings, f => Assert.Equal("#000000", f.Bg));
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

    [Fact] // 字色看不懂 → unresolved，不是 skipped —— 之前塞錯桶，盲區被藏進「合法豁免」
    public void UnresolvableForegroundCountsAsUnresolvedNotSkipped()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="{Binding Accent}" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.Unresolved);
        Assert.Equal(0, r.Skipped);
    }

    [Fact] // Style 配對看不懂時要計數 —— 之前是裸 continue，從報告徹底消失（靜默退化等於謊報）
    public void UnresolvableStylePairIsCountedNotSilentlyDropped()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Bound" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="{Binding A}"/>
                </Style>
                <Style x:Key="SeeThrough" TargetType="Button">
                  <Setter Property="Background" Value="Transparent"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.Unresolved); // Binding 字色 = 看不懂
        Assert.Equal(1, r.Skipped);    // Transparent 底 = 合法跳過
    }

    [Fact] // MultiTrigger 曾是黑洞：跳過清單認得它、收集處不認得，組合條件狀態整批沒被檢查
    public void MultiTriggerStateIsAudited()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="S" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Style.Triggers>
                    <MultiTrigger>
                      <MultiTrigger.Conditions>
                        <Condition Property="IsMouseOver" Value="True"/>
                        <Condition Property="IsPressed" Value="True"/>
                      </MultiTrigger.Conditions>
                      <Setter Property="Foreground" Value="#404040"/>
                    </MultiTrigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(2, r.Findings.Count); // 基礎 + MultiTrigger 觸發態
        var trigger = Assert.Single(r.Findings, f => f.Element == "Style[S]/trigger");
        Assert.Equal(Wcag.Contrast("#404040", "#000000"), trigger.RatioDark);
    }

    [Fact] // MultiTrigger 條件含 IsEnabled=False → 整個狀態只在停用時成立，WCAG 1.4.3 豁免
    public void MultiTriggerWithDisabledConditionIsExempted()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="S" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Style.Triggers>
                    <MultiTrigger>
                      <MultiTrigger.Conditions>
                        <Condition Property="IsEnabled" Value="False"/>
                        <Condition Property="IsMouseOver" Value="True"/>
                      </MultiTrigger.Conditions>
                      <Setter Property="Foreground" Value="#303030"/>
                    </MultiTrigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.DisabledExempt);
        Assert.Single(r.Findings); // 只剩基礎態
    }

    [Fact] // 「地板」：根容器不寫 Background、靠隱含樣式給底時，整個檔案的文字都無底可配。
           // ScreenToGif 實測 1295/1373 的 unresolved 是這個形狀（38 個根元素只有 6 個寫了背景）。
    public void ImplicitStyleGivesRootItsBackground()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources>
                <Style TargetType="{x:Type Window}">
                  <Setter Property="Background" Value="#111111"/>
                </Style>
              </Application.Resources>
            </Application>
            """);
        fx.File("Main.xaml", """
            <Window>
              <TextBlock Foreground="#FFFFFF" Text="底色來自隱含樣式"/>
            </Window>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("#111111", f.Bg);
        Assert.Equal(0, r.Unresolved);
    }

    [Fact] // 界線要守住：隱含樣式只補「根元素」一格，不是完整的隱含樣式解析。
           // 非根元素照舊沿樹往上找 —— 否則 Foreground 的繼承語意會把配對數炸開。
    public void ImplicitStyleIsNotAppliedToNonRootElements()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources>
                <Style TargetType="{x:Type Border}">
                  <Setter Property="Background" Value="#FFFFFF"/>
                </Style>
              </Application.Resources>
            </Application>
            """);
        fx.File("Main.xaml", """
            <Window Background="#000000">
              <Border>
                <TextBlock Foreground="#FFFFFF" Text="不該套到中間的 Border"/>
              </Border>
            </Window>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("#000000", f.Bg);   // 沿樹找到 Window 的黑，不是 Border 隱含樣式的白
        Assert.Equal(21.0, f.RatioDark);
    }

    [Fact] // 衍生命名的控制項要收斂到基底分類:MetroProgressBar 的 Foreground 是
           // 進度條填色不是文字 —— 全名相等對不上,被當文字用 4.5 要求(MahApps 實證)
    public void DerivedControlNamesClassifyBySuffix()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <MetroProgressBar Foreground="#EEEEEE" Background="#DDDDDD"/>
              <ExtendedTextBlock Foreground="#EEEEEE" Text="衍生的文字控制項還是文字"/>
            </Grid>
            """);
        var r = fx.Run();
        var bar = Assert.Single(r.Findings, f => f.Element.Contains("MetroProgressBar"));
        Assert.False(bar.IsText);                  // 進度條 → 裝飾,不設限
        Assert.Equal(Category.Decorative, bar.Category);
        var text = Assert.Single(r.Findings, f => f.Element.Contains("ExtendedTextBlock"));
        Assert.True(text.IsText);                  // 文字 → 4.5,低對比要報
        Assert.Equal(Category.Fail, text.Category);
    }

    [Fact] // unknown key 要點名 —— 每一個都是死引用、打字錯誤、或偵測漏掉的色盤檔
    public void UnknownPaletteKeysAreNamedInReports()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#000000"/>
              <SolidColorBrush x:Key="Fg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fgg}" Text="打錯字"/>
              <TextBlock Foreground="{DynamicResource Fgg}" Text="又一處"/>
            </Grid>
            """);
        var r = fx.Run();
        var site = r.UnresolvedSites.Where(s => s.Reason == UnresolvedReason.UnknownPaletteKey).ToList();
        Assert.Equal(2, site.Count);
        Assert.All(site, s => Assert.Equal("Fgg", s.Value));           // 記的是鍵名,不是原始字串
        Assert.All(site, s => Assert.True(s.Line > 0));                // 有行號可跳
        Assert.Contains("unknown palette keys (top): Fgg x2", Report.ToConsole(r));
        Assert.Contains("\"Fgg\": 2", Report.ToJson(r));               // summary.unknownKeys
        Assert.Contains("unknown-palette-key", Report.UnresolvedList(r));
    }

    [Fact] // unresolved 要分類 —— 只給總數的話，使用者看不出哪些可補救、哪些是硬邊界
    public void UnresolvedIsBrokenDownByReason()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid>
              <TextBlock Foreground="#FFFFFF" Text="祖先鏈上沒有背景"/>
              <Grid Background="{Binding UserColour}">
                <TextBlock Foreground="#FFFFFF" Text="底色綁執行期"/>
              </Grid>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(2, r.Unresolved);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.NoAncestorBackground]); // 可補救
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);      // 硬邊界
        Assert.Equal(r.Unresolved, r.UnresolvedBy.Values.Sum());                // 細目要湊得回總數
        Assert.Contains("no-ancestor-background 1", Report.ToConsole(r));
    }

    [Fact] // #FFRRGGBB 的 alpha=255 是「完全不透明」，不是半透明 —— 那是 Blend／VS 的
           // 預設輸出格式，WPF 生態最常見的寫法。當半透明會讓字色整批落進 skipped。
           // ScreenToGif 實測：97 個色票有 79 個長這樣，覆蓋率因此掉到 1%。
    public void FullyOpaqueEightDigitHexIsNotTreatedAsTranslucent()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FF000000">
              <TextBlock Foreground="#FFFFFFFF" Text="不透明的白配不透明的黑"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal(21.0, f.RatioDark);   // 等同 #FFFFFF 配 #000000
        Assert.Equal(0, r.Skipped);        // 不該被當半透明跳過
        Assert.Equal(0, r.Unresolved);
    }

    [Fact] // 真半透明（alpha < FF）維持疊底合成，不能被上面那條一起放行
    public void GenuinelyTranslucentEightDigitHexStillComposites()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <Grid Background="#80FFFFFF">
                <TextBlock Foreground="#FFFFFF" Text="白底 50% 疊黑"/>
              </Grid>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        var effective = Wcag.Composite("#FFFFFF", 0x80 / 255.0, "#000000");
        Assert.Equal(Wcag.Contrast("#FFFFFF", effective), f.RatioDark);
    }

    [Fact] // 半透明「色票」同理：#FF 開頭的色票鍵是不透明色，不是疊層
    public void OpaquePaletteKeyWithFfPrefixIsNotComposited()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Panel.Bg" Color="#FF000000"/>
              <SolidColorBrush x:Key="Panel.Fg" Color="#FFFFFFFF"/>
              <SolidColorBrush x:Key="Spare" Color="#FF808080"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Panel.Bg}">
              <TextBlock Foreground="{DynamicResource Panel.Fg}" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal(21.0, f.RatioDark);
        Assert.Equal(0, r.Skipped);
    }

    [Fact] // 對稱維度只在沒過的配對上有意義：21:1 的合格配對被標 both-low（＝色票太弱）是胡說。
           // 人看的報告只印 fail/warn 所以看不到，錯的標籤全流進 JSON 給下游吃。
    public void SymmetryIsNotClassifiedForPassingPairs()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="21:1，完美合格"/>
              <TextBlock Foreground="#111111" Text="1.1:1，破"/>
            </Grid>
            """);
        var r = fx.Run();
        var ok = Assert.Single(r.Findings, f => f.Category == Category.Ok);
        Assert.Equal(Symmetry.NotApplicable, ok.Symmetry); // 舊版會標 BothLow
        var bad = Assert.Single(r.Findings, f => f.Category == Category.Fail);
        Assert.NotEqual(Symmetry.NotApplicable, bad.Symmetry); // 沒過的照分類

        var json = Report.ToJson(r);
        // 合格那筆整個沒有 symmetry 欄位 —— 給錯值不如不給
        Assert.Equal(1, json.Split("\"symmetry\"").Length - 1);
    }

    [Fact] // WCAG 大字級的「粗體」是 weight ≥ 700：SemiBold(600) 不算 —— 舊版子字串匹配誤放寬到 3:1
    public void SemiBoldIsNotBoldForLargeTextExemption()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock FontSize="20" FontWeight="SemiBold" Foreground="#FFFFFF" Text="15pt 600 不是大字級"/>
              <TextBlock FontSize="19" FontWeight="Bold" Foreground="#FFFFFF" Text="14.25pt 700 是大字級"/>
            </Grid>
            """);
        var r = fx.Run();
        var semi = Assert.Single(r.Findings, f => f.Size == "20px");
        Assert.False(semi.Large);      // 600 不到粗體門檻，維持 4.5
        Assert.Equal(4.5, semi.Need);
        var bold = Assert.Single(r.Findings, f => f.Size == "19px");
        Assert.True(bold.Large);       // 700 才算，14.25pt 粗體 → 3.0
        Assert.Equal(3.0, bold.Need);
    }
}

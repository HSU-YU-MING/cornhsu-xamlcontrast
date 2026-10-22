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

    [Theory] // 停用態豁免的範圍要窄：只有「IsEnabled 為 False」才放行（Trigger 的 Property 可帶型別前綴；
             // DataTrigger 看 Binding 的 Path）。範圍一放寬，真實的觸發態問題就會被當成豁免藏起來。
             // DataTrigger／MultiDataTrigger 的 Binding 曾是子字串比對（b.Contains("IsEnabled")）：
             //   {Binding IsEnabledByLicense}、Converter={StaticResource IsEnabledConverter} 都被當成停用態豁免，
             //   fail 歸 0、exit 0，計數器標示的豁免原因是錯的。現在要求 Path 的最後一段恰為 IsEnabled、
             //   來源是元素自己／模板宿主／祖先（RelativeSource Self／TemplatedParent／FindAncestor —— 祖先停用，
             //   子元素一併停用），且沒有 Converter（可能反相）。DataContext 上的 IsEnabled 是 view model 的旗標，
             //   不保證控制項真的停用 —— 不猜，照常評分
    [InlineData("""<Trigger Property="IsEnabled" Value="False">""", "Trigger", true)]
    [InlineData("""<DataTrigger Binding="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=IsEnabled}" Value="False">""", "DataTrigger", true)]
    [InlineData("""<DataTrigger Binding="{Binding Path=(UIElement.IsEnabled), RelativeSource={RelativeSource AncestorType={x:Type Button}}}" Value="False">""", "DataTrigger", true)]
    [InlineData("""<DataTrigger Binding="{Binding IsEnabledByLicense}" Value="False">""", "DataTrigger", false)]
    [InlineData("""<DataTrigger Binding="{Binding IsEnabledByLicense, RelativeSource={RelativeSource Self}}" Value="False">""", "DataTrigger", false)]
    [InlineData("""<DataTrigger Binding="{Binding ViewModel.CanSave, Converter={StaticResource IsEnabledConverter}}" Value="False">""", "DataTrigger", false)]
    [InlineData("""<DataTrigger Binding="{Binding IsEnabled, RelativeSource={RelativeSource Self}, Converter={StaticResource Invert}}" Value="False">""", "DataTrigger", false)]
    [InlineData("""<DataTrigger Binding="{Binding IsEnabled, ElementName=OtherButton}" Value="False">""", "DataTrigger", false)]
    [InlineData("""<DataTrigger Binding="{Binding IsEnabled}" Value="False">""", "DataTrigger", false)] // DataContext 的旗標
    [InlineData("""<MultiDataTrigger><MultiDataTrigger.Conditions><Condition Binding="{Binding IsEnabledByLicense}" Value="False"/></MultiDataTrigger.Conditions>""", "MultiDataTrigger", false)]
    [InlineData("""<Trigger Property="UIElement.IsEnabled" Value="False">""", "Trigger", true)]
    [InlineData("""<DataTrigger Binding="{Binding IsEnabled, RelativeSource={RelativeSource Self}}" Value="False">""", "DataTrigger", true)]
    [InlineData("""<Trigger Property="IsEnabled" Value="True">""", "Trigger", false)]
    [InlineData("""<Trigger Property="IsReadOnly" Value="False">""", "Trigger", false)]
    [InlineData("""<Trigger Property="IsEnabledByLicense" Value="False">""", "Trigger", false)] // 自訂屬性：整字比對，不是子字串
    [InlineData("""<Trigger Property="ParentIsEnabled" Value="False">""", "Trigger", false)]
    [InlineData("""<DataTrigger Binding="{Binding IsBusy}" Value="False">""", "DataTrigger", false)]
    [InlineData("""<DataTrigger Binding="{Binding IsEnabled}" Value="True">""", "DataTrigger", false)]
    [InlineData("""<MultiDataTrigger><MultiDataTrigger.Conditions><Condition Binding="{Binding IsEnabled, RelativeSource={RelativeSource Self}}" Value="False"/><Condition Binding="{Binding IsMouseOver, RelativeSource={RelativeSource Self}}" Value="True"/></MultiDataTrigger.Conditions>""", "MultiDataTrigger", true)]
    [InlineData("""<MultiDataTrigger><MultiDataTrigger.Conditions><Condition Binding="{Binding IsBusy}" Value="False"/></MultiDataTrigger.Conditions>""", "MultiDataTrigger", false)]
    public void DisabledExemptionCoversOnlyIsEnabledFalse(string open, string close, bool exempted)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Style.Triggers>
                    {open}
                      <Setter Property="Foreground" Value="#303030"/>
                    </{close}>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(exempted ? 1 : 0, r.DisabledExempt);
        Assert.Equal(exempted ? 1 : 2, r.Findings.Count);   // 沒豁免的觸發態照常評分（#303030 疊黑 → fail）
        Assert.Equal(exempted ? 0 : 1, r.CountOf(Category.Fail));
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

    [Fact] // 只有一個主題半透明的色票當「字色」：舊版整組歸 skipped —— 另一側（不透明那側）真實的 fail 也跟著消失，
           // 而 skipped 不進覆蓋率分母（覆蓋率照樣 100%、exit 0）。兩側各自疊到自己的底上評分
    public void TranslucentOnOneSideForegroundIsGradedOnBothSides()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#121212"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Secondary" Color="#3A3A3A"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Secondary" Color="#99000000"/>
            </ResourceDictionary>
            """);
        fx.File("Views/Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Secondary}" Text="secondary"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Skipped);
        var f = Assert.Single(r.Findings);
        Assert.Equal(Wcag.Contrast("#3A3A3A", "#121212"), f.RatioDark);                                       // 深色 1.65:1
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#000000", 0x99 / 255.0, "#FFFFFF"), "#FFFFFF"), f.RatioLight);
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // 元素 × Style 鏈與 Style 定義處：半透明字色一樣疊到底上評分（三條路徑同一套）
    public void TranslucentForegroundIsCompositedOnTheStylePathsToo()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <StackPanel xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#000000">
              <StackPanel.Resources>
                <Style x:Key="Pad" TargetType="Button"><Setter Property="Padding" Value="4"/></Style>
                <Style x:Key="Faded" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#33FFFFFF"/>
                </Style>
              </StackPanel.Resources>
              <Button Style="{StaticResource Pad}" Foreground="#33FFFFFF" Content="chain"/>
            </StackPanel>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Skipped);
        var expected = Wcag.Contrast(Wcag.Composite("#FFFFFF", 0x33 / 255.0, "#000000"), "#000000");
        Assert.Equal(expected, Assert.Single(r.Findings, f => f.Element == "Button⊕Style[Pad]/base").RatioDark);
        Assert.Equal(expected, Assert.Single(r.Findings, f => f.Element == "Style[Faded]/base").RatioDark);
    }

    [Theory] // 只有一個主題是半透明的色票（合併式色盤裡只有一個主題用半透明遮罩，是實際會出現的形狀）。
             // 舊版只看深色那側的長度決定走不走 alpha：深色半透明時把 7 碼的淺色值切成 "#E0E0" → 算對比時
             // ArgumentOutOfRangeException、整支崩潰（退出碼跳出契約）；淺色半透明時 alpha 被剝掉、
             // 當成不透明黑 → ratioLight 報 1.3（實際約 3.91）。兩側各帶自己的 alpha（7 碼那側 alpha = 1）
    [InlineData("#80000000", "#E0E0E0")]
    [InlineData("#202020", "#80000000")]
    [InlineData("#FF202020", "#80000000")]
    public void TranslucentOnOneSideOnlyCompositesEachSideOnItsOwn(string darkScrim, string lightScrim)
    {
        using var fx = new Fixture();
        fx.File("Themes/DarkTheme.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#121212"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Scrim" Color="{darkScrim}"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/LightTheme.xaml", $"""
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Fg" Color="#1B1B1B"/>
              <SolidColorBrush x:Key="Scrim" Color="{lightScrim}"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <Border Background="{DynamicResource Scrim}">
                <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
              </Border>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);

        static string Effective(string scrim, string under) => scrim.Length == 9 && !scrim.StartsWith("#FF")
            ? Wcag.Composite("#" + scrim[3..], Convert.ToInt32(scrim[1..3], 16) / 255.0, under)
            : "#" + scrim[^6..];
        Assert.Equal(Wcag.Contrast("#EEEEEE", Effective(darkScrim, "#121212")), f.RatioDark);
        Assert.Equal(Wcag.Contrast("#1B1B1B", Effective(lightScrim, "#FAFAFA")), f.RatioLight);
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

    [Theory] // 規則 13 的模板根名字有 x:Name 與 Name 兩種寫法（FrameworkElement 都認）——
             // Style 定義處（WalkStyles）與元素 × Style 鏈（StyleIndex）兩條路都要認得，
             // 認不得就退回「hover 白字疊模板根原本的淺底」，報 1.16:1 的假 fail
    [InlineData("x:Name")]
    [InlineData("Name")]
    public void RootTargetedTriggerWorksWithEitherNameAttribute(string nameAttr)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $$"""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Foreground" Value="#111111"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border {{nameAttr}}="bg" Background="#EEEEEE"><ContentPresenter/></Border>
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
        Assert.Equal("#CC3A3A", Assert.Single(r.Findings, f => f.Fg == "#FFFFFF").Bg); // 定義處的 hover 配對
        Assert.DoesNotContain(r.Findings, f => f.Fg == "#FFFFFF" && f.Bg == "#EEEEEE"); // 鏈路徑沒有幻影組合
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

    [Fact] // 上一條的另一半：看不懂的是「底」時要點名底的值（unknownKeys 靠它當死引用偵測器）；
           // 半透明的是「字」時疊到 Style 自己的不透明底上照常評分（不是合法跳過）
    public void StyleDefinitionNamesTheSideItCouldNotResolve()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="DeadBg" TargetType="Button">
                  <Setter Property="Background" Value="{DynamicResource Typo}"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                </Style>
                <Style x:Key="FadedFg" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#80FFFFFF"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        var site = Assert.Single(r.UnresolvedSites);
        Assert.Equal(UnresolvedReason.UnknownPaletteKey, site.Reason);
        Assert.Equal("Typo", site.Value);
        Assert.Equal(0, r.Skipped);
        var faded = Assert.Single(r.Findings);
        Assert.Equal("Style[FadedFg]/base", faded.Element);
        Assert.Equal(Wcag.Contrast(Wcag.Composite("#FFFFFF", 0x80 / 255.0, "#000000"), "#000000"), faded.RatioDark);
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

    [Fact] // 地板的另一種寫法：隱含樣式不設 Background setter，而是把底畫在模板根上
    public void ImplicitStyleTemplateRootBackgroundIsAlsoTheFloor()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources>
                <Style TargetType="{x:Type Window}">
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Window">
                        <Border Background="#111111"><ContentPresenter/></Border>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Application.Resources>
            </Application>
            """);
        fx.File("Main.xaml", """
            <Window>
              <TextBlock Foreground="#FFFFFF" Text="底色來自隱含樣式的模板根"/>
            </Window>
            """);
        var r = fx.Run();
        Assert.Equal("#111111", Assert.Single(r.Findings).Bg);
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

    [Fact] // 規則 17(b):文字墊在同格子的 Image 上 —— 底是一張圖,靜態不可知。
           // 之前誤配祖先背景報假警報(HandyControl Carousel:白字疊照片,light=1 假 fail)
    public void TextOverSiblingImageIsUnresolvedNotPairedWithAncestor()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Image Source="photo.jpg"/>
              <TextBlock Foreground="#FFFFFF" Text="疊在照片上的白字"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);   // 之前:白配白 1:1 假 fail
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.OverSiblingContent]);
    }

    [Fact] // 規則 17(a):同格子前面的兄弟是佔滿格子的純色底 → 它才是真正的背板
    public void SiblingSolidBackdropIsUsedInsteadOfAncestor()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border Background="#000000"/>
              <TextBlock Foreground="#FFFFFF" Text="Border 墊底的白字"/>
              <TextBlock Grid.Row="1" Foreground="#111111" Text="不同格子,照配祖先"/>
            </Grid>
            """);
        var r = fx.Run();
        var over = Assert.Single(r.Findings, f => f.Fg == "#FFFFFF");
        Assert.Equal("#000000", over.Bg);       // 兄弟 Border 的黑,不是祖先的白
        Assert.Equal(21.0, over.RatioDark);
        var other = Assert.Single(r.Findings, f => f.Fg == "#111111");
        Assert.Equal("#FFFFFF", other.Bg);      // Row 不同 → 不受影響,配祖先
    }

    [Fact] // 規則 17 防呆:有明確尺寸/非 Stretch 對齊的兄弟(強調色條)不是背板;
           // 排在文字「後面」的兄弟畫在上面,也不是背板
    public void AccentBarAndLaterSiblingsAreNotBackdrops()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <Border Background="#FF0000" Width="4" HorizontalAlignment="Left"/>
              <TextBlock Foreground="#FFFFFF" Text="強調色條不是我的底"/>
              <Border Background="#EEEEEE"/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("#000000", f.Bg);   // 不是紅色條,也不是排在後面的淺色 Border
        Assert.Equal(21.0, f.RatioDark);
    }

    [Theory] // 規則 17(a) 只在子元素會重疊、而且背板會被撐滿的容器裡成立:Grid/自訂 *Panel 是疊層,
             // StackPanel 系與各種排開的 Panel 不是 —— 在那裡把前一個兄弟當背板,就是把旁邊的方塊當成字的底。
             // Canvas 子元素會重疊、但不會被撐滿:Canvas 不套 Stretch,沒尺寸的 Border 實際是 0×0。舊版照樣套 17(a),
             // 把一塊不存在的背板捏造出來(白字白底 1:1 報成 21:1 合格)。Canvas 只套 17(b),見下兩條
    [InlineData("Canvas", false)]
    [InlineData("Grid", true)]
    [InlineData("LayeredPanel", true)]           // 自訂 Panel:保守視為疊層
    [InlineData("StackPanel", false)]
    [InlineData("VirtualizingStackPanel", false)]
    [InlineData("MyStackPanel", false)]          // 衍生的 StackPanel 一樣排開
    [InlineData("DockPanel", false)]
    [InlineData("WrapPanel", false)]
    [InlineData("UniformGrid", false)]
    [InlineData("ToolBarPanel", false)]
    [InlineData("TabPanel", false)]
    public void OnlyOverlappingContainersHaveSiblingBackdrops(string container, bool overlaps)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <{container}>
                <Border Background="#000000"/>
                <TextBlock Foreground="#FFFFFF" Text="x"/>
              </{container}>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal(overlaps ? "#000000" : "#FFFFFF", f.Bg);
    }

    [Theory] // Canvas 裡有尺寸的純色兄弟也不當背板:Canvas.Left/Top 與文字的實際範圍靜態不可知,
             // 它在不在字底下沒辦法判定 —— 與 Grid 裡「有明確尺寸 = 不是佔滿」的強調色條同一個處理
    [InlineData("""<Border Background="#000000"/>""")]
    [InlineData("""<Border Background="#000000" Width="200" Height="40"/>""")]
    [InlineData("""<Rectangle Fill="#000000" Width="200" Height="40" Canvas.Left="10"/>""")]
    public void CanvasSolidSiblingIsNotABackdrop(string sibling)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <Canvas>
                {sibling}
                <TextBlock Foreground="#FFFFFF" Text="x"/>
              </Canvas>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings, x => x.Element == "TextBlock");
        Assert.Equal("#FFFFFF", f.Bg);              // 字真正疊在祖先的白上
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // Canvas 的子元素一律疊在 (0,0),所以它是重疊容器 —— 規則 17(b) 照樣成立:圖片墊底的字底不可知
    public void CanvasSiblingImageMakesTheBackdropUnknowable()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Canvas>
                <Image Source="a.png"/>
                <TextBlock Foreground="#FFFFFF" Text="x"/>
              </Canvas>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);   // 不是白配白 1:1(那是把 Canvas 當成排開的容器、配祖先)
        Assert.Equal(UnresolvedReason.OverSiblingContent, Assert.Single(r.UnresolvedSites).Reason);
    }

    [Theory] // 規則 17 的背板判定:只收「佔滿同一格、確定的純色」;內容元素(圖片/影片)讓底變成不可知
    [InlineData("""<Rectangle Fill="#000000"/>""", "#000000")]                                         // Rectangle 的底是 Fill
    [InlineData("""<Border Background="#000000" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>""", "#000000")]
    [InlineData("""<Border Background="#000000" Height="20"/>""", "#FFFFFF")]                          // 有明確尺寸 = 不是佔滿
    [InlineData("""<Border Background="#000000" Width="20"/>""", "#FFFFFF")]                           // 寬、高各自單獨就足以排除
    [InlineData("""<Border Background="#000000" VerticalAlignment="Top"/>""", "#FFFFFF")]              // 非 Stretch = 不是佔滿
    [InlineData("""<Border Background="#000000" HorizontalAlignment="Left"/>""", "#FFFFFF")]           // 水平、垂直各自單獨就足以排除
    [InlineData("""<Border Grid.Column="1" Background="#000000"/>""", "#FFFFFF")]                      // 不同欄 = 不同格
    [InlineData("""<MediaElement Source="intro.mp4"/>""", null)]                                     // 影片上的字
    [InlineData("""<CachedImage Source="a.png"/>""", null)]                                          // 衍生命名的 *Image
    public void SiblingBackdropCandidates(string sibling, string? expectedBg)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              {sibling}
              <TextBlock Foreground="#FFFFFF" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        var text = r.Findings.Where(f => f.Element == "TextBlock").ToList();
        if (expectedBg is null)
        {
            Assert.Empty(text);
            Assert.Equal(UnresolvedReason.OverSiblingContent, Assert.Single(r.UnresolvedSites).Reason);
        }
        else Assert.Equal(expectedBg, Assert.Single(text).Bg);
    }

    [Fact] // 佔滿格子的兄弟背板是半透明:比照巢狀,疊在它底下的那層上合成。舊版只收不透明的背板,其餘回傳 null,
           // 字改配祖先的底 —— 白 Grid 裡 #CC000000 的兄弟加黑字報 21:1 ok,同樣的顏色寫成巢狀是 1.66:1 fail
    public void TranslucentSiblingBackdropIsCompositedLikeANestedOne()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border Background="#CC000000"/>
              <TextBlock Foreground="#000000" Text="sibling"/>
            </Grid>
            """);
        fx.File("Nested.xaml", """
            <Grid Background="#FFFFFF">
              <Border Background="#CC000000"><TextBlock Foreground="#000000" Text="nested"/></Border>
            </Grid>
            """);
        var r = fx.Run();
        var sibling = Assert.Single(r.Findings, f => f.File == "Main.xaml");
        var nested = Assert.Single(r.Findings, f => f.File == "Nested.xaml");
        Assert.Equal("{#CC000000 over #FFFFFF}", sibling.Bg);
        Assert.Equal(nested.Bg, sibling.Bg);
        Assert.Equal(nested.RatioDark, sibling.RatioDark);  // 1.66:1,不是配祖先白的 21:1
        Assert.Equal(Category.Fail, sibling.Category);
    }

    [Fact] // 兄弟背板靠元素自己的 Opacity 變半透明（<Rectangle Fill="#000000" Opacity="0.2"/> 的遮罩寫法）：
           // 舊版仍當成不透明黑，白字報 21:1，實際是 20% 黑疊白 = #CCCCCC 上的白字 ≈ 1.61:1（審查素材 h15op）
    public void SiblingBackdropWithElementOpacityIsComposited()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Rectangle Fill="#000000" Opacity="0.2"/>
              <TextBlock Foreground="#FFFFFF" Text="over scrim"/>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings, x => x.Element == "TextBlock");
        Assert.Equal(Wcag.Contrast("#FFFFFF", Wcag.Composite("#000000", 0.2, "#FFFFFF")), f.RatioDark);
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // 背板的 Opacity 是 Binding：執行期才知道 → 後面的字計入 unresolved，不是當成不透明
    public void SiblingBackdropWithBoundOpacityIsUnresolved()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Border Background="#000000" Opacity="{Binding ScrimOpacity}"/>
              <TextBlock Foreground="#FFFFFF" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.DoesNotContain(r.Findings, f => f.Element == "TextBlock");
        Assert.Equal(UnresolvedReason.BoundOrGradient, Assert.Single(r.UnresolvedSites).Reason);
    }

    [Theory] // 兄弟背板是 Binding／漸層／不在色盤的鍵:底是執行期才知道的顏色 —— 依「Binding 底不可往上繼承」
             // 同一條原則計入 unresolved,不是配祖先的底給出篤定比值(舊版 Binding 兄弟照報 21:1)
    [InlineData("""<Border Background="{Binding OverlayBrush}"/>""", UnresolvedReason.BoundOrGradient)]
    [InlineData("""<Border><Border.Background><LinearGradientBrush/></Border.Background></Border>""", UnresolvedReason.BoundOrGradient)]
    [InlineData("""<Border Background="{DynamicResource NoSuchKey}"/>""", UnresolvedReason.UnknownPaletteKey)]
    public void UnknowableSiblingBackdropIsUnresolved(string sibling, UnresolvedReason reason)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              {sibling}
              <TextBlock Foreground="#000000" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.DoesNotContain(r.Findings, f => f.Element == "TextBlock");
        Assert.Equal(reason, Assert.Single(r.UnresolvedSites).Reason);
    }

    [Fact] // 半透明兄弟底下沒有可疊的顏色:跟巢狀一樣維持原判定(找不到祖先底 → no-ancestor-background),不猜
    public void TranslucentSiblingWithNothingUnderneathIsLikeTheNestedCase()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid>
              <Border Background="#CC000000"/>
              <TextBlock Foreground="#000000" Text="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal(UnresolvedReason.NoAncestorBackground, Assert.Single(r.UnresolvedSites).Reason);
    }

    [Fact] // 同 brush 配對 = 模板不透明度慣用手法(MaterialDesign 實證):字面 1:1、
           // 執行期是「全濃度字疊 12% 暈染」。模板的 Opacity 動畫靜態看不到 —— 不猜,
           // 歸 unresolved 而不是報必然假的 fail
    public void SameBrushStylePairIsUnresolvedNotFail()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Primary" Color="#673AB7"/>
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <Style x:Key="NavItem" TargetType="ListBoxItem">
                  <Setter Property="Background" Value="{DynamicResource Primary}"/>
                  <Setter Property="Foreground" Value="{DynamicResource Primary}"/>
                </Style>
                <Style x:Key="Real" TargetType="Button">
                  <Setter Property="Background" Value="{DynamicResource Bg}"/>
                  <Setter Property="Foreground" Value="{DynamicResource Fg}"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Single(r.Findings);                                      // 只有 Real 那組受檢
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.SameBrushPair]);
        var site = Assert.Single(r.UnresolvedSites, s => s.Reason == UnresolvedReason.SameBrushPair);
        Assert.Equal("Primary", site.Value);                            // 點名是哪個 brush
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

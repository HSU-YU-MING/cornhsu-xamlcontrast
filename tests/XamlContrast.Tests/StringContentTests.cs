using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>畫面檔（Window）的外殼 —— 擴充方法要擺在靜態類別裡。</summary>
internal static class ScreenFixture
{
    internal static Fixture Screen(this Fixture fx, string body) => fx.File("Main.xaml", $$"""
        <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          {{body}}
        </Window>
        """);
}

/// <summary>
/// 宿主的「字串內容」真正的字色（1.10）與「字色是繼承來的」的計數器。
///
/// 兩個都是 2026-11-25 的外部回報（ZeroCel 前端，1.8.3）—— 工具說 757 組全過，真 App 裡
/// 淺色主題的紫色主要鈕是黑字疊紫底 2.66:1、深色主題的圖層眼睛是黑疊近黑 1.21:1。
/// 兩筆都不是「算錯」，是**漏判**：
///
/// <list type="number">
/// <item><c>Content</c> 是字串時 ContentPresenter 自己產生一顆 TextBlock 來畫它。那顆 TextBlock
///   套得到的隱含樣式，它的 <c>Foreground</c> Setter 贏過「從宿主繼承」—— 所以 Style 上寫的
///   <c>Foreground="White"</c> 在畫面上完全沒有作用。模板產生的元素查隱含樣式時跳過視覺樹，
///   所以只有 <c>Application.Resources</c>（含它合併進來的字典）與模板自己的資源算。</item>
/// <item>文字元素有自己的 Style、那條鏈一個字色都沒設 ⇒ 字色是繼承來的。本工具不模擬繼承
///   也不模擬控制項預設值，但 1.9 之前這一組是**靜默消失**的：沒有 finding、沒有 unresolved、
///   覆蓋率照樣 100%。現在記 <c>inherited-foreground</c>。</item>
/// </list>
///
/// ⚠ 界線的方向很重要：看得到那個機制（模板裡真的有放內容的位置、而且隱含樣式在 App 作用域）
/// 才動手。看不到的維持 1.9 的答案 —— 拿推論去蓋掉元素寫的值就是編造。
/// </summary>
public class StringContentTests
{
    // App.xaml 合併 Styles/Controls.xaml；Controls.xaml 裡有隱含 TextBlock 樣式（深色字）
    // 與一個「宣告白字」的 AccentBtn（模板：Border 畫底，裡面一個 ContentPresenter）
    private static Fixture AppScope(string accentExtra = "", string presenter = "<ContentPresenter/>",
        string implicitType = "TextBlock") => new Fixture()
        .File("App.xaml", """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="Styles/Controls.xaml"/>
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
              </Application.Resources>
            </Application>
            """)
        .File("Styles/Controls.xaml", $$"""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style TargetType="{{implicitType}}">
                <Setter Property="Foreground" Value="#1B1B1B"/>
              </Style>
              <Style x:Key="AccentBtn" TargetType="Button">
                <Setter Property="Background" Value="#534AB7"/>
                <Setter Property="Foreground" Value="White"/>
                {{accentExtra}}
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="Button">
                      <Border Background="{TemplateBinding Background}">
                        {{presenter}}
                      </Border>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
            </ResourceDictionary>
            """);

    [Fact] // 回報的第一種：工具配 White 疊紫（過），畫出來的是 Fg0 疊紫（不過）
    public void TheStringContentOfAStyledButtonTakesTheAppImplicitTextBlockColour()
    {
        using var fx = AppScope().Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""");

        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal("#1B1B1B", f.Fg); // 不是 White —— Style 宣告的那個字色對字串內容沒有作用
        Assert.Equal("#534AB7", f.Bg);
        Assert.Equal(Category.Fail, f.Category);
    }

    [Fact] // 文字節點寫法（<Button>文字</Button>）與 Content="字" 同一件事
    public void ATextNodeCountsAsStringContentToo()
    {
        using var fx = AppScope().Screen("""<Button Style="{StaticResource AccentBtn}">補這一段</Button>""");

        Assert.Equal("#1B1B1B", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // ⚠ ZeroCel 的修法：ContentPresenter 自己的 Resources 離那顆 TextBlock 更近 ——
           // 看不懂它的話工具會對著**已經修好**的程式碼報 fail，等於逼人寫多餘的程式碼
    public void AnImplicitStyleInTheContentPresentersResourcesWins()
    {
        using var fx = AppScope(presenter: """
            <ContentPresenter>
              <ContentPresenter.Resources>
                <Style TargetType="TextBlock"><Setter Property="Foreground" Value="White"/></Style>
              </ContentPresenter.Resources>
            </ContentPresenter>
            """).Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""");

        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal("White", f.Fg);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Fact] // Kindling 的修法（WPF 的標準手法，1.5 起看得懂）：<Style.Resources> 也套得到那顆 TextBlock
    public void AnImplicitStyleInTheStylesResourcesWins()
    {
        using var fx = AppScope("""
            <Style.Resources>
              <Style TargetType="TextBlock"><Setter Property="Foreground" Value="White"/></Style>
            </Style.Resources>
            """).Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""");

        Assert.Equal("White", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // ⚠ 界線：同一份隱含樣式放在 Window.Resources 對模板產生的元素**沒有作用**
           //   （它查隱含樣式時跳過視覺樹）—— 照舊用宿主宣告的白字
    public void AnImplicitStyleOnTheScreenDoesNotReachTheTemplate()
    {
        using var fx = new Fixture()
            .File("Styles/Controls.xaml", """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Style x:Key="AccentBtn" TargetType="Button">
                    <Setter Property="Background" Value="#534AB7"/>
                    <Setter Property="Foreground" Value="White"/>
                    <Setter Property="Template">
                      <Setter.Value>
                        <ControlTemplate TargetType="Button">
                          <Border Background="{TemplateBinding Background}"><ContentPresenter/></Border>
                        </ControlTemplate>
                      </Setter.Value>
                    </Setter>
                  </Style>
                </ResourceDictionary>
                """)
            .File("Main.xaml", """
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Window.Resources>
                    <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#1B1B1B"/></Style>
                  </Window.Resources>
                  <Button Style="{StaticResource AccentBtn}" Content="補這一段"/>
                </Window>
                """);

        Assert.Equal("White", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // ⚠ 界線：字典沒有被 App.xaml 合併進來（C# 在執行期合併的主題字典就是這形狀）——
           //   追不到就不算 App 作用域，維持 1.9 的答案。盲區留著，不拿猜的去填
    public void ADictionaryThatTheApplicationDoesNotMergeIsNotAppScope()
    {
        using var fx = AppScope().Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""")
            .File("App.xaml", """
                <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Application.Resources>
                    <ResourceDictionary>
                      <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="Styles/Other.xaml"/>
                      </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                  </Application.Resources>
                </Application>
                """);

        Assert.Equal("White", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // 元件資源的寫法：pack://application:,,,/Asm;component/… 相對的是組件的根，不是引用它的那個檔
    public void TheAppScopeFollowsAPackUriSource()
    {
        using var fx = AppScope().Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""")
            .File("App.xaml", """
                <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Application.Resources>
                    <ResourceDictionary>
                      <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://application:,,,/MyApp;component/Styles/Controls.xaml"/>
                      </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                  </Application.Resources>
                </Application>
                """);

        Assert.Equal("#1B1B1B", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // 合併是遞移的：App → Theme.xaml → Controls.xaml
    public void TheAppScopeIsTransitive()
    {
        using var fx = AppScope().Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""")
            .File("App.xaml", """
                <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Application.Resources>
                    <ResourceDictionary>
                      <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="Styles/Theme.xaml"/>
                      </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                  </Application.Resources>
                </Application>
                """)
            .File("Styles/Theme.xaml", """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="Controls.xaml"/>
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
                """);

        Assert.Equal("#1B1B1B", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // ⚠ 界線：內容是一棵子樹時宿主的 Foreground 是給它繼承的，那一組照舊是宿主的字色；
           //   標記裡的那顆 TextBlock 自己會被走到（它看得到畫面層的隱含樣式）
    public void ElementChildrenKeepTheHostForeground()
    {
        using var fx = AppScope().Screen("""
            <Button Style="{StaticResource AccentBtn}"><Rectangle Width="8" Height="8" Fill="White"/></Button>
            """);

        Assert.Contains(fx.Run().Findings, f => f.Fg == "White" && f.Bg == "#534AB7");
    }

    [Fact] // ⚠ 界線：Content="{Binding …}" 執行期多半也是字串，但靜態分不出「字串」與「一棵子樹」——
           //   猜錯的方向是把宿主寫的字色換掉，那會編造。維持 1.9 的答案（明文盲區）
    public void BoundContentKeepsTheHostForeground()
    {
        using var fx = AppScope().Screen("""<Button Style="{StaticResource AccentBtn}" Content="{Binding Label}"/>""");

        Assert.Equal("White", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // ⚠ 界線：模板把字畫在 AccessText 上（不是 ContentPresenter）——隱含 TextBlock 樣式對它沒有作用
    public void AStyleWhoseTemplateHasNoContentHolderIsNotTouched()
    {
        using var fx = AppScope(presenter: """<AccessText Text="{TemplateBinding Content}"/>""")
            .Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""");

        Assert.Equal("White", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // ⚠ 界線：RecognizesAccessKey="True" 的 ContentPresenter 產生的是 AccessText（它要處理 _File 這種助記鍵）
           //   —— 隱含 TextBlock 樣式對它沒有作用。語料裡有 259 處這樣寫
    public void AContentPresenterThatRecognizesAccessKeysProducesAccessTextNotTextBlock()
    {
        using var fx = AppScope(presenter: """<ContentPresenter RecognizesAccessKey="True"/>""")
            .Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""");

        Assert.Equal("White", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // 同一個機制、不同型別：助記鍵的位置套得到的是隱含 AccessText 樣式
    public void AnImplicitAccessTextStyleAppliesWhereAccessKeysAreRecognized()
    {
        using var fx = AppScope(presenter: """<ContentPresenter RecognizesAccessKey="True"/>""",
                implicitType: "AccessText")
            .Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""");

        Assert.Equal("#1B1B1B", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // 觸發態也一樣：hover 把字色換成白的，對字串內容照樣沒有作用（Kindling 的 DarkBtn 按下態實例）
    public void ATriggerThatChangesTheHostForegroundDoesNotReachTheStringContent()
    {
        using var fx = AppScope("""
            <Style.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="#4A3A8F"/>
                <Setter Property="Foreground" Value="White"/>
              </Trigger>
            </Style.Triggers>
            """).Screen("""<Button Style="{StaticResource AccentBtn}" Content="補這一段"/>""");

        var hover = Assert.Single(fx.Run().Findings, f => f.Bg == "#4A3A8F");
        Assert.Equal("#1B1B1B", hover.Fg);
    }

    // ── 第二種：字色是繼承來的 ──

    [Fact] // 回報的第二種：ToggleButton 模板裡的 TextBlock 自帶一個只設 Text 的 Style ⇒
           // 隱含樣式不套、自己沒寫字色 ⇒ 字色繼承自沒套樣式的切換鈕（ControlText 近黑）。
           // 1.9 之前整組靜默消失：0 配對、0 unresolved、覆蓋率 100%
    public void TextWithItsOwnColourlessStyleIsCountedInsteadOfDropped()
    {
        using var fx = new Fixture().File("Views/CanvasView.xaml", """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#1A1A1A">
              <ToggleButton>
                <ToggleButton.Template>
                  <ControlTemplate TargetType="ToggleButton">
                    <TextBlock FontSize="13">
                      <TextBlock.Style>
                        <Style TargetType="TextBlock"><Setter Property="Text" Value="(眼睛)"/></Style>
                      </TextBlock.Style>
                    </TextBlock>
                  </ControlTemplate>
                </ToggleButton.Template>
              </ToggleButton>
            </UserControl>
            """);
        var r = fx.Run();

        Assert.Empty(r.Findings);
        var u = Assert.Single(r.UnresolvedSites);
        Assert.Equal(UnresolvedReason.InheritedForeground, u.Reason);
        Assert.Equal("TextBlock⊕Style[inline]", u.Value); // 要去修的是那個樣式：它沒設字色
    }

    [Fact] // 那條鏈有設字色（自己或 BasedOn 基底）→ 照舊評分，不計
    public void AStyleThatDoesSetTheColourIsGradedNotCounted()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#1A1A1A">
              <Window.Resources>
                <Style x:Key="Base" TargetType="TextBlock"><Setter Property="Foreground" Value="#333333"/></Style>
                <Style x:Key="Caption" TargetType="TextBlock" BasedOn="{StaticResource Base}">
                  <Setter Property="FontSize" Value="12"/>
                </Style>
              </Window.Resources>
              <TextBlock Style="{StaticResource Caption}" Text="字色來自基底"/>
            </Window>
            """);
        var r = fx.Run();

        Assert.DoesNotContain(r.UnresolvedSites, u => u.Reason == UnresolvedReason.InheritedForeground);
        Assert.Equal("#333333", Assert.Single(r.Findings).Fg);
    }

    [Fact] // 元素自己寫了字色 → 樣式沒設也無所謂，照舊評分
    public void AnElementThatWritesItsOwnColourIsNotCounted()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#1A1A1A">
              <Window.Resources>
                <Style x:Key="Caption" TargetType="TextBlock"><Setter Property="FontSize" Value="12"/></Style>
              </Window.Resources>
              <TextBlock Style="{StaticResource Caption}" Foreground="#EEEEEE" Text="自己寫了"/>
            </Window>
            """);
        var r = fx.Run();

        Assert.DoesNotContain(r.UnresolvedSites, u => u.Reason == UnresolvedReason.InheritedForeground);
        Assert.Equal("#EEEEEE", Assert.Single(r.Findings).Fg);
    }

    [Fact] // ⚠ 界線：元素**沒有**自己的 Style 時不計 —— 那時隱含樣式還有機會給字色，而
           //   「連隱含樣式也沒有」是另一個量級的形狀（語料 7351 處），要有繼承模型才評得出來
    public void TextWithNoStyleAtAllIsNotCountedYet()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#1A1A1A">
              <TextBlock Text="沒有樣式、沒有字色"/>
            </Window>
            """);
        var r = fx.Run();

        Assert.Empty(r.Findings);
        Assert.Empty(r.UnresolvedSites);
    }

    [Fact] // 不是文字元素的不計：Border／Grid 的 Foreground 不是它自己的文字
    public void ANonTextElementWithAColourlessStyleIsNotCounted()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#1A1A1A">
              <Window.Resources>
                <Style x:Key="Card" TargetType="Border"><Setter Property="Padding" Value="8"/></Style>
              </Window.Resources>
              <Border Style="{StaticResource Card}"/>
            </Window>
            """);

        Assert.DoesNotContain(fx.Run().UnresolvedSites, u => u.Reason == UnresolvedReason.InheritedForeground);
    }
}

using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 隱含樣式（無 x:Key、TargetType 是某個型別的 Style）—— 1.5 把它從「只補檔案根元素的背景」
/// 擴到「每個元素的底與字色」。
///
/// 為什麼值得做：1.4 在這個形狀上會回報**假的通過**（白底上的白字報 21:1 ✓，
/// 見 <see cref="NewRuleTests.ImplicitStyleAppliesToEveryElementNotJustTheRoot"/>），
/// 而漏報之上還有一層更糟的失敗 —— 回報健康。
///
/// 界線（這些測試一起把它釘住）：
///   • WPF 的優先序：元素屬性 &gt; 元素自己的 Style &gt; 隱含樣式。有自己的 Style 的元素**完全不套**隱含樣式。
///   • 1.6 起走的是**鏈路徑**（同具名 Style），所以觸發態也評得到；代價是 finding 的 element 標籤
///     從 <c>TextBlock</c> 變成 <c>TextBlock⊕Style[implicit TextBlock]/base</c>，也就是 baseline 的鍵變了
///     （1.6 把 <see cref="Baseline.KeyVersion"/> 從 1 遞增到 2）。
///   • 只有「真的會影響顏色」的隱含樣式走鏈路徑：只設 Margin／FontSize 的不走，否則每一筆 finding 的
///     element 都掛上 ⊕Style[implicit …]，白白動掉一批鍵。
///   • 字與底都出自同一個隱含樣式的那一組由定義處評，使用處不再評 —— 否則同一組算兩次。
/// </summary>
public class ImplicitStyleTests
{
    // 1.6 起隱含樣式走鏈路徑，element 標籤帶 ⊕Style[implicit …]（= baseline 的鍵，KeyVersion 2）
    private const string TextBlockBase = "TextBlock⊕Style[implicit TextBlock]/base";

    [Fact] // 沒寫 Foreground 的文字，字色來自隱含樣式 —— 1.4 整組不在報告裡（連 unresolved 都沒有）
    public void TheForegroundFromAnImplicitStyleIsGraded()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#BBBBBB"/>
                </Style>
              </Window.Resources>
              <TextBlock Text="字色來自隱含樣式"/>
            </Window>
            """);
        var r = fx.Run();

        var f = Assert.Single(r.Findings);
        Assert.Equal("#BBBBBB", f.Fg);
        Assert.Equal("#FFFFFF", f.Bg);
        Assert.Equal(Category.Fail, f.Category);
        Assert.Equal(TextBlockBase, f.Element); // 1.6：走鏈路徑，標籤帶 ⊕Style[implicit …]（KeyVersion 2）
    }

    [Fact] // 界線：不影響顏色的隱含樣式（只設 Margin／FontSize）不走鏈路徑 ——
           // 走了只會讓每一筆 finding 的 element 都掛上 ⊕Style[implicit …]，白白動掉一批 baseline 的鍵
    public void AnImplicitStyleThatDoesNotTouchColourLeavesTheLabelAlone()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="TextBlock">
                  <Setter Property="Margin" Value="4"/>
                  <Setter Property="FontSize" Value="13"/>
                </Style>
              </Window.Resources>
              <TextBlock Foreground="#BBBBBB" Text="字色是自己寫的"/>
            </Window>
            """);
        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal("TextBlock", f.Element); // 標籤不動 = 這一類專案的 baseline 鍵不會無謂地變
        Assert.Equal(13, f.FontSize);         // 但 FontSize 照舊從隱含樣式讀（1.5 的門檻判定）
    }

    [Fact] // 兩個主題各宣告一個隱含樣式 → 各評各的（@dark／@light），同具名 Style 的 perSide
    public void TwoThemesWithTheirOwnImplicitStyleAreGradedSeparately()
    {
        using var fx = new Fixture()
            .File("Themes/Dark.xaml", """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#111111"/>
                  <SolidColorBrush x:Key="Dim" Color="#2A2A2A"/>
                  <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
                  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#222222"/></Style>
                </ResourceDictionary>
                """)
            .File("Themes/Light.xaml", """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
                  <SolidColorBrush x:Key="Dim" Color="#E8E8E8"/>
                  <SolidColorBrush x:Key="Fg" Color="#212121"/>
                  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#EEEEEE"/></Style>
                </ResourceDictionary>
                """)
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <TextBlock Text="深淺各一個隱含樣式"/>
                </Window>
                """);
        var labels = fx.Run().Findings.Select(f => f.Element).OrderBy(x => x).ToList();
        Assert.Equal(
            ["TextBlock⊕Style[implicit TextBlock]@dark/base", "TextBlock⊕Style[implicit TextBlock]@light/base"],
            labels);
    }

    [Fact] // WPF：元素有自己的 Style 時，隱含樣式整個不套用（不是「補沒設到的屬性」）
    public void AnElementWithItsOwnStyleDoesNotTakeTheImplicitOne()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="Border">
                  <Setter Property="Background" Value="#000000"/>
                </Style>
                <Style x:Key="Plain" TargetType="Border"/>
              </Window.Resources>
              <Border Style="{StaticResource Plain}">
                <TextBlock Foreground="#111111" Text="底是 Window 的白，不是隱含樣式的黑"/>
              </Border>
            </Window>
            """);

        Assert.Equal("#FFFFFF", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 元素自己寫的屬性最大
    public void ALocalAttributeBeatsTheImplicitStyle()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#BBBBBB"/>
                </Style>
              </Window.Resources>
              <TextBlock Foreground="#000000" Text="本地值贏"/>
            </Window>
            """);

        Assert.Equal("#000000", Assert.Single(fx.Run().Findings).Fg);
    }

    [Fact] // 字與底都出自同一個隱含樣式 → 定義處評一次就好。兩邊都記就是同一組算兩次（覆蓋率與 fail 數都會虛增）
    public void APairWhollyFromTheImplicitStyleIsGradedOnce()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="TextBlock">
                  <Setter Property="Background" Value="#777777"/>
                  <Setter Property="Foreground" Value="#888888"/>
                </Style>
              </Window.Resources>
              <TextBlock Text="字與底同源"/>
            </Window>
            """);
        var r = fx.Run();

        var f = Assert.Single(r.Findings);
        Assert.Equal("#888888", f.Fg);
        Assert.Equal("#777777", f.Bg);
        Assert.Equal(1, r.Pairs);
    }

    [Fact] // 隱含樣式把字放大到大字級：門檻是 3:1 不是 4.5:1 —— 不看它就會報一筆站不住的 fail
    public void TheFontSizeFromTheImplicitStyleSetsTheThreshold()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="TextBlock">
                  <Setter Property="FontSize" Value="32"/>
                  <Setter Property="Foreground" Value="#767676"/>
                </Style>
              </Window.Resources>
              <TextBlock Text="32 DIP = 24pt，大字級"/>
            </Window>
            """);

        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal(3.0, f.Need);
        Assert.True(f.Large);
        Assert.Equal(Category.Ok, f.Category); // #767676 on #FFFFFF = 4.54:1 —— 大字級過得去
    }

    [Fact] // 底色沿用到子元素：隱含樣式畫的底，子元素的文字要配到它
    public void TheBackgroundFromAnImplicitStyleBecomesTheBackdropForChildren()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="Border">
                  <Setter Property="Background" Value="#202020"/>
                </Style>
              </Window.Resources>
              <Border>
                <TextBlock Foreground="#EEEEEE" Text="深底上的亮字"/>
              </Border>
            </Window>
            """);

        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal("#202020", f.Bg);
        Assert.Equal(Category.Ok, f.Category);
    }

    [Fact] // 兩個主題的隱含樣式給不同的字色：元素路徑一個元素只有一個字色，表達不了「深淺不同源」——
           // 拿其中一邊去配兩欄就是編造。不評（而且不能假裝兩欄相同）
    public void TwoThemesWithDifferentImplicitForegroundsAreNotInvented()
    {
        using var fx = new Fixture()
            .File("Themes/Dark.xaml", """
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#111111"/>
                  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#EEEEEE"/></Style>
                </ResourceDictionary>
                """)
            .File("Themes/Light.xaml", """
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
                  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#222222"/></Style>
                </ResourceDictionary>
                """)
            .File("Main.xaml", """
                <Window Background="{DynamicResource Bg}">
                  <TextBlock Text="兩個主題各自定義隱含樣式"/>
                </Window>
                """);

        Assert.Empty(fx.Run().Findings);
    }

    [Fact] // 1.6：隱含樣式的**觸發態**也評得到（走的是跟具名 Style 同一條鏈路徑）。
           // 1.5 刻意不評，因為標籤 = baseline 的鍵會變；1.6 遞增 KeyVersion 一起做掉
    public void TriggerStatesOfAnImplicitStyleAreGraded()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#000000"/>
                  <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Foreground" Value="#F0F0F0"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Window.Resources>
              <TextBlock Text="hover 的字色也評"/>
            </Window>
            """);
        var r = fx.Run();

        // 基礎態：黑字白底 21:1
        var f = Assert.Single(r.Findings, x => x.Element == TextBlockBase);
        Assert.Equal("#000000", f.Fg);
        Assert.Equal(Category.Ok, f.Category);
        // hover 那一組（#F0F0F0 on #FFFFFF = 1.13:1）—— 1.5 看不到它，1.6 報出來
        var hover = Assert.Single(r.Findings, x => x.Fg == "#F0F0F0");
        Assert.Equal("TextBlock⊕Style[implicit TextBlock]/trigger", hover.Element);
        Assert.Equal(Category.Fail, hover.Category);
    }

    [Fact] // 作用範圍：巢狀的 Resources 裡的隱含樣式只管那個元素的子樹。
           //
           // ⚠ 這一條是實測踩出來的（QuillNest，1.5 開發中）：第一版只用「同一個檔」當範圍，
           // 於是標籤膠囊模板深處 ContentPresenter.Resources 裡的
           // `<Style TargetType="TextBlock"><Setter Foreground="White">` 套到了同一個檔案裡
           // 毫不相干的工具列文字上 —— 報出三筆「白字配白卡片」的假 fail。
           // 一個假警報就夠讓人關掉守門員，而這條規則的整個價值是「它說的都是真的」。
    public void AnImplicitStyleInANestedScopeDoesNotLeakToTheWholeFile()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <StackPanel>
                <Border Background="#C62828">
                  <ContentPresenter>
                    <ContentPresenter.Resources>
                      <Style TargetType="TextBlock">
                        <Setter Property="Foreground" Value="White"/>
                      </Style>
                    </ContentPresenter.Resources>
                    <TextBlock Text="膠囊裡的白字（紅底，合格）"/>
                  </ContentPresenter>
                </Border>
                <TextBlock Text="工具列的字：不吃上面那個隱含樣式"/>
              </StackPanel>
            </Window>
            """);
        var r = fx.Run();

        // 範圍內那一筆照樣評得到（白字配紅底）
        var inScope = Assert.Single(r.Findings);
        Assert.Equal("White", inScope.Fg);
        Assert.Equal("#C62828", inScope.Bg);
        // 範圍外那個 TextBlock 沒有字色可言 —— 不能拿 White 去配 Window 的白底報一筆假的 1:1
        Assert.DoesNotContain(r.Findings, f => f.Bg == "#FFFFFF");
    }

    [Fact] // 同一個型別、巢狀兩層：最近的資源範圍贏（WPF 從最近的往外找）
    public void TheInnermostScopeWins()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Window.Resources>
                <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#000000"/></Style>
              </Window.Resources>
              <StackPanel>
                <Grid Background="#111111">
                  <Grid.Resources>
                    <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#EEEEEE"/></Style>
                  </Grid.Resources>
                  <TextBlock Text="深色區塊裡用亮字"/>
                </Grid>
                <TextBlock Text="外面用黑字"/>
              </StackPanel>
            </Window>
            """);
        var r = fx.Run();

        Assert.Equal("#EEEEEE", Assert.Single(r.Findings, f => f.Bg == "#111111").Fg);
        Assert.Equal("#000000", Assert.Single(r.Findings, f => f.Bg == "#FFFFFF").Fg);
    }

    // ── Style 的 <Style.Resources>（1.5 後半）────────────────────────────────────────────
    // WPF 修「全域隱含樣式蓋掉按鈕字色」的標準手法：在那個 Style 的作用範圍裡把字色蓋回來。
    // ⚠ 這一段是被自己的輸出逼出來的：1.5 前半抓到 Kindling 的 AccentBtn 標籤只有 2.49:1，
    //   而照這個標準手法修好之後，工具**看不到**那個修正（它的作用範圍不是行號區間，是「用了這個 Style 的地方」），
    //   逼得那邊得在元素上再明寫一次字色。守門員看不懂正確的修法，就是在逼人寫多餘的程式碼。

    private const string ScopedFixture = """
        <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
          <SolidColorBrush x:Key="Fg0" Color="#1B1B1B"/>
          <SolidColorBrush x:Key="Purple" Color="#534AB7"/>
          <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource Fg0}"/>
          </Style>
          <Style x:Key="AccentBtn" TargetType="Button">
            <Setter Property="Background" Value="{DynamicResource Purple}"/>
            <Setter Property="Foreground" Value="White"/>
            <Style.Resources>
              <Style TargetType="TextBlock">
                <Setter Property="Foreground" Value="White"/>
              </Style>
            </Style.Resources>
          </Style>
        </ResourceDictionary>
        """;

    [Fact] // 套了那個 Style 的元素,它的內容吃得到 Style.Resources 裡的隱含樣式 —— 而且範圍不外洩
    public void StyleScopedResourcesApplyToTheContentAndNowhereElse()
    {
        using var fx = new Fixture().File("Themes/Theme.xaml", ScopedFixture)
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <StackPanel>
                    <Button Style="{StaticResource AccentBtn}"><TextBlock Text="按鈕裡"/></Button>
                    <TextBlock Text="按鈕外"/>
                  </StackPanel>
                </Window>
                """);
        var r = fx.Run();

        // 按鈕裡：白字疊紫底（6.93:1）—— 這就是 AccentBtn 設計意圖的樣子
        var inside = Assert.Single(r.Findings, f => f.Element == TextBlockBase && f.Bg == "{Purple}");
        Assert.Equal("White", inside.Fg);
        Assert.Equal(Category.Ok, inside.Category);
        // 按鈕外：照舊吃全域的隱含樣式（Fg0 疊白底）
        var outside = Assert.Single(r.Findings, f => f.Element == TextBlockBase && f.Bg == "{Bg}");
        Assert.Equal("{DynamicResource Fg0}", outside.Fg);
    }

    [Fact] // 這就是 1.5 前半那筆真 bug 的「修好之後」：沒有 Style.Resources 的話標籤是深色字疊紫底 2.49:1
    public void WithoutTheScopedResourcesTheLabelIsTheFailingPair()
    {
        using var fx = new Fixture()
            .File("Themes/Theme.xaml", ScopedFixture.Replace("""
                    <Style.Resources>
                      <Style TargetType="TextBlock">
                        <Setter Property="Foreground" Value="White"/>
                      </Style>
                    </Style.Resources>
                """, ""))
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <Button Style="{StaticResource AccentBtn}"><TextBlock Text="按鈕裡"/></Button>
                </Window>
                """);

        var label = Assert.Single(fx.Run().Findings, f => f.Element == TextBlockBase);
        Assert.Equal("{DynamicResource Fg0}", label.Fg);   // 按鈕上的 Foreground="White" 贏不過隱含樣式
        Assert.Equal("{Purple}", label.Bg);
        Assert.Equal(Category.Fail, label.Category);       // 2.49:1
    }

    [Fact] // 離元素最近的贏：Style.Resources 勝過檔案／全域的隱含樣式（WPF 的資源查找方向）
    public void TheStyleScopedOneBeatsTheGlobalImplicitStyle()
    {
        using var fx = new Fixture().File("Themes/Theme.xaml", ScopedFixture)
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <Button Style="{StaticResource AccentBtn}"><TextBlock Text="按鈕裡"/></Button>
                </Window>
                """);

        Assert.Equal("White", Assert.Single(fx.Run().Findings, f => f.Element == TextBlockBase).Fg);
    }

    [Fact] // BasedOn:基底的 Style.Resources 也算，衍生自己宣告的蓋掉基底的
    public void ScopedResourcesAreInheritedThroughBasedOnAndOverriddenByTheDerivedStyle()
    {
        using var fx = new Fixture()
            .File("Themes/Theme.xaml", ScopedFixture.Replace("</ResourceDictionary>", """
                  <Style x:Key="AccentBtnQuiet" TargetType="Button" BasedOn="{StaticResource AccentBtn}"/>
                  <Style x:Key="AccentBtnLoud" TargetType="Button" BasedOn="{StaticResource AccentBtn}">
                    <Style.Resources>
                      <Style TargetType="TextBlock">
                        <Setter Property="Foreground" Value="#FFD700"/>
                      </Style>
                    </Style.Resources>
                  </Style>
                </ResourceDictionary>
                """))
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <StackPanel>
                    <Button Style="{StaticResource AccentBtnQuiet}"><TextBlock Text="繼承基底的白字"/></Button>
                    <Button Style="{StaticResource AccentBtnLoud}"><TextBlock Text="自己蓋成金色"/></Button>
                  </StackPanel>
                </Window>
                """);
        var labels = fx.Run().Findings.Where(f => f.Element == TextBlockBase).Select(f => f.Fg).ToList();

        Assert.Contains("White", labels);     // 繼承基底的
        Assert.Contains("#FFD700", labels);   // 衍生自己宣告的贏
    }

    [Fact] // 隱含樣式套得到的型別一個都沒有時，它的配對執行期不存在 —— 定義處照舊把看不懂的部分記出來
    public void AnImplicitStyleWithNoMatchingElementIsStillDisclosedAtItsDefinition()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style TargetType="ToggleButton">
                  <Setter Property="Background" Value="Transparent"/>
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                </Style>
              </Grid.Resources>
              <TextBlock Foreground="#000000" Text="沒有任何 ToggleButton"/>
            </Grid>
            """);
        var r = fx.Run();

        var site = Assert.Single(r.UnresolvedSites);
        Assert.Equal(UnresolvedReason.TranslucentUncomposited, site.Reason);
        Assert.Equal(0, r.Skipped); // 不能是 skipped：那會讓它不進覆蓋率的分母
    }
}

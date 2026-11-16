using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 模板裡哪一層畫底（1.6）—— 帶模板的控制項，字畫在模板**內部**某一層上面。
///
/// 為什麼值得做：1.5 只看模板根（<c>TemplateRootBg</c>），根沒設底就讓字去配**祖先**的底。
/// 實測（HandyControl 的 <c>&lt;hc:Shield Color="#1182c3"/&gt;</c>）：模板根是沒設底的 Grid，
/// 白字其實畫在兩個 Border 上（<c>#555</c> 與 <c>{TemplateBinding Color}</c>），配祖先的白底報出
/// 三筆 1:1 的假 fail。1.5 的對策是「帶模板、底又不是它自己給的就不評」（而且只對隱含樣式），
/// 1.6 改成把那一層真的找出來。
///
/// 界線（這些測試一起把它釘住）：
///   • 這一格排在**宿主自己的 Background 之後**：元素明寫了 Background 時那就是答案。
///     往前挪的話 MaterialDesign 的 MenusAndToolBars 會把 <c>&lt;MenuItem Background="Secondary"&gt;</c>
///     的字配到外層選單的 Primary 底上 —— 元素寫的值被一個推論蓋掉。
///   • 只收「在模板裡找得到畫底的祖先」的內容位置。找不到的不收（純加法）——
///     MaterialDesign 的按鈕模板把底畫在內容的**兄弟** Border 上，當成「模板裡沒有底」會讓
///     宿主的 Background Setter 整個被跳過，43 筆驗過的 finding 跟著消失。
///   • 那一層的顏色靜態看不到 → <c>template-painted</c>，不是退回祖先（祖先沒露出來）。
/// </summary>
public class TemplateLayerTests
{
    /// <summary>HandyControl 的 Shield 形狀：模板根是沒設底的 Grid，內容分別畫在兩個 Border 上。</summary>
    private const string ShieldStyle = """
        <Style x:Key="Shield" TargetType="Badge">
          <Setter Property="Foreground" Value="#FFFFFF"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Badge">
                <Grid>
                  <Border Grid.Column="0" Background="#555555">
                    <TextBlock Text="{TemplateBinding Subject}"/>
                  </Border>
                  <Border Grid.Column="1" Background="{TemplateBinding Color}">
                    <ContentPresenter/>
                  </Border>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    private static Fixture Shield(string element) => new Fixture().File("Main.xaml", $"""
        <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
          <Grid.Resources>
            {ShieldStyle}
          </Grid.Resources>
          {element}
        </Grid>
        """);

    [Fact] // 兩層都報：執行期它們同時存在（不是互斥的候選），而且都不是祖先的白
    public void BothLayersThatHoldContentAreGraded()
    {
        using var fx = Shield("""<Badge Style="{StaticResource Shield}" Color="#1182C3" Subject="build"/>""");
        var r = fx.Run();

        var bgs = r.Findings.Select(f => f.Bg).OrderBy(b => b).ToList();
        Assert.Equal(["#1182C3", "#555555"], bgs);
        // 祖先的白底是 1.5 的那個假 fail（白字配白底 1:1）—— 不能再出現
        Assert.DoesNotContain(r.Findings, f => f.Bg == "#FFFFFF");
        Assert.Empty(r.UnresolvedSites);
    }

    [Fact] // {TemplateBinding P} 是「宿主自己的 P」，不是一個看不懂的顏色 —— 要回到元素上查
    public void ATemplateBoundLayerResolvesAgainstTheHostsOwnProperty()
    {
        using var fx = Shield("""<Badge Style="{StaticResource Shield}" Color="#1182C3"/>""");
        var finding = Assert.Single(fx.Run().Findings, f => f.Bg == "#1182C3");
        Assert.Equal(Wcag.Contrast("#FFFFFF", "#1182C3"), finding.RatioDark);
    }

    [Fact] // 宿主沒設那個屬性 = 控制項自己的預設值（函式庫寫在 C# metadata 裡）→ 靜態不可知。
           // ⚠ 不能退回祖先：模板畫了一層底在中間，祖先的顏色根本沒露出來
    public void AnUnsetHostPropertyMakesTheLayerUnknowable()
    {
        using var fx = Shield("""<Badge Style="{StaticResource Shield}"/>""");
        var r = fx.Run();

        Assert.Contains(r.UnresolvedSites, s => s.Reason == UnresolvedReason.TemplatePainted);
        Assert.DoesNotContain(r.Findings, f => f.Bg == "#FFFFFF"); // 祖先的白不是退路
        Assert.Contains(r.Findings, f => f.Bg == "#555555");       // 另一層照樣算得出來
    }

    [Fact] // 元素自己寫了 Background → 那就是答案，模板分層不能蓋掉它。
           // ⚠ MaterialDesign 的 MenusAndToolBars 實證：挪到前面的話，巢狀 MenuItem 自己的底被忽略，
           //   字被配到外層選單的底上 —— 元素明寫的值被一個推論蓋掉，等於編造
    public void TheHostsOwnBackgroundWinsOverTheTemplateLayer()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Chip" TargetType="Button">
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Grid>
                          <Border Background="{TemplateBinding Background}">
                            <ContentPresenter/>
                          </Border>
                        </Grid>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Chip}" Background="#1182C3" Content="x"/>
            </Grid>
            """);
        var finding = Assert.Single(fx.Run().Findings);
        Assert.Equal("#1182C3", finding.Bg);
    }

    [Fact] // 模板裡的底綁 {TemplateBinding Background} 而宿主沒設 → 實質上沒有底，穿透到祖先。
           // ⚠ 這不是「看不懂」：少了這一條，ScreenToGif 的 Editor.xaml 有 163 組算得出來的配對變成 unresolved
    public void AnUnsetBackgroundPiercesToTheAncestor()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Chip" TargetType="Button">
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Grid>
                          <Border Background="{TemplateBinding Background}">
                            <ContentPresenter/>
                          </Border>
                        </Grid>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Chip}" Content="x"/>
            </Grid>
            """);
        var r = fx.Run();
        var finding = Assert.Single(r.Findings);
        Assert.Equal("#FFFFFF", finding.Bg);
        Assert.Empty(r.UnresolvedSites);
    }

    [Fact] // 內容的底畫在它的**兄弟**上（MaterialDesign 的按鈕模板）：模板裡找不到「畫底的祖先」→
           // 這一格不收，照舊走宿主的 Background Setter。⚠ 當成「模板裡沒有底」的話，
           // MaterialDesign 一口氣少掉 43 筆逐筆驗過的 finding（含 1.4 驗過的扁平鈕 2.63:1）
    public void ASiblingLayerIsNotAnAncestorSoThePrecedenceIsUnchanged()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Raised" TargetType="Button">
                  <Setter Property="Background" Value="#1182C3"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Grid>
                          <Border Background="{TemplateBinding Background}"/>
                          <ContentPresenter/>
                        </Grid>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Raised}" Content="x"/>
            </Grid>
            """);
        var finding = Assert.Single(fx.Run().Findings);
        Assert.Equal("#1182C3", finding.Bg);
    }

    [Fact] // 巢狀模板裡的內容位置是**別的控制項**的內容（ComboBox 模板裡 ToggleButton 的模板、ItemTemplate）
    public void ContentHostsInsideANestedTemplateDoNotCount()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Combo" TargetType="ComboBox">
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="ComboBox">
                        <Grid>
                          <ToggleButton>
                            <ToggleButton.Template>
                              <ControlTemplate TargetType="ToggleButton">
                                <Border Background="#1182C3"><ContentPresenter/></Border>
                              </ControlTemplate>
                            </ToggleButton.Template>
                          </ToggleButton>
                        </Grid>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <ComboBox Style="{StaticResource Combo}"/>
            </Grid>
            """);
        // 外層 ComboBox 的字不在那個 ToggleButton 模板的 Border 上 —— 不能拿 #1182C3 去配
        Assert.DoesNotContain(fx.Run().Findings, f => f.Bg == "#1182C3");
    }

    [Fact] // 標記裡的子元素放進模板的 ContentPresenter，所以它們的底是那一層
    public void MarkupChildrenSitOnThePrimaryLayer()
    {
        using var fx = Shield("""
            <Badge Style="{StaticResource Shield}" Color="#1182C3">
              <TextBlock Foreground="#2A7FBF" Text="子元素"/>
            </Badge>
            """);
        var child = Assert.Single(fx.Run().Findings, f => f.Fg == "#2A7FBF");
        Assert.Equal("#1182C3", child.Bg); // ContentPresenter 那一層，不是祖先的白
    }

    [Fact] // 把宿主字色畫成圖形的 Fill 也是一個內容位置（圖示按鈕的 Path）
    public void AShapeThatPaintsTheHostForegroundIsAContentHost()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="IconBtn" TargetType="Button">
                  <Setter Property="Foreground" Value="#8A8A8A"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Grid>
                          <Border Background="#707070">
                            <Path Fill="{TemplateBinding Foreground}"/>
                          </Border>
                        </Grid>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource IconBtn}"/>
            </Grid>
            """);
        var finding = Assert.Single(fx.Run().Findings);
        Assert.Equal("#707070", finding.Bg);
    }

    // ── 語料逼出來的三個「不要再編造」──
    //    三個都是 1.6 把隱含樣式接上逐狀態路徑之後才爆出來的，但三個都同時修掉具名 Style 的同一個毛病。

    [Fact] // XAML 的布林值不分大小寫：Value="false" 也是停用態（WCAG 1.4.3 豁免）。
           // ⚠ 舊版逐字比 "False"，寫小寫的停用態被當成普通狀態評分 —— ScreenToGif 的隱含 ScrollViewer
           //   樣式（Value="false" 設灰字）報出 2.14:1 的假 fail，MaterialDesign 有 16 處同形狀
    public void ALowercaseDisabledTriggerIsStillExempt()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <Grid.Resources>
                <Style TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#000000"/>
                  <Style.Triggers>
                    <Trigger Property="IsEnabled" Value="false">
                      <Setter Property="Foreground" Value="#F0F0F0"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
              <TextBlock Text="停用態的灰字"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.DisabledExempt);
        Assert.DoesNotContain(r.Findings, f => f.Fg == "#F0F0F0");
    }

    [Fact] // 觸發器把模板**內部**元素的底重畫了 → 那個狀態下字底下那一層靜態不可知。
           // ⚠ 舊版把那個 Setter 丟掉、照舊用基礎狀態的底評分 —— ScreenToGif 的 AwareTabItem
           //   選取態把 ContentBorder 重畫成 #90FFFFFF，黑字其實在那層淺底上，工具卻拿深色的 Level3
           //   去配，報 1.46:1。28 筆假 fail
    public void ATriggerThatRepaintsAnInnerLayerIsNotGradedAgainstTheOldBackground()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#111111">
              <Grid.Resources>
                <Style x:Key="Tab" TargetType="TabItem">
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="TabItem">
                        <Grid x:Name="Panel">
                          <Border x:Name="ContentBorder" Background="#222222">
                            <ContentPresenter/>
                          </Border>
                        </Grid>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsSelected" Value="True">
                            <Setter Property="Foreground" Value="#000000"/>
                            <Setter TargetName="ContentBorder" Property="Background" Value="#F0F0F0"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <TabItem Style="{StaticResource Tab}" Header="x"/>
            </Grid>
            """);
        var r = fx.Run();
        // 基礎態照樣評（#EEEEEE 疊在模板那一層的 #222222 上）
        Assert.Contains(r.Findings, f => f.Fg == "#EEEEEE" && f.Bg == "#222222");
        // 選取態：ContentBorder 被重畫成 #F0F0F0，黑字其實在那上面。
        // ⚠ 1.5 之前拿已經被蓋掉的 #222222 去配（編出來的）；現在知道重畫的是哪一個元素，
        //   而它正好就是畫這個內容位置的那一層 → 算得出來。
        //   這一筆原本的保證沒有變：**不能拿已經被蓋掉的 #222222 去配選取態的黑字**。
        Assert.Contains(r.Findings, f => f.Fg == "#000000" && f.Bg == "#F0F0F0");
        Assert.DoesNotContain(r.Findings, f => f.Fg == "#000000" && f.Bg == "#222222");
        Assert.DoesNotContain(r.UnresolvedSites, s => s.Reason == UnresolvedReason.TemplatePainted);
    }

    [Fact] // 巢狀 Style（ItemContainerStyle 之類）裡的觸發器是**別的元素**的狀態 —— 宿主沒有這一組。
           // ⚠ 舊版照樣當成宿主的狀態評分：ScreenToGif 的 ListView 被套上 ListViewItem 的
           //   hover／選取底色，報出 ListView 自己根本不存在的配對
    public void TriggersInsideANestedStyleAreNotTheHostsStates()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Frames" TargetType="ListView">
                  <Setter Property="Foreground" Value="#333333"/>
                  <Setter Property="ItemContainerStyle">
                    <Setter.Value>
                      <Style TargetType="ListViewItem">
                        <Style.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="#1182C3"/>
                          </Trigger>
                        </Style.Triggers>
                      </Style>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <ListView Style="{StaticResource Frames}"/>
            </Grid>
            """);
        // ListView 自己不會進 ListViewItem 的 hover 態
        Assert.DoesNotContain(fx.Run().Findings, f => f.Bg == "#1182C3");
    }

    [Fact] // 觸發器整個換掉 Template → 那個狀態的視覺是另一份模板畫的，底不是基礎狀態的底。
           // ⚠ ScreenToGif 的 ExtendedMenuItem 用 Role 觸發器換三份模板，舊版把三份模板裡的
           //   觸發器全當成宿主在基礎底上的狀態 —— 72 筆假 fail
    public void ATriggerThatSwapsTheWholeTemplateIsUnresolved()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Item" TargetType="MenuItem">
                  <Setter Property="Foreground" Value="#333333"/>
                  <Setter Property="Background" Value="#F5F5F5"/>
                  <Style.Triggers>
                    <Trigger Property="Role" Value="TopLevelHeader">
                      <Setter Property="Foreground" Value="#FFFFFF"/>
                      <Setter Property="Template">
                        <Setter.Value>
                          <ControlTemplate TargetType="MenuItem">
                            <Border Background="#1182C3"><ContentPresenter/></Border>
                          </ControlTemplate>
                        </Setter.Value>
                      </Setter>
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Grid.Resources>
              <MenuItem Style="{StaticResource Item}" Header="x"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Contains(r.Findings, f => f.Fg == "#333333" && f.Bg == "#F5F5F5"); // 基礎態照樣評
        // 白字不能拿 #F5F5F5 去配 —— 那個狀態的底是另一份模板畫的
        Assert.DoesNotContain(r.Findings, f => f.Fg == "#FFFFFF" && f.Bg == "#F5F5F5");
        Assert.Contains(r.UnresolvedSites, s => s.Reason == UnresolvedReason.TemplatePainted);
    }

    [Fact] // 元素自己換掉 Template → Style 那份模板的 ControlTemplate.Triggers 在執行期不存在。
           // ⚠ 實測（Kindling，1.6 開發中）：<Button Background="Transparent"> 自帶一份只有 ContentPresenter
           //   的模板，卻被套上 DarkBtn 模板裡 IsPressed 的白字，底又因為本地 Transparent 穿透到視窗底色
           //   —— 兩筆「白字配淺色視窗底 1.2:1」的假 fail，而那個狀態根本不存在。
           //   MergeChain 對 BasedOn 早就有這條（「模板被換掉」），元素端 1.6 才補上
    public void AnElementThatReplacesTheTemplateDropsTheStylesTemplateTriggers()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style TargetType="Button">
                  <Setter Property="Foreground" Value="#111111"/>
                  <Setter Property="Background" Value="#DDDDDD"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}">
                          <ContentPresenter/>
                        </Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsPressed" Value="True">
                            <Setter Property="Foreground" Value="#FFFFFF"/>
                            <Setter Property="Background" Value="#5B3FA8"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Background="Transparent">
                <Button.Template>
                  <ControlTemplate TargetType="Button">
                    <ContentPresenter/>
                  </ControlTemplate>
                </Button.Template>
              </Button>
            </Grid>
            """);
        var r = fx.Run();
        // 本地 Background="Transparent" 穿透到祖先的白，基礎態的字色照樣評
        Assert.Contains(r.Findings, f => f.Fg == "#111111" && f.Bg == "#FFFFFF");
        // 被換掉的那份模板裡的 IsPressed 在這個元素身上不存在 —— 白字不能配到祖先的白底。
        // ⚠ 定義處（WalkStyles）那一筆「白字配 #5B3FA8」照舊要有：別的按鈕沒換模板，那一組是真的
        Assert.DoesNotContain(r.Findings, f => f.Fg == "#FFFFFF" && f.Bg == "#FFFFFF");
        Assert.Contains(r.Findings, f => f.Fg == "#FFFFFF" && f.Bg == "#5B3FA8" && f.Element.StartsWith("Style["));
    }

    [Fact] // 規則 13 沒被模板分層蓋掉：TargetName=模板根的觸發 Setter 換掉的是根那一層，
           // 而且與字同源（定義處已檢過）→ 使用處不重複報
    public void RuleThirteenStillBeatsTheRootLayer()
    {
        using var fx = new Fixture().File("Main.xaml", """
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
        var hover = Assert.Single(r.Findings, f => f.Fg == "#FFFFFF");
        Assert.Equal("#CC3A3A", hover.Bg);
        Assert.Empty(r.UnresolvedSites); // 根那一層換底看得見，不是 template-painted
    }
    // ──────────────────────────────────────────────────────────────────────────
    // 模板裡畫底的那一層是內容的「兄弟」而不是祖先
    //
    // 只往祖先找不夠。MaterialDesign 的按鈕模板是另一個形狀 ——
    //   <Grid><Border Background="{TemplateBinding Background}"/><wpf:Ripple Content="…"/></Grid>
    // 畫底的 Border 是內容的兄弟。當成「模板裡沒有一層畫底」的話，只有「宿主自己的 Background」
    // 那一步接得住（所以那 43 筆沒事），宿主沒設底時就配到祖先 —— 編出來的數字。
    // 判定全部走 Panels，跟標記樹的規則 17 同一套。
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>MaterialDesign 的按鈕形狀，但底寫死在兄弟上 —— 1.6 配祖先的白（白字 1:1 假 fail）。</summary>
    private static Fixture SiblingBackdrop(string template, string element = """<Button Style="{StaticResource Flat}" Content="go"/>""")
        => new Fixture().File("Main.xaml", $$"""
        <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
          <Grid.Resources>
            <Style x:Key="Flat" TargetType="Button">
              <Setter Property="Foreground" Value="#FFFFFF"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="Button">
                    {{template}}
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
          </Grid.Resources>
          {{element}}
        </Grid>
        """);

    [Fact] // 核心：排在內容前面、撐滿同一格的兄弟才是字真正的底
    public void ASiblingThatPaintsBeforeTheContentIsTheBackdrop()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Border Background="#222222"/>
              <ContentPresenter/>
            </Grid>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings);
        Assert.Equal("#222222", f.Bg);
        Assert.DoesNotContain(r.Findings, x => x.Bg == "#FFFFFF"); // 祖先的白＝1.6 的那個 1:1
    }

    [Fact] // 兄弟畫的是 {TemplateBinding Background} 而宿主沒設底 ＝ 那一層實質上透明 → 穿透到祖先。
           // 這是 1.5 那條規則（ScreenToGif 的 163 組靠它），兄弟這條路不能把它弄壞
    public void ATemplateBoundSiblingWithAnUnsetHostBackgroundStillPiercesToTheAncestor()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Border Background="{TemplateBinding Background}"/>
              <ContentPresenter/>
            </Grid>
            """);
        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal("#FFFFFF", f.Bg); // 祖先 —— 這一層沒畫東西，不是「看不懂」
    }

    [Fact] // 好幾個兄弟都畫底時，文件順序最後的那個在最上面
    public void TheTopmostOfSeveralEarlierSiblingsWins()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Border Background="#111111"/>
              <Border Background="#222222"/>
              <ContentPresenter/>
            </Grid>
            """);
        Assert.Equal("#222222", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 排在內容後面的兄弟畫在字「上面」，不是它的底 —— 當成底就是編造
    public void ASiblingAfterTheContentIsNotTheBackdrop()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <ContentPresenter/>
              <Border Background="#222222"/>
            </Grid>
            """);
        Assert.Equal("#FFFFFF", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 同一格才相疊（規則 17 的格子判定，這裡共用）
    public void ASiblingInAnotherGridCellIsNotTheBackdrop()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Border Grid.Column="0" Background="#222222"/>
              <ContentPresenter Grid.Column="1"/>
            </Grid>
            """);
        Assert.Equal("#FFFFFF", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 有明確尺寸的兄弟撐不滿那一格（強調色條 Width="4"）—— 字不一定落在它上面
    public void ASizedSiblingIsNotABackdrop()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Border Width="4" Background="#222222"/>
              <ContentPresenter/>
            </Grid>
            """);
        Assert.Equal("#FFFFFF", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // StackPanel 把子元素排開，不疊 —— 旁邊的色塊不是字的底
    public void SiblingsInASideBySidePanelAreNotBackdrops()
    {
        using var fx = SiblingBackdrop("""
            <StackPanel>
              <Border Background="#222222"/>
              <ContentPresenter/>
            </StackPanel>
            """);
        Assert.Equal("#FFFFFF", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 兄弟排在容器自己的 Background 之前：容器的底畫在它所有子元素的下面
    public void TheSiblingBeatsTheContainersOwnBackground()
    {
        using var fx = SiblingBackdrop("""
            <Grid Background="#444444">
              <Border Background="#222222"/>
              <ContentPresenter/>
            </Grid>
            """);
        Assert.Equal("#222222", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 外層容器的底照舊找得到（兄弟那一格沒有候選時不能擋住往外找）
    public void TheOuterContainersBackgroundIsStillFoundThroughTheSiblingStep()
    {
        using var fx = SiblingBackdrop("""
            <Grid Background="#444444">
              <Grid>
                <ContentPresenter/>
              </Grid>
            </Grid>
            """);
        Assert.Equal("#444444", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 底是一張圖 → over-sibling-content（1.7）。往外找等於拿圖後面那一層的顏色報一個比值，
           // 而字其實疊在圖上 —— 跟標記樹的規則 17(b) 同一個答案
    public void AnImageSiblingIsOverSiblingContent()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Image Source="bg.png"/>
              <ContentPresenter/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal("<Image>", Assert.Single(r.UnresolvedSites, u => u.Reason == UnresolvedReason.OverSiblingContent).Value);
    }

    [Fact] // 排在圖後面、又畫了底的兄弟蓋過那張圖 —— 不是「看到圖就整層放棄」
    public void AnOpaqueSiblingAfterTheImageWinsAgain()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Image Source="bg.png"/>
              <Border Background="#222222"/>
              <ContentPresenter/>
            </Grid>
            """);
        Assert.Equal("#222222", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 半透明的兄弟要疊到它底下那一層才算得出來，而這一步回傳的是原始寫法 —— 猜一個不透明的值
           // 會往寬鬆那邊錯，所以不收
    public void ASiblingWithOpacityIsNotTakenAsAnOpaqueBackdrop()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Border Background="#222222" Opacity="0.5"/>
              <ContentPresenter/>
            </Grid>
            """);
        Assert.Equal("#FFFFFF", Assert.Single(fx.Run().Findings).Bg);
    }

    [Fact] // 接走宿主內容的自訂宿主（MaterialDesign 的 wpf:Ripple）也吃這條
    public void ACustomContentHolderGetsTheSiblingBackdropToo()
    {
        using var fx = SiblingBackdrop("""
            <Grid>
              <Border Background="#222222"/>
              <Ripple Content="{TemplateBinding Content}"/>
            </Grid>
            """);
        Assert.Equal("#222222", Assert.Single(fx.Run().Findings).Bg);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 觸發器重畫模板內部那一層
    //
    // 1.5 之前把那個 Setter 丟掉、照舊拿基礎狀態的底評分 —— 編出來的數字
    // (ScreenToGif 的 AwareTabItem 把 ContentBorder 寫死 #90FFFFFF,報出 1.46:1)。
    // 現在記下重畫了哪一個元素、換成什麼,再看它是不是正好畫在這個內容位置底下。
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>模板:根 Border 畫底(可命名)、裡面一層 ContentBorder 自己不畫底。</summary>
    private static Fixture Repaint(string rootBg, string setter, string fg = "#000000") => new Fixture().File("Main.xaml", $$"""
        <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
          <Grid.Resources>
            <Style x:Key="Tab" TargetType="TabItem">
              <Setter Property="Foreground" Value="{{fg}}"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="TabItem">
                    <Border x:Name="Root" Background="{{rootBg}}">
                      <Border x:Name="ContentBorder">
                        <ContentPresenter/>
                      </Border>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsSelected" Value="True">
                        {{setter}}
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
          </Grid.Resources>
          <TabItem Style="{StaticResource Tab}" Header="one"/>
        </Grid>
        """);

    [Fact] // 重畫的就是畫底的那一層本身：不透明 → 那就是這個狀態的底
    public void ATriggerThatRepaintsThePaintingLayerIsResolved()
    {
        using var fx = Repaint("#000000", """<Setter TargetName="Root" Property="Background" Value="#CCCCCC"/>""");
        var r = fx.Run();
        Assert.DoesNotContain(r.UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
        Assert.Contains(r.Findings, f => f.Bg == "#000000");  // 基礎態：黑字黑底
        Assert.Contains(r.Findings, f => f.Bg == "#CCCCCC");  // 選取態：重畫之後
    }

    [Fact] // ScreenToGif 的 AwareTabItem 形狀:重畫的那一層在畫底那一層「上面」,而且是半透明 ——
           // 要疊在它底下那一層(#000000)上。疊到祖先(白)上的話會報 21:1,是執行期不存在的比值
    public void ATranslucentRepaintCompositesOverTheLayerItCovers()
    {
        using var fx = Repaint("#000000", """<Setter TargetName="ContentBorder" Property="Background" Value="#90FFFFFF"/>""");
        var r = fx.Run();
        Assert.DoesNotContain(r.UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
        // 0x90 = 144/255 的白疊在純黑上 = #909090（剛好整數）
        var expected = Wcag.Contrast("#000000", "#909090");
        Assert.Contains(r.Findings, f => f.RatioDark == expected);
        Assert.DoesNotContain(r.Findings, f => f.RatioDark == Wcag.Contrast("#000000", "#FFFFFF"));
    }

    [Fact] // 重畫的元素不在這個位置的鏈上(別的分支、或撐不滿那一格)→ 維持 1.6 的誠實不知道
    public void ARepaintOfAnElementOffThisPositionsChainStaysUnresolved()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Tab" TargetType="TabItem">
                  <Setter Property="Foreground" Value="#000000"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="TabItem">
                        <Border x:Name="Root" Background="#000000">
                          <Grid>
                            <Border x:Name="Badge" Grid.Column="1" Width="8"/>
                            <ContentPresenter Grid.Column="0"/>
                          </Grid>
                        </Border>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Badge" Property="Background" Value="#CCCCCC"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <TabItem Style="{StaticResource Tab}" Header="one"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Contains(r.UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
        Assert.DoesNotContain(r.Findings, f => f.Bg == "#CCCCCC"); // 那一塊不在字底下
    }

    [Fact] // OpacityMask 不是一個底色 —— 收了就得猜它把下面那層挖成什麼
    public void AnOpacityMaskRepaintStaysUnresolved()
    {
        using var fx = Repaint("#000000", """<Setter TargetName="ContentBorder" Property="OpacityMask" Value="#80000000"/>""");
        Assert.Contains(fx.Run().UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
    }

    [Fact] // 半透明的重畫層，底下那一層讀不懂 → 那個顏色是答案的一部分，猜不得
    public void ATranslucentRepaintOverAnUnreadableLayerStaysUnresolved()
    {
        using var fx = Repaint("{Binding Tint}", """<Setter TargetName="ContentBorder" Property="Background" Value="#90FFFFFF"/>""");
        Assert.Contains(fx.Run().UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
    }

    [Fact] // 不透明的重畫層把底下那一層完全蓋住 —— 底下讀不懂也不影響
    public void AnOpaqueRepaintDoesNotNeedTheLayerBelow()
    {
        using var fx = Repaint("{Binding Tint}", """<Setter TargetName="ContentBorder" Property="Background" Value="#CCCCCC"/>""");
        var r = fx.Run();
        Assert.Contains(r.Findings, f => f.Bg == "#CCCCCC");
    }

    [Fact] // 沒有任何觸發器碰模板內部時，分層一個字都不該變（1.6 的鍵與數字不動）
    public void WithoutAnyInnerRepaintTheLayersAreUnchanged()
    {
        using var fx = Repaint("#000000", """<Setter Property="FontWeight" Value="Bold"/>""");
        var r = fx.Run();
        Assert.DoesNotContain(r.UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
        Assert.All(r.Findings, f => Assert.Equal("#000000", f.Bg));
    }

    /// <summary>宿主自己有底、模板裡的重畫打在別的地方（1.7）：<c>Elsewhere</c> 在另一格、
    /// 不在 ContentPresenter 底下那一疊上，所以那次重畫跟這段文字無關。</summary>
    private static Fixture HostBgRepaint(string targetName) => new Fixture().File("Main.xaml", $$"""
        <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
          <Grid.Resources>
            <Style x:Key="Btn" TargetType="Button">
              <Setter Property="Foreground" Value="#777777"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="Button">
                    <Grid>
                      <Border x:Name="Under" Grid.Column="0" Background="{TemplateBinding Background}">
                        <ContentPresenter/>
                      </Border>
                      <Border x:Name="Elsewhere" Grid.Column="1" Width="8" Background="#111111"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="{{targetName}}" Property="Background" Value="#222222"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
          </Grid.Resources>
          <Button Style="{StaticResource Btn}" Background="#FFFFFF" Content="hi"/>
        </Grid>
        """);

    [Fact] // 重畫打在別的分支上 → 宿主自己的底照舊算得出來，不要連同整個狀態一起丟掉
    public void ARepaintOffEveryContentPositionKeepsTheHostsOwnBackground()
    {
        using var fx = HostBgRepaint("Elsewhere");
        var r = fx.Run();
        Assert.DoesNotContain(r.UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
        Assert.Contains(r.Findings, f => f.Bg == "#FFFFFF" && f.Fg == "#777777");
    }

    [Fact] // 重畫打在內容底下那一層 → 宿主的底被蓋掉了，拿它評分就是編造
    public void ARepaintUnderTheContentStillDiscardsTheState()
    {
        using var fx = HostBgRepaint("Under");
        var r = fx.Run();
        Assert.Contains(r.UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
    }

    [Fact] // 同一格、但撐不滿那一格的兄弟（強調色條）：它在不在字底下靜態分不出來 ——
           // 不能因為「它不是背板」就當成跟這段文字無關
    public void ARepaintOfAPartialCellSiblingInTheSameCellStillDiscardsTheState()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Foreground" Value="#777777"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Grid>
                          <Border x:Name="Bar" Width="4" HorizontalAlignment="Left" Background="#111111"/>
                          <ContentPresenter/>
                        </Grid>
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bar" Property="Background" Value="#222222"/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <Button Style="{StaticResource Btn}" Background="#FFFFFF" Content="hi"/>
            </Grid>
            """);
        Assert.Contains(fx.Run().UnresolvedSites, u => u.Reason == UnresolvedReason.TemplatePainted);
    }
}

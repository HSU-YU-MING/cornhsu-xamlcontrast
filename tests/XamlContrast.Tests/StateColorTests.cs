using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// VisualStateManager 的狀態顏色（1.5）：**明文盲區的計數器**。
///
/// 這一類以前連計數器都沒有 —— 第四輪外部實查測得數十處／專案等級，而報告裡一個字都沒有。
/// 報告看起來很完整，實際上整組狀態顏色從頭到尾沒被提過，正是本工具最怕的那種「看起來很健康」。
///
/// 這些測試釘兩件事：
///   1. 兩種寫法都要數到（WPF 的狀態 storyboard、WinUI 的 VisualState.Setters）—— 只數一種的話，
///      另一種生態的專案會拿到 0，而 0 在這裡的意思是「你沒有這種盲區」，那是假的安心。
///   2. **不動任何既有數字**（pairs／unresolved／coverage／skipped）—— 覆蓋率是 exit 3 的門，
///      把盲區塞進分母會讓每個用 VSM 的專案無聲下降、有人直接變紅，而換到的資訊是零。
/// </summary>
public class StateColorTests
{
    private static Fixture WithStates(string states, string? extra = null) => new Fixture().WithPalette()
        .File("Main.xaml", $$"""
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
              <Border x:Name="Host" Background="{DynamicResource Bg}">
                <VisualStateManager.VisualStateGroups>
                  <VisualStateGroup x:Name="CommonStates">
            {{states}}
                  </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
                <TextBlock Foreground="{DynamicResource Fg}" Text="base state"/>
              </Border>
              {{extra}}
            </Window>
            """);

    private const string Animation = """
                    <VisualState x:Name="MouseOver">
                      <Storyboard>
                        <ColorAnimation Storyboard.TargetName="Host"
                                        Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="#303030"/>
                      </Storyboard>
                    </VisualState>
        """;

    private const string Setter = """
                    <VisualState x:Name="Pressed">
                      <VisualState.Setters>
                        <Setter Target="Host.Background" Value="#101010"/>
                      </VisualState.Setters>
                    </VisualState>
        """;

    [Fact]
    public void TheStateStoryboardColourIsCountedAndNamed()
    {
        using var fx = WithStates(Animation);
        var r = fx.Run();

        var site = Assert.Single(r.StateColors);
        Assert.Equal("Main.xaml", site.File);
        Assert.Equal("MouseOver", site.State);
        Assert.Equal("animation", site.Kind);
        Assert.Equal("(Border.Background).(SolidColorBrush.Color)", site.Property);
        Assert.Equal("Host", site.Target);
        Assert.True(site.Line > 0, "要點得出行號 —— 不然使用者沒有下一步");
    }

    [Fact] // WinUI／UWP 的寫法。只數 animation 的話，WinUI 專案的狀態顏色全在 Setters 裡 → 報 0 → 假的安心
    public void TheWinUiStateSetterIsCountedToo()
    {
        using var fx = WithStates(Setter);
        var r = fx.Run();

        var site = Assert.Single(r.StateColors);
        Assert.Equal("Pressed", site.State);
        Assert.Equal("setter", site.Kind);
        Assert.Equal("Host.Background", site.Property);
        Assert.Equal("Host", site.Target);
    }

    [Fact] // 這個計數器的設計前提：它只是多一份盲區地圖，不碰任何既有的數字
    public void TheBlindSpotMovesNoExistingNumber()
    {
        using var with = WithStates(Animation + "\n" + Setter);
        using var without = new Fixture().WithPalette()
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <Border x:Name="Host" Background="{DynamicResource Bg}">
                    <TextBlock Foreground="{DynamicResource Fg}" Text="base state"/>
                  </Border>
                </Window>
                """);
        var a = with.Run();
        var b = without.Run();

        Assert.Equal(2, a.StateColors.Count);
        Assert.Empty(b.StateColors);
        // 基礎態照舊評得到，而且兩邊一模一樣
        Assert.Equal(b.Pairs, a.Pairs);
        Assert.Equal(b.Unresolved, a.Unresolved);
        Assert.Equal(b.Skipped, a.Skipped);
        Assert.Equal(b.CoveragePercent, a.CoveragePercent);
        Assert.Equal(b.CountOf(Category.Ok), a.CountOf(Category.Ok));
        Assert.Equal(b.CountOf(Category.Fail), a.CountOf(Category.Fail));
    }

    [Fact] // 一個動畫算一處，keyframe 不另計 —— 計數要跟「要去看幾個地方」同單位
    public void KeyFramesCountAsOneAnimation()
    {
        using var fx = WithStates("""
                    <VisualState x:Name="Focused">
                      <Storyboard>
                        <ColorAnimationUsingKeyFrames Storyboard.TargetName="Host" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)">
                          <EasingColorKeyFrame KeyTime="0" Value="#101010"/>
                          <EasingColorKeyFrame KeyTime="0:0:0.2" Value="#404040"/>
                        </ColorAnimationUsingKeyFrames>
                      </Storyboard>
                    </VisualState>
            """);

        Assert.Single(fx.Run().StateColors);
    }

    [Fact] // brush 整個換掉也是改顏色（WPF 模板最常見的寫法）
    public void ABrushSwapByObjectKeyFramesIsAStateColour()
    {
        using var fx = WithStates("""
                    <VisualState x:Name="Selected">
                      <Storyboard>
                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="Host" Storyboard.TargetProperty="Background">
                          <DiscreteObjectKeyFrame KeyTime="0" Value="{DynamicResource Dim}"/>
                        </ObjectAnimationUsingKeyFrames>
                      </Storyboard>
                    </VisualState>
            """);

        var site = Assert.Single(fx.Run().StateColors);
        Assert.Equal("Background", site.Property);
    }

    [Fact] // 動的不是顏色就不數：Opacity 動畫在 VSM 裡幾乎都是焦點框／陰影的淡入淡出，
           // 全收會讓這個計數器變成雜訊（函式庫一個檔就幾十處），而雜訊計數器沒人看，等於沒有
    public void AnimationsThatDoNotPaintColoursAreNotCounted()
    {
        using var fx = WithStates("""
                    <VisualState x:Name="PointerOver">
                      <Storyboard>
                        <DoubleAnimation Storyboard.TargetName="Host" Storyboard.TargetProperty="Opacity" To="0.5"/>
                        <ObjectAnimationUsingKeyFrames Storyboard.TargetName="Host" Storyboard.TargetProperty="Visibility">
                          <DiscreteObjectKeyFrame KeyTime="0" Value="Collapsed"/>
                        </ObjectAnimationUsingKeyFrames>
                      </Storyboard>
                    </VisualState>
            """);

        Assert.Empty(fx.Run().StateColors);
    }

    [Fact] // 這不是判定，是工具的邊界 —— 壓抑註解壓得掉 finding，壓不掉盲區（同 unresolved 的規矩）
    public void AnIgnoreCommentDoesNotHideTheBlindSpot()
    {
        using var fx = new Fixture().WithPalette()
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <!-- xamlcontrast-ignore: decorative chrome -->
                  <Border x:Name="Host" Background="{DynamicResource Bg}">
                    <VisualStateManager.VisualStateGroups>
                      <VisualStateGroup x:Name="CommonStates">
                        <VisualState x:Name="MouseOver">
                          <Storyboard>
                            <ColorAnimation Storyboard.TargetName="Host"
                                            Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="#303030"/>
                          </Storyboard>
                        </VisualState>
                      </VisualStateGroup>
                    </VisualStateManager.VisualStateGroups>
                    <TextBlock Foreground="{DynamicResource Dim}" Text="suppressed"/>
                  </Border>
                </Window>
                """);
        var r = fx.Run();

        Assert.True(r.Suppressed > 0, "註解要有壓到東西，不然這條測試證明不了什麼");
        Assert.Single(r.StateColors);
    }

    [Fact] // Style／模板裡的 VSM 也要數到：元素上、Style 裡、ControlTemplate 裡都掛得住
    public void StatesInsideATemplateAreCounted()
    {
        using var fx = new Fixture().WithPalette()
            .File("Main.xaml", """
                <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
                  <Window.Resources>
                    <Style x:Key="Btn" TargetType="Button">
                      <Setter Property="Template">
                        <Setter.Value>
                          <ControlTemplate TargetType="Button">
                            <Border x:Name="Root" Background="{DynamicResource Bg}">
                              <VisualStateManager.VisualStateGroups>
                                <VisualStateGroup x:Name="CommonStates">
                                  <VisualState x:Name="Disabled">
                                    <Storyboard>
                                      <ColorAnimation Storyboard.TargetName="Root"
                                                      Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="#555555"/>
                                    </Storyboard>
                                  </VisualState>
                                </VisualStateGroup>
                              </VisualStateManager.VisualStateGroups>
                              <ContentPresenter/>
                            </Border>
                          </ControlTemplate>
                        </Setter.Value>
                      </Setter>
                    </Style>
                  </Window.Resources>
                  <Button Style="{StaticResource Btn}" Content="ok"/>
                </Window>
                """);

        var site = Assert.Single(fx.Run().StateColors);
        Assert.Equal("Disabled", site.State);
    }

    [Fact] // 名字沒寫也要記 —— 「不知道哪個狀態」好過整處不見
    public void AnUnnamedStateIsStillCounted()
    {
        using var fx = WithStates("""
                    <VisualState>
                      <Storyboard>
                        <ColorAnimation Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="#303030"/>
                      </Storyboard>
                    </VisualState>
            """);

        var site = Assert.Single(fx.Run().StateColors);
        Assert.Equal("(unnamed)", site.State);
        Assert.Null(site.Target);
    }
}

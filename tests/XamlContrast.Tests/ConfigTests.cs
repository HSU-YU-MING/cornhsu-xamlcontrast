using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>xamlcontrast.config.json —— config 是逃生口不是必經之路，
/// 但寫了就要生效，寫錯就要有好訊息（靜默忽略是謊報健康的親戚）。</summary>
public class ConfigTests
{
    private const string SmallDict = """
        <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="Bg" Color="#111111"/>
          <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
          <SolidColorBrush x:Key="Accent" Color="#42A5F5"/>
        </ResourceDictionary>
        """;

    [Fact]
    public void NoConfigFileMeansPureDefaults()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", "<Grid/>");
        var cfg = ToolConfig.Load(fx.Root);
        Assert.Null(cfg.SourcePath);
        Assert.Equal("auto", cfg.Palette.Mode);
        Assert.True(cfg.Thresholds.IsDefault);
    }

    [Fact] // 強制配對模式：檔名不含 dark/light 提示、自動偵測抓不到 → config 指定就吃得到
    public void ForcedPairModeUsesSpecifiedFiles()
    {
        using var fx = new Fixture();
        fx.File("Colors/A.xaml", SmallDict);   // 沒有 dark/light 命名提示
        fx.File("Colors/B.xaml", SmallDict.Replace("#111111", "#FAFAFA").Replace("#EEEEEE", "#1B1B1B"));
        fx.File(ToolConfig.FileName, """
            { "palette": { "mode": "pair", "darkFile": "Colors/A.xaml", "lightFile": "Colors/B.xaml" } }
            """);
        var cfg = ToolConfig.Load(fx.Root);
        var det = PaletteDetector.Detect(fx.Root, cfg);
        Assert.Equal(PaletteMode.Pair, det.Mode);
        Assert.Equal(("#111111", "#FAFAFA"), det.Palette.Entries["Bg"]);
        Assert.Contains("config-forced", det.Description);
    }

    [Fact] // 覆蓋率 = 解析成功 / 看到的總數 —— 「這份結果代不代表專案」的關鍵數字
    public void CoverageReflectsResolvedShareOfPairsSeen()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="解析得出"/>
              <Grid Background="{Binding X}">
                <TextBlock Foreground="#FFFFFF" Text="解析不出 1"/>
              </Grid>
              <Grid Background="{Binding Y}">
                <TextBlock Foreground="#FFFFFF" Text="解析不出 2"/>
              </Grid>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Pairs);
        Assert.Equal(2, r.Unresolved);
        Assert.Equal(1.0 / 3, r.Coverage, 3);
        Assert.Contains("coverage 33.3%", Report.ToConsole(r));
        Assert.Contains("\"coverage\": 33.3", Report.ToJson(r));
    }

    [Fact] // rootBackground:主題函式庫使用者的逃生口 —— 視窗底色鍵在 repo、
           // 連到視窗的隱含樣式在 NuGet 套件裡(NETworkManager 726 筆 no-ancestor 的形狀)
    public void ConfigRootBackgroundProvidesTheFloor()
    {
        using var fx = new Fixture();
        fx.File("Themes/Light.Accent1.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Lib.Window.Background" Color="#FFFFFFFF"/>
              <SolidColorBrush x:Key="Lib.Gray3" Color="#FF9D9D9D"/>
              <SolidColorBrush x:Key="Lib.Accent" Color="#FF2196F3"/>
            </ResourceDictionary>
            """);
        fx.File("Views/SomeView.xaml", """
            <UserControl>
              <TextBlock Foreground="{DynamicResource Lib.Gray3}" Text="次要文字"/>
            </UserControl>
            """);
        fx.File("xamlcontrast.config.json", """{ "rootBackground": "Lib.Window.Background" }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        var f = Assert.Single(r.Findings);
        Assert.Equal("{Lib.Window.Background}", f.Bg);   // 地板來自 config 宣告
        Assert.Equal(Category.Fail, f.Category);         // 灰疊白 2.7:1,真實問題浮出
        Assert.Equal(0, r.UnresolvedBy.GetValueOrDefault(UnresolvedReason.NoAncestorBackground));
    }

    [Fact] // minCoverage 要驗證範圍 —— config 寫錯要有欄位級訊息，不能靜默忽略
    public void MinCoverageOutOfRangeIsRejected()
    {
        using var fx = new Fixture();
        fx.File("xamlcontrast.config.json", """{ "minCoverage": 150 }""");
        var ex = Assert.Throws<ConfigException>(() => ToolConfig.Load(fx.Root));
        Assert.Contains("minCoverage", ex.Message);
    }

    [Fact] // 明選 none 是使用者的決定，不算退化（不觸發 --strict-palette）
    public void ForcedNoneIsNotDegraded()
    {
        using var fx = new Fixture();
        fx.File("Themes/DarkTheme.xaml", SmallDict); // 有東西可偵測，但使用者說不要
        fx.File(ToolConfig.FileName, """{ "palette": { "mode": "none" } }""");
        var det = PaletteDetector.Detect(fx.Root, ToolConfig.Load(fx.Root));
        Assert.Equal(PaletteMode.None, det.Mode);
        Assert.False(det.IsDegraded);
    }

    [Fact] // 門檻覆寫要真的改變分級，且報告要標明非預設
    public void ThresholdOverrideChangesGradingAndIsAnnounced()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#767676" Text="4.54:1 — AA 剛好過"/>
            </Grid>
            """);
        fx.File(ToolConfig.FileName, """{ "thresholds": { "normalText": 7.0 } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        var f = Assert.Single(r.Findings);
        Assert.Equal(7.0, f.Need);                       // AAA 門檻生效
        Assert.NotEqual(Category.Ok, f.Category);        // 4.54 過 AA 但不過 7.0
        Assert.Contains("non-default thresholds", Report.ToConsole(r));
        Assert.Contains("\"normalText\": 7", Report.ToJson(r));
    }

    [Fact] // classification 覆寫：自訂控制項加進排除清單 → 裝飾
    public void ExtraNonTextMakesElementDecorative()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#202020">
              <WaveformDisplay Foreground="#303030"/>
            </Grid>
            """);
        fx.File(ToolConfig.FileName, """{ "classification": { "extraNonText": ["WaveformDisplay"] } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(Category.Decorative, Assert.Single(r.Findings).Category);
    }

    [Fact] // requireReason=false：沒理由的 ignore 也壓（但仍計數）
    public void RequireReasonFalseAllowsBareIgnore()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore -->
              <TextBlock Foreground="#EEEEEE" Text="x"/>
            </Grid>
            """);
        fx.File(ToolConfig.FileName, """{ "ignore": { "requireReason": false } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(1, r.Suppressed);
        Assert.Empty(r.InvalidIgnores);
    }

    [Theory] // 寫錯要有好訊息 —— 靜默忽略等於使用者以為改了標準而其實沒有
    [InlineData("""{ "palette": { "mode": "figma" } }""", "palette.mode")]
    [InlineData("""{ "failOn": "never" }""", "failOn")]
    [InlineData("""{ "palette": { "mode": "pair", "darkFile": "a.xaml" } }""", "lightFile")]
    [InlineData("""{ not json """, "not valid JSON")]
    public void InvalidConfigThrowsWithHelpfulMessage(string json, string expectInMessage)
    {
        using var fx = new Fixture();
        fx.File(ToolConfig.FileName, json);
        var ex = Assert.Throws<ConfigException>(() => ToolConfig.Load(fx.Root));
        Assert.Contains(expectInMessage, ex.Message);
    }

    [Fact] // 強制模式指到不存在的檔 → 錯誤而不是退化
    public void ForcedModeMissingFileThrows()
    {
        using var fx = new Fixture();
        fx.File(ToolConfig.FileName, """
            { "palette": { "mode": "single", "darkFile": "Nope/Missing.xaml" } }
            """);
        var ex = Assert.Throws<ConfigException>(() => PaletteDetector.Detect(fx.Root, ToolConfig.Load(fx.Root)));
        Assert.Contains("Missing.xaml", ex.Message);
    }

    // ── 覆蓋率求救指引 ────────────────────────────────────────────────────
    // 主題函式庫使用者撞上覆蓋率下限時，逃生口要出現在他眼前。這幾條測的是
    // 「該印時印、不該印時不印」—— 錯誤方向的提示（叫人去設一個救不了他的東西）
    // 比沒有提示更糟，會讓人以為試過了、沒用。

    /// <summary>沒有祖先背景佔多數 → 印，且要點出那一行 config</summary>
    [Fact]
    public void CoverageHintOfferedWhenRootBackgroundWouldHelp()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <TextBlock Foreground="#EEEEEE" Text="沒有任何祖先宣告背景 1"/>
              <TextBlock Foreground="#DDDDDD" Text="沒有任何祖先宣告背景 2"/>
            </Window>
            """);
        var hint = Report.CoverageHint(fx.Run());
        Assert.NotNull(hint);
        Assert.Contains("rootBackground", hint);
        Assert.Contains("no-ancestor-background", hint);
    }

    /// <summary>硬邊界（Binding／漸層）佔多數 → 不印。rootBackground 救不了執行期的值，
    /// 叫人去設等於浪費他一次嘗試，還會讓真正的結論（這是靜態分析的極限）被蓋掉。</summary>
    [Fact]
    public void CoverageHintSuppressedWhenBoundColoursDominate()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#000000" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid Background="{Binding A}"><TextBlock Foreground="#FFFFFF" Text="1"/></Grid>
              <Grid Background="{Binding B}"><TextBlock Foreground="#FFFFFF" Text="2"/></Grid>
              <Grid Background="{Binding C}"><TextBlock Foreground="#FFFFFF" Text="3"/></Grid>
            </Grid>
            """);
        Assert.Null(Report.CoverageHint(fx.Run()));
    }

    /// <summary>已經設了 rootBackground 還是不夠 → 不印。重複叫他設一次已經設過的東西，
    /// 會讓人以為設定沒生效。</summary>
    [Fact]
    public void CoverageHintNotRepeatedOnceRootBackgroundIsDeclared()
    {
        using var fx = new Fixture();
        fx.File("Themes/DarkTheme.xaml", SmallDict);
        fx.File("Themes/LightTheme.xaml", SmallDict.Replace("#111111", "#FAFAFA").Replace("#EEEEEE", "#1B1B1B"));
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <TextBlock Foreground="{DynamicResource Nope}" Text="未知鍵"/>
            </Window>
            """);
        fx.File(ToolConfig.FileName, """{ "rootBackground": "Bg" }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Null(Report.CoverageHint(r));
    }
}

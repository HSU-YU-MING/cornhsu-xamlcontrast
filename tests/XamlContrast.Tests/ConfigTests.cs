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
}

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
    [InlineData("""{ "palette": { "mode": "pair", "lightFile": "b.xaml" } }""", "requires darkFile and lightFile")]
    [InlineData("""{ "palette": { "mode": "single" } }""", "palette.mode 'single' requires darkFile")]
    [InlineData("""{ "palette": { "mode": "csharp" } }""", "palette.mode 'csharp' requires csharpFile")]
    [InlineData("""{ "palette": { "mode": "Pair" } }""", "palette.mode")]             // 列舉值分大小寫
    [InlineData("""{ "failOn": "WARN" }""", "failOn must be fail|warn, got 'WARN'")]
    [InlineData("""{ "failOn": null }""", "failOn must be fail|warn, got ''")]
    [InlineData("""{ "thresholds": { "normalText": 1 } }""", "thresholds out of range")]
    [InlineData("""{ "thresholds": { "largeText": 0.5 } }""", "thresholds out of range")]
    [InlineData("""{ "thresholds": { "failFactor": 0 } }""", "thresholds out of range")]
    [InlineData("""{ "thresholds": { "failFactor": 1.01 } }""", "thresholds out of range")]
    [InlineData("""{ "minCoverage": -0.1 }""", "minCoverage must be between 0 and 100")]
    [InlineData("""{ "minCoverage": "50" }""", "not valid JSON")]                       // 型別錯
    [InlineData("""{ "classification": { "extraText": "TextBlock" } }""", "not valid JSON")]
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

    // ── 驗證的邊界與解析的寬鬆度 ──────────────────────────────────────────

    [Theory] // 範圍的邊界值本身是合法的（failFactor 可以是 1、minCoverage 可以是 0 或 100）
    [InlineData("""{ "thresholds": { "failFactor": 1 } }""")]
    [InlineData("""{ "thresholds": { "normalText": 1.01, "largeText": 1.01, "failFactor": 0.01 } }""")]
    [InlineData("""{ "minCoverage": 0 }""")]
    [InlineData("""{ "minCoverage": 100 }""")]
    public void BoundaryValuesAreAccepted(string json)
    {
        using var fx = new Fixture();
        fx.File(ToolConfig.FileName, json);
        Assert.NotNull(ToolConfig.Load(fx.Root).SourcePath);
    }

    [Fact] // 現況是寬鬆的：屬性名不分大小寫、允許註解與尾逗號、未知屬性（含拼錯）靜默忽略。
           // 公開介面盤點 8.3：收緊任何一項都會讓原本能跑的設定檔 exit 2 —— 是破壞性變更，
           // 改的時候這條會紅，提醒你那要走 0.x minor + CHANGELOG
    public void ParsingIsLenientAboutNamesCommentsAndUnknownProperties()
    {
        using var fx = new Fixture();
        fx.File(ToolConfig.FileName, """
            {
              // 行註解
              "FAILON": "warn",
              "minCoverge": 99,   /* 拼錯 → 忽略，不報錯 */
              "Thresholds": { "NormalText": 7, },
            }
            """);
        var cfg = ToolConfig.Load(fx.Root);
        Assert.Equal("warn", cfg.FailOn);
        Assert.Equal(7.0, cfg.Thresholds.NormalText);
        Assert.Equal(50, cfg.MinCoverage); // 拼錯的那格沒有生效
    }

    // ── 強制色盤模式：csharp／single ──────────────────────────────────────

    private const string CsPalette = """
        // 自訂格式：key: dark / light
        Bg: #111111 / #FAFAFA
        Fg: #EEEEEE / #1B1B1B
        Accent: #42A5F5 / #1D6FC4
        Typo: #FF0000A / #FFFFFF
        """;

    [Fact] // csharpPattern 的具名群組 key/dark/light；長度不是 7／9 的值不收（同一道 IsValidHex）；
           // 與 C# 色盤鍵集高度重疊的 XAML 靜態複本排除出 UI 掃描
    public void ForcedCSharpModeUsesCustomPatternWithNamedGroups()
    {
        using var fx = new Fixture();
        fx.File("Services/Theme.cs", CsPalette);
        fx.File("Themes/Static.xaml", SmallDict);
        fx.File(ToolConfig.FileName, """
            { "palette": { "mode": "csharp", "csharpFile": "Services/Theme.cs",
                           "csharpPattern": "^\\s*(?<key>\\w+): (?<dark>#\\w+) / (?<light>#\\w+)" } }
            """);
        var cfg = ToolConfig.Load(fx.Root);
        var det = PaletteDetector.Detect(fx.Root, cfg);
        Assert.Equal(PaletteMode.CSharp, det.Mode);
        Assert.Equal(("#111111", "#FAFAFA"), det.Palette.Entries["Bg"]);
        Assert.Equal(3, det.Palette.Count);
        Assert.False(det.Palette.Entries.ContainsKey("Typo")); // 七位數的打字錯誤不收
        Assert.Contains("config-forced C# source: Services/Theme.cs (3 keys)", det.Description);
        Assert.Contains(det.ExcludedFiles, f => f.EndsWith("Static.xaml"));
        var r = Auditor.Run(fx.Root, det, cfg);
        Assert.Contains("\"paletteMode\": \"csharp\"", Report.ToJson(r));
    }

    [Fact] // 群組名退回 k/d/l（內建樣式的寫法）也要認得
    public void ForcedCSharpPatternFallsBackToShortGroupNames()
    {
        using var fx = new Fixture();
        fx.File("Services/Theme.cs", CsPalette);
        fx.File(ToolConfig.FileName, """
            { "palette": { "mode": "csharp", "csharpFile": "Services/Theme.cs",
                           "csharpPattern": "^\\s*(?<k>\\w+): (?<d>#\\w+) / (?<l>#\\w+)" } }
            """);
        var det = PaletteDetector.Detect(fx.Root, ToolConfig.Load(fx.Root));
        Assert.Equal(("#EEEEEE", "#1B1B1B"), det.Palette.Entries["Fg"]);
    }

    [Fact] // 沒給 csharpPattern → 內建三元組 ("Key", "#深", "#淺")
    public void ForcedCSharpModeWithoutPatternUsesTheBuiltInTuple()
    {
        using var fx = new Fixture();
        fx.File("Palette.cs", """
            ("Bg", "#111111", "#EAEAEA"),
            ("Fg", "#EEEEEE", "#1B1B1B"),
            """);
        fx.File(ToolConfig.FileName, """{ "palette": { "mode": "csharp", "csharpFile": "Palette.cs" } }""");
        var det = PaletteDetector.Detect(fx.Root, ToolConfig.Load(fx.Root));
        Assert.Equal(PaletteMode.CSharp, det.Mode);
        Assert.Equal(("#111111", "#EAEAEA"), det.Palette.Entries["Bg"]); // 強制模式不受「≥3 鍵」的偵測門檻限制
    }

    [Fact] // csharpPattern 不是合法 regex → 設定錯誤（CLI 轉 exit 2），不是例外逃出契約
    public void InvalidCsharpPatternIsAConfigError()
    {
        using var fx = new Fixture();
        fx.File("Palette.cs", "");
        fx.File(ToolConfig.FileName, """
            { "palette": { "mode": "csharp", "csharpFile": "Palette.cs", "csharpPattern": "([" } }
            """);
        var ex = Assert.Throws<ConfigException>(() => PaletteDetector.Detect(fx.Root, ToolConfig.Load(fx.Root)));
        Assert.Contains("palette.csharpPattern is not a valid regex", ex.Message);
    }

    [Fact] // 強制單一主題：深淺同值、色盤檔排除出 UI 掃描；畸形色碼一樣不收
    public void ForcedSingleModeUsesOneFileForBothThemes()
    {
        using var fx = new Fixture();
        fx.File("Colors/Brand.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Typo" Color="#EEEEEEE"/>
            </ResourceDictionary>
            """);
        fx.File(ToolConfig.FileName, """{ "palette": { "mode": "single", "darkFile": "Colors/Brand.xaml" } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var det = PaletteDetector.Detect(fx.Root, cfg);
        Assert.Equal(PaletteMode.Single, det.Mode);
        Assert.Equal(("#111111", "#111111"), det.Palette.Entries["Bg"]);
        Assert.False(det.Palette.Entries.ContainsKey("Typo"));
        Assert.Contains("config-forced single-theme palette: Colors/Brand.xaml (2 keys)", det.Description);
        Assert.Contains(det.ExcludedFiles, f => f.EndsWith("Brand.xaml"));
        Assert.Contains("\"paletteMode\": \"single\"", Report.ToJson(Auditor.Run(fx.Root, det, cfg)));
    }

    // ── 分類與門檻覆寫 ────────────────────────────────────────────────────

    [Fact] // extraText：後綴比對會把 TitleBorder 歸成 Border（裝飾）；加進 extraText 就是文字 ——
           // 樹走訪與 Style 的 TargetType 兩條路都要生效
    public void ExtraTextMakesCustomElementText()
    {
        const string xaml = """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <Grid.Resources>
                <Style x:Key="S" TargetType="TitleBorder">
                  <Setter Property="Background" Value="#FFFFFF"/>
                  <Setter Property="Foreground" Value="#EEEEEE"/>
                </Style>
              </Grid.Resources>
              <TitleBorder Foreground="#EEEEEE"/>
            </Grid>
            """;
        using var plain = new Fixture();
        plain.File("Main.xaml", xaml);
        var before = plain.Run();
        Assert.Equal(2, before.Findings.Count);
        Assert.All(before.Findings, f => Assert.Equal(Category.Decorative, f.Category));

        using var fx = new Fixture();
        fx.File("Main.xaml", xaml);
        fx.File(ToolConfig.FileName, """{ "classification": { "extraText": ["TitleBorder"] } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(2, r.Findings.Count);
        Assert.All(r.Findings, f => { Assert.True(f.IsText); Assert.Equal(Category.Fail, f.Category); });
    }

    [Fact] // thresholds.largeText 覆寫：大字級門檻改 4.0 → 3.5:1 的大字從 ok 變 warn
    public void LargeTextThresholdOverrideApplies()
    {
        const string xaml = """
            <Grid Background="#FFFFFF">
              <TextBlock FontSize="24" Foreground="#888888" Text="24px = 18pt，大字級，~3.5:1"/>
            </Grid>
            """;
        using var plain = new Fixture();
        plain.File("Main.xaml", xaml);
        Assert.Equal(Category.Ok, Assert.Single(plain.Run().Findings).Category);

        using var fx = new Fixture();
        fx.File("Main.xaml", xaml);
        fx.File(ToolConfig.FileName, """{ "thresholds": { "largeText": 4.0 } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var f = Assert.Single(Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg).Findings);
        Assert.Equal(4.0, f.Need);
        Assert.Equal(Category.Warn, f.Category);
    }

    [Fact] // thresholds.failFactor 覆寫：fail 線從 4.5×0.67 移到 4.5×0.9 → 3.45:1 從 warn 變 fail
    public void FailFactorOverrideMovesTheFailLine()
    {
        const string xaml = """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#8A8A8A" Text="~3.45:1"/>
            </Grid>
            """;
        using var plain = new Fixture();
        plain.File("Main.xaml", xaml);
        Assert.Equal(Category.Warn, Assert.Single(plain.Run().Findings).Category);

        using var fx = new Fixture();
        fx.File("Main.xaml", xaml);
        fx.File(ToolConfig.FileName, """{ "thresholds": { "failFactor": 0.9 } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(Category.Fail, Assert.Single(r.Findings).Category);
        Assert.Contains("fail-factor 0.9", Report.ToConsole(r));
    }

    [Theory] // rootBackground 的四種寫法都是合法的地板：裸鍵（包成 DynamicResource）、#hex、具名色、{…} markup
    [InlineData("Bg", "{Bg}")]
    [InlineData("{StaticResource Bg}", "{Bg}")]
    [InlineData("#202020", "#202020")]
    [InlineData("White", "#FFFFFF")]
    public void RootBackgroundAcceptsKeysHexNamedColoursAndMarkup(string rootBackground, string expectedBg)
    {
        using var fx = new Fixture();
        fx.File("Themes/DarkTheme.xaml", SmallDict);
        fx.File("Views/V.xaml", """
            <UserControl>
              <TextBlock Foreground="#777777" Text="根元素沒有背景"/>
            </UserControl>
            """);
        fx.File(ToolConfig.FileName, System.Text.Json.JsonSerializer.Serialize(new { rootBackground }));
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(expectedBg, Assert.Single(r.Findings).Bg);
        Assert.Equal(0, r.Unresolved);
    }

    [Fact] // 正規化本身：只有裸鍵名會被包；具名色比對不分大小寫（white 也是色，不是鍵）
    public void NormalizeRootBackgroundOnlyWrapsBareKeys()
    {
        Assert.Null(Auditor.NormalizeRootBackground(null));
        Assert.Equal("{DynamicResource Lib.Window.Background}", Auditor.NormalizeRootBackground("Lib.Window.Background"));
        Assert.Equal("white", Auditor.NormalizeRootBackground("white"));
        Assert.Equal("#FF202020", Auditor.NormalizeRootBackground("#FF202020"));
        Assert.Equal("{StaticResource X}", Auditor.NormalizeRootBackground("{StaticResource X}"));
    }
}

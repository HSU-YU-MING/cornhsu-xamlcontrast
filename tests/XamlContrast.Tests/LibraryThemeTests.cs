using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 第三方主題函式庫的內建色票（1.5）：那些函式庫的使用者的「最底層」。三份表（WPF-UI、MaterialDesign、
/// HandyControl）各有一條 <c>Regenerate…</c>，形狀一樣：設環境變數指向上游 checkout 再跑那條測試。
/// ⚠ MahApps 沒有表，而且不是漏掉 —— 它的主題檔是建置時產生的（1077 個鍵、112 個佔位符），
/// 見 <see cref="LibraryThemes"/> 的註解。
///
/// <see cref="RegenerateWpfUi"/>：設 <c>XAMLCONTRAST_WPFUI_SOURCE</c> 指向 lepoco/wpfui 的 checkout 再跑這條測試，
/// 只用兩個檔（sparse checkout 就夠）：<c>src/Wpf.Ui/Resources/Theme/Dark.xaml</c> 與 <c>Light.xaml</c>。
/// ⚠ 產完人工看 git diff 再 commit（跟 baseline 同一個規矩）。
/// 用工具自己的合併式色盤解析讀，不另寫解析器：讀到的就是「工具讀這兩個檔會讀到的值」。
/// 高對比的 <c>HC*.xaml</c> 不收（跟 WinUI 一樣不評高對比主題）。
/// </summary>
public class LibraryThemeTests
{
    private const string DarkFile = "src/Wpf.Ui/Resources/Theme/Dark.xaml";
    private const string LightFile = "src/Wpf.Ui/Resources/Theme/Light.xaml";

    [Fact]
    public void RegenerateWpfUi()
    {
        var src = Environment.GetEnvironmentVariable("XAMLCONTRAST_WPFUI_SOURCE");
        if (string.IsNullOrEmpty(src)) return; // 平常不跑：沒有上游原始碼
        var sha = Environment.GetEnvironmentVariable("XAMLCONTRAST_WPFUI_COMMIT") ?? "unknown";

        using var fx = new Fixture();
        // 檔名就是主題提示（Dark／Light）—— 合併式色盤靠它分側，跟它讀任何專案的兩份主題字典一樣
        fx.File("Themes/Dark.xaml", File.ReadAllText(Path.Combine(src, DarkFile)))
          .File("Themes/Light.xaml", File.ReadAllText(Path.Combine(src, LightFile)));
        var merged = PaletteDetector.MergedXamlPalette(fx.Root)!.Value;

        var entries = merged.Pal.Entries.OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(e => e.Key, e => new[] { e.Value.Dark, e.Value.Light });
        var colors = merged.Pal.DarkColors.Keys.Intersect(merged.Pal.LightColors.Keys).Order(StringComparer.Ordinal)
            .ToDictionary(k => k, k => new[] { merged.Pal.DarkColors[k], merged.Pal.LightColors[k] });
        // 使用者的強調色：brush 引用 SystemAccentColor…（WPF-UI 執行期從 Windows 設定算出來的，原始碼裡沒有值）
        var runtime = AccentKeys(new[] { DarkFile, LightFile }.Select(f => File.ReadAllText(Path.Combine(src, f))), entries);

        Write($"lepoco/wpfui@{sha[..Math.Min(12, sha.Length)]}", entries, colors, runtime, "WpfUiTheme.json");
    }

    /// <summary>三條 Regenerate 共用的尾段：序列化成跟 WinUiTheme.json 同一個形狀、固定 LF、寫進 Core。</summary>
    private static void Write(string source, Dictionary<string, string[]> entries,
        Dictionary<string, string[]> colors, IEnumerable<string> runtime, string file)
    {
        var data = new WinUiTheme.Data(source, entries, colors, [.. runtime]);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions(WinUiTheme.Json) { WriteIndented = true })
            .Replace("\r\n", "\n") + "\n";
        File.WriteAllText(Repo.PathOf($"src/XamlContrast.Core/{file}"), json);
    }

    /// <summary>那個表裡「brush 引用使用者強調色」的鍵 —— 執行期才知道，不給值。</summary>
    private static SortedSet<string> AccentKeys(IEnumerable<string> texts, Dictionary<string, string[]> entries)
    {
        var runtime = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
            foreach (var (k, target) in PaletteDetector.ParseBrushDefsText(text, keepInvalid: false).Ref)
                if (target.StartsWith("SystemAccentColor", StringComparison.Ordinal) && !entries.ContainsKey(k))
                    runtime.Add(k);
        return runtime;
    }

    /// <summary>那兩份／三份主題檔讀出來的色票表（合併式色盤的三段式：主題檔各佔一側、中性檔補洞）。</summary>
    private static (Dictionary<string, string[]> Entries, Dictionary<string, string[]> Colors) Read(Fixture fx)
    {
        var merged = PaletteDetector.MergedXamlPalette(fx.Root)!.Value;
        // ⚠ 別名要解：MaterialDesign 的主題檔整份是 <StaticResource x:Key="A" ResourceKey="B"/>，
        //   真正的色值在它 merge 的 Internal/…BaseThemeColors.xaml 裡。不解的話那張表是空的。
        //   刻意不走 PaletteDetector.Detect —— 它會把**上一版的內嵌表**補進來，產生自我污染。
        PaletteDetector.ResolveAliases(merged.Pal);
        var entries = merged.Pal.Entries.OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(e => e.Key, e => new[] { e.Value.Dark, e.Value.Light });
        var colors = merged.Pal.DarkColors.Keys.Intersect(merged.Pal.LightColors.Keys).Order(StringComparer.Ordinal)
            .ToDictionary(k => k, k => new[] { merged.Pal.DarkColors[k], merged.Pal.LightColors[k] });
        return (entries, colors);
    }

    [Fact] // MaterialDesignInXamlToolkit：Light／Dark 兩份字面值主題檔（各 90 個鍵、沒有佔位符）。
           // ⚠ 裡面只有「不隨 BundledTheme 的 PrimaryColor 變」的那些 —— 主色是使用者選的，不猜
    public void RegenerateMaterialDesign()
    {
        var src = Environment.GetEnvironmentVariable("XAMLCONTRAST_MATERIALDESIGN_SOURCE");
        if (string.IsNullOrEmpty(src)) return;
        var sha = Environment.GetEnvironmentVariable("XAMLCONTRAST_MATERIALDESIGN_COMMIT") ?? "unknown";
        const string dir = "src/MaterialDesignThemes.Wpf/Themes";

        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", File.ReadAllText(Path.Combine(src, dir, "MaterialDesignTheme.Dark.xaml")))
          .File("Themes/Light.xaml", File.ReadAllText(Path.Combine(src, dir, "MaterialDesignTheme.Light.xaml")))
          // 兩份主題檔只有別名,色值在這一份（兩個主題共用,中性檔只補洞）
          .File("Themes/BaseColors.xaml",
                File.ReadAllText(Path.Combine(src, dir, "Internal/MaterialDesignTheme.BaseThemeColors.xaml")))
          // ⚠ 舊鍵名也要收:真實的 App 用的多半是 MaterialDesignPaper／MaterialDesignBody 這些
          //   （v5 改名之前的寫法）。語料實測 VisualHFT 的 50 筆 unknown-palette-key 裡有 49 筆是
          //   MaterialDesignPaper —— 少了這一份,整張表對真實消費端幾乎沒有作用
          .File("Themes/Obsolete.xaml",
                File.ReadAllText(Path.Combine(src, dir, "MaterialDesignTheme.ObsoleteBrushes.xaml")));
        var (entries, colors) = Read(fx);

        Write($"MaterialDesignInXAML/MaterialDesignInXamlToolkit@{sha[..Math.Min(12, sha.Length)]}",
            entries, colors, [], "MaterialDesignTheme.json");
    }

    [Fact] // HandyControl：色值在 Basic/Colors/{Colors,ColorsDark}.xaml（各 35 個 Color），
           // brush 在 Basic/Brushes.xaml 引用它們（33 個，兩個主題共用）—— 正好是工具的三段式
    public void RegenerateHandyControl()
    {
        var src = Environment.GetEnvironmentVariable("XAMLCONTRAST_HANDYCONTROL_SOURCE");
        if (string.IsNullOrEmpty(src)) return;
        var sha = Environment.GetEnvironmentVariable("XAMLCONTRAST_HANDYCONTROL_COMMIT") ?? "unknown";
        const string dir = "src/Shared/HandyControl_Shared/Themes/Basic";

        using var fx = new Fixture();
        fx.File("Themes/Light.xaml", File.ReadAllText(Path.Combine(src, dir, "Colors/Colors.xaml")))
          .File("Themes/Dark.xaml", File.ReadAllText(Path.Combine(src, dir, "Colors/ColorsDark.xaml")))
          .File("Themes/Brushes.xaml", File.ReadAllText(Path.Combine(src, dir, "Brushes.xaml")));
        var (entries, colors) = Read(fx);

        Write($"HandyOrg/HandyControl@{sha[..Math.Min(12, sha.Length)]}", entries, colors, [], "HandyControlTheme.json");
    }

    [Theory] // 三份表的共同不變量:來源寫得出來、值是正規化的色碼、深淺兩欄都有、強調色鍵不混進有值的那一堆
    [InlineData(ThemeLibraries.WpfUi, "lepoco/wpfui@")]
    [InlineData(ThemeLibraries.MaterialDesign, "MaterialDesignInXAML/MaterialDesignInXamlToolkit@")]
    [InlineData(ThemeLibraries.HandyControl, "HandyOrg/HandyControl@")]
    public void EveryShippedTableNamesItsSourceAndHoldsOnlyColours(string library, string sourcePrefix)
    {
        var t = LibraryThemes.Of(library);

        Assert.StartsWith(sourcePrefix, t.Source);
        Assert.NotEmpty(t.Entries);
        Assert.All(t.Entries.Values, v => Assert.Equal(2, v.Length));
        Assert.All(t.Entries.Values.Concat(t.Colors.Values).SelectMany(v => v),
            v => Assert.Matches("^#(?:[0-9A-F]{6}|[0-9A-F]{8})$", v));
        Assert.Empty(t.Runtime.Intersect(t.Entries.Keys));
    }

    [Fact] // MaterialDesign 的那份:深淺不可以顛倒。上游 Light.xaml 的 Background 指向 Neutral900(#FFF1F1F1,淺灰)、
           // Dark.xaml 指向 Neutral100(#FF323232)—— 顛倒的話工具會把兩個主題的比值整個對調,而數字看起來一樣合理
    public void TheMaterialDesignTableKeepsTheThemesOnTheRightSides()
    {
        var t = LibraryThemes.Of(ThemeLibraries.MaterialDesign);

        Assert.Equal(["#FF323232", "#FFF1F1F1"], t.Entries["MaterialDesign.Brush.Background"]);
        Assert.Equal(["#FF454545", "#FFFFFFFF"], t.Entries["MaterialDesign.Brush.Card.Background"]);
        Assert.Equal(["#E5FFFFFF", "#E5000000"], t.Entries["MaterialDesign.Brush.Foreground"]);
        // ⚠ 舊鍵名(v5 改名前)也要在:真實的 App 用的多半是這些。語料實測 VisualHFT 的 50 筆
        //   unknown-palette-key 有 49 筆是 MaterialDesignPaper —— 少了它們,整張表對真實消費端幾乎沒作用
        //   (配對 33 → 83、覆蓋率 17.7% → 44.6% 就是補上它們的差別)
        Assert.Equal(t.Entries["MaterialDesign.Brush.Background"], t.Entries["MaterialDesignPaper"]);
        Assert.Equal(t.Entries["MaterialDesign.Brush.Foreground"], t.Entries["MaterialDesignBody"]);
    }

    [Fact] // HandyControl 的那份:色值在 Colors/ColorsDark，brush 在 Brushes.xaml 引用它們 —— 兩段要接得起來
    public void TheHandyControlTableResolvesBrushesThroughItsColours()
    {
        var t = LibraryThemes.Of(ThemeLibraries.HandyControl);

        Assert.Equal(["#1C1C1C", "#FFFFFF"], t.Entries["RegionBrush"]);
        Assert.Equal(["#2D2D30", "#EEEEEE"], t.Entries["SecondaryRegionBrush"]);
        Assert.Equal(["#FFFFFF", "#212121"], t.Entries["PrimaryTextBrush"]);
    }

    [Fact] // 色盤那一行不可以自相矛盾:專案自己沒有色盤時,不能印「no palette found …; + 124 brush(es)」
           // （v2rayN 實測）。整個色盤都是函式庫的預設值就照實說,而且 degraded 維持 true ——
           // 「專案自己的色盤仍然不知道」是真的,--strict-palette 照舊擋得住
    public void ThePaletteLineDoesNotContradictItselfWhenEverythingComesFromTheLibrary()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", WpfUiApp);
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource ApplicationBackgroundBrush}">
              <TextBlock Foreground="{DynamicResource TextFillColorPrimaryBrush}" Text="全部來自函式庫"/>
            </Window>
            """);
        var r = fx.Run();

        Assert.Equal(PaletteMode.None, r.Detection.Mode);
        Assert.DoesNotContain("no palette found", r.Detection.Description);
        Assert.Contains("defines no palette of its own", r.Detection.Description);
        Assert.Contains("WPF-UI default theme brush", r.Detection.Description);
        Assert.True(r.Detection.IsDegraded, "專案自己的色盤還是不知道 —— degraded 不能因為補了預設值就變 false");
    }

    [Fact] // MahApps 刻意沒有表（主題檔是建置時產生的）—— 不是漏掉。它的使用者走 rootBackground
    public void MahAppsHasNoTableOnPurpose()
    {
        Assert.False(LibraryThemes.Has(ThemeLibraries.MahApps));
        Assert.True(LibraryThemes.Has(ThemeLibraries.WpfUi));
        Assert.True(LibraryThemes.Has(ThemeLibraries.MaterialDesign));
        Assert.True(LibraryThemes.Has(ThemeLibraries.HandyControl));
    }

    [Fact] // 整條路徑:HandyControl 系的 App（只用函式庫的鍵、自己一個都沒定義）現在評得出來
    public void AHandyControlAppIsGradedFromTheShippedTable()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", """
            <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Application.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml"/>
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
              </Application.Resources>
            </Application>
            """);
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource RegionBrush}">
              <TextBlock Foreground="{DynamicResource PrimaryTextBrush}" Text="函式庫的鍵"/>
            </Window>
            """);
        var r = fx.Run();

        var f = Assert.Single(r.Findings);
        Assert.Equal(Category.Ok, f.Category);
        Assert.Equal("#FFFFFF", f.ColorsIn(ThemeSide.Dark)?.Fg);   // 深色:白字疊 #1C1C1C
        Assert.Equal("#212121", f.ColorsIn(ThemeSide.Light)?.Fg);  // 淺色:深字疊白
        Assert.Contains("HandyControl default theme brush", r.Detection.Description);
    }

    [Fact] // 出貨的那份表:值是 WPF-UI 自己的、強調色不猜、所有值都是正規化的色碼
    public void TheShippedTableHasWpfUiDefaultsAndNoAccentGuesses()
    {
        var t = LibraryThemes.Of(ThemeLibraries.WpfUi);

        Assert.StartsWith("lepoco/wpfui@", t.Source);
        // 視窗底色:深色 #FF202020、淺色 #FFFAFAFA（提示就是叫使用者設這個鍵）
        Assert.Equal("#FF202020", t.Entries["ApplicationBackgroundBrush"][0]);
        Assert.Contains("TextFillColorPrimaryBrush", t.Entries.Keys);
        Assert.Contains("WindowBackground", t.Entries.Keys);
        // 強調色來的鍵:執行期才知道,不給值
        Assert.NotEmpty(t.Runtime);
        Assert.Empty(t.Runtime.Intersect(t.Entries.Keys));
        Assert.All(t.Entries.Values.Concat(t.Colors.Values).SelectMany(v => v),
            v => Assert.Matches("^#(?:[0-9A-F]{6}|[0-9A-F]{8})$", v));
    }

    private const string WpfUiApp = """
        <Application xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
          <Application.Resources>
            <ResourceDictionary>
              <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark"/>
                <ui:ControlsDictionary/>
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
          </Application.Resources>
        </Application>
        """;

    [Fact] // 這就是 Bloxstrap 形狀:直接用函式庫的鍵、自己一個都沒定義。1.4 全部是 unknown-palette-key
    public void AProjectThatOnlyUsesWpfUiKeysIsGradedInsteadOfUnknown()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", WpfUiApp);
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource ApplicationBackgroundBrush}">
              <TextBlock Foreground="{DynamicResource TextFillColorPrimaryBrush}" Text="函式庫的鍵"/>
            </Window>
            """);
        var r = fx.Run();

        var f = Assert.Single(r.Findings);
        Assert.Equal(Category.Ok, f.Category);
        Assert.Equal(0, r.Unresolved);
        Assert.Contains("WPF-UI default theme brush", r.Detection.Description);
        Assert.Contains("lepoco/wpfui@", r.Detection.Description);
    }

    [Fact] // 覆蓋率提示不可以說謊:鍵是工具補的就要說是工具補的（Bloxstrap 實測:它一個鍵都沒自己定義,
           // 而舊的說法是「你的主題檔有宣告這個鍵」）。那一行設定照樣有效 —— 實測 0 組配對 → 55 組、46.2%
    public void TheHintSaysTheKeyCameFromTheShippedDefaultsNotTheRepo()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", WpfUiApp);
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <TextBlock Foreground="{DynamicResource TextFillColorPrimaryBrush}" Text="沒有任何祖先宣告背景 1"/>
              <TextBlock Foreground="{DynamicResource TextFillColorSecondaryBrush}" Text="沒有任何祖先宣告背景 2"/>
            </Window>
            """);
        var hint = Report.CoverageHint(fx.Run());

        Assert.NotNull(hint);
        Assert.Contains("WPF-UI", hint);
        Assert.Contains("from the library defaults this tool ships", hint);
        Assert.Contains("your repo doesn't declare it", hint);
        Assert.Contains("""{ "rootBackground": "WindowBackground" }""", hint);
        Assert.DoesNotContain("Your own theme files do declare", hint);
    }

    [Fact] // 專案自己定義的鍵一律優先 —— 補的是洞,不是蓋掉
    public void TheProjectsOwnDefinitionWins()
    {
        using var fx = new Fixture();
        fx.File("App.xaml", WpfUiApp);
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="ApplicationBackgroundBrush" Color="#000000"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Dim" Color="#2A2A2A"/>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="ApplicationBackgroundBrush" Color="#FFFFFF"/>
              <SolidColorBrush x:Key="Fg" Color="#212121"/>
              <SolidColorBrush x:Key="Dim" Color="#E8E8E8"/>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource ApplicationBackgroundBrush}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="專案自己的底色"/>
            </Window>
            """);

        var f = Assert.Single(fx.Run().Findings);
        Assert.Equal("#000000", f.ColorsIn(ThemeSide.Dark)?.Bg);   // 不是 WPF-UI 的 #FF202020
        Assert.Equal("#FFFFFF", f.ColorsIn(ThemeSide.Light)?.Bg);
    }

    [Fact] // 沒引用 WPF-UI 的專案不碰 —— 鍵名跟 WinUI 大量重疊,值不一定一樣,不能亂補
    public void AProjectThatDoesNotUseWpfUiGetsNothing()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource ApplicationBackgroundBrush}">
              <TextBlock Foreground="{DynamicResource TextFillColorPrimaryBrush}" Text="沒有引用函式庫"/>
            </Window>
            """);
        var r = fx.Run();

        Assert.Empty(r.Findings);
        Assert.DoesNotContain("WPF-UI", r.Detection.Description);
        Assert.True(r.Unresolved > 0, "鍵查不到就是查不到 —— 不要拿別的函式庫的值來填");
    }
}

using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// WPF-UI 的內建主題色票（1.5）：`Wpf.Ui` 套件使用者的「最底層」。
///
/// <see cref="Regenerate"/> 產生：設 <c>XAMLCONTRAST_WPFUI_SOURCE</c> 指向 lepoco/wpfui 的 checkout 再跑這條測試，
/// 只用兩個檔（sparse checkout 就夠）：<c>src/Wpf.Ui/Resources/Theme/Dark.xaml</c> 與 <c>Light.xaml</c>。
/// ⚠ 產完人工看 git diff 再 commit（跟 baseline 同一個規矩）。
/// 用工具自己的合併式色盤解析讀，不另寫解析器：讀到的就是「工具讀這兩個檔會讀到的值」。
/// 高對比的 <c>HC*.xaml</c> 不收（跟 WinUI 一樣不評高對比主題）。
/// </summary>
public class WpfUiThemeTests
{
    private const string DarkFile = "src/Wpf.Ui/Resources/Theme/Dark.xaml";
    private const string LightFile = "src/Wpf.Ui/Resources/Theme/Light.xaml";

    [Fact]
    public void Regenerate()
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
        var runtime = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { DarkFile, LightFile })
            foreach (var (k, target) in PaletteDetector.ParseBrushDefsText(
                         File.ReadAllText(Path.Combine(src, file)), keepInvalid: false).Ref)
                if (target.StartsWith("SystemAccentColor", StringComparison.Ordinal) && !entries.ContainsKey(k))
                    runtime.Add(k);

        var data = new WinUiTheme.Data($"lepoco/wpfui@{sha[..Math.Min(12, sha.Length)]}", entries, colors, [.. runtime]);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions(WinUiTheme.Json) { WriteIndented = true })
            .Replace("\r\n", "\n") + "\n";
        File.WriteAllText(Repo.PathOf("src/XamlContrast.Core/WpfUiTheme.json"), json);
    }

    [Fact] // 出貨的那份表:值是 WPF-UI 自己的、強調色不猜、所有值都是正規化的色碼
    public void TheShippedTableHasWpfUiDefaultsAndNoAccentGuesses()
    {
        var t = WpfUiTheme.Current;

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

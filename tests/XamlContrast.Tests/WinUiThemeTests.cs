using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// WinUI 的框架預設主題色（1.4，<c>WinUiTheme</c>）。資料檔 <c>src/XamlContrast.Core/WinUiTheme.json</c> 由
/// <see cref="Regenerate"/> 產生：設 <c>XAMLCONTRAST_WINUI_SOURCE</c> 指向 microsoft/microsoft-ui-xaml 的 checkout 再跑這條測試，
/// 只用兩個檔（sparse checkout 就夠）：
/// <c>controls/dev/CommonStyles/Common_themeresources_any.xaml</c>（Fluent 的 TextFillColor… 等）與
/// <c>dxaml/xcp/dxaml/themes/generic.xaml</c>（SystemControl… 等舊名）。⚠ 產完人工看 git diff 再 commit（跟 baseline 同一個規矩）。
/// 用工具自己的 ThemeDictionaries 解析讀，不另寫解析器：讀到的就是「工具讀這兩個檔會讀到的值」。
/// </summary>
public class WinUiThemeTests
{
    private const string CommonFile = "controls/dev/CommonStyles/Common_themeresources_any.xaml";
    private const string GenericFile = "dxaml/xcp/dxaml/themes/generic.xaml";

    [Fact]
    public void Regenerate()
    {
        var src = Environment.GetEnvironmentVariable("XAMLCONTRAST_WINUI_SOURCE");
        if (string.IsNullOrEmpty(src)) return; // 平常不跑：沒有上游原始碼
        var sha = Environment.GetEnvironmentVariable("XAMLCONTRAST_WINUI_COMMIT") ?? "unknown";

        using var fx = new Fixture();
        // 順序 = 優先序：控制項函式庫（Common_themeresources）蓋過系統的 generic.xaml，跟 XamlControlsResources 的合併順序一樣
        fx.File("Themes/1-common.xaml", File.ReadAllText(Path.Combine(src, CommonFile)))
          .File("Themes/2-generic.xaml", File.ReadAllText(Path.Combine(src, GenericFile)));
        var merged = PaletteDetector.MergedXamlPalette(fx.Root)!.Value;

        var entries = merged.Pal.Entries.OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(e => e.Key, e => new[] { e.Value.Dark, e.Value.Light });
        var colors = merged.Pal.DarkColors.Keys.Intersect(merged.Pal.LightColors.Keys).Order(StringComparer.Ordinal)
            .ToDictionary(k => k, k => new[] { merged.Pal.DarkColors[k], merged.Pal.LightColors[k] });
        // 從使用者強調色來的鍵：深或淺任一側的 brush 引用 SystemAccentColor…（框架從 Windows 設定讀的，原始碼裡沒有值）
        var runtime = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { CommonFile, GenericFile })
        {
            var sections = PaletteDetector.ThemeDictionarySections(File.ReadAllText(Path.Combine(src, file)))!.Value;
            foreach (var section in new[] { sections.Dark, sections.Light })
                foreach (var (k, target) in PaletteDetector.ParseBrushDefsText(section ?? "", keepInvalid: false).Ref)
                    if (target.StartsWith("SystemAccentColor", StringComparison.Ordinal) && !entries.ContainsKey(k)) runtime.Add(k);
        }

        var data = new WinUiTheme.Data($"microsoft/microsoft-ui-xaml@{sha[..Math.Min(12, sha.Length)]}", entries, colors, [.. runtime]);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions(WinUiTheme.Json) { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
        File.WriteAllText(Repo.PathOf("src/XamlContrast.Core/WinUiTheme.json"), json);
    }

    [Fact]
    public void TheShippedTableHasTheFluentDefaultsAndNoAccentGuesses()
    {
        var t = WinUiTheme.Current;

        Assert.StartsWith("microsoft/microsoft-ui-xaml@", t.Source);
        Assert.Equal(["#FFFFFF", "#E4000000"], t.Entries["TextFillColorPrimaryBrush"]);   // 深色主題白字、淺色主題 89% 黑
        Assert.Contains("SystemControlForegroundAltHighBrush", t.Entries.Keys);          // generic.xaml 的舊名也在
        Assert.Contains("AccentTextFillColorPrimaryBrush", t.Runtime);                    // 強調色：執行期才知道
        Assert.Empty(t.Runtime.Intersect(t.Entries.Keys));
        Assert.All(t.Entries.Values.Concat(t.Colors.Values).SelectMany(v => v),
            v => Assert.Matches("^#(?:[0-9A-F]{6}|[0-9A-F]{8})$", v));
    }

    private const string WinUiPage = """
        <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid Background="{ThemeResource SolidBackgroundFillColorBaseBrush}">
            <TextBlock Foreground="{ThemeResource TextFillColorPrimaryBrush}" Text="a"/>
            <TextBlock Foreground="{ThemeResource AccentTextFillColorPrimaryBrush}" Text="b"/>
          </Grid>
        </Page>
        """;

    [Fact]
    public void AWinUiProjectGetsTheFrameworkDefaultsAndAccentStaysUnresolved()
    {
        using var fx = new Fixture();
        fx.File("MainPage.xaml", WinUiPage);

        var r = fx.Run();

        var text = Assert.Single(r.Findings);
        var t = WinUiTheme.Current;
        var bg = t.Entries["SolidBackgroundFillColorBaseBrush"];
        Assert.Equal(bg[0], text.ColorsDark!.Bg);
        Assert.Equal(bg[1], text.ColorsLight!.Bg);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);   // 強調色的那一組：不猜
        Assert.Contains("WinUI default theme resource", r.Detection.Description);
    }

    [Fact]
    public void TheProjectsOwnDefinitionWins()
    {
        using var fx = new Fixture();
        fx.File("Themes/Colors.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="TextFillColorPrimaryBrush" Color="#123456"/>
              <SolidColorBrush x:Key="A" Color="#111111"/>
              <SolidColorBrush x:Key="B" Color="#222222"/>
            </ResourceDictionary>
            """).File("MainPage.xaml", WinUiPage);

        var r = fx.Run();

        Assert.Equal(("#123456", "#123456"), r.Detection.Palette.Entries["TextFillColorPrimaryBrush"]);
        Assert.Equal("#123456", Assert.Single(r.Findings).ColorsDark!.Fg);
    }

    [Fact] // 沒用 {ThemeResource 的專案（WPF）完全不碰：Wpf.Ui 之類的函式庫借用同名鍵，值不一定一樣
    public void AWpfProjectIsNotTouched()
    {
        using var fx = new Fixture();
        fx.File("MainWindow.xaml", """
            <Window><Grid Background="{DynamicResource SolidBackgroundFillColorBaseBrush}">
              <TextBlock Foreground="{DynamicResource TextFillColorPrimaryBrush}" Text="a"/></Grid></Window>
            """);

        var r = fx.Run();

        Assert.Empty(r.Findings);
        Assert.True(r.UnresolvedBy[UnresolvedReason.UnknownPaletteKey] >= 1);
        Assert.DoesNotContain("WinUI", r.Detection.Description);
    }
}

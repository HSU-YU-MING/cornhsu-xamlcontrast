using System.Text.Json;

namespace XamlContrast.Core;

/// <summary>
/// WinUI 內建的 Fluent 主題資源（1.4）：<c>TextFillColorPrimaryBrush</c>、<c>CardBackgroundFillColorDefaultBrush</c>…
/// 定義在框架裡、不在專案裡，WinUI 專案用 <c>{ThemeResource …}</c> 引用的顏色大多是它們。
/// 不知道它們的話，WinUI 專案幾乎每一組都是 unknown-palette-key（WinUI Gallery 實測：172 筆、34 個鍵，評得到的只有 49 組）。
/// <para>
/// 資料是框架原始碼的預設值（<c>WinUiTheme.json</c>，由 microsoft/microsoft-ui-xaml 的
/// <c>Common_themeresources_any.xaml</c> 與 <c>generic.xaml</c> 產生，MIT；產生方式見 <c>WinUiThemeTests</c>）——
/// 工具自己的 ThemeDictionaries 解析讀出來的，不是手抄。<b>不猜</b>：
/// 從使用者的 Windows 強調色（<c>SystemAccentColor…</c>）來的鍵是執行期才知道（bound-or-gradient），不給值；
/// 高對比主題不評（跟專案自己的 ThemeDictionaries 一樣）。
/// </para>
/// <para>
/// 只在專案用到 <c>{ThemeResource</c> 時才補（WPF 沒有這個寫法；WPF 的 Wpf.Ui 之類函式庫借用同名鍵、值不一定一樣）。
/// 專案自己定義的鍵一律優先：只補專案沒有的鍵，專案定義了但讀不懂的鍵也不補（那是專案的值讀不懂，不是沒有）。
/// </para>
/// </summary>
internal static class WinUiTheme
{
    internal sealed record Data(
        string Source,
        Dictionary<string, string[]> Entries,
        Dictionary<string, string[]> Colors,
        List<string> Runtime);

    private static readonly Lazy<Data> Table = new(Load);

    internal static Data Current => Table.Value;

    internal const string ResourceName = "XamlContrast.Core.WinUiTheme.json";

    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static Data Load()
    {
        using var s = typeof(WinUiTheme).Assembly.GetManifestResourceStream(ResourceName)
                      ?? throw new InvalidOperationException($"embedded resource {ResourceName} is missing");
        return JsonSerializer.Deserialize<Data>(s, Json) ?? throw new InvalidOperationException($"{ResourceName} is empty");
    }

    /// <summary>專案有沒有用到 <c>{ThemeResource</c>（WinUI／UWP 的寫法）。測試素材路徑不算，跟色盤偵測同一套。</summary>
    internal static bool UsesThemeResource(string root)
    {
        foreach (var f in PaletteDetector.EnumerateFiles(root, "*.xaml"))
        {
            if (PaletteDetector.LooksLikeFixture(Path.GetRelativePath(root, f))) continue;
            try
            {
                if (File.ReadAllText(f).Contains("{ThemeResource", StringComparison.Ordinal)) return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    /// <summary>把框架的預設值補進專案的色盤（全域與每一個 App 作用域），描述加一句。</summary>
    internal static void Apply(string root, PaletteDetection d)
    {
        if (!UsesThemeResource(root)) return;
        var data = Current;
        var added = Augment(d.Palette, d.DroppedKeys, data);
        foreach (var scope in d.Scopes ?? []) Augment(scope.Palette, d.DroppedKeys, data);
        if (added == 0) return;
        var runtime = data.Runtime.Count(k => !d.Palette.Entries.ContainsKey(k));
        d.Description += $"; + {added} WinUI default theme resource(s) the project doesn't define (Fluent theme, {data.Source})"
                         + (runtime > 0 ? $"; {runtime} key(s) that follow the user's Windows accent colour stay unresolved" : "");
    }

    /// <summary>補進一個色盤。回傳補了幾個色票鍵。</summary>
    internal static int Augment(Palette pal, IReadOnlySet<string> dropped, Data data)
    {
        bool ProjectHas(string k) => pal.Entries.ContainsKey(k) || pal.RuntimeKeys.Contains(k) || pal.LocalOnly.ContainsKey(k)
                                     || pal.MissingRefs.ContainsKey(k) || dropped.Contains(k);
        var added = 0;
        foreach (var (k, v) in data.Entries)
        {
            if (ProjectHas(k)) continue;
            pal.Entries[k] = (v[0], v[1]);
            added++;
        }
        // Color 表：專案的區域 brush 引用框架的 Color（Color="{ThemeResource SystemBaseHighColor}"）時逐側解析用
        foreach (var (k, v) in data.Colors)
        {
            pal.DarkColors.TryAdd(k, v[0]);
            pal.LightColors.TryAdd(k, v[1]);
        }
        foreach (var k in data.Runtime.Where(k => !ProjectHas(k))) pal.RuntimeKeys.Add(k);
        return added;
    }
}

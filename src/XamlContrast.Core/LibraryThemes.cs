using System.Text.Json;

namespace XamlContrast.Core;

/// <summary>
/// 第三方 WPF 主題函式庫的內建色票（1.5）：`Wpf.Ui`、`MaterialDesignThemes`、`HandyControl` 的
/// 預設值定義在套件裡（編譯成 BAML、在組件的資源中），不在使用者的專案裡。
///
/// <para>為什麼需要：這類專案把函式庫的鍵直接拿來用、自己一個都沒抄，於是每一組都是
/// unknown-palette-key。實測（語料，零設定）：Bloxstrap 38 個 XAML、**0 組配對**、111 個鍵查不到；
/// QuickLook 51 個檔只看懂 7 組（1.8%）。`rootBackground` 救不了這種形狀 —— 它只補視窗底色那一層。</para>
///
/// <para>做法與 1.4 的 <see cref="WinUiTheme"/> 完全一樣（同一個資料形狀、同一套補洞邏輯）：
/// 資料不是手抄，是拿各函式庫自己的主題檔餵給工具自己的合併式色盤解析產生（見 <c>LibraryThemeTests</c>），
/// 來源 commit 記在每份 json 的 <c>source</c> 欄位。<b>不猜</b>：從使用者強調色算出來的鍵
/// （<c>SystemAccentColor…</c>）執行期才知道，不給值；高對比主題不收。</para>
///
/// <para>⚠ 只在專案真的引用那個函式庫時才補（<see cref="ThemeLibraries.DetectInProject"/>）。
/// 這些函式庫的鍵名彼此（以及與 WinUI）大量重疊，但**值不一樣** —— 所以一張表只給它自己的專案用。
/// 專案自己定義的鍵一律優先；專案定義了但讀不懂的鍵也不補（那是「值讀不懂」，不是「沒有」）。</para>
///
/// <para>⚠ <b>MahApps.Metro 不在這裡</b>，而且不是漏掉：它的主題檔是**建置時產生的**
/// （<c>Styles/Themes/Theme.Template.xaml</c> 與 <c>Styles/ThemeBase/Theme.Base.Template.xaml</c>，
/// 1077 個鍵、112 個 <c>{{佔位符}}</c>），repo 裡沒有任何一份有具體色值的主題檔。要補它就得把它的
/// 產生步驟重做一遍，而且值還跟使用者選的強調色（Light.Blue／Light.Red…）有關 —— 那是猜。
/// MahApps 的使用者走 config 的 <c>rootBackground</c>（覆蓋率提示會指名它），實測一行 9.4% → 83.5%。</para>
///
/// <para>⚠ 版本漂移是這個做法的已知代價：使用者裝的版本不一定等於這份快照的 commit。
/// 色票鍵的值在同一個 major 內很少動，而「有值可比」比「全部 unknown-palette-key」接近事實 ——
/// 報告的色盤那一行會寫出來源 commit，讀的人看得到這份值是哪一版的。</para>
/// </summary>
internal static class LibraryThemes
{
    /// <param name="Library">函式庫名（<see cref="ThemeLibraries"/> 的常數）。</param>
    /// <param name="Resource">內嵌的 json 資源名。</param>
    /// <param name="What">描述裡要怎麼稱呼補進去的東西。</param>
    private sealed record Table(string Library, string Resource, string What);

    private static readonly Table[] All =
    [
        new(ThemeLibraries.WpfUi, "XamlContrast.Core.WpfUiTheme.json", "WPF-UI default theme brush(es)"),
        new(ThemeLibraries.MaterialDesign, "XamlContrast.Core.MaterialDesignTheme.json", "MaterialDesign default theme brush(es)"),
        new(ThemeLibraries.HandyControl, "XamlContrast.Core.HandyControlTheme.json", "HandyControl default theme brush(es)"),
    ];

    private static readonly Dictionary<string, Lazy<WinUiTheme.Data>> Loaders =
        All.ToDictionary(t => t.Library, t => new Lazy<WinUiTheme.Data>(() => Load(t.Resource)), StringComparer.Ordinal);

    /// <summary>這個函式庫有沒有內嵌的色票表（MahApps 沒有，理由見類別註解）。</summary>
    internal static bool Has(string library) => Loaders.ContainsKey(library);

    /// <summary>那個函式庫的表（沒有就丟例外 —— 呼叫端要先問 <see cref="Has"/>）。</summary>
    internal static WinUiTheme.Data Of(string library) => Loaders[library].Value;

    private static WinUiTheme.Data Load(string resource)
    {
        using var s = typeof(LibraryThemes).Assembly.GetManifestResourceStream(resource)
                      ?? throw new InvalidOperationException($"embedded resource {resource} is missing");
        return JsonSerializer.Deserialize<WinUiTheme.Data>(s, WinUiTheme.Json)
               ?? throw new InvalidOperationException($"{resource} is empty");
    }

    /// <summary>
    /// 把專案引用到的函式庫的預設值補進色盤（全域與每一個 App 作用域），描述加一句。
    ///
    /// ⚠ 專案同時用兩個函式庫時（實測 MaaAssistantArknights：MaterialDesign ＋ HandyControl），
    /// 只補**兩邊值一致**的鍵。同名而值不同的，執行期看合併字典的順序 —— 靜態不可知，所以不猜。
    /// 挑其中一個來填是最糟的做法：數字看起來一樣合理，但有一半的機率是編的。
    /// </summary>
    internal static void Apply(string root, PaletteDetection d)
    {
        var libraries = ThemeLibraries.DetectInProject(root);
        d.Libraries = libraries;
        var tables = libraries.Where(Has).Select(l => (Library: l, Data: Of(l))).ToList();
        if (tables.Count == 0) return;

        WinUiTheme.Data data;
        if (tables.Count == 1) data = tables[0].Data;
        else
        {
            // 多個函式庫：只有「兩份表都定義、而且值不一樣」的鍵才丟掉（執行期看合併順序，不知道哪一份會贏）。
            // ⚠ 只有一份表定義的鍵沒有歧義 —— 全丟掉的話，MaaAssistantArknights 那種專案一個鍵都補不到，
            //   等於白做。要丟的是說不清楚的那幾個，不是全部。
            static Dictionary<string, string[]> Unanimous(IEnumerable<Dictionary<string, string[]>> maps)
            {
                var all = new Dictionary<string, string[]?>(StringComparer.Ordinal);
                foreach (var m in maps)
                    foreach (var (k, v) in m)
                    {
                        if (!all.TryGetValue(k, out var seen)) all[k] = v;
                        else if (seen is not null && !seen.SequenceEqual(v)) all[k] = null; // 兩份表不同調 → 不補
                    }
                return all.Where(e => e.Value is not null).ToDictionary(e => e.Key, e => e.Value!, StringComparer.Ordinal);
            }
            var entries = Unanimous(tables.Select(t => t.Data.Entries));
            var colors = Unanimous(tables.Select(t => t.Data.Colors));
            var runtime = tables.SelectMany(t => t.Data.Runtime).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            data = new WinUiTheme.Data(string.Join(" + ", tables.Select(t => t.Data.Source)), entries, colors, runtime);
        }

        // 補之前先記下「這些值是工具給的，不是專案自己的」—— 覆蓋率提示要據此說對話
        foreach (var k in data.Entries.Keys.Where(k => !d.Palette.Entries.ContainsKey(k))) d.FrameworkKeys.Add(k);
        var added = WinUiTheme.Augment(d.Palette, d.DroppedKeys, data);
        foreach (var scope in d.Scopes ?? []) WinUiTheme.Augment(scope.Palette, d.DroppedKeys, data);
        if (added == 0) return;
        var what = tables.Count == 1
            ? All.First(t => t.Library == tables[0].Library).What
            : $"default theme brush(es) ({string.Join(" + ", tables.Select(t => t.Library))}, only where they agree)";
        Note(d, added, what, data.Source, data.Runtime.Count(k => !d.Palette.Entries.ContainsKey(k)));
    }

    /// <summary>
    /// 色盤那一行要怎麼寫。
    ///
    /// ⚠ 專案自己**沒有**色盤時不能只是把句子接在後面：那會印出
    /// 「no palette found — auditing hardcoded colors only; + 124 … brush(es)」—— 前半句已經不成立了
    /// （v2rayN 實測）。自相矛盾的報告跟謊報是同一件事，讀的人只能挑一半相信。
    /// 這種專案整個色盤都是函式庫的預設值，就照實說，而且 <c>paletteDegraded</c> 維持 true ——
    /// 「專案自己的色盤仍然不知道」是真的（<c>--strict-palette</c> 照舊擋得住）。
    /// </summary>
    internal static void Note(PaletteDetection d, int added, string what, string source, int runtimeKeys)
    {
        var accent = runtimeKeys > 0
            ? $"; {runtimeKeys} key(s) that follow the user's accent colour stay unresolved" : "";
        d.Description = d.Mode == PaletteMode.None
            ? $"the project defines no palette of its own (no ResourceDictionary colour keys, no C# tuples); "
              + $"the {added} key(s) in use here are {what} ({source}) — hard-coded colours are audited as usual{accent}"
            : d.Description + $"; + {added} {what} the project doesn't define ({source}){accent}";
    }
}

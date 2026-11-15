using System.Text.Json;

namespace XamlContrast.Core;

/// <summary>
/// WPF-UI（`Wpf.Ui` 套件）內建主題的預設色票（1.5）：<c>ApplicationBackgroundBrush</c>、
/// <c>TextFillColorPrimaryBrush</c>… 定義在套件裡（編譯成 BAML、在組件的資源中），不在專案裡。
///
/// <para>為什麼需要：這類專案把函式庫的鍵直接拿來用、自己一個都沒抄，於是每一組都是
/// unknown-palette-key。實測（語料，零設定）：Bloxstrap 38 個 XAML、**0 組配對**、111 個鍵查不到；
/// QuickLook 51 個檔只看懂 7 組（1.8%）。`rootBackground` 救不了這種形狀 —— 它只補視窗底色那一層。</para>
///
/// <para>做法與 1.4 的 <see cref="WinUiTheme"/> 完全一樣（同一個資料形狀、同一套補洞邏輯）：
/// 資料不是手抄，是拿 WPF-UI 自己的 <c>Dark.xaml</c>／<c>Light.xaml</c> 餵給工具自己的
/// 合併式色盤解析產生（見 <c>WpfUiThemeTests.Regenerate</c>），commit 記在 <c>Source</c> 裡。
/// <b>不猜</b>：從使用者的 Windows 強調色來的鍵（<c>SystemAccentColor…</c>）執行期才知道，不給值；
/// 高對比主題（<c>HC*.xaml</c>）不收。</para>
///
/// <para>⚠ 只在專案真的引用 WPF-UI 時才補（<see cref="ThemeLibraries.DetectInProject"/>）。
/// 它的鍵名與 WinUI 大量重疊，但**值不一定一樣** —— 所以不能把 WinUI 的表拿來給 WPF-UI 專案用，
/// 反之亦然（1.4 的 WinUiTheme 已經寫下這條，這裡是它的另一半）。
/// 專案自己定義的鍵一律優先；專案定義了但讀不懂的鍵也不補（那是「值讀不懂」，不是「沒有」）。</para>
///
/// <para>⚠ 版本漂移是這個做法的已知代價：使用者裝的 Wpf.Ui 版本不一定等於這份快照的 commit。
/// 色票鍵的值在同一個 major 內很少動，而「有值可比」比「全部 unknown-palette-key」接近事實 ——
/// 但報告會在色盤描述裡寫出來源 commit，讀的人看得到這份值是哪一版的。</para>
/// </summary>
internal static class WpfUiTheme
{
    private static readonly Lazy<WinUiTheme.Data> Table = new(Load);

    internal static WinUiTheme.Data Current => Table.Value;

    internal const string ResourceName = "XamlContrast.Core.WpfUiTheme.json";

    private static WinUiTheme.Data Load()
    {
        using var s = typeof(WpfUiTheme).Assembly.GetManifestResourceStream(ResourceName)
                      ?? throw new InvalidOperationException($"embedded resource {ResourceName} is missing");
        return JsonSerializer.Deserialize<WinUiTheme.Data>(s, WinUiTheme.Json)
               ?? throw new InvalidOperationException($"{ResourceName} is empty");
    }

    /// <summary>把 WPF-UI 的預設值補進專案的色盤（全域與每一個 App 作用域），描述加一句。</summary>
    internal static void Apply(string root, PaletteDetection d)
    {
        if (ThemeLibraries.DetectInProject(root) != ThemeLibraries.WpfUi) return;
        var data = Current;
        // 補之前先記下「這些值是工具給的,不是專案自己的」—— 提示要據此說對話
        foreach (var k in data.Entries.Keys.Where(k => !d.Palette.Entries.ContainsKey(k))) d.FrameworkKeys.Add(k);
        var added = WinUiTheme.Augment(d.Palette, d.DroppedKeys, data);
        foreach (var scope in d.Scopes ?? []) WinUiTheme.Augment(scope.Palette, d.DroppedKeys, data);
        if (added == 0) return;
        var runtime = data.Runtime.Count(k => !d.Palette.Entries.ContainsKey(k));
        d.Description += $"; + {added} WPF-UI default theme brush(es) the project doesn't define ({data.Source})"
                         + (runtime > 0 ? $"; {runtime} key(s) that follow the user's accent colour stay unresolved" : "");
    }
}

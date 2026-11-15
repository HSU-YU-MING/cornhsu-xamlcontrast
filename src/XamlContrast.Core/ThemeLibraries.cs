using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>
/// 專案引用的第三方 WPF 主題函式庫（1.5）—— 只用在覆蓋率提示上。
///
/// 為什麼需要它：這類專案的色票鍵多半抄在自己的 repo 裡，但「視窗 → 那個鍵」的隱含樣式住在
/// NuGet 套件裡（編譯成 BAML、在組件的資源裡），靜態分析看不到。於是整棵樹找不到底色，
/// 覆蓋率掉到個位數、exit 3。逃生口是 config 的 <c>rootBackground</c> ——
/// 實測 NETworkManager 加一行：135 組 → 1161 組、覆蓋率 9.4% → 83.5%。
///
/// ⚠ 1.4.2 已經學過一次這個教訓的反面：提示原本一律叫人去設「MahApps 的鍵」，
/// 而用 WPF 內建 Fluent 主題（ThemeMode）的專案根本沒有那個鍵。**錯方向的提示比沒有提示更糟**
/// —— 人會以為試過了、沒用。所以這裡也不猜：偵測到哪一個函式庫就說那一個，
/// 而且候選鍵要先確認「這個專案自己的色盤裡真的有」才叫人去設它（<see cref="BackgroundKeyIn"/>）。
///
/// 偵測標記與候選鍵都來自各函式庫自己的原始碼（語料裡釘住的 clone，見 baselines/corpus/corpus.txt）。
/// 標記字串是**本檔的常數**，不是掃描到的內容 —— 印進提示是安全的（被掃描專案的 XAML 一律不可信，
/// 見 <see cref="Report.ConsoleSafe"/>）。
/// </summary>
internal static class ThemeLibraries
{
    /// <param name="Name">印給使用者看的名字（本檔常數）。</param>
    /// <param name="PackMarkers">pack URI 裡的組件片段。</param>
    /// <param name="ElementMarkers">那個函式庫自己的字典元素（MaterialDesign 的 BundledTheme、WPF-UI 的 ControlsDictionary）。</param>
    /// <param name="BackgroundKeys">視窗底色鍵的候選，依「函式庫自己用得最多的」排序。</param>
    internal sealed record Library(string Name, string[] PackMarkers, string[] ElementMarkers, string[] BackgroundKeys);

    /// <summary>名字的常數（補預設色票時要比對它，不要拿字串字面值到處寫）。</summary>
    internal const string WpfUi = "WPF-UI";
    internal const string MaterialDesign = "MaterialDesignInXamlToolkit";
    internal const string HandyControl = "HandyControl";
    internal const string MahApps = "MahApps.Metro";

    private static readonly Library[] All =
    [
        // MahApps.Metro：Styles 裡 22 處用 ThemeBackground、1 處用 Window.Background（9466e8a）
        new(MahApps, ["/MahApps.Metro;component/"], [],
            ["MahApps.Brushes.ThemeBackground", "MahApps.Brushes.Window.Background"]),
        // MaterialDesignInXamlToolkit：新版是 MaterialDesign.Brush.Background，舊版 MaterialDesignPaper（0877280）
        new(MaterialDesign,
            ["/MaterialDesignThemes.Wpf;component/", "/MaterialDesignColors;component/"], ["BundledTheme"],
            ["MaterialDesign.Brush.Background", "MaterialDesignPaper"]),
        // WPF-UI：FluentWindow 的 Style 設 WindowBackground；頁面層是 ApplicationBackgroundBrush（ffebacd）
        new(WpfUi, ["/Wpf.Ui;component/"], ["ControlsDictionary", "ThemesDictionary"],
            ["WindowBackground", "ApplicationBackgroundBrush"]),
        // HandyControl：Window 的隱含樣式設 SecondaryRegionBrush（e9ad3d7）
        new(HandyControl, ["/HandyControl;component/"], [],
            ["SecondaryRegionBrush", "RegionBrush"]),
    ];

    /// <summary>這份 XAML 有沒有引用某個主題函式庫（回傳函式庫名；沒有是 null）。</summary>
    public static string? Detect(XDocument doc)
    {
        foreach (var el in doc.Descendants())
        {
            foreach (var lib in All)
            {
                if (Array.IndexOf(lib.ElementMarkers, el.Name.LocalName) >= 0) return lib.Name;
                foreach (var attr in el.Attributes())
                    foreach (var marker in lib.PackMarkers)
                        if (attr.Value.Contains(marker, StringComparison.OrdinalIgnoreCase)) return lib.Name;
            }
        }
        return null;
    }

    /// <summary>
    /// 這個函式庫的視窗底色鍵裡，**這個專案自己的色盤真的有**的第一個（都沒有是 null）。
    ///
    /// 為什麼要查：`rootBackground` 給一個色盤裡沒有的鍵是 exit 2（用法錯誤）。叫人去設一個
    /// 會讓他 exit 2 的鍵，跟 1.4.2 那個「去找不存在的 MahApps 鍵」是同一種錯 —— 所以查不到就改口
    /// 叫他給色碼。
    /// </summary>
    public static string? BackgroundKeyIn(string library, Palette palette)
    {
        var lib = All.FirstOrDefault(l => l.Name == library);
        if (lib is null) return null;
        return lib.BackgroundKeys.FirstOrDefault(k => palette.Entries.ContainsKey(k));
    }

    /// <summary>
    /// 整個專案引用到的主題函式庫（回傳第一個比到的名字；沒有是 null）。
    /// 純文字掃描 —— 補框架預設色票時在色盤偵測階段就要知道，那時還沒有載入的 XDocument
    /// （同 <see cref="WinUiTheme.UsesThemeResource"/> 的做法）。測試素材路徑不算。
    /// </summary>
    public static string? DetectInProject(string root)
    {
        foreach (var f in PaletteDetector.EnumerateFiles(root, "*.xaml"))
        {
            if (PaletteDetector.LooksLikeFixture(Path.GetRelativePath(root, f))) continue;
            string text;
            try { text = File.ReadAllText(f); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var lib in All)
            {
                foreach (var marker in lib.PackMarkers)
                    if (text.Contains(marker, StringComparison.OrdinalIgnoreCase)) return lib.Name;
                foreach (var el in lib.ElementMarkers)
                    if (text.Contains("<" + el, StringComparison.Ordinal) || text.Contains(":" + el, StringComparison.Ordinal))
                        return lib.Name;
            }
        }
        return null;
    }

    /// <summary>這個函式庫的候選鍵（提示裡要列出來給使用者自己找）。</summary>
    public static IReadOnlyList<string> BackgroundKeysOf(string library)
        => All.FirstOrDefault(l => l.Name == library)?.BackgroundKeys ?? [];
}

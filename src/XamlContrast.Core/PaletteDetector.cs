using System.Text.RegularExpressions;

namespace XamlContrast.Core;

public enum PaletteMode { Pair, CSharp, Single, None }

/// <summary>主題字典屬於哪一個主題（色盤來自分開的深／淺 XAML 字典時才有意義）。</summary>
public enum ThemeSide { Dark, Light }

/// <summary>一個應用程式作用域：RootDir 是 App.xaml 所在目錄（"" = 共用區），
/// Palette 是「該目錄子樹 ∪ 共用區」合併出來的色盤。</summary>
public sealed record PaletteScope(string RootDir, Palette Palette);

public sealed class PaletteDetection
{
    public required Palette Palette { get; init; }
    public required PaletteMode Mode { get; init; }

    /// <summary>
    /// 應用程式層作用域（repo 裡有 ≥2 個 &lt;Application&gt; 根元素時才有值）。
    ///
    /// ⚠ 沒有它的代價（八專案抽驗實證的假警報第 2 類）：ScreenToGif 的 repo 裡
    /// 有第二個 App（Other/Translator），Playnite 有 Desktop 與 Fullscreen 兩套 ——
    /// 鍵名相同、值不同，單一全域色盤會把 A App 的字配上 B App 的底
    /// （Desktop 的 TextBrushDark 疊到 Fullscreen 的 ControlBackground 值）。
    /// App.xaml 就是現成的作用域邊界：各 App 用「自己子樹 ∪ 共用區」的色盤，
    /// 共用區（不屬於任何 App 目錄的檔案，通常是函式庫本體）只用共用區自己的。
    /// 單 App 的 repo 完全走原路徑，行為不變。完整的詞法作用域（元素層
    /// Resources、合併順序）仍不做 —— 這裡只切「不同應用程式」這一刀。
    /// </summary>
    public List<PaletteScope>? Scopes { get; init; }

    /// <summary>檔案該用哪個色盤：最深的包含它的 App 目錄；都不包含 → 共用區；
    /// 沒有作用域資訊 → 全域色盤。</summary>
    public Palette PaletteFor(string fullPath)
    {
        if (Scopes is null) return Palette;
        fullPath = Path.GetFullPath(fullPath); // 分隔線正規化(見 ApplicationScopeRoots 的教訓)
        PaletteScope? best = null;
        foreach (var s in Scopes)
            if (s.RootDir.Length > 0 &&
                fullPath.StartsWith(s.RootDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                (best is null || s.RootDir.Length > best.RootDir.Length))
                best = s;
        best ??= Scopes.FirstOrDefault(s => s.RootDir.Length == 0);
        // ⚠ 不屬於任何 App、共用區又沒有自己的字典 → 空色盤，不是退回全域色盤。全域色盤是各 App 合併
        //   （同鍵先出現的贏）—— 退回它等於借用某個 App 的值，而共用元件實際的值由執行期的宿主決定，
        //   那是在猜；也跟「有共用字典時 App 的鍵看不到」不一致。看不到的鍵計入 unknown-palette-key
        return best?.Palette ?? EmptyPalette;
    }
    private static readonly Palette EmptyPalette = new();

    /// <summary>合併式色盤因為定義讀不懂（或那個主題找不到引用的 Color）而剔除的鍵 —— 使用處是
    /// unknown-palette-key，但原因不是打字錯誤或死引用，報告點名時要分開講。</summary>
    public IReadOnlySet<string> DroppedKeys { get; init; } = new HashSet<string>();

    /// <summary>
    /// 只屬於一個主題的字典檔（完整路徑 → 深或淺）：色盤來自分開的深／淺 XAML 字典（合併式 pair、主題配對、
    /// config 強制 pair）時，Dark.xaml 裡的 Style 只在深色主題下存在。
    /// ⚠ 拿 Dark.xaml 的 Style 配淺色色票算出的那一欄是執行期不存在的組合 —— Dark.xaml 與 Light.xaml 各定義
    ///   一個 Chip 時，舊版各報一筆編造的 fail（light=1.16、dark=1.1）。C#／單一主題模式沒有這張表：
    ///   那裡的 XAML 主題檔是兩個主題共用的（Kindling 的 DarkTheme.xaml 是 C# 色盤的靜態複本）。
    /// </summary>
    public IReadOnlyDictionary<string, ThemeSide> ThemeFileSides { get; init; } =
        new Dictionary<string, ThemeSide>(StringComparer.OrdinalIgnoreCase);

    /// <summary>檔案只屬於哪一個主題（比對前 GetFullPath）；兩個主題共用回傳 null。</summary>
    public ThemeSide? SideOf(string file)
        => ThemeFileSides.Count > 0 && ThemeFileSides.TryGetValue(Path.GetFullPath(file), out var s) ? s : null;

    /// <summary>被認定為色盤檔、且根元素是 ResourceDictionary 的 XAML：不算進 summary.files（字典檔不是畫面），
    /// 但照樣走樹與 WalkStyles（裡面的模板與 Style 一樣要稽核）。
    /// 色盤來源是畫面檔（Window.Resources 裡的區域 brush）時照樣稽核，見 <see cref="PaletteDetector.IsResourceDictionaryFile"/>。</summary>
    public required HashSet<string> ExcludedFiles { get; init; }
    /// <summary>偵測結果的人話描述，一律印出來（偵測結果不靜默）。</summary>
    public required string Description { get; init; }
    /// <summary>偵測時讀不到的檔（"相對路徑: 訊息"）。稽核端併進 parse errors —— 檔案不能從報告裡靜默消失。
    /// ⚠ 自動偵測會讀樹裡每一個 .cs 找 C# 色盤；舊版 File.ReadLines 沒有 try，任何一個被 IDE／建置
    ///   鎖住的 .cs（跟色盤有沒有關係都一樣）都讓整支程式以未處理的 IOException 結束、退出碼跳出契約。</summary>
    public List<string> ReadErrors { get; } = new();
    /// <summary>自動偵測找不到色盤、退回只算寫死色碼 —— 退化要喊（規劃書 8.1 規則 3）。
    /// config 明選 mode "none" 不算退化：那是使用者的決定，不是工具的失敗。</summary>
    public bool IsDegraded { get; init; }
}

/// <summary>
/// M1 色盤自動偵測。
///
/// 2026-07-30 用四個真實專案探測出來的四種形狀，決定了偵測順序：
///   QuillNest：Dark+Light 兩檔配對（鍵集重疊）           → 配對模式
///   Kindling ：單一 Dark XAML（只有深色值）＋ C# 三元組   → C# 勝出
///   CelFlow  ：單一 Dark XAML，無配對無 C#               → 單一主題模式
///   Cornea   ：什麼都沒有                                → 退回只算寫死色碼，喊出來
///
/// ⚠ Kindling 那格是關鍵教訓：它的 Themes\DarkTheme.xaml 有 33 個 brush 但只有深色值
///   （真相源是 C# 陣列，執行期換值）。天真地選 XAML 檔，淺色值就全是錯的。
///   規則：**提供兩套主題的來源，優先於只有一套的。**
/// </summary>
public static partial class PaletteDetector
{
    // ── 色彩定義的解析 ──
    // ⚠ 舊版三條正則假設「屬性順序固定、且寫在同一行」，兩個假設在外部專案上都不成立：
    //   HandyControl 寫 <SolidColorBrush o:Freeze="True" x:Key="X" Color="..."/>（x:Key 不在第一個），
    //   MahApps 的 <Color x:Key="..."> 與色值分屬不同行。兩者都會整份漏掉。
    //   改成「先抓元素、再從屬性字串裡取值」，順序與換行都不影響。
    // 內容除了 #hex 也可能是具名色（White／Black…）—— HandyControl 的深色檔就這樣寫
    [GeneratedRegex("""<Color\b(?<attrs>[^>]*)>\s*(?<v>#?\w+)\s*</Color>""")]
    private static partial Regex ColorElement();

    [GeneratedRegex("""<SolidColorBrush\b(?<attrs>[^>]*?)/?>""")]
    private static partial Regex BrushElement();

    [GeneratedRegex("""\bx:Key\s*=\s*"(?<k>[^"]+)"\s*""")]
    private static partial Regex KeyAttr();

    [GeneratedRegex("""\bColor\s*=\s*"(?<v>[^"]+)"\s*""")]
    private static partial Regex ColorAttr();

    /// <summary>色票值引用另一個 Color 資源。⚠ 舊版只認 StaticResource ——
    /// HandyControl 全用 DynamicResource 跨檔引用，色盤偵測因此完全失敗。</summary>
    [GeneratedRegex("""^\{(?:Static|Dynamic)Resource\s+(?<c>[\w.]+)\}$""")]
    private static partial Regex ResourceRefValue();

    /// <summary>C# 真相源：("Key", "#深色", "#淺色") 三元組。
    /// ⚠「第一個是深、第二個是淺」是假設（Kindling 如此）；之後改由 config 指定。</summary>
    [GeneratedRegex("""\("(?<k>\w+)"\s*,\s*"(?<d>#[0-9A-Fa-f]{6,8})"\s*,\s*"(?<l>#[0-9A-Fa-f]{6,8})"\)""")]
    private static partial Regex CsTuple();

    internal sealed record XamlCandidate(
        string File, string Rel, Dictionary<string, string> Brushes, bool HintDark, bool HintLight);

    internal sealed record CsCandidate(string File, string Rel, Dictionary<string, (string Dark, string Light)> Pal);

    /// <summary>
    /// 色票值只有 #RRGGBB 與 #AARRGGBB 兩種長度有意義（含 '#' 是 7 或 9）。
    /// ⚠ 上面幾條正則寫的是 {6,8}，七位數的打字錯誤（#FF0000A）也會被吃下來，
    /// 而 <see cref="Wcag.Luminance"/> 只讀前六位 —— 等於收下一個畸形色碼、
    /// 默默猜它的前半段，然後回報一個看起來很篤定的對比值。
    /// 與「看不懂就要喊」的原則相反，所以長度不對就不收進色盤；
    /// 用到該鍵的地方自然變成 UnknownKey，走既有的 unresolved 計數喊出來。
    /// </summary>
    /// ⚠ 也要全是十六進位數字：XAML 那兩條正則吃的是 \w，#GGGGGG 長度對、字元不對，
    ///   收進色盤之後 Wcag.Luminance 的 Convert.ToInt32 直接丟例外 —— 整支稽核崩潰、退出碼跳出契約。
    private static bool IsValidHex(string v) => v.Length is 7 or 9 && v[0] == '#' && !v.AsSpan(1).ContainsAnyExcept(HexDigits);

    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>「有定義、但值讀不懂」的標記 —— 只在合併式色盤內部流通，見 <see cref="MergedXamlPalette"/>。</summary>
    private const string InvalidValue = "!invalid";

    /// <summary>字面色值（#hex 或具名色）→ 正規化色碼（#RRGGBB／#AARRGGBB）；讀不懂回傳 null。
    /// WPF 也吃 #RGB／#ARGB 與完整的具名色表 —— 跟 ColorResolver 同一套（色票與使用處的標準要一致）。</summary>
    private static string? LiteralColor(string v)
        => v.StartsWith('#') ? ColorResolver.NormalizeHex(v) : ColorResolver.NamedHex(v);

    /// <summary>
    /// 專案內的檔案，排除建置輸出（bin／obj）。
    /// ⚠ 只看 root 以下的相對路徑、整段比對、不分大小寫。舊版比對的是含 root 的完整路徑：專案本身放在
    ///   叫 bin 或 obj 的目錄底下（root 用絕對路徑）就整個專案 0 個檔 → 「resolved 0 pairs」exit 1，
    ///   訊息誤導；改傳相對路徑又正常 —— 跟「dark／light 提示只看專案內相對路徑」同一類地雷。
    ///   大小寫敏感則讓 Windows 上的 Bin\ 照掃。
    /// </summary>
    internal static IEnumerable<string> EnumerateFiles(string root, string pattern)
        => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(Path.GetRelativePath(root, f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    private static bool IsBuildOutput(string rel)
    {
        var segments = rel.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        // 最後一段是檔名，只看目錄段
        return segments.Take(segments.Length - 1)
            .Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase) || s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 根元素是 ResourceDictionary 的檔，才是可以排除出 UI 掃描的「色盤／資源字典」。
    ///
    /// ⚠ 「當色盤來源」與「排除出 UI 掃描」是兩件事。舊版綁在一起：合併式色盤收了任何含
    ///   keyed brush／Color 的 XAML，於是在 Window.Resources 放一個區域 brush 的視窗、在
    ///   Application.Resources 放色票的 App.xaml，整檔從稽核消失 —— files 少算、沒有任何提示，
    ///   裡面真實的不合格就這樣不見了。區域 brush 照樣當色盤來源（畫面上引用得到它），
    ///   但只有字典檔才不是畫面。讀不出根元素的壞檔不排除，交給稽核端喊 parse failed。
    /// </summary>
    internal static bool IsResourceDictionaryFile(string file)
    {
        try
        {
            using var r = System.Xml.XmlReader.Create(file);
            r.MoveToContent();
            return r.LocalName == "ResourceDictionary";
        }
        catch { return false; }
    }

    /// <summary>
    /// 主題字典的歸屬（色盤來自分開的深／淺字典時才建）：專案裡每個帶主題提示的字典檔（與合併式色盤同一套
    /// <see cref="ThemeHint"/>），加上明確指定的深／淺兩檔（主題配對、config 強制 pair；指定的優先）。
    /// 只收根元素是 ResourceDictionary 的檔（畫面檔不是「某個主題的字典」），測試素材不算。
    /// ⚠ 舊版只看「貢獻了色票的檔」：Style 放在不含色票的主題檔（Themes/Dark/Styles.xaml 搭配
    ///   Themes/Dark/Colors.xaml）時被當成兩個主題共用，Dark 的 Chip 配淺色底照樣編出 1.16:1（審查素材 b4split）。
    /// </summary>
    private static Dictionary<string, ThemeSide> ThemeSides(string root, string? darkFile = null, string? lightFile = null)
    {
        var sides = new Dictionary<string, ThemeSide>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in EnumerateFiles(root, "*.xaml"))
        {
            var rel = Path.GetRelativePath(root, f);
            if (LooksLikeFixture(rel)) continue;
            if (ThemeHint(rel) is { } side && IsResourceDictionaryFile(f)) sides[Path.GetFullPath(f)] = side;
        }
        if (darkFile is not null && IsResourceDictionaryFile(darkFile)) sides[Path.GetFullPath(darkFile)] = ThemeSide.Dark;
        if (lightFile is not null && IsResourceDictionaryFile(lightFile)) sides[Path.GetFullPath(lightFile)] = ThemeSide.Light;
        return sides;
    }

    /// <summary>
    /// 檔名提示：這個檔只屬於深色或淺色主題嗎？看「檔名（不含副檔名）」與「最後一段目錄」，切成詞
    /// （與 <see cref="LooksLikeFixture"/> 同一套詞邊界：. - _ 與大小寫轉換），有一個詞恰好是 dark 或 light。
    /// 兩種都有、或都沒有 → null（不確定歸屬 = 兩個主題共用，兩欄都評）。
    /// ⚠ 舊版對整條相對路徑做子字串比對：Highlight、Lightweight、Spotlight 都命中 light，資料夾叫 Starlight/
    ///   的話整個專案每個檔都帶 light 提示 —— 中性檔被當成只屬於淺色主題、只評淺色那欄，深色主題下的真 fail
    ///   消失（審查素材 b4hl、b4dir）。Dark.xaml、DarkTheme.xaml、ColorsDark.xaml、Theme.Dark.xaml、
    ///   Themes/Dark/Colors.xaml 照樣認得；全小寫黏在一起的 darktheme.xaml 認不得，退回中性（兩欄都評，不猜）。
    /// </summary>
    internal static ThemeSide? ThemeHint(string rel)
    {
        var segments = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        bool dark = false, light = false;
        void Scan(string segment)
        {
            foreach (var w in WordBoundary().Split(segment))
            {
                if (w.Equals("dark", StringComparison.OrdinalIgnoreCase)) dark = true;
                else if (w.Equals("light", StringComparison.OrdinalIgnoreCase)) light = true;
            }
        }
        Scan(Path.GetFileNameWithoutExtension(segments[^1]));
        if (segments.Length >= 2) Scan(segments[^2]);
        return dark == light ? null : dark ? ThemeSide.Dark : ThemeSide.Light;
    }


    /// <summary>畫面檔：根元素讀得出來、而且不是 ResourceDictionary 或 Application（App.xaml 的
    /// Application.Resources 是整個 App 的範圍，照全域處理）。它的區域資源只在那個畫面裡生效。</summary>
    internal static bool IsScreenFile(string file)
    {
        try
        {
            using var r = System.Xml.XmlReader.Create(file);
            r.MoveToContent();
            return r.LocalName is not ("ResourceDictionary" or "Application");
        }
        catch { return false; }
    }

    /// <summary>
    /// 畫面檔自己的區域 brush 在那個畫面裡蓋過全域色盤（WPF 的資源查找先找近的）。
    /// 回傳 鍵 → 字面色（深淺同值）或 null（讀不懂 = 執行期才知道，例如 Color="{Binding …}"）。
    /// 引用 Color 鍵的區域 brush 不列（它解出的顏色跟著主題走，沿用全域那一層的解析）。
    /// </summary>
    internal static Dictionary<string, string?> LocalOverrides(string file)
    {
        var overrides = new Dictionary<string, string?>();
        if (!IsScreenFile(file)) return overrides;
        var (lit, _) = ParseBrushDefs(file, keepInvalid: true);
        foreach (var (k, v) in lit) overrides[k] = v is InvalidValue ? null : v;
        return overrides;
    }

    /// <summary>色盤來源檔 → 不算進 files 的「字典檔」（只留根元素是 ResourceDictionary 的；照樣稽核）。</summary>
    private static HashSet<string> UiExclusions(IEnumerable<string> paletteFiles)
        => new(paletteFiles.Where(IsResourceDictionaryFile), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 色盤檔是用正則掃原始文字的 —— 先把 XML 註解拿掉。
    /// ⚠ 舊版直接掃：註解掉的舊定義（&lt;!-- 舊值：&lt;SolidColorBrush x:Key="Fg" Color="#333333"/&gt; --&gt;）
    ///   也被收進色盤，而且同鍵後者覆蓋前者 —— 報告用一個已經失效的值算出篤定的比值，
    ///   舊值比較亮時就藏掉真實的 fail；被註解掉的 brush 也會讓畫面檔看起來像色盤來源。
    /// </summary>
    private static string ReadXamlWithoutComments(string file)
        => XmlComment().Replace(File.ReadAllText(file), " ");

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    /// <summary>C# 三元組同一個根因：整行被 // 註解掉的舊列不是色盤。</summary>
    private static bool IsCSharpCommentLine(string line) => line.AsSpan().TrimStart().StartsWith("//");

    private static string? Attr(Regex re, string attrs)
    {
        var m = re.Match(attrs);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>單一檔案裡的 &lt;Color x:Key&gt; 定義（供跨檔解析 brush 引用）。
    /// <paramref name="keepInvalid"/>：讀不懂的值記成 <see cref="InvalidValue"/> 而不是略過（合併式色盤用）。</summary>
    internal static Dictionary<string, string> ParseColorDefs(string file, bool keepInvalid = false)
    {
        var colors = new Dictionary<string, string>();
        string text;
        try { text = ReadXamlWithoutComments(file); } catch { return colors; }
        foreach (Match m in ColorElement().Matches(text))
        {
            var key = Attr(KeyAttr(), m.Groups["attrs"].Value);
            if (key is null) continue;
            if (LiteralColor(m.Groups["v"].Value) is { } hex) colors[key] = hex;
            else if (keepInvalid) colors[key] = InvalidValue;
        }
        return colors;
    }

    /// <summary>
    /// 解析單一 XAML 檔的 brush 定義。
    ///
    /// <paramref name="globalColors"/> 是「全專案的 &lt;Color&gt; 定義」——
    /// ⚠ 舊版只在**同一個檔案內**解析 brush 對 Color 的引用，但把 Color 與 Brush
    /// 分成兩個字典是 WPF 生態的常見組織方式（HandyControl：Theme.xaml 放 Brush、
    /// 引用另一個檔的 Color，結果 8 個色彩定義檔一個都認不出來，色盤偵測完全失敗）。
    /// 同檔優先於全域，模擬 WPF 的資源查找順序。
    /// </summary>
    internal static Dictionary<string, string> ParseXamlPalette(
        string file, IReadOnlyDictionary<string, string>? globalColors = null)
    {
        var colors = ParseColorDefs(file);
        var (lit, refs) = ParseBrushDefs(file); // 讀不懂的值不收：這條路沒有「退回另一側」，鍵缺了就是 UnknownKey
        var brushes = new Dictionary<string, string>(lit);

        // brush 引用 Color 定義 → 解成實際色值。同檔優先，其次全專案。
        foreach (var (k, c) in refs)
        {
            if (ResolveRef(c, key => colors.GetValueOrDefault(key) ?? globalColors?.GetValueOrDefault(key)) is { } v and not InvalidValue)
                brushes[k] = v;
        }
        // 純 <Color> 字典（整份沒有任何 SolidColorBrush）才把 Color 鍵當色票。
        // ⚠ 不能無條件加：Color 鍵通常是 brush 的中介值（BgColor → Bg），
        //   一起灌進色盤會讓鍵數虛胖，還會影響「挑哪個檔當色盤」的判斷。
        if (brushes.Count == 0)
            foreach (var (k, v) in colors) brushes[k] = v;
        return brushes;
    }

    /// <summary>
    /// 測試素材／範例不是專案的色盤。⚠ ILSpy 實證：偵測器挑中的「色盤」是
    /// <c>ILSpy.BamlDecompiler.Tests.Windows\Cases\…</c> —— 一個反編譯測試的假資料檔。
    /// 只影響「能不能當色盤候選」（XAML 與 C# 兩條路都套用），不影響這些檔案本身要不要被稽核。
    ///
    /// ⚠ 判準是「路徑段切成詞、逐詞比前綴」，不是「路徑段以 test 開頭」：.NET 的測試專案慣用
    ///   <c>MyApp.Tests</c>／<c>UnitTests</c> 命名，舊判準對不上（那一段以 myapp／unit 開頭），
    ///   放在那裡的假色盤照樣被選中。詞的邊界是 . - _ 與大小寫轉換（UnitTests → Unit + Tests），
    ///   所以 Contest、LatestNews 這種「詞中間剛好有 test」的名字不受影響。
    /// </summary>
    internal static bool LooksLikeFixture(string rel)
    {
        var segments = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            foreach (var w in WordBoundary().Split(segments[i]))
            {
                var word = w.ToLowerInvariant();
                if (word.StartsWith("test") || word.StartsWith("sample") ||
                    word.StartsWith("demo") || word.StartsWith("example")) return true;
                // fixture／mock 只看第二層以下（沿用舊判準）
                if (i > 0 && (word.StartsWith("fixture") || word.StartsWith("mock"))) return true;
            }
        }
        return false;
    }

    [GeneratedRegex(@"[._\-\s]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBoundary();

    /// <summary>全專案的 &lt;Color x:Key&gt; 索引 —— 讓 brush 能引用別的檔案裡的 Color。</summary>
    internal static Dictionary<string, string> GlobalColors(string root)
    {
        var all = new Dictionary<string, string>();
        foreach (var f in EnumerateFiles(root, "*.xaml"))
        {
            if (LooksLikeFixture(Path.GetRelativePath(root, f))) continue;
            foreach (var (k, v) in ParseColorDefs(f)) all.TryAdd(k, v);
        }
        return all;
    }

    internal static List<XamlCandidate> XamlCandidates(string root)
    {
        var cands = new List<XamlCandidate>();
        var globalColors = GlobalColors(root);
        foreach (var f in EnumerateFiles(root, "*.xaml"))
        {
            var relPath = Path.GetRelativePath(root, f);
            if (LooksLikeFixture(relPath)) continue;
            var brushes = ParseXamlPalette(f, globalColors);
            if (brushes.Count < 3) continue;

            // ⚠ 提示只看「專案內的相對路徑」。看完整路徑的話，專案放在
            //   D:\dark-projects\ 底下會讓所有候選都染上 dark 提示、配對整個失效。
            var rel = Path.GetRelativePath(root, f);
            var hint = ThemeHint(rel);
            cands.Add(new XamlCandidate(f, rel, brushes, hint == ThemeSide.Dark, hint == ThemeSide.Light));
        }
        return cands;
    }

    /// <summary>
    /// 合併式色盤：把整個專案的色彩字典當成一份色盤，而不是挑其中一個檔。
    ///
    /// ⚠ 「挑一個檔」是四個受測專案養出來的啟發法（它們的色票剛好集中在單一檔案），
    /// 在外部專案上普遍失效 —— 八個公開專案實測：
    ///   HandyControl  色彩定義分散在 8 個檔，brush 名稱與主題無關、顏色值分主題，
    ///                 兩者還在不同檔案（Themes/Theme.xaml 的 brush 引用
    ///                 Colors/ColorsDark.xaml 的 Color）→ 舊版完全找不到色盤
    ///   MaterialDesign 154 個檔含色彩定義，舊版只抓到 1 個（34 鍵）
    ///   MahApps       挑中建置期樣板檔 Theme.Template.xaml（值是 {{佔位符}}）
    ///
    /// 模型（貼近 WPF 的合併字典語意）：
    ///   1. 色彩字典依檔名提示分成 dark／light／中性三組
    ///   2. darkColors = 中性 ∪ dark（dark 覆蓋）；lightColors = 中性 ∪ light
    ///      只有 dark 提示而無 light 時，中性即淺色 —— HandyControl 的形狀
    ///      （預設檔 Colors.xaml 不帶 light 字樣，只有深色變體被標記）
    ///   3. brush 字典**同樣分三組**：字面值 brush 住在 dark 檔就只提供深色值、
    ///      住在 light 檔就只提供淺色值、中性檔兩邊都給；引用型 brush 對兩張
    ///      Color 表各解一次。最後同鍵跨組合併、缺的一側退回另一側。
    ///      ⚠ 第 3 點是修過的：第一版把字面值 brush 一律當「深淺同值」收，
    ///      Dark.xaml 字典序排在 Light.xaml 前 → 深色值佔據兩欄、淺色檔整份被忽略。
    ///      ScreenToGif 實測 456 筆 findings 全部 dark==light —— 淺色欄整欄是編造的。
    ///      QuillNest 抓不到這個回歸：它的配對走 Color 引用（第 2 點的路），
    ///      字面值配對（ScreenToGif 的形狀）在四個受測專案裡一個都沒有。
    /// 鍵衝突一律「先出現的贏」，檔案順序固定（字典序），結果可重現。
    /// </summary>
    /// <summary>找出所有 &lt;Application&gt; 根元素的檔案所在目錄（App.xaml = 作用域邊界）。</summary>
    internal static List<string> ApplicationScopeRoots(string root)
    {
        var dirs = new List<string>();
        foreach (var f in EnumerateFiles(root, "*.xaml"))
        {
            if (LooksLikeFixture(Path.GetRelativePath(root, f))) continue;
            try
            {
                using var r = System.Xml.XmlReader.Create(f);
                r.MoveToContent();
                // ⚠ 一律 GetFullPath 正規化:GetDirectoryName 會把分隔線轉成反斜線,
                //   但 EnumerateFiles 的結果沿用呼叫端傳入的 root 寫法(可能是正斜線)。
                //   混用的話 StartsWith 永遠不成立 → 每個檔都被當共用區 → 作用域
                //   **靜默**退化成全域色盤 —— 探針實測抓到的,正是本專案最忌諱的形狀。
                if (r.LocalName == "Application") dirs.Add(Path.GetDirectoryName(Path.GetFullPath(f))!);
            }
            catch { /* 解析失敗的檔案由稽核端負責喊,這裡跳過 */ }
        }
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static (Palette Pal, bool IsPair, HashSet<string> Files, SortedSet<string> Conflicts, SortedSet<string> Invalid)? MergedXamlPalette(
        string root, Func<string, bool>? include = null, int minKeys = 3)
    {
        // ⚠「缺的一側退回另一側」只適用於**沒有定義**的那一側。有定義、但值讀不懂（七位數打字錯誤、
        //   Color="{Binding …}"、#GGGGGG）的一側舊版也一樣被丟掉，看起來像「沒定義」，於是拿另一個主題的值
        //   補上 —— 比值是編出來的（demo 的 Fg 深色值打成 #EEEEEEE：深色欄拿到淺色的 #1B1B1B、報 1.09:1；
        //   unresolved 0，跟文件承諾的「長度不對就走 UnknownKey 喊出來」相反）。
        //   現在讀不懂的值以 InvalidValue 佔住那一側，整個鍵剔除並在偵測描述點名，使用處走 unknown-palette-key。
        var invalid = new SortedSet<string>(StringComparer.Ordinal);
        var colorFiles = new List<(string File, Dictionary<string, string> Colors, bool D, bool L)>();
        var brushFiles = new List<(string File, Dictionary<string, string> Lit, Dictionary<string, string> Ref, bool D, bool L)>();
        // 畫面檔（Window／UserControl…）的區域定義另成最低一層，見下面「區域層」
        var localColorFiles = new List<(string File, Dictionary<string, string> Colors)>();
        var localBrushFiles = new List<(string File, Dictionary<string, string> Lit, Dictionary<string, string> Ref)>();

        foreach (var f in EnumerateFiles(root, "*.xaml"))
        {
            var rel = Path.GetRelativePath(root, f);
            if (LooksLikeFixture(rel)) continue;
            if (include is not null && !include(f)) continue;
            var hint = ThemeHint(rel); // 整詞比對，見 ThemeHint
            var isDark = hint == ThemeSide.Dark;
            var isLight = hint == ThemeSide.Light;

            var colors = ParseColorDefs(f, keepInvalid: true);
            var (lit, refs) = ParseBrushDefs(f, keepInvalid: true);
            if (IsScreenFile(f))
            {
                if (colors.Count > 0) localColorFiles.Add((f, colors));
                if (lit.Count + refs.Count > 0) localBrushFiles.Add((f, lit, refs));
                continue;
            }
            if (colors.Count > 0) colorFiles.Add((f, colors, isDark, isLight));
            if (lit.Count + refs.Count > 0) brushFiles.Add((f, lit, refs, isDark, isLight));
        }
        if (colorFiles.Count + brushFiles.Count + localColorFiles.Count + localBrushFiles.Count == 0) return null;
        IEnumerable<string> SourceFiles() => colorFiles.Select(c => c.File).Concat(brushFiles.Select(b => b.File))
            .Concat(localColorFiles.Select(c => c.File)).Concat(localBrushFiles.Select(b => b.File));

        // ── Color 表：主題檔先進（各佔自己那側），中性檔補洞、兩側都給 ──
        var darkColors = new Dictionary<string, string>();
        var lightColors = new Dictionary<string, string>();
        var colorConflicts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var c in colorFiles.Where(c => c.D && !c.L))
            foreach (var (k, v) in c.Colors) Put(darkColors, k, v, colorConflicts);
        foreach (var c in colorFiles.Where(c => c.L && !c.D))
            foreach (var (k, v) in c.Colors) Put(lightColors, k, v, colorConflicts);
        foreach (var c in colorFiles.Where(c => !(c.D ^ c.L)))
            foreach (var (k, v) in c.Colors) { Put(darkColors, k, v, colorConflicts); Put(lightColors, k, v, colorConflicts); }

        // ── Brush 表：同樣依主題分側，且**主題檔先進、中性檔只補洞** ──
        // ⚠ 第一版照檔案順序 TryAdd（Color 表是三段式、brush 表卻忘了）。
        //   ScreenToGif 實證：repo 裡有第二個 App（Other/Translator，自帶淺色的
        //   Panel.Background、檔名中性），Other/ 字典序排在 ScreenToGif/ 前 →
        //   中性檔搶走主題檔的值，深色欄拿到白底，報出 1.23:1 的假 fail
        //   （實際 ~13:1 合格）。這同時是規劃書 4.2「資源作用域」簡化的實例 ——
        //   同鍵衝突要明講，見下面的 conflicts。
        var darkB = new Dictionary<string, string>();
        var lightB = new Dictionary<string, string>();
        var darkR = new Dictionary<string, string>();
        var lightR = new Dictionary<string, string>();
        var conflicts = new SortedSet<string>(StringComparer.Ordinal);

        static void Put(Dictionary<string, string> map, string k, string v, SortedSet<string> conflicts)
        {
            if (map.TryGetValue(k, out var existing)) { if (existing != v) conflicts.Add(k); }
            else map[k] = v;
        }
        foreach (var b in brushFiles.Where(b => b.D && !b.L))
        {
            foreach (var (k, v) in b.Lit) Put(darkB, k, v, conflicts);
            foreach (var (k, c) in b.Ref) Put(darkR, k, c, conflicts);
        }
        foreach (var b in brushFiles.Where(b => b.L && !b.D))
        {
            foreach (var (k, v) in b.Lit) Put(lightB, k, v, conflicts);
            foreach (var (k, c) in b.Ref) Put(lightR, k, c, conflicts);
        }
        foreach (var b in brushFiles.Where(b => !(b.D ^ b.L)))
        {
            foreach (var (k, v) in b.Lit) { Put(darkB, k, v, conflicts); Put(lightB, k, v, conflicts); }
            foreach (var (k, c) in b.Ref) { Put(darkR, k, c, conflicts); Put(lightR, k, c, conflicts); }
        }

        // ── 區域層：畫面檔的 Window.Resources／UserControl.Resources 只補「字典與 App 都沒定義的鍵」──
        // ⚠ 區域定義只在那個畫面裡生效（那個畫面自己的覆蓋由稽核端的 LocalOverrides 處理）。舊版把它當成
        //   中性檔灌進全域：區域的字面值蓋掉主題用 Color 引用定義的同名鍵（字面值優先），讀不懂的區域值
        //   （Color="{Binding …}"）更把整個專案的同名鍵剔除 —— 一個畫面的區域 brush 污染了全部畫面。
        //   讀不懂的區域值不進全域；同鍵的區域定義之間不算衝突（各管各的畫面），先出現的補洞
        bool DefinedGlobally(string k) => darkB.ContainsKey(k) || lightB.ContainsKey(k) || darkR.ContainsKey(k) || lightR.ContainsKey(k);
        foreach (var c in localColorFiles)
            foreach (var (k, v) in c.Colors)
                if (v is not InvalidValue && !darkColors.ContainsKey(k) && !lightColors.ContainsKey(k))
                { darkColors[k] = v; lightColors[k] = v; }
        foreach (var b in localBrushFiles)
        {
            foreach (var (k, v) in b.Lit)
                if (v is not InvalidValue && !DefinedGlobally(k)) { darkB[k] = v; lightB[k] = v; }
            foreach (var (k, c) in b.Ref)
                if (!DefinedGlobally(k)) { darkR[k] = c; lightR[k] = c; }
        }

        var pal = new Palette();
        foreach (var k in darkB.Keys.Concat(lightB.Keys).Concat(darkR.Keys).Concat(lightR.Keys).Distinct())
        {
            // 每側：字面值優先，其次引用解析（引用對自己那側的 Color 表解；那一側找不到引用的 Color = 解不出顏色，
            // 跟讀不懂同一個處理 —— 不能拿另一個主題的值補）
            var d = darkB.GetValueOrDefault(k)
                    ?? (darkR.TryGetValue(k, out var dc) ? (ResolveRef(dc, darkColors.GetValueOrDefault) ?? InvalidValue) : null);
            var l = lightB.GetValueOrDefault(k)
                    ?? (lightR.TryGetValue(k, out var lc) ? (ResolveRef(lc, lightColors.GetValueOrDefault) ?? InvalidValue) : null);
            if (d is InvalidValue || l is InvalidValue) { invalid.Add(k); continue; } // 讀不懂 ≠ 沒定義：不補
            if (d is null && l is null) continue;
            pal.Entries.TryAdd(k, (d ?? l!, l ?? d!)); // 缺的一側（沒有定義）退回另一側
        }
        // 沒有任何 brush 字典 → 直接把 Color 鍵當色票（有些專案就這樣用）
        // （brush 有定義、只是全部讀不懂時不退回 Color 表：那些 brush 才是色盤，讀不懂要照實喊出來）
        if (pal.Count == 0 && invalid.Count == 0)
        {
            foreach (var (k, v) in darkColors)
            {
                var lv = lightColors.GetValueOrDefault(k, v);
                if (v is InvalidValue || lv is InvalidValue) { invalid.Add(k); continue; }
                pal.Entries.TryAdd(k, (v, lv));
            }
        }

        // ≥ 3 是「偵測到色盤」的門檻；作用域用 1（小字典也是它自己的）。
        // ⚠ 門檻算「定義了的鍵」：讀不懂而剔除的鍵也算一個。只算剩下的鍵的話，剔除一個就可能掉到門檻以下、
        //   放棄合併式色盤，落到「單一主題檔」那條路 —— 整欄深色值改用淺色檔的值，比原本的編造更大，而且不算退化。
        //   一個鍵都沒剩（全部讀不懂）才放棄：剩下的路只會喊「找不到色盤」，不會編
        if (pal.Count == 0 && invalid.Count >= minKeys)
            // 定義了夠多的鍵、卻一個都讀不懂：回傳空色盤＋點名，讓 Detect 判成退化，而不是交給「挑一個檔」去編
            return (pal, false, new HashSet<string>(SourceFiles(), StringComparer.OrdinalIgnoreCase),
                    new SortedSet<string>(StringComparer.Ordinal), invalid);
        if (pal.Count == 0 || pal.Count + invalid.Count < minKeys) return null;
        var anyDark = colorFiles.Any(c => c.D && !c.L) || brushFiles.Any(b => b.D && !b.L);
        var anyLight = colorFiles.Any(c => c.L && !c.D) || brushFiles.Any(b => b.L && !b.D);
        var files = new HashSet<string>(
            SourceFiles(),
            StringComparer.OrdinalIgnoreCase);
        conflicts.UnionWith(colorConflicts);
        return (pal, anyDark && anyLight || !pal.IsSingleTheme, files, conflicts, invalid);
    }

    /// <summary>同鍵不同值的衝突要明講（規劃書 4.2：「不可默默取其一」）——
    /// 這通常代表 repo 裡有多個 App／子專案各自的色盤（ScreenToGif 的 Translator 形狀），
    /// 共用鍵名的配對可能配到錯的那套值。</summary>
    private static string ConflictNote(SortedSet<string> conflicts)
        => conflicts.Count == 0 ? "" :
           $"; !! {conflicts.Count} key(s) defined with conflicting values (resource scoping not modelled" +
           $" — first themed definition wins): {string.Join(", ", conflicts.Take(5))}{(conflicts.Count > 5 ? ", …" : "")}";

    /// <summary>單一檔案的 brush 定義，分成「字面色值」與「引用其他 Color 鍵」兩類。
    /// <paramref name="keepInvalid"/>：有 x:Key 但色值讀不懂（七位數、Binding、Color 寫成屬性元素…）
    /// 的 brush 記成 <see cref="InvalidValue"/> 而不是略過（合併式色盤用）。</summary>
    internal static (Dictionary<string, string> Literal, Dictionary<string, string> Ref) ParseBrushDefs(
        string file, bool keepInvalid = false)
    {
        var lit = new Dictionary<string, string>();
        var refs = new Dictionary<string, string>();
        string text;
        try { text = ReadXamlWithoutComments(file); } catch { return (lit, refs); }
        foreach (Match m in BrushElement().Matches(text))
        {
            var attrs = m.Groups["attrs"].Value;
            var key = Attr(KeyAttr(), attrs);
            if (key is null) continue;
            var val = Attr(ColorAttr(), attrs);
            // Opacity：整個 brush 變半透明。舊版只取 x:Key 與 Color —— Opacity="0.2" 的白字 brush
            // 被當成不透明白色評分（疊黑底實際約 1.66:1，報 21:1 ok）。讀不懂的 Opacity 等同讀不懂的色值
            var opRaw = Attr(OpacityAttr(), attrs);
            double op = 1;
            var opOk = opRaw is null ||
                       double.TryParse(opRaw, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out op) && double.IsFinite(op);
            if (!opOk) { if (keepInvalid) lit[key] = InvalidValue; continue; }
            if (val is not null && LiteralColor(val) is { } hex) lit[key] = WithOpacity(hex, op); // Color="White" 也是合法寫法
            else if (val is not null && ResourceRefValue().Match(val) is { Success: true } rm)
                refs[key] = rm.Groups["c"].Value + (op < 0.999 ? "@" + op.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "");
            else if (keepInvalid) lit[key] = InvalidValue;
        }
        return (lit, refs);
    }

    [GeneratedRegex("""\bOpacity\s*=\s*"(?<v>[^"]+)"\s*""")]
    private static partial Regex OpacityAttr();

    private static string WithOpacity(string hex, double op)
        => op >= 0.999 ? hex : ColorResolver.ApplyOpacity(hex, op) ?? InvalidValue;

    /// <summary>brush 對 Color 鍵的引用（"Key" 或帶 brush Opacity 的 "Key@0.5"）→ 實際色值。
    /// 找不到回傳 null（那一側沒有定義）；讀不懂回傳 <see cref="InvalidValue"/>。</summary>
    private static string? ResolveRef(string refVal, Func<string, string?> lookup)
    {
        var at = refVal.IndexOf('@');
        var v = lookup(at < 0 ? refVal : refVal[..at]);
        if (v is null or InvalidValue || at < 0) return v;
        return WithOpacity(v, double.Parse(refVal[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>值讀不懂而剔除的鍵要明講 —— 它們在使用處會變成 unknown-palette-key。</summary>
    private static string InvalidNote(SortedSet<string> invalid)
        => invalid.Count == 0 ? "" :
           $"; !! {invalid.Count} key(s) dropped — a theme defines them with a colour value the tool cannot read" +
           " or a Color reference it cannot find for that theme" +
           $" (uses are counted as unknown-palette-key): {string.Join(", ", invalid.Take(5))}{(invalid.Count > 5 ? ", …" : "")}";

    internal static List<CsCandidate> CsCandidates(string root, List<string>? readErrors = null)
    {
        var cands = new List<CsCandidate>();
        foreach (var f in EnumerateFiles(root, "*.cs"))
        {
            // 測試檔的 [InlineData("Bg", "#..", "#..")] 正好符合三元組格式 —— 會搶走色盤、報出 1:1 的假 fail，
            // 還讓偵測看起來沒有退化（--strict-palette 不會觸發）。與 XAML 端同一個判準
            if (LooksLikeFixture(Path.GetRelativePath(root, f))) continue;
            var map = new Dictionary<string, (string, string)>();
            try
            {
                foreach (var line in File.ReadLines(f))
                {
                    if (IsCSharpCommentLine(line)) continue;
                    var m = CsTuple().Match(line);
                    if (m.Success && IsValidHex(m.Groups["d"].Value) && IsValidHex(m.Groups["l"].Value))
                        map[m.Groups["k"].Value] =
                            (m.Groups["d"].Value.ToUpperInvariant(), m.Groups["l"].Value.ToUpperInvariant());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 讀不到（被 IDE／建置鎖住）：跳過，但要喊 —— 稽核端併進 parse errors
                readErrors?.Add($"{Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/')}: {ex.Message}");
                continue;
            }
            if (map.Count >= 3)
                cands.Add(new CsCandidate(f, Path.GetRelativePath(root, f), map));
        }
        return cands;
    }

    /// <summary>config 強制模式共用：解析 C# 檔的 (key, dark, light) 三元組。</summary>
    private static Palette ParseCsPalette(string file, Regex pattern)
    {
        var pal = new Palette();
        foreach (var line in File.ReadLines(file))
        {
            if (IsCSharpCommentLine(line)) continue;
            var m = pattern.Match(line);
            if (!m.Success) continue;
            var dark = (m.Groups["dark"].Success ? m.Groups["dark"] : m.Groups["d"]).Value;
            var light = (m.Groups["light"].Success ? m.Groups["light"] : m.Groups["l"]).Value;
            // 使用者自訂的 csharpPattern 也走同一道長度驗證 —— 畸形值一律不收
            if (!IsValidHex(dark) || !IsValidHex(light)) continue;
            pal.Entries[m.Groups["key"].Success ? m.Groups["key"].Value : m.Groups["k"].Value] =
                (dark.ToUpperInvariant(), light.ToUpperInvariant());
        }
        return pal;
    }

    private static string RequireFile(string root, string rel, string field)
    {
        string full;
        // 路徑含 NUL 之類的字元：GetFullPath 丟 ArgumentException —— 設定錯誤，不是 .NET 堆疊
        try { full = Path.GetFullPath(Path.Combine(root, rel)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ConfigException($"palette.{field} is not a valid path: {ex.Message}"); }
        if (!File.Exists(full))
            throw new ConfigException($"palette.{field} not found: {rel}");
        return full;
    }

    public static PaletteDetection Detect(string root, ToolConfig? config = null)
    {
        var readErrors = new List<string>();
        var d = DetectCore(root, config, readErrors);
        d.ReadErrors.AddRange(readErrors);
        return d;
    }

    private static PaletteDetection DetectCore(string root, ToolConfig? config, List<string> readErrors)
    {
        // ── config 強制模式：使用者說了算，檔案不存在是錯誤而不是退化 ──
        switch (config?.Palette.Mode)
        {
            case "pair":
                {
                    var dFile = RequireFile(root, config.Palette.DarkFile!, "darkFile");
                    var lFile = RequireFile(root, config.Palette.LightFile!, "lightFile");
                    var d = ParseXamlPalette(dFile);
                    var l = ParseXamlPalette(lFile);
                    var pal = new Palette();
                    foreach (var (k, v) in d)
                        if (l.TryGetValue(k, out var lv)) pal.Entries[k] = (v, lv);
                    return new PaletteDetection
                    {
                        Palette = pal,
                        Mode = PaletteMode.Pair,
                        ExcludedFiles = UiExclusions([dFile, lFile]),
                        ThemeFileSides = ThemeSides(root, dFile, lFile),
                        Description = $"config-forced theme pair: {config.Palette.DarkFile} + {config.Palette.LightFile} ({pal.Count} keys)",
                    };
                }
            case "csharp":
                {
                    var cFile = RequireFile(root, config.Palette.CsharpFile!, "csharpFile");
                    Regex pattern;
                    try
                    {
                        pattern = config.Palette.CsharpPattern is null
                            ? CsTuple()
                            : new Regex(config.Palette.CsharpPattern);
                    }
                    catch (ArgumentException ex) { throw new ConfigException($"palette.csharpPattern is not a valid regex: {ex.Message}"); }
                    Palette pal;
                    // 使用者指定的真相源讀不到 = 設定沒辦法生效 → exit 2（不是退化成別的色盤）
                    try { pal = ParseCsPalette(cFile, pattern); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { throw new ConfigException($"palette.csharpFile cannot be read: {ex.Message}"); }
                    var excl = XamlCandidates(root)
                        .Where(c => c.Brushes.Count > 0 &&
                                    (double)c.Brushes.Keys.Count(pal.Entries.ContainsKey) / c.Brushes.Count >= 0.5)
                        .Select(c => c.File);
                    return new PaletteDetection
                    {
                        Palette = pal,
                        Mode = PaletteMode.CSharp,
                        ExcludedFiles = UiExclusions(excl),
                        Description = $"config-forced C# source: {config.Palette.CsharpFile} ({pal.Count} keys)",
                    };
                }
            case "single":
                {
                    var sFile = RequireFile(root, config.Palette.DarkFile!, "darkFile");
                    var brushes = ParseXamlPalette(sFile);
                    var pal = new Palette();
                    foreach (var (k, v) in brushes) pal.Entries[k] = (v, v);
                    return new PaletteDetection
                    {
                        Palette = pal,
                        Mode = PaletteMode.Single,
                        ExcludedFiles = UiExclusions([sFile]),
                        Description = $"config-forced single-theme palette: {config.Palette.DarkFile} ({pal.Count} keys)",
                    };
                }
            case "none":
                // 明選 none = 使用者的決定，不算退化（不觸發 --strict-palette）
                return new PaletteDetection
                {
                    Palette = new Palette(),
                    Mode = PaletteMode.None,
                    ExcludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    Description = "config-forced: no palette — auditing hardcoded colors only",
                };
        }

        var xaml = XamlCandidates(root);
        var cs = CsCandidates(root, readErrors);
        var merged = MergedXamlPalette(root);

        // ── 應用程式層作用域：≥2 個 App.xaml 才啟動;單 App repo 行為完全不變 ──
        var scopeDirs = ApplicationScopeRoots(root);
        List<PaletteScope>? scopes = null;
        if (scopeDirs.Count >= 2 && merged is not null)
        {
            bool UnderAny(string f)
            {
                var full = Path.GetFullPath(f); // 分隔線正規化,理由同 ApplicationScopeRoots
                return scopeDirs.Any(d => full.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }
            scopes = new List<PaletteScope>();
            foreach (var d in scopeDirs)
            {
                // 各 App 看得到:自己子樹 ∪ 共用區(函式庫本體之類,不屬於任何 App 目錄)
                // ⚠ 「自己子樹」要扣掉更深一層的 App 目錄:巢狀 App 的字典屬於內層,舊版會漏進外層,
                //   同鍵衝突時看路徑字典序決定用誰(資料夾叫 Plugins 還是 Tools 會得到不同結論)
                bool InThisApp(string f)
                {
                    var full = Path.GetFullPath(f);
                    return full.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                           !scopeDirs.Any(o => o.Length > d.Length &&
                                               o.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                               full.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                }
                var m = MergedXamlPalette(root, f => InThisApp(f) || !UnderAny(f), minKeys: 1);
                if (m is { } sm && sm.Pal.Count > 0) scopes.Add(new PaletteScope(d, sm.Pal));
            }
            var shared = MergedXamlPalette(root, f => !UnderAny(f), minKeys: 1);
            if (shared is { } sh && sh.Pal.Count > 0) scopes.Add(new PaletteScope("", sh.Pal));
            if (scopes.Count == 0) scopes = null;
        }
        var scopeNote = scopes is null ? "" :
            $"; {scopes.Count(s => s.RootDir.Length > 0)} application scope(s) resolved separately" +
            (scopes.Any(s => s.RootDir.Length == 0) ? " + shared" : "");

        // 0) 合併式色盤且深淺兩套值俱全 —— 優先於任何「挑單一檔」的路徑
        if (merged is { IsPair: true } mp)
            return new PaletteDetection
            {
                Palette = mp.Pal,
                Mode = PaletteMode.Pair,
                ExcludedFiles = UiExclusions(mp.Files),
                ThemeFileSides = ThemeSides(root),
                DroppedKeys = mp.Invalid,
                Scopes = scopes,
                Description = $"merged colour dictionaries: {mp.Files.Count} file(s), {mp.Pal.Count} keys (dark + light)"
                              + scopeNote + ConflictNote(mp.Conflicts) + InvalidNote(mp.Invalid),
            };

        // 1) 深淺配對：dark 檔 × light 檔，brush 鍵集重疊 ≥ 50%
        (XamlCandidate D, XamlCandidate L, List<string> Common)? bestPair = null;
        foreach (var d in xaml.Where(c => c.HintDark && !c.HintLight))
            foreach (var l in xaml.Where(c => c.HintLight && !c.HintDark))
            {
                var common = d.Brushes.Keys.Where(l.Brushes.ContainsKey).ToList();
                var minCount = Math.Min(d.Brushes.Count, l.Brushes.Count);
                if (minCount > 0 && (double)common.Count / minCount >= 0.5 &&
                    (bestPair is null || common.Count > bestPair.Value.Common.Count))
                    bestPair = (d, l, common);
            }
        if (bestPair is { } p)
        {
            var pal = new Palette();
            foreach (var k in p.Common)
                pal.Entries[k] = (p.D.Brushes[k], p.L.Brushes[k]);
            return new PaletteDetection
            {
                Palette = pal,
                Mode = PaletteMode.Pair,
                ExcludedFiles = UiExclusions([p.D.File, p.L.File]),
                ThemeFileSides = ThemeSides(root, p.D.File, p.L.File),
                Description = $"auto-detected theme pair: {p.D.Rel} + {p.L.Rel} ({pal.Count} keys)",
            };
        }

        // 2) C# 三元組（深淺都有）—— 優先於只有一套值的單一 XAML 檔
        if (cs.Count > 0)
        {
            var best = cs.OrderByDescending(c => c.Pal.Count).First();
            var pal = new Palette();
            foreach (var (k, v) in best.Pal) pal.Entries[k] = v;
            // 鍵集與 C# 高度重疊的 XAML 候選視為同一色盤的靜態複本，一併排除出 UI 掃描
            var excl = xaml
                .Where(c => c.Brushes.Count > 0 &&
                            (double)c.Brushes.Keys.Count(pal.Entries.ContainsKey) / c.Brushes.Count >= 0.5)
                .Select(c => c.File);
            return new PaletteDetection
            {
                Palette = pal,
                Mode = PaletteMode.CSharp,
                ExcludedFiles = UiExclusions(excl),
                Description = $"auto-detected C# source of truth: {best.Rel} ({pal.Count} keys; assuming tuple = key, dark, light)",
            };
        }

        // 2.4) 字典裡定義了鍵、卻一個都讀不懂：這是偵測失敗，不能往下交給「挑一個檔」——
        //      那會讓另一個主題的值佔據兩欄。標成退化（--strict-palette 擋得住）並點名
        if (merged is { Pal.Count: 0, Invalid.Count: > 0 } mi)
            return new PaletteDetection
            {
                Palette = new Palette(),
                Mode = PaletteMode.None,
                IsDegraded = true,
                ExcludedFiles = UiExclusions(mi.Files),
                DroppedKeys = mi.Invalid,
                Description = "no usable palette — every colour key the dictionaries define has a value the tool cannot read; auditing hardcoded colors only"
                              + InvalidNote(mi.Invalid),
            };

        // 2.5) 合併式色盤（單一主題）—— 仍優於「挑一個檔」，鍵集是所有字典的聯集
        if (merged is { } ms && ms.Pal.Count > 0)
            return new PaletteDetection
            {
                Palette = ms.Pal,
                Mode = PaletteMode.Single,
                ExcludedFiles = UiExclusions(ms.Files),
                DroppedKeys = ms.Invalid,
                Scopes = scopes,
                Description = $"merged colour dictionaries: {ms.Files.Count} file(s), {ms.Pal.Count} keys (single theme)"
                              + scopeNote + ConflictNote(ms.Conflicts) + InvalidNote(ms.Invalid),
            };

        // 3) 單一 XAML 色盤檔：深淺同值（單一主題專案）
        if (xaml.Count > 0)
        {
            var best = xaml.OrderByDescending(c => c.Brushes.Count).First();
            var pal = new Palette();
            foreach (var (k, v) in best.Brushes) pal.Entries[k] = (v, v);
            var others = xaml.Where(c => c.File != best.File).Select(c => c.Rel).ToList();
            var note = others.Count > 0 ? $"; unused candidates: {string.Join(", ", others)}" : "";
            return new PaletteDetection
            {
                Palette = pal,
                Mode = PaletteMode.Single,
                ExcludedFiles = UiExclusions([best.File]),
                Description = $"auto-detected single-theme palette: {best.Rel} ({pal.Count} keys){note}",
            };
        }

        // 4) 什麼都沒找到 → 退回只算寫死色碼。這是退化，要喊。
        return new PaletteDetection
        {
            Palette = new Palette(),
            Mode = PaletteMode.None,
            IsDegraded = true,
            ExcludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Description = "no palette found (no ResourceDictionary color keys, no C# tuples) — auditing hardcoded colors only",
        };
    }
}

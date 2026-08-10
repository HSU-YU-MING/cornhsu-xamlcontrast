using System.Text.RegularExpressions;

namespace XamlContrast.Core;

public enum PaletteMode { Pair, CSharp, Single, None }

public sealed class PaletteDetection
{
    public required Palette Palette { get; init; }
    public required PaletteMode Mode { get; init; }
    /// <summary>被認定為色盤檔的 XAML（不進 UI 掃描 —— 色盤定義檔不是畫面）。</summary>
    public required HashSet<string> ExcludedFiles { get; init; }
    /// <summary>偵測結果的人話描述，一律印出來（偵測結果不靜默）。</summary>
    public required string Description { get; init; }
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
    private static bool IsValidHex(string v) => v.Length is 7 or 9;

    internal static IEnumerable<string> EnumerateFiles(string root, string pattern)
        => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Replace('/', '\\').Contains("\\obj\\") && !f.Replace('/', '\\').Contains("\\bin\\"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    private static string? Attr(Regex re, string attrs)
    {
        var m = re.Match(attrs);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>單一檔案裡的 &lt;Color x:Key&gt; 定義（供跨檔解析 brush 引用）。</summary>
    internal static Dictionary<string, string> ParseColorDefs(string file)
    {
        var colors = new Dictionary<string, string>();
        string text;
        try { text = File.ReadAllText(file); } catch { return colors; }
        foreach (Match m in ColorElement().Matches(text))
        {
            var key = Attr(KeyAttr(), m.Groups["attrs"].Value);
            if (key is null) continue;
            var v = m.Groups["v"].Value;
            if (v.StartsWith('#')) { if (IsValidHex(v)) colors[key] = v.ToUpperInvariant(); }
            else if (ColorResolver.NamedHex(v) is { } hex) colors[key] = hex;
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
        var brushes = new Dictionary<string, string>();
        var refs = new Dictionary<string, string>();
        var colors = ParseColorDefs(file);

        string text;
        try { text = File.ReadAllText(file); } catch { return brushes; }
        foreach (Match m in BrushElement().Matches(text))
        {
            var attrs = m.Groups["attrs"].Value;
            var key = Attr(KeyAttr(), attrs);
            var val = Attr(ColorAttr(), attrs);
            if (key is null || val is null) continue;
            if (val.StartsWith('#'))
            {
                if (IsValidHex(val)) brushes[key] = val.ToUpperInvariant();
            }
            else if (ColorResolver.NamedHex(val) is { } hex) brushes[key] = hex;
            else
            {
                var rm = ResourceRefValue().Match(val);
                if (rm.Success) refs[key] = rm.Groups["c"].Value;
            }
        }

        // brush 引用 Color 定義 → 解成實際色值。同檔優先，其次全專案。
        foreach (var (k, c) in refs)
        {
            if (colors.TryGetValue(c, out var v)) brushes[k] = v;
            else if (globalColors is not null && globalColors.TryGetValue(c, out var gv)) brushes[k] = gv;
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
    /// 只影響「能不能當色盤候選」，不影響這些檔案本身要不要被稽核。
    /// </summary>
    private static bool LooksLikeFixture(string rel)
    {
        var n = rel.Replace('\\', '/').ToLowerInvariant();
        return n.Contains("/test") || n.StartsWith("test") ||
               n.Contains("/sample") || n.StartsWith("sample") ||
               n.Contains("/demo") || n.StartsWith("demo") ||
               n.Contains("/example") || n.StartsWith("example") ||
               n.Contains("/fixture") || n.Contains("/mock");
    }

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
            var n = rel.ToLowerInvariant();
            cands.Add(new XamlCandidate(f, rel, brushes, n.Contains("dark"), n.Contains("light")));
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
    internal static (Palette Pal, bool IsPair, HashSet<string> Files, SortedSet<string> Conflicts)? MergedXamlPalette(string root)
    {
        var colorFiles = new List<(string File, Dictionary<string, string> Colors, bool D, bool L)>();
        var brushFiles = new List<(string File, Dictionary<string, string> Lit, Dictionary<string, string> Ref, bool D, bool L)>();

        foreach (var f in EnumerateFiles(root, "*.xaml"))
        {
            var rel = Path.GetRelativePath(root, f);
            if (LooksLikeFixture(rel)) continue;
            var n = rel.ToLowerInvariant();
            var isDark = n.Contains("dark");
            var isLight = n.Contains("light");

            var colors = ParseColorDefs(f);
            if (colors.Count > 0) colorFiles.Add((f, colors, isDark, isLight));

            var (lit, refs) = ParseBrushDefs(f);
            if (lit.Count + refs.Count > 0) brushFiles.Add((f, lit, refs, isDark, isLight));
        }
        if (colorFiles.Count == 0 && brushFiles.Count == 0) return null;

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

        var pal = new Palette();
        foreach (var k in darkB.Keys.Concat(lightB.Keys).Concat(darkR.Keys).Concat(lightR.Keys).Distinct())
        {
            // 每側：字面值優先，其次引用解析（引用對自己那側的 Color 表解）
            var d = darkB.GetValueOrDefault(k)
                    ?? (darkR.TryGetValue(k, out var dc) ? darkColors.GetValueOrDefault(dc) : null);
            var l = lightB.GetValueOrDefault(k)
                    ?? (lightR.TryGetValue(k, out var lc) ? lightColors.GetValueOrDefault(lc) : null);
            if (d is null && l is null) continue;
            pal.Entries.TryAdd(k, (d ?? l!, l ?? d!)); // 缺的一側退回另一側
        }
        // 沒有任何 brush 字典 → 直接把 Color 鍵當色票（有些專案就這樣用）
        if (pal.Count == 0)
            foreach (var (k, v) in darkColors)
                pal.Entries.TryAdd(k, (v, lightColors.GetValueOrDefault(k, v)));

        if (pal.Count < 3) return null;
        var anyDark = colorFiles.Any(c => c.D && !c.L) || brushFiles.Any(b => b.D && !b.L);
        var anyLight = colorFiles.Any(c => c.L && !c.D) || brushFiles.Any(b => b.L && !b.D);
        var files = new HashSet<string>(
            colorFiles.Select(c => c.File).Concat(brushFiles.Select(b => b.File)),
            StringComparer.OrdinalIgnoreCase);
        conflicts.UnionWith(colorConflicts);
        return (pal, anyDark && anyLight || !pal.IsSingleTheme, files, conflicts);
    }

    /// <summary>同鍵不同值的衝突要明講（規劃書 4.2：「不可默默取其一」）——
    /// 這通常代表 repo 裡有多個 App／子專案各自的色盤（ScreenToGif 的 Translator 形狀），
    /// 共用鍵名的配對可能配到錯的那套值。</summary>
    private static string ConflictNote(SortedSet<string> conflicts)
        => conflicts.Count == 0 ? "" :
           $"; !! {conflicts.Count} key(s) defined with conflicting values (resource scoping not modelled" +
           $" — first themed definition wins): {string.Join(", ", conflicts.Take(5))}{(conflicts.Count > 5 ? ", …" : "")}";

    /// <summary>單一檔案的 brush 定義，分成「字面色值」與「引用其他 Color 鍵」兩類。</summary>
    internal static (Dictionary<string, string> Literal, Dictionary<string, string> Ref) ParseBrushDefs(string file)
    {
        var lit = new Dictionary<string, string>();
        var refs = new Dictionary<string, string>();
        string text;
        try { text = File.ReadAllText(file); } catch { return (lit, refs); }
        foreach (Match m in BrushElement().Matches(text))
        {
            var attrs = m.Groups["attrs"].Value;
            var key = Attr(KeyAttr(), attrs);
            var val = Attr(ColorAttr(), attrs);
            if (key is null || val is null) continue;
            if (val.StartsWith('#')) { if (IsValidHex(val)) lit[key] = val.ToUpperInvariant(); }
            else if (ColorResolver.NamedHex(val) is { } hex) lit[key] = hex; // Color="White" 也是合法寫法
            else
            {
                var rm = ResourceRefValue().Match(val);
                if (rm.Success) refs[key] = rm.Groups["c"].Value;
            }
        }
        return (lit, refs);
    }

    internal static List<CsCandidate> CsCandidates(string root)
    {
        var cands = new List<CsCandidate>();
        foreach (var f in EnumerateFiles(root, "*.cs"))
        {
            var map = new Dictionary<string, (string, string)>();
            foreach (var line in File.ReadLines(f))
            {
                var m = CsTuple().Match(line);
                if (m.Success && IsValidHex(m.Groups["d"].Value) && IsValidHex(m.Groups["l"].Value))
                    map[m.Groups["k"].Value] =
                        (m.Groups["d"].Value.ToUpperInvariant(), m.Groups["l"].Value.ToUpperInvariant());
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
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (!File.Exists(full))
            throw new ConfigException($"palette.{field} not found: {rel}");
        return full;
    }

    public static PaletteDetection Detect(string root, ToolConfig? config = null)
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
                        ExcludedFiles = new HashSet<string>([dFile, lFile], StringComparer.OrdinalIgnoreCase),
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
                    var pal = ParseCsPalette(cFile, pattern);
                    var excl = XamlCandidates(root)
                        .Where(c => c.Brushes.Count > 0 &&
                                    (double)c.Brushes.Keys.Count(pal.Entries.ContainsKey) / c.Brushes.Count >= 0.5)
                        .Select(c => c.File);
                    return new PaletteDetection
                    {
                        Palette = pal,
                        Mode = PaletteMode.CSharp,
                        ExcludedFiles = new HashSet<string>(excl, StringComparer.OrdinalIgnoreCase),
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
                        ExcludedFiles = new HashSet<string>([sFile], StringComparer.OrdinalIgnoreCase),
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
        var cs = CsCandidates(root);
        var merged = MergedXamlPalette(root);

        // 0) 合併式色盤且深淺兩套值俱全 —— 優先於任何「挑單一檔」的路徑
        if (merged is { IsPair: true } mp)
            return new PaletteDetection
            {
                Palette = mp.Pal,
                Mode = PaletteMode.Pair,
                ExcludedFiles = mp.Files,
                Description = $"merged colour dictionaries: {mp.Files.Count} file(s), {mp.Pal.Count} keys (dark + light)"
                              + ConflictNote(mp.Conflicts),
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
                ExcludedFiles = new HashSet<string>(new[] { p.D.File, p.L.File }, StringComparer.OrdinalIgnoreCase),
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
                ExcludedFiles = new HashSet<string>(excl, StringComparer.OrdinalIgnoreCase),
                Description = $"auto-detected C# source of truth: {best.Rel} ({pal.Count} keys; assuming tuple = key, dark, light)",
            };
        }

        // 2.5) 合併式色盤（單一主題）—— 仍優於「挑一個檔」，鍵集是所有字典的聯集
        if (merged is { } ms && ms.Pal.Count > 0)
            return new PaletteDetection
            {
                Palette = ms.Pal,
                Mode = PaletteMode.Single,
                ExcludedFiles = ms.Files,
                Description = $"merged colour dictionaries: {ms.Files.Count} file(s), {ms.Pal.Count} keys (single theme)"
                              + ConflictNote(ms.Conflicts),
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
                ExcludedFiles = new HashSet<string>(new[] { best.File }, StringComparer.OrdinalIgnoreCase),
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

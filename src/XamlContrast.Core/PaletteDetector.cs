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
    /// <summary>找不到色盤、退回只算寫死色碼 —— 這是退化，要喊（規劃書 8.1 規則 3）。</summary>
    public bool IsDegraded => Mode == PaletteMode.None;
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
    [GeneratedRegex("""<Color x:Key="(?<k>\w+)">(?<v>#[0-9A-Fa-f]{6,8})</Color>""")]
    private static partial Regex ColorDef();

    [GeneratedRegex("""<SolidColorBrush x:Key="(?<k>\w+)"\s+Color="(?<v>#[0-9A-Fa-f]{6,8})["]""")]
    private static partial Regex BrushLiteral();

    [GeneratedRegex("""<SolidColorBrush x:Key="(?<k>\w+)"\s+Color="\{StaticResource (?<c>\w+)\}""")]
    private static partial Regex BrushRef();

    /// <summary>C# 真相源：("Key", "#深色", "#淺色") 三元組。
    /// ⚠「第一個是深、第二個是淺」是假設（Kindling 如此）；之後改由 config 指定。</summary>
    [GeneratedRegex("""\("(?<k>\w+)"\s*,\s*"(?<d>#[0-9A-Fa-f]{6,8})"\s*,\s*"(?<l>#[0-9A-Fa-f]{6,8})"\)""")]
    private static partial Regex CsTuple();

    internal sealed record XamlCandidate(
        string File, string Rel, Dictionary<string, string> Brushes, bool HintDark, bool HintLight);

    internal sealed record CsCandidate(string File, string Rel, Dictionary<string, (string Dark, string Light)> Pal);

    internal static IEnumerable<string> EnumerateFiles(string root, string pattern)
        => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Replace('/', '\\').Contains("\\obj\\") && !f.Replace('/', '\\').Contains("\\bin\\"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    internal static List<XamlCandidate> XamlCandidates(string root)
    {
        var cands = new List<XamlCandidate>();
        foreach (var f in EnumerateFiles(root, "*.xaml"))
        {
            var colors = new Dictionary<string, string>();
            var brushes = new Dictionary<string, string>();
            var refs = new Dictionary<string, string>();
            foreach (var line in File.ReadLines(f))
            {
                Match m;
                if ((m = ColorDef().Match(line)).Success) colors[m.Groups["k"].Value] = m.Groups["v"].Value.ToUpperInvariant();
                else if ((m = BrushLiteral().Match(line)).Success) brushes[m.Groups["k"].Value] = m.Groups["v"].Value.ToUpperInvariant();
                else if ((m = BrushRef().Match(line)).Success) refs[m.Groups["k"].Value] = m.Groups["c"].Value;
            }
            // brush 引用同檔的 Color 定義 → 解成實際色值
            foreach (var (k, c) in refs)
                if (colors.TryGetValue(c, out var v)) brushes[k] = v;

            if (brushes.Count < 3) continue;

            // ⚠ 提示只看「專案內的相對路徑」。看完整路徑的話，專案放在
            //   D:\dark-projects\ 底下會讓所有候選都染上 dark 提示、配對整個失效。
            var rel = Path.GetRelativePath(root, f);
            var n = rel.ToLowerInvariant();
            cands.Add(new XamlCandidate(f, rel, brushes, n.Contains("dark"), n.Contains("light")));
        }
        return cands;
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
                if (m.Success)
                    map[m.Groups["k"].Value] =
                        (m.Groups["d"].Value.ToUpperInvariant(), m.Groups["l"].Value.ToUpperInvariant());
            }
            if (map.Count >= 3)
                cands.Add(new CsCandidate(f, Path.GetRelativePath(root, f), map));
        }
        return cands;
    }

    public static PaletteDetection Detect(string root)
    {
        var xaml = XamlCandidates(root);
        var cs = CsCandidates(root);

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
            ExcludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Description = "no palette found (no ResourceDictionary color keys, no C# tuples) — auditing hardcoded colors only",
        };
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlContrast.Core;

/// <summary>
/// 導入模式（規劃書 4.6）：既有專案起跑就是破 39~55 組，「零破才給過」的 gate
/// 第一天全紅，工具會被關掉。baseline 承認既有債、只擋新增與惡化；債只准減不准增。
///
/// 鍵不含行號 —— 行號隨編輯漂移，含進去的話每次重排版都會「假性新增」。
/// 同鍵多筆用計數比較：同一組字底配對從 2 處變 3 處也是惡化。
///
/// 收的是「低於 AA」的每一組（fail＋warn），不論寫出時的 gate；比對時只擋目前 gate 會擋的那些
/// （預設 gate warn：兩者都擋；--fail-on fail：只擋 fail）。⚠ 0.x 只收 fail —— 1.0 的預設 gate 改成 warn 之後，
/// 那會讓既有的 warn 一個都不算已知債，baseline 對 warn 完全沒用。
/// </summary>
public static class Baseline
{
    // Worst = 記錄當下的最差比值。鍵是字面文字（{DynamicResource Fg} 之類），
    // 色盤值被調暗時鍵不會變 —— 少了比值欄位，「色盤漂移」型的惡化會靜默放行。
    public sealed record Entry(string File, string Element, string Fg, string Bg, int Count, double Worst);

    public sealed class ComparisonResult
    {
        public required List<Finding> NewFailures { get; init; }
        public required List<Entry> WorsenedFailures { get; init; }
        public required int KnownDebt { get; init; }
        public required int PaidDebt { get; init; }
        /// <summary>gate 擋的每一筆 finding 的判定：known／new／worsened（JSON 的 findings[].baseline）。
        /// 鍵惡化時那個鍵底下 gate 擋的每一筆都標 worsened —— 鍵不含行號，分不出「多出來的是哪一處」。</summary>
        public IReadOnlyDictionary<Finding, string> Status { get; init; } = new Dictionary<Finding, string>();
        public bool Passes => NewFailures.Count == 0 && WorsenedFailures.Count == 0;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string KeyOf(Finding f) => $"{f.File}|{f.Element}|{f.Fg}|{f.Bg}";

    private static bool BelowAa(Finding f) => f.Category is Category.Fail or Category.Warn;

    /// <summary>baseline 檔格式的版本。1.0 起寫進檔裡、讀取時必須認得 ——
    /// 沒有它就無法在 1.x 演進格式，舊格式的檔也分辨不出來（0.x 的檔沒有這一欄）。</summary>
    public const int SchemaVersion = 1;

    /// <summary>鍵的產生方式的版本（審查 1.0 A2）。鍵的文字（element 的標籤、fg 的寫法、bg 的合成描述）屬稽核語意，
    /// 1.x 的 minor 可以改 —— 改的那一版遞增這個數字。讀到舊的就明確叫人重寫，不要冒出一整排假的 NEW 加 paid off
    /// （凍結宣告的演進規則 5）。檔案沒寫這一欄視為 1。</summary>
    public const int KeyVersion = 1;

    public static string Write(AuditResult result)
    {
        var entries = result.Findings.Where(BelowAa)
            .GroupBy(KeyOf)
            .Select(g =>
            {
                var f = g.First();
                return new Entry(f.File, f.Element, f.Fg, f.Bg, g.Count(), g.Min(x => x.Worst));
            })
            .OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Element, StringComparer.Ordinal)
            .ThenBy(e => e.Fg, StringComparer.Ordinal).ThenBy(e => e.Bg, StringComparer.Ordinal);
        return JsonSerializer.Serialize(new { schemaVersion = SchemaVersion, keyVersion = KeyVersion, comment = "XamlContrast baseline — known debt; may only shrink", entries }, JsonOpts);
    }

    /// <param name="gateWarn">gate 是 warn（預設）：低於 AA 的都擋；false（--fail-on fail）：只擋 fail。</param>
    public static ComparisonResult Compare(AuditResult result, string baselineJson, bool gateWarn = true)
        => Compare(result, Parse(baselineJson), gateWarn);

    /// <summary>讀進來的 baseline：鍵 → entry。</summary>
    public sealed class Known
    {
        public required Dictionary<string, Entry> Entries { get; init; }
    }

    /// <summary>
    /// 解析 baseline 檔。格式錯誤擲例外（CLI 轉成 exit 2、提示重跑 --write-baseline）—— 在稽核之前就驗，輸入錯了後面都沒有意義。
    /// 1.0 起嚴格（決策檔 H17）：
    /// - 必須有 schemaVersion 而且是這個版本認得的。沒有 = 0.x 寫的檔：1.0 改了 baseline 收的東西（低於 AA 的每一組，
    ///   不只 fail），照讀的話所有 warn 都會變成 NEW —— 大聲失敗並說出怎麼辦，比一牆的假 NEW 好。
    /// - 每筆都要有 file／element／fg／bg／count／worst。⚠ 0.x 缺欄時補 0：缺 worst → 色盤漂移檢查靜默失效
    ///   （比值再差也不會 &lt; 0 − 0.01）；缺 count → 每一處都算惡化。
    /// 頂層與每筆的欄位名都不分大小寫。
    /// </summary>
    public static Known Parse(string baselineJson) => Parse(baselineJson, KeyVersion);

    /// <param name="currentKeyVersion">這個版本產生鍵的方式（測試用來模擬「之後的版本」）。</param>
    internal static Known Parse(string baselineJson, int currentKeyVersion)
    {
        using var doc = JsonDocument.Parse(baselineJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("the file is not a JSON object");
        JsonElement? Prop(JsonElement obj, string name)
            => obj.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                  .Select(p => (JsonElement?)p.Value).FirstOrDefault();

        if (Prop(root, "schemaVersion") is not { } ver)
            throw new FormatException("the file has no schemaVersion — it was written by XamlContrast 0.x. " +
                                      "1.0 changed what a baseline holds (every pair below AA, not only fails), " +
                                      "so regenerate it with --write-baseline");
        if (ver.ValueKind != JsonValueKind.Number || !ver.TryGetInt32(out var v) || v != SchemaVersion)
            throw new FormatException($"schemaVersion {ver.GetRawText()} is not supported (this version reads {SchemaVersion}) — " +
                                      "was the file written by a newer XamlContrast?");
        var keyVersion = 1;
        if (Prop(root, "keyVersion") is { } kv)
        {
            if (kv.ValueKind != JsonValueKind.Number || !kv.TryGetInt32(out keyVersion) || keyVersion < 1)
                throw new FormatException($"keyVersion {kv.GetRawText()} is not a positive integer");
        }
        if (keyVersion > currentKeyVersion)
            throw new FormatException($"keyVersion {keyVersion} is newer than this version generates ({currentKeyVersion}) — " +
                                      "was the file written by a newer XamlContrast?");
        if (keyVersion < currentKeyVersion)
            throw new FormatException($"the file's keys were generated by an older XamlContrast (keyVersion {keyVersion}; " +
                                      $"this version generates keyVersion {currentKeyVersion}), so every known pair would show up as NEW — " +
                                      "regenerate it with --write-baseline");
        if (Prop(root, "entries") is not { ValueKind: JsonValueKind.Array } entries)
            throw new FormatException("the file has no 'entries' array");

        var known = new Dictionary<string, Entry>();
        var i = 0;
        foreach (var el in entries.EnumerateArray())
        {
            i++;
            if (el.ValueKind != JsonValueKind.Object) throw new FormatException($"entry {i} is not an object");
            foreach (var field in (ReadOnlySpan<string>)["file", "element", "fg", "bg", "count", "worst"])
                if (Prop(el, field) is not { } fv || fv.ValueKind == JsonValueKind.Null)
                    throw new FormatException($"entry {i} has no '{field}'");
            var e = el.Deserialize<Entry>(JsonOpts)!;
            known[$"{e.File}|{e.Element}|{e.Fg}|{e.Bg}"] = e;
        }
        return new Known { Entries = known };
    }

    public static ComparisonResult Compare(AuditResult result, Known baseline, bool gateWarn = true)
    {
        var known = baseline.Entries;

        bool Gating(Finding f) => f.Category == Category.Fail || gateWarn && f.Category == Category.Warn;
        // 目前低於 AA 的每一組，依鍵分組；處數與最差比值都算整組（warn 惡化成 fail 也是比值變差）
        var current = result.Findings.Where(BelowAa).GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList());
        var newFailures = new List<Finding>();
        var worsened = new List<Entry>();
        var status = new Dictionary<Finding, string>(ReferenceEqualityComparer.Instance);
        var knownDebt = 0;
        void Mark(List<Finding> gating, string s) { foreach (var f in gating) status[f] = s; }

        foreach (var (key, list) in current)
        {
            var gating = list.Where(Gating).ToList();
            if (gating.Count == 0) continue; // 這個 gate 不擋的（--fail-on fail 下的 warn）：不判新增或惡化
            var count = list.Count;
            if (!known.TryGetValue(key, out var entry))
            {
                newFailures.AddRange(gating);
                Mark(gating, "new");
                continue;
            }
            var worstNow = list.Min(f => f.Worst);
            if (count > entry.Count)
            {
                worsened.Add(entry with { Count = count }); // 同鍵的處數變多也是惡化
                Mark(gating, "worsened");
            }
            else if (worstNow < entry.Worst - 0.01)
            {
                worsened.Add(entry with { Worst = worstNow }); // 色盤漂移：鍵沒變但比值更差
                Mark(gating, "worsened");
            }
            else
            {
                knownDebt += count;
                Mark(gating, "known");
            }
        }

        // ⚠ 單位要跟 knownDebt 一致：knownDebt 累加的是 count（處數），
        //   這裡若數 key 的個數，同鍵 5 處一次修完只顯示「還了 1」——
        //   進度看起來比實際少，導入期最需要看到進展的時候反而低報。
        var paid = known.Where(kv => !current.ContainsKey(kv.Key)).Sum(kv => kv.Value.Count);
        return new ComparisonResult
        {
            NewFailures = newFailures,
            WorsenedFailures = worsened,
            KnownDebt = knownDebt,
            PaidDebt = paid,
            Status = status,
        };
    }

}

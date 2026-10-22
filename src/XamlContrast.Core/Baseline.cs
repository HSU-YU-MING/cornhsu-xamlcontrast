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
        /// <summary>baseline 檔本身的問題（缺欄位之類）—— 不改判定，但一定要講出來。</summary>
        public List<string> Warnings { get; init; } = new();
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
        return JsonSerializer.Serialize(new { comment = "XamlContrast baseline — known debt; may only shrink", entries }, JsonOpts);
    }

    /// <param name="gateWarn">gate 是 warn（預設）：低於 AA 的都擋；false（--fail-on fail）：只擋 fail。</param>
    public static ComparisonResult Compare(AuditResult result, string baselineJson, bool gateWarn = true)
        => Compare(result, Parse(baselineJson), gateWarn);

    /// <summary>讀進來的 baseline：鍵 → entry，加上檔案本身的問題。</summary>
    public sealed class Known
    {
        public required Dictionary<string, Entry> Entries { get; init; }
        public List<string> Warnings { get; init; } = new();
    }

    /// <summary>解析 baseline 檔。格式錯誤擲例外（CLI 轉成 exit 2）—— 在稽核之前就驗，輸入錯了後面都沒有意義。</summary>
    public static Known Parse(string baselineJson)
    {
        using var doc = JsonDocument.Parse(baselineJson);
        var known = new Dictionary<string, Entry>();
        int noCount = 0, noWorst = 0;
        // 頂層的 entries 與每筆的欄位名同一套政策：不分大小寫（舊版頂層用 GetProperty 分大小寫，
        // PascalCase 的檔 exit 2，訊息是不知所云的 "The given key was not present in the dictionary."）
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.EnumerateObject().Any(p => p.Name.Equals("entries", StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("the file has no 'entries' array");
        var entries = doc.RootElement.EnumerateObject()
            .First(p => p.Name.Equals("entries", StringComparison.OrdinalIgnoreCase)).Value;
        foreach (var el in entries.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) throw new FormatException("every entry must be an object");
            var e = el.Deserialize<Entry>(JsonOpts)!;
            known[$"{e.File}|{e.Element}|{e.Fg}|{e.Bg}"] = e;
            // ⚠ 缺欄位會被補成 0：缺 worst → 色盤漂移檢查失效（比值再差也不會 < 0 − 0.01）；
            //   缺 count → 每一處都算惡化。工具自己寫出的檔一定有這兩欄，只有手改或外部產生的會中 ——
            //   但中了就是一整類惡化靜默放行，所以至少要點名（要不要直接拒絕讀取是待決定的格式收緊）
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (!HasProperty(el, "count")) noCount++;
                if (!HasProperty(el, "worst")) noWorst++;
            }
        }
        var warnings = new List<string>();
        if (noWorst > 0)
            warnings.Add($"{noWorst} {(noWorst == 1 ? "entry has" : "entries have")} no 'worst' — palette drift " +
                         "(same key, darker colour) is not detected for them; regenerate with --write-baseline");
        if (noCount > 0)
            warnings.Add($"{noCount} {(noCount == 1 ? "entry has" : "entries have")} no 'count' — every occurrence " +
                         "counts as worsened; regenerate with --write-baseline");
        return new Known { Entries = known, Warnings = warnings };
    }

    public static ComparisonResult Compare(AuditResult result, Known baseline, bool gateWarn = true)
    {
        var known = baseline.Entries;
        var warnings = baseline.Warnings;

        bool Gating(Finding f) => f.Category == Category.Fail || gateWarn && f.Category == Category.Warn;
        // 目前低於 AA 的每一組，依鍵分組；處數與最差比值都算整組（warn 惡化成 fail 也是比值變差）
        var current = result.Findings.Where(BelowAa).GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList());
        var newFailures = new List<Finding>();
        var worsened = new List<Entry>();
        var knownDebt = 0;

        foreach (var (key, list) in current)
        {
            var gating = list.Where(Gating).ToList();
            if (gating.Count == 0) continue; // 這個 gate 不擋的（--fail-on fail 下的 warn）：不判新增或惡化
            var count = list.Count;
            if (!known.TryGetValue(key, out var entry))
            {
                newFailures.AddRange(gating);
                continue;
            }
            var worstNow = list.Min(f => f.Worst);
            if (count > entry.Count)
            {
                worsened.Add(entry with { Count = count }); // 同鍵的處數變多也是惡化
            }
            else if (worstNow < entry.Worst - 0.01)
            {
                worsened.Add(entry with { Worst = worstNow }); // 色盤漂移：鍵沒變但比值更差
            }
            else
            {
                knownDebt += count;
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
            Warnings = warnings,
        };
    }

    /// <summary>屬性名不分大小寫（與 Deserialize 的 PropertyNameCaseInsensitive 一致）。</summary>
    private static bool HasProperty(JsonElement obj, string name)
        => obj.EnumerateObject().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlContrast.Core;

/// <summary>
/// 導入模式（規劃書 4.6）：既有專案起跑就是破 39~55 組，「零破才給過」的 gate
/// 第一天全紅，工具會被關掉。baseline 承認既有債、只擋新增與惡化；債只准減不准增。
///
/// 鍵不含行號 —— 行號隨編輯漂移，含進去的話每次重排版都會「假性新增」。
/// 同鍵多筆用計數比較：同一組字底配對從 2 處變 3 處也是惡化。
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
        public bool Passes => NewFailures.Count == 0 && WorsenedFailures.Count == 0;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string KeyOf(Finding f) => $"{f.File}|{f.Element}|{f.Fg}|{f.Bg}";

    private static Dictionary<string, int> FailKeys(AuditResult result) =>
        result.Findings.Where(f => f.Category == Category.Fail)
            .GroupBy(KeyOf)
            .ToDictionary(g => g.Key, g => g.Count());

    public static string Write(AuditResult result)
    {
        var entries = result.Findings.Where(f => f.Category == Category.Fail)
            .GroupBy(KeyOf)
            .Select(g =>
            {
                var f = g.First();
                return new Entry(f.File, f.Element, f.Fg, f.Bg, g.Count(), g.Min(x => x.Worst));
            })
            .OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Element, StringComparer.Ordinal);
        return JsonSerializer.Serialize(new { comment = "XamlContrast baseline — known debt; may only shrink", entries }, JsonOpts);
    }

    public static ComparisonResult Compare(AuditResult result, string baselineJson)
    {
        var doc = JsonDocument.Parse(baselineJson);
        var known = new Dictionary<string, Entry>();
        foreach (var el in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            var e = el.Deserialize<Entry>(JsonOpts)!;
            known[$"{e.File}|{e.Element}|{e.Fg}|{e.Bg}"] = e;
        }

        var current = FailKeys(result);
        var newFailures = new List<Finding>();
        var worsened = new List<Entry>();
        var knownDebt = 0;

        foreach (var (key, count) in current)
        {
            if (!known.TryGetValue(key, out var entry))
            {
                newFailures.AddRange(result.Findings.Where(f => f.Category == Category.Fail && KeyOf(f) == key));
                continue;
            }
            var worstNow = result.Findings
                .Where(f => f.Category == Category.Fail && KeyOf(f) == key)
                .Min(f => f.Worst);
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

        var paid = known.Keys.Count(k => !current.ContainsKey(k));
        return new ComparisonResult
        {
            NewFailures = newFailures,
            WorsenedFailures = worsened,
            KnownDebt = knownDebt,
            PaidDebt = paid,
        };
    }
}

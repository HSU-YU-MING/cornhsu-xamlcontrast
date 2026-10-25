using System.Text.Json.Nodes;

namespace XamlContrast.Core;

/// <summary>
/// 匿名使用統計的 data（XamlContrast 1.1；協定是 stats-collector 的 PROTOCOL.md 第 7.3 節，收集站的 validate.ts 逐欄驗）。
/// 只有計數與分桶：檔名、路徑、元素名、色票鍵、色碼、設定檔內容一個都不進來 —— 那些只在報告裡，報告不離開這台機器。
/// 收集站對多一個欄位、少一個欄位、型別或範圍不對一律拒收整筆，所以這裡的形狀與協定逐欄一致；**要改先改協定**，
/// 收集站先上線新版，工具才能送新欄位。外層（schema、tool、toolVersion、os、ci、projectId）、開關、告知與傳輸在 CLI 的 Stats.cs。
/// </summary>
public static class UsageStats
{
    /// <summary>ratios 的分桶邊界（左閉右開）：&lt;1.5、1.5–2、2–3、3–4.5、4.5–7、≥7 —— 六桶。</summary>
    public static IReadOnlyList<double> RatioBounds { get; } = [1.5, 2, 3, 4.5, 7];

    /// <summary>計數的上限（協定：0–10,000,000）。超過的照上限送 —— 送一筆會被拒收的資料沒有意義，而真實專案到不了這個數。</summary>
    public const int MaxCount = 10_000_000;

    /// <summary>協定認得的開放集的值。1.x 長出新值（新的 unresolved 原因、新的色盤模式、新的 gate）時送 other ——
    /// 收集站不認得的鍵會讓整筆被拒，而協定更新之前它不會認得。</summary>
    private static readonly HashSet<string> KnownReasons =
    [
        "no-ancestor-background", "bound-or-gradient", "unknown-palette-key", "translucent-uncomposited",
        "same-brush-pair", "over-sibling-content", "in-style-template",
    ];
    private static readonly HashSet<string> KnownModes = ["pair", "csharp", "single", "none"];
    private static readonly HashSet<string> KnownGates = ["warn", "fail"];

    /// <summary>一個比值落在哪一桶（0–5）。</summary>
    public static int RatioBucket(double ratio)
    {
        var i = 0;
        while (i < RatioBounds.Count && ratio >= RatioBounds[i]) i++;
        return i;
    }

    /// <summary>協定 7.3 的 data。exitCode 是稽核的判定（0／1／3 —— 只有得出判定的稽核才送，用法錯誤的 2 不會走到這裡）；
    /// failOn 是實際生效的 gate（旗標 &gt; config &gt; 預設）。欄位順序照協定的範例。</summary>
    public static JsonObject Data(AuditResult r, int exitCode, string failOn)
    {
        if (exitCode is not (0 or 1 or 3))
            throw new ArgumentOutOfRangeException(nameof(exitCode), exitCode, "statistics are only sent for an audit verdict (0, 1 or 3)");

        // 只列 > 0 的原因（收集站拒收 0）；不認得的併進 other
        var unresolvedBy = new JsonObject();
        foreach (var reason in Enum.GetValues<UnresolvedReason>())
        {
            if (r.UnresolvedBy.GetValueOrDefault(reason) is not (> 0 and var n)) continue;
            var label = Report.ReasonLabel(reason);
            var key = KnownReasons.Contains(label) ? label : "other";
            unresolvedBy[key] = Count((unresolvedBy[key]?.GetValue<int>() ?? 0) + (long)n);
        }

        // 非裝飾配對依最差比值分桶：兩個主題取低的；只存在於一個主題的就是那個主題（Finding.Worst）
        var ratios = new int[RatioBounds.Count + 1];
        foreach (var f in r.Findings)
            if (f.Category != Category.Decorative) ratios[RatioBucket(f.Worst)]++;

        var mode = Report.ModeLabel(r.Detection.Mode);
        return new JsonObject
        {
            ["exitCode"] = exitCode,
            ["failOn"] = KnownGates.Contains(failOn) ? failOn : "other",
            ["paletteMode"] = KnownModes.Contains(mode) ? mode : "other",
            ["paletteDegraded"] = r.Detection.IsDegraded,
            ["files"] = Count(r.FileCount),
            ["pairs"] = Count(r.Pairs),
            // 與報告的 summary.coverage 同一個數（一位小數）
            ["coverage"] = r.CoveragePercent,
            ["counts"] = new JsonObject
            {
                ["fail"] = Count(r.CountOf(Category.Fail)),
                ["warn"] = Count(r.CountOf(Category.Warn)),
                ["ok"] = Count(r.CountOf(Category.Ok)),
                ["decorative"] = Count(r.CountOf(Category.Decorative)),
            },
            ["unresolvedBy"] = unresolvedBy,
            ["ratios"] = new JsonArray([.. ratios.Select(n => (JsonNode)Count(n))]),
        };
    }

    private static int Count(long n) => (int)Math.Clamp(n, 0, MaxCount);
}

using XamlContrast.Core;

// XamlContrast CLI — 靜態 XAML 對比度稽核，退出碼給 CI 當守門員。
//
// 退出碼政策（規劃書 4.5）：
//   fail > 0                    → 1
//   --fail-on warn 且 warn > 0  → 1
//   配對數 0                    → 1（不可設定 —— 空掃綠燈就是謊報健康）
//   --strict-palette 且色盤退化 → 1

var showOk = false;
var showUnresolved = false;
var failOnWarn = false;
var strictPalette = false;
double? minCoverage = null;   // null = 用 config／內建預設
string? jsonPath = null;
string? sarifPath = null;
string? mdPath = null;
string? baselinePath = null;
string? writeBaselinePath = null;
string? root = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--show-ok": showOk = true; break;
        case "--show-unresolved": showUnresolved = true; break;
        case "--strict-palette": strictPalette = true; break;
        case "--min-coverage":
            if (i + 1 >= args.Length ||
                !double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out var mc) ||
                mc is < 0 or > 100)
            { Console.Error.WriteLine("--min-coverage takes a number between 0 and 100"); return 2; }
            minCoverage = mc; i++;
            break;
        case "--fail-on":
            if (i + 1 >= args.Length || args[i + 1] is not ("warn" or "fail"))
            { Console.Error.WriteLine("--fail-on takes 'warn' or 'fail'"); return 2; }
            failOnWarn = args[++i] == "warn";
            break;
        case "--json":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--json takes a file path"); return 2; }
            jsonPath = args[++i];
            break;
        case "--sarif":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--sarif takes a file path"); return 2; }
            sarifPath = args[++i];
            break;
        case "--md":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--md takes a file path"); return 2; }
            mdPath = args[++i];
            break;
        case "--baseline":
            // 路徑可省略 —— 預設檔名慣例 xamlcontrast-baseline.json
            baselinePath = i + 1 < args.Length && !args[i + 1].StartsWith('-')
                ? args[++i] : "xamlcontrast-baseline.json";
            break;
        case "--write-baseline":
            writeBaselinePath = i + 1 < args.Length && !args[i + 1].StartsWith('-')
                ? args[++i] : "xamlcontrast-baseline.json";
            break;
        case "-h" or "--help":
            PrintUsage();
            return 0;
        case "--version":
            Console.WriteLine(typeof(Program).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                is [System.Reflection.AssemblyInformationalVersionAttribute a, ..] ? a.InformationalVersion.Split('+')[0] : "unknown");
            return 0;
        default:
            if (root is null && !args[i].StartsWith('-')) { root = args[i]; break; }
            Console.Error.WriteLine($"unknown argument: {args[i]}");
            return 2;
    }
}

if (root is null) { PrintUsage(); return 2; }
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"root not found: {root}");
    return 2;
}

ToolConfig config;
PaletteDetection detection;
try
{
    // config 是逃生口不是必經之路；寫錯要有好訊息，不能靜默忽略
    config = ToolConfig.Load(root);
    detection = PaletteDetector.Detect(root, config);
}
catch (ConfigException ex)
{
    Console.Error.WriteLine($"config error: {ex.Message}");
    return 2;
}

// 優先序：CLI 旗標 > config > 內建預設（旗標只能加嚴，不能替 config 鬆綁）
failOnWarn = failOnWarn || config.FailOn == "warn";
strictPalette = strictPalette || config.StrictPalette;
// ⚠ 覆蓋率下限是唯一「旗標可以放寬 config」的項目：它擋的是「工具看不見你的專案」，
//    不是稽核標準。使用者明確傳 --min-coverage 0 表示「我知道，先讓我跑」是合理的逃生口。
var minCov = minCoverage ?? config.MinCoverage;

var result = Auditor.Run(root, detection, config);

Console.Write(Report.ToConsole(result, showOk));
if (showUnresolved && result.UnresolvedSites.Count > 0)
    Console.Write(Report.UnresolvedList(result));

if (jsonPath is not null && !TryWrite(jsonPath, Report.ToJson(result), "json")) return 2;

if (sarifPath is not null)
{
    var ver = typeof(Program).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        is [System.Reflection.AssemblyInformationalVersionAttribute va, ..] ? va.InformationalVersion.Split('+')[0] : "0.0.0";
    if (!TryWrite(sarifPath, Report.ToSarif(result, ver), "sarif")) return 2;
}

var fail = result.CountOf(Category.Fail);
var warn = result.CountOf(Category.Warn);

// 退出碼的判定抽成區域函式,因為 --md 的報告要標明 PASS/FAIL —— 得先知道結論才能寫檔。
// 判定邏輯與各分支的輸出一字未改,只是包了一層。
var exitCode = DecideExit();

if (mdPath is not null && !TryWrite(mdPath, Report.ToMarkdown(result, exitCode), "markdown")) return 2;

return exitCode;

// 輸出寫檔失敗要落在 0/1/2 的退出碼契約內，並給人話 —— 之前是裸 File.WriteAllText，
// `--json out/report.json` 而 out/ 不存在就噴 .NET 堆疊、exit 127（未處理例外的退出碼，
// 契約外的值）。baseline 讀取失敗早就有友善訊息了，輸出端不該是另一套標準。
// 不代建目錄：輸出路徑寫錯時，安靜地生一個目錄比報錯更難查。
static bool TryWrite(string path, string content, string label, string suffix = "")
{
    try
    {
        File.WriteAllText(path, content);
        Console.WriteLine($"{label} written: {path}{suffix}");
        return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
    {
        Console.Error.WriteLine($"cannot write {label} to {path}: {ex.Message}");
        return false;
    }
}

int DecideExit()
{

    if (result.Pairs == 0)
    {
        // 0 組配對時「達標」無意義 —— 沒有任何可稽核的項目。空掃綠燈就是謊報健康。
        Console.WriteLine("exit 1: resolved 0 pairs — nothing was audited, this result does not mean the project passes");
        return 1;
    }

    // ⚠ 色盤退化在「所有模式」下都算失敗。這個檢查原本擺在函式最尾端，而
    //   --baseline / --write-baseline 兩個分支都會直接 return，於是永遠走不到 ——
    //   偏偏 --baseline 正是 README 推薦給既有專案的導入路徑，等於守門員在最常見
    //   的組合下靜默失效。實測過的失效鏈：主題檔被搬走 → 色盤偵測退化 → 色票配對
    //   全變 unresolved → 從 findings 消失 → ratchet 判成「已還債」→ 綠燈，而且
    //   還印出「paid off N」報告你把債還清了。弄壞主題檔看起來像修好了所有問題。
    //   放在 Pairs==0 之後、模式分支之前：兩道「這份結果不可信」的保險並排。
    if (strictPalette && detection.IsDegraded)
    {
        Console.WriteLine("exit 1: palette detection failed and --strict-palette is set");
        return 1;
    }

    // ⚠ 覆蓋率下限 —— 「0 組配對就擋」的自然推廣。二元的那條線擋不住實際發生的情況：
    //   八個公開專案裡有三個在 0.2% / 1.7% / 10.2% 的覆蓋率下亮綠燈（HandyControl
    //   342 個檔只解析出 7 組，然後印「all pairs meet AA」）。「幾乎什麼都沒看」
    //   與「看過了都沒問題」在退出碼上分不出來，是同一種謊報健康。
    //   與 --strict-palette 並排放在模式分支之前，baseline 模式一樣適用。
    if (result.Coverage * 100 < minCov)
    {
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"exit 1: only {result.Coverage * 100:F1}% of colour pairs could be resolved " +
            $"({result.Pairs} of {result.Pairs + result.Unresolved}), below the {minCov:F0}% floor — " +
            $"this result does not represent the project. See the unresolved breakdown above; " +
            $"pass --min-coverage 0 to proceed anyway."));
        return 1;
    }

    if (writeBaselinePath is not null)
    {
        // 導入模式第一步：把現況凍成已知債清單（存進 repo）。之後只擋新增與惡化。
        if (!TryWrite(writeBaselinePath, Baseline.Write(result), "baseline",
                      $" ({fail} known failure pair(s))")) return 2;
        return 0;
    }

    if (baselinePath is not null)
    {
        if (!File.Exists(baselinePath))
        {
            Console.Error.WriteLine($"baseline not found: {baselinePath}");
            return 2;
        }
        Baseline.ComparisonResult cmp;
        try { cmp = Baseline.Compare(result, File.ReadAllText(baselinePath)); }
        catch (Exception ex)
        {
            // 壞掉的 baseline 要有好訊息，不是裸拋堆疊
            Console.Error.WriteLine($"baseline is not valid ({baselinePath}): {ex.Message}");
            Console.Error.WriteLine("regenerate it with --write-baseline");
            return 2;
        }
        Console.WriteLine($"baseline: known debt {cmp.KnownDebt}, paid off {cmp.PaidDebt} (debt may only shrink)");
        foreach (var f in cmp.NewFailures)
            Console.WriteLine($"  NEW failure: {f.File}:{f.Line} {f.Element} fg={f.Fg} bg={f.Bg}");
        foreach (var e in cmp.WorsenedFailures)
            Console.WriteLine($"  WORSENED: {e.File} {e.Element} fg={e.Fg} bg={e.Bg} (now {e.Count} occurrence(s))");
        if (!cmp.Passes)
        {
            Console.WriteLine($"exit 1: {cmp.NewFailures.Count} new / {cmp.WorsenedFailures.Count} worsened failure(s) vs baseline");
            return 1;
        }
        if (failOnWarn && warn > 0)
        {
            Console.WriteLine($"exit 1: {warn} pair(s) below AA (--fail-on warn)");
            return 1;
        }
        Console.WriteLine("exit 0: no new or worsened failures vs baseline");
        return 0;
    }

    if (fail > 0)
    {
        Console.WriteLine($"exit 1: {fail} pair(s) below threshold x 2/3 ({warn} warn)");
        return 1;
    }
    if (failOnWarn && warn > 0)
    {
        Console.WriteLine($"exit 1: {warn} pair(s) below AA (--fail-on warn)");
        return 1;
    }
    if (warn > 0) Console.WriteLine($"exit 0: no fail, but {warn} pair(s) below AA");
    else Console.WriteLine("exit 0: all pairs meet AA");
    return 0;

}   // DecideExit

static void PrintUsage()
{
    Console.WriteLine("""
        XamlContrast — static WCAG contrast audit for XAML source

        usage: xamlcontrast <project-root> [options]

        options:
          --json <path>            write machine-readable report (summary + findings)
          --sarif <path>           write SARIF 2.1.0 (GitHub code scanning; fail/warn only)
          --md <path>              write a Markdown report (suitable for a PR comment)
          --fail-on warn           also fail the run when pairs are below AA but above 2/3
          --strict-palette         fail the run when palette detection degrades
          --min-coverage <0-100>   fail when fewer than N% of colour pairs could be
                                   resolved (default 50; a pass over a fraction of the
                                   project is not a pass). 0 disables.
          --baseline [path]        ratchet mode: only fail on NEW or WORSENED failures
                                   (default path: xamlcontrast-baseline.json)
          --write-baseline [path]  freeze current failures as known debt (run once, commit the file)
          --show-ok                list passing pairs, grouped
          --show-unresolved        list every pair the tool could NOT resolve, with
                                   file:line, reason and the offending value/key
        """);
}

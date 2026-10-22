using XamlContrast.Core;

// XamlContrast CLI — 靜態 XAML 對比度稽核，退出碼給 CI 當守門員。
//
// 退出碼契約（1.0 凍結；判定順序見 DecideExit 與 CLAUDE.md「退出碼契約與檢查順序」）：
//   0 通過 ／ 1 找到違規（低於 gate 的配對）／ 2 用法、設定或輸入檔錯誤 ／ 3 結果不可信
//   （0 組配對、--strict-palette 且色盤退化、覆蓋率低於下限 —— 「工具沒看到」與「看到了有問題」
//   在 CI 上的處置不同：前者要修設定，後者要修顏色。比照 Parity 的 exit 3）

var showOk = false;
var showUnresolved = false;
string? failOnFlag = null;   // null = 用 config／內建預設（warn）
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
                // ⚠ NaN 對任何比較都是 false：範圍檢查擋不住它，之後「覆蓋率 < NaN」也永遠不成立 →
                //   覆蓋率下限靜默失效。IsFinite 一併擋掉 Infinity
                !double.IsFinite(mc) || mc is < 0 or > 100)
            { Console.Error.WriteLine("--min-coverage takes a number between 0 and 100"); return 2; }
            minCoverage = mc; i++;
            break;
        case "--fail-on":
            if (i + 1 >= args.Length || args[i + 1] is not ("warn" or "fail"))
            { Console.Error.WriteLine("--fail-on takes 'warn' or 'fail'"); return 2; }
            failOnFlag = args[++i];
            break;
        case "--json":
            if (OutputPath(args, i, "--json") is not { } jp) return 2;
            jsonPath = jp; i++;
            break;
        case "--sarif":
            if (OutputPath(args, i, "--sarif") is not { } sp) return 2;
            sarifPath = sp; i++;
            break;
        case "--md":
            if (OutputPath(args, i, "--md") is not { } mp) return 2;
            mdPath = mp; i++;
            break;
        // 路徑必填（1.0）。⚠ 0.x 可省略（預設 xamlcontrast-baseline.json）：下一個字不以 - 開頭就被吃成路徑，
        //   `--baseline src/MyApp` 把 root 吃掉、root 落空，看起來只是 usage 錯誤
        case "--baseline":
            if (OutputPath(args, i, "--baseline") is not { } bp) return 2;
            baselinePath = bp; i++;
            break;
        case "--write-baseline":
            if (OutputPath(args, i, "--write-baseline") is not { } wbp) return 2;
            writeBaselinePath = wbp; i++;
            break;
        case "-h" or "--help":
            PrintUsage(Console.Out);
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

// 兩個 baseline 模式並用沒有意義（寫出新的已知債，同時又跟舊的比？）—— 0.x 是 --write-baseline 勝、--baseline 被靜默忽略
if (baselinePath is not null && writeBaselinePath is not null)
{
    Console.Error.WriteLine("--baseline and --write-baseline cannot be used together");
    return 2;
}

if (root is null)
{
    PrintUsage(Console.Error);
    // `--baseline src/MyApp`：可省略的路徑把 root 吃掉了 —— 舊版只印 usage，看不出發生了什麼
    foreach (var (flag, taken) in new[] { ("--baseline", baselinePath), ("--write-baseline", writeBaselinePath) })
        if (taken is not null && Directory.Exists(taken))
            Console.Error.WriteLine($"note: {flag} took '{taken}' as the baseline file — " +
                                    $"put <project-root> first, or give {flag} an explicit file path");
    return 2;
}
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
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // 列舉專案目錄時碰到沒有權限的子目錄之類 —— 個別檔案讀不到會跳過並列入 parse errors，
    // 這裡是連「有哪些檔」都拿不到。不能落到契約外的 .NET 堆疊
    Console.Error.WriteLine($"cannot read the project under {root}: {ex.Message}");
    return 2;
}

// rootBackground 寫錯是設定錯誤,不是靜默忽略 —— 設了一個不存在的鍵,
// 使用者以為地板鋪好了,實際上覆蓋率一點都沒回來(謊報健康的親戚)
if (config.RootBackground is not null)
{
    var rb = ColorResolver.Resolve(Auditor.NormalizeRootBackground(config.RootBackground), detection.Palette);
    if (rb is null || rb.Kind is ColorKind.UnknownKey or ColorKind.Other)
    {
        Console.Error.WriteLine($"config error: rootBackground '{config.RootBackground}' is not a colour or a key in the detected palette ({detection.Palette.Count} keys)");
        return 2;
    }
}

// 優先序：CLI 旗標 > config > 內建預設。
// gate 是真正的覆寫（1.0）：預設 warn（沒達到 AA 就算失敗），--fail-on fail 可以把 config 的 warn 放寬回「只擋 fail」。
// ⚠ 0.x 是「旗標只能加嚴」（flag || config）—— 預設改成 warn 之後，那會讓 --fail-on fail 永遠放不寬
var gateWarn = (failOnFlag ?? config.FailOn) == "warn";
strictPalette = strictPalette || config.StrictPalette;
// ⚠ 覆蓋率下限是唯一「旗標可以放寬 config」的項目：它擋的是「工具看不見你的專案」，
//    不是稽核標準。使用者明確傳 --min-coverage 0 表示「我知道，先讓我跑」是合理的逃生口。
var minCov = minCoverage ?? config.MinCoverage;

// baseline 檔是輸入：在稽核之前就驗（不存在、壞掉 → exit 2），排在「結果不可信」（3）之前 ——
// 輸入本身錯了，後面的判定都沒有意義
Baseline.Known? known = null;
if (baselinePath is not null)
{
    if (!File.Exists(baselinePath))
    {
        Console.Error.WriteLine($"baseline not found: {baselinePath}");
        return 2;
    }
    try { known = Baseline.Parse(File.ReadAllText(baselinePath)); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidOperationException or ArgumentException)
    {
        // 壞掉的 baseline 要有好訊息，不是裸拋堆疊
        Console.Error.WriteLine($"baseline is not valid ({baselinePath}): {ex.Message}");
        Console.Error.WriteLine("regenerate it with --write-baseline");
        return 2;
    }
}

AuditResult result;
try { result = Auditor.Run(root, detection, config); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"cannot read the project under {root}: {ex.Message}");
    return 2;
}

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

// --json／--sarif／--md／--baseline／--write-baseline 的值：必填，而且不能長得像旗標。舊版只檢查「後面還有沒有字」，
// `--json --strict-palette` 會寫出一個叫 --strict-palette 的檔、把保險旗標靜默吃掉。
// 以 - 開頭的檔名沒有正當用途（真的要就寫 ./-name）；「-」也不代表 stdout。
static string? OutputPath(string[] args, int i, string flag)
{
    if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) return args[i + 1];
    Console.Error.WriteLine(i + 1 >= args.Length
        ? $"{flag} takes a file path"
        : $"{flag} takes a file path, got '{args[i + 1]}' (an option?) — for a file name starting with '-', write ./{args[i + 1]}");
    return null;
}

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

    // ── 結果不可信 → 3。三道保險並排、擋在兩個 baseline 模式之前（見下面的地雷註記）──
    if (result.Pairs == 0)
    {
        // 0 組配對時「達標」無意義 —— 沒有任何可稽核的項目。空掃綠燈就是謊報健康。
        Console.WriteLine("exit 3: resolved 0 pairs — nothing was audited, this result does not mean the project passes");
        // 整個視窗沒有底（全是 no-ancestor-background）正是最需要 rootBackground 提示的情況 ——
        // 舊版這裡先 return，走不到下面覆蓋率那條的求救指引
        if (Report.CoverageHint(result) is { } zeroHint) Console.WriteLine(zeroHint);
        return 3;
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
        Console.WriteLine("exit 3: palette detection failed and --strict-palette is set");
        return 3;
    }

    // ⚠ 覆蓋率下限 —— 「0 組配對就擋」的自然推廣。二元的那條線擋不住實際發生的情況：
    //   八個公開專案裡有三個在 0.2% / 1.7% / 10.2% 的覆蓋率下亮綠燈（HandyControl
    //   342 個檔只解析出 7 組，然後印「all pairs meet AA」）。「幾乎什麼都沒看」
    //   與「看過了都沒問題」在退出碼上分不出來，是同一種謊報健康。
    //   與 --strict-palette 並排放在模式分支之前，baseline 模式一樣適用。
    //   比的是報告上那個數字（四捨五入到一位）：舊版拿未取整的值比，JSON 與 console 寫 66.7、退出碼卻說不到 66.7
    if (result.CoveragePercent < minCov)
    {
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"exit 3: only {result.CoveragePercent:F1}% of colour pairs could be resolved " +
            $"({result.Pairs} of {result.Pairs + result.Unresolved}), below the {minCov:F0}% floor — " +
            $"this result does not represent the project. See the unresolved breakdown above; " +
            $"pass --min-coverage 0 to proceed anyway."));

        // 求救指引（文字與觸發條件見 Report.CoverageHint）—— 使用者在這裡最可能放棄
        if (Report.CoverageHint(result) is { } hint) Console.WriteLine(hint);
        return 3;
    }

    if (writeBaselinePath is not null)
    {
        // 導入模式第一步：把現況凍成已知債清單（存進 repo）。之後只擋新增與惡化。
        // 收「低於 AA」的每一組（fail＋warn），不論 gate —— 之後換 gate 也不必重寫
        if (!TryWrite(writeBaselinePath, Baseline.Write(result), "baseline",
                      $" ({fail + warn} known pair(s) below AA)")) return 2;
        return 0;
    }

    if (known is not null)
    {
        var cmp = Baseline.Compare(result, known, gateWarn);
        Console.WriteLine($"baseline: known debt {cmp.KnownDebt}, paid off {cmp.PaidDebt} (debt may only shrink)");
        foreach (var w in cmp.Warnings) Console.WriteLine($"!! baseline: {w}"); // baseline 檔本身的問題不能靜默
        foreach (var f in cmp.NewFailures)
            Console.WriteLine($"  NEW failure: {f.File}:{f.Line} {f.Element} fg={f.Fg} bg={f.Bg}");
        foreach (var e in cmp.WorsenedFailures)
            Console.WriteLine($"  WORSENED: {e.File} {e.Element} fg={e.Fg} bg={e.Bg} (now {e.Count} occurrence(s))");
        if (!cmp.Passes)
        {
            Console.WriteLine($"exit 1: {cmp.NewFailures.Count} new / {cmp.WorsenedFailures.Count} worsened failure(s) vs baseline");
            return 1;
        }
        // warn 也在 ratchet 裡（baseline 收「低於 AA」的每一組）—— 0.x 在這裡另外對「所有」warn 套 --fail-on warn，
        // 既有的 warn 也擋，預設 gate 改成 warn 之後 baseline 就對 warn 完全沒用了
        Console.WriteLine("exit 0: no new or worsened failures vs baseline");
        return 0;
    }

    if (fail > 0)
    {
        // 係數印實際生效的 failFactor（config 可改）—— 舊版寫死「x 2/3」，改了 failFactor 之後
        // console 同時印 "fail-factor 0.9" 與 "x 2/3"；預設值本身也是 0.67 不是 2/3
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"exit 1: {fail} pair(s) below threshold x {config.Thresholds.FailFactor} ({warn} warn)"));
        return 1;
    }
    if (gateWarn && warn > 0)
    {
        Console.WriteLine($"exit 1: {warn} pair(s) below AA");
        return 1;
    }
    if (warn > 0) Console.WriteLine($"exit 0: no fail, but {warn} pair(s) below AA (--fail-on fail)");
    else Console.WriteLine("exit 0: all pairs meet AA");
    return 0;

}   // DecideExit

// --help → stdout、exit 0；用法錯誤 → stderr、exit 2（舊版兩者都印到 stdout）
static void PrintUsage(TextWriter w)
{
    w.WriteLine("""
        XamlContrast — static WCAG contrast audit for XAML source

        usage: xamlcontrast <project-root> [options]

        options:
          --json <path>            write machine-readable report (summary + findings)
          --sarif <path>           write SARIF 2.1.0 (GitHub code scanning; fail/warn only)
          --md <path>              write a Markdown report (suitable for a PR comment)
          --fail-on <warn|fail>    warn (default): exit 1 on any pair below AA; fail: only on
                                   pairs below the fail line (threshold x fail-factor,
                                   default 0.67). Overrides the config's failOn
          --strict-palette         fail the run when palette detection degrades
          --min-coverage <0-100>   fail when fewer than N% of colour pairs could be
                                   resolved (default 50; a pass over a fraction of the
                                   project is not a pass). 0 disables.
          --baseline <path>        ratchet mode: only fail on NEW or WORSENED pairs below the gate
          --write-baseline <path>  freeze every pair below AA as known debt (run once, commit the
                                   file); cannot be combined with --baseline
          --show-ok                list passing pairs, grouped
          --show-unresolved        list every pair the tool could NOT resolve, with
                                   file:line, reason and the offending value/key
          --version                print the tool version
          -h, --help               print this help

        exit codes: 0 pass; 1 pairs below the gate; 2 usage, config or input-file error;
                    3 the result cannot be trusted (0 pairs, degraded palette with
                    --strict-palette, coverage below --min-coverage)
        """);
}

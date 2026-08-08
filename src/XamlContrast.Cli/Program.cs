using XamlContrast.Core;

// XamlContrast CLI — 靜態 XAML 對比度稽核，退出碼給 CI 當守門員。
//
// 退出碼政策（規劃書 4.5）：
//   fail > 0                    → 1
//   --fail-on warn 且 warn > 0  → 1
//   配對數 0                    → 1（不可設定 —— 空掃綠燈就是謊報健康）
//   --strict-palette 且色盤退化 → 1

var showOk = false;
var failOnWarn = false;
var strictPalette = false;
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
        case "--strict-palette": strictPalette = true; break;
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

var result = Auditor.Run(root, detection, config);

Console.Write(Report.ToConsole(result, showOk));

if (jsonPath is not null)
{
    File.WriteAllText(jsonPath, Report.ToJson(result));
    Console.WriteLine($"json written: {jsonPath}");
}

if (sarifPath is not null)
{
    var ver = typeof(Program).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        is [System.Reflection.AssemblyInformationalVersionAttribute va, ..] ? va.InformationalVersion.Split('+')[0] : "0.0.0";
    File.WriteAllText(sarifPath, Report.ToSarif(result, ver));
    Console.WriteLine($"sarif written: {sarifPath}");
}

var fail = result.CountOf(Category.Fail);
var warn = result.CountOf(Category.Warn);

// 退出碼的判定抽成區域函式,因為 --md 的報告要標明 PASS/FAIL —— 得先知道結論才能寫檔。
// 判定邏輯與各分支的輸出一字未改,只是包了一層。
var exitCode = DecideExit();

if (mdPath is not null)
{
    File.WriteAllText(mdPath, Report.ToMarkdown(result, exitCode));
    Console.WriteLine($"markdown written: {mdPath}");
}

return exitCode;

int DecideExit()
{

    if (result.Pairs == 0)
    {
        // 0 組配對時「達標」無意義 —— 沒有任何可稽核的項目。空掃綠燈就是謊報健康。
        Console.WriteLine("exit 1: resolved 0 pairs — nothing was audited, this result does not mean the project passes");
        return 1;
    }

    if (writeBaselinePath is not null)
    {
        // 導入模式第一步：把現況凍成已知債清單（存進 repo）。之後只擋新增與惡化。
        File.WriteAllText(writeBaselinePath, Baseline.Write(result));
        Console.WriteLine($"baseline written: {writeBaselinePath} ({fail} known failure pair(s))");
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
    if (strictPalette && detection.IsDegraded)
    {
        Console.WriteLine("exit 1: palette detection failed and --strict-palette is set");
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
          --baseline [path]        ratchet mode: only fail on NEW or WORSENED failures
                                   (default path: xamlcontrast-baseline.json)
          --write-baseline [path]  freeze current failures as known debt (run once, commit the file)
          --show-ok                list passing pairs, grouped
        """);
}

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
        case "--baseline":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--baseline takes a file path"); return 2; }
            baselinePath = args[++i];
            break;
        case "--write-baseline":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--write-baseline takes a file path"); return 2; }
            writeBaselinePath = args[++i];
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

var detection = PaletteDetector.Detect(root);
var result = Auditor.Run(root, detection);

Console.Write(Report.ToConsole(result, showOk));

if (jsonPath is not null)
{
    File.WriteAllText(jsonPath, Report.ToJson(result));
    Console.WriteLine($"json written: {jsonPath}");
}

var fail = result.CountOf(Category.Fail);
var warn = result.CountOf(Category.Warn);

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
    var cmp = Baseline.Compare(result, File.ReadAllText(baselinePath));
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

static void PrintUsage()
{
    Console.WriteLine("""
        XamlContrast — static WCAG contrast audit for XAML source

        usage: xamlcontrast <project-root> [options]

        options:
          --json <path>            write machine-readable report (summary + findings)
          --fail-on warn           also fail the run when pairs are below AA but above 2/3
          --strict-palette         fail the run when palette detection degrades
          --baseline <path>        ratchet mode: only fail on NEW or WORSENED failures
          --write-baseline <path>  freeze current failures as known debt (run once, commit the file)
          --show-ok                list passing pairs, grouped
        """);
}

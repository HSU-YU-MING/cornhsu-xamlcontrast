# M2 驗收：C# 版在四個真實專案上必須重現 M0 基準線的數字（語意比對，不比位元組）。
#
# 期望值抽自 prototype/baseline-*.txt（2026-07-30，死 setter 過濾之後）。
# 「新實作必須重現這些數字，否則就是移植過程掉了東西。」
#
# 用法：powershell -ExecutionPolicy Bypass -File .\scripts\verify-baselines.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$cli = Join-Path $repo 'src\XamlContrast.Cli'

$expected = @{
    kindling = @{
        Root = 'D:\應用程式\Kindling\Kindling.WPF'
        files = 15; pairs = 134; unresolved = 0; skipped = 2; deadForeground = 0
        ok = 130; fail = 1; warn = 0; decorative = 3
        paletteMode = 'csharp'; exit = 1
        failLines = @('TimelineView.xaml:368')
    }
    quillnest = @{
        Root = 'D:\應用程式\QuillNest\QuillNest'
        files = 30; pairs = 702; unresolved = 55; skipped = 9; deadForeground = 4
        ok = 696; fail = 2; warn = 0; decorative = 4
        paletteMode = 'pair'; exit = 1
        failLines = @('ProjectView.xaml:326', 'AppLabelManagerDialog.xaml:240')
    }
    celflow = @{
        Root = 'D:\應用程式\CelFlow\frontend\CelFlow.WPF'
        files = 22; pairs = 308; unresolved = 4; skipped = 15; deadForeground = 0
        ok = 283; fail = 3; warn = 2; decorative = 20
        paletteMode = 'single'; exit = 1
        failLines = @('CrashRecoveryDialog.xaml:18', 'TimelineView.xaml:542', 'AiPersonalizationView.xaml:109')
    }
    cornea = @{
        Root = 'D:\應用程式\Cornea'
        files = 5; pairs = 0; unresolved = 1; skipped = 2; deadForeground = 0
        ok = 0; fail = 0; warn = 0; decorative = 0
        paletteMode = 'none'
        # 0 組配對 → exit 1 是 4.5 輸出合約的預設（原型時代是 exit 0＋警告）
        exit = 1
        failLines = @()
    }
}

$anyFail = $false
foreach ($name in @('kindling', 'quillnest', 'celflow', 'cornea')) {
    $e = $expected[$name]
    $json = Join-Path $env:TEMP "xamlcontrast-verify-$name.json"
    dotnet run --project $cli -c Release -- $e.Root --json $json *> $null
    $exit = $LASTEXITCODE
    $r = Get-Content $json -Raw | ConvertFrom-Json
    $s = $r.summary

    $checks = [ordered]@{
        files = @($e.files, $s.files)
        pairs = @($e.pairs, $s.pairs)
        unresolved = @($e.unresolved, $s.unresolved)
        skipped = @($e.skipped, $s.skipped)
        deadForeground = @($e.deadForeground, $s.deadForeground)
        ok = @($e.ok, $s.counts.ok)
        fail = @($e.fail, $s.counts.fail)
        warn = @($e.warn, $s.counts.warn)
        decorative = @($e.decorative, $s.counts.decorative)
        paletteMode = @($e.paletteMode, $s.paletteMode)
        exitCode = @($e.exit, $exit)
    }
    $bad = @()
    foreach ($k in $checks.Keys) {
        $exp = $checks[$k][0]; $got = $checks[$k][1]
        if ("$exp" -ne "$got") { $bad += "$k 期望 $exp 實得 $got" }
    }
    $gotFails = @($r.findings | Where-Object category -eq 'fail' | ForEach-Object { "$($_.file):$($_.line)" })
    foreach ($fl in $e.failLines) {
        if ($fl -notin $gotFails) { $bad += "缺少 fail: $fl" }
    }
    foreach ($gf in $gotFails) {
        if ($gf -notin $e.failLines) { $bad += "多出 fail: $gf" }
    }

    if ($bad.Count -gt 0) {
        $anyFail = $true
        Write-Host "[$name] 不一致：" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host "[$name] 與基準線一致（$($s.pairs) 組配對，fail $($s.counts.fail)）" -ForegroundColor Green
    }
}

if ($anyFail) { Write-Host "驗收失敗" -ForegroundColor Red; exit 1 }
Write-Host "M2 驗收通過：四個專案的數字全部與 M0 基準線一致" -ForegroundColor Green
exit 0

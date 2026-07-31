# M2 驗收（持續有效）：C# 版在四個真實專案上必須重現原型基準線的數字。
# 期望值直接從 prototype/baseline-*.txt 解析 —— 基準線更新時本腳本自動跟上，
# 不會出現「腳本裡的硬編數字」與基準線各說各話的第二真相源。
#
# 比對項目：檔案數／配對數／無法解析／跳過／死 setter 排除／停用態豁免／
# 各分級數／「破」的每一筆 file:line。
#
# 用法：powershell -ExecutionPolicy Bypass -File .\scripts\verify-baselines.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$cli = Join-Path $repo 'src\XamlContrast.Cli'

$projects = @{
    kindling  = 'D:\應用程式\Kindling\Kindling.WPF'
    quillnest = 'D:\應用程式\QuillNest\QuillNest'
    celflow   = 'D:\應用程式\CelFlow\frontend\CelFlow.WPF'
    cornea    = 'D:\應用程式\Cornea'
}

function Parse-Baseline([string]$path) {
    $t = Get-Content $path -Raw
    $e = @{ files=0; pairs=0; unresolved=0; skipped=0; deadfg=0; disabled=0
            ok=0; fail=0; warn=0; decorative=0; failLines=@() }
    if ($t -match '檔案 (\d+) 個｜解析出 (\d+) 組') { $e.files=[int]$Matches[1]; $e.pairs=[int]$Matches[2] }
    if ($t -match '無法解析（[^）]*）: (\d+) 處｜跳過（[^）]*）: (\d+) 處') { $e.unresolved=[int]$Matches[1]; $e.skipped=[int]$Matches[2] }
    if ($t -match '已排除 (\d+) 組 Style 配對') { $e.deadfg=[int]$Matches[1] }
    if ($t -match '已豁免 (\d+) 組停用態配對') { $e.disabled=[int]$Matches[1] }
    if ($t -match '(?m)^\s+OK\s+(\d+) 組') { $e.ok=[int]$Matches[1] }
    if ($t -match '(?m)^\s+破\s+(\d+) 組') { $e.fail=[int]$Matches[1] }
    if ($t -match '(?m)^\s+偏低\s+(\d+) 組') { $e.warn=[int]$Matches[1] }
    if ($t -match '(?m)^\s+裝飾\s+(\d+) 組') { $e.decorative=[int]$Matches[1] }
    # 「破」區塊的每一筆 file:line
    if ($t -match '(?s)===== 破 [^\r\n]*=====\r?\n(.*?)(\r?\n\r?\n|$)') {
        $e.failLines = @([regex]::Matches($Matches[1], '(?m)^(\S+\.xaml:\d+)') | ForEach-Object { $_.Groups[1].Value })
    }
    return $e
}

$anyFail = $false
foreach ($name in @('kindling', 'quillnest', 'celflow', 'cornea')) {
    $e = Parse-Baseline (Join-Path $repo "prototype\baseline-$name.txt")
    $json = Join-Path $env:TEMP "xamlcontrast-verify-$name.json"
    dotnet run --project $cli -c Release -- $projects[$name] --json $json *> $null
    $exit = $LASTEXITCODE
    $r = Get-Content $json -Raw | ConvertFrom-Json
    $s = $r.summary

    # 退出碼合約：破>0 → 1；配對 0 → 1（空掃綠燈）；否則 0
    $expExit = if ($e.fail -gt 0 -or $e.pairs -eq 0) { 1 } else { 0 }

    $checks = [ordered]@{
        files = @($e.files, $s.files);            pairs = @($e.pairs, $s.pairs)
        unresolved = @($e.unresolved, $s.unresolved); skipped = @($e.skipped, $s.skipped)
        deadForeground = @($e.deadfg, $s.deadForeground)
        disabledExempt = @($e.disabled, $s.disabledExempt)
        ok = @($e.ok, $s.counts.ok);              fail = @($e.fail, $s.counts.fail)
        warn = @($e.warn, $s.counts.warn);        decorative = @($e.decorative, $s.counts.decorative)
        exitCode = @($expExit, $exit)
    }
    $bad = @()
    foreach ($k in $checks.Keys) {
        if ("$($checks[$k][0])" -ne "$($checks[$k][1])") { $bad += "$k 期望 $($checks[$k][0]) 實得 $($checks[$k][1])" }
    }
    $gotFails = @($r.findings | Where-Object category -eq 'fail' | ForEach-Object { "$($_.file):$($_.line)" })
    foreach ($fl in $e.failLines) { if ($fl -notin $gotFails) { $bad += "缺少 fail: $fl" } }
    foreach ($gf in $gotFails) { if ($gf -notin $e.failLines) { $bad += "多出 fail: $gf" } }

    if ($bad.Count -gt 0) {
        $anyFail = $true
        Write-Host "[$name] 不一致：" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host "[$name] 一致（$($s.pairs) 組配對，fail $($s.counts.fail)，豁免 $($s.disabledExempt)）" -ForegroundColor Green
    }
}

if ($anyFail) { Write-Host "驗收失敗" -ForegroundColor Red; exit 1 }
Write-Host "驗收通過：四個專案的數字全部與原型基準線一致" -ForegroundColor Green
exit 0

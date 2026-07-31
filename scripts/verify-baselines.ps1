# 驗收（C# 快照回歸模式）
#
# 原型已於 v0.1.0 凍結（治理決定，規劃書 M5 節）：規則 13 起 C# 是權威實作，
# 期望值改為 baselines/<name>.json —— C# 自己產的快照，比對「這次跑」與「上次凍結」。
# prototype/baseline-*.txt 保留為凍結原型的歷史規格，不再是驗收來源。
#
# 用法：
#   powershell -File .\scripts\verify-baselines.ps1           # 驗收（比對快照）
#   powershell -File .\scripts\verify-baselines.ps1 -Update   # 重新凍結快照
#                                                              （行為變更後用；diff 要人工檢視再 commit）
#
# 比對項目：summary 全部計數器 + 每筆 fail 的 file:line + 退出碼。

param([switch]$Update)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$cli = Join-Path $repo 'src\XamlContrast.Cli'
$snapDir = Join-Path $repo 'baselines'
New-Item -ItemType Directory -Force $snapDir | Out-Null

$projects = @{
    kindling  = 'D:\應用程式\Kindling\Kindling.WPF'
    quillnest = 'D:\應用程式\QuillNest\QuillNest'
    celflow   = 'D:\應用程式\CelFlow\frontend\CelFlow.WPF'
    cornea    = 'D:\應用程式\Cornea'
}

$anyFail = $false
foreach ($name in @('kindling', 'quillnest', 'celflow', 'cornea')) {
    $json = Join-Path $env:TEMP "xamlcontrast-verify-$name.json"
    dotnet run --project $cli -c Release -- $projects[$name] --json $json *> $null
    $exit = $LASTEXITCODE
    $snapPath = Join-Path $snapDir "$name.json"

    if ($Update) {
        Copy-Item $json $snapPath -Force
        $s = (Get-Content $json -Raw | ConvertFrom-Json).summary
        Write-Host "[$name] 快照已更新（$($s.pairs) 組，fail $($s.counts.fail)，exit=$exit）" -ForegroundColor Cyan
        continue
    }

    if (-not (Test-Path $snapPath)) {
        Write-Host "[$name] 沒有快照 —— 先跑 -Update 凍結" -ForegroundColor Red
        $anyFail = $true; continue
    }

    $cur = Get-Content $json -Raw | ConvertFrom-Json
    $exp = Get-Content $snapPath -Raw | ConvertFrom-Json
    $s = $cur.summary; $e = $exp.summary
    $expExit = if ($e.counts.fail -gt 0 -or $e.pairs -eq 0) { 1 } else { 0 }

    $checks = [ordered]@{
        files = @($e.files, $s.files);                    pairs = @($e.pairs, $s.pairs)
        unresolved = @($e.unresolved, $s.unresolved);     skipped = @($e.skipped, $s.skipped)
        deadForeground = @($e.deadForeground, $s.deadForeground)
        disabledExempt = @($e.disabledExempt, $s.disabledExempt)
        suppressed = @($e.suppressed, $s.suppressed)
        ok = @($e.counts.ok, $s.counts.ok);               fail = @($e.counts.fail, $s.counts.fail)
        warn = @($e.counts.warn, $s.counts.warn);         decorative = @($e.counts.decorative, $s.counts.decorative)
        exitCode = @($expExit, $exit)
    }
    $bad = @()
    foreach ($k in $checks.Keys) {
        if ("$($checks[$k][0])" -ne "$($checks[$k][1])") { $bad += "$k 期望 $($checks[$k][0]) 實得 $($checks[$k][1])" }
    }
    $expFails = @($exp.findings | Where-Object category -eq 'fail' | ForEach-Object { "$($_.file):$($_.line)" })
    $gotFails = @($cur.findings | Where-Object category -eq 'fail' | ForEach-Object { "$($_.file):$($_.line)" })
    foreach ($fl in $expFails) { if ($fl -notin $gotFails) { $bad += "缺少 fail: $fl" } }
    foreach ($gf in $gotFails) { if ($gf -notin $expFails) { $bad += "多出 fail: $gf" } }

    if ($bad.Count -gt 0) {
        $anyFail = $true
        Write-Host "[$name] 與快照不一致：" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host "[$name] 一致（$($s.pairs) 組配對，fail $($s.counts.fail)，豁免 $($s.disabledExempt)）" -ForegroundColor Green
    }
}

if ($Update) { Write-Host "快照凍結完成 —— 檢視 git diff baselines/ 後 commit" -ForegroundColor Cyan; exit 0 }
if ($anyFail) { Write-Host "驗收失敗：行為變了 —— 若為刻意，人工檢視後跑 -Update 重新凍結" -ForegroundColor Red; exit 1 }
Write-Host "驗收通過：四個專案與凍結快照一致" -ForegroundColor Green
exit 0

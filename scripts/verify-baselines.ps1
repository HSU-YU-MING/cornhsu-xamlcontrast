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
#
# ⚠ 絕對不要為了讓它變綠就跑 -Update：快照不一致 = 行為變了。先逐項說明每一個差異是哪個 commit／決定造成的，
#   給使用者看過、同意之後才重新凍結。
#
# 舊快照（schemaVersion 5，0.x 寫的）照樣讀得懂：比對的欄位（summary 的計數器、findings 的 category／file／line）
# 在 6 都還在。差別只在退出碼的期望 —— 見下面 $expExit。

param([switch]$Update)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$cli = Join-Path $repo 'src\XamlContrast.Cli'
$snapDir = Join-Path $repo 'baselines'
New-Item -ItemType Directory -Force $snapDir | Out-Null

$projects = @{
    kindling  = 'D:\應用程式\Kindling\Kindling.WPF'
    quillnest = 'D:\應用程式\QuillNest\QuillNest'
    # CelFlow 2026-08-28 改名 ZeroCel（資料夾、專案都改了，舊路徑不再存在）。
    # 快照名稱沿用 celflow.json：它記錄的是同一個 App，改名不是行為變更
    celflow   = 'D:\應用程式\ZeroCel\frontend\ZeroCel.WPF'
    cornea    = 'D:\應用程式\Cornea'
}

$anyFail = $false
foreach ($name in @('kindling', 'quillnest', 'celflow', 'cornea')) {
    # 受測專案不在（改名、搬家、沒 clone）→ 紅字點名、繼續比對下一個。
    # ⚠ 之前直接把不存在的路徑丟給 CLI：它照契約 exit 2、stderr 印 root not found，
    #   而 Windows PowerShell 5.1 在 $ErrorActionPreference='Stop' 下把原生指令的 stderr 變成終止錯誤，
    #   整支腳本在那裡中斷 —— 後面的專案從頭到尾沒比對，也沒有任何一行提示
    if (-not (Test-Path $projects[$name])) {
        Write-Host "[$name] 找不到受測專案：$($projects[$name]) —— 未比對（改名或搬家了就更新這支腳本的路徑）" -ForegroundColor Red
        $anyFail = $true; continue
    }
    $json = Join-Path $env:TEMP "xamlcontrast-verify-$name.json"
    if (Test-Path $json) { Remove-Item $json -Force }
    # 原生指令的輸出（含 stderr）丟掉、只看退出碼；這一段放寬 ErrorActionPreference，
    # 否則 5.1 會把 stderr 的每一行當成終止錯誤
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & dotnet run --project $cli -c Release -- $projects[$name] --json $json 2>&1 | Out-Null
    $exit = $LASTEXITCODE
    $ErrorActionPreference = $prev
    $snapPath = Join-Path $snapDir "$name.json"
    if (-not (Test-Path $json)) {
        Write-Host "[$name] 稽核沒有產出 JSON（exit=$exit）—— 先手動跑一次看錯誤訊息" -ForegroundColor Red
        $anyFail = $true; continue
    }

    if ($Update) {
        Copy-Item $json $snapPath -Force
        $s = (Get-Content $json -Raw -Encoding UTF8 | ConvertFrom-Json).summary
        Write-Host "[$name] 快照已更新（$($s.pairs) 組，fail $($s.counts.fail)，exit=$exit）" -ForegroundColor Cyan
        continue
    }

    if (-not (Test-Path $snapPath)) {
        Write-Host "[$name] 沒有快照 —— 先跑 -Update 凍結" -ForegroundColor Red
        $anyFail = $true; continue
    }

    $cur = Get-Content $json -Raw -Encoding UTF8 | ConvertFrom-Json
    $exp = Get-Content $snapPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $s = $cur.summary; $e = $exp.summary
    # 期望的退出碼：快照有 exitCode（schemaVersion 6 起，CLI 寫進 JSON）就用它。
    # 舊快照沒有，依 1.0 的規則從快照的計數推：0 組配對或覆蓋率低於下限 → 3；有 fail、或 gate 是 warn（預設）而有 warn → 1；否則 0。
    # ⚠ 0.x 的推法是「fail > 0 或 0 組 → 1」—— 1.0 拆出 exit 3、預設 gate 改成 warn 之後就不對了
    if ($null -ne $exp.exitCode) {
        $expExit = [int]$exp.exitCode
    } else {
        $total = $e.pairs + $e.unresolved
        $cov = if ($total -eq 0) { 0 } else { [math]::Round($e.pairs * 100.0 / $total, 1, [MidpointRounding]::AwayFromZero) }
        $minCov = if ($null -ne $e.minCoverage) { $e.minCoverage } else { 50 }
        $gateWarn = $e.failOn -ne 'fail'
        $expExit = if ($e.pairs -eq 0 -or $cov -lt $minCov) { 3 }
                   elseif ($e.counts.fail -gt 0 -or ($gateWarn -and $e.counts.warn -gt 0)) { 1 }
                   else { 0 }
        Write-Host "[$name] 快照是 schemaVersion $($exp.schemaVersion)（沒有 exitCode）—— 退出碼期望依 1.0 規則從快照的計數推得：$expExit" -ForegroundColor DarkYellow
    }

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

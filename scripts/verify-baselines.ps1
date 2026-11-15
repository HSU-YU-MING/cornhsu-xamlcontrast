# 驗收（C# 快照回歸模式）
#
# 原型已於 v0.1.0 凍結（治理決定，規劃書 M5 節）：規則 13 起 C# 是權威實作，
# 期望值改為 baselines/<name>.json —— C# 自己產的快照，比對「這次跑」與「上次凍結」。
# prototype/baseline-*.txt 保留為凍結原型的歷史規格，不再是驗收來源。
#
# 用法：
#   powershell -File .\scripts\verify-baselines.ps1           # 驗收（比對快照 + 對象指紋）
#   powershell -File .\scripts\verify-baselines.ps1 -Update   # 重新凍結快照與指紋
#                                                              （行為變更後用；diff 要人工檢視再 commit）
#   powershell -File .\scripts\verify-baselines.ps1 -UpdateFingerprint
#                                                             # 只補指紋、不動快照（數字一致時才寫）
#
# 比對項目：summary 全部計數器 + 每筆 fail 的 file:line + 退出碼。
#
# ⚠ 絕對不要為了讓它變綠就跑 -Update：快照不一致 = 行為變了。先逐項說明每一個差異是哪個 commit／決定造成的，
#   給使用者看過、同意之後才重新凍結。
#
# 受測對象的指紋（baselines/<name>.project.txt）
# ---------------------------------------------
# 這支腳本有一個結構性弱點：受測的四個專案活在 repo 外面、會自己演化，所以紅燈分不出
# 「工具行為變了」還是「被稽核的 App 改了」。實際發生過 —— celflow.json 從 2026-08-26 就是紅的
# （pairs 期望 363 實得 353），原因是 CelFlow.WPF 在 2026-08-18 改過四個 XAML 檔，不是工具的回歸，
# 但要人逐項比對才看得出來，中間紅了兩個月。
#
# 所以 -Update 現在會在快照旁邊寫一份指紋：受測專案的路徑、git commit、以及每個稽核輸入檔
# （*.xaml、*.cs、root 的 config）的 SHA-256 前 16 碼。驗收時重算一次再比，腳本自己就說得出
# 「對象沒變 → 差異來自工具」還是「對象也變了 → 這些檔案動過，先看它們」。
#
# 為什麼不塞進 JSON 報告：那是凍結的對外介面，而且每個使用者的 report.json 都要背著幾百筆
# 檔案雜湊。這份只是維護者的守門資料，所以留在 baselines/、用純文字 —— git diff 直接看得出
# 哪個檔動了，不必再跑一次腳本。
#
# 指紋不一致本身不算驗收失敗（對象更新是正常的事），但一定會印出來，而且會改寫結論那一行：
# 不會再出現「一致」卻其實是對著另一個版本的 App 比的情況。
#
# 舊快照（schemaVersion 5，0.x 寫的）照樣讀得懂：比對的欄位（summary 的計數器、findings 的 category／file／line）
# 在 6 都還在。差別只在退出碼的期望 —— 見下面 $expExit。

param([switch]$Update, [switch]$UpdateFingerprint)

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

# ── 受測對象的指紋 ──────────────────────────────────────────────────────────────

function Get-FingerprintPath { param([string]$Name) Join-Path $snapDir "$Name.project.txt" }

<#
 稽核輸入檔的雜湊表（相對路徑 → SHA-256 前 16 碼）。
 ⚠ 收的檔案要跟工具自己讀的一致：*.xaml、*.cs（C# 色盤與轉換器）、root 的 config，
   排除規則照 PaletteDetector.IsExcluded —— bin／obj 與點開頭的**目錄段**，不分大小寫。
   收多了（例如 bin 底下的複本）會讓指紋每次建置都變；收少了則漏掉真正改動的檔。
#>
function Get-AuditInputs {
    param([string]$Root)
    $full = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    $table = @{}
    $cand = @(Get-ChildItem -LiteralPath $full -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq '.xaml' -or $_.Extension -eq '.cs' })
    $cfg = Join-Path $full 'xamlcontrast.config.json'
    if (Test-Path -LiteralPath $cfg) { $cand += @(Get-Item -LiteralPath $cfg) }
    foreach ($f in $cand) {
        $rel = $f.FullName.Substring($full.Length).TrimStart('\', '/')
        $seg = @($rel -split '[\\/]')
        # 最後一段是檔名，只看目錄段。⚠ $seg.Count 為 1（檔案就在 root）時不能取
        #   $seg[0..($seg.Count - 2)] —— PowerShell 的 0..-1 會回 @(第一個, 最後一個)，
        #   等於拿檔名去比 bin／obj
        if ($seg.Count -gt 1) {
            $skip = $false
            foreach ($d in $seg[0..($seg.Count - 2)]) {
                if ($d.StartsWith('.') -or $d -ieq 'bin' -or $d -ieq 'obj') { $skip = $true; break }
            }
            if ($skip) { continue }
        }
        $table[($rel -replace '\\', '/')] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
    }
    return $table
}

<#
 受測專案的 git 狀態。⚠ git 會往 stderr 寫（不是 repo、detached…），而 5.1 在
 ErrorActionPreference='Stop' 下把原生指令的 stderr 當終止錯誤 —— 這裡要放寬。
#>
function Get-GitInfo {
    param([string]$Root)
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $commit = (& git -C $Root rev-parse HEAD 2>$null | Select-Object -First 1)
    $repoTop = (& git -C $Root rev-parse --show-toplevel 2>$null | Select-Object -First 1)
    $dirty = @(& git -C $Root status --porcelain 2>$null).Count
    $ErrorActionPreference = $prev
    if (-not $commit) { return @{ commit = '(不是 git 工作目錄)'; repo = ''; dirty = 0 } }
    return @{ commit = "$commit".Trim(); repo = "$repoTop".Trim(); dirty = $dirty }
}

function Write-Fingerprint {
    param([string]$Path, [string]$Project)
    $full = (Resolve-Path -LiteralPath $Project).Path.TrimEnd('\', '/')
    $git = Get-GitInfo $full
    $inputs = Get-AuditInputs $full
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# XamlContrast 受測對象指紋 —— 由 scripts/verify-baselines.ps1 -Update 產生，不要手改。')
    $lines.Add('# 旁邊那份快照是對這個版本的專案凍結的。驗收時重算一次再比：對象沒變 → 差異來自工具本身；')
    $lines.Add('# 對象也變了 → 先看下面哪些檔案動過（快照紅燈曾經兩個月都是 App 自己改了，不是工具回歸）。')
    $lines.Add("project: $full")
    $lines.Add("frozen: $(Get-Date -Format 'yyyy-MM-dd')")
    $lines.Add("git-repo: $($git.repo)")
    $lines.Add("git-commit: $($git.commit)")
    $lines.Add("git-dirty: $($git.dirty)")
    $lines.Add("inputs: $($inputs.Count)")
    $lines.Add('# sha256(前 16 碼)  專案內相對路徑')
    $keys = [string[]]@($inputs.Keys)
    [Array]::Sort($keys, [StringComparer]::OrdinalIgnoreCase)
    foreach ($k in $keys) { $lines.Add("$($inputs[$k])  $k") }
    # ⚠ 固定寫 LF、無 BOM：這支腳本 Windows PowerShell 與 pwsh 都跑得到，
    #   用 [Environment]::NewLine 或 Set-Content -Encoding utf8 會讓同一份指紋在兩邊長得不一樣、
    #   每次 -Update 都冒出整檔的假 diff（跟統計預覽那一條地雷同一個形狀）
    [IO.File]::WriteAllText($Path, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
    return @{ git = $git; inputs = $inputs }
}

function Read-Fingerprint {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $header = @{}; $files = @{}
    foreach ($line in [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)) {
        if ($line -match '^\s*#' -or $line.Trim() -eq '') { continue }
        if ($line -match '^([0-9A-Fa-f]{16})\s\s(.+)$') { $files[$matches[2]] = $matches[1]; continue }
        if ($line -match '^([a-z-]+):\s*(.*)$') { $header[$matches[1]] = $matches[2] }
    }
    return @{ header = $header; files = $files }
}

<# 指紋差異的人話清單（空陣列 = 對象沒變）。#>
function Compare-Fingerprint {
    param($Old, [string]$Project, $Git, $Inputs)
    $notes = New-Object System.Collections.Generic.List[string]
    if ($Old.header['project'] -and $Old.header['project'] -ne $Project) {
        $notes.Add("專案路徑：$($Old.header['project']) → $Project")
    }
    if ($Old.header['git-commit'] -and $Old.header['git-commit'] -ne $Git.commit) {
        $o = $Old.header['git-commit']; $n = $Git.commit
        $notes.Add("git commit：$($o.Substring(0, [Math]::Min(9, $o.Length))) → $($n.Substring(0, [Math]::Min(9, $n.Length)))")
    }
    $added = @(); $removed = @(); $changed = @()
    foreach ($k in $Inputs.Keys) {
        if (-not $Old.files.ContainsKey($k)) { $added += $k }
        elseif ($Old.files[$k] -ne $Inputs[$k]) { $changed += $k }
    }
    foreach ($k in $Old.files.Keys) { if (-not $Inputs.ContainsKey($k)) { $removed += $k } }
    foreach ($pair in @(@('改過', $changed), @('新增', $added), @('刪除', $removed))) {
        $label = $pair[0]; $list = @($pair[1] | Sort-Object)
        if ($list.Count -eq 0) { continue }
        $notes.Add("$label $($list.Count) 個輸入檔：" + (($list | Select-Object -First 8) -join '、') +
            $(if ($list.Count -gt 8) { "…（還有 $($list.Count - 8) 個）" } else { '' }))
    }
    return $notes
}

$anyFail = $false
$drifted = 0
foreach ($name in @('kindling', 'quillnest', 'celflow', 'cornea')) {
    # 受測專案不在（改名、搬家、沒 clone）→ 紅字點名、繼續比對下一個。
    # ⚠ 之前直接把不存在的路徑丟給 CLI：它照契約 exit 2、stderr 印 root not found，
    #   而 Windows PowerShell 5.1 在 $ErrorActionPreference='Stop' 下把原生指令的 stderr 變成終止錯誤，
    #   整支腳本在那裡中斷 —— 後面的專案從頭到尾沒比對，也沒有任何一行提示
    if (-not (Test-Path $projects[$name])) {
        Write-Host "[$name] 找不到受測專案：$($projects[$name]) —— 未比對（改名或搬家了就更新這支腳本的路徑）" -ForegroundColor Red
        # 指紋還在的話，至少說得出那份快照當初是對什麼凍結的 —— 專案不在本機時，
        # 這是唯一還能看的線索（例如 celflow：ZeroCel 2026-11 已不在這台機器上）
        $gone = Read-Fingerprint (Get-FingerprintPath $name)
        if ($gone) {
            Write-Host "    快照凍結時的對象：$($gone.header['project'])（$($gone.header['frozen'])，git $($gone.header['git-commit'])，$($gone.header['inputs']) 個輸入檔）" -ForegroundColor DarkYellow
        }
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
        $fp = Write-Fingerprint (Get-FingerprintPath $name) $projects[$name]
        $s = (Get-Content $json -Raw -Encoding UTF8 | ConvertFrom-Json).summary
        Write-Host "[$name] 快照已更新（$($s.pairs) 組，fail $($s.counts.fail)，exit=$exit）" -ForegroundColor Cyan
        Write-Host "    對象指紋：git $($fp.git.commit.Substring(0, [Math]::Min(9, $fp.git.commit.Length)))$(if ($fp.git.dirty -gt 0) { "（工作目錄有 $($fp.git.dirty) 處未提交）" })，$($fp.inputs.Count) 個輸入檔" -ForegroundColor Cyan
        continue
    }

    if (-not (Test-Path $snapPath)) {
        Write-Host "[$name] 沒有快照 —— 先跑 -Update 凍結" -ForegroundColor Red
        $anyFail = $true; continue
    }

    # 對象指紋：先算這次的，再跟快照旁邊那份比。沒有指紋的快照（1.4.3 之前凍的）只提示，不算失敗
    $fpNotes = @()
    $oldFp = Read-Fingerprint (Get-FingerprintPath $name)
    if ($null -eq $oldFp) {
        Write-Host "[$name] 這份快照沒有對象指紋（下次 -Update 會補上）—— 在那之前，紅燈仍分不出是工具還是 App 變了" -ForegroundColor DarkYellow
    } else {
        $curGit = Get-GitInfo (Resolve-Path -LiteralPath $projects[$name]).Path
        $curInputs = Get-AuditInputs $projects[$name]
        $fpNotes = @(Compare-Fingerprint $oldFp (Resolve-Path -LiteralPath $projects[$name]).Path.TrimEnd('\', '/') $curGit $curInputs)
        if ($fpNotes.Count -gt 0) {
            $drifted++
            Write-Host "[$name] 受測對象自己變了（快照凍於 $($oldFp.header['frozen'])）：" -ForegroundColor DarkYellow
            $fpNotes | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
        }
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
        # 這兩句是這份指紋存在的理由：紅燈的歸因方向不一樣，處理方式也不一樣
        if ($fpNotes.Count -gt 0) {
            Write-Host "    ⚠ 對象也變了（上面列出）—— 這些差異不一定是工具的回歸，先逐項歸因再決定要不要 -Update" -ForegroundColor Red
        } elseif ($null -ne $oldFp) {
            Write-Host "    對象沒變（git commit 與每個輸入檔的雜湊都一樣）—— 差異來自工具本身：回歸，或是這次刻意的改動" -ForegroundColor Red
        }
    } elseif ($fpNotes.Count -gt 0) {
        # 數字一致但對象變了：不能單說「一致」—— 那會讓人以為是對同一個版本的 App 比的
        Write-Host "[$name] 數字一致（$($s.pairs) 組配對，fail $($s.counts.fail)，豁免 $($s.disabledExempt)），但對象已更新 —— 指紋待重凍（-UpdateFingerprint）" -ForegroundColor DarkYellow
    } else {
        Write-Host "[$name] 一致（$($s.pairs) 組配對，fail $($s.counts.fail)，豁免 $($s.disabledExempt)）" -ForegroundColor Green
    }

    # -UpdateFingerprint：只補指紋、不動快照。
    # ⚠ 只在數字一致時才寫 —— 數字不一致表示這份快照對現在的 App 已經不成立，
    #   補指紋會把「對著舊版 App 凍的數字」蓋章成「對著新版 App 驗過」，正好是這份指紋要防的謊。
    if ($UpdateFingerprint) {
        if ($bad.Count -gt 0) {
            Write-Host "    指紋未更新：數字還不一致 —— 先逐項歸因，確認新數字才是對的之後跑 -Update（它會同時重凍快照與指紋）" -ForegroundColor Red
        } else {
            $fp = Write-Fingerprint (Get-FingerprintPath $name) $projects[$name]
            Write-Host "    指紋已更新：git $($fp.git.commit.Substring(0, [Math]::Min(9, $fp.git.commit.Length)))，$($fp.inputs.Count) 個輸入檔" -ForegroundColor Cyan
        }
    }
}

if ($Update) { Write-Host "快照凍結完成 —— 檢視 git diff baselines/ 後 commit（*.project.txt 是對象指紋，一起 commit）" -ForegroundColor Cyan; exit 0 }
if ($drifted -gt 0) { Write-Host "有 $drifted 個受測對象自己也變了 —— 對象更新是正常的，但歸因時要先把它們的影響扣掉" -ForegroundColor DarkYellow }
if ($anyFail) { Write-Host "驗收失敗：行為變了 —— 若為刻意，人工檢視後跑 -Update 重新凍結" -ForegroundColor Red; exit 1 }
if ($drifted -gt 0) { Write-Host "驗收通過：四個專案的計數與凍結快照一致（其中 $drifted 個對象已更新，指紋待重凍）" -ForegroundColor Green; exit 0 }
Write-Host "驗收通過：四個專案與凍結快照一致，對象也沒變" -ForegroundColor Green
exit 0

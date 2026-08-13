# README 的示範輸出把關
#
# 為什麼需要這支：README 的「實際跑起來長這樣」是新使用者看到的第一個東西，
# 而它是手貼的 —— 0.5 加了合併式色盤偵測、0.6 加了覆蓋率那行之後，那段整整
# 漂了兩個版本沒人發現：色盤那行印的已經不是 README 上寫的字串，覆蓋率那行
# 根本不存在。示範輸出對不上實際輸出，是「謊報健康」的文件版。
#
# 比對前會正規化：空白行拿掉、連續空白收成一個。所以欄寬為了排版收窄沒關係，
# 但「多一行、少一行、字不一樣、數字不一樣」一律紅燈。
#
# 用法：
#   powershell -File .\scripts\verify-readme-sample.ps1           # 驗收
#   powershell -File .\scripts\verify-readme-sample.ps1 -Update   # 用實跑結果覆寫 README 區塊
#                                                                  （diff 要人工檢視再 commit）

param([switch]$Update)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
# 正斜線：這支跟 verify-baselines.ps1 不同，CI 會在 Linux 上跑（pwsh），
# 反斜線在那裡是檔名的一部分而不是分隔線
$cli = Join-Path $repo 'src/XamlContrast.Cli'
$demo = Join-Path $repo 'samples/demo'

# 各語版 README 裡那個區塊的定位錨點（標題之後的第一個 ``` 圍籬）
$targets = @(
    @{ File = 'README.md';         Heading = '## What it looks like' }
    @{ File = 'README.zh-Hant.md'; Heading = '## 實際跑起來長這樣' }
)

# ── 實跑一次，拿到權威輸出 ──────────────────────────────────────────────
# demo 是刻意做壞的，exit 1 才是正確行為；這裡只取 stdout，不看退出碼
$actual = & dotnet run --project $cli -c Release -- $demo 2>&1 | ForEach-Object { "$_" }
if (-not $actual -or $actual.Count -lt 5) {
    Write-Host "跑不出示範輸出 —— 先確認 dotnet build 過得去" -ForegroundColor Red
    exit 1
}

function Normalize([string[]]$lines) {
    $out = @()
    foreach ($l in $lines) {
        $t = ($l -replace '\s+', ' ').Trim()
        if ($t.Length -gt 0) { $out += $t }
    }
    return $out
}

$expected = Normalize $actual
$anyFail = $false

foreach ($t in $targets) {
    $path = Join-Path $repo $t.File
    # 明確指定 UTF8：Windows PowerShell 的 Get-Content 預設用系統 ANSI 讀，
    # 中文版 README（UTF-8 無 BOM）會整份變亂碼，連標題錨點都對不上
    $lines = @(Get-Content $path -Encoding UTF8)

    $hIdx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq $t.Heading) { $hIdx = $i; break }
    }
    if ($hIdx -lt 0) {
        Write-Host "[$($t.File)] 找不到標題「$($t.Heading)」—— 錨點被改掉了，這支守門員需要跟著更新" -ForegroundColor Red
        $anyFail = $true; continue
    }

    $open = -1; $close = -1
    for ($i = $hIdx + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].TrimEnd() -eq '```') {
            if ($open -lt 0) { $open = $i } else { $close = $i; break }
        }
    }
    if ($open -lt 0 -or $close -lt 0) {
        Write-Host "[$($t.File)] 標題之後找不到完整的 ``` 區塊" -ForegroundColor Red
        $anyFail = $true; continue
    }

    if ($Update) {
        $new = @()
        if ($open -gt 0) { $new += $lines[0..($open)] }
        $new += $actual
        $new += $lines[$close..($lines.Count - 1)]
        # 不用 Set-Content -Encoding utf8：Windows PowerShell 會塞 BOM、pwsh 不會，
        # 同一支腳本在兩邊跑會讓 README 無謂地變動。明確指定「UTF-8 無 BOM」。
        [System.IO.File]::WriteAllLines($path, $new, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "[$($t.File)] 區塊已用實跑結果覆寫（$($actual.Count) 行）" -ForegroundColor Cyan
        continue
    }

    $documented = Normalize @($lines[($open + 1)..($close - 1)])

    $bad = @()
    $max = [Math]::Max($documented.Count, $expected.Count)
    for ($i = 0; $i -lt $max; $i++) {
        $d = ''; $e = ''
        if ($i -lt $documented.Count) { $d = $documented[$i] }
        if ($i -lt $expected.Count) { $e = $expected[$i] }
        if ($d -ne $e) {
            if ($d -eq '') { $bad += "第 $($i + 1) 行：README 少了「$e」" }
            elseif ($e -eq '') { $bad += "第 $($i + 1) 行：README 多了「$d」" }
            else { $bad += "第 $($i + 1) 行：`n        README 寫  $d`n        實際印出  $e" }
        }
    }

    if ($bad.Count -gt 0) {
        $anyFail = $true
        Write-Host "[$($t.File)] 示範輸出與實際不符：" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host "[$($t.File)] 示範輸出與實際一致（$($expected.Count) 行）" -ForegroundColor Green
    }
}

if ($Update) { Write-Host "覆寫完成 —— 檢視 git diff README*.md 後 commit" -ForegroundColor Cyan; exit 0 }
if ($anyFail) {
    Write-Host "把關失敗：README 的示範輸出已經不是使用者會拿到的東西 —— 跑 -Update 重貼" -ForegroundColor Red
    exit 1
}
Write-Host "把關通過：兩份 README 的示範輸出都等於實際輸出" -ForegroundColor Green
exit 0

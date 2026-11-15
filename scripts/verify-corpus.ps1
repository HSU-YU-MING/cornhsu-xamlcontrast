<#
.SYNOPSIS
  公開語料回歸：八個公開 WPF 專案 + WinUI Gallery，逐筆比對 findings 與 unresolvedSites。

.DESCRIPTION
  verify-baselines.ps1 守的是四個自家專案 —— 它們全部是 WPF 應用程式，測不到控制項函式庫、
  WinUI、ThemeDictionaries、建置時產生的主題檔那幾類形狀。那幾類的回歸以前靠「改完記得手動
  跑一輪外部比對」，而「記得」不是機制：1.4 那輪（2026-11-10）比完之後，結果只留在 CHANGELOG 的
  散文裡，下一次改規則沒有任何東西會紅。

  這支腳本把那一輪變成常設守門：語料清單（baselines/corpus/corpus.txt）釘住每個專案的 commit，
  摘要（baselines/corpus/<name>.txt）凍住每一筆 finding 與 unresolved 的位置。
  語料釘死 + 摘要凍結 = 紅燈只有一個可能的原因：工具自己變了。

  摘要是純文字、一行一筆，所以 `git diff baselines/corpus/` 直接看得出是哪一筆動了 ——
  跟 verify-baselines.ps1 的對象指紋同一個設計：守門員要說得出下一步，不是只說「不一致」。

.PARAMETER Update
  用目前的輸出重凍摘要（行為刻意變了之後用）。⚠ 不要為了變綠就跑：先逐筆說明差異是哪個改動
  造成的，確認新行為才是對的，再重凍並把 diff 一起 commit。

.PARAMETER Repin
  把語料的釘子移到遠端現在的 HEAD（刻意更新語料）。會直接改 corpus.txt，之後必須連帶 -Update，
  而那一次的摘要 diff 混著「語料變了」與「工具變了」—— 所以兩件事不要同時做。

.PARAMETER CorpusDir
  clone 放哪裡。預設 $HOME/.xamlcontrast-corpus，或環境變數 XAMLCONTRAST_CORPUS。
  只 sparse checkout *.xaml。

.PARAMETER Only
  只跑其中幾個（除錯用），例如 -Only wpfui,winuigallery。

.EXAMPLE
  powershell -File .\scripts\verify-corpus.ps1
  powershell -File .\scripts\verify-corpus.ps1 -Update
#>
param(
    [switch]$Update,
    [switch]$Repin,
    [string]$CorpusDir,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$digestDir = Join-Path $repo 'baselines/corpus'
$manifestPath = Join-Path $digestDir 'corpus.txt'
if (-not $CorpusDir) {
    $CorpusDir = if ($env:XAMLCONTRAST_CORPUS) { $env:XAMLCONTRAST_CORPUS } else { Join-Path $HOME '.xamlcontrast-corpus' }
}
# 語料是別人的專案，不要替他們送統計（兩個 workflow 也是這樣設的）
$env:DO_NOT_TRACK = '1'

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# ── git（stderr 不要變成終止錯誤）────────────────────────────────────────────────
function Invoke-Git {
    param([string[]]$GitArgs, [string]$Cwd)
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    if ($Cwd) { $out = & git -C $Cwd @GitArgs 2>&1 } else { $out = & git @GitArgs 2>&1 }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($code -ne 0) { throw "git $($GitArgs -join ' ') 失敗（exit $code）：$(($out | Select-Object -Last 3) -join ' / ')" }
    return $out
}

function Read-Manifest {
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "找不到語料清單：$manifestPath" }
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($line in [IO.File]::ReadAllLines($manifestPath, [Text.Encoding]::UTF8)) {
        if ($line -match '^\s*#' -or $line.Trim() -eq '') { continue }
        $parts = $line -split '\|'
        if ($parts.Count -lt 3) { throw "語料清單這一行看不懂（要 `名稱 | repo | commit`）：$line" }
        $list.Add([pscustomobject]@{ Name = $parts[0].Trim(); Repo = $parts[1].Trim(); Commit = $parts[2].Trim() })
    }
    return $list
}

<# 只改一行的釘子，其他行（含註解）原樣保留 —— 清單裡的說明比釘子值錢。 #>
function Set-Pin {
    param([string]$Name, [string]$Commit)
    $lines = [IO.File]::ReadAllLines($manifestPath, [Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*#' -or $lines[$i].Trim() -eq '') { continue }
        $parts = $lines[$i] -split '\|'
        if ($parts.Count -ge 3 -and $parts[0].Trim() -eq $Name) {
            $lines[$i] = "$($parts[0].Trim()) | $($parts[1].Trim()) | $Commit"
        }
    }
    [IO.File]::WriteAllText($manifestPath, ($lines -join "`n") + "`n", $utf8NoBom)
}

function Sync-Clone {
    param([string]$Name, [string]$Repo, [string]$Commit)
    $dir = Join-Path $CorpusDir $Name
    if (-not (Test-Path -LiteralPath (Join-Path $dir '.git'))) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        Invoke-Git @('init', '-q') $dir
        Invoke-Git @('remote', 'add', 'origin', $Repo) $dir
        # blob:none + sparse：只下載真的要 checkout 的 *.xaml。沒有這兩個，
        # Playnite／ILSpy 這種專案一個就幾百 MB，而我們只讀 XAML
        Invoke-Git @('config', 'extensions.partialClone', 'origin') $dir
        Invoke-Git @('config', 'remote.origin.partialclonefilter', 'blob:none') $dir
        Invoke-Git @('sparse-checkout', 'set', '--no-cone', '*.xaml') $dir
    }
    $head = ''
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $head = "$(& git -C $dir rev-parse HEAD 2>$null | Select-Object -First 1)".Trim()
    $ErrorActionPreference = $prev
    if ($head -ne $Commit) {
        Invoke-Git @('fetch', '-q', '--depth', '1', '--filter=blob:none', 'origin', $Commit) $dir
        Invoke-Git @('checkout', '-q', '--detach', 'FETCH_HEAD') $dir
    }
    return $dir
}

# ── 摘要 ───────────────────────────────────────────────────────────────────────
function Format-Ratio { param($V) if ($null -eq $V) { '-' } else { ('{0:0.00}' -f [double]$V) } }

<#
 摘要的內容 = 這次跑的全部可觀察結果，壓成一行一筆的純文字：
 計數器（header）+ 每一筆 finding + 每一筆 unresolved。
 ⚠ 刻意連 unresolvedSites 都逐筆記：1.4 那輪比對抓到的大宗變化正是 unresolved 的流向
 （unknown-palette-key 355 → 21），光看總數看不出是「解析出來了」還是「換了個原因」。
#>
function Build-Digest {
    param([string]$Name, $Entry, $Report, [int]$ExitCode, [string]$ToolCommit)
    $s = $Report.summary
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# XamlContrast 公開語料摘要 —— 由 scripts/verify-corpus.ps1 -Update 產生，不要手改。')
    $lines.Add('# 語料釘死（corpus.txt 的 commit）+ 這份凍結 = 紅燈只有一個可能：工具自己變了。')
    $lines.Add("project: $Name")
    $lines.Add("repo: $($Entry.Repo)")
    $lines.Add("commit: $($Entry.Commit)")
    # tool-commit 不列入比對（每個 commit 都會變），只是「這份是誰凍的」的紀錄
    $lines.Add("tool-commit: $ToolCommit")
    $lines.Add("frozen: $(Get-Date -Format 'yyyy-MM-dd')")
    $lines.Add("exit: $ExitCode")
    $lines.Add("palette-mode: $($s.paletteMode)")
    $lines.Add("palette-degraded: $($s.paletteDegraded)")
    $lines.Add("palette-keys: $($s.paletteKeys)")
    $lines.Add("single-theme: $($s.singleTheme)")
    $lines.Add("files: $($s.files)")
    $lines.Add("pairs: $($s.pairs)")
    $lines.Add("coverage: $($s.coverage)")
    $lines.Add("unresolved: $($s.unresolved)")
    $lines.Add("skipped: $($s.skipped)")
    $lines.Add("dead-foreground: $($s.deadForeground)")
    $lines.Add("disabled-exempt: $($s.disabledExempt)")
    $lines.Add("converter-pairs: $($s.converterPairs)")
    $lines.Add("ui-component-pairs: $($s.uiComponentPairs)")
    $lines.Add("parse-errors: $($s.parseErrors)")
    $lines.Add("suppressed: $($s.suppressed)")
    # 1.5：VisualStateManager 的狀態顏色（明文盲區的計數器）—— 語料裡最有代表性的那一類，
    # 自家四個專案一處都沒有（0），公開專案動輒數十處
    $lines.Add("state-colors: $($s.stateColors)")
    $lines.Add("fail: $($s.counts.fail)")
    $lines.Add("warn: $($s.counts.warn)")
    $lines.Add("ok: $($s.counts.ok)")
    $lines.Add("decorative: $($s.counts.decorative)")
    foreach ($prop in @(@('unresolved-by', $s.unresolvedBy), @('unknown-keys', $s.unknownKeys))) {
        $map = $prop[1]
        if ($null -eq $map) { continue }
        $names = @($map.PSObject.Properties.Name | Sort-Object)
        foreach ($k in $names) { $lines.Add("$($prop[0]).$($k): $($map.$k)") }
    }
    $f = New-Object System.Collections.Generic.List[string]
    foreach ($x in @($Report.findings)) {
        $f.Add(("F  {0}  {1}:{2}  {3}  {4} on {5}  {6}/{7}  {8}" -f
            $x.category, $x.file, $x.line, $x.element, $x.fg, $x.bg,
            (Format-Ratio $x.ratioDark), (Format-Ratio $x.ratioLight), $x.symmetry))
    }
    $u = New-Object System.Collections.Generic.List[string]
    foreach ($x in @($Report.unresolvedSites)) {
        $u.Add(("U  {0}  {1}:{2}  {3}" -f $x.reason, $x.file, $x.line, $x.value))
    }
    $fa = [string[]]$f.ToArray(); [Array]::Sort($fa, [StringComparer]::Ordinal)
    $ua = [string[]]$u.ToArray(); [Array]::Sort($ua, [StringComparer]::Ordinal)
    $lines.Add('# F  分類  檔案:行  元素  字 on 底  深/淺比值  對稱')
    foreach ($l in $fa) { $lines.Add($l) }
    $lines.Add('# U  原因  檔案:行  值')
    foreach ($l in $ua) { $lines.Add($l) }
    return $lines
}

function Read-Digest {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $header = @{}; $rows = New-Object System.Collections.Generic.List[string]
    foreach ($line in [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)) {
        if ($line -match '^\s*#' -or $line.Trim() -eq '') { continue }
        if ($line -match '^(F|U)\s\s') { $rows.Add($line); continue }
        if ($line -match '^([a-z][a-z0-9.\-]*):\s*(.*)$') { $header[$matches[1]] = $matches[2] }
    }
    return @{ header = $header; rows = $rows }
}

function Compare-Digest {
    param($Old, $New)
    $diffs = New-Object System.Collections.Generic.List[string]
    $newH = @{}; $newRows = New-Object System.Collections.Generic.List[string]
    foreach ($line in $New) {
        if ($line -match '^\s*#' -or $line.Trim() -eq '') { continue }
        if ($line -match '^(F|U)\s\s') { $newRows.Add($line); continue }
        if ($line -match '^([a-z][a-z0-9.\-]*):\s*(.*)$') { $newH[$matches[1]] = $matches[2] }
    }
    # tool-commit／frozen 是紀錄，不是行為
    $skip = @('tool-commit', 'frozen')
    foreach ($k in @(@($Old.header.Keys) + @($newH.Keys) | Sort-Object -Unique)) {
        if ($skip -contains $k) { continue }
        $o = $Old.header[$k]; $n = $newH[$k]
        if ("$o" -ne "$n") { $diffs.Add("$k 期望 $(if ($null -eq $o) { '(沒有這一項)' } else { $o }) 實得 $(if ($null -eq $n) { '(沒有這一項)' } else { $n })") }
    }
    $oldSet = @{}; foreach ($r in $Old.rows) { $oldSet[$r] = $true }
    $newSet = @{}; foreach ($r in $newRows) { $newSet[$r] = $true }
    $gone = @($Old.rows | Where-Object { -not $newSet.ContainsKey($_) })
    $added = @($newRows | Where-Object { -not $oldSet.ContainsKey($_) })
    foreach ($pair in @(@('少了', $gone), @('多了', $added))) {
        $list = @($pair[1])
        if ($list.Count -eq 0) { continue }
        $diffs.Add("$($pair[0]) $($list.Count) 筆：")
        foreach ($l in ($list | Select-Object -First 10)) { $diffs.Add("      $l") }
        if ($list.Count -gt 10) { $diffs.Add("      …（還有 $($list.Count - 10) 筆，看 git diff）") }
    }
    return $diffs
}

# ── 主流程 ─────────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force $digestDir | Out-Null
New-Item -ItemType Directory -Force $CorpusDir | Out-Null

Write-Host "語料目錄：$CorpusDir" -ForegroundColor DarkGray
dotnet build (Join-Path $repo 'src/XamlContrast.Cli/XamlContrast.Cli.csproj') -c Release -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'build 失敗' }
$dll = Get-ChildItem -Path (Join-Path $repo 'src/XamlContrast.Cli/bin/Release') -Recurse -Filter 'xamlcontrast.dll' |
    Select-Object -First 1
if (-not $dll) { throw '找不到建好的 xamlcontrast.dll' }
$toolCommit = "$(& git -C $repo rev-parse --short HEAD 2>$null)".Trim()
if (@(& git -C $repo status --porcelain 2>$null).Count -gt 0) { $toolCommit += '+dirty' }

$entries = Read-Manifest
# ⚠ `powershell -File` 沒辦法傳陣列：-Only a,b 會整串變成一個字串，比對一個都不中、
#   然後腳本說「清單是空的」—— 訊息還會誤導成清單壞了。所以這裡自己拆逗號
if ($Only) {
    $names = @($Only -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $entries = @($entries | Where-Object { $names -contains $_.Name })
    if ($entries.Count -eq 0) { throw "-Only 的名稱都不在語料清單裡：$($names -join '、')" }
}
if ($entries.Count -eq 0) { throw '語料清單是空的（或 -Only 沒對上任何名稱）' }

$anyFail = $false
$repinned = 0
foreach ($e in $entries) {
    $digestPath = Join-Path $digestDir "$($e.Name).txt"
    # 釘子：HEAD（還沒釘）或 -Repin（刻意移動）時去問遠端現在是哪一顆
    if ($e.Commit -eq 'HEAD' -or $Repin) {
        $remote = "$(Invoke-Git @('ls-remote', $e.Repo, 'HEAD') | Select-Object -First 1)".Split("`t")[0].Trim()
        if (-not $remote) { throw "[$($e.Name)] 問不到遠端的 HEAD" }
        if ($remote -ne $e.Commit) {
            Write-Host "[$($e.Name)] 釘子：$($e.Commit) → $remote" -ForegroundColor Cyan
            Set-Pin $e.Name $remote
            $e.Commit = $remote
            $repinned++
        }
    }

    try { $dir = Sync-Clone $e.Name $e.Repo $e.Commit }
    catch {
        Write-Host "[$($e.Name)] 取不到語料：$($_.Exception.Message)" -ForegroundColor Red
        $anyFail = $true; continue
    }

    $json = Join-Path ([IO.Path]::GetTempPath()) "xamlcontrast-corpus-$($e.Name).json"
    if (Test-Path -LiteralPath $json) { Remove-Item -LiteralPath $json -Force }
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & dotnet $dll.FullName $dir --json $json 2>&1 | Out-Null
    $exit = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if (-not (Test-Path -LiteralPath $json)) {
        Write-Host "[$($e.Name)] 稽核沒有產出 JSON（exit=$exit）—— 手動跑一次看訊息" -ForegroundColor Red
        $anyFail = $true; continue
    }
    $report = Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json
    $digest = Build-Digest $e.Name $e $report $exit $toolCommit

    if ($Update -or $Repin) {
        [IO.File]::WriteAllText($digestPath, (($digest -join "`n") + "`n"), $utf8NoBom)
        Write-Host "[$($e.Name)] 摘要已凍結（$($report.summary.files) 檔，$($report.summary.pairs) 組，fail $($report.summary.counts.fail)，unresolved $($report.summary.unresolved)，exit=$exit）" -ForegroundColor Cyan
        continue
    }

    $old = Read-Digest $digestPath
    if ($null -eq $old) {
        Write-Host "[$($e.Name)] 還沒有摘要 —— 先跑 -Update 凍結" -ForegroundColor Red
        $anyFail = $true; continue
    }
    if ($old.header['commit'] -ne $e.Commit) {
        # 清單被改過但摘要沒重凍：比下去只會得到一堆分不清來源的差異
        Write-Host "[$($e.Name)] 語料的釘子（$($e.Commit)）跟摘要凍的（$($old.header['commit'])）不一樣 —— 先 -Update 重凍，差異算在語料更新上" -ForegroundColor Red
        $anyFail = $true; continue
    }
    $diffs = Compare-Digest $old $digest
    if ($diffs.Count -gt 0) {
        $anyFail = $true
        Write-Host "[$($e.Name)] 與摘要不一致（語料釘在 $($e.Commit.Substring(0, 9))，所以差異來自工具）：" -ForegroundColor Red
        $diffs | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    } else {
        Write-Host "[$($e.Name)] 一致（$($report.summary.files) 檔，$($report.summary.pairs) 組，fail $($report.summary.counts.fail)，unresolved $($report.summary.unresolved)）" -ForegroundColor Green
    }
}

if ($repinned -gt 0) { Write-Host "有 $repinned 個語料的釘子移動了 —— corpus.txt 的 diff 要一起 commit" -ForegroundColor Cyan }
if ($Update -or $Repin) { Write-Host "摘要凍結完成 —— 檢視 git diff baselines/corpus/ 後 commit" -ForegroundColor Cyan; exit 0 }
if ($anyFail) { Write-Host "語料回歸失敗：工具在別人的專案上行為變了 —— 逐筆確認是改進還是回歸，刻意的才跑 -Update" -ForegroundColor Red; exit 1 }
Write-Host "語料回歸通過：$($entries.Count) 個公開專案逐筆一致" -ForegroundColor Green
exit 0

# XAML 樹解析式對比度稽核
#
# 跟前一版（上下 8 行視窗）的差別：這支真的解析 XAML 樹。
# 對每個有文字色的元素，沿著父節點往上找「最近一個真正生效的背景色」，
# 再把兩邊的資源鍵各自代入深色 / 淺色兩套值，算 WCAG 對比度。
#
# 處理的情況：
#   - 元素屬性上的 Background / Foreground（含祖先繼承）
#   - Background="Transparent" 視為穿透，繼續往上找
#   - ControlTemplate 內部自成一個子樹（模板根的背景管到模板內的文字）
#   - <Style> 裡同時有 Background 與 Foreground 兩個 Setter → 視為一組配對
#   - <Trigger> / <DataTrigger> 內的 Setter：以所屬 Style 的另一半為對照
#   - Style 自帶 ControlTemplate 且模板內無 Foreground 消費者
#     （ContentPresenter/ContentControl/TextBlock/AccessText/Label 皆無、
#     也沒有 TemplateBinding Foreground）→ Style 層 Foreground 是死 setter，
#     整組排除並計數回報（QuillNest DataGridCheckBoxStyle 誤報案例）
#   - 元素套用的具名／行內 Style（Style="{StaticResource X}"、<X.Style> BasedOn）：
#     跨檔案索引具名 Style，解析 BasedOn 鏈＋Style/模板觸發器，
#     元素的字色配上 Style 來源的底色逐狀態檢（2026-07-31 CelFlow 實證：
#     刪除鈕字 RedL 實際疊的是 DarkBtn 的 Bg3、hover Bg4，舊版樹走訪看不到
#     Style 的底色而誤落到祖先卡片 Bg2 低估實況；CrashRecoveryDialog「忽略」鈕
#     Fg1 疊 Border0/Border1 則整組漏掉）
#   - IsEnabled=False 的觸發態＝停用態：WCAG 1.4.3 豁免，計數回報、不評分
#     （CelFlow CrashRecoveryDialog 停用態 Fg2 疊 Bg3 是刻意設計，
#     曾被報成「破 2.16」的假警報）
#   - 半透明「色票」（值為 #AARRGGBB 的 palette 鍵）同樣疊底合成：
#     之前只有字面 8 碼會合成，色票鍵被 Lum() 剝掉 alpha 當不透明色
#     （CelFlow 實證：InfoL 疊 BlueOverlay 被算成疊純藍的假「破 2.68」）
#
# 明確不處理（會標成「無法解析」而不是猜）：
#   - 只有單邊 Setter 的 Style（套用位置由型別決定，不在樹上）
#   - 隱含樣式（只有 TargetType、無 x:Key 的全域 Style）——要模擬 WPF 的
#     隱含樣式查找，且會把「繼承的 Foreground」整個拉進來，雜訊量級不同，留給 .NET 版
#   - TemplateBinding / Binding 來的顏色
#   - 屬性元素語法 <Border.Background><LinearGradientBrush .../></Border.Background>

param(
    [Parameter(Mandatory)][string]$Root,
    # M1：不給 -Theme 就自動偵測色盤。寫死清單保留當對照組（驗證自動偵測用），之後砍掉。
    [ValidateSet('kindling','quillnest','celflow','cornea')][string]$Theme,
    [switch]$ShowOk
)

Add-Type -AssemblyName System.Xml.Linq

# ── 色卡本：直接從專案讀，不要用寫死的複本 ──
#
# ⚠ 這支原本把色盤寫死在腳本裡，結果專案色票改了、工具還在用舊快照，
#   新增的色票也被當成「不認識的鍵」直接跳過 —— 稽核結果會靜默失真。
#   現在改成每次執行都從真實檔案解析。

function Read-QuillNestPalette {
    # 兩份字典的格式：<Color x:Key="XColor">#RRGGBB</Color>
    # 再由 <SolidColorBrush x:Key="XBrush" Color="{StaticResource XColor}"/> 引用。
    # 這裡把 Brush 鍵直接對應到它引用的 Color 值。
    param([string]$Dir)
    $result = @{}
    $colors = @{ dark = @{}; light = @{} }
    foreach ($t in @(@('dark','DarkTheme.xaml'), @('light','LightTheme.xaml'))) {
        $path = Join-Path $Dir $t[1]
        foreach ($line in Get-Content $path) {
            if ($line -match '<Color x:Key="(?<k>\w+)">(?<v>#[0-9A-Fa-f]{6,8})</Color>') {
                $colors[$t[0]][$Matches['k']] = $Matches['v'].ToUpper()
            }
        }
    }
    foreach ($line in Get-Content (Join-Path $Dir 'DarkTheme.xaml')) {
        if ($line -match '<SolidColorBrush x:Key="(?<b>\w+)"\s+Color="\{StaticResource (?<c>\w+)\}"') {
            $b = $Matches['b']; $c = $Matches['c']
            if ($colors.dark.ContainsKey($c) -and $colors.light.ContainsKey($c)) {
                $result[$b] = "$($colors.dark[$c]),$($colors.light[$c])"
            }
        }
    }
    return $result
}

function Read-CelFlowPalette {
    # CelFlow 只有深色一份字典(Styles\DarkTheme.xaml),格式與 QuillNest 相同:
    #   <Color x:Key="XColor">#RRGGBB</Color>
    #   <SolidColorBrush x:Key="X" Color="{StaticResource XColor}"/>
    # 沒有淺色版,所以深淺兩欄填同一個值 —— 稽核時「兩邊一致」的判定會自動
    # 把所有色票歸為對稱,不會誤報成「淺色模式破版」。
    param([string]$Path)
    $result = @{}
    $colors = @{}
    foreach ($line in Get-Content $Path) {
        if ($line -match '<Color x:Key="(?<k>\w+)">(?<v>#[0-9A-Fa-f]{6,8})</Color>') {
            $colors[$Matches['k']] = $Matches['v'].ToUpper()
        }
    }
    foreach ($line in Get-Content $Path) {
        if ($line -match '<SolidColorBrush x:Key="(?<b>\w+)"\s+Color="\{StaticResource (?<c>\w+)\}"') {
            $b = $Matches['b']; $c = $Matches['c']
            if ($colors.ContainsKey($c)) { $result[$b] = "$($colors[$c]),$($colors[$c])" }
        }
    }
    return $result
}

function Read-KindlingPalette {
    # 真相源是 SystemThemeService.cs 裡的 ("Key", "#dark", "#light") 陣列
    param([string]$ServicePath)
    $result = @{}
    foreach ($line in Get-Content $ServicePath) {
        if ($line -match '\("(?<k>\w+)"\s*,\s*"(?<d>#[0-9A-Fa-f]{6,8})"\s*,\s*"(?<l>#[0-9A-Fa-f]{6,8})"\)') {
            $result[$Matches['k']] = "$($Matches['d'].ToUpper()),$($Matches['l'].ToUpper())"
        }
    }
    return $result
}

# ── M1 色盤自動偵測 ──
#
# 2026-07-30 用四個真實專案探測出來的四種形狀，決定了偵測順序：
#   QuillNest：Dark+Light 兩檔配對（鍵集重疊）            → 配對模式
#   Kindling ：單一 Dark XAML（只有深色值）＋ C# 三元組    → C# 勝出
#   CelFlow  ：單一 Dark XAML，無配對無 C#                → 單一主題模式
#   Cornea   ：什麼都沒有                                 → 退回只算寫死色碼，喊出來
#
# ⚠ Kindling 那格是關鍵教訓：它的 Themes\DarkTheme.xaml 有 33 個 brush 但只有深色值
#   （真相源是 C# 陣列，執行期換值）。天真地選 XAML 檔，淺色值就全是錯的。
#   規則：**提供兩套主題的來源，優先於只有一套的。**

function Get-XamlPaletteCandidates([string]$Dir) {
    # 掃所有 ResourceDictionary，找「像色盤的檔案」：brush 定義 ≥ 3 個
    $cands = @()
    foreach ($f in Get-ChildItem $Dir -Filter '*.xaml' -Recurse |
             Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }) {
        $colors = @{}; $brushes = @{}; $refs = @{}
        foreach ($line in Get-Content $f.FullName) {
            if ($line -match '<Color x:Key="(?<k>\w+)">(?<v>#[0-9A-Fa-f]{6,8})</Color>') {
                $colors[$Matches['k']] = $Matches['v'].ToUpper()
            }
            elseif ($line -match '<SolidColorBrush x:Key="(?<k>\w+)"\s+Color="(?<v>#[0-9A-Fa-f]{6,8})"') {
                $brushes[$Matches['k']] = $Matches['v'].ToUpper()
            }
            elseif ($line -match '<SolidColorBrush x:Key="(?<k>\w+)"\s+Color="\{StaticResource (?<c>\w+)\}"') {
                $refs[$Matches['k']] = $Matches['c']
            }
        }
        # brush 引用同檔的 Color 定義 → 解成實際色值
        foreach ($k in @($refs.Keys)) {
            if ($colors.ContainsKey($refs[$k])) { $brushes[$k] = $colors[$refs[$k]] }
        }
        if ($brushes.Count -ge 3) {
            # ⚠ 提示只看「專案內的相對路徑」。看完整路徑的話，專案放在
            #   D:\dark-projects\ 底下會讓所有候選都染上 dark 提示、配對整個失效
            #   —— 2026-07-30 回頭檢視時抓到的，四個驗證專案路徑剛好無此字樣所以沒踩到。
            $rel = $f.FullName.Substring($Dir.Length + 1)
            $n = $rel.ToLower()
            $cands += [pscustomobject]@{
                File = $f.FullName
                Rel  = $rel
                Brushes   = $brushes
                HintDark  = ($n -match 'dark')
                HintLight = ($n -match 'light')
            }
        }
    }
    return ,$cands
}

function Get-CsPaletteCandidates([string]$Dir) {
    # C# 真相源：("Key", "#深色", "#淺色") 三元組 ≥ 3 個的檔案。
    # ⚠ 「第一個是深、第二個是淺」是假設（Kindling 如此）；.NET 版改由 config 指定。
    $cands = @()
    foreach ($f in Get-ChildItem $Dir -Filter '*.cs' -Recurse |
             Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }) {
        $map = @{}
        foreach ($line in Get-Content $f.FullName) {
            if ($line -match '\("(?<k>\w+)"\s*,\s*"(?<d>#[0-9A-Fa-f]{6,8})"\s*,\s*"(?<l>#[0-9A-Fa-f]{6,8})"\)') {
                $map[$Matches['k']] = "$($Matches['d'].ToUpper()),$($Matches['l'].ToUpper())"
            }
        }
        if ($map.Count -ge 3) {
            $cands += [pscustomobject]@{
                File = $f.FullName
                Rel  = $f.FullName.Substring($Dir.Length + 1)
                Pal  = $map
            }
        }
    }
    return ,$cands
}

function Detect-Palette([string]$Dir) {
    # 回傳 @{ Pal; Exclude; Msg; Warn }。
    # Exclude = 被認定為色盤檔的 XAML（不進 UI 掃描 —— 色盤定義檔不是畫面）。
    # 偵測結果一律明講（Msg）；退化（none）另外用 Warn 喊（規劃書 8.1 規則 3）。
    $x = Get-XamlPaletteCandidates $Dir
    $c = Get-CsPaletteCandidates $Dir

    # 1) 深淺配對：dark 檔 × light 檔，brush 鍵集重疊 ≥ 50%
    $bestPair = $null
    foreach ($d in @($x | Where-Object { $_.HintDark -and -not $_.HintLight })) {
        foreach ($l in @($x | Where-Object { $_.HintLight -and -not $_.HintDark })) {
            $common = @($d.Brushes.Keys | Where-Object { $l.Brushes.ContainsKey($_) })
            $minC = [Math]::Min($d.Brushes.Count, $l.Brushes.Count)
            if ($minC -gt 0 -and ($common.Count / $minC) -ge 0.5) {
                if (-not $bestPair -or $common.Count -gt $bestPair.Common.Count) {
                    $bestPair = @{ D = $d; L = $l; Common = $common }
                }
            }
        }
    }
    if ($bestPair) {
        $pal = @{}
        foreach ($k in $bestPair.Common) {
            $pal[$k] = "$($bestPair.D.Brushes[$k]),$($bestPair.L.Brushes[$k])"
        }
        return @{
            Pal = $pal
            Exclude = @($bestPair.D.File, $bestPair.L.File)
            Msg = "自動偵測：主題配對 $($bestPair.D.Rel) ＋ $($bestPair.L.Rel)（$($pal.Count) 個色票）"
            Warn = $false
        }
    }

    # 2) C# 三元組（深淺都有）—— 優先於只有一套值的單一 XAML 檔
    if ($c.Count -gt 0) {
        $best = $c | Sort-Object { $_.Pal.Count } -Descending | Select-Object -First 1
        # 鍵集與 C# 高度重疊的 XAML 候選視為同一色盤的靜態複本，一併排除出 UI 掃描
        $excl = @()
        foreach ($cand in $x) {
            $common = @($cand.Brushes.Keys | Where-Object { $best.Pal.ContainsKey($_) })
            if ($cand.Brushes.Count -gt 0 -and ($common.Count / $cand.Brushes.Count) -ge 0.5) {
                $excl += $cand.File
            }
        }
        return @{
            Pal = $best.Pal
            Exclude = $excl
            Msg = "自動偵測：C# 真相源 $($best.Rel)（$($best.Pal.Count) 個色票；假設三元組＝鍵,深,淺）"
            Warn = $false
        }
    }

    # 3) 單一 XAML 色盤檔：深淺同值（單一主題專案）
    if ($x.Count -gt 0) {
        $best = $x | Sort-Object { $_.Brushes.Count } -Descending | Select-Object -First 1
        $pal = @{}
        foreach ($k in $best.Brushes.Keys) { $pal[$k] = "$($best.Brushes[$k]),$($best.Brushes[$k])" }
        $others = @($x | Where-Object { $_.File -ne $best.File } | ForEach-Object { $_.Rel })
        $note = if ($others.Count -gt 0) { "；其他候選未採用：$($others -join '、')" } else { '' }
        return @{
            Pal = $pal
            Exclude = @($best.File)
            Msg = "自動偵測：單一主題色盤 $($best.Rel)（$($pal.Count) 個色票）$note"
            Warn = $false
        }
    }

    # 4) 什麼都沒找到 → 退回只算寫死色碼。這是退化，要喊。
    return @{
        Pal = @{}
        Exclude = @()
        Msg = "自動偵測：找不到色盤（無 ResourceDictionary 色票、無 C# 三元組）—— 只算寫死色碼與具名色"
        Warn = $true
    }
}

# ── 保留的靜態表：只當作解析失敗時的退路 ──
$palettes = @{
    kindling = @{
        Bg0='#111111,#EAEAEA'; Bg1='#1A1A1A,#F3F3F3'; Bg2='#242424,#FBFBFB'
        Bg3='#2E2E2E,#E9E9E9'; Bg4='#383838,#DFDFDF'; BgDeep='#0D0D0D,#FFFFFF'
        Fg0='#EEEEEE,#1B1B1B'; Fg1='#A0A0A0,#5A5A5A'; Fg2='#606060,#8F8F8F'
        Border0='#3A3A3A,#D0D0D0'
        Purple='#7F77DD,#6B62D6'; PurpleD='#534AB7,#534AB7'
        Blue='#378ADD,#1D6FC4'; Red='#E24B4A,#C42B1C'
        DangerBg='#7A2E2D,#FDE7E9'; DangerBorder='#B5413F,#EEC0C3'
        DangerFg='#F4D6D5,#C42B1C'; DangerSolid='#CC3A3A,#D13438'
        Amber='#E8C66B,#9D5D00'; AmberBg='#3A2E12,#FFF4CE'; AmberBorder='#5A4A1E,#E6D8A8'
        InfoBarWarningBackground='#433519,#FFF4CE'; InfoBarWarningIcon='#FCE100,#9D5D00'
        InfoBarWarningForeground='#FFFFFF,#1B1B1B'
        BadgeWarnBg='#241C0D,#FFF4CE'; BadgeWarnBorder='#5A4422,#E6D8A8'; BadgeWarnFg='#D9A441,#9D5D00'
        BadgeOkBg='#0E1A12,#DFF6DD'; BadgeOkBorder='#244A30,#9FD49B'; BadgeOkFg='#6FCF97,#0F7B0F'
    }
    quillnest = @{
        PrimaryBrush='#42A5F5,#2196F3'; PrimaryDarkBrush='#1976D2,#1976D2'; AccentBrush='#FF4081,#FF4081'
        BackgroundBrush='#121212,#FAFAFA'; SurfaceBrush='#1E1E1E,#FFFFFF'
        SidebarBrush='#252526,#F5F5F5'; CardBackgroundBrush='#1E1E1E,#FFFFFF'
        TextPrimaryBrush='#FFFFFF,#212121'; TextSecondaryBrush='#B0B0B0,#757575'
        TextBrush='#FFFFFF,#212121'; SecondaryTextBrush='#B0B0B0,#757575'
        DividerBrush='#3F3F46,#E0E0E0'; BorderBrush='#3F3F46,#DDDDDD'; HoverBrush='#2A2A2A,#F0F0F0'
    }
}

$named = @{
    white='#FFFFFF'; black='#000000'; gray='#808080'; darkgray='#A9A9A9'; lightgray='#D3D3D3'
    red='#FF0000'; green='#008000'; blue='#0000FF'; orange='#FFA500'; yellow='#FFFF00'
    silver='#C0C0C0'; whitesmoke='#F5F5F5'; gainsboro='#DCDCDC'; dimgray='#696969'
}

# 優先從專案讀真實色盤；讀不到才退回腳本裡的靜態表
$pal = $null
$autoExclude = @()   # 自動模式下認定為色盤檔的 XAML（不進 UI 掃描）
if (-not $Theme) {
    # ── M1：自動偵測 ──
    $det = Detect-Palette $Root
    $pal = $det.Pal
    $autoExclude = $det.Exclude
    if ($det.Warn) { Write-Host "⚠ $($det.Msg)" -ForegroundColor Yellow }
    else           { Write-Host $det.Msg -ForegroundColor DarkGray }
} else {
try {
    if ($Theme -eq 'quillnest') {
        $dir = Join-Path $Root 'Resources'
        if (Test-Path (Join-Path $dir 'LightTheme.xaml')) { $pal = Read-QuillNestPalette $dir }
    }
    elseif ($Theme -eq 'celflow') {
        $dt = Join-Path $Root 'Styles\DarkTheme.xaml'
        if (Test-Path $dt) { $pal = Read-CelFlowPalette $dt }
    }
    elseif ($Theme -eq 'kindling') {
        $svc = Join-Path $Root 'Services\SystemThemeService.cs'
        if (Test-Path $svc) { $pal = Read-KindlingPalette $svc }
    }
    # cornea：沒有自訂色盤（.NET Fluent ThemeMode="System"），$pal 留空，見下
} catch { Write-Warning "讀取專案色盤失敗，改用腳本內建表：$_" }

if ($Theme -eq 'cornea') {
    # Cornea 無自訂色盤 —— 這是 M1「找不到色盤就退回只算寫死色碼」模式的預演。
    # 退化要用喊的（規劃書 8.1 規則 3），所以這行是黃色的。
    $pal = @{}
    Write-Host "⚠ 色盤來源：無（專案沒有自訂色盤；只算寫死色碼與具名色）" -ForegroundColor Yellow
} elseif ($pal -and $pal.Count -gt 0) {
    Write-Host "色盤來源：專案檔案（$($pal.Count) 個色票）" -ForegroundColor DarkGray
} else {
    $pal = $palettes[$Theme]
    Write-Host "⚠ 色盤來源：腳本內建的靜態表（可能已過時，$($pal.Count) 個色票）" -ForegroundColor Yellow
}
}

function Lum([string]$hex) {
    $h = $hex.TrimStart('#')
    if ($h.Length -eq 8) { $h = $h.Substring(2) }
    $ch = @(0,2,4) | ForEach-Object {
        $v = [Convert]::ToInt32($h.Substring($_,2),16) / 255.0
        if ($v -le 0.03928) { $v/12.92 } else { [Math]::Pow(($v+0.055)/1.055, 2.4) }
    }
    0.2126*$ch[0] + 0.7152*$ch[1] + 0.0722*$ch[2]
}
function Con([string]$a, [string]$b) {
    $la = Lum $a; $lb = Lum $b
    $hi = [Math]::Max($la,$lb); $lo = [Math]::Min($la,$lb)
    [Math]::Round(($hi+0.05)/($lo+0.05), 2)
}

# 半透明色 $fg（不含 alpha 的 RGB）以透明度 $a 疊在不透明底色 $bg 上的結果
function Composite([string]$fg, [double]$a, [string]$bg) {
    $f = $fg.TrimStart('#'); $b = $bg.TrimStart('#')
    if ($b.Length -eq 8) { $b = $b.Substring(2) }
    $out = @(0,2,4) | ForEach-Object {
        $cf = [Convert]::ToInt32($f.Substring($_,2),16)
        $cb = [Convert]::ToInt32($b.Substring($_,2),16)
        [int][Math]::Round($a * $cf + (1 - $a) * $cb)
    }
    '#' + (($out | ForEach-Object { $_.ToString('X2') }) -join '')
}

# 把一個屬性值解析成 @{Dark=..;Light=..;Kind=..} 或 $null
function Resolve([string]$v) {
    if (-not $v) { return $null }
    $v = $v.Trim()
    if ($v -eq 'Transparent') { return @{ Kind='transparent' } }
    if ($v -match '^#[0-9A-Fa-f]{6}$') { return @{ Dark=$v.ToUpper(); Light=$v.ToUpper(); Kind='hard' } }
    if ($v -match '^#[0-9A-Fa-f]{8}$') {
        # 半透明：要跟底下的顏色合成，交給呼叫端處理（帶 A 與 RGB 出去；
        # 字面值深淺同值，AL/RGBL 就是 A/RGB 的複本）
        $h = $v.TrimStart('#')
        $a = [Convert]::ToInt32($h.Substring(0,2),16) / 255.0
        $rgb = '#' + $h.Substring(2).ToUpper()
        return @{ Kind='alpha'; A=$a; RGB=$rgb; AL=$a; RGBL=$rgb }
    }
    if ($named.ContainsKey($v.ToLower())) {
        $h = $named[$v.ToLower()]; return @{ Dark=$h; Light=$h; Kind='hard' }
    }
    if ($v -match '^\{(Dynamic|Static)Resource\s+(?<k>[\w\.]+)\}$') {
        $k = $Matches['k']
        if ($pal.ContainsKey($k)) {
            $parts = $pal[$k].Split(',')
            if ($parts[0].Length -eq 9) {
                # #AARRGGBB 的「半透明色票」（如 CelFlow 的 BlueOverlay/Scrim）：
                # 2026-07-31 之前只有字面 8 碼會走合成，色票鍵的 8 碼值被 Lum()
                # 剝掉 alpha 當『不透明色』用 —— CelFlow 實證：InfoL 疊 BlueOverlay
                # （16% 藍）被算成 InfoL 疊純藍 2.68 的假「破」。深淺各帶自己的 alpha。
                return @{ Kind='alpha'; Key=$k
                          A   =([Convert]::ToInt32($parts[0].Substring(1,2),16)/255.0)
                          RGB ='#'+$parts[0].Substring(3)
                          AL  =([Convert]::ToInt32($parts[1].Substring(1,2),16)/255.0)
                          RGBL='#'+$parts[1].Substring(3) }
            }
            return @{ Dark=$parts[0]; Light=$parts[1]; Kind='soft'; Key=$k }
        }
        return @{ Kind='unknownkey'; Key=$k }
    }
    return @{ Kind='other' }   # TemplateBinding / Binding / 漸層等
}

$findings = @()
$stats = @{ pairs=0; unresolved=0; skipped=0; deadfg=0; disabled=0 }

# ── 文字／裝飾分類表（Walk 與 WalkStyles 共用）──
# Rectangle 當分隔線、ProgressBar 當進度條、Ellipse 當圓點，都是裝飾元素，
# 用文字標準去要求它們是錯的（分隔線本來就該低調）。
$textEls = @('TextBlock','Run','Label','AccessText','TextBox','PasswordBox',
             'RichTextBox','Hyperlink','MenuItem','CheckBox','RadioButton',
             'ComboBox','ComboBoxItem','Expander','DataGridTextColumn','Button')
# ⚠ 有些控制項的 Foreground 根本不是文字：ProgressBar 的 Foreground 是進度條填色、
#   Shape 類的是圖形本身。只看「有沒有 Foreground」會把它們全誤判成文字，
#   然後用 4.5 去要求一條進度條。先排除，再看元素型別。
$nonTextEls = @('ProgressBar','Rectangle','Ellipse','Path','Polygon','Polyline',
                'Line','Border','Separator','Slider','Thumb','Track','ScrollBar')

# ── 具名 Style 索引與解析（2026-07-31，修「元素自身底色來自具名 Style」盲區）──
#
# 範圍界定：
#   - 只解析「具名引用」與「行內 Style」；隱含樣式不套用（見檔頭「明確不處理」）。
#   - BasedOn 鏈逐層合併；衍生 Style 自帶 Template 時不繼承基底的模板觸發器
#     （WPF 換掉整個模板，基底模板的觸發器就不存在了）。
#   - 模板觸發器只收「無 TargetName 的 Setter」（套回模板宿主本身）；
#     有 TargetName 的要解析模板內部樹，不做。
#   - 同條件的觸發器跨節點合併（style trigger 蓋 template trigger、衍生蓋基底），
#     否則「style trigger 設字色＋template trigger 設底色」的同一個狀態
#     會被拆成兩個各缺一半的幻影組合。

function Test-DisabledTrigger($t) {
    # IsEnabled=False 觸發 = 停用態。WCAG 1.4.3 明文豁免停用中的控制項。
    if ($t.Name.LocalName -eq 'Trigger') {
        $p = $t.Attribute('Property'); $v = $t.Attribute('Value')
        return [bool]($p -and $v -and $p.Value -match '(^|\.)IsEnabled$' -and $v.Value -eq 'False')
    }
    if ($t.Name.LocalName -eq 'DataTrigger') {
        $b = $t.Attribute('Binding'); $v = $t.Attribute('Value')
        return [bool]($b -and $v -and $b.Value -match 'IsEnabled' -and $v.Value -eq 'False')
    }
    return $false
}

$script:styleIdSeq = 0
function New-StyleRecord($style, [string]$file) {
    $script:styleIdSeq++
    $setters = @{}
    foreach ($s in $style.Elements() | Where-Object { $_.Name.LocalName -eq 'Setter' }) {
        $p = $s.Attribute('Property'); $v = $s.Attribute('Value')
        if ($p -and $v) { $setters[$p.Value] = $v.Value }
    }
    $tmplEl = $style.Descendants() |
        Where-Object { $_.Name.LocalName -eq 'ControlTemplate' } | Select-Object -First 1
    $hasTmpl = [bool]$tmplEl
    # 模板根元素自帶的 Background（CellDeleteBtn 形狀：Style 層沒有 Background setter，
    # 真正的底是模板根 Border 的 DangerSolid —— 少了這條，字會誤配到祖先背景）。
    # 跳過 <ControlTemplate.Resources> 之類的屬性元素，取第一個視覺元素。
    $tmplBg = $null
    if ($tmplEl) {
        $rootEl = $tmplEl.Elements() |
            Where-Object { $_.Name.LocalName -notmatch '\.' } | Select-Object -First 1
        if ($rootEl) {
            $a = $rootEl.Attribute('Background')
            if ($a) { $tmplBg = $a.Value }
        }
    }
    $states = @()
    foreach ($t in $style.Descendants() | Where-Object { $_.Name.LocalName -in @('Trigger','DataTrigger') }) {
        $ts = @{}
        foreach ($s in $t.Elements() | Where-Object { $_.Name.LocalName -eq 'Setter' }) {
            $p = $s.Attribute('Property'); $v = $s.Attribute('Value')
            if ($p -and $v -and -not $s.Attribute('TargetName')) { $ts[$p.Value] = $v.Value }
        }
        if ($ts.Count -eq 0) { continue }
        $inTmpl = $false; $anc = $t.Parent
        while ($anc -and -not [object]::ReferenceEquals($anc, $style)) {
            if ($anc.Name.LocalName -eq 'ControlTemplate') { $inTmpl = $true; break }
            $anc = $anc.Parent
        }
        # 條件簽名：同條件的狀態之後要跨節點合併
        $cond =
            if ($t.Name.LocalName -eq 'Trigger') {
                "P:$($t.Attribute('Property').Value)=$($t.Attribute('Value').Value)"
            } elseif ($t.Attribute('Binding') -and $t.Attribute('Value')) {
                "B:$($t.Attribute('Binding').Value)=$($t.Attribute('Value').Value)"
            } else { $null }
        $states += ,@{ Disabled = (Test-DisabledTrigger $t); FromTemplate = $inTmpl
                       Set = $ts; Cond = $cond }
    }
    $keyAttr = $style.Attribute('{http://schemas.microsoft.com/winfx/2006/xaml}Key')
    $boKey = $null
    $bo = $style.Attribute('BasedOn')
    if ($bo -and $bo.Value -match '^\{StaticResource\s+(?<k>[\w\.]+)\}$') { $boKey = $Matches['k'] }
    return [pscustomobject]@{
        Id = $script:styleIdSeq; File = $file
        Key = if ($keyAttr) { $keyAttr.Value } else { $null }
        BasedOn = $boKey; Setters = $setters; States = $states; HasTemplate = $hasTmpl
        TmplBg = $tmplBg
    }
}

# 索引所有檔案的具名 Style —— 含色盤／主題字典（全域樣式就住在那裡；
# 「排除出 UI 掃描」不等於「排除出索引」）。查找時同檔優先於全域，
# 模擬 WPF 的資源查找順序（CelFlow 的 CrashRecoveryDialog 有自己的 DarkBtn，
# 與 DarkTheme.xaml 的全域 DarkBtn 同名不同值）。
$styleIndexByFile = @{}
$styleIndexGlobal = @{}
foreach ($sf in Get-ChildItem $Root -Filter '*.xaml' -Recurse |
         Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }) {
    try { $sdoc = [System.Xml.Linq.XDocument]::Load($sf.FullName) } catch { continue }
    $isGlobal = $sdoc.Root.Name.LocalName -in @('ResourceDictionary','Application')
    foreach ($style in $sdoc.Descendants() | Where-Object { $_.Name.LocalName -eq 'Style' }) {
        $rec = New-StyleRecord $style $sf.FullName
        if (-not $rec.Key) { continue }
        if (-not $styleIndexByFile.ContainsKey($sf.FullName)) { $styleIndexByFile[$sf.FullName] = @{} }
        $styleIndexByFile[$sf.FullName][$rec.Key] = $rec
        if ($isGlobal -and -not $styleIndexGlobal.ContainsKey($rec.Key)) { $styleIndexGlobal[$rec.Key] = $rec }
    }
}

function Find-StyleRecord([string]$key, [string]$file) {
    if ($styleIndexByFile.ContainsKey($file) -and $styleIndexByFile[$file].ContainsKey($key)) {
        return $styleIndexByFile[$file][$key]
    }
    if ($styleIndexGlobal.ContainsKey($key)) { return $styleIndexGlobal[$key] }
    return $null
}

function Merge-StyleChain($rec, $seen) {
    # 把 BasedOn 鏈揉平：Props = 各屬性最後生效的 Setter（帶來源 Style Id），
    # States = 鏈上所有觸發器狀態（Set 逐屬性帶來源，供「同節點配對跳過」判斷），
    # TmplBg = 生效模板的根元素背景（衍生自帶模板時整個換掉，含根背景）。
    $props = @{}; $states = @(); $tmplBg = $null
    if ($rec.BasedOn -and $seen.Add($rec.BasedOn)) {
        $base = Find-StyleRecord $rec.BasedOn $rec.File
        if ($base) {
            $m = Merge-StyleChain $base $seen
            foreach ($k in $m.Props.Keys) { $props[$k] = $m.Props[$k] }
            foreach ($s in $m.States) {
                if ($s.FromTemplate -and $rec.HasTemplate) { continue }   # 模板被換掉
                $states += ,$s
            }
            $tmplBg = $m.TmplBg
        }
    }
    foreach ($p in $rec.Setters.Keys) { $props[$p] = @{ V = $rec.Setters[$p]; Src = $rec.Id } }
    foreach ($s in $rec.States) {
        $srcMap = @{}; foreach ($k in $s.Set.Keys) { $srcMap[$k] = $rec.Id }
        $states += ,@{ name='觸發'; Disabled=$s.Disabled; FromTemplate=$s.FromTemplate
                       Set=$s.Set; SetSrc=$srcMap; Cond=$s.Cond }
    }
    if ($rec.HasTemplate) { $tmplBg = $rec.TmplBg }
    return @{ Props = $props; States = $states; TmplBg = $tmplBg }
}

function Merge-SameConditionStates($states) {
    # 同條件觸發器合併：WPF 優先序是 style trigger > template trigger、
    # 衍生（後到）> 基底（先到）。合併時 template 不蓋 style 已設的屬性。
    $byCond = [ordered]@{}
    $anon = 0
    foreach ($s in $states) {
        $c = if ($s.Cond) { $s.Cond } else { $anon++; "anon:$anon" }
        if (-not $byCond.Contains($c)) {
            $set = @{}; $src = @{}
            foreach ($k in $s.Set.Keys) { $set[$k] = $s.Set[$k]; $src[$k] = $s.SetSrc[$k] }
            $byCond[$c] = @{ name=$s.name; Disabled=$s.Disabled; FromTemplate=$s.FromTemplate
                             Set=$set; SetSrc=$src }
        } else {
            $t = $byCond[$c]
            if ($s.FromTemplate -and -not $t.FromTemplate) {
                # template 觸發只補 style 觸發沒設的屬性
                foreach ($k in $s.Set.Keys) {
                    if (-not $t.Set.ContainsKey($k)) { $t.Set[$k] = $s.Set[$k]; $t.SetSrc[$k] = $s.SetSrc[$k] }
                }
            } else {
                foreach ($k in $s.Set.Keys) { $t.Set[$k] = $s.Set[$k]; $t.SetSrc[$k] = $s.SetSrc[$k] }
                $t.FromTemplate = $t.FromTemplate -and $s.FromTemplate
            }
            $t.Disabled = $t.Disabled -or $s.Disabled
        }
    }
    return @($byCond.Values)
}

function Get-ElementStyleChain($el) {
    $sAttr = $el.Attribute('Style')
    if ($sAttr) {
        if ($sAttr.Value -notmatch '^\{(Dynamic|Static)Resource\s+(?<k>[\w\.]+)\}$') { return $null }
        $k = $Matches['k']
        $rec = Find-StyleRecord $k $script:curFile
        if (-not $rec) { return $null }
        $seen = New-Object 'System.Collections.Generic.HashSet[string]'
        [void]$seen.Add($k)
        $m = Merge-StyleChain $rec $seen
        $m['States'] = Merge-SameConditionStates $m.States
        $m['Label'] = "Style[$k]"
        return $m
    }
    $propEl = $el.Elements() |
        Where-Object { $_.Name.LocalName -eq ($el.Name.LocalName + '.Style') } | Select-Object -First 1
    if (-not $propEl) { return $null }
    $styleEl = $propEl.Elements() | Where-Object { $_.Name.LocalName -eq 'Style' } | Select-Object -First 1
    if (-not $styleEl) { return $null }
    $rec = New-StyleRecord $styleEl $script:curFile
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $m = Merge-StyleChain $rec $seen
    $m['States'] = Merge-SameConditionStates $m.States
    $m['Label'] = if ($rec.BasedOn) { "Style[→$($rec.BasedOn)]" } else { 'Style[行內]' }
    return $m
}

$files = Get-ChildItem $Root -Filter '*.xaml' -Recurse |
         Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
if ($Theme) {
    # 寫死模式沿用舊的名稱規則（對照組，之後隨 -Theme 一起砍）
    $files = @($files | Where-Object { $_.Name -notmatch '^(Dark|Light)Theme\.xaml$' })
} else {
    # 自動模式：排除被認定為色盤檔的 XAML —— 色盤定義檔不是畫面
    $files = @($files | Where-Object { $_.FullName -notin $autoExclude })
}

# 遞迴走訪：$bgStack 是目前生效的背景（最後一個非 transparent 的）
#
# $op = 累積的 Opacity。WPF 的 Opacity 會往下乘到所有子元素，
# 所以要一路帶著走：Border(0.5) 裡的 TextBlock(0.5) 實際是 0.25。
#
# ⚠ 2026-07-30 開 App 實測才發現的盲區：
#   CelFlow 畫布的提示文字沒指定 Foreground（繼承 Fg0 #EEEEEE），但有 Opacity="0.3"，
#   螢幕上取樣到的是 #535353（2.45:1）。工具當時只看 Foreground 的色票值，
#   回報「Fg0 = 16.28 ✓」—— 完全漏掉。
#   Opacity 是繼「Foreground 色票」「背景 alpha」之後第三種讓文字變暗的方式。
function Walk($el, $bg, $file, $op = 1.0) {
    $opRaw = $el.Attribute('Opacity')
    if ($opRaw) {
        $v = 0.0
        # Opacity 也可能是 Binding；只處理字面數值，其餘維持原值
        if ([double]::TryParse($opRaw.Value, [ref]$v)) { $op = $op * $v }
    }

    # 元素套用的 Style（具名引用或 <X.Style> 行內）：底色與觸發態的另一個來源
    $chain = Get-ElementStyleChain $el

    # 逐狀態路徑的 alpha 合成要用「更新前」的祖先背景 —— 下面的 bg 更新塊會把
    # 半透明底先合成一次，逐狀態再合成就是疊兩層（2026-07-31 回頭檢視抓到的）
    $ancestorBg = $bg

    $localBgRaw = $el.Attribute('Background')
    $bgVal = $null
    if ($localBgRaw) { $bgVal = $localBgRaw.Value }
    elseif ($chain -and $chain.Props.ContainsKey('Background')) { $bgVal = $chain.Props['Background'].V }
    elseif ($chain -and $chain.TmplBg) { $bgVal = $chain.TmplBg }   # 模板根的背景管到內容
    if ($bgVal) {
        $r = Resolve $bgVal
        if ($r) {
            if ($r.Kind -eq 'alpha') {
                # 半透明遮罩：疊在目前生效的背景上算出實際顏色（WPF 以 sRGB 混合）
                if ($bg -and $bg.Dark -and $bg.Light) {
                    $bg = @{
                        Kind  = 'hard'
                        Key   = "$bgVal 疊於 $(if($bg.Key){'{'+$bg.Key+'}'}else{$bg.Dark})"
                        Dark  = Composite $r.RGB  $r.A  $bg.Dark
                        Light = Composite $r.RGBL $r.AL $bg.Light
                    }
                }
                # 找不到底色就維持原本的（無法判定，後面會計入 unresolved）
            }
            elseif ($r.Kind -eq 'other') {
                # 底色來自 Binding / TemplateBinding / 漸層 —— 執行期才知道實際顏色。
                # 關鍵：不能「往上繼承祖先背景」，那會把子元素的文字色配到錯的底色上
                # （例：標籤徽章的底色綁使用者選的顏色，白字是對的，但繼承祖先會誤判成白配白）。
                # 標記為無法判定，子元素一律跳過。
                $bg = @{ Kind='other' }
            }
            elseif ($r.Kind -ne 'transparent') { $bg = $r }
        }
    }

    # ── 元素 × Style 鏈的逐狀態檢查（具名 Style 底色盲區，2026-07-31）──
    # CelFlow 實證：刪除鈕 Foreground=RedL 配 BasedOn=DarkBtn，DarkBtn 的底是
    # Bg3、hover Bg4 —— 舊版看不到 Style 的底色，誤落到祖先卡片的 Bg2（4.44），
    # 低估實況（Bg3 3.89 / Bg4 3.36）；「忽略」鈕（Fg1 疊 Border0/Border1）整組漏掉。
    $handledFg = $false
    if ($chain) {
        $handledFg = $true   # Foreground 一律由這裡接手，避免與下面的舊路徑重複輸出
        $localFgRaw = $el.Attribute('Foreground')
        $emitted = @{}
        $allStates = @(@{ name='基礎'; Set=@{}; SetSrc=@{}; Disabled=$false }) + @($chain.States)
        foreach ($st in $allStates) {
            # WPF 優先序：元素屬性 > Style 觸發 > Style Setter
            $fgV = $null; $fgSrc = $null
            if ($localFgRaw) { $fgV = $localFgRaw.Value; $fgSrc = 'local' }
            elseif ($st.Set.ContainsKey('Foreground')) { $fgV = $st.Set['Foreground']; $fgSrc = $st.SetSrc['Foreground'] }
            elseif ($chain.Props.ContainsKey('Foreground')) {
                $fgV = $chain.Props['Foreground'].V; $fgSrc = $chain.Props['Foreground'].Src
            }
            if (-not $fgV) { continue }

            $bgV = $null; $bgSrc = $null
            if ($localBgRaw) { $bgV = $localBgRaw.Value; $bgSrc = 'local' }
            elseif ($st.Set.ContainsKey('Background')) { $bgV = $st.Set['Background']; $bgSrc = $st.SetSrc['Background'] }
            elseif ($chain.Props.ContainsKey('Background')) {
                $bgV = $chain.Props['Background'].V; $bgSrc = $chain.Props['Background'].Src
            }
            elseif ($chain.TmplBg) {
                # 模板根元素自帶的背景（CellDeleteBtn 形狀）。來源標 'tmpl'——
                # 它不是 Style 層 setter，WalkStyles 不會在定義處檢到，這裡必須報
                $bgV = $chain.TmplBg; $bgSrc = 'tmpl'
            }

            # 字與底都出自同一個 Style 節點 → WalkStyles 在定義處已檢過，不重複
            if ($fgSrc -and $bgSrc -and $fgSrc -ne 'local' -and $bgSrc -ne 'local' -and $fgSrc -eq $bgSrc) { continue }
            # 停用態：WCAG 1.4.3 豁免，計數回報、不評分
            if ($st.Disabled) { $script:stats.disabled++; continue }

            $fg = Resolve $fgV
            if (-not $fg) { continue }
            if ($fg.Kind -in @('other','alpha','transparent','unknownkey')) { $script:stats.skipped++; continue }

            $bgObj = $null
            if ($bgV) {
                $rb = Resolve $bgV
                if (-not $rb) { continue }
                if ($rb.Kind -eq 'transparent') { $bgObj = $bg }
                elseif ($rb.Kind -eq 'alpha') {
                    if ($ancestorBg -and $ancestorBg.Dark -and $ancestorBg.Light) {
                        $bgObj = @{ Kind='hard'
                                    Key ="$bgV 疊於 $(if($ancestorBg.Key){'{'+$ancestorBg.Key+'}'}else{$ancestorBg.Dark})"
                                    Dark =(Composite $rb.RGB  $rb.A  $ancestorBg.Dark)
                                    Light=(Composite $rb.RGBL $rb.AL $ancestorBg.Light) }
                    }
                }
                elseif ($rb.Kind -in @('other','unknownkey')) { $script:stats.unresolved++; continue }
                else { $bgObj = $rb }
            } else { $bgObj = $bg }   # Style 鏈沒設底 → 退回祖先背景
            if (-not $bgObj) { $script:stats.unresolved++; continue }
            if ($bgObj.Kind -in @('other','alpha','unknownkey')) { $script:stats.unresolved++; continue }
            if ($op -lt 0.01) { $script:stats.skipped++; continue }

            # 觸發器只動了無關屬性（如停用態只設 Opacity）→ 與基礎同組，不重複
            $comboKey = "$($fg.Dark)|$($bgObj.Dark)"
            if ($emitted.ContainsKey($comboKey)) { continue }
            $emitted[$comboKey] = $true

            $isText = if ($el.Name.LocalName -in $nonTextEls) { $false } else { $true }
            $fs = 0.0; $fsRaw = $el.Attribute('FontSize')
            if ($fsRaw) { [double]::TryParse($fsRaw.Value, [ref]$fs) | Out-Null }
            elseif ($st.Set.ContainsKey('FontSize')) { [double]::TryParse($st.Set['FontSize'], [ref]$fs) | Out-Null }
            elseif ($chain.Props.ContainsKey('FontSize')) { [double]::TryParse($chain.Props['FontSize'].V, [ref]$fs) | Out-Null }
            $fwRaw = $el.Attribute('FontWeight')
            $fwV = if ($fwRaw) { $fwRaw.Value }
                   elseif ($chain.Props.ContainsKey('FontWeight')) { $chain.Props['FontWeight'].V } else { $null }
            $bold = $fwV -and ($fwV -match 'Bold|Black|Heavy|SemiBold')
            $pt = $fs * 0.75
            $isLarge = ($pt -ge 18) -or ($bold -and $pt -ge 14)
            $need = if (-not $isText) { 0.0 } elseif ($isLarge) { 3.0 } else { 4.5 }

            $fgD = $fg.Dark; $fgL = $fg.Light
            if ($op -lt 0.999) {
                $fgD = Composite $fg.Dark  $op $bgObj.Dark
                $fgL = Composite $fg.Light $op $bgObj.Light
            }

            $line = 0
            $li = [System.Xml.IXmlLineInfo]$el
            if ($li.HasLineInfo()) { $line = $li.LineNumber }
            $script:stats.pairs++
            $script:findings += [pscustomobject]@{
                File   = $file
                Line   = $line
                Elem   = "$($el.Name.LocalName)⊕$($chain.Label)/$($st.name)"
                Fg     = if ($op -lt 0.999) { "$fgV ×$([Math]::Round($op,2))" } else { $fgV }
                Bg     = if ($bgObj.Key) { '{'+$bgObj.Key+'}' } else { $bgObj.Dark }
                Mixed  = ($fg.Kind -ne $bgObj.Kind)
                CDark  = Con $fgD $bgObj.Dark
                CLight = Con $fgL $bgObj.Light
                IsText = $isText
                Need   = $need
                Size   = if ($fs -gt 0) { "$([int]$fs)px" } else { '' }
                Large  = $isLarge
            }
        }
    }

    foreach ($attr in @('Foreground','Fill')) {
        if ($attr -eq 'Foreground' -and $handledFg) { continue }
        $fgRaw = $el.Attribute($attr)
        if (-not $fgRaw) { continue }
        $fg = Resolve $fgRaw.Value
        if (-not $fg) { continue }
        if ($fg.Kind -in @('other','alpha','transparent','unknownkey')) { $script:stats.skipped++; continue }
        if (-not $bg) { $script:stats.unresolved++; continue }
        if ($bg.Kind -in @('other','alpha','unknownkey')) { $script:stats.unresolved++; continue }

        # Opacity 0 = 元素當下完全隱形。XAML 裡寫 Opacity="0" 幾乎都是淡入動畫的初始狀態
        # （QuillNest 的自動儲存提示、Kindling 的通知快顯都是），穩態是 1。
        # 對「看不見的東西」算對比沒有意義，跳過而不是報成 1:1 的假警報。
        if ($op -lt 0.01) { $script:stats.skipped++; continue }

        # 兩邊都寫死 → 自洽，不受主題影響
        $mixed = ($fg.Kind -ne $bg.Kind)
        $script:stats.pairs++

        $line = 0
        $li = [System.Xml.IXmlLineInfo]$el
        if ($li.HasLineInfo()) { $line = $li.LineNumber }

        # ── 這是文字還是裝飾？WCAG 的 4.5:1 只管文字 ──
        # 分類表在檔案上方（與 WalkStyles 共用）。之前每跑一次就要人工濾一次，現在內建。
        $isText = if ($el.Name.LocalName -in $nonTextEls) { $false }
                  elseif ($el.Name.LocalName -in $textEls) { $true }
                  else { $attr -eq 'Foreground' }

        # ── WCAG 大字級豁免：≥18pt 或 ≥14pt 粗體只需 3:1 ──
        # ⚠ WPF 的 FontSize 是「裝置獨立像素」(1/96 吋) 不是 point(1/72 吋)，
        #    換算要 ×0.75。18pt = 24px、14pt = 18.67px —— 少了這一步會把
        #    18px 的字誤判成大字級而放過它。
        $fs = 0.0; $fsRaw = $el.Attribute('FontSize')
        if ($fsRaw) { [double]::TryParse($fsRaw.Value, [ref]$fs) | Out-Null }
        $fw = $el.Attribute('FontWeight')
        $bold = $fw -and ($fw.Value -match 'Bold|Black|Heavy|SemiBold')
        $pt = $fs * 0.75
        $isLarge = ($pt -ge 18) -or ($bold -and $pt -ge 14)
        $need = if (-not $isText) { 0.0 } elseif ($isLarge) { 3.0 } else { 4.5 }

        # Opacity < 1 時，螢幕上看到的是「文字色以 $op 疊在背景上」的混色結果，
        # 不是文字色本身。用同一支 Composite（它本來就是為背景 alpha 寫的）。
        $fgD = $fg.Dark; $fgL = $fg.Light
        if ($op -lt 0.999) {
            $fgD = Composite $fg.Dark  $op $bg.Dark
            $fgL = Composite $fg.Light $op $bg.Light
        }

        $script:findings += [pscustomobject]@{
            File   = $file
            Line   = $line
            Elem   = $el.Name.LocalName
            Fg     = if ($op -lt 0.999) { "$($fgRaw.Value) ×$([Math]::Round($op,2))" } else { $fgRaw.Value }
            Bg     = if ($bg.Key) { '{'+$bg.Key+'}' } else { $bg.Dark }
            Mixed  = $mixed
            CDark  = Con $fgD $bg.Dark
            CLight = Con $fgL $bg.Light
            IsText = $isText
            Need   = $need
            Size   = if ($fs -gt 0) { "$([int]$fs)px" } else { '' }
            Large  = $isLarge
        }
    }

    foreach ($child in $el.Elements()) {
        # Style / Setter / Trigger 這些不是視覺樹，個別處理（見下），這裡跳過
        if ($child.Name.LocalName -in @('Style','Setter','Trigger','DataTrigger','MultiTrigger')) { continue }
        Walk $child $bg $file $op
    }
}

# Style 裡成對的 Setter（Background + Foreground 同時存在）
function WalkStyles($doc, $file) {
    foreach ($style in $doc.Descendants() | Where-Object { $_.Name.LocalName -eq 'Style' }) {
        $setters = @{}
        foreach ($s in $style.Elements() | Where-Object { $_.Name.LocalName -eq 'Setter' }) {
            $p = $s.Attribute('Property'); $v = $s.Attribute('Value')
            if ($p -and $v) { $setters[$p.Value] = $v.Value }
        }
        # 觸發器裡的 Setter 也算進來（它們覆蓋同一個 Style 的基礎值）
        $trigSetters = @()
        foreach ($t in $style.Descendants() | Where-Object { $_.Name.LocalName -in @('Trigger','DataTrigger') }) {
            $ts = @{}
            foreach ($s in $t.Elements() | Where-Object { $_.Name.LocalName -eq 'Setter' }) {
                $p = $s.Attribute('Property'); $v = $s.Attribute('Value')
                if ($p -and $v) { $ts[$p.Value] = $v.Value }
            }
            if ($ts.Count -gt 0) { $trigSetters += ,@{ Set=$ts; Disabled=(Test-DisabledTrigger $t) } }
        }

        # ── 誤報過濾：模板沒有 Foreground 消費者 → Style 層 Foreground 是死 setter ──
        # 2026-07-30 QuillNest WorkloadTemplateDialog 的 DataGridCheckBoxStyle 實證：
        # ControlTemplate 把 CheckBox 視覺整個換掉，只剩 Border＋寫死白色的勾勾 Path，
        # 沒有 ContentPresenter → Style 的 Foreground 不會被任何元素渲染，
        # 卻被拿去配模板觸發器裡勾選框的 Background（PrimaryButtonBrush），
        # 報出「淺色 3.5:1 不足」的假警報。
        # ⚠ 不能只看 ContentPresenter：Foreground 是繼承屬性，模板裡一顆沒指定
        # Foreground 的裸 TextBlock 一樣會繼承到 Style 的值。所以消費者清單是
        # ContentPresenter / ContentControl / TextBlock / AccessText / Label，
        # 外加模板裡明確接線的 {TemplateBinding Foreground}。
        # 一個都沒有時，這個 Style 所有含 Foreground 的配對（基礎＋觸發態）都不成立。
        $tmpl = $style.Descendants() | Where-Object { $_.Name.LocalName -eq 'ControlTemplate' } | Select-Object -First 1
        $fgDead = $false
        if ($tmpl) {
            $consumers = @('ContentPresenter','ContentControl','TextBlock','AccessText','Label')
            $hasConsumer = [bool]($tmpl.Descendants() |
                Where-Object { $_.Name.LocalName -in $consumers } | Select-Object -First 1)
            if (-not $hasConsumer) { $hasConsumer = $tmpl.ToString() -match 'TemplateBinding\s+Foreground' }
            $fgDead = -not $hasConsumer
        }

        $key = $style.Attribute('{http://schemas.microsoft.com/winfx/2006/xaml}Key')
        $tt  = $style.Attribute('TargetType')
        $label = if ($key) { $key.Value } elseif ($tt) { $tt.Value } else { '(匿名)' }
        # TargetType 有 "Button" 與 "{x:Type Button}" 兩種寫法，取最後一個單字
        $ttName = $null
        if ($tt -and $tt.Value -match '(?<t>\w+)\s*\}?\s*$') { $ttName = $Matches['t'] }
        $line = 0
        $sli = [System.Xml.IXmlLineInfo]$style
        if ($sli.HasLineInfo()) { $line = $sli.LineNumber }

        # 基礎 + 每個觸發器狀態各檢一次
        $states = @(@{ name='基礎'; set=$setters; disabled=$false })
        foreach ($ts in $trigSetters) {
            $merged = @{}; $setters.Keys | ForEach-Object { $merged[$_] = $setters[$_] }
            $ts.Set.Keys | ForEach-Object { $merged[$_] = $ts.Set[$_] }
            $states += @{ name='觸發'; set=$merged; disabled=$ts.Disabled }
        }

        foreach ($st in $states) {
            $fgv = $st.set['Foreground']; $bgv = $st.set['Background']
            if (-not $fgv -or -not $bgv) { continue }
            if ($fgDead) { $script:stats.deadfg++; continue }
            # 停用態（IsEnabled=False 觸發）：WCAG 1.4.3 豁免，計數回報、不評分
            if ($st.disabled) { $script:stats.disabled++; continue }
            $fg = Resolve $fgv; $bg = Resolve $bgv
            if (-not $fg -or -not $bg) { continue }
            if ($fg.Kind -in @('other','alpha','transparent','unknownkey')) { continue }
            if ($bg.Kind -in @('other','alpha','transparent','unknownkey')) { continue }

            # ⚠ v0.1 的第 5 號 bug（規劃書 8.2）：這裡漏了 Need 欄位，而分級用
            #   $r.Need -le 0 判裝飾，PS 裡 $null -le 0 為 True →
            #   所有 Style 配對被靜默歸為裝飾、完全不受檢 —— 謊報健康的第五次。
            #   分類沿用 Walk 的排除清單：TargetType 是非文字控制項才免檢；
            #   不確定（匿名、沒 TargetType）就當文字報出來 —— 不確定就報，不替使用者放過。
            $isText = -not ($ttName -and ($ttName -in $nonTextEls))
            $fs = 0.0
            if ($st.set['FontSize']) { [double]::TryParse($st.set['FontSize'], [ref]$fs) | Out-Null }
            $bold = $st.set['FontWeight'] -and ($st.set['FontWeight'] -match 'Bold|Black|Heavy|SemiBold')
            $pt = $fs * 0.75   # WPF FontSize 是 DIP，換 point 要 ×0.75（規劃書 4.3）
            $isLarge = ($pt -ge 18) -or ($bold -and $pt -ge 14)
            $need = if (-not $isText) { 0.0 } elseif ($isLarge) { 3.0 } else { 4.5 }

            $script:stats.pairs++
            $script:findings += [pscustomobject]@{
                File  = $file
                Line  = $line
                Elem  = "Style[$label]/$($st.name)"
                Fg    = $fgv
                Bg    = if ($bg.Key) { '{'+$bg.Key+'}' } else { $bg.Dark }
                Mixed = ($fg.Kind -ne $bg.Kind)
                CDark = Con $fg.Dark $bg.Dark
                CLight= Con $fg.Light $bg.Light
                IsText = $isText
                Need   = $need
                Size   = if ($fs -gt 0) { "$([int]$fs)px" } else { '' }
                Large  = $isLarge
            }
        }
    }
}

foreach ($f in $files) {
    try {
        $doc = [System.Xml.Linq.XDocument]::Load($f.FullName, [System.Xml.Linq.LoadOptions]::SetLineInfo)
    } catch { Write-Warning "解析失敗：$($f.Name) — $_"; continue }

    $script:curFile = $f.FullName   # 具名 Style 查找的「同檔優先」範圍
    # 報告用 repo 相對路徑（正斜線）：只給檔名的話，MVVM 專案的同名檔（ItemView.xaml）
    # 無法定位，baseline 的鍵還會跨資料夾互相污染。正斜線讓 baseline 跨 OS 可攜。
    $rel = $f.FullName.Substring($Root.Length).TrimStart('\','/').Replace('\','/')
    Walk $doc.Root $null $rel
    WalkStyles $doc $rel
}

# ── 報告 ──
"檔案 $($files.Count) 個｜解析出 $($stats.pairs) 組『文字色 × 生效背景色』配對"
"無法解析（背景在樹上找不到／來自 Binding）: $($stats.unresolved) 處｜跳過（半透明、漸層等）: $($stats.skipped) 處"
if ($stats.deadfg -gt 0) {
    # 過濾不靜默：被判定為死 setter 的配對要讓使用者知道有幾組、憑什麼被排除
    Write-Host "已排除 $($stats.deadfg) 組 Style 配對：模板無 Foreground 消費者（無 ContentPresenter/TextBlock 等，Foreground 不會被渲染）" -ForegroundColor DarkGray
}
if ($stats.disabled -gt 0) {
    Write-Host "已豁免 $($stats.disabled) 組停用態配對（IsEnabled=False 觸發；WCAG 1.4.3 不要求停用控制項的文字對比）" -ForegroundColor DarkGray
}
""

# 分級與對稱是「兩個獨立的問題」，不可混為一談。
#
# ⚠ 2026-07-30 修正的重大缺陷（先後在 CelFlow 與 Kindling 上實證）：
#   舊版把「兩個主題都低而且差不多」直接判成『刻意低對比（設計意圖）』並藏起來。
#   但**對稱不是意圖的證據 —— 兩邊一樣糟就只是兩邊都糟**。
#   實證：
#     · CelFlow 單一主題（深淺同值，gap 恆為 0）→ 96 組全被吃掉，
#       含 9pt 小字 1.75:1。
#     · Kindling 兩套主題 → 52 組被吃掉，其中 43 處是 Fg2 當正文
#       （「尚無片段。」「必填・點擊選圖」）深 2.47~3.09 / 淺 2.69~3.23，
#       而工具當時回報「破 1 / 偏低 2」，等於謊報健康。
#
#   現在改成兩個維度各自回答各自的問題，任何低於 AA 的都會被報出來：
#     等級（絕對對比）：破 <3.0 ／ 偏低 3.0~4.5 ／ OK ≥4.5
#     對稱（跨主題）  ：單一主題壞＝設計意圖沒跨主題保住
#                       兩邊皆低＝色票本身就不夠，換主題也救不了
#   「這是不是刻意的裝飾」只有看它在畫什麼才知道，工具不猜 —— 一律報出來由人判斷。
$singleTheme = -not ($pal.Values | Where-Object { $p = $_ -split ','; $p[0] -ne $p[1] })
if ($singleTheme -and $pal.Count -gt 0) {
    Write-Host "偵測到單一主題色盤：對稱欄位不適用，只看絕對對比" -ForegroundColor DarkGray
}
foreach ($r in $findings) {
    $worst = [Math]::Min($r.CDark, $r.CLight)
    $best  = [Math]::Max($r.CDark, $r.CLight)
    $gap   = [Math]::Round($best - $worst, 2)
    # 門檻依元素而異：一般文字 4.5、大字級 3.0、裝飾元素不設限（$Need = 0）
    $cat =
        if ($r.Need -le 0)          { '裝飾' }
        elseif ($worst -ge $r.Need) { 'OK' }
        elseif ($worst -lt ($r.Need * 0.67)) { '破' }
        else                        { '偏低' }
    $sym =
        if ($singleTheme)  { '單一主題專案' }
        elseif ($gap -lt 1.5) { '兩邊皆低' }
        else               { if ($r.CLight -lt $r.CDark) { '淺色壞' } else { '深色壞' } }
    $r | Add-Member -NotePropertyName Gap -NotePropertyValue $gap -Force
    $r | Add-Member -NotePropertyName Cat -NotePropertyValue $cat -Force
    $r | Add-Member -NotePropertyName Sym -NotePropertyValue $sym -Force
}

$findings | Group-Object Cat | Sort-Object Name | ForEach-Object { "  {0,-10} {1} 組" -f $_.Name, $_.Count }
""
# ⚠ v0.1 的第 6 號 bug（規劃書 8.2）：這裡曾用 Cat -ne 'OK'，把「裝飾」也算進
#   「低於 AA」—— 裝飾不適用 AA，算進來是虛胖的數字（CelFlow 曾報 80 = 破1+偏低1+裝飾78）。
$bad = @($findings | Where-Object { $_.Cat -in @('破','偏低') })
if ($bad.Count -gt 0) {
    "  低於 AA 的 $($bad.Count) 組，依成因細分："
    $bad | Group-Object Sym | Sort-Object Count -Descending | ForEach-Object {
        "    {0,-14} {1} 組" -f $_.Name, $_.Count
    }
    ""
    "  『兩邊皆低』代表色票本身就不夠亮，換主題救不了；"
    "  『深色壞／淺色壞』代表設計意圖沒跨主題保住。兩者都要處理，只是解法不同。"
    ""
}

foreach ($cat in @('破','偏低')) {
    $g = $findings | Where-Object Cat -eq $cat
    if (-not $g) { continue }
    # ⚠ @() 必要：PS 5.1 單一結果時 .Count 是空，曾印成「（ 組）」（規劃書 8.2 第 7 號）
    "===== $cat （$(@($g).Count) 組）====="
    foreach ($r in ($g | Sort-Object { [Math]::Min($_.CDark,$_.CLight) })) {
        $tag = "需 $($r.Need)"
        if ($r.Large) { $tag += " 大字級$($r.Size)" } elseif ($r.Size) { $tag += " $($r.Size)" }
        ('{0,-34} {1,-20} 字={2,-26} 底={3,-18} 深={4,6}:1 淺={5,6}:1  [{6}] {7}' -f `
            "$($r.File):$($r.Line)", $r.Elem, $r.Fg, $r.Bg, $r.CDark, $r.CLight, $r.Sym, $tag)
    }
    ""
}

# ⚠ v0.1 的第 8 號 bug（規劃書 8.2）：這段原本放在 exit 之後，永遠跑不到。
if ($ShowOk) {
    $g = @($findings | Where-Object Cat -eq 'OK')
    "===== OK（達標）：$($g.Count) 組，依色票配對彙整（前 30）====="
    $g | Group-Object Fg, Bg | Sort-Object { $_.Group.Count } -Descending | Select-Object -First 30 | ForEach-Object {
        $x = $_.Group[0]
        ('  字={0,-28} 底={1,-20} 深={2,6}:1 淺={3,6}:1   x{4}' -f $x.Fg, $x.Bg, $x.CDark, $x.CLight, $_.Group.Count)
    }
    ""
}

# ── CI 用的退出碼：有「破」就非零，讓它能當守門員 ──
# 裝飾元素不計入（它們本來就不適用文字對比標準）。
#
# ⚠ 必須用 @() 強制成陣列。PowerShell 5.1 裡 Where-Object 只篩出「一個」結果時
#   回傳的是單一物件而不是陣列，.Count 會失準 —— 實測「破 1 組」卻回報「全部達標」。
#   守門員靜默放行比沒有守門員更糟。
$fail = @($findings | Where-Object { $_.Cat -eq '破' }).Count
$warn = @($findings | Where-Object { $_.Cat -eq '偏低' }).Count
if ($fail -gt 0) {
    Write-Host "退出碼 1：有 $fail 組低於門檻的 2/3（另有 $warn 組偏低）" -ForegroundColor Red
    exit 1
}
if ($warn -gt 0) { Write-Host "退出碼 0：無「破」，但有 $warn 組偏低" -ForegroundColor Yellow }
elseif ($stats.pairs -eq 0) {
    # 0 組配對時「全部達標」是空掃綠燈 —— 沒有任何可稽核的項目，「達標」無意義。
    # 依規劃書 4.5 的輸出合約，這種情況預設要 exit 1；退出碼的改動留給 M3，話先喊。
    Write-Host "⚠ 退出碼 0，但解析出 0 組配對 —— 沒有可稽核的項目，此結果不代表達標（M3 起預設 exit 1）" -ForegroundColor Yellow
}
else { Write-Host "退出碼 0：全部達標" -ForegroundColor Green }
exit 0

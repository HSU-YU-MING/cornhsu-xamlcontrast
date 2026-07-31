# 原型（PowerShell）

這是 2026-07-30 在四個真實 WPF 專案上驗證過的可運作版本（M0 修正後）。
**移植成 .NET 之前，這支就是規格。**

> **凍結預告（治理決定，2026-07-31）**：M5 發佈後這支凍結、退役為歷史規格 ——
> 新規則改成 C# 先行，基準線由 C# 版產生。在那之前維持雙實作互證。

## 跑法（M1 之後：零設定）

```powershell
# 不給 -Theme 就自動偵測色盤 —— 這是預設路徑，四個專案都零設定可跑
powershell -ExecutionPolicy Bypass -File .\XamlContrastTree.ps1 `
  -Root "D:\應用程式\QuillNest\QuillNest"

# 加 -ShowOk 會列出通過的配對（依色票配對彙整，前 30 組）
```

四個驗證專案的根目錄與偵測到的形狀：

```
D:\應用程式\Kindling\Kindling.WPF        → C# 真相源（SystemThemeService.cs，33 色票）
D:\應用程式\QuillNest\QuillNest          → 主題配對（Dark+LightTheme.xaml，31 色票）
D:\應用程式\CelFlow\frontend\CelFlow.WPF → 單一主題（Styles\DarkTheme.xaml，48 色票）
D:\應用程式\Cornea                       → 無色盤（退回只算寫死色碼，會喊）
```

`-Theme kindling|quillnest|celflow|cornea` 仍可用，是驗證自動偵測的**對照組**：
兩種模式在四個專案上輸出逐行一致（除色盤來源行）。.NET 版不移植 `-Theme`。

自動偵測的順序與理由見 `../docs/config-schema.md`。
最關鍵的一條：**提供兩套主題的來源優先於只有一套的** —— Kindling 的
`Themes\DarkTheme.xaml` 有 33 個 brush 但只有深色值，真相源是 C# 陣列，
選錯來源淺色值就全是錯的。

退出碼：有「破」回傳 1，否則 0。可直接當 CI gate。
（0 組配對時會警告「此結果不代表達標」；依規劃書 4.5，M3 起這種情況預設 exit 1。）

## 基準線（M0 重出，2026-07-30）

`baseline-*.txt` 是四個專案在 M0 修正後的實測輸出。
移植成 .NET 之後，**新實作必須重現這些數字**，否則就是移植過程掉了東西。

```
QuillNest  30 檔  702 組配對  OK 696  破 2         裝飾  4  無法解析 55  死Fg排除 4
Kindling   15 檔  134 組配對  OK 130  破 1         裝飾  3  無法解析  0
CelFlow    22 檔  308 組配對  OK 283  破 3  偏低 2  裝飾 20  無法解析  4
Cornea      5 檔    0 組配對（無自訂色盤；無法解析 1、跳過 2）
```

QuillNest 的數字在「死 setter 過濾」（2026-07-30，M0 後追加）後更新：
Style 自帶的 ControlTemplate 裡若沒有任何 Foreground 消費者
（ContentPresenter/ContentControl/TextBlock/AccessText/Label、或 TemplateBinding
Foreground），Style 層 Foreground 不會被渲染，整組配對排除並計數回報。
這吃掉了先前的偏低 1（DataGridCheckBoxStyle：勾選框底色被配上根本不存在的標籤文字）
與 3 組原本誤入的 OK（EditableComboBoxStyle 外層 Style 收進巢狀 ComboBoxItem 的
觸發器——那幾組由巢狀樣式自己受檢，覆蓋沒有掉，用 -ShowOk 可驗證）。
其他三個專案在此規則前後輸出逐行一致。

破 6 組的組成：3 組是已知盲區的假警報（規劃書第 7 節：QuillNest×2 Style 背景、
CelFlow×1 alpha 疊層），**3 組是 v0.1 的 bug #5 吃掉的真問題**
（CelFlow 的 Style 觸發態：`Fg2` 疊 `Bg3` 2.16、White 疊 `Warn` 2.73 等）——
修好報告層之後才浮出來的。

v0.1 的舊基準線（含 bug #5/#6 的失真）保留在 `baseline-v01/`，只作歷史對照，
**不要拿它驗收移植**。

## 這支腳本裡值得保留的東西

程式碼裡的註解不是裝飾，是踩過的坑。移植時請一併帶走：

| 位置 | 內容 |
|---|---|
| 檔頭 | 「處理的情況 / 明確不處理的情況」——**不猜比猜錯好** |
| `Read-*Palette` 上方 | 色盤曾經寫死在腳本裡，專案改了色票工具卻用舊快照，稽核靜默失真 |
| `Walk` 的 `$op` 參數 | `Opacity` 是第三種讓文字變暗的方式，開 App 取像素才發現 |
| `$bg.Kind -eq 'other'` 那段 | 背景來自 Binding 時**不能往上繼承祖先背景**，那會把子元素的文字色配到錯的底色上 |
| 分級那段 | 「對稱＝刻意」是錯的啟發法，吃掉了 Kindling 52 組、CelFlow 96 組 |
| `$nonTextEls` | `ProgressBar` 的 `Foreground` 是進度條填色不是文字，用「有沒有 Foreground」判斷會誤判 |
| `$fail = @(...)` | PowerShell 5.1 裡 `Where-Object` 篩出單一結果時不是陣列，`.Count` 失準 —— **守門員靜默放行比沒有守門員更糟** |
| 大字級那段 | WPF 的 `FontSize` 是裝置獨立像素不是 point，換算要 ×0.75 |
| `WalkStyles` 的 `$fgDead` | 模板無 Foreground 消費者時 Style 層 Foreground 是死 setter，不參與配對。⚠ 不能只看 ContentPresenter——`Foreground` 是繼承屬性，模板裡的裸 `TextBlock` 一樣會吃到 |

## 已知限制

見規劃書第 7 節。簡述：

- 不合成「背景」的 alpha 疊層（會假警報）
- 看不到 Style setter 設定的背景（會假警報）
- 看不到 sibling 元素的背景（會假警報）
- Binding 來的顏色標為「無法解析」而不猜（**這是正確行為**）

## 編碼注意

腳本含中文註解，PowerShell 5.1 **只認 UTF-8 with BOM**。
用編輯器改完要確認 BOM 還在，否則直接 ParserError：

```powershell
$c = [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText($p, $c, (New-Object Text.UTF8Encoding($true)))
```

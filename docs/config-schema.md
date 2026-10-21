# xamlcontrast.config.json — 第一版 schema（M1 交付物）

> **狀態（2026-07-31）：已實作** —— CLI 自動從專案根目錄載入本檔；
> 值不合法（不是合法 JSON、型別錯、列舉值錯、範圍外、強制模式缺檔）會 exit 2，並指出出錯的欄位或區段
> （設定檔被靜默忽略等於使用者以為改了稽核標準而其實沒有）。
> ⚠ 目前有兩個例外：**拼錯的屬性名會被靜默忽略**（例：`"minCoverge": 99` 不報錯，下限仍是預設 50）；
> 區段、陣列或陣列元素給 `null` 可能讓 CLI 以未處理例外結束，而不是 exit 2（例：`"palette": null` 一定會；
> 完整清單見 [公開介面盤點](公開介面盤點.md) 3.3 節，未知屬性見 8.1／8.3 節）。修正前請對照本文確認欄位名沒有拼錯。
> 優先序：CLI 旗標 > config > 內建預設
> （`failOn`／`strictPalette` 的旗標只能加嚴，見文末「CLI 對應」）。
> 非預設門檻會在輸出中標明（`!! non-default thresholds`）。

> 2026-07-30。零設定是預設路徑；這個檔只在自動偵測猜錯或猜不到時用。
> 原則：**config 是逃生口，不是必經之路。** 四個驗證專案全部零設定可跑。

## 全貌

```json
{
  "palette": {
    "mode": "auto",
    "darkFile": "Resources/DarkTheme.xaml",
    "lightFile": "Resources/LightTheme.xaml",
    "csharpFile": "Services/SystemThemeService.cs",
    "csharpPattern": "\\(\"(?<key>\\w+)\"\\s*,\\s*\"(?<dark>#[0-9A-Fa-f]{6,8})\"\\s*,\\s*\"(?<light>#[0-9A-Fa-f]{6,8})\"\\)"
  },
  "thresholds": {
    "normalText": 4.5,
    "largeText": 3.0,
    "failFactor": 0.67
  },
  "classification": {
    "extraText": [],
    "extraNonText": []
  },
  "failOn": "fail",
  "strictPalette": false,
  "minCoverage": 50,
  "rootBackground": null,
  "ignore": {
    "requireReason": true
  }
}
```

## 欄位

### palette

| 欄位 | 預設 | 說明 |
|---|---|---|
| `mode` | `"auto"` | `auto` / `pair` / `csharp` / `single` / `none`。後四個是強制模式，對應自動偵測各路徑回報的模式（見下） |
| `darkFile` / `lightFile` | 自動 | `mode: "pair"` 時指定兩份字典；`mode: "single"` 只給 `darkFile`。其他模式下忽略 |
| `csharpFile` | 自動 | `mode: "csharp"` 時指定真相源檔案。其他模式下忽略 |
| `csharpPattern` | 內建三元組 | 具名群組 `key` / `dark` / `light` 的 regex（沒有這三個名字時退回 `k` / `d` / `l`）。內建值就是 Kindling 形狀：`("Key", "#深", "#淺")`。**只在 `mode: "csharp"` 生效**，自動偵測一律用內建樣式 |

自動偵測順序（與 README「Zero-config palette detection」同一份；最初四種形狀是
2026-07-30 用四個真實專案定下來的，兩條合併字典路徑是外部專案實查後加的）：

1. **合併字典・深淺俱全**：專案裡所有定義了具 `x:Key` 的 `Color`／`SolidColorBrush` 的 XAML 併成一份色盤，每個鍵深淺兩套值都有（`pair`）。
   通常就是各個 `ResourceDictionary`；⚠ 但目前**視窗只要在 `Window.Resources` 之類放了一個具 key 的區域 brush 也會被算進來**，
   於是它被當成色盤檔、整檔不受稽核（見清單下方「被認定為色盤來源」那段；已回報待修）
2. **配對**：dark 檔 × light 檔、brush 鍵集重疊 ≥ 50%（QuillNest 形狀；`pair`）
3. **C# 三元組**：深淺都有的來源，**優先於只有一套值的單一 XAML**（Kindling 形狀——
   它的 `Themes\DarkTheme.xaml` 有 33 個 brush 但只有深色值，真相源是 C# 陣列；
   選錯來源淺色值就全是錯的；`csharp`）
4. **合併字典・單一主題**：同 1 的聯集，但每個鍵只有一套值（`single`）
5. **單一主題**：唯一的色盤檔，深淺同值（CelFlow 形狀；`single`）
6. **無色盤**：退回只算寫死色碼，**大聲喊**（Cornea 形狀；`none`）

被認定為色盤來源（或其靜態複本）的 XAML 檔不進 UI 掃描。
反過來，路徑（相對掃描根目錄）中有資料夾或檔名以 `test`／`sample`／`demo`／`example` 開頭
（第二層以下另含 `fixture`／`mock`）的 XAML 不當色盤候選、也不算應用程式根元素，但照樣受稽核。

### thresholds

WCAG 2.x AA。`failFactor` 是「破」的分界（門檻 × `failFactor`，預設 0.67 ≈ 2/3）：
一般文字 破 < 3.015、偏低 3.015~4.5；大字級 破 < 2.01、偏低 2.01~3.0
（比值先四捨五入到小數兩位再比，所以 3.01:1 的一般文字是「破」）。
「非預設」是逐值精確比較：`0.667` 也算改了門檻。
改這些值等於改稽核標準 —— 允許，但輸出要標明用了非預設門檻。

### classification

`extraText` / `extraNonText`：加進內建分類表的元素名。內建表見
`src/XamlContrast.Core/Auditor.cs` 的 `TextEls` / `NonTextEls`
（`ProgressBar` 誤判事件的教訓：排除清單優先於「有沒有 Foreground」）。
元素名先比全名，對不上再比後綴、兩表都中時取較長的那個
（`MetroProgressBar` → `ProgressBar`、`ExtendedTextBlock` → `TextBlock`）。

### failOn / strictPalette

| 欄位 | 預設 | 說明 |
|---|---|---|
| `failOn` | `"fail"` | `"warn"` = 偏低也擋（法遵場景 AA 全過） |
| `strictPalette` | `false` | `true` = 色盤偵測失敗（退回寫死色碼模式）直接 exit 1 |
| `minCoverage` | `50` | 解析成功的配對低於此百分比即 exit 1 —— 只解析到一小部分的「通過」沒有意義。設 `0` 關閉 |
| `rootBackground` | — | 文件根元素的預設背景:色票鍵(`"MahApps.Brushes.Window.Background"`)或色碼(`"#FF202020"`)。給**主題函式庫的使用者**:視窗底色鍵在 repo 的主題檔裡,但「視窗 → 那個鍵」的隱含樣式住在 NuGet 套件裡,工具看不到。鍵不在偵測到的色盤裡 → exit 2。實測:NETworkManager 一行設定,覆蓋率 8.8% → 87.2% |

配對數為 0 時 exit 1 是**不可設定的預設** —— 空掃綠燈就是謊報健康。
`minCoverage` 是同一條原則的推廣:那條線原本是二元的,而八個公開專案實測有三個
在 0.2% / 1.7% / 10.2% 的覆蓋率下照樣亮綠燈(HandyControl 342 個檔只解析出 7 組,
然後印「all pairs meet AA」)。「幾乎什麼都沒看」與「看過了都沒問題」在退出碼上
分不出來,是同一種謊報。與 `strictPalette` 一樣在**所有模式**下生效,含 `--baseline`。

`strictPalette` 在**所有模式**下都生效，包含 `--baseline` 與 `--write-baseline`
（0.6 修正前它排在退出碼判定的最尾端，兩個 baseline 分支都會提早 return 而跳過它）。
這個組合特別要緊：色盤壞掉時色票配對全變 `unresolved`、從 findings 消失，
ratchet 會把「不見了」讀成「還清了」而放行 —— 弄壞主題檔看起來像修好了所有問題。
`--write-baseline` 同理會拒絕凍結一份用退化色盤算出來的基準線。

色票值只接受 `#RRGGBB` 與 `#AARRGGBB`。七位數之類的打字錯誤不會進色盤
（以前會被截前六位默默採用，算出一個看起來很篤定的錯比值）；
用到該鍵的地方改走 `summary.unresolved` 喊出來。

### ignore

`requireReason`（預設 `true`）：`<!-- xamlcontrast-ignore: 理由 -->` 沒理由視為無效並警告。
被壓掉的項目計入 `summary.suppressed`。

## CLI 對應

config 的 `failOn` / `strictPalette` 可被 CLI 旗標**加嚴**（`--fail-on warn`、`--strict-palette`），
但不能放寬：config 寫了 `"warn"`，`--fail-on fail` 不會把它改回 `fail`。
`minCoverage` 是例外：`--min-coverage` 兩個方向都會覆寫 config 的值。
CLI > config > 內建預設。

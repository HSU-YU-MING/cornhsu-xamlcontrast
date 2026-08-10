# xamlcontrast.config.json — 第一版 schema（M1 交付物）

> **狀態（2026-07-31）：已實作** —— CLI 自動從專案根目錄載入本檔；
> 寫錯會 exit 2 並給出欄位級錯誤訊息（設定檔被靜默忽略等於使用者以為改了
> 稽核標準而其實沒有）。優先序：CLI 旗標 > config > 內建預設。
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
    "failFactor": 0.667
  },
  "classification": {
    "extraText": [],
    "extraNonText": []
  },
  "failOn": "fail",
  "strictPalette": false,
  "ignore": {
    "requireReason": true
  }
}
```

## 欄位

### palette

| 欄位 | 預設 | 說明 |
|---|---|---|
| `mode` | `"auto"` | `auto` / `pair` / `csharp` / `single` / `none`。自動偵測的四種形狀（見下）各有對應的強制模式 |
| `darkFile` / `lightFile` | 自動 | `mode: "pair"` 時指定兩份字典；`mode: "single"` 只給 `darkFile` |
| `csharpFile` | 自動 | `mode: "csharp"` 時指定真相源檔案 |
| `csharpPattern` | 內建三元組 | 具名群組 `key` / `dark` / `light` 的 regex。內建值就是 Kindling 形狀：`("Key", "#深", "#淺")` |

自動偵測順序（2026-07-30 用四個真實專案定下來的，見原型）：

1. **配對**：dark 檔 × light 檔、brush 鍵集重疊 ≥ 50%（QuillNest 形狀）
2. **C# 三元組**：深淺都有的來源，**優先於只有一套值的單一 XAML**（Kindling 形狀——
   它的 `Themes\DarkTheme.xaml` 有 33 個 brush 但只有深色值，真相源是 C# 陣列；
   選錯來源淺色值就全是錯的）
3. **單一主題**：唯一的色盤檔，深淺同值（CelFlow 形狀）
4. **無色盤**：退回只算寫死色碼，**大聲喊**（Cornea 形狀）

被認定為色盤來源（或其靜態複本）的 XAML 檔不進 UI 掃描。

### thresholds

WCAG 2.x AA。`failFactor` 是「破」的分界（門檻 × 2/3）：
一般文字 破 < 3.0、偏低 3.0~4.5；大字級 破 < 2.0、偏低 2.0~3.0。
改這些值等於改稽核標準 —— 允許，但輸出要標明用了非預設門檻。

### classification

`extraText` / `extraNonText`：加進內建分類表的元素名。內建表見原型
（`ProgressBar` 誤判事件的教訓：排除清單優先於「有沒有 Foreground」）。

### failOn / strictPalette

| 欄位 | 預設 | 說明 |
|---|---|---|
| `failOn` | `"fail"` | `"warn"` = 偏低也擋（法遵場景 AA 全過） |
| `strictPalette` | `false` | `true` = 色盤偵測失敗（退回寫死色碼模式）直接 exit 1 |
| `minCoverage` | `50` | 解析成功的配對低於此百分比即 exit 1 —— 只解析到一小部分的「通過」沒有意義。設 `0` 關閉 |

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

config 的 `failOn` / `strictPalette` 可被 CLI 旗標覆蓋（`--fail-on warn`、`--strict-palette`）。
CLI > config > 內建預設。

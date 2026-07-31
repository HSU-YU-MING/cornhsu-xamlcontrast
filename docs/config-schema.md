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

配對數為 0 時 exit 1 是**不可設定的預設** —— 空掃綠燈就是謊報健康。

### ignore

`requireReason`（預設 `true`）：`<!-- xamlcontrast-ignore: 理由 -->` 沒理由視為無效並警告。
被壓掉的項目計入 `summary.suppressed`。

## CLI 對應

config 的 `failOn` / `strictPalette` 可被 CLI 旗標覆蓋（`--fail-on warn`、`--strict-palette`）。
CLI > config > 內建預設。

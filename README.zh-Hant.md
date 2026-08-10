# <img src="icon.png" width="28" alt=""/> XamlContrast

**XAML 原始碼的對比度靜態稽核。不用開 App、一次掃完整個專案、低於 WCAG AA 就讓 CI 紅燈。**

[English](README.md)

現有的桌面對比檢查全部是「執行期＋手動＋一次一個元素」。XamlContrast 反過來：
解析 XAML 原始碼、算出**每段文字實際疊在什麼顏色上**、深淺兩主題各算一次 WCAG 對比，
低於 AA 就讓 CI 失敗。

```bash
dotnet tool install -g Cornhsu.XamlContrast
xamlcontrast 你的專案路徑
```

> 需要 **.NET 10 執行環境**（解析本身是純 XML —— Linux CI 也跑得動）。
> 0.x 期間請 pin 確切版本（`--version 0.5.1`）；1.0 後介面凍結。

## 實際跑起來長這樣

對 [`samples/demo`](samples/demo)（刻意做壞的示範）跑一次：

```
palette: auto-detected theme pair: Themes\DarkTheme.xaml + Themes\LightTheme.xaml (6 keys)
files 1 | text-on-background pairs 8
exempted 1 disabled-state pair(s) (IsEnabled=False; WCAG 1.4.3 ...)
suppressed 1 pair(s) via xamlcontrast-ignore comments

  ok          3
  fail        3
  decorative  1

===== fail (3) =====
MainWindow.xaml:30  TextBlock                fg=White                     bg={Surface}  dark=16.67:1 light=   1:1  [light-fails] need 4.5 12px
MainWindow.xaml:26  TextBlock                fg={DynamicResource DimText} bg={Bg}       dark= 2.11:1 light= 1.6:1  [both-low]    need 4.5 12px
MainWindow.xaml:6   Style[HoverBtn]/trigger  fg=#9A9A9A                   bg={Surface}  dark= 5.92:1 light=2.81:1  [light-fails] need 4.5

exit 1: 3 pair(s) below threshold x 2/3 (0 warn)
```

工具的輸出是英文（跨語言使用者共用同一份 CI log）；這份說明文件是中文。

## 開發時它幫你什麼

- **秒級取代目測**：不用開 App、切兩個主題逐畫面用眼睛判斷 —— commit 前一行指令掃完全專案。
- **守住你沒在看的那個主題**：深色調到滿意時，淺色可能已掉到 1.1:1。每組配對深淺各算一次，對稱欄位直接指出哪邊壞。
- **檢查人工測不到的狀態**：hover／pressed／觸發態／`BasedOn` 鏈實際生效值 —— 某驗證專案的 23 筆真問題全是作者不知道存在的具名 Style 觸發態。
- **逼出健康的色票系統**：寫死色碼不跟主題走、一掃現形（實證：572 處寫死歸零）；baseline 記錄比值，色盤漂移也抓得到。
- **把品味之爭變成數字裁決**：「看得清嗎」吵不完，「2.54:1、差 1.96」可以直接做決定 —— 實戰中連修正方向的直覺都被數字推翻過一次。
- **成本前移到 PR**：貴的路徑是發行→被回報→全 App 回頭翻；便宜的是紅燈＋行內註記標在你剛改的那行。既有專案 `--write-baseline` 第一天就綠，只擋新增。

一句話：**它讓對比檢查升格成跟單元測試同級的東西 —— 改完就跑、壞了就紅、紅了就知道在哪一行。**

## 核心概念

每筆結果有兩個**獨立**維度：

- **等級**（絕對對比）：`fail`（< 門檻×2/3）／`warn`／`ok`／`decorative`
- **對稱**（跨主題）：`both-low`＝色票本身不夠，換主題救不了；
  `dark-fails`／`light-fails`＝設計意圖沒跨主題保住

對稱只回答「換個主題救不救得回來」，所以**只有 `fail` 與 `warn` 帶這個維度** ——
`ok` 與 `decorative` 在 JSON 裡整個不輸出這個欄位（`schemaVersion` 2 起）。
合格的配對沒有問題要救，把 21:1 標成 `both-low`（＝色票太弱）是胡說。

對稱**永遠不會**被拿來藏結果 ——「兩邊一樣糟」不是刻意的證據，只是糟兩次。
低於 AA 的一律報出來，由人判斷。

## 難的不是 WCAG 公式

公式只有 20 行。價值在「這段文字實際疊在什麼顏色上」的**十六條解析規則**，
每一條都是在正式發行的產品上踩出來的：Transparent 穿透、alpha 合成、Opacity 沿樹累乘、
ControlTemplate 子樹、Style setter 配對、觸發態、死 setter 過濾、具名／行內 Style
（含 BasedOn 鏈）、同條件觸發器合併、停用態豁免（WCAG 1.4.3）、半透明色票、
模板根背景、指向模板根的觸發 Setter。

## 零設定色盤偵測

自動判斷色盤在哪：主題配對（Dark+Light 兩檔）→ C# 真相源（三元組陣列，
**提供兩套主題的來源優先於只有一套的**）→ 單一主題字典 → 都沒有就退回只算寫死色碼
並**大聲講**。猜錯可用 `xamlcontrast.config.json` 指定（[schema](docs/config-schema.md)）。

## 進 CI

```bash
xamlcontrast src/MyApp --json report.json    # 有 fail 就 exit 1
xamlcontrast src/MyApp --fail-on warn        # 嚴格模式：全部要過 AA
xamlcontrast src/MyApp --sarif audit.sarif   # GitHub code scanning 格式
xamlcontrast src/MyApp --md report.md        # Markdown 報告（可貼 PR 留言）
```

退出碼：失敗 `1`、**解析出 0 組配對也是 `1`**（空掃不是通過）、用法錯誤 `2`、其餘 `0`。

**既有專案導入（baseline ratchet）**：第一天全紅的門會被關掉。先凍結既有債，
之後只擋新增與惡化 —— 債只准減不准增：

```bash
xamlcontrast src/MyApp --write-baseline xamlcontrast-baseline.json   # 跑一次，commit 進 repo
xamlcontrast src/MyApp --baseline xamlcontrast-baseline.json         # CI 裡
```

**刻意低對比的標記**（理由必填，壓掉會計數，不會靜默消失）：

```xml
<!-- xamlcontrast-ignore: 浮水印，刻意低對比 -->
<TextBlock Opacity="0.4" Text="草稿" ... />
```

### GitHub Action

```yaml
# .github/workflows/contrast.yml（你的專案）
name: Contrast
on: [pull_request]
jobs:
  xamlcontrast:
    runs-on: ubuntu-latest        # 解析是純 XML，不需要 Windows
    permissions:
      contents: read
      pull-requests: write        # 讓 action 把稽核報告貼成 PR 留言
      # security-events: write    # 只有在同時開 `sarif: true` 時才需要
    steps:
      - uses: actions/checkout@v4
      - uses: HSU-YU-MING/cornhsu-xamlcontrast@v0.5.1   # 0.x 期間 pin 確切版本
        with:
          root: src/MyApp
```

> **`pull-requests: write` 是 PR 留言能不能貼的關鍵。** 沒有它那一步會 403。
> 那步刻意設了 `continue-on-error`，所以把關照常運作、只是留言不見了 ——
> 但失敗是安靜的，所以值得一次設對。
> （fork 來的 PR 拿到的 token 一律唯讀，那是預期行為，不是設定錯誤。）

破的配對會**直接標在 PR 的那一行**，同時把稽核報告貼成一則 PR 留言（同一則反覆更新，不洗版）。
留言裡除了落差表，也會列出「沒被評估到的東西」——豁免、壓掉、解析失敗、色盤偵測退化，
因為這個工具最大的風險不是漏報，是謊報健康。

輸入：`root`（必填）、`working-directory`、`version`、`fail-on`、`baseline`、
`strict-palette`、`comment`（關掉 PR 留言）、`upload-report`、`sarif`。

## JSON 輸出

`--json report.json` 產出兩層報告：`findings`（每組配對一筆）加一個 `summary` 區塊，
裡面有**全部的退化計數** —— `paletteSource`、`unresolved`、`skipped`、`suppressed`、
`parseErrors`、`disabledExempt`。工具沒看到的東西，都是機器可讀的。
消費端請檢查 `schemaVersion`。

`unresolved` 在 `summary.unresolvedBy` 裡有原因細目 —— 光看總數看不出「補不補得回來」:

| 原因 | 意思 |
|---|---|
| `no-ancestor-background` | 祖先鏈上沒有任何一層宣告背景。常見於根容器靠 App 層隱含樣式給底,或資源字典裡的向量圖 |
| `bound-or-gradient` | 顏色來自 `{Binding}` / `{TemplateBinding}` / 漸層 —— 執行期才知道 |
| `unknown-palette-key` | 資源鍵不在偵測到的色盤裡 —— 打字錯誤,或色盤偵測漏了某個檔 |
| `translucent-uncomposited` | 半透明背景但底下沒有可疊的顏色 |

`summary.coverage` 是解析成功的比例。**低於 `--min-coverage`(預設 50%)會 exit 1** ——
只掃到一小部分的「通過」不算通過,這是「0 組配對即 exit 1」那條不可設定規則的推廣。
要關掉用 `--min-coverage 0`。

`--md report.md` 則產出給人讀的 Markdown（Action 用它貼 PR 留言，也可以自己拿去用）。

## 實戰成績

不是「demo 跑得動」——在**三個正式發行的 WPF 產品**上找到並促成修正 **250+ 處**真實對比問題：

| 專案 | 修正前 | 修正後 |
|---|---|---|
| CelFlow | 破 39／偏低 57 | **0**（停用態豁免 21） |
| Kindling | 破 41／偏低 14 | **真實問題 0**（餘 3 筆皆為已記錄假警報類） |
| QuillNest | 破 13／偏低 31 | **真實問題 0**（餘 7 筆同上） |

信任是怎麼掙來的（完整故事見[開發歷程回顧](docs/開發歷程回顧.md)）：

- **兩套獨立實作互證**：PowerShell 原型（規格）與 .NET 版在四個驗證專案上
  數字完全一致 —— 精確到每筆 fail 的 `file:line` 與退出碼，
  `scripts/verify-baselines.ps1` 隨時可重跑。
- **十六條解析規則沒有一條是在白板上設計的** —— 每條都來自真實的假警報或漏報實查
  (前十三條來自四個上線產品,後三條來自八個公開 WPF 專案、1909 個 XAML 檔的批次掃描),
  第 12 條（模板根背景）甚至是在驗收工具自己促成的修正時挖出來的。
- **開發期間工具自己出過八次「看起來健康但錯」的報告，每一次都變成回歸測試。**
  用血寫成的第一原則：**稽核工具最大的風險不是漏報，是謊報健康。**
  所有放過的東西 —— 豁免、排除、壓掉、解析失敗 —— 都計數、都喊出來。
- **殘餘的 fail 有名有姓、不藏**：每一筆都對應已記錄的假警報類（見已知限制），
  導入 CI 時由 baseline ratchet 承接。

## 已知限制

TargetName 指向模板**內部**元素（指向根的已解析）、跨元素同條件觸發器、
sibling 背景、圖片上的文字、隱含樣式；Binding 來的顏色標「無法解析」而不猜 ——
猜了就是拿誠實的不確定去換錯誤的信心。

## 另見

[**Parity**](https://github.com/HSU-YU-MING/cornhsu-parity) —— 同作者的姊妹專案，
同一套哲學（數值檢查、CI 把關）。Parity 回答「**實作跟設計稿一不一樣**」
（Figma vs 渲染後的真實數值）；XamlContrast 回答「**做出來的東西看不看得見**」。
一個守還原度、一個守可讀性，在同一張 PR 檢查清單上會合。

## 授權

MIT © [許彧銘 Hsu Yu-Ming](https://cornhsu.com/)

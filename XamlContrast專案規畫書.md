# XamlContrast 專案規劃書 v0.2

> 日期：2026-07-30（v0.2 同日修訂：補輸出合約 4.5、導入模式 4.6、里程碑 M0、補記 8.2）
> 狀態：**原型已驗證，尚未套件化**
> 一句話：**XAML 原始碼的對比度靜態稽核 —— 微軟自己的工具沒有的那個 CI 守門員。**

---

## 0. 核心原則

**靜態分析 + 全量掃描 + CI 退出碼。** 三者缺一就退回成既有工具的樣子。

現有的桌面對比檢查全部是「執行期 + 手動 + 一次一個元素」。這個工具反過來：不用開 App、
一次掃完整個專案、有「破」就讓 CI 紅燈。

---

## 1. 這是什麼，跟現有工具的界線

| 工具 | 做什麼 | 缺什麼 |
|---|---|---|
| **Accessibility Insights for Windows**（微軟官方） | 60+ 項自動檢查、UIA 樹、Tab 順序 | **對比度是手動的** —— 懸停測單一元素或滴管取兩個像素。無全 App 掃描、無清單、無 CI |
| **AccChecker** | UIA / MSAA / ARIA 驗證、缺 Name 檢查 | 不做對比度 |
| **CCA（Colour Contrast Analyser）** | 螢幕取色比對 | 純手動，一次兩個顏色 |
| **Rapid XAML** | XAML 靜態分析、可寫自訂規則、有 CI 用的 NuGet | 規則裡**沒有**對比度／無障礙 |
| **axe / Lighthouse / WebAIM** | 網頁自動掃描 | 不吃 XAML |

2026-07-30 查證：GitHub 搜「xaml contrast accessibility」→ **0 個 repo**。
NuGet / npm / GitHub 的 `XamlContrast` 精確名稱**全部未被使用**。

微軟官方文件對它的「自動偵測」模式明說：*不建議用在圖示或其他圖形元素上，
信心不足或偵測失敗時要改手動*。它從來不會產出「這個 App 有 74 處低於 AA」的清單。

**空缺是真的。**

### 1.1 誰在什麼階段用

| 角色 | 階段 | 摩擦 |
|---|---|---|
| 開發者 | 改完 UI，commit 前 | 一行指令，秒級 |
| CI | PR gate | 退出碼非零就擋 |
| 設計/QA | 交付前盤點 | 讀 JSON 或報表 |

不取代 Accessibility Insights —— 那個管 UIA 樹、Name、鍵盤。這個只管「看不看得見」，
但把它做到自動化。兩者互補。

---

## 2. 名詞對照

| 詞 | 意思 |
|---|---|
| **配對（pair）** | 一組「文字色 × 生效背景色」。工具的基本單位 |
| **生效背景（effective background）** | 沿視覺樹往上找到的、真正會被畫出來的底色 |
| **色盤（palette）** | 專案的色票定義。深色/淺色兩套值 |
| **裝飾（decorative）** | `Rectangle` 分隔線、`ProgressBar` 等非文字元素，不適用文字對比標準 |
| **對稱（symmetry）** | 深淺兩主題是否同時失敗。只回答「跨主題有沒有保住」，**不回答「是不是刻意的」** |

---

## 3. 範圍邊界

### 做

- 解析 XAML 樹，算出每個文字元素的生效背景
- WCAG 2.x 對比度（AA 4.5:1 / 大字級 3:1）
- 深淺雙主題各算一次
- 文字 vs 裝飾自動分類
- CI 退出碼 + JSON 輸出

### 不做（v1 明確排除）

- **執行期取樣** —— 那是 Accessibility Insights 的地盤
- **UIA / Name / 鍵盤順序** —— 同上
- **自動修** —— 換哪個色票是設計決定，工具只報不改
- **圖片上的文字** —— 靜態分析看不到圖片內容
- **色盲模擬** —— 不同的問題
- **APCA / WCAG 3** —— 以 WCAG 2.x AA 為準；WCAG 3 尚未定案，不追草案，定案後再評估

---

## 4. 系統架構

### 4.1 真正的難題不是 WCAG 公式

WCAG 相對亮度與對比度公式**只有 20 行**，網路上到處都是。
這個工具的價值全部在**「這段文字實際疊在什麼顏色上」**：

```
沿父節點往上找生效背景
  Background="Transparent" 要穿透，繼續往上找
  背景是半透明 ARGB 要與下層合成
  Opacity 要沿樹累乘（Border 0.5 裡的 TextBlock 0.5 = 0.25）
  ControlTemplate 自成一個子樹（模板根的背景管到模板內的文字）
  Style 的 Background/Foreground 兩個 Setter 要配對
  Trigger 內的 Setter 要用所屬 Style 的另一半當對照
  Style 模板內無 Foreground 消費者 → 死 setter，整組排除並計數
    （無 ContentPresenter/ContentControl/TextBlock/AccessText/Label、
     也無 TemplateBinding Foreground 時，Style 層 Foreground 不會被渲染）
  元素套用的具名／行內 Style：跨檔索引（同檔優先，模擬 WPF 資源查找）、
    BasedOn 鏈揉平、Style/模板觸發器逐狀態檢、同條件觸發器跨節點合併
  IsEnabled=False 觸發態＝停用態：WCAG 1.4.3 豁免，計數回報、不評分
  半透明「色票」（palette 鍵的值是 #AARRGGBB）同樣疊底合成，深淺各帶自己的 alpha
  模板根元素自帶的 Background：Style 層沒有 Background setter 時的真正底色
    （Kindling CellDeleteBtn 實證：白 ✕ 疊模板根的 DangerSolid 紅底，
     少這條會誤配祖先背景報 1:1 假警報）
  TargetName=模板根 的觸發 Setter 等同設在宿主上；且它直接設在根元素、
    蓋過經 TemplateBinding 進來的宿主本地值（指向內部元素的 TargetName 仍不做）
```

這十三條是 2026-07-30～31 用四個真實專案一條一條踩出來的（第八條來自 QuillNest
DataGridCheckBoxStyle 誤報實查、九～十一條來自 CelFlow 觸發態實查（4929b69）、
十二～十三條來自 Kindling/QuillNest 修正驗收的殘餘假警報實查；
第十三條是**原型凍結後第一條 C# 先行的規則**，驗證鏈自此改為 C# 快照回歸
—— `baselines/*.json`＋`verify-baselines.ps1 -Update`）。
**任何抄公式的實作都會漏掉它們。**

### 4.2 色盤偵測（v0.2 的頭號任務）

**目前的最大阻礙**：原型有三個寫死的色盤讀取器。

```powershell
-Theme kindling    → 讀 Services/SystemThemeService.cs 的 C# 陣列
-Theme quillnest   → 讀 Resources/DarkTheme.xaml + LightTheme.xaml
-Theme celflow     → 讀 Styles/DarkTheme.xaml（單一主題）
```

**每加一個專案就要改一次腳本 —— 這是套件化的反面。**

要做的是自動偵測：掃專案裡所有 `ResourceDictionary`，找 `<Color x:Key>` 與
`<SolidColorBrush x:Key>`，推斷哪幾份是「同一組色票的不同主題版本」（鍵集重疊度高
＋檔名或資料夾有 Dark/Light 線索）。找不到就退回「只算寫死色碼」模式。

也要支援 C# 端的色盤（Kindling 那種真相源在 `.cs` 陣列裡的），
但要用可設定的樣式而不是寫死。偵測失敗時的手動指定、C# 擷取樣式、
門檻與分類覆寫，全部走 `xamlcontrast.config.json` —— schema 隨 M1 一起凍出第一版。

**已知簡化 —— 資源作用域**：偵測把色盤當成全專案共用的一張表，但 XAML 實際是
詞法作用域 —— 同一個 key 可以在不同檔案的區域 `ResourceDictionary` 給不同值，
App.xaml 的 merge 順序也會影響誰生效。v1 不解作用域，但**偵測到同鍵不同值時
必須明講**（列出衝突的鍵與檔案），不可默默取其一 —— 否則這會成為下一個
「主動宣告錯誤結論」的來源（第 8.1 節）。

### 4.3 分級

```
門檻依元素而異：
  一般文字   4.5:1
  大字級     3.0:1   （≥18pt 或 ≥14pt 粗體）
  裝飾元素   不設限

⚠ WPF 的 FontSize 是「裝置獨立像素」(1/96 吋) 不是 point(1/72 吋)，換算要 ×0.75。
   18pt = 24px、14pt = 18.67px。少了這一步會把 18px 的字誤判成大字級而放過它。

分級：
  ≥ 門檻          OK
  < 門檻 × 2/3    破
  其間            偏低

兩級門檻換算出來的實際分界（移植時照抄，不要現場心算）：
  一般文字（需 4.5）：破 < 3.0，偏低 3.0 ~ 4.5
  大字級  （需 3.0）：破 < 2.0，偏低 2.0 ~ 3.0
```

### 4.4 對稱是獨立維度，不能拿來當「刻意」的證據

**原型犯過的最嚴重錯誤**：把「深淺兩主題都低而且差不多」判定成
『刻意低對比（設計意圖）』**並藏起來**。

實證後果：
- Kindling 被回報成「破 1 / 偏低 2」，實際有 **52 組**低於 AA 被吃掉，
  其中 44 處是 `Fg2` 當正文（「尚無片段。」「必填・點擊選圖」）
- CelFlow 單一主題（深淺同值，差距恆為 0）→ **96 組全被吃掉**，含 9pt 小字 1.75:1

**對稱不是意圖的證據 —— 兩邊一樣糟就只是兩邊都糟。**
「這是不是刻意的裝飾」只有看它在畫什麼才知道，工具不猜，一律報出來由人判斷。

現在改成兩個維度各自回答各自的問題：
```
等級（絕對對比）：破 / 偏低 / OK / 裝飾
對稱（跨主題）  ：兩邊皆低（色票本身不夠，換主題救不了）
                  深色壞 / 淺色壞（設計意圖沒跨主題保住）
```

### 4.5 輸出合約（JSON、退出碼、語言）

第 8.1 節的規則 3（退化要用喊的）必須落到機器可讀的通路上，不能只在 console。

**JSON 分兩層。** findings 陣列之外要有 summary 區塊 —— CI 只消費 findings 的話，
退化資訊等於不存在：

```json
{
  "summary": {
    "paletteSource": "project | config | builtin-fallback | none",
    "files": 30, "pairs": 706,
    "unresolved": 55, "skipped": 9, "suppressed": 0,
    "counts": { "fail": 2, "warn": 0, "ok": 589, "decorative": 115 }
  },
  "findings": [ ... ]
}
```

**退出碼政策要可設定，且要堵住「空掃綠燈」：**

| 情況 | 預設 | 可設定 |
|---|---|---|
| 有「破」 | exit 1 | — |
| 只有「偏低」 | exit 0 | `--fail-on warn` 改成 exit 1（法遵場景要 AA 全過） |
| 配對數為 0 | **exit 1** | 一次重構讓所有配對變無法解析，綠燈放行就是謊報健康 |
| 色盤偵測失敗、退回只算寫死色碼 | exit 0＋大聲警告 | `--strict-palette` 改成 exit 1 |

**語言：CLI 輸出與 JSON 欄位值一律英文。** JSON 的 category 是給 CI 腳本 match 的，
不能是「破」；console 訊息跟著英文走，中文只留在 README.zh-Hant。
（原型全中文輸出，移植時一併處理；分級詞對應：破=fail、偏低=warn、裝飾=decorative。）

### 4.6 導入既有專案：baseline 與 ignore

本規劃書自己的數據（第 8 節）說明真實專案起跑就是破 39~55 組。
「零破才給過」的 gate 第一天全紅，工具會被關掉 —— 這是風險表
「假警報多到沒人信」的另一半。Parity 為同一個問題做了 baseline 模式（其 M5），照搬：

**baseline / ratchet**：第一次跑產生已知債清單（存檔進 repo），之後只擋
「新增或惡化的破」；債只准減不准增。沒有它，工具只能用在新專案
或已經修完的專案 —— 也就是只有作者自己。

**ignore 註解**：

```xml
<!-- xamlcontrast-ignore: 浮水印文字，刻意低對比 -->
<TextBlock ... />
```

- 管**下一個元素**（含其子樹），不管整個檔
- **理由必填** —— 沒理由的 ignore 視為無效並警告
- 被壓掉的項目**計入 `summary.suppressed` 並可列出** —— 否則 ignore
  就是新開的一條靜默退化通道（第 8.1 節規則 3）

---

## 5. Repo 結構（照 Parity）

```
cornhsu-xamlcontrast/
├── src/
│   ├── XamlContrast.Core/        解析引擎（樹走訪、色盤、WCAG）
│   └── XamlContrast.Cli/         CLI 外殼
├── tests/
│   └── XamlContrast.Tests/       ⚠ 原型沒有測試，這是 v0.2 的必要條件
├── samples/
│   └── demo/                     可直接跑的範例專案（含刻意的失敗案例）
├── docs/
├── npm/                          npm 包裝
├── .github/workflows/            ci.yml + release.yml
├── action.yml                    GitHub Marketplace Action
├── README.md / README.zh-Hant.md
├── CHANGELOG.md
├── ROADMAP.md
├── RELEASING.md
├── Directory.Build.props
└── LICENSE                       MIT
```

---

## 6. 里程碑

### M0 — 修原型報告層＋重出基準線 ✅ 2026-07-30 完成
第 8.2 節的四個報告層 bug，其中兩個直接汙染基準線 —— 不先修，
M2 的驗收就是拿一個已知有洞的規格去對數字。
1. `WalkStyles` 產出的配對補 `Need` 欄位（現在全被 `$null -le 0` 判成裝飾）
2. 「低於 AA 的 N 組」排除裝飾
3. 「破／偏低」標題的 `.Count` 補 `@()`
4. `-ShowOk` 區塊移到 `exit` 之前（或砍掉）

**驗收**：重跑四個專案 —— 含目前**缺基準線的 Cornea**（現有 baseline 只有三份）——
產出四份 `baseline-*.txt`，數字經人工抽查後取代舊檔。

### M1 — 色盤自動偵測 ✅ 2026-07-30 原型版完成（四專案零設定、輸出與基準線逐行一致；schema 見 docs/config-schema.md）
砍掉 `-Theme` 參數。掃 ResourceDictionary 推斷主題配對。找不到就退回只算寫死色碼。
交付物含 `xamlcontrast.config.json` 的第一版 schema：手動指定色盤檔案、
C# 色盤擷取樣式、門檻與分類規則覆寫 —— 第 4.2、4.6 節與風險表都依賴這個檔。
**驗收**：四個真實專案（Cornea / Kindling / QuillNest / CelFlow）零設定跑得出結果，
且數字與 **M0 重出的** baseline 一致。

### M2 — 移植成 .NET + 測試 ✅ 2026-07-31 完成（33 測試全過；四專案語意比對與基準線一致，scripts/verify-baselines.ps1）
PowerShell 原型 → C#。用 `System.Xml.Linq` 走樹（原型已經是這樣，移植成本低）。
補測試：
- 每條解析規則（Transparent 穿透、alpha 合成、Opacity 累乘、ControlTemplate、
  Style setter 配對、死 setter 過濾）各一個最小案例
- **八個「謊報健康」形狀各一個回歸測試**（第 8.1 節四個＋第 8.2 節四個）——
  這個工具的歷史說明它最容易壞在報告層，不是解析層

**驗收方式（2026-07-30 回頭檢視後修正）**：不逐位元組複刻原型的中文輸出
（那個格式 M3 就要換掉，位元組級複刻是花力氣驗證一個即將丟棄的東西）。
C# 版直接實作 M3 的輸出合約（英文＋JSON），驗收改為**語意比對**：
從 M0 基準線抽出數字（檔案數／配對數／各分級數／無法解析／跳過／
破與偏低的每筆 file:line＋比值），與 C# 版 JSON 對數字。
M2 與 M3 的輸出工作因此合併交付。

### M3 — 輸出合約落地 ✅ 2026-07-31 隨 M2 一併完成（JSON summary＋findings、--fail-on／--strict-palette、0 配對 exit 1、全英文輸出）
照第 4.5 節：JSON 含 summary 區塊（paletteSource / unresolved / skipped /
suppressed / counts）、findings 欄位含 file/line/element/fg/bg/
ratio-dark/ratio-light/threshold/category/symmetry、退出碼政策
（`--fail-on warn`、配對數 0 即 exit 1、`--strict-palette`）、輸出全英文。

### M4 — 導入模式（baseline + ignore）✅ 2026-07-31 完成（--baseline／--write-baseline、ignore 理由必填＋計數；鍵不含行號防漂移、記錄比值防色盤漂移型惡化；41 測試全過）
照第 4.6 節：baseline/ratchet（只擋新增與惡化）、`xamlcontrast-ignore` 註解
（理由必填、計入 suppressed）。這是既有專案唯一的導入路徑。

### M5 — GitHub Action ✅ 2026-07-31 完成（repo 公開、CI 兩次全綠、v0.1.0 以 Trusted Publishing 發佈至 NuGet）
`action.yml`，照 Parity 的形狀。0.x 期間 README 要明寫 pin 到確切版本。

**治理決定（2026-07-31）**：M5 發佈後**凍結原型** —— PS 腳本退役為歷史規格，
新規則改成 C# 先行（測試齊備），基準線由 C# 版產生。
否則每條規則要寫兩遍，原型會長成第二個要永久維護的產品。
在那之前維持雙實作互證（verify-baselines.ps1）。
加 GitHub annotations（`::error file=…,line=…`）讓破的配對直接標在 PR 的行上；
SARIF 視需求後補。解析不依賴 WPF runtime（純 `System.Xml.Linq`），
CLI 與 Action 都能跑 `ubuntu-latest` —— 這是實質賣點，README 要明寫。

### M6 — 擴到 XAML 生態
WinUI 3 / Uno / Avalonia 也吃 XAML，解析邏輯大致共用，差在色盤慣例與內建樣式。
**這是比只做 WPF 大得多的市場。**

---

## 7. 已知盲區（誠實清單，要寫進 README 的 Known limitations）

| 盲區 | 具體案例 | 影響 | 處置 |
|---|---|---|---|
| ~~不合成「背景」的 alpha 疊層~~ | CelFlow `TimelineView:542`，`InfoL` 疊 16% alpha 的 `BlueOverlay` | 假警報 | ✅ **已修**（4929b69 半透明色票合成：色票鍵的 8 碼值走疊底合成） |
| ~~看不到 Style 設定的背景~~ | QuillNest `ProjectView:326` 等，徽章底色由 Style setter 給 | 假警報（報成白配白 1:1） | ✅ **已修**（4929b69 具名/行內 Style 解析：跨檔索引、BasedOn 揉平、觸發器逐狀態）。殘餘限制見下兩列 |
| ~~TargetName 的模板 Setter（指向模板根）~~ | QuillNest `NoteEditorWindow`×3、`CalendarView:234`；Kindling `ProjectWallView:152/156` hover | 假警報（6 筆） | ✅ **已修**（規則 13，C# 先行）—— 指向模板根＝等同設在宿主，且蓋過 TemplateBinding 的本地值。六筆全數消除 |
| **TargetName 指向模板內部元素** | 換的是內部零件的底，與宿主文字的疊層關係要解版面 | 漏報（不猜） | **維持不做** —— 與 sibling 背景同類，超出靜態逐元素分析 |
| **跨元素同條件觸發器** | QuillNest `TodoView:115/262`、`CalendarView:783` —— Border 的底與 TextBlock 的字由同一個 DataTrigger 條件同步翻，字底分屬兩元素 | 假警報（4 筆，同上實查；原始碼有註解證明是刻意設計） | **v1 不修** —— 要理解觸發條件的相關性，超出靜態逐元素分析 |
| **看不到 sibling 元素的背景** | Kindling `TimelineView:368`，「空」標記的底是同層 `<Border Background="Black"/>` 而非祖先 | 假警報 | **v1 不修** —— 要解版面疊層，超出靜態分析的合理範圍 |
| **Binding / TemplateBinding 的顏色** | 標籤徽章底色綁使用者選的顏色 | 標為「無法解析」而不猜 —— **這是正確行為**，猜了會誤判 | **維持現狀** —— 不猜是設計決定 |
| **圖片上的文字** | 靜態分析看不到圖片內容 | 漏報 | **不修** —— 明寫進 Known limitations |
| **隱含樣式**（無 x:Key 的 TargetType Style 自動套用） | — | 漏報 | **留給 .NET 版**（4929b69 記在檔頭） |

「有東西但工具看不到」與「工具知道自己不知道」是兩種等級 ——
**後者比前者好**，它不會給出錯誤的信心。
處置欄要同步進 README 的 Known limitations，讓讀者分得出「暫時」與「永久」。

另：4929b69 新增**停用態豁免** —— `IsEnabled=False` 觸發態計數回報、不評分
（WCAG 1.4.3 明文豁免停用控制項）。這是「可以放過」的邏輯，但放行憑據是
WCAG 條文而非啟發法，且有計數（`已豁免 N 組`）—— 符合 8.1 的三規則。

---

## 8. 原型的實測成績（2026-07-30）

在四個真實 WPF 專案上跑過，全部是正式發行中的產品：

| 專案 | XAML 檔 | 解析出配對 | OK | 破 | 偏低 | 裝飾 | 無法解析 |
|---|---|---|---|---|---|---|---|
| QuillNest | 30 | 706 | 589 | 2 | 0 | 115 | 55 |
| Kindling | 15 | 134 | 114 | 1 | 0 | 19 | 0 |
| CelFlow | 22 | 308 | 228 | 1 | 1 | 78 | 4 |

**剩下的 4 組「破」全部是上表列出的已知盲區造成的假警報。**

修正前的真實數字（工具修好之後才量得到）：
```
QuillNest  破 13 / 偏低 31
Kindling   破 41 / 偏低 14     ← 135 組配對裡 55 組低於 AA，密度最高
CelFlow    破 39 / 偏低 57
```

實際促成的修正：
- CelFlow 572 處寫死色碼 → 0，色票 13 → 48 個
- Kindling 48 處 `Fg2` 當正文 → `Fg1`
- QuillNest 26 處彩色按鈕底 → 新增的按鈕色票
- 三個專案共同的結論：**`Fg2`（第三階暗文字）在深色底上不可能過 AA，只該給停用態用**

### 為什麼這個成績值得寫進 README

它不是「在 demo 專案上跑得動」，是**在三個正式發行的產品上找到並修好了 100+ 處真實問題**，
而且過程中發現並修掉工具自己的四個 bug（謊報健康、單一主題盲區、
`ProgressBar` 誤判成文字、退出碼靜默放行）。

Parity 的 README 寫「validated against a real full-site migration」—— 同一個標準。

---

## 8.1 開發期間工具自己出過四次錯 —— 每一次都是「謊報健康」

這一節是整份文件最該被記住的部分。原型在 2026-07-30 這一天內，
**四次給出「看起來很健康」但實際是錯的報告**：

| # | 錯誤 | 後果 |
|---|---|---|
| 1 | 把「深淺兩主題都低」判成『刻意低對比』並藏起來 | 回報 Kindling「破 1 / 偏低 2」，實際 55 組低於 AA |
| 2 | 單一主題專案深淺同值 → 差距恆為 0 → 全落入上述判定 | CelFlow 96 組全被吃掉，含 9pt 小字 1.75:1 |
| 3 | 用「有沒有 Foreground」判斷是不是文字 | `ProgressBar` 的填色被當文字，用 4.5 去要求一條進度條 |
| 4 | PowerShell 5.1 的 `Where-Object` 篩出單一結果時不是陣列 | `.Count` 失準，「破 1 組」卻回報「全部達標」—— **守門員靜默放行** |

四次的共同形狀：**不是漏掉某個功能，是主動宣告了一個錯誤的結論。**

### 這對設計的約束

**稽核工具最大的風險不是漏報，是謊報健康。** 漏報只是少做事；
謊報會讓人停止檢查，比沒有工具更糟。

由此推導出三條硬規則，應寫進 CONTRIBUTING：

1. **任何「這個可以放過」的邏輯，都要先問：它會不會讓真實問題消失在報告裡。**
   第 1、2 次錯誤都出在這裡 —— 「對稱＝刻意」是一個看似合理的放過理由。
2. **不確定就報出來，讓人判斷；不要替使用者決定什麼叫「刻意」。**
   工具能回答「對比是多少」，不能回答「這是不是故意的」—— 後者要看它在畫什麼。
3. **退化要用喊的。** 色盤偵測失敗、無法解析背景、跳過的項目，
   都必須出現在輸出裡並計數。靜默退化等於謊報。

第 3 次錯誤另有一條教訓：**分類規則要有明確的排除清單，不能只靠「有沒有某個屬性」。**
`Foreground` 在 `ProgressBar` 上根本不是文字色。

---

## 8.2 補記：移植前的 code review 又找到四個（2026-07-30 稍晚）

規劃書 v0.1 寫完後、動工移植前，對原型做了一次逐行 review，又找到四個報告層問題。
列在這裡是因為其中兩個**直接汙染第 8 節的基準線**（→ M0）：

| # | 問題 | 形狀 |
|---|---|---|
| 5 | `WalkStyles` 產出的配對沒有 `Need` 欄位，分級用 `$r.Need -le 0` 判裝飾，而 PS 裡 `$null -le 0` 為 True → **所有 Style 配對被靜默歸為裝飾、不受檢** | 謊報健康（同第 1、2 次的形狀）；**汙染基準線** |
| 6 | 「低於 AA 的 N 組」用 `Cat -ne 'OK'` 篩選，把裝飾算進去（CelFlow 的 80 = 破 1＋偏低 1＋裝飾 78） | 虛胖的數字；**汙染基準線** |
| 7 | 「破／偏低」標題的 `$g.Count` 沒包 `@()`，單一結果時印成「（ 組）」—— baseline 檔裡就看得到 | 顯示錯誤（退出碼那處已有 `@()`，守門員本身沒事） |
| 8 | `-ShowOk` 區塊在 `exit 0` 之後，永遠跑不到，但 prototype/README 記載了這個參數 | 死碼＋文件失真 |

第 5 個值得特別記：它就是 8.1 節警告的形狀 —— 一個看不見的預設值
（缺欄位 → null → 裝飾）讓一整類配對消失在報告裡，而且沒有任何輸出提示。
**回歸測試清單因此從四個變八個（M2）。**

---

## 9. 風險表

| 風險 | 影響 | 對策 |
|---|---|---|
| **色盤慣例太發散** | 自動偵測失敗率高 | 允許 `xamlcontrast.config.json` 手動指定（schema 隨 M1 交付）；偵測失敗要**明講**而不是靜默退化 |
| **假警報多到沒人信** | 工具被關掉 | 已知盲區寫進 README（含處置欄）；`<!-- xamlcontrast-ignore -->` 註解＋baseline 模式（第 4.6 節，M4） |
| **裝飾/文字分類不準** | 誤擋 PR | 分類規則要可設定；`ProgressBar` 那個誤判已經是前車之鑑 |
| **市場太小** | 沒人用 | XAML 生態（WinUI/Uno/Avalonia）比純 WPF 大；且無障礙法遵在部分市場是硬需求 |
| **微軟補上這塊** | 被取代 | 他們的產品線是執行期工具，靜態分析不在同一條路上；真被補上也證明方向對 |

---

## 10. 版本策略（照 Parity）

**留在 0.x 直到介面凍結。**

Parity 的 README 明寫：`@v0.9.7  # 0.x 時 pin 到確切版本；1.0 後改用 @v1`。
同樣做法。

1.0 的條件（照 Parity 的「1.0 介面凍結審查」）：
- [ ] 色盤自動偵測在四個專案上零設定可用
- [ ] CLI 參數與 JSON schema（**含 summary 區塊**）不再預期變動
- [ ] 測試涵蓋十三條解析規則＋八個「謊報健康」回歸形狀
- [ ] baseline 模式與 ignore 註解至少在一個既有專案上實際用過
- [ ] 至少一個非自己的專案用過並回報
- [ ] **重新辯論預設 gate**：目前 warn（3.0~4.5，低於 AA）預設放行 ——
      合規視角很難辯護，傾向 1.0 改成預設 `--fail-on warn`、
      導入友善的職責交給 baseline 機制（2026-07-31 工程師視角檢視記錄）

**起始版本 0.1.0。**

---

## 11. 命名（定案）

```
產品名 / repo    XamlContrast
NuGet            Cornhsu.XamlContrast
npm              cornhsu-xamlcontrast
GitHub           HSU-YU-MING/cornhsu-xamlcontrast
```

**套件形態（定案，照 Parity）**：dotnet global tool（`PackAsTool`，指令名
`xamlcontrast`，套件掛 `Cornhsu.*` 前綴）；npm 版是自帶執行環境的原生執行檔，
只下載當前平台那一份。

2026-07-30 查證：三個通路的精確名稱全部未被使用。

選它而不是抽象名（Discern / Legible / Perceivable）的理由：
**這個工具要開創一個沒人做過的類別，最大的風險不是名字不夠優雅，是沒人知道要搜什麼。**
`XamlContrast` 本身就是搜尋詞，而這個詞現在沒有主人。

（Parity 能用抽象名，是因為它有 GitHub Marketplace 分類幫忙曝光。）

**取捨**：名字綁死 XAML。但 WinUI 3 / Uno / Avalonia 都吃 XAML，反而是加分；
只有擴到 WinForms 之類才會變成限制 —— 而那不在路線圖上。

---

## 下一步

1. **M0 修原型報告層、重出四份基準線（含 Cornea）** ——
   半天的事，但不做，後面全部踩在錯的數字上
2. **M1 色盤自動偵測＋config schema** —— 沒有它就不能算套件
3. 移植成 .NET + 補測試（M2）
4. 開 GitHub repo、搬 Parity 的 CI / RELEASING 骨架

原型在 `prototype/`，可直接跑，見該資料夾的 README。

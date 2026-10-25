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
> **1.x 凍結的是語法，不是稽核結果。** CLI 旗標、退出碼、JSON 報告、Action inputs、config 檔、baseline 檔的格式、
> SARIF 規則、壓抑註解、稽核範圍在 2.0 之前只增不改（[完整清單](CHANGELOG.md)）；實驗性的 `--push` 還不在內
> （見下方「推送到 Parity 儀表板」）。稽核**找到什麼**可以在 minor 改進 ——
> 計數會動、build 可能因此變紅，每一版都會寫在 CHANGELOG 的 Upgrading。baseline 鍵的寫法改變時遞增 `keyVersion`，
> 舊的 baseline 會被拒並提示重寫。從 0.x 升級？先看 [CHANGELOG 的 1.0.0](CHANGELOG.md)，裡面列了必須改的地方。

> **匿名使用統計預設打開**（1.1 起）：每次稽核之後送一筆只有計數與比值區間的資料 —— 沒有檔名、路徑、元素名稱或顏色 ——
> 做為碩士論文的補充證據。[送什麼、怎麼關](#匿名使用統計)（設 `DO_NOT_TRACK=1` 就夠了）。

## 實際跑起來長這樣

對 [`samples/demo`](samples/demo)（刻意做壞的示範）跑一次：

```
palette: merged colour dictionaries: 2 file(s), 6 keys (dark + light)
files 1 | text-on-background pairs 8 | coverage 100.0% of pairs seen
unresolved (colour bound at runtime / gradient / key not in palette): 0 | skipped (invisible: transparent or opacity 0): 0
exempted 1 disabled-state pair(s) (IsEnabled=False; WCAG 1.4.3 does not require contrast for disabled controls)
suppressed 1 pair(s) via xamlcontrast-ignore comments

  ok          3
  fail        3
  decorative  1

  below AA: 3, by cause:
    light-fails   2
    both-low      1

  both-low = the palette itself is too weak, switching theme won't save it;
  dark-fails / light-fails = the design intent didn't survive the other theme.

===== fail (3) =====
MainWindow.xaml:30  TextBlock                fg=White                      bg={Surface}  dark= 16.67:1 light=     1:1  [light-fails] need 4.5 12px
MainWindow.xaml:26  TextBlock                fg={DynamicResource DimText}  bg={Bg}       dark=  2.11:1 light=   1.6:1  [both-low] need 4.5 12px
MainWindow.xaml:6   Style[HoverBtn]/trigger  fg=#9A9A9A                    bg={Surface}  dark=  5.92:1 light=  2.81:1  [light-fails] need 4.5

exit 1: 3 pair(s) below threshold x 0.67 (0 warn)
```

（欄寬有收窄以配合頁面，其餘逐字照實際輸出。）

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

- **等級**（絕對對比）：`fail`（< 門檻×0.67，約 2/3）／`warn`／`ok`／`decorative`。
  預設 `fail` 與 `warn` 都讓 CI 失敗（沒達到 AA 就算）；`--fail-on fail` 讓 `warn` 通過
- **對稱**（跨主題）：`both-low`＝色票本身不夠，換主題救不了；
  `dark-fails`／`light-fails`＝設計意圖沒跨主題保住

對稱只回答「換個主題救不救得回來」，所以**只有 `fail` 與 `warn` 帶這個維度** ——
`ok` 與 `decorative` 在 JSON 裡的 `symmetry` 是 `null`。
合格的配對沒有問題要救，把 21:1 標成 `both-low`（＝色票太弱）是胡說。

對稱**永遠不會**被拿來藏結果 ——「兩邊一樣糟」不是刻意的證據，只是糟兩次。
低於 AA 的一律報出來，由人判斷。

## 難的不是 WCAG 公式

公式只有 20 行。價值在「這段文字實際疊在什麼顏色上」的**十七條解析規則**，
每一條都是在真實出貨的產品上踩出來的：Transparent 穿透、alpha 合成、Opacity 沿樹累乘、
ControlTemplate 子樹、Style setter 配對、觸發態、死 setter 過濾、具名／行內 Style
（含 BasedOn 鏈）、同條件觸發器合併、停用態豁免（WCAG 1.4.3）、半透明色票、
模板根背景、指向模板根的觸發 Setter、`MultiTrigger` 複合條件狀態、
`#FFRRGGBB` 是不透明而非半透明、文件根的隱含樣式背景、重疊容器中的兄弟背板。

前十三條來自四個已出貨的 WPF App；後四條來自 2026 年 8 月掃描八個公開 WPF 專案
（1909 個 XAML 檔）——其中兩條在原本那四個專案上**完全測不到代價**，因為四個都是同一
作者、共用同一套寫法。**盲區的嚴重程度，量不出來的樣本等於沒量。**

## 零設定色盤偵測

自動判斷色盤在哪，依序嘗試，實際命中哪一條會印在報告第一行：

1. **合併字典・深淺俱全** —— 專案裡所有定義了具 key 的 `Color`／`SolidColorBrush` 的 XAML
   併成一份色盤，且每個鍵深淺兩套值都有。排在最前面，因為真實專案的色票常拆散在多個字典裡，
   「挑單一檔」會漏。`Application.Resources` 算字典；視窗自己的區域 brush（`Window.Resources`、
   `UserControl.Resources`）跟 WPF 的資源查找一樣只在那個視窗裡看得到：在那裡蓋過色盤（引用 `Color` 的
   區域 brush 依主題各自解析），只有某個視窗定義的鍵在其他地方一律是 `unknown-palette-key`。字典檔不算進
   `summary.files` 的畫面數，但裡面的 Style（以及不在 Style 裡的 DataTemplate／ControlTemplate）照樣稽核。色盤依路徑裡任何位置的 dark／light 分成深淺兩組
   （`darktheme.xaml`、`Dark2.xaml`、`Themes/Dark/Colors/Palette.xaml` 都算）；檔名完全看不出主題（`Ocean.xaml`／
   `Nord.xaml`）、又有三個以上的鍵在不同字典裡值不同時，判成偵測退化，不靜默只評其中一套。
   字典**只屬於一個主題**的條件：檔名或最後一段資料夾裡有一個完整的詞是 dark 或 light（`Dark.xaml`、
   `DarkTheme.xaml`、`Colors.Dark.xaml`；`Highlight.xaml`、`Starlight/` 不算）而且定義了色票，或另一個主題的
   對應檔存在（`Themes/Dark/Styles.xaml` ↔ `Themes/Light/Styles.xaml`）。裡面的 Style 只評那個主題，兩個主題的
   字典都定義的同 key Style 在使用處依主題各自解析（元素名顯示成 `Button⊕Style[Chip]@dark/base`；只有一個主題
   定義的 key，另一個主題記 `unknown-palette-key`）
2. **主題配對** —— 一個 Dark 檔 × 一個 Light 檔，鍵集重疊 ≥ 50%
3. **C# 真相源** —— 三元組陣列，**提供兩套主題的來源優先於只有一套的**
4. **合併字典・單一主題** —— 同 (1) 的聯集，但每個鍵只有一套值
5. **單一主題檔** —— 一份字典，深淺兩欄同值
6. **都沒有** —— 退回只算寫死色碼，並**大聲講**

(1)(2) 都回報成 `pair` 模式，(4)(5) 都是 `single`。repo 裡有兩個以上 `<Application>`
根元素（通常是 `App.xaml`）時，合併字典的 (1)(4) 會讓各應用程式子樹再各自對自己的色盤解析一次；
不屬於任何應用程式的檔案只用共用區自己的字典（只有某個 App 定義的鍵在那裡記為 `unknown-palette-key` ——
它的值取決於執行期的宿主 App）。
掃描根目錄以下的路徑中，有資料夾（或檔名）**整段**就是 `test`、`sample`、`demo`、`example`、`spec`
（單複數皆可、可帶數字，如 `Tests2`），或最後一個詞是 `Tests`／`Test`／`Specs`（`MyApp.Tests`、`UnitTests`、
`e2e-tests`；第二層以下另含 `Fixtures`／`Mocks`）的 —— `ExampleCorp.Core`、`TestRunner.UI`、`DemoScene`、
`Contoso.Demographics` 不算 ——
不會被當成色盤來源（XAML 字典或 C# 三元組檔）或應用程式根元素；其中的 XAML 照樣受稽核。
其他地方都找不到色盤時，色盤那一行會點名因此被略過的檔，並說明怎麼在 config 指定它們。

猜錯可用 `xamlcontrast.config.json` 指定（[schema](docs/config-schema.md)）。
工具不認得的屬性名（例如拼錯的 `strictPallete`）是設定錯誤（exit 2）並點名，不會被靜默忽略。
若你的 App 建在主題函式庫上（MahApps、HandyControl、MaterialDesign⋯），通常色盤找得到、
但**視窗底色找不到** —— 用 `rootBackground` 一行宣告即可，工具在覆蓋率不足時會主動提示。

## 進 CI

```bash
xamlcontrast src/MyApp --json report.json    # 有任何一組沒達到 AA 就 exit 1
xamlcontrast src/MyApp --fail-on fail        # 放寬：只有低於 fail 線的才擋
xamlcontrast src/MyApp --sarif audit.sarif   # GitHub code scanning 格式
xamlcontrast src/MyApp --md report.md        # Markdown 報告（可貼 PR 留言）
xamlcontrast src/MyApp --html report.html    # 單檔 HTML 報告，附色塊
```

退出碼：

| 碼 | 意思 |
|---|---|
| `0` | 通過 |
| `1` | 有配對低於 gate —— 預設是任何 `fail` 或 `warn`（`--fail-on fail` 時只看 `fail`），或 `--baseline` 下有新增或惡化 |
| `2` | 用法、設定或輸入檔錯誤 —— 旗標錯、config 不合法、報告或 baseline 檔寫不出／讀不到 |
| `3` | **這份結果不可信** —— 解析出 0 組配對（空掃不是通過）、覆蓋率低於 `--min-coverage`、`--strict-palette` 下色盤退化 |

gate 的優先序：`--fail-on` > config 的 `failOn` > 預設 `warn`。三道「不可信」的檢查排在兩個 baseline 模式之前，
弄壞的色盤不會被當成「債還清了」。

**既有專案導入（baseline ratchet）**：第一天全紅的門會被關掉。先凍結既有債（每一組沒達到 AA 的），
之後只擋新增與惡化 —— 債只准減不准增：

```bash
xamlcontrast src/MyApp --write-baseline xamlcontrast-baseline.json   # 跑一次，commit 進 repo
xamlcontrast src/MyApp --baseline xamlcontrast-baseline.json         # CI 裡
```

兩個旗標都要給路徑，而且不能同時用。baseline 的鍵不含行號（行號會漂），但會記錄最差比值
（同一個鍵、色值被調暗的色盤漂移照樣抓得到）。檔案帶 `schemaVersion` 與 `keyVersion`；缺欄位的、0.x 寫的、
或鍵是較舊版本產生的（之後的版本可能改變鍵的寫法）baseline 會被拒絕（exit 2），並提示重跑 `--write-baseline`
—— 而不是把每一筆既有債都報成 NEW。

**刻意低對比的標記**（理由必填；壓掉的計入 `summary.suppressed`，不會靜默消失）：

```xml
<!-- xamlcontrast-ignore: 浮水印，刻意低對比 -->
<TextBlock Opacity="0.4" Text="草稿" ... />
```

完全沒寫理由的 `<!-- xamlcontrast-ignore -->` 視為無效並警告（配對照報）。標記要一字不差 ——
小寫、有冒號、理由寫在同一行；寫錯的變體（大小寫不同、冒號後沒有理由、漏冒號、理由換到第二行）
不會壓掉任何配對，並以同樣方式警告（`summary.invalidIgnores`）。標記寫在容器上時，涵蓋容器裡的
一切 —— 元素，以及容器 `Resources` 裡的 `Style` 定義。

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
      - uses: actions/checkout@v7
      - uses: HSU-YU-MING/cornhsu-xamlcontrast@v1   # 跟隨最新的 1.x
        with:
          root: src/MyApp
          # version: 1.0.0        # 選填：把工具釘在確切版本；留空裝最新的 1.x
```

> **`pull-requests: write` 是 PR 留言能不能貼的關鍵。** 沒有它那一步會 403。
> 那步刻意設了 `continue-on-error`，所以把關照常運作、只是留言不見了 ——
> 但失敗是安靜的，所以值得一次設對。
> （fork 來的 PR 拿到的 token 一律唯讀，那是預期行為，不是設定錯誤。）

破的配對會**直接標在 PR 的那一行**，同時把稽核報告貼成一則 PR 留言（同一則反覆更新，不洗版）。
留言裡除了落差表，也會列出「沒被評估到的東西」——豁免、壓掉、解析失敗、色盤偵測退化，
因為這個工具最大的風險不是漏報，是謊報健康。

行內註記跟著 gate 走：gate 擋的是 error，gate 放行的 warn（`fail-on: fail`）是 warning，
baseline 模式下的既有債是 notice。

JSON、Markdown、HTML 三份報告會上傳成這次 run 的 `xamlcontrast-report` artifact —— 打開裡面的
`xamlcontrast-report.html` 就看得到每一筆 fail 的色塊（見 [HTML 報告](#html-報告)）。

輸入：`root`（必填）、`working-directory`、`version`（留空 = 最新的 1.x）、`fail-on`
（`warn`／`fail`；留空 = config 的 `failOn`，沒有就是 `warn`）、`min-coverage`（留空 = config 的
`minCoverage`，沒有就是 50）、`baseline`、`strict-palette`、`comment`（關掉 PR 留言）、
`upload-report`、`sarif`、`share-stats`（預設 `true`；`false` 不送[匿名使用統計](#匿名使用統計)，並在 job summary 註明）、
`artifact-name`（預設 `xamlcontrast-report`）。開關類只收 `true` 或 `false`；
不認得的值會讓 job 失敗並點名是哪個 input，不會悄悄退回預設值。`root` 與 `baseline` 不能以 `-` 開頭（寫成 `./-name`）。

**同一個 workflow run 裡用到兩次以上**（matrix，或同一個 job 呼叫兩次）？每一次給不同的 `artifact-name` ——
它是上傳的 artifact 名稱，也是 PR 留言的標記，各名稱各自一則留言。開 `sarif: true` 時，不是預設值的名稱也會當成
code scanning 的 `category` 傳出去，兩份分析不會互相取代。同一個名稱用兩次，第二次上傳會撞名、留言（與 SARIF 分析）會互相蓋掉。

### 推送到 Parity 儀表板（實驗性）

`--push` 把稽核已經寫好的報告上傳到 [Parity](https://github.com/HSU-YU-MING/cornhsu-parity) 的團隊儀表板
（`Parity.Server`，自己架設）。它**不稽核** —— 先用 `--json <file>` 跑稽核，再推那個檔：

```bash
xamlcontrast src/MyApp --json report.json                              # 稽核：報告留在這台機器上
xamlcontrast --push report.json --server https://dashboard.example.com  # 環境裡要有 XAMLCONTRAST_TOKEN
```

- **送出什麼**：一個 `POST {server}/api/runs`，本文就是 `--json` 寫出的那個報告檔、原樣不動 ——
  相對於掃描根目錄的檔案路徑、行號、元素名稱、顏色值與色票鍵、計數。在意的話，第一次推送前先看一眼那個檔。
  另外以標頭帶上：token、commit（`--commit`，否則 `GITHUB_SHA`）、分支（`--branch`，否則 `GITHUB_HEAD_REF`，
  再否則 `GITHUB_REF_NAME`）、GitHub Actions 上的觸發者與 repo 網址，以及報告 `exitCode` 對應的 gate（0 = pass，1 或 3 = fail）。
- **環境變數**：`XAMLCONTRAST_SERVER`（或 `--server`）是儀表板的絕對 `http`／`https` 網址；`XAMLCONTRAST_TOKEN`
  是伺服器 `create-project` 指令印出的專案 token。刻意沒有 `--token` 旗標 —— 旗標值會留在 shell 歷史與 CI log 裡。
- **選擇性加入**：不跑 `--push`，報告就不會離開這台機器；稽核本身唯一的對外請求是[匿名使用統計](#匿名使用統計)
  （只有計數，隨時可以關）。推送只送到你指名的伺服器，
  而且不跟轉址，token 不會被轉送到別處。請用 `https`：走 `http` 時 token 是明文。
- **退出碼**：`0` 送出，`2` 其他一切 —— 沒有 token、檔案不是 XamlContrast `schemaVersion` 6 的報告（送出前就先驗）、
  連不上伺服器（逾時 60 秒）或伺服器回錯誤。不會是 `1` 或 `3`：那是稽核的判定。`--push` 與專案根目錄或任何稽核旗標並用是用法錯誤。
- **實驗性**：儀表板還沒公開上線，這些旗標、環境變數、push 模式的退出碼與伺服器協定不在 1.x 凍結面內，
  可能在 minor 改變；儀表板上線後併入凍結。

在 workflow 裡接在 action 之後。action 會把工具留在 `PATH` 上，稽核得出判定時一律把 JSON 報告寫到
`$RUNNER_TEMP/xamlcontrast-report.json`（用法或設定錯誤、exit 2 時不會寫）：

```yaml
      - uses: HSU-YU-MING/cornhsu-xamlcontrast@v1
        with:
          root: src/MyApp
      - name: Push to the dashboard (experimental)
        if: always()                # gate 紅了也推 —— 那正是最值得看的一次
        continue-on-error: true     # 儀表板掛掉、或拿不到 secret 的 fork PR，不讓 build 失敗
        shell: bash
        run: xamlcontrast --push "$RUNNER_TEMP/xamlcontrast-report.json"
        env:
          XAMLCONTRAST_SERVER: https://dashboard.example.com
          XAMLCONTRAST_TOKEN: ${{ secrets.XAMLCONTRAST_TOKEN }}
```

GitHub Actions 上這一步也會在 job summary 加一行儀表板連結。`pull_request` 事件的 `GITHUB_SHA` 是 merge commit、
不是 PR 的 head，要記別的就用 `--commit`。同一個 job 呼叫 action 兩次以上？每次呼叫之後各推一次 —— 下一次呼叫會換掉報告檔。

## JSON 輸出

`--json report.json` 產出 `schemaVersion` 6 —— 1.x 凍結，形狀發佈在
[`docs/report.schema.json`](docs/report.schema.json)。每個欄位一律輸出，沒有值是 `null`、`[]` 或 `{}`。
1.x 只會新增欄位，`schemaVersion` 維持 6 —— **不認得的欄位請略過**。有些值是 **開放集**，1.x 可能加新值 ——
unresolved 的原因（`unresolvedBy` 的鍵、`unresolvedSites[].reason`）、`paletteMode`、`summary.failOn`、`findings[].theme` —— **不認得的值也要容忍**；
schema 把它們寫成字串，目前的值列在 description。

- 頂層：`toolVersion`、`exitCode`（稽核的判定：0、1 或 3）、`summary`、`unresolvedSites`、`findings`。
- `summary` 帶實際生效的 gate（`failOn`、`minCoverage`、`strictPalette`）與**全部的退化計數** ——
  `paletteDegraded`、`coverage`、`unresolved`（含 `unresolvedBy`）、`skipped`、`deadForeground`、`disabledExempt`、`suppressed`、
  `parseErrors`／`parseErrorFiles`、`invalidIgnoreCount`／`invalidIgnores`、`unknownKeys`。工具沒看到的東西，都是機器可讀的。
- 每筆 finding 帶 `file`、`line`、`element`、`fg`、`bg`、`ratioDark`、`ratioLight`、`colors`、`threshold`、`category`、
  `symmetry`（fail／warn；其餘 `null`）、`isText`、`isLargeText`、`fontSize`（DIP，沒設是 `null`）、`theme`、
  `baseline`（`--baseline` 模式下 gate 擋的那些：`known`／`new`／`worsened`）。被壓掉的配對只計入 `summary`，不列出。
- `colors`（1.1 新增；`schemaVersion` 維持 6）是比值實際拿來算的那兩個顏色 —— 合成之後、螢幕上的不透明顏色：
  字色已經以自己的 alpha 與累乘的 `Opacity` 疊到底上，底已經把半透明的層合成完。`fg`／`bg` 說顏色是**怎麼寫的**，
  `colors` 說它們**實際是什麼**：

  ```json
  "fg": "#FFFFFF ×0.5", "bg": "{Surface}", "ratioDark": 5.09, "ratioLight": 1,
  "colors": { "dark": { "fg": "#8E8E8E", "bg": "#1E1E1E" }, "light": { "fg": "#FFFFFF", "bg": "#FFFFFF" } }
  ```

  拿 `colors.dark.fg` 對 `colors.dark.bg` 重算對比，結果就是 `ratioDark`。某個主題是 `null`，恰好就是那個主題的比值是 `null` 的時候。

**只存在於一個主題**的配對（只屬於一個主題的字典裡的 Style，或使用處的 Style 依主題解析到不同的鏈，
見上面的色盤偵測）**只有那個主題的比值**：`theme` 標出是哪一個（`"dark"`／`"light"`；兩個主題都有的是 `null`），
另一個主題的比值與 `colors` 是 `null`，console、Markdown、HTML、SARIF 與 Action 註記印 `n/a (dark theme only)` 而不是一個數字。
fail／warn 另標 `symmetry: single-theme`。
`skipped` 只計看不見的字（透明、alpha ≈ 0、`Opacity="0"`）；半透明的字色會合成後評分。

`unresolved` 在 `summary.unresolvedBy` 裡有原因細目 —— 光看總數看不出「補不補得回來」:

| 原因 | 意思 |
|---|---|
| `no-ancestor-background` | 祖先鏈上沒有任何一層宣告背景。常見於根容器靠 App 層隱含樣式給底,或資源字典裡的向量圖 |
| `bound-or-gradient` | 顏色來自 `{Binding}` / `{TemplateBinding}` / 漸層或圖片 brush —— 執行期才知道。其他工具讀不出顏色的值也歸這裡(WPF 的色彩字面值 —— `#RGB`、`#ARGB`、`#RRGGBB`、`#AARRGGBB`、具名色 —— 都讀得懂) |
| `unknown-palette-key` | 資源鍵不在偵測到的色盤裡 —— 打字錯誤,或色盤偵測漏了某個檔 |
| `translucent-uncomposited` | 透明或半透明的背景、底下沒有可疊的顏色 —— 半透明的 `Background`、上面的祖先都沒有底；Style 定義處自己的 `Background="Transparent"`，而且沒有任何使用處會評到它（隱含樣式，或沒有元素以 `Style=` 引用的具名 Style；有元素用到的改在使用處評）；或以 `Opacity` 變淡、底下什麼都沒有的一層。第一種用下面的 `rootBackground` 補得回來 |
| `same-brush-pair` | Style 把字色和底色設成**同一個** brush —— Material 系的模板不透明度慣用手法(模板把背景以 10~12% Opacity 畫成暈染);字面 1:1、執行期不是,模板動畫靜態看不到 |
| `over-sibling-content` | 文字墊在同格子的兄弟 `Image`/媒體元素上 —— 真正的底是一張圖,靜態不可知;硬配祖先背景等於編造比值 |
| `in-style-template` | Style 的 `Template` setter（`<Setter Property="Template"><Setter.Value><ControlTemplate>`）裡自己帶 `Foreground` 的文字元素 —— 還沒有評分（見已知限制），計入這裡讓覆蓋率反映這個缺口 |

`summary.coverage` 是解析成功的比例。**低於 `--min-coverage`(預設 50%)會 exit 3** ——
只掃到一小部分的「通過」不算通過,這是「0 組配對不算通過」那條不可設定規則的推廣。
要關掉用 `--min-coverage 0`(Action 用 `min-coverage: 0`,或在 `xamlcontrast.config.json`
設 `"minCoverage": 0`)。

每一筆無法解析的配對都有位置可查:`--show-unresolved` 逐筆列出
`file:line · 原因 · 卡住的值`(卡在背景時放背景的值),JSON 頂層的 `unresolvedSites` 陣列帶同一份清單(`file`、`line`、`reason`、`value`)。
unknown key 另外點名加計數(`summary.unknownKeys`,console 印前幾名)——
每一個都是死引用、打字錯誤、或色盤偵測漏掉的檔,等於免費送一個 lint。

**第一次跑,多半會停在覆蓋率下限。** 解析得出多少,取決於 App 怎麼給顏色。四個驗證專案是 73~96%
(1.0 實測:Kindling 95.8%、QuillNest 89.4%、ZeroCel 73.4%;第四個 Cornea 是截圖覆蓋視窗,
字疊在工具看不到的畫面上 —— 0 組配對、exit 3)。公開專案實測更低:應用程式 26~57%,
控制項函式庫(MahApps、MaterialDesign、HandyControl)5~19% —— 所以**不少專案開箱就是 exit 3**,
低於 50% 的 `--min-coverage` 下限。那是下限在說「這份結果不代表整個專案」,不是對比度的判決。
看 `unresolved by reason`:`no-ancestor-background` 佔大宗,通常是主題函式庫給了視窗底色 —— 用下面的
`rootBackground` 宣告;`bound-or-gradient` 佔大宗是靜態分析的硬邊界(走 `{TemplateBinding}` / `{Binding}`
的顏色執行期才存在,這也是控制項函式庫解析率低的原因)—— 把下限調到專案做得到的值
(`--min-coverage 20`,或 config 的 `"minCoverage"`),並把解析出的部分當抽樣看待。

**建立在主題函式庫上的 App**(MahApps、HandyControl、MaterialDesign…)是可修的特例:
視窗底色的**鍵**在你 repo 的主題檔裡,但把它連到視窗的隱含樣式住在 NuGet 套件裡,
靜態分析看不到。在 `xamlcontrast.config.json` 宣告一次:

```json
{ "rootBackground": "MahApps.Brushes.Window.Background" }
```

實測(NETworkManager,MahApps 系 App、154 個 XAML):一行設定,覆蓋率 **8.8% → 87.2%**,
浮出 284 筆先前完全看不到的淺色主題真實問題。鍵不在偵測到的色盤裡是設定錯誤(exit 2),
不會靜默沒作用。

`--md report.md` 則產出給人讀的 Markdown（Action 用它貼 PR 留言，也可以自己拿去用）。

### HTML 報告

`--html report.html` 寫出一個單檔網頁 —— 樣式內嵌、沒有 script、不載入任何外部檔案 —— 從 CI 的 artifact 直接打開就能看
（Action 會把它上傳成 `xamlcontrast-report.html`）。內容：

- 結論與理由（exit 0、1 或 3），所有退化警告排在最前面 —— 色盤偵測退化、0 組配對、非預設門檻、解析失敗、無效的 ignore 註解；
- 摘要：計數、覆蓋率、gate、色盤模式與偵測描述，以及所有「沒評到的東西」的計數器（unresolved 依原因、skipped、excluded、
  exempted、suppressed、parse errors）—— 是 0 也印；
- 每一筆 `fail` 與 `warn` 一張卡：位置、元素、原始寫法的字色與底色，以及每個主題各自的字色色塊、底色色塊、用那兩個顏色寫的樣字、
  比值、色碼、有沒有達到門檻（等級與達標與否一律有文字，不只靠顏色表示）；
- 每一筆 unresolved 的原因與卡住的值；
- `ok` 與 `decorative` 全部列出，收在預設收合的區塊裡。

色塊就是 JSON 的 `colors`。來自被稽核 XAML 的文字一律跳脫，網頁的 Content-Security-Policy 禁止 script 與外部資源。
深淺色跟著系統設定。版面跟 console、Markdown 一樣屬於呈現、不在凍結面內 —— 程式請讀 JSON。

## 匿名使用統計

1.1 起，每次稽核得出判定（exit 0、1 或 3）之後，XamlContrast 會送**一筆**很小的請求到
`https://stats.cornhsu.com/v1/xamlcontrast`。**預設打開，CI 也送**，一個設定就能關。它是作者碩士論文
「真實 XAML App 的對比度問題長什麼樣」的補充證據；[stats.cornhsu.com](https://stats.cornhsu.com/) 說明收什麼、做什麼用、保存多久。

**送什麼** —— 只有計數與區間：

| 欄位 | 內容 |
|---|---|
| `schema`、`tool`、`toolVersion` | `1`、`"xamlcontrast"`、工具版本（不含 build metadata） |
| `os` | `windows`、`linux`、`macos` 或 `other` |
| `ci` | `CI` 或 `GITHUB_ACTIONS` 是不是 `true` |
| `projectId` | `null` —— 除非**你自己**加一個（見下） |
| `data.exitCode` | 稽核的判定：0、1 或 3 |
| `data.failOn` | 實際生效的 gate：`warn` 或 `fail` |
| `data.paletteMode`、`data.paletteDegraded` | 走了哪一條色盤偵測路徑、有沒有退化 |
| `data.files`、`data.pairs`、`data.coverage` | 檔案數、配對數、覆蓋率 |
| `data.counts` | `fail`、`warn`、`ok`、`decorative` 各幾組 |
| `data.unresolvedBy` | 無法解析的配對依原因的組數（原因見上面那張表） |
| `data.ratios` | 非裝飾配對依最差比值分成六個區間的組數：< 1.5、1.5–2、2–3、3–4.5、4.5–7、≥ 7 |

**一律不送**：檔名或路徑、元素／Style／資源的名稱、色碼、報告本身、你的設定檔、repo 名稱、使用者名稱、機器名稱，
或任何其他來自你專案的東西。請求不帶 User-Agent、cookie 或任何自訂標頭。收集站不存 IP
（代管它的 Cloudflare 跟任何網路服務商一樣看得到連線），只記收到的日期，多一個不認得的欄位就整筆拒收。

**沒有 ID，除非你自己加。** 沒有 ID 的時候，沒有任何東西能把兩次執行連在一起、或連到你身上 —— 這也表示
沒辦法找出或個別刪除「你的」資料。想讓同一個專案的多次執行歸在一起，就自己編一個 ID（16–64 個 `A-Z a-z 0-9 _ -`；
不要從 repo 名稱推出來）寫進設定檔 —— 格式不對是設定錯誤，exit 2：

```json
{ "stats": { "projectId": "pick-your-own-random-id-2026" } }
```

**怎麼關** —— 任何一個就夠：

- `DO_NOT_TRACK=1` —— `0`／`false` 以外的任何值都算；永遠優先
- `XAMLCONTRAST_SHARE_STATS=false`，或 `CORNHSU_SHARE_STATS=false`（姊妹工具 Parity 也一起關）。
  `1`、`true`、`yes`、`on` 以外的任何值都是關 —— 打錯字也往「不送」那邊錯
- `xamlcontrast.config.json` 裡寫 `"stats": { "share": false }`
- GitHub Action 用 `share-stats: false`

**送之前先看**：`xamlcontrast <root> --stats-preview` 照常稽核、什麼都不送，在 stderr 印出這次會送的完整 JSON，
以及會不會送、為什麼（`would not be sent: DO_NOT_TRACK is set`）。

**你會看到什麼**：本機上，第一次要送的時候在 stderr 印一段說明（使用者設定目錄裡的標記檔
`cornhsu/xamlcontrast-stats-notice` —— Windows 是 `%APPDATA%`、Linux 是 `~/.config` —— 讓它不再重複）；
CI 上每次在 stderr 印一行，GitHub Actions 另外在 job summary 寫一行。stdout 一個字都不碰；不送的時候什麼都不印。

**不影響你的結果。** 請求最多等 3 秒（連線 1.5 秒），不重試、不跟轉址，任何失敗都安靜略過 —— 退出碼與所有輸出
都跟沒有它的時候一模一樣。開發版 —— 版本以 `0.0.0-` 開頭的，例如從原始碼建出來的 `0.0.0-dev` —— 永遠不送。

## 實戰成績

不是「demo 跑得動」——在**三個正式發行的 WPF 產品**上找到並促成修正 **250+ 處**真實對比問題
（修正前後都由工具自己稽核；數字是修正當時、2026 年 7 月 v0.1.0 時期的工具數出來的）：

| 專案 | 修正前 | 修正後（2026 年 7 月） |
|---|---|---|
| CelFlow | 破 39／偏低 57 | **0**（停用態豁免 21） |
| Kindling | 破 41／偏低 14 | **真實問題 0**（餘 3 筆皆為已記錄假警報類） |
| QuillNest | 破 13／偏低 31 | **真實問題 0**（餘 破 7／偏低 1，同上） |

「修正後」一欄是當時的紀錄，不是即時數字。以 1.0 的工具重新稽核（2026 年 10 月）是
ZeroCel（原 CelFlow）破 1／偏低 6、Kindling 破 1／偏低 1、QuillNest 破 2／偏低 1 ——
新出現的 fail 是先前的工具看不到的真問題，消失的那一筆原本是拿錯的底色來比（詳見 CHANGELOG）。

信任是怎麼掙來的（完整故事見[開發歷程回顧](docs/開發歷程回顧.md)）：

- **兩套獨立實作互證**：v0.1.0 時 PowerShell 原型（規格）與 .NET 版在四個驗證專案上
  數字完全一致 —— 精確到每筆 fail 的 `file:line` 與退出碼。之後原型凍結，
  .NET 版改以自己凍結的四專案快照做回歸（`scripts/verify-baselines.ps1`，
  受測的四個 App 未公開，只有維護者能跑）。
- **十七條解析規則沒有一條是在白板上設計的** —— 每條都來自真實的假警報或漏報實查
  (前十三條來自四個上線產品,後四條來自八個公開 WPF 專案、1909 個 XAML 檔的批次掃描),
  第 12 條（模板根背景）甚至是在驗收工具自己促成的修正時挖出來的。
- **開發期間工具自己出過八次「看起來健康但錯」的報告，每一次都變成回歸測試。**
  用血寫成的第一原則：**稽核工具最大的風險不是漏報，是謊報健康。**
  所有放過的東西 —— 豁免、排除、壓掉、解析失敗 —— 都計數、都喊出來。
- **殘餘的 fail 有名有姓、不藏**：2026 年 7 月那張表中當時殘餘的每一筆都對應已記錄的假警報類（見已知限制），
  導入 CI 時由 baseline ratchet 承接。

## 已知限制

完整盲區表與逐案證據見[規畫書](XamlContrast專案規畫書.md)。

Style 的 `Template` setter 裡自己帶 `Foreground` 的文字元素**還沒有評分**（樹走訪不進 Style）——
計入 unresolved（`in-style-template`，點名 `file:line` 與那個字色），覆蓋率照實反映；模板經 `{TemplateBinding Foreground}`
或 `ContentPresenter` 顯示的宿主字色照舊由 Style 鏈評分。
TargetName 指向模板**內部**元素（指向根的已解析）、跨元素同條件觸發器、
隱含樣式（根元素背景以外;例:`DataGrid` 列裡的按鈕是配表格的底評,不是隱含 `DataGridRow` 樣式畫的選取列底 ——
實測一個報 ok 的 ghost 按鈕在選取列上是 4.06:1）;sibling 背景已解析 `Grid` 格子裡「佔滿格子的兄弟」(純色直接用;
半透明的 —— 不論來自 brush 的 alpha 或元素自己的 `Opacity` —— 合成;Binding／漸層計入無法解析),
文字疊兄弟 Image 誠實歸 `over-sibling-content`;版面無法得知的自訂 `*Panel`、衍生命名的 `*Canvas`／`*Grid` 裡,
可能是背板的兄弟之後的字也歸 `over-sibling-content`(`InkCanvas` 與 `Canvas` 一樣疊層;
在 config 的 `layout` 宣告那個容器是 `overlap` 或 `sideBySide` 就不必猜)——
但部分覆蓋的兄弟(含 `Canvas` 裡的一切,Canvas 不撐滿子元素)、
藏在背板元素**裡面**的圖片仍看不到;Binding 來的顏色標「無法解析」而不猜 ——
猜了就是拿誠實的不確定去換錯誤的信心。
另:`VisualStateManager` 的顏色動畫(狀態 storyboard 裡的 `ColorAnimation`)不稽核
**且尚未計數** —— Blend 時代的樣式方言(實測:kaxaml/snoop 這類老專案有數十處,
現代專案為零)。計數器已列入規劃;在那之前,這一行就是揭露。

## 在你的專案上跑過了嗎？我想知道結果

十七條規則每一條都是真實的程式碼逼出來的。這個方法有個內建的弱點，而且它已經咬過這個
專案一次：最初四個驗證專案同一個作者、同一套寫法，於是有兩整類的 bug 在它們身上
**完全測不到代價**，直到掃了八個不相干的公開專案才浮出來。

**你的專案是我手上沒有的樣本。** 最有價值的回報是假警報——它說某處字太淺，但其實沒有。
沒人相信的工具會被關掉，所以假警報在這裡比漏報更嚴重。

[開一個 issue](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast/issues/new/choose)——
表單會問清楚重現所需的東西，中英文都可以。
細節（含 PR 的要求）見 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 另見

[**Parity**](https://github.com/HSU-YU-MING/cornhsu-parity) —— 同作者的姊妹專案，
同一套哲學（數值檢查、CI 把關）。Parity 回答「**實作跟設計稿一不一樣**」
（Figma vs 渲染後的真實數值）；XamlContrast 回答「**做出來的東西看不看得見**」。
一個守還原度、一個守可讀性，在同一張 PR 檢查清單上會合。

## 授權

MIT © [許彧銘 Hsu Yu-Ming](https://cornhsu.com/)

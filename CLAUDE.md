# XamlContrast 開發指南

靜態 XAML 對比度稽核工具（.NET 10、CLI + GitHub Action + NuGet 套件
`Cornhsu.XamlContrast`）：不啟動 App，直接讀 XAML 原始碼，把「這段文字實際疊在什麼顏色上」
算出來，低於 WCAG AA 就讓 CI 紅燈。使用者文件在 [README.md](README.md)／
[README.zh-Hant.md](README.zh-Hant.md)，設計全貌在 `XamlContrast專案規畫書.md`，
對外貢獻規則在 [CONTRIBUTING.md](CONTRIBUTING.md)——**這份是開發慣例**。

發版流程（推 tag、Trusted Publishing、CHANGELOG、作品集同步）是四個 NuGet 套件共用的，
寫在全域 skill `nuget-packages`（`~/.claude/skills/nuget-packages/SKILL.md`）。
下面只寫**這個 repo 特有**的部分。

## 指令

```bash
dotnet build XamlContrast.slnx -c Release
dotnet test  XamlContrast.slnx -c Release          # 2026-08-26 實跑：101 綠
dotnet format XamlContrast.slnx --verify-no-changes # CI 第一關，不過直接紅

# 示範專案（刻意做壞的，exit 1 才是對的）
dotnet run --project src/XamlContrast.Cli -c Release -- samples/demo
```

守門腳本兩支（下一節是重點）：

```powershell
powershell -File .\scripts\verify-baselines.ps1      # 四個真實 App 的快照回歸（只有本機能跑）
powershell -File .\scripts\verify-readme-sample.ps1  # README 示範輸出比對（CI 也跑）
```

兩支都吃 `-Update`：確認新行為才是對的之後，一鍵重貼／重凍，然後**人工看 git diff 再 commit**。

## 守門機制：這個 repo 是四個套件裡做得最完整的，其他 repo 要抄的樣板

工具本身的立場是「稽核工具最糟的失敗不是漏報，是給人虛假的信心」。這條規矩對工具自己
也成立——**文件與快照一樣會謊報健康**，所以守門也得自動化。目前有三層，形狀一致：

| 層 | 檔案 | 誰跑 | 擋什麼 |
|---|---|---|---|
| demo 自我把關 | `.github/workflows/ci.yml` + `samples/demo` | CI（`ci.yml`／`release.yml` 各一份） | 守門員本身失效：退出碼不是 1，或 `jq` 驗的計數（fail 3／豁免 1／壓掉 1）變動 |
| README 示範輸出 | `scripts/verify-readme-sample.ps1` | CI（Linux 上以 pwsh 跑）＋本機 | 文件裡貼的輸出跟實跑對不上 |
| 四專案快照回歸 | `scripts/verify-baselines.ps1` + `baselines/*.json` | **只有維護者本機**，發版前 | 稽核行為在真實專案上悄悄變了 |

### 為什麼要有這一套

`verify-readme-sample.ps1` 是被真的踩出來的：0.5 換掉色盤那行、0.6 加了覆蓋率那行之後，
README 的示範區塊**整整漂了兩個版本沒人發現**——印出來的字串已經不是 README 上寫的，
覆蓋率那行根本不存在。改輸出格式的時候沒有人會想到 README，所以人工比對一定會漏。
解法不是「記得改」，是讓 CI 自己跑一次再比對。

`verify-baselines.ps1` 是另一種：規則 13 之後原型（PowerShell）退役，C# 成為權威實作，
期望值改成**C# 自己產的快照**——比對的是「這次跑」與「上次凍結」，不是「跟正確答案比」。
比對項目是 summary 的全部計數器 + 每筆 fail 的 `file:line` + 退出碼。

### 抄的時候要抄什麼

1. **腳本放 `scripts/`、由 CI 呼叫**，不要塞進 workflow 的 inline script——本機跑得到才有人用。
2. **一定要有 `-Update`。** 沒有逃生口的守門員會讓人想把它關掉；有了 `-Update`，
   「行為刻意變了」的成本從「手動重貼十八行」降到「跑一次、看 diff」。
   這是這套設計能活下來的關鍵，不是附加功能。
3. **`-Update` 不自動 commit。** 兩支腳本結尾都印「檢視 git diff 後 commit」——
   自動 commit 會讓「刻意變更」與「回歸」再次分不出來，等於把守門員繞過去。
4. **比對前正規化，但只放寬排版。** `verify-readme-sample.ps1` 拿掉空行、連續空白收成一個，
   所以為了版面收窄欄寬沒關係；**多一行、少一行、字不一樣、數字不一樣一律紅燈**。
5. **失敗訊息要指出下一步**，不是只說「不一致」。

## 版本與發版（本 repo 特有）

- **`src/XamlContrast.Cli/XamlContrast.Cli.csproj` 的 `<Version>` 固定 `0.0.0-dev`，不要動。**
  這格曾經寫死 `0.5.1`，而 tag 已經走到 v0.6.0——CI 發到 NuGet 的沒事（`release.yml` 用
  `-p:Version=` 從 tag 覆寫），但**從原始碼 build 出來的執行檔自稱 0.5.1，連它蓋在 JSON
  報告上的 `toolVersion` 也是錯的**。整格刪掉更糟：退回 MSBuild 預設的 `1.0.0`，
  等於對外宣告介面已凍結。**tag 是唯一真相源，發版不要手動改版本欄位。**
- **這是四個套件裡唯一還在 0.x 的**（目前 v0.6.1）。介面尚未凍結，所以：
  - README 要維持「0.x 期間請 pin 確切版本」的說明（`README.md` 第 23 行的引言、
    第 191 行的 Action 範例都寫著 `@v0.6.1`）。**發新版時這兩處要跟著換。**
  - GitHub Action 的用法**到 1.0 之後才改成 `@v1`**（那時才需要開始維護移動式 v1 tag，
    Parity 已經在做，形狀照抄它）。
  - 凍結的定義是：CLI 旗標、JSON schema（含 `summary`）、Action inputs。
  - **JSON 形狀變了就遞增 `Report.cs` 的 `schemaVersion`**（目前 5），不要默默改欄位。
- `release.yml` 的 `NuGet/login` **已釘 commit SHA**
  （`8d196754b4036150537f80ac539e15c2f1028841` = v1.2.0），2026-08-25 的安全硬化，
  **不要改回 `@v1`**。上游出新版要更新時，四個 repo 一起換新 SHA。
  ⚠ dependabot 有開 `github-actions` 更新，它送的 PR 會直接換 SHA——合併前確認那顆
  SHA 真的對應到上游的某個 release tag。
- `ci.yml` 已明確宣告 `permissions: contents: read`（2026-08-14），**這是六個 repo 的基準寫法**，
  不要因為加了新 step 就整個拿掉；真的需要寫入權限就在該 job 上加最小的那一項
  （`release.yml` 就是這樣：job 層 `contents: write` + `id-token: write`）。
- 相依掃描 2026-08-26 實掃：**0 漏洞**（`dotnet list ... package --vulnerable --include-transitive`）。
  相依只有 `Microsoft.SourceLink.GitHub` 與測試端的 xunit／coverlet。

## 刻意的設計取捨

這些不是「還沒做」，是想清楚之後決定不做的。改之前先確認你不是在推翻結論。

- **不猜。** `{Binding}` / `{TemplateBinding}` / 漸層的顏色只有執行期才知道，一律計入
  `unresolved` 並點名位置，**永不推測**。拿誠實的不確定去換一個看起來很篤定的數字，
  是這個工具唯一不能做的事。
- **靜默退化等於謊報健康。** 所以 `Program.cs` 的退出碼有四道保險：
  0 組配對、色盤退化（`--strict-palette`）、覆蓋率低於下限（預設 50%）、fail > 0。
  豁免、壓抑、跳過、解析失敗全部有計數器且一律印出來。
  **「更乾淨的報告」不是改進**——把沒看過的東西藏起來的報告是更糟的報告。
- **對稱不是設計意圖的證據。** 舊版把「深淺兩邊都低而且差不多」判成「刻意低對比」藏起來，
  在 CelFlow 與 Kindling 上實證是錯的：兩邊一樣糟就只是兩邊都糟。
  分級（絕對對比）與對稱（跨主題）是兩個獨立問題，見 `Grader.cs`。
  對稱只在 fail/warn 上計算——合格的配對沒有問題要救。
- **規則來自實查。** 十七條解析規則沒有一條是在白板上想出來的，每一條都有一個真實專案
  證明它缺了。新增規則要附上證據（CONTRIBUTING 對外也是這樣寫）。
- **原型已凍結。** `prototype/XamlContrastTree.ps1` 與 `prototype/baseline-*.txt` 是
  v0.1.0 的歷史規格，**不是正確答案**，不要拿它當驗收來源（`baselines/*.json` 才是）。
- **不做 npm 包裝**（2026-07-31 決定）：使用者是 XAML/.NET 開發者，人人都有 dotnet，
  `dotnet tool install` 不構成採用門檻。受眾論證不成立就不付那條發佈管線的維護成本。
- **`XamlContrast.Core` 不獨立發套件**，除非 Parity 的 WPF adapter 真的動工或出現第二個
  消費者。沒有消費者的公開 API 只是提前凍結自己的重構自由。

## 地雷（改了會安靜壞掉的地方）

### 退出碼契約與檢查順序

`0`／`1`／`2` 是對使用者的契約（2 = 用法或設定錯誤）。**輸出寫檔失敗必須落在契約內**——
之前是裸 `File.WriteAllText`，目錄不存在就噴 .NET 堆疊、exit 127。

⚠ **`--strict-palette` 與覆蓋率下限必須留在模式分支（`--baseline` / `--write-baseline`）
之前。** 這兩個檢查原本擺在 `DecideExit()` 尾端，而 baseline 兩個分支都會直接 return，
於是永遠走不到——偏偏 `--baseline` 正是 README 推薦給既有專案的導入路徑。
實測過的失效鏈：主題檔被搬走 → 色盤偵測退化 → 配對全變 unresolved → 從 findings 消失 →
ratchet 判成「已還債」→ 綠燈，還印出「paid off N」恭喜你把債還清了。
**弄壞主題檔看起來像修好了所有問題。**

### 數值與字串解析

- **WPF 的 `FontSize` 是 DIP（1/96 吋）不是 point（1/72 吋），換算要 ×0.75。**
  18pt = 24px、14pt = 18.67px。少這一步會把 18px 的字當大字級放過。
  `Auditor.cs` 有三處在算（樹走訪、Style 鏈逐狀態、`WalkStyles`），改要一起改。
- **所有數值屬性一律 `InvariantCulture`**（`Auditor.ParseDouble`）。否則德語系 locale 的
  CI 上 `Opacity="0.5"` 會靜默解析失敗。
- **`BoldWeight()` 是錨定的整字匹配。** 舊版用子字串（`"Bold"` 命中 `"SemiBold"`），
  把 600 也放寬到 3:1，會漏掉 3.0~4.5 之間真正不合格的文字。SemiBold/DemiBold 是 600，不算粗體。
- **色票值只收長度 7（`#RRGGBB`）或 9（`#AARRGGBB`）**（`PaletteDetector.IsValidHex`）。
  正則寫的是 `{6,8}`，七位數的打字錯誤會被吃下來，而 `Wcag.Luminance` 只讀前六位——
  等於默默猜前半段再回報一個很篤定的對比值。長度不對就不收，讓它走 UnknownKey 的計數喊出來。

### 色盤偵測

- **檔名的 dark／light 提示只看「專案內相對路徑」。** 看完整路徑的話，專案放在
  `D:\dark-projects\` 底下會讓所有候選都染上 dark 提示、配對整個失效。
- **一律 `Path.GetFullPath` 正規化分隔線。** `GetDirectoryName` 回傳反斜線，
  但 `EnumerateFiles` 沿用呼叫端傳入的 root 寫法（可能是正斜線）；混用會讓 `StartsWith`
  永遠不成立 → 每個檔都被當共用區 → **應用程式作用域靜默退化成全域色盤**。
- **合併式色盤是三段式：主題檔先進（各佔自己那側），中性檔只補洞。**
  第一版 brush 表照檔案順序 `TryAdd`（Color 表做對了、brush 表忘了），ScreenToGif 的
  `Other/` 字典序排在前面，中性檔搶走主題檔的值，深色欄拿到白底、報出 1.23:1 的假 fail。
  另一版把字面值 brush 一律當「深淺同值」收，`Dark.xaml` 排在 `Light.xaml` 前 →
  **456 筆 findings 全部 dark==light，淺色欄整欄是編造的**。
  ⚠ 這兩個回歸四個驗證專案都抓不到（它們的配對走 Color 引用那條路）。動這塊要加測試。
- **元素分類用「後綴比對」不是全名相等**（`Auditor.IsNonTextElement`）：MahApps 的
  `MetroProgressBar` 對不上 `ProgressBar`，進度條填色會被當文字用 4.5 要求。
  兩邊都中時取最長的後綴（名字愈長愈具體）。

### baseline 的鍵

`Baseline.KeyOf` 是 `file|element|fg|bg`，**刻意不含行號**——行號隨編輯漂移，含進去每次
重排版都會「假性新增」。相對地必須存 `Count`（同鍵處數變多 = 惡化）與 `Worst`
（鍵不變但色盤值被調暗 = 惡化）。少任何一欄都會有一整類惡化靜默放行。
`paid off` 數的是**處數**不是鍵數，要跟 `knownDebt` 同單位。

### 守門腳本自身

- **`verify-readme-sample.ps1` 靠標題錨點定位**：`## What it looks like`（英）與
  `## 實際跑起來長這樣`（中）。**改 README 標題會讓守門員失效**——腳本會紅並明講
  「錨點被改掉了」，看到這個訊息是去改腳本，不是去改回標題。
- 這支**會在 Linux 的 CI 上以 pwsh 跑**，所以路徑用正斜線；讀 README 一定要
  `-Encoding UTF8`（Windows PowerShell 預設用系統 ANSI 讀，中文版 README 會整份變亂碼）；
  寫回去用 `UTF8Encoding($false)` 明確無 BOM（`Set-Content -Encoding utf8` 在
  Windows PowerShell 會塞 BOM、pwsh 不會，同一支腳本兩邊跑會讓 README 無謂地變動）。
- `dotnet format` 是 CI 第一關。**送 PR 前跑一次 `dotnet format XamlContrast.slnx`**，
  不然會為了縮排來回一趟。

## 技術債（留帳）

- **`baselines/celflow.json` 目前是紅的**（2026-08-26 實跑：`pairs` 期望 363 實得 353、
  `ok` 期望 346 實得 336，`fail` 仍是 0）。**不是 XamlContrast 的回歸**——`src/` 最後一次
  變動是 2026-08-12，而 CelFlow.WPF 有四個 XAML 檔在 2026-08-18 被改過。
  這暴露了 `verify-baselines.ps1` 的結構性弱點：**受測對象是四個活在 repo 外、會自己演化的
  專案**，紅燈分不出「工具行為變了」與「被稽核的 App 改了」。目前只能靠人工看 diff 判斷。
  修法有兩條（都還沒做）：把四個專案的 XAML 快照複製進 repo 當固定素材，
  或在快照裡記下受測專案的 commit／檔案雜湊，讓腳本自己說「對象變了」。
  ⚠ 在判定之前**不要習慣性跑 `-Update` 蓋掉**——那正是這套機制要防的事。
- **`verify-baselines.ps1` 進不了 CI**（路徑寫死 `D:\應用程式\...`，且那四個是未發佈的
  App）。所以它是「發版前本機關卡」而不是「每次 push 的關卡」，外部貢獻者跑不了
  （CONTRIBUTING 已明講）。RELEASING.md 把它列為發版前提，實務上要記得它可能因上面那個
  原因紅燈。
- **已知盲區（明文不做，不是 bug）**：VisualStateManager 的顏色動畫（Blend 時代方言，
  第四輪外部實查測得數十處／專案等級，目前連計數器都沒有，只有文件揭露）、隱含樣式的
  完整解析（只做「根元素背景」極窄的一格）、`TargetName` 指向模板內部元素的 Setter、
  跨元素相關聯的觸發器、文字疊在兄弟圖片上。收到相關 issue 時先確認是不是這幾條。
- **C# 色盤的「第一個是深、第二個是淺」是假設**（`PaletteDetector.CsTuple`，源自 Kindling
  的寫法）。config 的 `csharpPattern` 可以繞過，但自動偵測那條路仍寫死這個順序。
- **`.editorconfig` 刻意不設 `end_of_line` / `charset`**：既有檔案是 CRLF，強制成 lf 會逼出
  一次全 repo 的行尾重寫。要收斂請先加 `.gitattributes`（參考 PolyMigrate）、
  一次性正規化，最後才打開這兩項——不要夾帶在別的變更裡。
- `docs/論文相關/` 在 `.gitignore` 裡，是私人筆記，不是這個工具的產出。

## 開工慣例

- **改了稽核邏輯** → `dotnet test` + `verify-baselines.ps1`。四個專案的計數變動要能解釋，
  解釋不了就是回歸。
- **改了 console 輸出**（包含只是換個字）→ `verify-readme-sample.ps1`，不一致就 `-Update`
  重貼並把 README 的 diff 一起 commit。CI 會擋，但本機先看一眼省一趟。
- **改了 JSON 輸出的形狀** → 遞增 `schemaVersion`，並確認 `action.yml` 裡那幾段 `jq`
  （行內註記、SARIF 路徑改寫）還讀得到欄位。
- **新增任何「這組可以放過」的邏輯**（豁免／過濾／跳過）→ 一定要有計數器 + 回歸測試。
  開發期間這個工具產出過八份「看起來很健康但是錯的」報告，每一份現在都是一個測試。
  問自己：這條新邏輯有沒有可能讓一個真實問題從報告裡消失？
- **收尾**：動了功能面 → 兩份 README 都要同步（中英版曾經一份寫十七條規則、
  另一份只列十三條）；重大決策記進這份 CLAUDE.md、workaround 留技術債帳。

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
dotnet test  XamlContrast.slnx -c Release          # 2026-10-26 實跑（Windows）：946 綠、2 略過（沒有 jq 的機器略過 Action 註記的實跑；非 Linux 略過統計說明的標記檔）
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
⚠ `verify-baselines.ps1` 紅燈時**不要為了變綠就跑 `-Update`**：先逐項說明每個差異是哪個 commit 造成的，
給維護者看過、同意之後才重凍（1.0 的那份說明在 `D:/擴充功能/nuget/稽核-2026-10-22/XamlContrast-快照變動說明.md`）。

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
- **1.0.0（2026-10-23）起介面凍結**，直接從 0.6.1 跳到 1.0（沒有 0.7.0）。凍結宣告與 1.x 演進規則寫在
  `CHANGELOG.md` 的 1.0.0 一節，`CONTRIBUTING.md`「Interfaces and versions」是對外的摘要。
  **凍結的是語法，不是稽核語意**（審查 1.0 A2：舊版宣告把兩者混在一起，還凍住了一個會誤判的啟發法）。九個面：
  CLI 旗標（含 `--`）、退出碼（含判定順序）、JSON 報告（schemaVersion 6 + `docs/report.schema.json`）、Action inputs、
  config 檔（含預設值）、baseline 檔的格式（schemaVersion、keyVersion、欄位、`file|element|fg|bg` 的鍵）、
  SARIF 的 `wcag-contrast` 規則與等級對應、壓抑註解語法、稽核範圍的**硬規則**（`*.xaml`、bin／obj、`*.cs` 只找 C# 色盤、
  config 只讀 root）。**不凍結**：找到哪些配對、配到什麼底、測試／範例路徑判準、unresolved 的分類、鍵裡的文字
  （element 標籤、合成底的描述）—— 這些改了是 minor，鍵的寫法變了就遞增 `Baseline.KeyVersion`。
  - **1.x 只增**：新旗標／input／config 屬性／JSON 欄位／SARIF 規則／開放集的新值 = minor。
    開放集：unresolved 原因、paletteMode、failOn、theme；封閉集：category、symmetry、baseline。
    改名、刪除、**改預設**、收緊驗證 = 2.0（改預設比 Parity 的範本嚴，刻意的：預設值決定 gate 本身，不在 minor 改；
    稽核語意的改進仍可能讓 build 變紅，會寫在 Upgrading）。
  - 第 9 面凍的是**預設行為**：opt-in 的範圍擴充（`--config`、額外排除路徑、`.axaml`）= minor；改內建範圍 = 2.0。
    console 與 Markdown 的輸出屬 presentation、不凍結，機器讀取請用 JSON。
  - **`--push`（實驗性）不在凍結面內**（`--push` 不送匿名統計，`--stats-preview` 與它並用是用法錯誤），直到 Parity.Server 儀表板公開上線：`--push`／`--server`／`--commit`／`--branch`、
    `XAMLCONTRAST_SERVER`／`XAMLCONTRAST_TOKEN`、push 模式的退出碼（0 送出、2 其他一切）與伺服器協定
    （`POST {server}/api/runs`、`X-Parity-*` 標頭、201 回應的形狀）都可以在 minor 改。上線後併入凍結（旗標進第 1 面、退出碼進第 2 面），
    CHANGELOG、README 兩份、SECURITY、CONTRIBUTING 的「實驗性」字樣一起拿掉。比照 Parity 的 `parity push`（伺服器上線前不凍結）。
    - 用旗標不用 `push` 子指令：`xamlcontrast push` 今天是「稽核名叫 push 的目錄」，子指令會打破第 1 面。
    - **不加 Action input**：input 一加就凍結（第 4 面）。README 教使用者在 action 之後自己接一步
      `xamlcontrast --push "$RUNNER_TEMP/xamlcontrast-report.json"`。⚠ 那一步靠 `action.yml` 的兩件事：Install 那步把
      `~/.dotnet/tools` 寫進 `GITHUB_PATH`、Run audit 的 `--json` 落點固定在 `$RUNNER_TEMP/xamlcontrast-report.json`。
      改了任何一個，README 的那一步就安靜失效。
    - 協定跟 Parity repo 的伺服器端一起定，**不要單方面改**（伺服器照這個形狀實作，`CliPushTests` 釘住線上的樣子）。
  - **1.1 的新增（凍結面，minor）**：`--html`、`--stats-preview`（第 1 面）、`findings[].colors`（第 3 面）、Action input `share-stats`
    （第 4 面）、config 的 `stats.share`／`stats.projectId`（第 5 面）。HTML 的**版面**跟 console、Markdown 一樣是 presentation、不凍結。
    **匿名統計送出去的 JSON 不是凍結面**：它的真相源是 `D:\擴充功能\nuget\stats-collector\PROTOCOL.md`，跟收集站與 Parity 一起演進
    （改欄位先改協定、收集站先上線，工具才能送新欄位 —— 收集站多一個欄位就整筆拒收）。
  - **1.1.0 的發版條件**：研究倫理對「預設打開」的確認**已取得（2026-10-28，維護者確認）**；收集站 stats.cornhsu.com
    也已上線（2026-10-28）；發版前確認說明頁上的 stats@cornhsu.com 收得到信（`稽核-2026-10-22/統計功能規劃.md`）。
    預設送不送是**一個常數** `Stats.ShareByDefault`（`src/XamlContrast.Cli/Stats.cs`）：審查要求主動同意就改成 `false` ——
    之後只有使用者明確說好（`*_SHARE_STATS` 是 1／true／yes／on，或 `"stats": { "share": true }`）才送；config 的 `stats.share`
    不寫與 Action 的 `share-stats: 'true'` 本來就是「不表態」，預設值不必跟著改。協定說的「第一次在終端機詢問」那時才做，
    README 兩份、CHANGELOG 的提示、SECURITY 的「預設送」字樣一起改。
    發版之後才改成預設不送：凍結規則沒寫到「只會送得更少」的預設變更，要不要算 2.0 由維護者決定（建議在 1.1.0 的 CHANGELOG 先講明）。
  - 對外的說法（README 兩份、SECURITY、CONTRIBUTING、RELEASING）一律是「語法凍結；稽核語意與 baseline 鍵的寫法可以在 minor 改變，
    Upgrading 會說明，keyVersion 遞增時舊 baseline 被拒並提示重寫」。⚠ 不要寫成「1.x 升級不會打壞任何東西」。
  - SARIF 的等級表示嚴重度、不表示 gate（預設 gate warn 時 warn 讓 CI 失敗，SARIF 裡照樣是 warning）；
    跟著 gate 走的是 Action 的行內註記。
  - **JSON 新增欄位不遞增 `schemaVersion`**（`Report.JsonSchemaVersion`，凍結在 6；消費端必須忽略未知欄位）。
    但 `docs/report.schema.json` 與 `JsonReportContractTests` 的形狀表要跟著加，測試會擋。
    破壞性改變才遞增，而且只在 major。⚠ 0.x 是「形狀變了就遞增」，1.x 不是。
  - **稽核語意的改進（計數會動）是 minor，不是 patch**，CHANGELOG 開頭要有 Upgrading 說明實測影響；
    baseline 的鍵會動的話要明講、叫使用者重跑 `--write-baseline`。
  - README 兩份引用移動式 `@v1`，1.x 發版**不必**改 README 的版號。每發一個 1.x 把 `v1` 前移
    （`git tag -f v1 v1.x.y && git push -f origin v1`，見 `RELEASING.md`）；`release.yml` 只認三段式 tag，
    前移 `v1` 不會觸發發版。
  - `action.yml` 在 `version` 留空時裝 `"[1.*,2.0.0)"`（上界不能省：光寫 `"1.*"`，NuGet 上沒有穩定 1.x 時會裝到 2.0.0）。
    發 2.0.0 時要改成 `"[2.*,3.0.0)"`
    （`ActionContractTests.TheFloatingMajorIsTheLatestReleasesMajor` 會在 CHANGELOG 頂端變成 2.x 時紅）。
  - `release.yml` 會核對 CHANGELOG 最上面的版號 = tag，不一致不發。
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
- **靜默退化等於謊報健康。** 所以 `Program.cs` 的退出碼有三道「結果不可信」的保險（exit 3）：
  0 組配對、色盤退化（`--strict-palette`）、覆蓋率低於下限（預設 50%）；之後才是 gate（exit 1）。
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

`0`／`1`／`2`／`3` 是對使用者的契約（1.0 凍結）：0 通過、1 低於 gate、2 用法／設定／輸入檔錯誤、3 結果不可信。
**輸出寫檔失敗必須落在契約內**（2）——之前是裸 `File.WriteAllText`，目錄不存在就噴 .NET 堆疊、exit 127。

判定順序（先中先回；`Program.cs` 的旗標解析與 `DecideExit()`）：

| # | 條件 | 退出碼 |
|---|---|---|
| 1 | 旗標錯誤（未知旗標、缺值、值以 `-` 開頭、`--fail-on`／`--min-coverage` 不合法、`--baseline` 與 `--write-baseline` 並用、缺 root） | 2 |
| 2 | root 不存在 | 2 |
| 3 | config 錯誤（JSON、型別、範圍、未知屬性、`largeText > normalText`、`layout` 兩邊都宣告、`null`）；連檔案清單都拿不到 | 2 |
| 4 | `rootBackground` 在色盤裡解析不出顏色 | 2 |
| 5 | `--baseline` 檔不存在或不合法（**稽核之前**就驗：缺 `schemaVersion`、版本不認得、缺欄位） | 2 |
| 6 | 稽核時讀不到專案 | 2 |
| 7 | 0 組配對 | 3 |
| 8 | `--strict-palette`（或 config）且色盤退化 | 3 |
| 9 | 覆蓋率（`CoveragePercent`，四捨五入到一位）< 下限 | 3 |
| 10 | `--write-baseline` | 0（檔案在判定之後寫；寫不出去 → 2） |
| 11 | `--baseline`：有新增或惡化（只看目前 gate 擋的） | 1，否則 0 |
| 12 | fail > 0 | 1 |
| 13 | gate 是 warn（預設）且 warn > 0 | 1 |
| 14 | 其餘 | 0 |
| 15 | 判定之後寫 `--json`／`--sarif`／baseline／`--md`／`--html`，任何一個寫不出去 | 2 |
| 16 | 全部寫完之後送匿名統計（`Stats.AfterAudit`）—— **不改退出碼**，送不出去最多多花 3 秒 | 同 11–14 |

JSON 的 `exitCode` 記的是判定（0／1／3）；第 15 列的 2 發生時，JSON 已經寫出去了，統計不送（只有 0／1／3 才送）。

**`--push`（實驗性）不走這張表**：旗標解析完就分流（帶了任何稽核輸入 → 2），之後只有 0（送出）與 2（其他一切）。
不借用 1／3 —— 那是稽核的判定，上傳失敗長得像「有違規」的話，CI 分不出來。

⚠ **`--strict-palette` 與覆蓋率下限必須留在模式分支（`--baseline` / `--write-baseline`）
之前。** 這兩個檢查原本擺在 `DecideExit()` 尾端，而 baseline 兩個分支都會直接 return，
於是永遠走不到——偏偏 `--baseline` 正是 README 推薦給既有專案的導入路徑。
實測過的失效鏈：主題檔被搬走 → 色盤偵測退化 → 配對全變 unresolved → 從 findings 消失 →
ratchet 判成「已還債」→ 綠燈，還印出「paid off N」恭喜你把債還清了。
**弄壞主題檔看起來像修好了所有問題。**

### 數值與字串解析

- **WPF 的 `FontSize` 是 DIP（1/96 吋）不是 point（1/72 吋），換算要 ×0.75。**
  18pt = 24px、14pt = 18.67px。少這一步會把 18px 的字當大字級放過。
  `Auditor.cs` 有三處在算（樹走訪、Style 鏈逐狀態、`WalkStyles`），改要一起改；三處都經 `ParseFontSize`，
  它照 WPF 的 FontSizeConverter 認單位（px、in = 96、cm = 96/2.54、pt = 96/72）—— `FontSize="18pt"` 是 24 DIP、大字級。
- **所有數值屬性一律 `InvariantCulture`**（`Auditor.ParseDouble`）。否則德語系 locale 的
  CI 上 `Opacity="0.5"` 會靜默解析失敗。
- **`BoldWeight()` 是錨定的整字匹配。** 舊版用子字串（`"Bold"` 命中 `"SemiBold"`），
  把 600 也放寬到 3:1，會漏掉 3.0~4.5 之間真正不合格的文字。SemiBold/DemiBold 是 600，不算粗體。
- **色碼一律經 `ColorResolver.NormalizeHex` 正規化成 7（`#RRGGBB`）或 9（`#AARRGGBB`）碼**：
  WPF 的 `#RGB`／`#ARGB` 逐位展開，其他長度、非十六進位字元不收（`#GGGGGG` 收下之後
  `Wcag.Luminance` 的 `Convert.ToInt32` 會直接崩潰）。正則寫的是 `{6,8}`／`\w+`，七位數的打字錯誤會被吃下來，
  而 `Wcag.Luminance` 只讀前六位——等於默默猜前半段再回報一個很篤定的對比值。讀不懂就不收，讓它走
  UnknownKey 的計數喊出來。具名色表是 WPF 的 140 色（由 `KnownColor` 的網頁色產生，排除系統色與 RebeccaPurple）。
- **合併式色盤裡「讀不懂」≠「沒定義」**：某一側有定義但值讀不懂（打字錯誤、`Color="{Binding}"`、
  讀不懂的 `Opacity`），不能走「缺的一側退回另一側」—— 那會拿另一個主題的值編出比值。
  `PaletteDetector.InvalidValue` 佔住那一側，整個鍵剔除並在偵測描述點名。
- **色盤檔的正則掃描前先剔除 `<!-- -->`**（`ReadXamlWithoutComments`）：註解掉的舊定義會蓋掉現役值。

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
- **「當色盤來源」與「不算進 files」是兩件事**（`PaletteDetector.IsResourceDictionaryFile`）：
  只有根元素是 `ResourceDictionary` 的色盤檔不算畫面檔，但**照樣走樹、照樣跑 `WalkStyles`**——
  元素端的鏈路徑看到「字與底同源」就略過、交給定義處，定義處沒人走的話那組配對就從報告與所有計數器消失；
  字典裡的 DataTemplate 也一樣。比對排除清單時兩邊都 `GetFullPath`。
- **畫面檔的區域定義只是那個畫面的**：畫面檔（Window／UserControl…）的區域定義不進全域的 Color／brush 表；
  字典沒定義、只有畫面定義的鍵記在 `Palette.LocalOnly`（照樣算進偵測），稽核時 `Auditor.VisibleIn` 只讓定義它的畫面看得到。
  在那個畫面裡由 `LocalOverrides` 蓋過全域：字面值兩欄同值、`Color` 引用對「畫面的 Color ∪ 全域 Color 表」逐側解析
  （`Palette.DarkColors`／`LightColors`）；讀不懂 → `Palette.RuntimeKeys`（bound-or-gradient），找不到 Color →
  `Palette.MissingRefs`（unknown-palette-key，點名 Color）。⚠ 補全域的洞也是猜：只在 Badge.xaml 定義的鍵，Main.xaml 執行期查不到。
- **主題提示有兩套，不能混用**：
  - 色盤分組（`PaletteDetector.GroupHint`）要**寬鬆**：嚴格提示認得就用，否則退回 v0.6.1 的整條路徑子字串。
    ⚠ 分組也用整詞判定時，`darktheme.xaml`、`Dark2.xaml`、`深色Dark.xaml` 的兩個主題全變成中性、色盤塌成單一主題、
    淺色 fail 整欄消失，而且不算退化。檔名完全看不出主題、≥ 3 個鍵在不同字典裡值不同 → 判成退化並點名。
  - 只評單欄（`PaletteDetection.ThemeFileSides`）要**嚴**：檔名或最後一段目錄有「完整的詞」dark／light
    （`ThemeHint`，Highlight、Starlight/ 不算）**而且定義了色票**，或另一個主題的對應檔存在（`CounterpartExists`，
    此時寬鬆提示也算）。⚠ 認錯會藏 fail：兩個主題共用的 `Styles/DarkButton.xaml` 只評深色，淺色 fail 消失。
  那些檔用單一主題視圖評（對稱標 single-theme；`Finding.Theme` 記是哪個主題，另一個主題的比值經 `Finding.RatioIn` 是 null ——
  ⚠ `RatioDark`／`RatioLight` 內部兩欄都填那個主題的值，對外輸出一律走 `RatioIn`，不然會印出不存在的主題的比值）；`StyleIndex.Build(sideOf)` 讓同 key 的 Style 在使用處依主題
  各自解析，兩個主題解析到不同的鏈時各評各的（`@dark`／`@light`），只有一個主題解析得到時另一側記 unknown-palette-key。
  拿 Dark 的 Style 配淺色色票是執行期不存在的組合。C#／單一主題模式不建這張表——Kindling 的 `DarkTheme.xaml`
  是 C# 色盤的靜態複本，兩個主題共用。
- **Application 含子類別**（`PaletteDetector.RootKindOf`）：根元素名稱以 Application 結尾（Prism 的 `PrismApplication`），
  或根元素底下有 `<…Application.Resources>`。只認 `Application` 的話，Prism 的 App.xaml 被當成畫面、全域鍵全變成區域鍵。
- **元素 Opacity 是群組**（`Auditor.Backdrop`／`OpacityGroup`）：WPF 的 Opacity 把整棵子樹畫成一層再疊。
  底畫在變淡的那一層裡時，底跟字一起變淡（白底上 Opacity 0.5 的黑 Border 裡的白字是 3.95:1，不是 5.32）。
  底在群組外面時與 `EffectiveForeground` 逐位相同；變淡的一層底下沒有底 → translucent-uncomposited。
- **多 App 作用域**：外層 App 的子樹要扣掉內層 App 的目錄；不屬於任何 App 的檔案沒有共用區色盤時
  用空色盤，不是退回全域（全域是各 App 合併，等於借用某個 App 的值）。
- **半透明字色疊到底上評分**（`Auditor.EffectiveForeground`，三條路徑同一套）：`#AARRGGBB`、帶 Opacity 的色票、
  只有一個主題半透明的色票都以各自的 alpha（乘上元素 Opacity）合成到已解析的底上。舊設計一律歸 skipped，
  而 skipped 不進覆蓋率分母——Opacity 0.2 的白字疊黑底（1.66:1）整組不在報告裡、覆蓋率照樣 100%。
  完全透明、Opacity 0 才是合法跳過（排在底的判斷前面：看不見的字不管底是什麼都看不見）；底沒解析出來照舊進 unresolved。
  Style 定義處遇到透明／半透明的**底**：有元素以 `Style=` 用到的（`StyleIndex.IsUsedByAnElement`，含 BasedOn 基底）與行內
  Style 交給使用處評、不另外計；使用處評不到的（隱含樣式、只從 code-behind／ItemContainerStyle 套用或沒用到的）記
  unresolved（translucent-uncomposited），不是 skipped——記 skipped 等於整組消失，兩邊都記則同一組算兩次。
- **元素分類用「後綴比對」不是全名相等**（`Auditor.IsNonTextElement`）：MahApps 的
  `MetroProgressBar` 對不上 `ProgressBar`，進度條填色會被當文字用 4.5 要求。
  兩邊都中時取最長的後綴（名字愈長愈具體）。

### baseline 的鍵

`Baseline.KeyOf` 是 `file|element|fg|bg`，**刻意不含行號**——行號隨編輯漂移，含進去每次
重排版都會「假性新增」。相對地必須存 `Count`（同鍵處數變多 = 惡化）與 `Worst`
（鍵不變但色盤值被調暗 = 惡化）。少任何一欄都會有一整類惡化靜默放行 —— 所以 1.0 起缺欄位是 exit 2，不再補 0。
`paid off` 數的是**處數**不是鍵數，要跟 `knownDebt` 同單位。
1.0 起 baseline 收「低於 AA」的每一組（fail＋warn），不論寫出時的 gate；比對時只擋目前 gate 擋的那些
（`--fail-on fail` 下 warn 不判新增或惡化）。檔頭有 `"schemaVersion": 1`；0.x 的檔沒有，一律拒絕並叫人重寫
—— 照讀的話既有的 warn 全部會變成 NEW。
**`keyVersion`（`Baseline.KeyVersion`，缺省視為 1）**：鍵的文字（`element` 的標籤、`fg` 的寫法、`bg` 的合成描述）屬稽核語意，
1.x 的 minor 可以改 —— **改了產生鍵的方式就要遞增 `KeyVersion`**，舊檔會被拒絕並叫人重寫，不然使用者會看到一整排假的 NEW。
⚠ **Parity 的儀表板照這個鍵與 `Compare` 的比法手刻了一份**（Parity repo 的 `ContrastCompare`，「跟上次比」用；2026-10-28）：
伺服器不引用這裡的組件，JSON 報告裡也沒有 keyVersion。遞增 `KeyVersion` 或改 `Compare` 的判定時，那邊要一起改，
不然儀表板上跨版本的兩筆 run 會冒出一排假的「新增」與「修好」（那邊只在兩筆的 toolVersion 不同時提醒）。

### `--push`（實驗性，`Push.cs`）

- **空字串一律當沒設**（`Push.Env`）：push 事件的 `GITHUB_HEAD_REF` 是空字串，用 `??` 接的話分支會送 `""`、
  永遠退不到 `GITHUB_REF_NAME`。`--server`／`--commit`／`--branch` 的空值也是用法錯誤。
- **伺服器回的文字是不可信的輸入**：錯誤訊息、專案名、網址印出來一律經 `Report.ConsoleSafe`，job summary 那行也是 ——
  跟稽核的 console 輸出同一條規矩（審查 1.0 最終驗證 S2）。push 不在 Action 的 `::stop-commands::` 裡面，只有這一道。
- **不跟轉址**（`AllowAutoRedirect = false`）：跟了的話 POST 變 GET、本文丟掉、.NET 拿掉 Authorization，
  使用者只看到莫名的 401／405；token 也只該送到使用者指名的伺服器。
- **標頭只帶可見 ASCII**：中文分支名 .NET 直接拒送（整次推送失敗），所以那一欄不送、stderr 說明。
- 本文是檔案原樣（只拿掉 BOM），不重新序列化；schemaVersion 比對 `Report.JsonSchemaVersion`，不寫死 6。
- 測試的環境變數只設在子行程（`Cli.RunEnv`）：CI 的 runner 自己帶著 `GITHUB_SHA`、`GITHUB_STEP_SUMMARY`，
  測試行程的環境變數又是平行測試共用的。stub 用 `TcpListener`（`HttpListener` 在 Windows 走 http.sys，非管理員綁不上 127.0.0.1）。

### HTML 報告（`Report.Html.cs`）

- **裡面的文字全部不可信，而且會從 CI 的 artifact 直接在瀏覽器打開**：一律經 `Report.H`（控制字元、U+2028／2029 與 Cf 類格式字元
  —— 雙向覆寫 U+202E 能讓檔名倒著顯示 —— 換成看得見的 `\uXXXX`，再跳脫 `& < > " '`）；掃描內容**不進任何屬性**，
  `style` 只放 `Report.CssHex` 驗過的 `#RRGGBB`。CSP（`default-src 'none'; style-src 'unsafe-inline'`）是第二道。
  `HtmlReportTests` 用標籤與屬性的白名單驗整份輸出 —— 加新標籤或屬性要一起加進白名單，不是放寬檢查。
- `Card` 的兩欄是 **HTML**（呼叫端跳脫），標題與說明是純文字。來自掃描內容的標籤先經 `H`。
- 色塊就是 `Finding.ColorsIn` —— 跟 JSON 的 `colors` 同一份；函式庫呼叫端自己建、沒有顏色的 finding 不畫色塊、比值照印。
- ⚠ 寫測試時 `Assert.DoesNotContain(string, string)` 預設是文化比較，**格式字元（U+202E）會被忽略、恆為「找到」** —— 要加 `StringComparison.Ordinal`。

### 匿名使用統計（`Stats.cs`、Core 的 `UsageStats.cs`）

協定是 `D:\擴充功能\nuget\stats-collector\PROTOCOL.md`，**不要單方面改**；收集站的 `src/validate.ts` 是實際驗的那一份（多一個欄位就整筆拒收）。

- **研究資料的保護（測試）**：`Cli.Start` 對每個子行程預設 `DO_NOT_TRACK=1`；要測統計的測試在 env 裡明確給 `DO_NOT_TRACK`（null = 拿掉）。
  `CliStatsTests` 的收集站是 127.0.0.1 的 stub（`CORNHSU_STATS_URL`），另外把 `HTTP(S)_PROXY`（大小寫都設）指到第二個 stub、
  `NO_PROXY` 只放行 127.0.0.1 —— 哪條路徑出錯往外送也只會落到它（`TheEgressGuardCatchesEveryNonLoopbackRequest` 證明這張網接得住）。
  兩個 workflow 在最上層設 `DO_NOT_TRACK: '1'`（`WorkflowsTurnStatisticsOffForTheMaintainersOwnRuns` 守著）—— release 用真的版號跑測試與 demo。
- **標記檔在 Windows 測不了**：`Environment.SpecialFolder.ApplicationData` 在 Windows 走 Shell API，**不吃 `APPDATA`／`USERPROFILE` 環境變數**
  （2026-10-26 實測），測試寫下去就是開發者真的設定目錄。所以本機說明＋標記檔只在 Linux 測（`XDG_CONFIG_HOME` 指到暫存目錄），
  其他統計測試一律 `CI=true`（CI 模式不讀不寫標記）。
- **開發版是 `0.0.0-` 開頭的任何版本**（`UsageStats.DevVersionPrefix`；協定 2026-10-26 從「只有 `0.0.0-dev`」放寬）：原始碼的 `0.0.0-dev`、
  CI pack-smoke 的 `0.0.0-ci`、`nuget-packages` skill 乾跑的 `0.0.0-local.*` 都不送。⚠ `RELEASING.md` 的本機預覽版（`1.1.0-rc.1`）**不是**
  開發版 —— 拿它跑稽核會送出真的統計，要自己設 `DO_NOT_TRACK=1`。子行程只測得到這次建置的版本，其他版本字串由
  `DevelopmentVersionsNeverSendWithoutAStatsUrl` 直接餵 `UsageStats.Decide`；子行程那條（`DevBuildFact`）在真版號的建置上略過。
- 開關的值比對**不 Trim、不寬鬆**（`" true"`、`ture` 都是不送）：協定要「寫錯往不送那邊錯」。`DO_NOT_TRACK` 只有 `0`／`false` 不算。
- 傳輸：`SocketsHttpHandler` 的 `ActivityHeadersPropagator = null`（不然有 listener 時會帶 traceparent）、`UseCookies = false`、
  `AllowAutoRedirect = false`；`HttpClient` 預設不帶 User-Agent。**全部例外都吞**（`catch (Exception)`）是刻意的 —— 統計失敗絕不能改退出碼。
- 預覽印的就是送出去的位元組：`NewLine = "\n"` 固定（`WriteIndented` 預設用 `Environment.NewLine`，兩個平台會不一樣）。
  說明的 raw string 在 CRLF checkout 下帶 `\r\n`，印之前正規化。
- job summary：送了的那一行由 CLI 寫（只有它知道到底送了沒 —— DO_NOT_TRACK、設定檔也關得掉），`share-stats: false` 那一行由 action.yml 寫。
- Action 的 `share-stats: false` 在 Run audit 的 script 裡 `export`，**不寫進 step 的 `env:`** —— step 的 env 會蓋掉呼叫端 workflow／job 層的
  同名變數，`true` 時呼叫端的 `XAMLCONTRAST_SHARE_STATS=false` 就失效了。
- `stats.projectId` 逐字檢查，不用正則的 `$`（它在結尾的換行前也成立）。

### 守門腳本自身

- **`verify-readme-sample.ps1` 靠標題錨點定位**：`## What it looks like`（英）與
  `## 實際跑起來長這樣`（中）。**改 README 標題會讓守門員失效**——腳本會紅並明講
  「錨點被改掉了」，看到這個訊息是去改腳本，不是去改回標題。
- 這支**會在 Linux 的 CI 上以 pwsh 跑**，所以路徑用正斜線；讀 README 一定要
  `-Encoding UTF8`（Windows PowerShell 預設用系統 ANSI 讀，中文版 README 會整份變亂碼）；
  寫回去用 `UTF8Encoding($false)` 明確無 BOM（`Set-Content -Encoding utf8` 在
  Windows PowerShell 會塞 BOM、pwsh 不會，同一支腳本兩邊跑會讓 README 無謂地變動）。
- **先 build、再用 `--no-build` 跑示範**（2026-10-06）。直接 `dotnet run` 會順便 restore，
  NuGet 的漏洞警告（NU19xx）就跟著 `2>&1` 混進「實際輸出」。這樣只要有新 CVE 公布，
  跟它毫無關係的 PR 也會紅燈；而失敗訊息叫人跑 `-Update`，照做的話，警告會連同路徑一起被貼進 README。
- `dotnet format` 是 CI 第一關。**送 PR 前跑一次 `dotnet format XamlContrast.slnx`**，
  不然會為了縮排來回一趟。

## 技術債（留帳）

- **四份 `baselines/*.json` 已在 2026-10-23 依 1.0 重凍**（schemaVersion 6，帶 `exitCode`）。
  重凍前四份都是紅的，每一個差異都歸因到 commit（逐 commit 重播 v0.6.1 之後的 82 個 src commit），
  寫在 `D:/擴充功能/nuget/稽核-2026-10-22/XamlContrast-快照變動說明.md`，維護者逐項看過、同意之後才跑 `-Update`。
  其中 `celflow.json` 早在 1.0 之前就是紅的（2026-08-26 實跑：`pairs` 期望 363 實得 353），
  原因是 CelFlow.WPF 在 2026-08-18 改過四個 XAML 檔，不是工具的回歸；這次重凍一併吸收。
  2026-08-28 CelFlow 改名 ZeroCel，腳本已改指 `D:\應用程式\ZeroCel\frontend\ZeroCel.WPF`
  （快照名稱沿用 `celflow.json`）；找不到專案時會紅字點名並繼續。
  這段歷史暴露了 `verify-baselines.ps1` 的結構性弱點：**受測對象是四個活在 repo 外、會自己演化的
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
- **改了 JSON 輸出的形狀** → 1.x 只能新增：`docs/report.schema.json` 與 `JsonReportContractTests` 的形狀表
  一起加（`schemaVersion` 不動）；改名或刪除要等 2.0。並確認 `action.yml` 裡那幾段 `jq`
  （行內註記、SARIF 路徑改寫）還讀得到欄位（`ActionContractTests` 會驗）。
- **改了 `action.yml`** → input 一律經 `env:` 進 run 區塊，不直接 `${{ }}` 內插；布林／列舉 input 在
  Validate inputs 那一步驗。`ActionContractTests` 用 bash 實跑 step（dotnet／xamlcontrast 換成 stub）。
- **新增任何「這組可以放過」的邏輯**（豁免／過濾／跳過）→ 一定要有計數器 + 回歸測試。
  開發期間這個工具產出過八份「看起來很健康但是錯的」報告，每一份現在都是一個測試。
  問自己：這條新邏輯有沒有可能讓一個真實問題從報告裡消失？
- **收尾**：動了功能面 → 兩份 README 都要同步（中英版曾經一份寫十七條規則、
  另一份只列十三條）；重大決策記進這份 CLAUDE.md、workaround 留技術債帳。

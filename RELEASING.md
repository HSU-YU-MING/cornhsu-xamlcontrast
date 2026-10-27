# Releasing（照 Parity 的形狀）

## 流程

1. 確認 `CHANGELOG.md` 最上面的版本段落完整（標題寫 `## 1.2.0` 或 `## 1.2.0 — 2026-11-03` 都可以，別加方括號）。
   `release.yml` 會核對 tag 與 CHANGELOG 頂端的版號，不一致就不發。
   - `scripts/verify-baselines.ps1` 綠燈。**不一致時不要為了變綠就跑 `-Update`**：快照不一致 = 行為變了，
     先逐項說明每一個差異是哪個 commit 造成的，給維護者看過、同意之後才重新凍結
   - 改過 console 輸出的話，`scripts/verify-readme-sample.ps1` 也要綠（CI 會擋，但發版前先看一眼比較快；
     確認新輸出是對的之後才跑 `-Update` 重貼）
   - 兩份 README 引用的是移動式 `@v1`、引言寫的是 1.x —— 1.x 期間發版**不必**改 README 的版號
   - **1.1.0（第一個送匿名統計的版本）已發佈（2026-10-28）**：發版前研究倫理對「預設打開」的確認已取得、收集站
     `https://stats.cornhsu.com/` 已上線、刪除聯絡地址 `stats@cornhsu.com` 收得到信。日後要再驗正式站：
     打得開資料說明頁；對 `POST /v1/xamlcontrast` 送故意不合法的 `{}` 回 400 —— 不要拿合法資料試正式站，那一筆會混進研究資料。
     日後審查若改要求主動同意，只改 `Stats.ShareByDefault` 這一個常數（細節在 CLAUDE.md 的凍結段落）
2. 推版號 tag：

```bash
git tag v1.2.0
git push origin v1.2.0
```

3. `.github/workflows/release.yml` 接手：核對 CHANGELOG → build → test → demo 守門（exit 1）→
   `dotnet pack` → 以 **NuGet Trusted Publishing**（OIDC，無長期 API key）發佈 `Cornhsu.XamlContrast` →
   以 CHANGELOG 那一節建 GitHub Release
4. 發完之後把移動式 major tag 前移到這一版（見下一節）：

```bash
git tag -f v1 v1.2.0 && git push -f origin v1
```

## 移動式 major tag（`v1`）

GitHub Action 的使用者寫 `uses: HSU-YU-MING/cornhsu-xamlcontrast@v1`。`v1` 是一個**移動式** tag：
每發一個 1.x 就前移到那一版，`@v1` 的使用者自動拿到 1.x 的修正，但不會跨到 2.0。

- **1.0.0 首發**（一次性）：

```bash
git tag v1.0.0 && git push origin v1.0.0     # 觸發 release.yml（守門比對 CHANGELOG 頂端 = 1.0.0）
git tag v1 v1.0.0 && git push origin v1      # @v1 的使用者從這裡開始拿到 1.x；這個推送不觸發 release
```

- 之後**每發一個 1.x**：`git tag -f v1 v1.x.y && git push -f origin v1`
- 移動式 tag 的推送**不會**觸發 release —— `release.yml` 只認三段式 `v*.*.*` tag，這是刻意的：
  `v1` 若觸發，守門會因 tag ≠ CHANGELOG 版號而必紅一次，徒增失敗通知（Parity 發 1.0.0 與 1.1.0 時各踩一次）
- Action 的工具版本也鎖在同一個 major：`action.yml` 在 `version` 留空時裝 `"[1.*,2.0.0)"`（最高的 1.x 穩定版；上界擋住 2.0.0 ——
  光寫 `"1.*"` 的話，NuGet 上還沒有穩定的 1.x 時會裝到 2.0.0）。
  所以 `v1` 停在哪一版都不會裝到 2.x 的工具
- **下一個 major（2.0.0）**要一起改：`action.yml` 的 `"[1.*,2.0.0)"` → `"[2.*,3.0.0)"`（`ActionContractTests` 會在 CHANGELOG 頂端
  變成 2.x 時紅燈）、兩份 README 的 `@v1` → `@v2` 與 1.x 的說明、建 `v2` tag。`v1` 停在最後一個 1.x，不再前移

## 版本規則

- **版本號的唯一真相源是 git tag。** `release.yml` 用 `-p:Version=<tag>` 覆寫，
  發版時**不需要也不應該**去改 csproj。csproj 裡那格固定是 `0.0.0-dev`，
  用意是讓「從原始碼 build 的」一眼看得出不是正式版 —— 曾經寫死 0.5.1
  而跟著 v0.6.0 一起漂掉，source build 自稱舊版、SARIF 與 JSON 報告的工具版本也錯
- 唯一要人手維護版號的地方是 **`CHANGELOG.md`**：它是「還沒推 tag 之前」的版本真相源
- **1.0 起介面凍結**（凍結宣告與 1.x 的演進規則見 `CHANGELOG.md` 的 1.0.0 一節，對外摘要在 `CONTRIBUTING.md`）。
  凍結的是**語法**（旗標、退出碼、JSON 欄位、input、config、baseline 檔的格式、SARIF 規則、壓抑註解、稽核範圍的硬規則），
  不是**稽核語意**（找到哪些配對、配到什麼底、測試路徑的判準、鍵裡的文字）：
  - 只新增（新旗標、新 input、JSON 新欄位、config 新屬性、開放集的新值）= minor
  - **稽核語意的改進 = minor**（計數會動，所以不是 patch），CHANGELOG 開頭寫 Upgrading 與四個驗證專案的實測影響；
    baseline 的鍵因此改變時遞增 `Baseline.KeyVersion`
  - 改名、刪除、改預設值、收緊驗證（凍結的語法）= 2.0。預設值決定 gate 本身，不在 minor 改；
    語意改進仍可能讓 build 變紅，那一版的 Upgrading 要寫明
  - 稽核範圍凍的是預設行為：opt-in 的擴充（`--config`、額外排除路徑、`.axaml`）= minor，改內建範圍 = 2.0
  - 輸出完全不變的修正 = patch
  - JSON 的 `schemaVersion` 只在破壞性變更時遞增（也就是只在 major）；新增欄位不遞增，消費端必須忽略不認得的欄位與開放集的新值

## 預覽版（目前沒有發佈路徑）

`v1.1.0-rc.1` 之類的預覽版 tag 會被 `release.yml` 擋下：觸發條件是三段式 `v[0-9]*.[0-9]*.[0-9]*`（`-rc.1` 結尾照樣符合），
但「CHANGELOG 頂端版號 = tag」的守門只取三段式的版號（`1.1.0`），與 `1.1.0-rc.1` 不相等 → 失敗、不發佈。
這是刻意的現狀（Parity 也沒有預覽版路徑）：要試新版就從原始碼 build，或在本機打包一份預覽版來裝：

```bash
dotnet pack src/XamlContrast.Cli/XamlContrast.Cli.csproj -c Release -p:Version=1.1.0-rc.1 -o /tmp/xc-rc-pkg
dotnet tool install Cornhsu.XamlContrast --tool-path /tmp/xc-rc-tool --add-source /tmp/xc-rc-pkg --version 1.1.0-rc.1
/tmp/xc-rc-tool/xamlcontrast --version     # 應該印 1.1.0-rc.1
```

四個坑：

- **試裝的版本會送匿名統計。** `1.1.0-rc.1` 這種版號不是開發版（只有 `0.0.0-` 開頭的才是），拿它跑稽核就會往
  stats.cornhsu.com 送一筆真的統計、混進研究資料 —— 試之前先 `export DO_NOT_TRACK=1`（PowerShell：`$env:DO_NOT_TRACK = '1'`）。

- **安裝一定要帶 `--version 1.1.0-rc.1`。** 不指定版本時 NuGet 不選預覽版，會裝到 nuget.org 上的正式版 ——
  `--add-source` 只是多一個來源，不會讓預覽版優先。
- **每次重新打包都換一個版本號**（`-rc.2`、`-rc.3`…）。同一個版本號第二次 pack，install 會拿
  `~/.nuget/packages` 裡快取的上一份，看起來裝好了、其實是舊內容。
- **用 `--tool-path` 裝到暫存資料夾，不要 `--global`。** 全域已經裝了正式版時 `--global` 會失敗
  （同一個工具在全域只能有一份）；暫存資料夾用完整個刪掉即可，全域的正式版不受影響。

真的需要發預覽版到 nuget.org 時，要一起改的是：守門接受「CHANGELOG 頂端 = tag 去掉
預發行標籤」、GitHub Release 標成 prerelease、而且**不前移 `v1`**（`@v1` 只能指向正式版；`action.yml` 的
`[1.*,2.0.0)` 本來就不會裝到預覽版）。

## 首次發佈前的一次性設定

- NuGet.org：套件 `Cornhsu.XamlContrast` 設定 Trusted Publishing
  （repo `HSU-YU-MING/cornhsu-xamlcontrast`、workflow `release.yml`）
- GitHub repo settings：Actions 需 `id-token: write`（workflow 已宣告）

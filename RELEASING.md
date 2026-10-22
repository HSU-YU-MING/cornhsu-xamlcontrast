# Releasing（照 Parity 的形狀）

## 流程

1. 確認 `CHANGELOG.md` 最上面的版本段落完整（標題寫 `## 1.2.0` 或 `## 1.2.0 — 2026-11-03` 都可以，別加方括號）。
   `release.yml` 會核對 tag 與 CHANGELOG 頂端的版號，不一致就不發。
   - `scripts/verify-baselines.ps1` 綠燈。**不一致時不要為了變綠就跑 `-Update`**：快照不一致 = 行為變了，
     先逐項說明每一個差異是哪個 commit 造成的，給維護者看過、同意之後才重新凍結
   - 改過 console 輸出的話，`scripts/verify-readme-sample.ps1` 也要綠（CI 會擋，但發版前先看一眼比較快；
     確認新輸出是對的之後才跑 `-Update` 重貼）
   - 兩份 README 引用的是移動式 `@v1`、引言寫的是 1.x —— 1.x 期間發版**不必**改 README 的版號
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
- **1.0 起介面凍結**（凍結面與 1.x 的相容性規則見 `CHANGELOG.md` 的 1.0.0 一節與 `CONTRIBUTING.md`）：
  - 1.x 只能新增：新旗標、新 input、JSON 新欄位、config 新屬性 = minor
  - 改名、刪除、改預設值、改語意 = 2.0
  - JSON 的 `schemaVersion` 只在破壞性變更時遞增（也就是只在 major）；新增欄位不遞增，消費端必須忽略不認得的欄位

## 首次發佈前的一次性設定

- NuGet.org：套件 `Cornhsu.XamlContrast` 設定 Trusted Publishing
  （repo `HSU-YU-MING/cornhsu-xamlcontrast`、workflow `release.yml`）
- GitHub repo settings：Actions 需 `id-token: write`（workflow 已宣告）

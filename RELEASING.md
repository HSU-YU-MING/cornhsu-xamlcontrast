# Releasing（照 Parity 的形狀）

## 流程

1. 確認 `CHANGELOG.md` 的版本段落完整、`scripts/verify-baselines.ps1` 綠燈
   （改過 console 輸出的話，`scripts/verify-readme-sample.ps1` 也要綠 ——
   CI 會擋，但發版前先看一眼比較快；不一致就跑 `-Update` 重貼）
2. 推 tag：

```bash
git tag v0.1.0
git push origin v0.1.0
```

3. `.github/workflows/release.yml` 接手：從 tag 導出版本 → build → test →
   `dotnet pack` → 以 **NuGet Trusted Publishing**（OIDC，無長期 API key）發佈
   `Cornhsu.XamlContrast`

## 版本規則

- **0.x 直到介面凍結**（CLI 參數、JSON schema 含 summary、action inputs）
- README 明寫：0.x 期間 pin 確切版本；1.0 後 Action 改用 `@v1`
- JSON 格式變動時遞增 `schemaVersion`

## 首次發佈前的一次性設定

- NuGet.org：套件 `Cornhsu.XamlContrast` 設定 Trusted Publishing
  （repo `HSU-YU-MING/cornhsu-xamlcontrast`、workflow `release.yml`）
- GitHub repo settings：Actions 需 `id-token: write`（workflow 已宣告）

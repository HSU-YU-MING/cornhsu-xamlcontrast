# Roadmap

規劃書（`XamlContrast專案規畫書.md`）是完整版；這份是對外的摘要。

## 0.1.0（首發）

- [x] M0 原型報告層修正＋四專案基準線
- [x] M1 色盤自動偵測（四形狀：配對／C# 真相源／單一主題／無色盤退路）
- [x] M2/M3 .NET 移植＋輸出合約（46+ 測試；兩套獨立實作數字互證）
- [x] M4 導入模式（baseline ratchet＋ignore 註解）
- [x] M5 GitHub repo＋Action＋NuGet 發佈（Trusted Publishing，2026-07-31 v0.1.0）

## 0.x（介面凍結前）

- [ ] `xamlcontrast.config.json` 實際載入（schema 已定於 docs/config-schema.md；
      CLI 尚未讀取 —— 目前只有旗標）
- [x] findings 的 `file` 改成 root 相對路徑（正斜線，跨 OS baseline 可攜）——
      2026-07-31，同名檔不再互撞
- [ ] 已知盲區中「可修」項：TargetName 模板 Setter 的解析
- [x] 「Style＋半透明底」逐狀態路徑的重複合成修正 —— 2026-07-31，兩實作同步修
- [ ] npm 包裝（自帶執行環境的原生執行檔，照 Parity）
- [ ] SARIF 輸出（GitHub code scanning）

## 1.0 條件

見規劃書第 10 節：四專案零設定、CLI 與 JSON schema 凍結、
十一條解析規則＋八個回歸形狀全覆蓋、baseline/ignore 實戰驗證、
至少一個非作者的專案回報。

## 1.x+

- WinUI 3 / Uno / Avalonia（解析邏輯大致共用，差在色盤慣例與內建樣式 ——
  比純 WPF 大得多的市場）
- 隱含樣式解析

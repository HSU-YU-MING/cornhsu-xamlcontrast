# Roadmap

規劃書（`XamlContrast專案規畫書.md`）是完整版；這份是對外的摘要。

## 0.1.0（首發）

- [x] M0 原型報告層修正＋四專案基準線
- [x] M1 色盤自動偵測（四形狀：配對／C# 真相源／單一主題／無色盤退路）
- [x] M2/M3 .NET 移植＋輸出合約（46+ 測試；兩套獨立實作數字互證）
- [x] M4 導入模式（baseline ratchet＋ignore 註解）
- [x] M5 GitHub repo＋Action＋NuGet 發佈（Trusted Publishing，2026-07-31 v0.1.0）

## 0.x（介面凍結前）

- [x] `xamlcontrast.config.json` 實際載入 —— 2026-07-31：強制色盤模式（pair/csharp/
      single/none）、門檻與分類覆寫（非預設會標明）、failOn/strictPalette、
      requireReason；寫錯 exit 2 給欄位級訊息
- [x] findings 的 `file` 改成 root 相對路徑（正斜線，跨 OS baseline 可攜）——
      2026-07-31，同名檔不再互撞
- [x] TargetName 模板 Setter 的解析（指向模板根）—— 2026-07-31 規則 13，
      六筆假警報全消；指向內部元素維持明文不做
- [x] 「Style＋半透明底」逐狀態路徑的重複合成修正 —— 2026-07-31，兩實作同步修
- [x] SARIF 輸出 —— 2026-07-31：`--sarif` 旗標＋action 的 `sarif: true`
      （只輸出 fail/warn；退化計數器仍以 --json 的 summary 為準）
- **npm 包裝：暫緩**（2026-07-31 決定）—— Parity 做 npm 的理由是「前端使用者
  有 Node、不一定有 .NET」，但本工具的使用者是 XAML/.NET 開發者，**人人都有
  dotnet**，`dotnet tool install` 不構成採用門檻。受眾論證不成立就不付
  發佈管線與平台二進位的維護成本；出現真實的 npm 需求再開（同 M6 雲端外殼
  的決策模式）

## 1.0 條件

見規劃書第 10 節：四專案零設定、CLI 與 JSON schema 凍結、
十六條解析規則＋八個回歸形狀全覆蓋、baseline/ignore 實戰驗證、
至少一個非作者的專案回報。

## 1.x+

- WinUI 3 / Uno / Avalonia（解析邏輯大致共用，差在色盤慣例與內建樣式 ——
  比純 WPF 大得多的市場）。**前置條件：要有真實專案當驗證白老鼠** ——
  「每條規則來自實查」是本專案的憲法，沒有白老鼠的平台支援只是沒驗證過的猜測
- 隱含樣式解析

## 與 Parity 的交會點（構想，尚未排程）

- **XamlContrast.Core 抽成獨立套件**（`Cornhsu.XamlContrast.Core`）供 Parity 的
  `WpfImplementationSource` 引用（靜態預期值產生器）。**觸發條件才動工**：
  (a) Parity 的 WPF adapter 實際動工、或 (b) 出現第二個想吃解析引擎的消費者。
  在那之前不發 —— 沒有消費者的公開 API 只是提前凍結自己的重構自由。

- **Figma 色票當色盤來源**：Parity 已會從 Figma 抽真實數值 —— 色盤偵測加一個
  `figma` 模式，就能在**設計稿階段**稽核對比（程式碼還沒寫就先攔）；
  同時「實作色盤 vs Figma 色票」的漂移也有了對照組
- **執行期抽驗盲區**：本工具的已知盲區（sibling 背景、TargetName、Binding 色）
  恰好全是「只有執行期看得到」的 —— 把 unresolved／假警報類 findings 匯出成
  目標清單，用 Parity 式的取樣 harness 對跑起來的 App 抽驗像素，
  靜態全量＋執行期抽點互補
- **合約對齊**：baseline／退出碼／action 慣例已照 Parity 形狀，
  維持同一套心智模型，讓同時導入兩者的團隊只學一次

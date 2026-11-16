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
      requireReason；寫錯 exit 2 給欄位級訊息（null 區段／元素、讀不到的檔 2026-10 補齊；
      不認得的屬性名 1.0 起也是 exit 2）
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

## 1.0（2026-10-22）

- [x] 語法凍結：CLI 旗標、退出碼、JSON 報告（schemaVersion 6）、Action inputs、config、baseline 檔的格式、
      SARIF 規則、壓抑註解語法、稽核範圍的硬規則（預設行為）；稽核語意與 baseline 鍵的寫法可以在 minor 改進
      （寫 Upgrading、鍵變了就遞增 keyVersion）—— 宣告與 1.x 演進規則見 `CHANGELOG.md` 的 1.0.0
- [x] 預設 gate 改成 warn（沒達到 AA 就算失敗）；「結果不可信」改用 exit 3
- [x] 直接從 0.6.1 發 1.0.0，不經 0.7.0。規劃書第 10 節「至少一個非作者的專案回報」的條件，
      改成四個驗證專案的實測（差異逐項歸因，交維護者確認後才重凍快照）

## 1.x+

- WinUI 3 / Uno / Avalonia（解析邏輯大致共用，差在色盤慣例與內建樣式 ——
  比純 WPF 大得多的市場）。**前置條件：要有真實專案當驗證白老鼠** ——
  「每條規則來自實查」是本專案的憲法，沒有白老鼠的平台支援只是沒驗證過的猜測。
  WinUI 3 的 `{ThemeResource}`／ThemeDictionaries／框架預設主題色已在 1.4 做掉（白老鼠：WinUI Gallery）
- [x] 隱含樣式解析 —— 1.5：每個元素的底與字色都查隱含樣式，作用範圍照 WPF 的資源範圍走
      （巢狀 `<X.Resources>` 只管子樹、最內層優先、元素有自己的 Style 就不套）。
      實證價值：維護者自己的 Kindling 有一筆「按鈕上的白字其實是深色字、2.49:1」被 1.4 報成 21:1 ✓
- [x] VisualStateManager 狀態顏色的計數器 —— 1.5：`summary.stateColors` ＋ `stateColorSites` 逐筆點名
      （含 WinUI 的 `<VisualState.Setters>`）。照舊不評，只是不再靜默。
      實測 WinUI Gallery 44 處、wpfui 18、HandyControl 15，而自家四個專案是 0
- [x] **隱含樣式的觸發態** —— 1.6：隱含樣式改走 `GradeChain` 的逐狀態路徑，所以 hover／選取／勾選都評得到。
      代價如預期是 `element` 標籤（= baseline 的鍵）改變，所以 `Baseline.KeyVersion` 1 → 2，舊 baseline 被拒並叫人重寫。
      只有「真的會動到顏色」的隱含樣式走這條，否則每一筆 finding 的鍵都會無謂地變
- [x] **帶模板的控制項：模板裡哪一層畫底** —— 1.6：模板裡放宿主內容的位置，對著它在模板裡最近一層畫底的祖先評，
      `{TemplateBinding P}` 回宿主身上解；兩層都放內容就兩筆。HandyControl 的 Shield 從「配祖先白底的 1:1 假 fail」
      變成 7.46:1（#555）與 4.20:1（#1182C3），兩筆都是真的。解不出來的那一層記 `template-painted`，不退回祖先
- **模板裡的兄弟背板**（1.6 新明文化的盲區）：畫底的那一層是內容的**兄弟**而不是祖先時還解不出來。
  標記樹裡的兄弟背板早就解得出來（規則 17），模板裡還沒有 —— MaterialDesign 的按鈕模板就是這個形狀，
  目前靠「宿主自己的 Background」接住，所以不會編造，但也還沒真的算出那一層
- **觸發器重畫模板內部那一層**（1.6 起誠實記 `template-painted`）：`TargetName` 指向模板內部元素的顏色 Setter
  要解析模板內部樹才算得出來。1.6 之前是拿基礎狀態的底去評 —— 編出來的；現在是誠實的不知道，
  但那 63 組的答案其實靜態看得見（ScreenToGif 的 `ContentBorder` 寫死 `#90FFFFFF`），值得做

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

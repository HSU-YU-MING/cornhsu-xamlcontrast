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
- [x] **模板裡的兄弟背板** —— 1.7：模板裡找「畫在內容底下的那一層」改成照 WPF 的繪製順序走，所以兄弟也算得出來
      （同一個 `Grid` 格子、撐滿那一格、最靠上的贏）。疊層判定抽進 `Panels`，與標記樹的規則 17 共用一份 ——
      抄一份的話同一段 XAML 寫在畫面裡與寫在模板裡會得到不同的答案，而且沒有任何測試會紅。
      實測：17 個公開專案＋三個本機專案**逐筆完全一致**，所以它關掉的是一條編造路徑（宿主沒設底時配到祖先），
      不是一批新數字 —— MaterialDesign 的按鈕本來就靠「宿主自己的 Background」接住了
- [x] **觸發器重畫模板內部那一層** —— 1.7：記下重畫了哪一個元素、換成什麼，再看它是不是正好在那段內容底下那一疊上。
      不透明的重畫就是這個狀態的底；半透明的疊在它蓋住的那一層上（ScreenToGif 的 `AwareTabItem` 選取態把
      `ContentBorder` 畫成 `#90FFFFFF`，疊在面板的 Level3 上 → 7 筆 `8.23/20.29` 的 ok；1.6 只能說 `template-painted`，
      1.5 拿被蓋掉的顏色報 1.46:1）。另有 13 筆（MahApps 3、MaterialDesign 10）從 `template-painted` 改成
      `bound-or-gradient` —— 那一層解開之後，真正卡住的是字色的 Binding，原因終於指對地方
- **重畫的元素不在那一疊上**（1.7 新明文化）：`TargetName` 指的元素可能在模板的別的分支（跟這段內容無關），
  也可能在那一疊上但撐不滿它那一格 —— 靜態分不出來，所以維持誠實的 `template-painted`。
  同一類還有：重畫的值是漸層或 Binding（ScreenToGif 另有 7 筆 `RadialGradientBrush`，現在誠實點名那支 brush）、
  半透明的重畫層而它蓋住的那一層本身讀不懂
- **模板裡的 Image 兄弟與帶 Opacity 的兄弟**（1.7 新明文化）：前者的底是一張圖（標記樹裡記
  `over-sibling-content`，模板裡還沒有對應的計數器，收了會讓那些配對悄悄變成不可知）；後者要把半透明疊到
  它底下那一層才算得出來，而那一步回傳的是原始寫法。兩種都維持 1.6 的答案，不是編造，但也還沒算出來
- **畫在內容後面的兄弟**：那是蓋在字上面，不是當字的底 —— 文字被遮住多少靜態不可知

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

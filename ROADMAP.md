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
- [x] **狀態裡「某個具名模板元素的字色變了」** —— 1.8 做掉了。這是 VSM 評分的前置條件，而且它自己就是一個
      活著的編造來源：語料 489 個 `<Setter TargetName="X" Property="Foreground">`，狀態如果沒改別的就被整個
      略過，同時改了宿主底的就拿基礎狀態的字色去配。逐筆驗過四個形狀（Flow Launcher 的 RepeatButton 按下態
      14.94:1 ok → **2.27:1 真 fail**、WPF-UI 的 CardAction 與 ToggleButton、HandyControl 的 TabControl
      6 筆編造的 ok 改記 unresolved、MahApps 的 PART_FloatingMessage）。
      `State.ElementFg` ＋ `TemplateLayer.ContentNames` ＋ 評分處按層展開；定義處（`WalkStyles`）也要看得到，
      不然它會照舊用基礎字色評出那一筆編造的。
- **VSM 還缺的最後一塊：說得出「哪一層被打開了」**（1.8 把前兩個前置條件都補掉之後重量過一次的結論）。
  1.8 做掉了「具名元素的字色算數」與「逐層判定改用這一層自己那一疊」—— 那正是前兩次卡住的原因。
  補完之後再把 VSM 翻譯成同一個 State 模型量一次：**959 筆新 unresolved、0 組新配對**，
  Flow Launcher 覆蓋率 29.8% → 14.8%。逐筆看過：那些 unresolved 落在真的元素上（ToggleSwitch 之類），
  不是空轉 —— 卡住的是第三個原因：**非顏色的動畫也會換掉字的底**。
  HandyControl 的 Calendar 用 `DoubleAnimation` 把 `Opacity="0"` 的 Rectangle 打開，字真正的底是它。
  不算進去就會拿祖先的底去配白字（編造），保守記不可知就是那 959 筆（吵）。
  - ⚠ **這一塊也做過一輪了（2026-11-16），量出來不划算，不要再做一次。**
    實作內容正是下面那兩行：`State.LayerShown`（`Visibility`／`Opacity` 的終值讀得懂時）＋
    `BackdropChain` 記下「現在看不見、但狀態可能打開」的候選層。野外形狀不缺
    （語料 1715 個 `TargetName` + `Visibility` 的 Setter、831 個 `Opacity`），但結果是
    **+0 組配對、+0 筆 finding、+111 筆 unresolved**。
    中途曾經看到 +134 組配對／42 筆 finding，那是假的：那些狀態打開的是箭頭、勾勾這種根本不是底的東西，
    只是讓同一組配對**又被算一次**；6 筆「新 fail」逐筆查下去全是既有配對的重複計數。
    加上「只有可能當底的元素才算」那道閘之後收益歸零。**0 筆編造被修掉、0 個新問題被找到。**
    唯一留下來的是順手發現的真 bug：工具完全沒看過 `Visibility`，收起來的 Border 照樣被當成底（已修）。
  - 原本寫的做法（留著當紀錄）：`State` 要能說「哪個具名元素被打開／關掉了」，
    `BackdropChain` 要能把「原本 `Opacity="0"`、這個狀態被打開」的那一層放回那一疊上。
  - ⚠ 在那之前不要接 VSM。三次量測都指向同一個結論：**`stateColors` 的計數器已經是誠實的信號，
    把它換成更吵的信號不是改進。**
  - 原本的說明保留如下，它記著為什麼不能照舊模型直接接上去：
  - 量出來的：語料 88 處 VSM 裡，改字色的 40 處**全部 40 處**都指向具名元素，只有 4 處動宿主自己。
    所以照現在的模型評 VSM，字色會用基礎狀態的值 —— 那個組合執行期不存在，正是 1.6 花整版修掉的那類編造。
  - 同一個洞在**觸發器**上也活著：語料有 489 個 `<Setter TargetName="X" Property="Foreground">`。
    那個狀態如果只有這一個 Setter，整個狀態會被略過（盲區，不是編造）；但如果同時還改了宿主的底，
    狀態會成立、而字色用基礎值 —— 那就是編造。**這一條比 VSM 本身更該先做**，它同時解鎖兩邊。
  - 要做的事：`State` 多一格「哪個具名元素的哪個屬性變成什麼」，`TemplateLayer` 記下該位置的內容元素名字，
    `GradeChain` 的字色解析從「每個狀態一次」改成「每一層一次」。⚠ 最後那一步動到全專案最敏感的那段程式碼，
    每一筆既有 finding 都流經它 —— 要獨立一版、獨立量測
  - 做完之後 VSM 才值得接上：翻譯成同一個 State 模型（`ColorAnimation` 的 `To`／keyframe、
    `(TextElement.Foreground).(SolidColorBrush.Color)` 這類路徑取出真正改的屬性、WinUI 的 `<VisualState.Setters>`），
    1.6／1.8 的守門就全部原封不動適用。⚠ 還要把**非顏色的動畫**算進去：HandyControl 的 Calendar 用
    `DoubleAnimation` 把 `Opacity="0"` 的 Rectangle 打開，字真正的底是它 —— 只看顏色動畫就會拿祖先的底配白字。
    1.8 已經先補好這一塊的鏈記錄（被放棄的那一疊上的名字照樣收）
  - ⚠ **先試過一次「保守翻譯」：不可行。** 把 VSM 狀態翻成 State、碰到任何具名元素就記不可知，
    結果是語料多了 971 筆 unresolved、**0 組新配對**，覆蓋率從 29.8% 掉到 14.8%（Flow Launcher）。
    那些位置本來就在 `stateColors` 裡誠實列著，換成 971 筆 unresolved 只是同一件事報兩次、外加打爛
    `--min-coverage`。**計數器已經是誠實的信號，把它換成更吵的信號不是改進。**
- [x] VisualStateManager 狀態顏色的計數器 —— 1.5：`summary.stateColors` ＋ `stateColorSites` 逐筆點名
      （含 WinUI 的 `<VisualState.Setters>`）。照舊不評，只是不再靜默。
      實測 WinUI Gallery 44 處、wpfui 18、HandyControl 15，而自家四個專案是 0
- [x] **隱含樣式的觸發態** —— 1.6：隱含樣式改走 `GradeChain` 的逐狀態路徑，所以 hover／選取／勾選都評得到。
      代價如預期是 `element` 標籤（= baseline 的鍵）改變，所以 `Baseline.KeyVersion` 1 → 2，舊 baseline 被拒並叫人重寫。
      只有「真的會動到顏色」的隱含樣式走這條，否則每一筆 finding 的鍵都會無謂地變
- [x] **帶模板的控制項：模板裡哪一層畫底** —— 1.6：模板裡放宿主內容的位置，對著它在模板裡最近一層畫底的祖先評，
      `{TemplateBinding P}` 回宿主身上解；兩層都放內容就兩筆。HandyControl 的 Shield 從「配祖先白底的 1:1 假 fail」
      變成 7.46:1（#555）與 4.20:1（#1182C3），兩筆都是真的。解不出來的那一層記 `template-painted`，不退回祖先
- [x] **模板裡的兄弟背板** —— 1.6：模板裡找「畫在內容底下的那一層」照 WPF 的繪製順序走，所以兄弟也算得出來
      （同一個 `Grid` 格子、撐滿那一格、最靠上的贏）。疊層判定抽進 `Panels`，與標記樹的規則 17 共用一份 ——
      抄一份的話同一段 XAML 寫在畫面裡與寫在模板裡會得到不同的答案，而且沒有任何測試會紅。
      實測：17 個公開專案＋三個本機專案**逐筆完全一致**，所以它關掉的是一條編造路徑（宿主沒設底時配到祖先），
      不是一批新數字 —— MaterialDesign 的按鈕本來就靠「宿主自己的 Background」接住了
- [x] **觸發器重畫模板內部那一層** —— 1.6：記下重畫了哪一個元素、換成什麼，再看它是不是正好在那段內容底下那一疊上。
      不透明的重畫就是這個狀態的底；半透明的疊在它蓋住的那一層上（ScreenToGif 的 `AwareTabItem` 選取態把
      `ContentBorder` 畫成 `#90FFFFFF`，疊在面板的 Level3 上 → 7 筆 `8.23/20.29` 的 ok；1.5 拿被蓋掉的顏色報 1.46:1）。另有 13 筆（MahApps 3、MaterialDesign 10）從 `template-painted` 改成
      `bound-or-gradient` —— 那一層解開之後，真正卡住的是字色的 Binding，原因終於指對地方
- [x] **重畫的元素不在那一疊上** —— 1.8：動工前先量，而量測改掉了計畫。2370 筆 `template-painted` 的子類是
      84.8%「完全沒有模板分層可查」、13.4% 定義處、**只有 1.5%（36 筆）**是原先以為的「重畫在別的分支」。
      所以做的是前者：重畫的目標**一個放內容的位置都沾不到**時，那次重畫不在這段文字底下，底照舊算得出來。
      白老鼠是 wpfui 的 `CheckBox` —— 重畫的是 `BulletDecorator.Bullet` 裡那顆方塊，跟放文字的那一支是不同分支，
      1.6 卻把每一個勾選態都丟掉。實測 181 筆 unresolved 從 `template-painted` 換成真正的阻塞原因
      （wpfui 170、MahApps 10 卡在色盤裡沒有的鍵、MaterialDesign 1）。
      ⚠ 同一格但撐不滿那一格的兄弟照樣算在鏈上 —— 它在不在字底下正好是靜態分不出來的那件事。
      這條保守規則是量出來的：少了它，MaterialDesign 有 402 筆會被放行，而那些重畫的正是文字欄位
      底下那條有尺寸的底線。
      ⚠ 第一版因為漏抄一個欄位而從未生效，語料顯示「零變化」，差點被當成「野外不存在這種形狀」而放棄；
      是針對性測試抓到它是死碼。**語料說「沒變化」時，先確認那條路真的跑得到。**
- [x] **模板裡的 Image 兄弟** —— 1.8：改記 `over-sibling-content`，跟標記樹的規則 17(b) 同一個答案。
      之前是「看到內容元素就整層放棄、往外找」，等於拿圖後面那一層的顏色報比值。17 個公開專案與三個本機專案
      一個數字都沒變 —— 它關掉的是一條編造路徑。排在圖後面又畫了底的兄弟照舊蓋過那張圖
- [x] **宿主的字串內容由 ContentPresenter 產生的 TextBlock 畫** —— 1.10：外部回報逼出來的（ZeroCel 前端，
      1.8.3 說 757 組全過，真 App 裡紫色主要鈕是黑字疊紫底 2.66）。套得到那顆 TextBlock 的隱含樣式是
      「設在元素上」的值，贏過從宿主繼承來的，所以 Style 上宣告的 `Foreground="White"` 畫不上去。
      關鍵是分出 **`Application` 作用域**（模板產生的元素查隱含樣式時跳過視覺樹，`Window.Resources` 的同一份
      樣式沒有作用），作用域靠追 App.xaml 的 `MergedDictionaries` 算。
      實測：17 個公開專案**一列都沒變**（函式庫沒有 App.xaml），維護者自己的 Kindling **14 筆從 ok 變成
      13 fail + 1 warn**，逐筆都是真的 —— `DarkBtn` 按下態同時設 `Background=PurpleD` 與 `Foreground="White"`，
      而那個白色不會到位，淺色主題是 `#1B1B1B` 疊 `#534AB7` = 2.49。同一個檔的 `AccentBtn` 用
      `<Style.Resources>` 正確修好了，照舊 ok（看不懂正確的修法就是在逼人寫多餘的程式碼）。
- [x] **字色純粹靠繼承的文字元素不再靜默消失** —— 1.10：記 `inherited-foreground`。
      同一份回報的第二種（ToggleButton 模板裡的 TextBlock 自帶一個只設 `Text` 的 Style，1.21:1），
      1.9 之前 0 配對、0 unresolved、覆蓋率照樣 100%。實測 +1436 筆、0 筆 finding 變動。
- [x] **畫字串內容的隱含樣式有觸發器 ⇒ 不評分** —— 1.11：同一個回報人用 1.10 複驗之後的第一件。
      照 1.10 的建議修好（`ContentPresenter` 的隱含樣式用 `DataTrigger` 把字色翻成白色，畫出來 6.93）之後，
      工具只讀觸發**前**那個 Setter，照舊報同樣 8 筆 fail —— 改記 `content-style-trigger`。
      只在 `IsEnabled=False` 觸發的不算（WCAG 1.4.3 豁免，基礎狀態的字色照舊是正常狀態的答案）。
      **看不懂正確的修法就是在逼人寫多餘的程式碼**，這次是同一條規則的另一邊。
- [x] **內容是 markup extension ⇒ 不評分** —— 1.11：第二件，而且是「漏判」那一類。
      1.10 只收字面字串，其餘維持 1.9 的答案 —— 而那個答案是宿主宣告的字色，正是畫不上去的那一個。
      多語言專案每一句話都是 `{x:Static res:Strings.…}`，所以同一種病 1.10 只抓到少數寫死在標記裡的。
      改記 `bound-content`（點名那個內容的寫法）。猜「八成是字串」會往另一邊錯（蓋掉元素真的宣告的字色）。
      界線兩條：隱含樣式的字色跟宿主一樣時沒有不確定、宿主自己宣告 `ContentTemplate` 時整個不適用。
- [x] **`<Style.Resources>` 與模板裡的 brush 不算色票** —— 1.11：第三件。區域資源被收成主題色票，
      讓一個只放樣式的字典變成「色盤來源」（檔數與鍵數虛胖，而那正是挑色盤的判準），值不是色票時還印
      「!! 1 key(s) dropped —— 色票讀壞了」。實測只有 NETworkManager 動（`files` 163 → 168，五個
      `Resources/Styles/*Styles.xaml` 各有一個 `MahApps.Brushes.*` 在 `<Style.Resources>` 裡），色盤與每一筆
      finding 都沒變。⚠ 畫面檔自己的區域 brush 刻意不遮 —— 詳見下面那條。
- **把透明色票當透明，不要當「讀不懂」**：`<SolidColorBrush x:Key="Ghost" Color="Transparent"/>` 現在被剔除、
  列在「讀不懂」的名單上（用到它的地方記 `unknown-palette-key`），而工具其實讀得懂 —— 它就是透明，
  應該穿透到祖先。改了會多出配對、覆蓋率上升、可能多出 finding，所以是獨立的一次稽核語意改進（minor ＋ Upgrading），
  不夾帶在別的修正裡。順帶把那句警告的措辭分成兩種（真的讀不懂／刻意不當色票）。
- **畫面檔的區域 brush 收斂到子樹**：現在是「整個檔」（1.0 就在的近似）。量過了（1.11）：MaterialDesign 的
  `DataGrids.xaml` 有三顆按鈕各自在 `<Button.Resources>` 裡把 `MaterialDesign.Brush.Primary` 換成
  Orange／DodgerBlue／Red，工具現在讓**先出現的那一個**贏、三顆都用它評 —— 三筆裡有兩筆是編的。
  要做對得按行號區間收範圍（同隱含樣式的 `ScopeOf`），而且色盤那一層得從「每檔一份」變成「每個範圍一份」。
- **`{x:Static res:Strings.X}` 解析成字串**：要讀專案的 `*.Designer.cs`／`.resx` 確認那個成員是 `string`。
  讀 `*.cs` 做色盤以外的事是**擴大稽核範圍**（第 9 面，同 `converters.constantBrushes`）—— 預設開要等 2.0，
  所以這條只能是 opt-in 的設定。先確認有人要：`bound-content` 已經誠實點名，解析它換到的是「少一筆 unresolved」。
- **繼承模型（把 `inherited-foreground` 從「點名」變成「評得出來」）**：往上找到第一個宣告字色的祖先就評它；
  整條都沒有才落到控制項的系統預設。⚠ **系統預設那一格是唯一的難處**，而且會編造：`ControlText` 只在
  「沒有函式庫提供那個控制項的預設樣式」時才對，而 `ThemeLibraries.DetectInProject` 認不出 MahApps
  （它的主題檔是建置時產生的，刻意沒有表）—— 填黑色會讓 NETworkManager 那一類專案冒出一批假 fail。
  範圍也要先量：語料有 7351 處「連樣式都沒有、也沒寫字色」的文字元素，接上繼承之後那些全部變成新配對。
  **前置條件：先想清楚「分不出控制項預設色」的出口**（config 宣告？只在沒偵測到任何函式庫時才填？），
  不然這一步會把 1.10 誠實的點名換成一批編出來的數字。
- **帶 Opacity 的兄弟（模板裡）**：量過了（1.8），17 個公開專案只有 **2 處**，都在 MaterialDesign，
  而且兩處現在**已經是誠實的** `no-ancestor-background`、不是編造。要算出來得在模板那條路上再寫一份
  Opacity 疊合（標記樹那一份在 `Auditor.SiblingBackdrop`，拿不到色盤），為 2 處維護兩份會漂開的判定不划算
- **重畫的值是漸層或 Binding、半透明的重畫層而它蓋住的那一層本身讀不懂**：維持誠實的 `template-painted`
  （ScreenToGif 另有 7 筆 `RadialGradientBrush`，現在誠實點名那支 brush）
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

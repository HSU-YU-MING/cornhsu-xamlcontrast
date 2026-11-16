using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>
/// 具名 Style 索引與解析（港自原型 4929b69，修「元素自身底色來自具名 Style」盲區）。
///
/// 範圍界定（與原型一致）：
///   - 只解析「具名引用」與「行內 Style」；隱含樣式不套用（雜訊量級不同，檔頭明記）。
///   - BasedOn 鏈逐層合併；衍生 Style 自帶 Template 時不繼承基底的模板觸發器
///     （WPF 換掉整個模板，基底模板的觸發器就不存在了）。
///   - 模板觸發器只收「無 TargetName 的 Setter」（套回模板宿主本身）；
///     有 TargetName 的要解析模板內部樹，不做 —— QuillNest 四筆假警報的成因，已記進盲區表。
///   - 同條件的觸發器跨節點合併（style trigger 蓋 template trigger、衍生蓋基底），
///     否則「style trigger 設字色＋template trigger 設底色」的同一個狀態
///     會被拆成兩個各缺一半的幻影組合。
/// </summary>
internal sealed partial class StyleIndex
{
    internal sealed class State
    {
        public bool Disabled;
        public bool FromTemplate;
        /// <summary>觸發器不屬於這個 Style 自己（或它自己的模板）：在巢狀的 Style（ItemContainerStyle 之類）或巢狀的模板
        /// （ComboBox 模板裡 ToggleButton 的模板）裡 —— 那是別的元素的狀態。只有 nonText.uiComponents 看這一欄（1.4）；
        /// 文字配對照舊全收（改它會動到既有的計數）。</summary>
        public bool Nested;
        public Dictionary<string, string> Set = new();
        public Dictionary<string, int> SetSrc = new(); // 屬性 → 來源 Style Id
        /// <summary>來自「TargetName=模板根」Setter 的屬性 —— 直接設在根元素上，
        /// WPF 屬性優先序會蓋過經 TemplateBinding 進來的宿主本地值</summary>
        public HashSet<string> RootTargeted = new();
        /// <summary>
        /// 這個狀態把模板內部哪個元素的底重畫成什麼（1.6）：<c>TargetName</c> → 新的底。
        /// 1.5 之前只知道有沒有重畫，而且照舊拿基礎狀態的底評分（編出來的）；現在記下是誰、
        /// 換成什麼，使用處就能看那個元素是不是正好畫在這個內容位置底下（<see cref="TemplateLayer.Name"/>／
        /// <see cref="TemplateLayer.Above"/>）—— 對得上就算得出來，對不上才記 template-painted。
        /// ⚠ 只收 Background／Fill。<c>OpacityMask</c> 不是一個底色，收了就得猜它把下面那層變成什麼。
        /// </summary>
        public Dictionary<string, string> LayerPaints = new();
        public Dictionary<string, int> LayerPaintSrc = new(); // TargetName → 來源 Style Id
        /// <summary>
        /// 這個狀態把「看不到的那一層」重畫了（1.6）：Setter 的 <c>TargetName</c> 指向模板**內部**的元素
        /// 而且畫的是顏色，或是整個換掉 <c>Template</c>。兩種都讓這個狀態下字底下那一層變成靜態不可知。
        ///
        /// ⚠ 這一格是語料逼出來的。指向內部元素的 TargetName 一直是明文盲區（規則 13 只收模板根），
        ///   而舊版的做法是**把那個 Setter 丟掉、照舊用基礎狀態的底評分** —— 等於拿一個已經被蓋掉的底
        ///   去配觸發態的字色，報出執行期不存在的比值。1.5 之前只有具名 Style 走逐狀態路徑，所以零星；
        ///   1.6 隱含樣式也走這條之後，ScreenToGif 一口氣冒出 28 筆假 fail
        ///   （<c>AwareTabItem</c> 的選取態把 <c>ContentBorder</c> 重畫成 <c>#90FFFFFF</c>，
        ///   黑字其實在那層淺底上，工具卻拿深色主題的 Level3 去配，報 1.46:1）。
        ///   另外 72 筆是 <c>ExtendedMenuItem</c> 的 <c>Role</c> 觸發器整個換掉 Template。
        ///   現在這種狀態只有在重畫的元素對得上字底下那一疊時才評（<see cref="LayerPaints"/>），
        ///   對不上的一律記 unresolved（template-painted），不評。
        /// </summary>
        public bool UnknownLayer;
        /// <summary>這個狀態重畫的模板內部元素的名字（1.7）：<see cref="LayerPaints"/> 的鍵再加上
        /// <c>OpacityMask</c> 那種「畫得出來但讀不懂」的目標 —— 問的是「它在不在字底下那一疊上」，
        /// 不是「它畫成什麼顏色」，所以讀不懂的也要收。</summary>
        public HashSet<string> LayerTouched = new(StringComparer.Ordinal);
        /// <summary>這個狀態的不可知**不是**來自某個指得出來的 TargetName（1.7）：整個換掉 <c>Template</c>、
        /// 或視覺是另一份模板畫的。這兩種沒有可查的目標，所以永遠不能用「重畫的不在這一疊上」放行。</summary>
        public bool UnknownBeyondLayers;
        public string? Cond;
    }

    /// <summary>模板裡一個放內容的位置，與它底下最近一層畫底的值（1.6，見 <see cref="Record.TemplateLayers"/>）。
    /// <paramref name="Bg"/> 是 null ＝模板裡沒有任何一層畫底（實質上沒有底，穿透到祖先）。
    /// <paramref name="Primary"/> ＝那個位置是 ContentPresenter／ContentControl：元素在標記裡的子元素會落在這裡，
    /// 所以子元素的底要用這一層。<paramref name="Root"/> ＝畫底的是模板根（或沒有任何一層畫底）——
    /// 「TargetName=模板根」的觸發 Setter 換底時換掉的就是這一層（規則 13）。
    /// <paramref name="Name"/>（1.6）＝畫這一層的那個元素的名字，<paramref name="Above"/> ＝在它與內容之間、
    /// 自己沒畫底的那些元素（由內而外）。兩者都只收「真的有觸發器重畫它」的名字 ——
    /// 沒有任何觸發器碰模板內部時（絕大多數的 Style）這兩格一律是 null，那條路就是單純的分層。
    /// 觸發器重畫的元素對得上這兩格時，那個狀態的底就算得出來（<c>Auditor.TemplateChoices</c>）。</summary>
    internal sealed record TemplateLayer(string? Bg, bool Primary, bool Root,
        string? Name = null, IReadOnlyList<string>? Above = null);

    /// <summary>把宿主的內容整個接過去的自訂元素（<c>Content="{TemplateBinding Content}"</c>）——
    /// 不是 ContentPresenter 也一樣在畫宿主的內容，標記裡的子元素就落在這裡。
    /// ⚠ 少了這一條，MaterialDesign 的按鈕模板（內容在 <c>wpf:Ripple</c> 裡）看不出內容放在哪一層上。
    /// ⚠ 要**逐字比**是不是綁到 Content／Text 自己：放寬成「任何 {TemplateBinding …}」的話，
    ///   HandyControl 的 Shield 模板裡 <c>Text="{TemplateBinding Subject}"</c> 的那顆 TextBlock
    ///   會被當成放內容的主要位置，標記裡的子元素就被配到它那一層（#555）而不是 ContentPresenter 那一層。</summary>
    private static bool IsHostContentHolder(XElement el)
        => el.Attribute("Content")?.Value.Trim() == "{TemplateBinding Content}"
           || el.Attribute("Text")?.Value.Trim() == "{TemplateBinding Text}";

    internal sealed class Record
    {
        public required int Id;
        public required string File;
        /// <summary>定義它的檔只屬於一個主題（Dark.xaml 之類）時是那個主題；兩個主題共用為 null</summary>
        public ThemeSide? Side;
        public string? Key;
        public string? BasedOn;
        public Dictionary<string, string> Setters = new();
        public List<State> States = new();
        public bool HasTemplate;
        /// <summary>模板根元素自帶的 Background（CellDeleteBtn 形狀：Style 層沒有
        /// Background setter，真正的底是模板根 Border 的色票 —— 少了它會誤配祖先背景）</summary>
        public string? TemplateRootBg;
        /// <summary>模板根元素自帶的框線（1.4，nonText.uiComponents）：寫死在模板裡的 BorderBrush／BorderThickness。
        /// <c>{TemplateBinding …}</c> 不算 —— 那就是宿主自己的值，照 Style 的 Setter 走。</summary>
        public string? TemplateRootBorder;
        public string? TemplateRootBorderThickness;
        /// <summary>這個 Style 自己帶 ControlTemplate（<see cref="HasTemplate"/> 連巢狀 Style 的模板也算）——
        /// 只有自己的模板才決定 <see cref="TemplateLayers"/>。</summary>
        public bool OwnTemplate;
        /// <summary>
        /// 模板裡「宿主的文字畫在哪一層上」（1.6）。
        ///
        /// <see cref="TemplateRootBg"/> 只看模板根，根沒設底時字就配到祖先的底 —— 而帶模板的控制項幾乎都把
        /// 內容放在模板**內部**某一層上面。⚠ 實測（HandyControl 的 <c>&lt;hc:Shield Color="#1182c3"/&gt;</c>）：
        /// 模板根是沒設底的 Grid，白字其實畫在兩個 Border 上（<c>#555</c> 與 <c>{TemplateBinding Color}</c>），
        /// 配祖先的白底報出三筆 1:1 的假 fail。1.5 的做法是「帶模板、底又不是它自己給的就不評」（而且只對隱含樣式），
        /// 1.6 改成把那一層真的找出來。
        ///
        /// 每個「放內容的位置」（ContentPresenter，或字色由宿主決定的文字／圖形元素）配一筆，值是它**在模板裡**
        /// 最近一層畫底（Background／Fill）的祖先的值；同值的合併成一筆。
        /// 巢狀模板裡的位置不收 —— 那是別的控制項的內容。
        /// </summary>
        public List<TemplateLayer> TemplateLayers = new();
        /// <summary>模板裡「在某個放內容的位置底下那一疊上」的每個具名元素（1.7）。
        /// 觸發器重畫了模板內部某一層時，拿這一份問「它在不在字底下」—— 不在的話那次重畫跟這段文字無關，
        /// 底照舊算得出來。⚠ 收的是**所有**內容位置的鏈，含沒進 <see cref="TemplateLayers"/> 的那些
        /// （那些位置的鏈上沒有畫底的祖先、也沒有被重畫的名字，但它底下仍然是內容的位置）——
        /// 收多了只會讓更多狀態維持「不知道」，收少了才會編造。</summary>
        public HashSet<string> ContentChainNames = new(StringComparer.Ordinal);
        /// <summary>這個 Style 當成 FocusVisualStyle 時畫的焦點框（1.4）：模板裡第一個帶 Stroke（Rectangle 之類）或 BorderBrush 的元素。</summary>
        public string? FocusStroke;
        /// <summary>那個焦點框的 Margin 有負值：畫在控制項外面（旁邊是周圍的底），不是疊在控制項的底上。</summary>
        public bool FocusOutside;
        /// <summary>隱含樣式的作用範圍（1.5）：放它的那層 Resources 屬於哪個元素的子樹，用行號區間表示
        /// （<c>FromLine</c>～<c>ToLine</c>；0～int.MaxValue = 整個檔）。
        ///
        /// ⚠ 這一格是實測踩出來的。只用「同一個檔」當範圍的話，QuillNest 的
        /// <c>ContentPresenter.Resources</c> 裡（標籤膠囊的模板深處、底是 <c>{Binding ColorHex}</c>）
        /// 那個 <c>&lt;Style TargetType="TextBlock"&gt;&lt;Setter Foreground="White"&gt;</c>
        /// 會套到同一個檔案裡**毫不相干**的工具列文字上 —— 報出三筆「白字配白卡片」的假 fail。
        /// 執行期它只管那個 ContentPresenter 底下的東西。
        ///
        /// 兩份 XDocument（索引自己載一次、稽核再載一次）之間沒辦法比祖先關係，所以用行號區間：
        /// 範圍元素的起始行 ～ 它子樹裡最大的行號（≈ 它的結束標籤）。偏差只會發生在最後一行上，
        /// 而且方向是「漏掉」而不是「多套」。</summary>
        public int FromLine;
        public int ToLine = int.MaxValue;
        /// <summary>
        /// 這個 Style 的 <c>&lt;Style.Resources&gt;</c> 裡宣告的隱含樣式（型別名 → 那個 Style，1.5）。
        ///
        /// 執行期它們套用在「用了這個 Style 的元素」的內容上 —— 這是 WPF 修「全域隱含樣式蓋掉按鈕字色」
        /// 的標準手法。⚠ 作用範圍跟別的隱含樣式不同（不是「某個子樹的行號區間」，是「用了這個 Style 的地方」），
        /// 所以得由鏈帶著走，見 <see cref="Merged.ScopedImplicit"/>。
        /// 實例：Kindling 的 AccentBtn 這樣把標籤的字色蓋回白色，而 1.5 開發中的工具看不到，
        /// 於是那個修正在報告上「沒發生」—— 得在元素上再明寫一次才檢得到。
        /// </summary>
        public Dictionary<string, Record> ScopedImplicit = new(StringComparer.Ordinal);
    }

    internal sealed class Merged
    {
        /// <summary>各屬性最後生效的 Setter（帶來源 Style Id）</summary>
        public Dictionary<string, (string V, int Src)> Props = new();
        public List<State> States = new();
        /// <summary>生效模板的根元素背景（衍生自帶模板時整個換掉，含根背景）</summary>
        public string? TemplateRootBg;
        /// <summary>生效模板的根元素框線（同 <see cref="TemplateRootBg"/>，衍生自帶模板時整個換掉）</summary>
        public string? TemplateRootBorder;
        /// <summary>鏈上各層的 <c>&lt;Style.Resources&gt;</c> 隱含樣式（衍生蓋基底）—— 見 <see cref="Record.ScopedImplicit"/>。</summary>
        public Dictionary<string, Record> ScopedImplicit = new(StringComparer.Ordinal);
        /// <summary>鏈上有任何一層帶 ControlTemplate —— 這個控制項的視覺是模板畫的（1.5 用它判斷
        /// 「字到底畫在什麼上面」：模板裡哪一層畫底靜默看不到時，拿祖先的底去配就是編造）。</summary>
        public bool HasTemplate;
        /// <summary>生效模板裡放內容的那幾層（1.6，同 <see cref="TemplateRootBg"/>：衍生自帶模板時整個換掉）。</summary>
        public List<TemplateLayer> TemplateLayers = new();
        /// <summary>生效模板裡「在內容底下那一疊上」的具名元素（1.7）—— 見 <see cref="Record.ContentChainNames"/>。</summary>
        public HashSet<string> ContentChainNames = new(StringComparer.Ordinal);
        public string? TemplateRootBorderThickness;
        public required string Label;
        /// <summary>鏈上有任何一層定義在只屬於一個主題的字典檔裡 —— 另一個主題下解析到的是別的 Style（或沒有）</summary>
        public bool SideSpecific;
        /// <summary>鏈上第一個找不到的 BasedOn 鍵（在這個主題、或不分主題時在全域）；整條鏈都找得到是 null。
        /// 找不到的基底照舊略過（外部函式庫的 MahApps.Styles.Button 之類在哪個主題都找不到）；
        /// 只有「這個主題找不到、另一個主題找得到」時，元素端那個主題記 unknown-style-key、不評（Auditor.Walk）。</summary>
        public string? MissingBase;
    }

    [GeneratedRegex(@"^\{(?:\w+:)?(Dynamic|Static|Theme)Resource\s+(?<k>[\w\.]+)\}$")] // ThemeResource：WinUI／UWP（1.4）；前綴：wpfui 的 {ui:ThemeResource K}（1.7）
    private static partial Regex ResourceRef();

    [GeneratedRegex(@"(^|\.)IsEnabled$")]
    private static partial Regex IsEnabledProp();

    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    private readonly Dictionary<string, Dictionary<string, Record>> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Record> _global = new();
    /// <summary>隱含樣式（只有 TargetType、無 x:Key）依型別名索引。
    /// ⚠ 一個檔、同一個型別可以有好幾個（巢狀的 Resources 各一個），所以是 List：
    /// 查找時依行號挑「作用範圍涵蓋這個元素、而且範圍最窄」的那一個（WPF 從最近的資源範圍往外找）。
    /// 見 <see cref="Record.FromLine"/>。</summary>
    private readonly Dictionary<string, Dictionary<string, List<Record>>> _implicitByFile = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>全域（字典最外層／App.Resources）的隱含樣式候選。
    /// ⚠ 一個型別可能有好幾個互相競爭的全域隱含樣式（MaterialDesign 的 repo 裡 Button 就有好幾個：
    ///   兩份 Defaults、ToolBar、Snackbar…）。執行期是「合併字典的順序」決定誰贏，靜態不可知 ——
    ///   所以候選內容不一致時一律不套用（<see cref="Agree"/>），不是挑第一個。</summary>
    private readonly Dictionary<string, List<Record>> _implicitGlobal = new(StringComparer.Ordinal);
    // 依主題的全域查找表：只收兩個主題共用的檔與那個主題自己的檔（先出現的贏，與 _global 同一個順序）
    private readonly Dictionary<ThemeSide, Dictionary<string, Record>> _globalBySide = new()
    {
        [ThemeSide.Dark] = new(),
        [ThemeSide.Light] = new(),
    };
    private readonly Dictionary<ThemeSide, Dictionary<string, List<Record>>> _implicitBySide = new()
    {
        [ThemeSide.Dark] = new(StringComparer.Ordinal),
        [ThemeSide.Light] = new(StringComparer.Ordinal),
    };
    private Func<string, ThemeSide?>? _sideOf;
    /// <summary>模板裡的兄弟背板要跟標記樹的規則 17 同一套疊層判定（<see cref="Panels"/>），
    /// 包含 config 宣告的自訂容器版面 —— 同一段 XAML 寫在畫面裡與寫在模板裡不該得到不同的答案。</summary>
    private ToolConfig _cfg = new();
    private int _seq;

    /// <summary>有沒有「只屬於一個主題」的字典檔（色盤來自分開的深／淺 XAML 字典時才有）。</summary>
    public bool HasThemeSides { get; private set; }

    private readonly Dictionary<string, string> _fontFamilies = new(StringComparer.Ordinal);

    /// <summary>專案裡的 FontFamily 資源（&lt;FontFamily x:Key="K"&gt;值&lt;/FontFamily&gt;），鍵 → 值；同名先出現的贏（與 Style 的全域表同一個順序）。
    /// 只給圖示字型的判斷用（<see cref="IconFonts.IsIconFont"/>）：FontFamily="{StaticResource SymbolFont}" 這種寫法。</summary>
    public IReadOnlyDictionary<string, string> FontFamilies => _fontFamilies;

    // 有元素以 Style="{…Resource K}" 引用的 Style 鍵（含經 BasedOn 間接用到的基底）
    private readonly HashSet<string> _usedKeys = new(StringComparer.Ordinal);

    // 專案裡出現過的元素型別名（屬性元素 <X.Y> 不算）
    private readonly HashSet<string> _elementTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// 這個型別的隱含樣式，在這個檔的這一行適用的是哪一筆：
    /// 同檔、作用範圍涵蓋這一行的之中取**最窄**的（WPF 從最近的資源範圍往外找），
    /// 都不涵蓋就退回全域（字典／App.xaml 的）。
    /// ⚠ 舊版「同檔就算」沒有範圍概念 —— 模板深處 <c>ContentPresenter.Resources</c> 裡的隱含樣式
    ///   會套到同一個檔案裡毫不相干的元素上（見 <see cref="Record.FromLine"/> 的實例）。
    /// </summary>
    private Record? ImplicitRecord(string typeName, string file, int line, ThemeSide? side)
    {
        if (_implicitByFile.TryGetValue(file, out var map) && map.TryGetValue(typeName, out var recs))
        {
            var inScope = recs.Where(r => line >= r.FromLine && line <= r.ToLine).ToList();
            if (inScope.Count > 0)
            {
                // 最近的資源範圍優先；同樣窄的有好幾個（同一層宣告兩個同型別的隱含樣式）就要內容一致
                var narrowest = inScope.Min(r => (long)r.ToLine - r.FromLine);
                var best = Agree(inScope.Where(r => (long)r.ToLine - r.FromLine == narrowest).ToList());
                if (best is not null) return best;
                // 這一層說不清楚 → 不往外退（外層的在執行期也贏不了這一層）
                return null;
            }
        }
        var global = side is { } s ? _implicitBySide[s].GetValueOrDefault(typeName) : _implicitGlobal.GetValueOrDefault(typeName);
        return global is null ? null : Agree(global);
    }

    /// <summary>專案裡有這個型別的元素嗎 —— 隱含樣式會不會在某個使用處被評到（1.5）。
    /// 一個都沒有的話執行期根本不會套用那個隱含樣式，它的配對不存在，定義處要照舊把看不懂的部分記成 unresolved。
    /// ⚠ 這是專案層級、不分作用域的近似：隱含樣式寫在某個畫面的 Resources 裡、而那個型別只出現在別的畫面時，
    ///   執行期一樣套不到 —— 兩種情況的結論都是「那一組不存在」，所以近似的方向是安全的。</summary>
    public bool AppliesToSomeElement(string typeName) => _elementTypes.Contains(typeName);

    /// <summary>
    /// 這個具名 Style 會不會在某個使用處被評到：有元素以 Style= 引用它，或它是這種 Style（或行內 Style）的 BasedOn 基底。
    /// 會的話，Style 定義處評不了的組合（透明底）交給使用處，不在定義處另外計 —— 否則同一組算兩次。
    /// 只透過 ItemContainerStyle 之類套用、或根本沒用到的，使用處評不到。
    /// </summary>
    public bool IsUsedByAnElement(string key) => _usedKeys.Contains(key);

    [GeneratedRegex(@"(?<t>\w+)\s*\}?\s*$")]
    private static partial Regex TargetTypeName();

    /// <summary>IsEnabled=False 觸發 = 停用態。WCAG 1.4.3 明文豁免停用中的控制項。
    /// Multi(Data)Trigger 的條件是 AND：任一條是 IsEnabled=False，整個狀態就只在停用時成立。
    ///
    /// ⚠ 值的比對**不分大小寫**（1.6）：XAML 的布林轉換器收 <c>"false"</c> 也收 <c>"False"</c>，
    ///   而舊版是逐字比 <c>"False"</c> —— 寫小寫的停用態（ScreenToGif 的隱含 ScrollViewer 樣式就是
    ///   <c>Value="false"</c>）被當成普通狀態評分，報出「停用態的灰字配底」這種必然假的 fail。
    ///   1.5 之前隱含樣式不走逐狀態路徑，所以這個 bug 只在具名 Style 上零星發作、沒被抓到。</summary>
    internal static bool IsDisabledTrigger(XElement t)
    {
        static bool IsFalse(string? v) => string.Equals(v, "False", StringComparison.OrdinalIgnoreCase);
        if (t.Name.LocalName == "Trigger")
        {
            var p = t.Attribute("Property")?.Value;
            return p is not null && IsFalse(t.Attribute("Value")?.Value) && IsEnabledProp().IsMatch(p);
        }
        if (t.Name.LocalName == "DataTrigger")
        {
            var b = t.Attribute("Binding")?.Value;
            return b is not null && IsFalse(t.Attribute("Value")?.Value) && IsOwnIsEnabledBinding(b);
        }
        if (t.Name.LocalName is "MultiTrigger" or "MultiDataTrigger")
            return ConditionsOf(t).Any(c =>
                IsFalse(c.Attribute("Value")?.Value) &&
                (c.Attribute("Property")?.Value is { } p && IsEnabledProp().IsMatch(p) ||
                 c.Attribute("Binding")?.Value is { } b && IsOwnIsEnabledBinding(b)));
        return false;
    }

    /// <summary>
    /// DataTrigger 的 Binding 是不是「這個控制項自己（或模板宿主、祖先）的 IsEnabled」。
    ///
    /// ⚠ 舊版是子字串比對 b.Contains("IsEnabled")：{Binding IsEnabledByLicense}、
    ///   {Binding ViewModel.CanSave, Converter={StaticResource IsEnabledConverter}} 都被當成停用態豁免 ——
    ///   真實的 fail 歸 0、exit 0，計數器標示的豁免原因是錯的。WCAG 1.4.3 豁免的是「停用中的控制項」，
    ///   放行憑據要是條文本身，所以：
    ///   - Path 恰為 IsEnabled、(IsEnabled) 或 (UIElement.IsEnabled)（帶點的路徑是別的物件的屬性）
    ///   - 來源是元素自己、模板宿主或祖先（RelativeSource Self／TemplatedParent／FindAncestor —— 祖先停用時
    ///     WPF 會把子元素一併停用）。沒有 RelativeSource 就是 DataContext 上的 view model 旗標，
    ///     不保證控制項真的停用；ElementName／Source 指向別的物件 —— 都不猜
    ///   - 沒有 Converter（可能是反相的，Value="False" 不再代表停用）
    /// </summary>
    internal static bool IsOwnIsEnabledBinding(string binding)
    {
        var b = binding.Trim();
        if (!b.StartsWith("{Binding", StringComparison.Ordinal) || !b.EndsWith('}')) return false;
        string? path = null, relativeSource = null;
        var args = SplitTopLevel(b["{Binding".Length..^1]);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i].Trim();
            if (a.Length == 0) continue;
            var eq = a.IndexOf('=');
            var brace = a.IndexOf('{');
            if (eq < 0 || (brace >= 0 && brace < eq))
            {
                if (i == 0) path = a; // 第一個位置參數就是 Path
                continue;
            }
            var name = a[..eq].Trim();
            var value = a[(eq + 1)..].Trim();
            switch (name)
            {
                case "Path": path = value; break;
                case "RelativeSource": relativeSource = value; break;
                case "Converter" or "ElementName" or "Source": return false;
            }
        }
        if (path is null || relativeSource is null) return false;
        // Path 要恰好是 IsEnabled 本身（含附加屬性寫法）。⚠ 只看最後一段的話，DataContext.IsEnabled、
        // Owner.IsEnabled 這種「別的物件的 IsEnabled」也會被豁免 —— 那是 view model 的旗標，不是控制項停用
        return path.Trim() is "IsEnabled" or "(IsEnabled)" or "(UIElement.IsEnabled)" &&
               (relativeSource.Contains("Self") || relativeSource.Contains("TemplatedParent") ||
                relativeSource.Contains("FindAncestor") || relativeSource.Contains("AncestorType"));
    }

    /// <summary>markup extension 的參數清單：只在最外層（不在 {…} 裡）的逗號切開。</summary>
    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') depth--;
            else if (s[i] == ',' && depth == 0) { parts.Add(s[start..i]); start = i + 1; }
        }
        parts.Add(s[start..]);
        return parts;
    }

    /// <summary>這個 Setter 的屬性會不會重畫「字底下那一層」（<c>Border.Background</c> 這種帶型別前綴的寫法也算）。
    /// 只給 <see cref="State.UnknownLayer"/> 用：指向模板內部元素時那一層就看不到了。
    /// ⚠ 刻意**不收 BorderBrush／Stroke**：框線畫在邊上，不是字底下那一面 ——
    ///   收了的話 MahApps 的 TextBox 只要進焦點態（基底模板換 BorderElement 的框色）就整組不評，
    ///   而那一組的底其實算得出來（衍生樣式自己設了宿主的 Background）。</summary>
    /// <summary>重畫的是一個「底色」—— 收得進 <see cref="State.LayerPaints"/> 當另一個狀態的底。
    /// <c>OpacityMask</c> 不算（它不是顏色，是把下面那層挖掉多少）。</summary>
    private static bool IsBackdropProp(string prop)
    {
        var dot = prop.LastIndexOf('.');
        return (dot >= 0 ? prop[(dot + 1)..] : prop) is "Background" or "Fill";
    }

    private static bool PaintsLayer(string prop)
    {
        var dot = prop.LastIndexOf('.');
        return (dot >= 0 ? prop[(dot + 1)..] : prop) is "Background" or "Fill" or "OpacityMask";
    }

    private static IEnumerable<XElement> ConditionsOf(XElement t)
        => t.Descendants().Where(e => e.Name.LocalName == "Condition");

    /// <summary>觸發器種類（含 Multi 組合條件）—— 觸發器收集處共用這一份清單，
    /// 加新種類只改這裡。MultiTrigger 曾是黑洞：跳過清單認得它、收集處不認得，
    /// 組合條件狀態（hover＋選取之類）整批沒被檢查，也沒有任何計數提示。</summary>
    internal static bool IsTriggerElement(XElement e)
        => e.Name.LocalName is "Trigger" or "DataTrigger" or "MultiTrigger" or "MultiDataTrigger";

    /// <summary>條件簽名 —— 同條件的觸發器狀態要跨節點合併。
    /// Multi(Data)Trigger 逐條列出以 &amp; 串接（條件是 AND）。</summary>
    private static string? CondSignature(XElement t)
    {
        if (t.Name.LocalName == "Trigger")
            return $"P:{t.Attribute("Property")?.Value}={t.Attribute("Value")?.Value}";
        if (t.Name.LocalName == "DataTrigger")
            return t.Attribute("Binding") is { } b && t.Attribute("Value") is { } v
                ? $"B:{b.Value}={v.Value}" : null;
        var parts = ConditionsOf(t)
            .Select(c => c.Attribute("Property") is { } p
                ? $"P:{p.Value}={c.Attribute("Value")?.Value}"
                : $"B:{c.Attribute("Binding")?.Value}={c.Attribute("Value")?.Value}")
            .ToList();
        return parts.Count > 0 ? string.Join("&", parts) : null;
    }

    /// <summary>模板裡會渲染宿主 Foreground 的元素。⚠ 不能只看 ContentPresenter：
    /// Foreground 是繼承屬性，模板裡一顆沒指定 Foreground 的裸 TextBlock 一樣會繼承到。
    /// 規則 8（定義處的死 setter 過濾，<c>Auditor.WalkStyles</c>）與模板分層（<see cref="ContentLayers"/>）
    /// 共用這一份 —— 兩邊對「誰會畫宿主的字」要有同一個答案。</summary>
    internal static readonly HashSet<string> FgConsumers = new(StringComparer.Ordinal)
    {
        "ContentPresenter", "ContentControl", "TextBlock", "AccessText", "Label",
    };

    [GeneratedRegex(@"TemplateBinding\s+Foreground")]
    internal static partial Regex TemplateBindingFg();

    /// <summary>
    /// 模板裡每個「放內容的位置」底下最近一層畫底的值（1.6，見 <see cref="Record.TemplateLayers"/>）。
    ///
    /// 放內容的位置有四種：ContentPresenter／ContentControl、把宿主的內容整個接過去的自訂宿主
    /// （<c>Content="{TemplateBinding Content}"</c>，MaterialDesign 的 <c>wpf:Ripple</c> 就是）、
    /// 字色由宿主決定的文字元素（自己沒寫 Foreground，或寫的是 <c>{TemplateBinding Foreground}</c>），
    /// 以及把宿主字色畫成圖形的 Fill／Stroke（圖示按鈕的 Path）。自己帶字色的文字元素不收 ——
    /// 那不是宿主的配對，由 <c>Auditor.CountStyleTemplateText</c> 記成 in-style-template。
    ///
    /// ⚠ **只收在模板裡找得到「畫底的祖先」的位置**，找不到的不收 —— 那種形狀照舊走 1.5 的優先序，
    ///   所以這一格是純加法。這條界線是語料逼出來的：MaterialDesign 的按鈕模板把底畫在
    ///   <c>&lt;Border Background="{TemplateBinding Background}"/&gt;</c>，而它是內容（Ripple）的**兄弟**、不是祖先。
    ///   當成「模板裡沒有底 → 穿透到祖先」的話，宿主的 Background Setter 整個被跳過，1.4 逐筆驗過的兩筆真 fail
    ///   （工具列字 1.55、扁平鈕 2.63）連同另外 41 筆一起消失。模板裡的兄弟背板是另一塊盲區
    ///   （樹走訪的規則 17 只管標記裡的兄弟），不在這一版的範圍。
    /// </summary>
    private List<TemplateLayer> ContentLayers(XElement tmpl, IReadOnlySet<string> repainted, HashSet<string> chainNames)
    {
        var layers = new List<TemplateLayer>();
        var byBg = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var host in tmpl.Descendants())
        {
            var ln = host.Name.LocalName;
            if (ln.Contains('.')) continue; // 屬性元素
            // 巢狀模板裡的位置是別的控制項的內容（ComboBox 模板裡 ToggleButton 的模板、ItemTemplate 的 DataTemplate）
            if (host.Ancestors().TakeWhile(a => !ReferenceEquals(a, tmpl))
                    .Any(a => !a.Name.LocalName.Contains('.') &&
                              a.Name.LocalName.EndsWith("Template", StringComparison.Ordinal)))
                continue;
            var primary = ln is "ContentPresenter" or "ContentControl" || IsHostContentHolder(host);
            if (!primary && !IsHostTextHolder(host, ln) && !IsHostForegroundShape(host)) continue;
            // chainNames：這個位置底下那一疊上的每個具名元素（1.7）—— 連「這一格之後被跳過」的位置也要收
            var chain = BackdropChain(tmpl, host, chainNames);
            var painter = chain.Count > 0 && chain[^1].Bg is not null ? chain[^1] : default;
            var bg = painter.El is null ? null : painter.Bg;
            var root = painter.El?.Parent is { } pp && ReferenceEquals(pp, tmpl);
            // 名字只收「真的會被重畫」的（repainted）—— 其餘一律 null，沒有觸發器的 Style 不受這一格影響
            var name = painter.El is not null && NameOf(painter.El) is { } pn && repainted.Contains(pn) ? pn : null;
            var above = chain.Where(c => c.Bg is null)
                .Select(c => NameOf(c.El)).OfType<string>().Where(repainted.Contains).ToList();
            // 模板裡沒有一層畫底，又沒有任何可能被重畫的元素 —— 照舊走 1.5 的優先序（見方法註解）
            if (bg is null && name is null && above.Count == 0) continue;
            // 同一層被兩個內容位置共用時只記一次（鍵要含重畫目標：同一個底、不同的重畫鏈是兩層）
            var key = $"{bg}|{name}|{string.Join(",", above)}";
            if (byBg.TryGetValue(key, out var at))
            {
                if (primary) layers[at] = layers[at] with { Primary = true };
                continue;
            }
            byBg[key] = layers.Count;
            layers.Add(new TemplateLayer(bg, primary, root, name,
                above.Count > 0 ? above : null));
        }
        return layers;
    }

    /// <summary>
    /// 由內而外列出「畫在這個內容位置底下」的那些層（1.6），每一層是一個元素與它靜態寫的底
    /// （null ＝它自己沒畫底，但有觸發器可能把它畫起來）。到第一個真的畫了底的那一層為止 ——
    /// 再往下就看不到了。
    ///
    /// 順序照 WPF 的繪製順序：同一格裡文件順序在前的兄弟先（愈後面的愈上層），再才是容器自己的底 ——
    /// 容器的 Background 畫在它所有子元素的下面。疊層判定全部走 <see cref="Panels"/>，跟標記樹的
    /// 規則 17 同一套。
    ///
    /// ⚠ 兄弟這一步是語料逼出來的：MaterialDesign 的按鈕模板把底畫在
    /// <c>&lt;Border Background="{TemplateBinding Background}"/&gt;</c>，而它是內容（<c>wpf:Ripple</c>）的
    /// **兄弟**、不是祖先。只往祖先找的話這形狀收不到任何一層；宿主自己有設底時還接得住
    /// （宿主的 Background 優先序在前），**宿主沒設底時就配到祖先** —— 編出來的數字。
    ///
    /// 刻意放棄那一層、往外找的兩種（留在盲區表）：
    /// <list type="bullet">
    /// <item>內容元素（Image）：底是一張圖。標記樹裡記 <c>over-sibling-content</c>，模板裡還沒有對應的
    /// 計數器，收了會讓那些配對悄悄變成不可知</item>
    /// <item>帶 <c>Opacity</c> 的兄弟：半透明要疊到它底下那一層才算得出來，而這裡回傳的是**原始寫法**、
    /// 疊合在 Auditor 那邊做。猜一個不透明的值會往寬鬆那邊錯</item>
    /// </list>
    /// </summary>
    private List<(XElement El, string? Bg)> BackdropChain(XElement tmpl, XElement host, HashSet<string>? names = null)
    {
        var chain = new List<(XElement El, string? Bg)>();
        for (var node = host; node.Parent is not null && !ReferenceEquals(node, tmpl); node = node.Parent)
        {
            var box = node.Parent;
            if (Panels.LayoutOf(box.Name.LocalName, _cfg) == Panels.Layout.Overlap)
            {
                var isGrid = box.Name.LocalName == "Grid";
                var cell = Panels.CellOf(node, isGrid);
                var stretches = Panels.StretchesChildren(box.Name.LocalName);
                var level = new List<(XElement El, string? Bg)>();
                var painted = -1;
                foreach (var sib in box.Elements())
                {
                    if (ReferenceEquals(sib, node)) break; // 只看排在前面的（後面的畫在字上面，不是底）
                    var ln = sib.Name.LocalName;
                    if (ln.Contains('.') || Panels.Styleish.Contains(ln)) continue;
                    if (Panels.CellOf(sib, isGrid) != cell) continue; // 不同格子不相疊
                    // 內容元素（Image 等）：底是一張圖、靜態不可知 —— 跟標記樹的規則 17(b) 同一個答案
                    // （ColorResolver 會把這個記號轉回 ColorKind.SiblingContent）。
                    // ⚠ 不是「放棄這一層往外找」：往外找等於拿圖後面那一層的顏色報一個比值，而字其實疊在圖上
                    //   （HandyControl 的 Carousel 在標記樹那邊就是這個形狀）。排在它後面又畫了底的兄弟會蓋過
                    //   它，所以照舊往後掃、讓 painted 自己往後移。
                    if (Panels.IsContentElement(ln)) { painted = level.Count; level.Add((sib, ColorResolver.SiblingContentMark + ln)); continue; }
                    // ⚠ 撐不滿那一格的兄弟不算背板（強調色條之類），但它的名字照樣要記進 names（1.7）：
                    //   觸發器重畫它時，它在不在字底下靜態分不出來 —— 不記的話會被判成「跟這段文字無關」而放行，
                    //   那就是編造。記了只是讓那個狀態維持「不知道」。
                    if (!Panels.FillsCell(sib, stretches)) { AddName(names, sib); continue; }
                    var sraw = Panels.BackdropBrush(sib);
                    if (sraw is not null && sib.Attribute("Opacity") is not null) { level.Clear(); painted = -1; break; }
                    if (sraw is not null) painted = level.Count;
                    level.Add((sib, sraw));
                }
                foreach (var (el, _) in level) AddName(names, el);
                if (painted >= 0)
                {
                    // 畫了底的那一個以下看不到；它以上（文件順序在它後面）的候選由上而下排
                    level.RemoveRange(0, painted);
                    level.Reverse();
                    chain.AddRange(level);
                    return chain;
                }
                level.Reverse();
                chain.AddRange(level); // 都沒畫底：全是「可能被重畫」的候選
            }
            if (ReferenceEquals(box, tmpl)) return chain; // 模板外的事由呼叫端（祖先的底）決定
            var v = XamlBrush.Read(box, "Background") ?? XamlBrush.Read(box, "Fill");
            AddName(names, box);
            chain.Add((box, v));
            if (v is not null) return chain;
        }
        return chain;
    }

    private static void AddName(HashSet<string>? names, XElement el)
    {
        if (names is not null && NameOf(el) is { } n) names.Add(n);
    }

    /// <summary><c>x:Name</c> 或 <c>Name</c> —— 觸發 Setter 的 TargetName 指的就是這個。</summary>
    private static string? NameOf(XElement el)
        => el.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
           ?? el.Attribute("Name")?.Value;

    /// <summary>模板裡字色由宿主決定的文字元素（自己沒寫 Foreground，或寫的是 <c>{TemplateBinding Foreground}</c>）。</summary>
    private static bool IsHostTextHolder(XElement el, string localName)
    {
        if (!FgConsumers.Contains(localName)) return false;
        var own = XamlBrush.Read(el, "Foreground");
        return own is null || own.TrimStart().StartsWith("{TemplateBinding", StringComparison.Ordinal);
    }

    /// <summary>把宿主的字色畫成圖形的模板元素（圖示按鈕的 <c>&lt;Path Fill="{TemplateBinding Foreground}"/&gt;</c>）。</summary>
    private static bool IsHostForegroundShape(XElement el)
        => (XamlBrush.Read(el, "Fill") ?? XamlBrush.Read(el, "Stroke")) is { } v && TemplateBindingFg().IsMatch(v);

    internal Record CreateRecord(XElement style, string file)
    {
        var rec = new Record { Id = ++_seq, File = file, Side = _sideOf?.Invoke(file) };
        foreach (var s in style.Elements().Where(e => e.Name.LocalName == "Setter"))
        {
            var p = s.Attribute("Property")?.Value;
            var v = XamlBrush.SetterValue(s);
            if (p is not null && v is not null) rec.Setters[p] = v;
        }
        // <Style.Resources> 裡的隱含樣式（1.5）：執行期套用在「用了這個 Style 的元素」的內容上。
        // ⚠ 只收直接那一層、只收無 x:Key 且有 TargetType 的（keyed 的是一般資源，要靠 Style= 引用）。
        //   這些 Style 本身照舊也進一般的隱含樣式索引（它們在資源集合裡），那一份的作用範圍是行號區間、
        //   幾乎不會命中 —— 真正讓它們生效的是這裡這一格。
        if (style.Elements().FirstOrDefault(e => e.Name.LocalName == style.Name.LocalName + ".Resources") is { } styleRes)
            foreach (var inner in styleRes.Elements().Where(e => e.Name.LocalName == "Style"))
            {
                if (inner.Attribute(XKey) is not null) continue;
                if (inner.Attribute("TargetType")?.Value is not { } itt) continue;
                var im = TargetTypeName().Match(itt);
                if (im.Success) rec.ScopedImplicit[im.Groups["t"].Value] = CreateRecord(inner, file);
            }
        var tmplEl = style.Descendants().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
        rec.HasTemplate = tmplEl is not null;
        // 模板根＝第一個視覺元素（跳過 <ControlTemplate.Resources> 之類的屬性元素）
        var tmplRoot = tmplEl?.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
        rec.TemplateRootBg = tmplRoot is null ? null : XamlBrush.Read(tmplRoot, "Background");
        // 1.4 的新欄位只看這個 Style 自己的模板：tmplEl 是第一個 ControlTemplate，可能是巢狀 Style（ItemContainerStyle）的
        // （TemplateRootBg 照舊用 tmplEl —— 改它會動到文字配對的既有計數）
        var ownTmpl = tmplEl is not null && tmplEl.Ancestors().TakeWhile(a => !ReferenceEquals(a, style)).All(a => a.Name.LocalName != "Style")
            ? tmplEl : null;
        var ownRoot = ownTmpl is null ? null : tmplRoot;
        if (ownRoot is not null)
        {
            rec.TemplateRootBorder = NotTemplateBound(XamlBrush.Read(ownRoot, "BorderBrush"));
            rec.TemplateRootBorderThickness = NotTemplateBound(ownRoot.Attribute("BorderThickness")?.Value);
        }
        rec.OwnTemplate = ownTmpl is not null;
        // 先掃一遍「哪些模板內部元素會被觸發器重畫底」（Background／Fill 的 TargetName Setter）。
        // 分層只記這些名字 —— 沒有任何觸發器碰模板內部的 Style（絕大多數）行為完全不變。
        var repainted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in style.Descendants().Where(IsTriggerElement))
            foreach (var st in t.Elements().Where(e => e.Name.LocalName == "Setter"))
                if (st.Attribute("TargetName")?.Value is { } tname && st.Attribute("Property")?.Value is { } tp
                    && IsBackdropProp(tp) && XamlBrush.SetterValue(st) is not null)
                    repainted.Add(tname);
        if (ownTmpl is not null) rec.TemplateLayers = ContentLayers(ownTmpl, repainted, rec.ContentChainNames);
        var focusEl = ownTmpl?.Descendants()
            .FirstOrDefault(e => NotTemplateBound(XamlBrush.Read(e, "Stroke") ?? XamlBrush.Read(e, "BorderBrush")) is not null);
        if (focusEl is not null)
        {
            rec.FocusStroke = NotTemplateBound(XamlBrush.Read(focusEl, "Stroke") ?? XamlBrush.Read(focusEl, "BorderBrush"));
            rec.FocusOutside = focusEl.Attribute("Margin")?.Value.Contains('-') == true;
        }
        // 模板根的名字：規則 13 —— 觸發器裡「TargetName=模板根」的 Setter 等同
        // 直接設在宿主上（Kindling CellDeleteBtn hover 換底、QuillNest RadioButton
        // 選取態換底，共六筆假警報全是這形狀）。指向內部元素的 TargetName 仍不做。
        var rootName = tmplRoot?.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                       ?? tmplRoot?.Attribute("Name")?.Value;
        foreach (var t in style.Descendants().Where(IsTriggerElement))
        {
            var set = new Dictionary<string, string>();
            var rootTargeted = new HashSet<string>();
            var layerPaints = new Dictionary<string, string>();
            var layerTouched = new HashSet<string>(StringComparer.Ordinal);
            var unknownLayer = false;
            var beyondLayers = false;
            foreach (var s in t.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                var p = s.Attribute("Property")?.Value;
                var v = XamlBrush.SetterValue(s);
                if (p is null || v is null) continue;
                var tn = s.Attribute("TargetName")?.Value;
                // 無 TargetName＝套宿主；TargetName=模板根＝實質也是宿主的視覺（規則 13）
                if (tn is null) { set[p] = v; rootTargeted.Remove(p); }
                else if (rootName is not null && tn == rootName) { set[p] = v; rootTargeted.Add(p); }
                // 指向模板內部元素的顏色 Setter（見 State.UnknownLayer／State.LayerPaints）
                else if (PaintsLayer(p))
                {
                    unknownLayer = true;
                    layerTouched.Add(tn);
                    if (IsBackdropProp(p)) layerPaints[tn] = v;
                }
            }
            if (set.Count == 0 && !unknownLayer) continue;
            // 整個換掉模板：這個狀態的視覺是另一份模板畫的（ScreenToGif 的 ExtendedMenuItem Role 觸發器）
            if (set.ContainsKey("Template") || set.ContainsKey("Control.Template")) unknownLayer = beyondLayers = true;
            var inTmpl = false;
            var otherStyle = false;
            var otherTemplate = false;
            for (var anc = t.Parent; anc is not null && !ReferenceEquals(anc, style); anc = anc.Parent)
            {
                var ln = anc.Name.LocalName;
                if (ln == "ControlTemplate") inTmpl = true;
                if (ln == "Style") otherStyle = true;
                else if (ln.EndsWith("Template", StringComparison.Ordinal) && !ln.Contains('.') && !ReferenceEquals(anc, ownTmpl))
                    otherTemplate = true;
            }
            // ⚠ 1.6：觸發器從 Style 的**所有後代**收，所以會收到兩種不屬於這個元素的狀態。1.5 之前兩種都
            //   照樣當成宿主的狀態評分（只有 nonText 那條路看 Nested），1.6 隱含樣式也走逐狀態路徑之後
            //   就爆出來了 —— ScreenToGif 的 ExtendedMenuItem 一口氣 72 筆假 fail。
            //   - 巢狀 Style 裡的（ItemContainerStyle、DataGridTextColumn.ElementStyle、<Style.Resources> 裡的）：
            //     那是**別的元素**的狀態，宿主根本沒有這一組 → 整個不收。那些 Style 自己會被 WalkStyles 走到，
            //     不是靜默丟掉。
            //   - 被觸發器換掉的另一份模板裡的（Role 觸發器那種）：宿主真的會進那個狀態，但視覺是另一份模板畫的
            //     → 記 unresolved（同 UnknownLayer）。
            if (otherStyle) continue;
            if (otherTemplate) unknownLayer = beyondLayers = true;
            var nested = otherTemplate;
            // 條件簽名：同條件的狀態之後要跨節點合併
            var cond = CondSignature(t);
            rec.States.Add(new State
            {
                Disabled = IsDisabledTrigger(t),
                FromTemplate = inTmpl,
                Nested = nested,
                Set = set,
                RootTargeted = rootTargeted,
                LayerPaints = layerPaints,
                LayerPaintSrc = layerPaints.Keys.ToDictionary(k => k, _ => rec.Id),
                UnknownLayer = unknownLayer,
                LayerTouched = layerTouched,
                UnknownBeyondLayers = beyondLayers,
                Cond = cond,
            });
        }
        rec.Key = style.Attribute(XKey)?.Value;
        var bo = style.Attribute("BasedOn")?.Value;
        if (bo is not null)
        {
            var m = ResourceRef().Match(bo);
            if (m.Success) rec.BasedOn = m.Groups["k"].Value;
        }
        return rec;
    }

    /// <summary>
    /// 索引所有檔案的具名 Style —— 含色盤／主題字典（全域樣式就住在那裡；
    /// 「排除出 UI 掃描」不等於「排除出索引」）。查找時同檔優先於全域，
    /// 模擬 WPF 的資源查找順序（CelFlow 的對話框有自己的 DarkBtn，
    /// 與 DarkTheme.xaml 的全域 DarkBtn 同名不同值）。
    ///
    /// <paramref name="sideOf"/>：檔案只屬於哪一個主題（<see cref="PaletteDetection.SideOf"/>）。給了的話，
    /// 全域查找另外依主題各建一張表 —— Dark.xaml 與 Light.xaml 各定義一個 Chip 時，深色主題下只看得到
    /// Dark 的、淺色主題下只看得到 Light 的（執行期一次只載入一個主題字典）。
    /// </summary>
    public static StyleIndex Build(string root, Func<string, ThemeSide?>? sideOf = null, ToolConfig? cfg = null)
    {
        var idx = new StyleIndex { _sideOf = sideOf, _cfg = cfg ?? new ToolConfig() };
        foreach (var f in PaletteDetector.EnumerateFiles(root, "*.xaml"))
        {
            XDocument doc;
            // SetLineInfo：隱含樣式的作用範圍用行號表示（見 Record.FromLine）
            try { doc = XDocument.Load(f, LoadOptions.SetLineInfo); }
            catch { continue; }
            if (doc.Root is null) continue;
            var isGlobal = doc.Root.Name.LocalName == "ResourceDictionary" || PaletteDetector.IsApplicationRoot(doc.Root); // 含 Application 子類別
            foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
            {
                var rec = idx.CreateRecord(style, f);
                if (rec.Key is null)
                {
                    // 隱含樣式：無 x:Key、有 TargetType、而且**放在資源集合裡**，自動套用到該型別的每個實例。
                    // ⚠ 「沒有 x:Key」不等於「隱含樣式」。行內樣式（<TextBlock.Style><Style TargetType=…>）、
                    //   指派給屬性的樣式（ItemContainerStyle、DataGridTextColumn.ElementStyle、Setter.Value 裡的）
                    //   都沒有 x:Key，但它們只管被指派的那個元素／那一欄。
                    //   實測（MaterialDesign，1.5 開發中）：Clock 模板裡時鐘讀數的行內樣式
                    //   （FontSize 66、Foreground=Primary.Foreground，疊在紫色標頭上）被當成全域隱含樣式，
                    //   於是整個 repo 的 TextBlock 都被套上「66 級的深色字」—— 一口氣多出 214 筆假 fail。
                    if (!InResources(style)) continue;
                    var tt = style.Attribute("TargetType")?.Value;
                    if (tt is null) continue;
                    var m = TargetTypeName().Match(tt);
                    if (!m.Success) continue;
                    var typeName = m.Groups["t"].Value;
                    if (!idx._implicitByFile.TryGetValue(f, out var imap))
                        idx._implicitByFile[f] = imap = new Dictionary<string, List<Record>>(StringComparer.Ordinal);
                    var (from, to) = ScopeOf(style);
                    rec.FromLine = from; rec.ToLine = to;
                    if (!imap.TryGetValue(typeName, out var recs)) imap[typeName] = recs = new List<Record>();
                    recs.Add(rec);
                    // 全域表只收「整個範圍」的（字典最外層、App.Resources）—— 巢狀 Resources 裡的只管那個子樹，
                    // 連它自己的檔案都不是全部，更不可能跨檔
                    var wholeFile = from == 0 && to == int.MaxValue;
                    if (isGlobal && wholeFile)
                    {
                        if (!idx._implicitGlobal.TryGetValue(typeName, out var g)) idx._implicitGlobal[typeName] = g = new List<Record>();
                        g.Add(rec);
                        idx.AddImplicitBySide(typeName, rec);
                    }
                    continue;
                }
                if (!idx._byFile.TryGetValue(f, out var map))
                    idx._byFile[f] = map = new Dictionary<string, Record>();
                map[rec.Key] = rec;
                if (isGlobal && !idx._global.ContainsKey(rec.Key)) idx._global[rec.Key] = rec;
                if (isGlobal) idx.AddBySide(idx._globalBySide, rec.Key, rec);
            }
            foreach (var el in doc.Descendants())
            {
                // 型別名的集合（1.5）：隱含樣式在使用處評不評得到，看專案裡有沒有那個型別的元素。
                // 屬性元素 <Border.Background> 不是實例，排除
                if (!el.Name.LocalName.Contains('.', StringComparison.Ordinal)) idx._elementTypes.Add(el.Name.LocalName);
                if (el.Name.LocalName == "FontFamily" && el.Attribute(XKey)?.Value is { } fontKey)
                    idx._fontFamilies.TryAdd(fontKey, el.Value.Trim());
                if (el.Attribute("Style")?.Value is { } s && ResourceRef().Match(s.Trim()) is { Success: true } m)
                    idx._usedKeys.Add(m.Groups["k"].Value);
                // 行內 Style 一定被那個元素用到 —— 它的 BasedOn 基底也在使用處評
                if (el.Name.LocalName == "Style" && el.Parent?.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal) == true &&
                    el.Attribute("BasedOn")?.Value is { } bo && ResourceRef().Match(bo.Trim()) is { Success: true } bm)
                    idx._usedKeys.Add(bm.Groups["k"].Value);
            }
        }
        // BasedOn 的閉包：用到的 Style 的基底也會在使用處的合併鏈裡被評到
        var basesOf = idx._byFile.Values.SelectMany(map => map.Values)
            .Where(r => r.Key is not null && r.BasedOn is not null)
            .GroupBy(r => r.Key!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(r => r.BasedOn!).ToList(), StringComparer.Ordinal);
        var queue = new Queue<string>(idx._usedKeys);
        while (queue.Count > 0)
            if (basesOf.TryGetValue(queue.Dequeue(), out var bases))
                foreach (var b in bases)
                    if (idx._usedKeys.Add(b)) queue.Enqueue(b);
        return idx;
    }

    private void AddBySide(Dictionary<ThemeSide, Dictionary<string, Record>> tables, string key, Record rec)
    {
        if (rec.Side is not null) HasThemeSides = true;
        foreach (var (side, table) in tables)
            if ((rec.Side is null || rec.Side == side) && !table.ContainsKey(key)) table[key] = rec;
    }

    /// <summary>隱含樣式的依主題候選（同 <see cref="AddBySide"/>，但收成清單）。</summary>
    private void AddImplicitBySide(string typeName, Record rec)
    {
        if (rec.Side is not null) HasThemeSides = true;
        foreach (var (side, table) in _implicitBySide)
            if (rec.Side is null || rec.Side == side)
            {
                if (!table.TryGetValue(typeName, out var list)) table[typeName] = list = new List<Record>();
                list.Add(rec);
            }
    }

    /// <summary>
    /// 候選一致就回那一個，不一致回 null（「不知道是哪一個」→ 不猜）。
    ///
    /// 一致的判準是「我們會用到的那幾格」：BasedOn、Foreground／Background 的 Setter、有沒有模板、模板根的底。
    /// 深／淺兩份主題字典各宣告一個寫法相同的隱含樣式（<c>Foreground="{DynamicResource Fg0}"</c>）是常態，
    /// 那不是歧義 —— 色票鍵本身會依主題解析。
    /// </summary>
    private static Record? Agree(List<Record> candidates)
    {
        if (candidates.Count == 0) return null;
        static object Shape(Record r) => (r.BasedOn, r.Setters.GetValueOrDefault("Foreground"),
            r.Setters.GetValueOrDefault("Background"), r.HasTemplate, r.TemplateRootBg,
            // 模板把內容畫在不同的層上 = 不同的底（1.6），同樣是「不知道是哪一個」
            string.Join("|", r.TemplateLayers.Select(l => l.Bg)));
        var first = Shape(candidates[0]);
        return candidates.All(c => Shape(c).Equals(first)) ? candidates[0] : null;
    }

    /// <summary>同檔優先，其次全域；指定主題時全域只看那個主題看得到的（共用檔 ∪ 那個主題自己的檔）。</summary>
    private Record? Find(string key, string file, ThemeSide? side)
    {
        if (_byFile.TryGetValue(file, out var map) && map.TryGetValue(key, out var rec)) return rec;
        return side is { } s ? _globalBySide[s].GetValueOrDefault(key) : _global.GetValueOrDefault(key);
    }

    private Merged MergeChain(Record rec, HashSet<string> seen, string label, ThemeSide? side)
    {
        // 定義在主題檔裡的 Style，BasedOn 一律在那個主題裡找
        side = rec.Side ?? side;
        var merged = new Merged { Label = label, SideSpecific = rec.Side is not null };
        if (rec.BasedOn is not null && seen.Add(rec.BasedOn))
        {
            var baseRec = Find(rec.BasedOn, rec.File, side);
            if (baseRec is null) merged.MissingBase = rec.BasedOn;
            else
            {
                var m = MergeChain(baseRec, seen, label, side);
                merged.SideSpecific |= m.SideSpecific;
                merged.MissingBase = m.MissingBase;
                foreach (var (k, v) in m.Props) merged.Props[k] = v;
                foreach (var s in m.States)
                {
                    if (s.FromTemplate && rec.HasTemplate) continue; // 模板被換掉
                    merged.States.Add(s);
                }
                merged.TemplateRootBg = m.TemplateRootBg;
                merged.TemplateRootBorder = m.TemplateRootBorder;
                merged.TemplateRootBorderThickness = m.TemplateRootBorderThickness;
                merged.TemplateLayers = m.TemplateLayers;
                merged.ContentChainNames = m.ContentChainNames;
                merged.HasTemplate = merged.HasTemplate || m.HasTemplate;
                foreach (var (k, v) in m.ScopedImplicit) merged.ScopedImplicit[k] = v; // 基底的先進，下面讓衍生蓋掉
            }
        }
        foreach (var (k, v) in rec.ScopedImplicit) merged.ScopedImplicit[k] = v;
        foreach (var (p, v) in rec.Setters) merged.Props[p] = (v, rec.Id);
        foreach (var s in rec.States)
        {
            var srcMap = s.Set.Keys.ToDictionary(k => k, _ => rec.Id);
            merged.States.Add(new State
            {
                Disabled = s.Disabled,
                FromTemplate = s.FromTemplate,
                Nested = s.Nested,
                Set = new Dictionary<string, string>(s.Set),
                SetSrc = srcMap,
                RootTargeted = new HashSet<string>(s.RootTargeted),
                LayerPaints = new Dictionary<string, string>(s.LayerPaints),
                LayerPaintSrc = new Dictionary<string, int>(s.LayerPaintSrc),
                UnknownLayer = s.UnknownLayer,
                LayerTouched = new HashSet<string>(s.LayerTouched, StringComparer.Ordinal),
                UnknownBeyondLayers = s.UnknownBeyondLayers,
                Cond = s.Cond,
            });
        }
        if (rec.HasTemplate)
        {
            merged.HasTemplate = true;
            merged.TemplateRootBg = rec.TemplateRootBg;
            merged.TemplateRootBorder = rec.TemplateRootBorder;
            merged.TemplateRootBorderThickness = rec.TemplateRootBorderThickness;
        }
        // 模板分層與「有沒有人畫宿主的字」只跟**自己的**模板有關（1.6）：rec.HasTemplate 連巢狀 Style
        // 的模板也算，拿它當條件會把基底模板的分層清掉
        if (rec.OwnTemplate) { merged.TemplateLayers = rec.TemplateLayers; merged.ContentChainNames = rec.ContentChainNames; }
        return merged;
    }

    /// <summary>同條件觸發器合併：WPF 優先序是 style trigger &gt; template trigger、
    /// 衍生（後到）&gt; 基底（先到）。合併時 template 不蓋 style 已設的屬性。</summary>
    private static List<State> MergeSameCondition(List<State> states)
    {
        var byCond = new Dictionary<string, State>();
        var order = new List<string>();
        var anon = 0;
        foreach (var s in states)
        {
            var c = s.Cond ?? $"anon:{++anon}";
            if (!byCond.TryGetValue(c, out var t))
            {
                byCond[c] = new State
                {
                    Disabled = s.Disabled,
                    FromTemplate = s.FromTemplate,
                    Nested = s.Nested,
                    Set = new Dictionary<string, string>(s.Set),
                    SetSrc = new Dictionary<string, int>(s.SetSrc),
                    RootTargeted = new HashSet<string>(s.RootTargeted),
                    LayerPaints = new Dictionary<string, string>(s.LayerPaints),
                    LayerPaintSrc = new Dictionary<string, int>(s.LayerPaintSrc),
                    UnknownLayer = s.UnknownLayer,
                    LayerTouched = new HashSet<string>(s.LayerTouched, StringComparer.Ordinal),
                    UnknownBeyondLayers = s.UnknownBeyondLayers,
                    Cond = s.Cond,
                };
                order.Add(c);
                continue;
            }
            if (s.FromTemplate && !t.FromTemplate)
            {
                // template 觸發只補 style 觸發沒設的屬性
                foreach (var (k, v) in s.Set)
                    if (!t.Set.ContainsKey(k))
                    {
                        t.Set[k] = v; t.SetSrc[k] = s.SetSrc[k];
                        if (s.RootTargeted.Contains(k)) t.RootTargeted.Add(k);
                    }
            }
            else
            {
                foreach (var (k, v) in s.Set)
                {
                    t.Set[k] = v; t.SetSrc[k] = s.SetSrc[k];
                    if (s.RootTargeted.Contains(k)) t.RootTargeted.Add(k); else t.RootTargeted.Remove(k);
                }
                t.FromTemplate = t.FromTemplate && s.FromTemplate;
            }
            foreach (var (k, v) in s.LayerPaints) { t.LayerPaints[k] = v; t.LayerPaintSrc[k] = s.LayerPaintSrc[k]; }
            t.Disabled = t.Disabled || s.Disabled;
            t.UnknownLayer = t.UnknownLayer || s.UnknownLayer; // 同條件的狀態有一邊重畫了看不到的那層 → 整個不評
            t.LayerTouched.UnionWith(s.LayerTouched);
            t.UnknownBeyondLayers = t.UnknownBeyondLayers || s.UnknownBeyondLayers;
            t.Nested = t.Nested && s.Nested; // 有一半是自己的狀態就算自己的
        }
        return order.Select(c => byCond[c]).ToList();
    }

    /// <summary>
    /// 檔案根元素的隱含樣式背景 —— 「地板」。
    ///
    /// 為什麼只做根元素、不做完整的隱含樣式解析：
    ///   完整支援要模擬 WPF 的資源查找（合併字典、Application/Window/元素三層作用域、
    ///   跨組件 pack:// URI），而且 Foreground 是繼承屬性，一旦每個元素都能從隱含樣式
    ///   拿到值，配對數會爆量、假警報跟著來 —— 那是另一個量級的工程（見 ROADMAP）。
    ///
    /// 但外部專案實測指出，覆蓋率的損失幾乎全來自一個很窄的形狀：
    /// 根容器不寫 Background，靠隱含的 &lt;Style TargetType="Window"&gt; 給底，
    /// 於是樹走訪走到頂還是空的，整個檔案的文字全部無底可配。
    /// ScreenToGif：38 個根元素只有 6 個寫了 Background，1295/1373 的 unresolved
    /// 都是「祖先鏈上找不到背景」。四個受測專案則有 71~86% 的根元素直接寫了背景，
    /// 所以這個盲區在它們身上的代價是零 —— 同一個盲區、兩個數量級的差別。
    ///
    /// 只補「根元素」這一格：一個檔案最多影響一個值，不碰繼承語意，
    /// 拿到大部分的覆蓋率而不引入完整解析的雜訊風險。
    /// </summary>
    public string? ImplicitRootBackground(string typeName, string file, int line = 0, ThemeSide? side = null)
    {
        var rec = ImplicitRecord(typeName, file, line, side);
        if (rec is null) return null;
        var merged = MergeChain(rec, new HashSet<string>(), $"Style[implicit {typeName}]", side);
        return merged.Props.TryGetValue("Background", out var v) ? v.V : merged.TemplateRootBg;
    }

    /// <summary>
    /// Style 定義處（Auditor.WalkStyles）用的合併鏈：這個 Style 連同 BasedOn 逐層合併、同條件觸發器合併 ——
    /// 與元素端的鏈路徑同一套規則。回傳的 Id 是這個 Style 自己的來源編號：字與底都出自它的配對才在
    /// 定義處報，任一半來自 BasedOn 基底的組合由使用處（元素 × Style 鏈）評，兩邊不重複。
    /// ⚠ 舊版定義處每個觸發器各自跟基礎值合併、也不看 BasedOn：style trigger 設字色＋同條件 template trigger
    ///   設模板根底的同一個 hover 狀態被拆成兩個各缺一半的幻影組合，各報一筆假 fail。
    /// </summary>
    public (Merged Chain, int Id) DefinitionChain(XElement style, string file)
    {
        var rec = CreateRecord(style, file);
        var seen = new HashSet<string>();
        if (rec.Key is not null) seen.Add(rec.Key);
        var merged = MergeChain(rec, seen, "", null);
        merged.States = MergeSameCondition(merged.States);
        return (merged, rec.Id);
    }

    private static int LineOf(XElement el) => el is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

    /// <summary>
    /// 隱含樣式的作用範圍（行號區間）：放它的那層 <c>&lt;X.Resources&gt;</c> 屬於哪個元素的子樹。
    /// 字典／App 的資源、以及檔案根元素的 Resources 都是「整個範圍」（0～MaxValue）；
    /// 巢狀的（某個 Grid、ContentPresenter、模板裡的 Resources）只管那個元素的子樹。
    /// ⚠ 這是 1.5 的假警報修正，實例見 <see cref="Record.FromLine"/>。
    /// </summary>
    /// <summary>這個 Style 是放在資源集合裡嗎（<c>&lt;ResourceDictionary&gt;</c> 底下，或 <c>&lt;X.Resources&gt;</c> 裡，
    /// 含中間那層 ResourceDictionary）—— 只有這種無 x:Key 的 Style 才是隱含樣式。</summary>
    private static bool InResources(XElement style)
        => style.Parent is { } p &&
           (p.Name.LocalName == "ResourceDictionary" ||
            p.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal));

    private static (int From, int To) ScopeOf(XElement style)
    {
        var holder = style.Ancestors()
            .FirstOrDefault(a => a.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal));
        var owner = holder?.Parent;
        // 沒有 X.Resources 包著（直接寫在 ResourceDictionary 的最外層）或擁有者就是檔案根元素
        // （Window.Resources／Application.Resources）→ 整個範圍
        if (owner is null || owner.Parent is null) return (0, int.MaxValue);
        // ⚠ 字典檔也要套這條。MaterialDesign 的 PopupBox 模板裡有一個
        //   <Style TargetType="Button" BasedOn="…PopupBoxButton">，寫在模板內某個元素的 Resources 裡；
        //   把它當全域的話，那個 repo 裡六個示範 App 的每一顆 Button 都被套上彈出盒按鈕的字色 ——
        //   數字甚至可能剛好接近真實（都是卡片上的強調色字），但歸因是碰巧對的，那不是稽核。
        var to = owner.DescendantsAndSelf().Select(LineOf).DefaultIfEmpty(0).Max();
        return (LineOf(owner), to);
    }

    private static string? NotTemplateBound(string? v)
        => v is not null && v.TrimStart().StartsWith("{TemplateBinding", StringComparison.Ordinal) ? null : v;

    /// <summary>
    /// 型別的隱含樣式（無 x:Key、TargetType 是這個型別）合併成鏈 —— 1.4 起只給 nonText.uiComponents 用：
    /// 輸入框的框線幾乎都寫在主題的隱含樣式裡，不看它就什麼都評不到。查找同 <see cref="ImplicitRootBackground"/>（同檔優先、再全域）。
    /// ⚠ 文字配對照舊不吃隱含樣式（理由見 ImplicitRootBackground）：這裡不改預設的稽核範圍。
    /// </summary>
    public Merged? GetImplicitChain(string typeName, string file, int line = 0, ThemeSide? side = null)
    {
        var rec = ImplicitRecord(typeName, file, line, side);
        if (rec is null) return null;
        var merged = MergeChain(rec, new HashSet<string>(), $"Style[implicit {typeName}]", side);
        merged.States = MergeSameCondition(merged.States);
        return merged;
    }

    /// <summary>
    /// 這條鏈的 <c>&lt;Style.Resources&gt;</c> 裡那個型別的隱含樣式（1.5）；沒有是 null。
    /// 用在「套了這個 Style 的元素」的**內容**上 —— 呼叫端負責把它帶給子元素，見 <c>Auditor.Walk</c>。
    /// </summary>
    public Merged? ScopedImplicitChain(Merged owner, string typeName, ThemeSide? side = null)
    {
        if (!owner.ScopedImplicit.TryGetValue(typeName, out var rec)) return null;
        var merged = MergeChain(rec, new HashSet<string>(), $"Style[implicit {typeName}]", side);
        merged.States = MergeSameCondition(merged.States);
        return merged;
    }

    /// <summary>FocusVisualStyle 的值（<c>{StaticResource K}</c>）指向的 Style 畫的焦點框顏色（1.4）；
    /// <c>{x:Null}</c>、找不到的鍵、模板裡沒有框線是 null。</summary>
    public (string Stroke, bool Outside)? FocusVisualStroke(string? value, string file, ThemeSide? side = null)
    {
        if (value is null || ResourceRef().Match(value.Trim()) is not { Success: true } m) return null;
        return Find(m.Groups["k"].Value, file, side) is { FocusStroke: { } stroke } rec ? (stroke, rec.FocusOutside) : null;
    }

    /// <summary>元素套用的 Style（Style="{StaticResource X}" 或 &lt;X.Style&gt; 行內）。
    /// <paramref name="side"/>：在哪一個主題下解析（null = 不分主題，全域先出現的贏）。</summary>
    public Merged? GetElementChain(XElement el, string file, ThemeSide? side = null)
    {
        var sAttr = el.Attribute("Style")?.Value;
        if (sAttr is not null)
        {
            var m = ResourceRef().Match(sAttr);
            if (!m.Success) return null;
            var key = m.Groups["k"].Value;
            var rec = Find(key, file, side);
            if (rec is null) return null;
            var merged = MergeChain(rec, new HashSet<string> { key }, $"Style[{key}]", side);
            merged.States = MergeSameCondition(merged.States);
            return merged;
        }
        var propEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == el.Name.LocalName + ".Style");
        var styleEl = propEl?.Elements().FirstOrDefault(e => e.Name.LocalName == "Style");
        if (styleEl is null) return null;
        var inline = CreateRecord(styleEl, file);
        var label = inline.BasedOn is not null ? $"Style[→{inline.BasedOn}]" : "Style[inline]";
        var mergedInline = MergeChain(inline, new HashSet<string>(), label, side);
        mergedInline.States = MergeSameCondition(mergedInline.States);
        return mergedInline;
    }
}

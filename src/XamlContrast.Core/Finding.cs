namespace XamlContrast.Core;

/// <summary>等級（絕對對比）—— 與對稱是兩個獨立維度，不可混為一談。</summary>
public enum Category { Ok, Warn, Fail, Decorative }

/// <summary>
/// 對稱（跨主題）。⚠ 對稱不是意圖的證據 —— 兩邊一樣糟就只是兩邊都糟。
/// 舊版把「兩主題都低」判成『刻意低對比』並藏起來，吃掉了 Kindling 52 組、
/// CelFlow 96 組真實問題（規劃書 4.4／8.1）。
///
/// ⚠ 這個維度只在「沒過」的配對上有意義：BothLow 的語意是「色票本身不夠亮，
/// 換主題救不了」，套在 21:1 的合格配對上是胡說。ok/decorative 一律 NotApplicable，
/// JSON 輸出 null —— 人看的報告本來就只印 fail/warn，錯的標籤只會流到
/// 吃 JSON 的下游手上。
/// </summary>
public enum Symmetry { BothLow, DarkFails, LightFails, SingleTheme, NotApplicable }

/// <summary>
/// 一個主題下「比值是拿哪兩個顏色算的」：都是不透明的 #RRGGBB（大寫）。字色已經以自己的 alpha 與累乘的 Opacity
/// 疊到底上，底已經把半透明的層、Opacity 群組一層一層合成完 —— 也就是螢幕上實際看到的字色與底色。
/// <see cref="Wcag.Contrast"/> 拿這兩個值算出來的就是 finding 的比值（逐位相同，測試釘住）。
/// ⚠ 只放算過的值：fg／bg 欄（原始寫法）讀不出顏色的配對根本不會成為 finding，這裡不編造。
/// </summary>
public sealed record ContrastColors(string Fg, string Bg);

public sealed class Finding
{
    public required string File { get; init; }
    public int Line { get; init; }
    public required string Element { get; init; }
    public required string Fg { get; init; }
    public required string Bg { get; init; }
    /// <summary>字與底一個是色票一個是寫死（跨主題時寫死那邊不會跟著換）</summary>
    public bool Mixed { get; init; }
    /// <summary>深色主題下的比值。⚠ 只存在於淺色主題的配對（<see cref="Theme"/> 是 Light），這一欄內部填的是淺色的值 ——
    /// 對外一律經 <see cref="RatioIn"/>，不印一個不存在的主題的比值。</summary>
    public double RatioDark { get; init; }
    /// <summary>淺色主題下的比值（只存在於深色主題的配對，同上）。</summary>
    public double RatioLight { get; init; }

    /// <summary>那個主題下的比值；這組配對在那個主題不存在（<see cref="Theme"/> 是另一個主題）時是 null。
    /// JSON、console、Markdown、SARIF 都經這裡（審查 1.0 B3：1.0 之前兩欄都填那個主題的值，讀的人會以為另一個主題也量過）。</summary>
    public double? RatioIn(ThemeSide side) => Theme is { } t && t != side ? null
        : side == ThemeSide.Dark ? RatioDark : RatioLight;

    /// <summary>深色主題下算出 <see cref="RatioDark"/> 的那一對顏色（同 RatioDark：只存在於淺色主題的配對，內部填的是淺色的值 ——
    /// 對外一律經 <see cref="ColorsIn"/>）。稽核產的 finding 一律有；函式庫呼叫端自己建的可以沒有（null）。</summary>
    public ContrastColors? ColorsDark { get; init; }
    /// <summary>淺色主題下算出 <see cref="RatioLight"/> 的那一對顏色（同上）。</summary>
    public ContrastColors? ColorsLight { get; init; }

    /// <summary>那個主題下算比值的那一對顏色；這組配對在那個主題不存在時是 null —— 與 <see cref="RatioIn"/> 同一套規則，
    /// 不印一個不存在的主題的顏色。JSON 的 findings[].colors 與 HTML 報告的色塊都經這裡。</summary>
    public ContrastColors? ColorsIn(ThemeSide side) => Theme is { } t && t != side ? null
        : side == ThemeSide.Dark ? ColorsDark : ColorsLight;
    public bool IsText { get; init; }
    /// <summary>門檻：一般文字 4.5、大字級 3.0、裝飾 0</summary>
    public double Need { get; init; }
    /// <summary>給人看的字級標籤（"12px"，取整；沒設是空字串）—— console 用。JSON 用 <see cref="FontSize"/>。</summary>
    public string Size { get; init; } = "";
    /// <summary>FontSize（DIP），照寫的值不取整；沒設字級是 null。</summary>
    public double? FontSize { get; init; }
    public bool Large { get; init; }
    /// <summary>這組配對只存在於哪一個主題（Dark.xaml 裡的 Style、依主題解析到的 @dark／@light 鏈）；兩個主題都有是 null。
    /// 不是 null 時另一個主題的比值不存在（<see cref="RatioIn"/> 是 null），對稱欄標 single-theme。</summary>
    public ThemeSide? Theme { get; init; }
    /// <summary>只屬於一個主題的配對（<see cref="Theme"/> 不是 null）</summary>
    public bool OneThemeOnly => Theme is not null;

    // 分級後填入
    public Category Category { get; set; }
    public Symmetry Symmetry { get; set; }
    public double Gap { get; set; }

    /// <summary>最差比值：只存在於一個主題的配對就是那個主題的比值（baseline 的 worst、分級都用它）。</summary>
    public double Worst => Theme switch
    {
        ThemeSide.Dark => RatioDark,
        ThemeSide.Light => RatioLight,
        _ => Math.Min(RatioDark, RatioLight),
    };
}

/// <summary>
/// 無法解析的原因。總數告訴你「漏了多少」，但沒告訴你「能不能補救」——
/// 一個無法行動的計數器只誠實了一半。外部專案實測：ScreenToGif 的 1373 組 unresolved
/// 有 1295 組是同一個原因（根容器沒宣告背景），而使用者從總數上完全看不出這件事。
/// </summary>
public enum UnresolvedReason
{
    /// <summary>祖先鏈上沒有任何一層宣告背景色 —— 通常是根容器靠隱含樣式給底。可補救。</summary>
    NoAncestorBackground,
    /// <summary>顏色來自 Binding / TemplateBinding / 漸層 —— 執行期才知道。靜態分析的硬邊界。</summary>
    BoundOrGradient,
    /// <summary>資源鍵不在偵測到的色盤裡 —— 打字錯誤，或色盤偵測漏了某個檔。可補救。</summary>
    UnknownPaletteKey,
    /// <summary>背景是半透明但底下沒有可疊的顏色 —— 合成不出實際色值。</summary>
    TranslucentUncomposited,
    /// <summary>Style 把 Foreground 與 Background 設成同一個 brush —— Material 系的
    /// 模板不透明度慣用手法（模板把背景以 10~12% Opacity 畫成暈染，字用全濃度）。
    /// 字面上是 1:1，執行期不是；模板的 Opacity 動畫靜態看不到 —— 不猜。</summary>
    SameBrushPair,
    /// <summary>文字墊在同格子的兄弟「內容」元素上（Image 等）—— 實際背景是圖片，
    /// 靜態不可知；配祖先背景會產生假警報。</summary>
    OverSiblingContent,
    /// <summary>Style 的 Template setter 裡（&lt;Setter.Value&gt;&lt;ControlTemplate&gt;）自己帶 Foreground 的文字元素 ——
    /// 樹走訪不進 Style，這些從來沒有被評過，也不在任何計數器裡（審查 1.0 最終驗證 S3）。完整稽核屬稽核語意、留到 1.x；
    /// 1.0 先讓它不靜默：計入 unresolved、點名位置與那個 Foreground。</summary>
    InStyleTemplate,
}

/// <summary>一筆無法解析的配對「在哪裡、為什麼、卡在哪個值」。
/// 只有原因分類還不夠 —— 使用者知道「57 組找不到底色」,但不知道在哪幾個檔;
/// unknown-key 更是只要點名鍵就是免費的死引用/打字錯誤偵測器。</summary>
public sealed record UnresolvedSite(UnresolvedReason Reason, string File, int Line, string Value);

public sealed class AuditResult
{
    public required PaletteDetection Detection { get; init; }
    public required int FileCount { get; init; }
    public required List<Finding> Findings { get; init; }
    public required int Pairs { get; init; }
    public required int Unresolved { get; init; }
    /// <summary>unresolved 的原因細目（總和 = Unresolved）—— 讓「漏了多少」變成「該修哪裡」。</summary>
    public required Dictionary<UnresolvedReason, int> UnresolvedBy { get; init; }
    /// <summary>逐筆的無法解析位置（file:line＋卡住的值）。</summary>
    public required List<UnresolvedSite> UnresolvedSites { get; init; }
    /// <summary>translucent-uncomposited 裡「半透明的一層、底下什麼都沒有」的那些（不含 Style 定義處的透明底）——
    /// rootBackground 救得回來，求救指引要算進去。</summary>
    internal int TranslucentOverNothing { get; init; }
    public required int Skipped { get; init; }
    /// <summary>死 setter 過濾排除的 Style 配對數（過濾不靜默）</summary>
    public required int DeadForeground { get; init; }
    /// <summary>停用態豁免數（IsEnabled=False 觸發；WCAG 1.4.3 豁免，放行憑據是條文不是啟發法）</summary>
    public required int DisabledExempt { get; init; }
    /// <summary>XAML 解析失敗的檔案（檔名: 錯誤）。解析失敗不能讓檔案靜默消失。</summary>
    public required List<string> ParseErrors { get; init; }
    /// <summary>ignore 註解壓掉的數量 —— 壓掉不進 findings，但一定要計數</summary>
    public int Suppressed { get; init; }
    /// <summary>沒附理由的 ignore（無效，不壓）—— 要警告</summary>
    public required List<string> InvalidIgnores { get; init; }
    /// <summary>config 的 layout 宣告了、但這次稽核走過的元素沒有一個叫這個名字的（多半是拼錯或大小寫不對）——
    /// 宣告靜默失效，要點名（審查 1.0 B8）。不是錯誤：那個容器可能只是不在這次的掃描範圍裡。</summary>
    public IReadOnlyList<string> UnmatchedLayoutNames { get; init; } = [];
    /// <summary>本次生效的設定（分級門檻等；預設值 = 沒有 config 檔）</summary>
    public ToolConfig Config { get; init; } = new();

    public int CountOf(Category c) => Findings.Count(f => f.Category == c);

    /// <summary>
    /// 覆蓋率：解析成功的配對佔「看到的配對總數」的比例（0~1）。
    ///
    /// 「0 組配對就 exit 1」一直是不可設定的預設，理由是「空掃綠燈就是謊報健康」。
    /// 但那條線是二元的 —— 八個公開專案實測發現三個在 0.2% / 1.7% / 10.2% 的
    /// 覆蓋率下照樣亮綠燈（HandyControl 342 個檔只看懂 7 組，然後說「通過」）。
    /// 「幾乎什麼都沒看」和「看過了都沒問題」在退出碼上分不出來，等於同一種謊報。
    /// </summary>
    public double Coverage => Pairs + Unresolved == 0 ? 0 : (double)Pairs / (Pairs + Unresolved);

    /// <summary>覆蓋率百分比，四捨五入到一位（.5 進位）—— console、JSON、Markdown 印的與覆蓋率下限比的都是這一個值。</summary>
    public double CoveragePercent => Math.Round(Coverage * 100, 1, MidpointRounding.AwayFromZero);
}

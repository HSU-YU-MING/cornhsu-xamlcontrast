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
/// JSON 直接不輸出該欄位 —— 人看的報告本來就只印 fail/warn，錯的標籤只會流到
/// 吃 JSON 的下游手上。
/// </summary>
public enum Symmetry { BothLow, DarkFails, LightFails, SingleTheme, NotApplicable }

public sealed class Finding
{
    public required string File { get; init; }
    public int Line { get; init; }
    public required string Element { get; init; }
    public required string Fg { get; init; }
    public required string Bg { get; init; }
    /// <summary>字與底一個是色票一個是寫死（跨主題時寫死那邊不會跟著換）</summary>
    public bool Mixed { get; init; }
    public double RatioDark { get; init; }
    public double RatioLight { get; init; }
    public bool IsText { get; init; }
    /// <summary>門檻：一般文字 4.5、大字級 3.0、裝飾 0</summary>
    public double Need { get; init; }
    public string Size { get; init; } = "";
    public bool Large { get; init; }

    // 分級後填入
    public Category Category { get; set; }
    public Symmetry Symmetry { get; set; }
    public double Gap { get; set; }

    public double Worst => Math.Min(RatioDark, RatioLight);
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
}

public sealed class AuditResult
{
    public required PaletteDetection Detection { get; init; }
    public required int FileCount { get; init; }
    public required List<Finding> Findings { get; init; }
    public required int Pairs { get; init; }
    public required int Unresolved { get; init; }
    /// <summary>unresolved 的原因細目（總和 = Unresolved）—— 讓「漏了多少」變成「該修哪裡」。</summary>
    public required Dictionary<UnresolvedReason, int> UnresolvedBy { get; init; }
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
}

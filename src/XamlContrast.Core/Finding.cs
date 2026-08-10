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

public sealed class AuditResult
{
    public required PaletteDetection Detection { get; init; }
    public required int FileCount { get; init; }
    public required List<Finding> Findings { get; init; }
    public required int Pairs { get; init; }
    public required int Unresolved { get; init; }
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
}

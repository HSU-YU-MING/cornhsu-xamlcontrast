namespace XamlContrast.Core;

/// <summary>等級（絕對對比）—— 與對稱是兩個獨立維度，不可混為一談。</summary>
public enum Category { Ok, Warn, Fail, Decorative }

/// <summary>
/// 對稱（跨主題）。⚠ 對稱不是意圖的證據 —— 兩邊一樣糟就只是兩邊都糟。
/// 舊版把「兩主題都低」判成『刻意低對比』並藏起來，吃掉了 Kindling 52 組、
/// CelFlow 96 組真實問題（規劃書 4.4／8.1）。
/// </summary>
public enum Symmetry { BothLow, DarkFails, LightFails, SingleTheme }

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
    /// <summary>XAML 解析失敗的檔案（檔名: 錯誤）。解析失敗不能讓檔案靜默消失。</summary>
    public required List<string> ParseErrors { get; init; }
    /// <summary>ignore 註解壓掉的數量 —— 壓掉不進 findings，但一定要計數</summary>
    public int Suppressed { get; init; }
    /// <summary>沒附理由的 ignore（無效，不壓）—— 要警告</summary>
    public required List<string> InvalidIgnores { get; init; }

    public int CountOf(Category c) => Findings.Count(f => f.Category == c);
}

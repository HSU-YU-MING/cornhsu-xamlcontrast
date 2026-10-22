using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlContrast.Core;

/// <summary>config 檔內容不合法 —— CLI 層轉成 exit 2 與好訊息，不裸拋堆疊。</summary>
public sealed class ConfigException(string message) : Exception(message);

/// <summary>
/// xamlcontrast.config.json（schema 見 docs/config-schema.md）。
/// 原則：config 是逃生口，不是必經之路 —— 全部欄位都有預設值，
/// 零設定路徑完全不需要這個檔。優先序：CLI 旗標 &gt; config &gt; 內建預設。
/// </summary>
public sealed class ToolConfig
{
    public PaletteSection Palette { get; init; } = new();
    public ThresholdSection Thresholds { get; init; } = new();
    public ClassificationSection Classification { get; init; } = new();
    /// <summary>自訂容器的版面（規則 17 的兄弟背板）：疊層或排開。工具認不得的容器版面靜態不可知 →
    /// over-sibling-content；使用者知道自己的容器怎麼排，宣告的就是事實（決策檔 m7 的逃生口）。</summary>
    public LayoutSection Layout { get; init; } = new();
    public string FailOn { get; init; } = "warn"; // 1.0 的預設 gate：沒達到 AA 就算失敗
    public bool StrictPalette { get; init; }

    /// <summary>覆蓋率下限（百分比）。低於此值視為「這份結果不足以代表專案」而 exit 1。
    /// 預設 50 —— 解析不到一半就給過，那個「過」沒有意義。設 0 可完全關閉
    /// （0 組配對的保險仍在，那條不可設定）。</summary>
    public double MinCoverage { get; init; } = 50;

    /// <summary>
    /// 文件根元素的預設背景 —— 色票鍵（"MahApps.Brushes.Window.Background"）或
    /// 字面色碼（"#FF202020"）。只在根元素自己、Style 鏈、隱含樣式都沒給背景時生效。
    ///
    /// 為誰而設（三個新公開專案實查出的最大共同形狀）：**主題函式庫的使用者**。
    /// NETworkManager 的 726 筆、QuickLook 的 269 筆 no-ancestor-background 全是
    /// 同一回事 —— 視窗底色鍵就在 repo 的主題檔裡（深淺兩套俱全），但把
    /// 「MetroWindow → 那個鍵」連起來的隱含樣式住在 MahApps 的 NuGet 套件裡。
    /// 工具不猜（憲法）；使用者宣告的就是事實 —— 這正是 config 的角色。
    /// 鍵不在偵測到的色盤裡時是設定錯誤（exit 2），不是靜默忽略。
    /// </summary>
    public string? RootBackground { get; init; }
    public IgnoreSection Ignore { get; init; } = new();

    /// <summary>載入來源路徑；null = 沒有 config 檔（純預設值）</summary>
    [JsonIgnore] public string? SourcePath { get; private set; }

    public sealed class PaletteSection
    {
        public string Mode { get; init; } = "auto"; // auto | pair | csharp | single | none
        public string? DarkFile { get; init; }
        public string? LightFile { get; init; }
        public string? CsharpFile { get; init; }
        /// <summary>具名群組 key / dark / light 的 regex；null = 內建三元組樣式</summary>
        public string? CsharpPattern { get; init; }
    }

    public sealed class ThresholdSection
    {
        public double NormalText { get; init; } = 4.5;
        public double LargeText { get; init; } = 3.0;
        public double FailFactor { get; init; } = 0.67;

        [JsonIgnore]
        public bool IsDefault => NormalText == 4.5 && LargeText == 3.0 && FailFactor == 0.67;
    }

    public sealed class ClassificationSection
    {
        public string[] ExtraText { get; init; } = [];
        public string[] ExtraNonText { get; init; } = [];
    }

    public sealed class LayoutSection
    {
        /// <summary>子元素疊在一起的容器（當成只有一格的 Grid：佔滿的純色兄弟是後面字的底）。比對元素名（不含前綴），分大小寫。</summary>
        public string[] Overlap { get; init; } = [];
        /// <summary>子元素排開的容器（兄弟不是彼此的底）。</summary>
        public string[] SideBySide { get; init; } = [];
    }

    public sealed class IgnoreSection
    {
        public bool RequireReason { get; init; } = true;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public const string FileName = "xamlcontrast.config.json";

    /// <summary>從專案根目錄載入 config；沒有檔案就回傳純預設值。
    /// 壞掉或不合法的 config 擲 <see cref="ConfigException"/> —— 設定檔寫錯卻被
    /// 靜默忽略，等於使用者以為改了稽核標準而其實沒有（謊報健康的親戚）。</summary>
    public static ToolConfig Load(string root)
    {
        var path = Path.Combine(root, FileName);
        if (!File.Exists(path)) return new ToolConfig();

        ToolConfig cfg;
        string text;
        // 讀不到（被鎖、沒權限）也是設定錯誤 → exit 2；舊版只接 JsonException，IOException 直接逃出契約
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new ConfigException($"cannot read {FileName}: {ex.Message}"); }
        try { cfg = JsonSerializer.Deserialize<ToolConfig>(text, JsonOpts) ?? new ToolConfig(); }
        catch (JsonException ex) { throw new ConfigException(DescribeJsonError(ex, text)); }

        // 未知屬性是設定錯誤（1.0，決策檔 H5）。⚠ 0.x 靜默忽略：{"strictPallete": true} 載入時沒有任何錯誤，
        //   色盤退化保險其實沒開 —— 使用者以為改了稽核標準而其實沒有。全部點名（含區段內的，以 palette.mdoe 的形式）；
        //   $schema 是 JSON 編輯器的慣例，放行
        if (FindUnknown(text) is { Count: > 0 } unknown)
            throw new ConfigException(
                $"unknown propert{(unknown.Count == 1 ? "y" : "ies")} {string.Join(", ", unknown.Select(u => $"'{u}'"))} " +
                "— check the spelling against docs/config-schema.md");

        // ⚠ 區段、陣列、陣列元素給 null 時 System.Text.Json 照單全收 —— 舊版接著就是
        //   NullReferenceException／ArgumentNullException 的 .NET 堆疊，退出碼跳出 0/1/2 契約。
        //   寫錯的 config 一律 exit 2 並點名欄位
        if (cfg.Palette is null) throw new ConfigException("palette must be an object, got null");
        if (cfg.Thresholds is null) throw new ConfigException("thresholds must be an object, got null");
        if (cfg.Classification is null) throw new ConfigException("classification must be an object, got null");
        if (cfg.Ignore is null) throw new ConfigException("ignore must be an object, got null");
        if (cfg.Layout is null) throw new ConfigException("layout must be an object, got null");
        RequireNames(cfg.Layout.Overlap, "layout.overlap");
        RequireNames(cfg.Layout.SideBySide, "layout.sideBySide");
        // 比對的是不帶命名空間前綴的元素名（XElement.Name.LocalName）：l:CardGrid 永遠不會命中，宣告靜默失效。
        // 現在就拒絕（審查 1.0 B8：1.0 之後再擋就是收緊）
        foreach (var (names, field) in new[] { (cfg.Layout.Overlap, "layout.overlap"), (cfg.Layout.SideBySide, "layout.sideBySide") })
            if (names.FirstOrDefault(n => n.Contains(':')) is { } prefixed)
                throw new ConfigException($"{field}: '{prefixed}' has a namespace prefix — declare the element name without it ('{prefixed[(prefixed.LastIndexOf(':') + 1)..]}')");
        if (cfg.Layout.Overlap.Intersect(cfg.Layout.SideBySide, StringComparer.Ordinal).FirstOrDefault() is { } both)
            throw new ConfigException($"layout: '{both}' is declared both overlap and sideBySide");
        RequireNames(cfg.Classification.ExtraText, "classification.extraText");
        RequireNames(cfg.Classification.ExtraNonText, "classification.extraNonText");

        if (cfg.Palette.Mode is not ("auto" or "pair" or "csharp" or "single" or "none"))
            throw new ConfigException($"palette.mode must be auto|pair|csharp|single|none, got '{cfg.Palette.Mode}'");
        if (cfg.FailOn is not ("fail" or "warn"))
            throw new ConfigException($"failOn must be fail|warn, got '{cfg.FailOn}'");
        if (cfg.Palette.Mode == "pair" && (cfg.Palette.DarkFile is null || cfg.Palette.LightFile is null))
            throw new ConfigException("palette.mode 'pair' requires darkFile and lightFile");
        if (cfg.Palette.Mode == "single" && cfg.Palette.DarkFile is null)
            throw new ConfigException("palette.mode 'single' requires darkFile");
        if (cfg.Palette.Mode == "csharp" && cfg.Palette.CsharpFile is null)
            throw new ConfigException("palette.mode 'csharp' requires csharpFile");
        if (cfg.Thresholds.NormalText <= 1 || cfg.Thresholds.LargeText <= 1 ||
            cfg.Thresholds.FailFactor is <= 0 or > 1)
            throw new ConfigException("thresholds out of range (contrast ratios > 1, failFactor in (0,1])");
        // 大字級的門檻不能比一般文字嚴（1.0）：0.x 照單全收，大字反而要更高的對比 —— 幾乎一定是兩個值寫反了
        if (cfg.Thresholds.LargeText > cfg.Thresholds.NormalText)
            throw new ConfigException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"thresholds.largeText ({cfg.Thresholds.LargeText}) must not exceed thresholds.normalText ({cfg.Thresholds.NormalText})"));
        if (cfg.MinCoverage is < 0 or > 100)
            throw new ConfigException($"minCoverage must be between 0 and 100, got {cfg.MinCoverage.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        cfg.SourcePath = path;
        return cfg;
    }

    /// <summary>
    /// 型別錯誤 → 「欄位 must be 期望的型別」；JSON 本身壞掉才說不是合法的 JSON（審查 1.0 C5）。
    /// ⚠ 舊版一律「is not valid JSON: The JSON value could not be converted to System.Boolean. Path: …」——
    ///   JSON 本身沒錯，訊息卻這樣說，還帶出 .NET 的型別名。
    /// </summary>
    private static string DescribeJsonError(JsonException ex, string text)
    {
        // 根不是物件（[]、42、"warn"）：舊版說「is not valid JSON … could not be converted to XamlContrast.Core.ToolConfig」
        if (ex.Path == "$")
        {
            try
            {
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var got = doc.RootElement.ValueKind switch
                {
                    JsonValueKind.Array => "an array",
                    JsonValueKind.String => "a string",
                    JsonValueKind.Number => "a number",
                    JsonValueKind.True or JsonValueKind.False => "a boolean",
                    JsonValueKind.Null => "null",
                    _ => null,
                };
                if (got is not null) return $"{FileName} must be a JSON object, got {got}";
            }
            catch (JsonException) { }
        }
        var type = System.Text.RegularExpressions.Regex.Match(ex.Message, @"could not be converted to (?<t>\S+?)\.?(\s|$)");
        if (type.Success && ex.Path is { Length: > 2 } path && path.StartsWith("$."))
        {
            var t = type.Groups["t"].Value;
            var expected = t.EndsWith("[]") ? "an array of strings"
                : t.Contains("Boolean") ? "true or false"
                : t.Contains("Double") || t.Contains("Int32") ? "a number"
                : t.Contains("String") ? "a string"
                : "an object";
            return $"{path[2..]} must be {expected}";
        }
        return $"{FileName} is not valid JSON: {ex.Message}";
    }

    /// <summary>元素名清單：不能是 null，也不能含 null／空白名 —— 分類用後綴比對，
    /// EndsWith("") 恆真：extraNonText 裡一個 "" 會把所有不在內建表的元素都判成裝飾、
    /// 不受文字門檻檢查（靜默放行），跟 null 一樣當設定錯誤。</summary>
    private static void RequireNames(string[]? names, string field)
    {
        if (names is null) throw new ConfigException($"{field} must be an array of element names, got null");
        if (names.Any(string.IsNullOrWhiteSpace))
            throw new ConfigException($"{field} must not contain null or empty element names");
    }

    private static List<string> FindUnknown(string json)
    {
        var unknown = new List<string>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        Walk(doc.RootElement, typeof(ToolConfig), "");
        return unknown;

        void Walk(JsonElement obj, Type type, string path)
        {
            if (obj.ValueKind != JsonValueKind.Object) return;
            foreach (var p in obj.EnumerateObject())
            {
                if (path.Length == 0 && p.Name == "$schema") continue;
                // 與反序列化同一套規則：不分大小寫、只認可寫入且沒有 [JsonIgnore] 的公開屬性
                var prop = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(pi => pi.SetMethod is not null &&
                                          !pi.IsDefined(typeof(JsonIgnoreAttribute), inherit: true) &&
                                          string.Equals(pi.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                if (prop is null) { unknown.Add(path + p.Name); continue; }
                if (prop.PropertyType.DeclaringType == typeof(ToolConfig))
                    Walk(p.Value, prop.PropertyType, path + p.Name + ".");
            }
        }
    }
}

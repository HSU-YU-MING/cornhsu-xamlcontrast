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
    public string FailOn { get; init; } = "fail";
    public bool StrictPalette { get; init; }
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
        try { cfg = JsonSerializer.Deserialize<ToolConfig>(File.ReadAllText(path), JsonOpts) ?? new ToolConfig(); }
        catch (JsonException ex) { throw new ConfigException($"{FileName} is not valid JSON: {ex.Message}"); }

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

        cfg.SourcePath = path;
        return cfg;
    }
}

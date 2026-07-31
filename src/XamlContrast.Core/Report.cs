using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamlContrast.Core;

/// <summary>
/// 輸出合約（規劃書 4.5）。JSON 分兩層：summary 讓退化資訊機器可讀
/// （CI 只消費 findings 的話，「退化要用喊的」等於不存在），findings 是逐筆結果。
/// 文案一律英文 —— JSON 的 category 是給 CI 腳本 match 的。
/// </summary>
public static class Report
{
    public static string CategoryLabel(Category c) => c switch
    {
        Category.Ok => "ok",
        Category.Warn => "warn",
        Category.Fail => "fail",
        _ => "decorative",
    };

    public static string SymmetryLabel(Symmetry s) => s switch
    {
        Symmetry.BothLow => "both-low",
        Symmetry.DarkFails => "dark-fails",
        Symmetry.LightFails => "light-fails",
        _ => "single-theme",
    };

    private static string ModeLabel(PaletteMode m) => m switch
    {
        PaletteMode.Pair => "pair",
        PaletteMode.CSharp => "csharp",
        PaletteMode.Single => "single",
        _ => "none",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(AuditResult r)
    {
        var payload = new
        {
            summary = new
            {
                paletteSource = r.Detection.Mode == PaletteMode.None ? "none" : "project",
                paletteMode = ModeLabel(r.Detection.Mode),
                paletteDetail = r.Detection.Description,
                paletteKeys = r.Detection.Palette.Count,
                singleTheme = r.Detection.Palette.IsSingleTheme,
                files = r.FileCount,
                pairs = r.Pairs,
                unresolved = r.Unresolved,
                skipped = r.Skipped,
                deadForeground = r.DeadForeground,
                parseErrors = r.ParseErrors.Count,
                parseErrorFiles = r.ParseErrors.Count > 0 ? r.ParseErrors : null,
                suppressed = r.Suppressed,
                counts = new
                {
                    fail = r.CountOf(Category.Fail),
                    warn = r.CountOf(Category.Warn),
                    ok = r.CountOf(Category.Ok),
                    decorative = r.CountOf(Category.Decorative),
                },
            },
            findings = r.Findings.Select(f => new
            {
                file = f.File,
                line = f.Line,
                element = f.Element,
                fg = f.Fg,
                bg = f.Bg,
                ratioDark = f.RatioDark,
                ratioLight = f.RatioLight,
                threshold = f.Need,
                category = CategoryLabel(f.Category),
                symmetry = SymmetryLabel(f.Symmetry),
                isText = f.IsText,
                fontSize = f.Size.Length > 0 ? f.Size : null,
                largeText = f.Large,
            }),
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    /// <summary>
    /// 主控台報告。逐段對應原型的輸出（那是規格），但文案改英文（M3 合約）。
    /// </summary>
    public static string ToConsole(AuditResult r, bool showOk = false)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine((r.Detection.IsDegraded ? "!! palette: " : "palette: ") + r.Detection.Description);
        sb.AppendLine(string.Create(inv,
            $"files {r.FileCount} | text-on-background pairs {r.Pairs}"));
        sb.AppendLine(string.Create(inv,
            $"unresolved (background not in tree / bound at runtime): {r.Unresolved} | skipped (translucent, gradients, invisible): {r.Skipped}"));
        if (r.DeadForeground > 0)
        {
            // 過濾不靜默：被判定為死 setter 的配對要讓使用者知道有幾組、憑什麼被排除
            sb.AppendLine(string.Create(inv,
                $"excluded {r.DeadForeground} style pair(s): template has no Foreground consumer (no ContentPresenter/TextBlock/... — the Foreground setter is never rendered)"));
        }
        foreach (var e in r.ParseErrors)
            sb.AppendLine($"!! parse failed: {e}"); // 檔案不能從報告裡靜默消失
        sb.AppendLine();

        if (r.Detection.Palette.Count > 0 && r.Detection.Palette.IsSingleTheme)
            sb.AppendLine("single-theme palette detected: symmetry column not applicable, absolute contrast only");

        foreach (var cat in (ReadOnlySpan<Category>)[Category.Ok, Category.Fail, Category.Warn, Category.Decorative])
        {
            var n = r.CountOf(cat);
            if (n > 0) sb.AppendLine(string.Create(inv, $"  {CategoryLabel(cat),-11} {n}"));
        }
        sb.AppendLine();

        // ⚠「低於 AA」只算 fail + warn。裝飾不適用 AA，算進來是虛胖的數字
        //   （v0.1 曾把 CelFlow 報成 80 = 破1+偏低1+裝飾78，規劃書 8.2 #6）。
        var bad = r.Findings.Where(f => f.Category is Category.Fail or Category.Warn).ToList();
        if (bad.Count > 0)
        {
            sb.AppendLine(string.Create(inv, $"  below AA: {bad.Count}, by cause:"));
            foreach (var g in bad.GroupBy(f => f.Symmetry).OrderByDescending(g => g.Count()))
                sb.AppendLine(string.Create(inv, $"    {SymmetryLabel(g.Key),-13} {g.Count()}"));
            sb.AppendLine();
            sb.AppendLine("  both-low = the palette itself is too weak, switching theme won't save it;");
            sb.AppendLine("  dark-fails / light-fails = the design intent didn't survive the other theme.");
            sb.AppendLine();
        }

        foreach (var cat in (ReadOnlySpan<Category>)[Category.Fail, Category.Warn])
        {
            var group = r.Findings.Where(f => f.Category == cat).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine($"===== {CategoryLabel(cat)} ({group.Count}) =====");
            foreach (var f in group.OrderBy(f => f.Worst))
            {
                var tag = string.Create(inv, $"need {f.Need}");
                if (f.Large) tag += $" large-text {f.Size}";
                else if (f.Size.Length > 0) tag += $" {f.Size}";
                sb.AppendLine(string.Create(inv,
                    $"{f.File + ":" + f.Line,-34} {f.Element,-24} fg={f.Fg,-26} bg={f.Bg,-20} dark={f.RatioDark,6}:1 light={f.RatioLight,6}:1  [{SymmetryLabel(f.Symmetry)}] {tag}"));
            }
            sb.AppendLine();
        }

        if (showOk)
        {
            var ok = r.Findings.Where(f => f.Category == Category.Ok).ToList();
            sb.AppendLine($"===== ok ({ok.Count}), grouped by pair (top 30) =====");
            foreach (var g in ok.GroupBy(f => (f.Fg, f.Bg)).OrderByDescending(g => g.Count()).Take(30))
            {
                var x = g.First();
                sb.AppendLine(string.Create(inv,
                    $"  fg={x.Fg,-28} bg={x.Bg,-20} dark={x.RatioDark,6}:1 light={x.RatioLight,6}:1   x{g.Count()}"));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

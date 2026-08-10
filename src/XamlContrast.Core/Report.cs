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
        Symmetry.SingleTheme => "single-theme",
        _ => "n/a",
    };

    /// <summary>unresolved 原因的對外名稱。「可補救 / 硬邊界」的區分是這個欄位的重點：
    /// 使用者要知道的不是「漏了 1373 組」，是「其中 1295 組只要宣告根背景就會回來」。</summary>
    public static string ReasonLabel(UnresolvedReason r) => r switch
    {
        UnresolvedReason.NoAncestorBackground => "no-ancestor-background",
        UnresolvedReason.BoundOrGradient => "bound-or-gradient",
        UnresolvedReason.UnknownPaletteKey => "unknown-palette-key",
        _ => "translucent-uncomposited",
    };

    private static IEnumerable<KeyValuePair<UnresolvedReason, int>> Reasons(AuditResult r)
        => r.UnresolvedBy.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value);

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
            // JSON 消費端要能偵測格式演進 —— 0.x 期間欄位可能變動，變動時遞增
            // 2：ok/decorative 的 findings 不再輸出 symmetry（該維度只在沒過的配對上有意義）
            // 3：summary 新增 unresolvedBy（unresolved 的原因細目）
            // 4：summary 新增 coverage（解析成功的配對佔看到的比例）
            schemaVersion = 4,
            summary = new
            {
                paletteSource = r.Detection.Mode == PaletteMode.None ? "none" : "project",
                paletteMode = ModeLabel(r.Detection.Mode),
                paletteDetail = r.Detection.Description,
                paletteKeys = r.Detection.Palette.Count,
                singleTheme = r.Detection.Palette.IsSingleTheme,
                configLoaded = r.Config.SourcePath is not null,
                thresholds = new
                {
                    normalText = r.Config.Thresholds.NormalText,
                    largeText = r.Config.Thresholds.LargeText,
                    failFactor = r.Config.Thresholds.FailFactor,
                },
                files = r.FileCount,
                pairs = r.Pairs,
                // 覆蓋率是「這份結果代不代表專案」的關鍵數字 —— 只給 pairs 的話，
                // 消費端無從分辨「全綠」是掃得乾淨還是根本沒掃到
                coverage = Math.Round(r.Coverage * 100, 1),
                unresolved = r.Unresolved,
                // 細目讓「漏了多少」變成「該修哪裡」—— 只有總數的話，
                // 使用者看不出其中絕大多數可能是同一個可補救的原因
                unresolvedBy = Reasons(r).ToDictionary(kv => ReasonLabel(kv.Key), kv => kv.Value),
                skipped = r.Skipped,
                deadForeground = r.DeadForeground,
                disabledExempt = r.DisabledExempt,
                parseErrors = r.ParseErrors.Count,
                parseErrorFiles = r.ParseErrors.Count > 0 ? r.ParseErrors : null,
                suppressed = r.Suppressed,
                invalidIgnores = r.InvalidIgnores.Count > 0 ? r.InvalidIgnores : null,
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
                // 合格的配對沒有「換主題救不救得回來」可言 —— 欄位整個省略（null 不輸出），
                // 比給一個看起來像分類結果的錯值安全
                symmetry = f.Symmetry == Symmetry.NotApplicable ? null : SymmetryLabel(f.Symmetry),
                isText = f.IsText,
                fontSize = f.Size.Length > 0 ? f.Size : null,
                largeText = f.Large,
            }),
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    /// <summary>
    /// SARIF 2.1.0（GitHub code scanning 消費）。只輸出 fail/warn ——
    /// ok/decorative 灌進 Security tab 是噪音；退化計數器不屬於 SARIF 的語彙，
    /// machine-readable 的退化資訊仍以 --json 的 summary 為準。
    /// </summary>
    public static string ToSarif(AuditResult r, string toolVersion)
    {
        var results = r.Findings
            .Where(f => f.Category is Category.Fail or Category.Warn)
            .Select(f => new
            {
                ruleId = "wcag-contrast",
                level = f.Category == Category.Fail ? "error" : "warning",
                message = new
                {
                    text = $"Contrast {f.RatioDark}:1 dark / {f.RatioLight}:1 light — needs {f.Need}:1 " +
                           $"(fg={f.Fg}, bg={f.Bg}, {SymmetryLabel(f.Symmetry)}, {f.Element})",
                },
                locations = new[]
                {
                    new
                    {
                        physicalLocation = new
                        {
                            artifactLocation = new { uri = f.File }, // root 相對、正斜線
                            region = new { startLine = Math.Max(f.Line, 1) },
                        },
                    },
                },
            });

        var payload = new
        {
            version = "2.1.0",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "XamlContrast",
                            version = toolVersion,
                            informationUri = "https://github.com/HSU-YU-MING/cornhsu-xamlcontrast",
                            rules = new[]
                            {
                                new
                                {
                                    id = "wcag-contrast",
                                    name = "WcagContrast",
                                    shortDescription = new { text = "Text contrast below WCAG 2.x AA" },
                                    helpUri = "https://github.com/HSU-YU-MING/cornhsu-xamlcontrast#readme",
                                },
                            },
                        },
                    },
                    results,
                },
            },
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
        if (r.Config.SourcePath is not null)
            sb.AppendLine($"config: {Path.GetFileName(r.Config.SourcePath)} loaded");
        if (!r.Config.Thresholds.IsDefault)
        {
            // 改門檻等於改稽核標準 —— 允許，但要標明，報告的讀者才知道基準不是 WCAG 預設
            var t = r.Config.Thresholds;
            sb.AppendLine(string.Create(inv,
                $"!! non-default thresholds: normal {t.NormalText}, large {t.LargeText}, fail-factor {t.FailFactor}"));
        }
        sb.AppendLine(string.Create(inv,
            $"files {r.FileCount} | text-on-background pairs {r.Pairs} | coverage {r.Coverage * 100:F1}% of pairs seen"));
        sb.AppendLine(string.Create(inv,
            $"unresolved (colour bound at runtime / gradient / key not in palette): {r.Unresolved} | skipped (translucent, invisible): {r.Skipped}"));
        if (r.Unresolved > 0)
            // 細目才是可行動的部分：no-ancestor-background 佔大宗代表「宣告根背景就會回來」，
            // bound-or-gradient 佔大宗代表那是靜態分析的硬邊界，沒得救
            sb.AppendLine("  unresolved by reason: " +
                string.Join(", ", Reasons(r).Select(kv => string.Create(inv, $"{ReasonLabel(kv.Key)} {kv.Value}"))));
        if (r.DeadForeground > 0)
        {
            // 過濾不靜默：被判定為死 setter 的配對要讓使用者知道有幾組、憑什麼被排除
            sb.AppendLine(string.Create(inv,
                $"excluded {r.DeadForeground} style pair(s): template has no Foreground consumer (no ContentPresenter/TextBlock/... — the Foreground setter is never rendered)"));
        }
        if (r.DisabledExempt > 0)
            sb.AppendLine(string.Create(inv,
                $"exempted {r.DisabledExempt} disabled-state pair(s) (IsEnabled=False; WCAG 1.4.3 does not require contrast for disabled controls)"));
        foreach (var e in r.ParseErrors)
            sb.AppendLine($"!! parse failed: {e}"); // 檔案不能從報告裡靜默消失
        if (r.Suppressed > 0)
            sb.AppendLine(string.Create(inv, $"suppressed {r.Suppressed} pair(s) via xamlcontrast-ignore comments"));
        foreach (var w in r.InvalidIgnores)
            sb.AppendLine($"!! {w}"); // 沒理由的 ignore 是無效的，且要讓人看見
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

    /// <summary>
    /// Markdown 報告 —— 給 PR 留言用(姊妹專案 Parity 的 action 早就這樣做,這邊補上)。
    ///
    /// 為什麼行內註記不夠:::error 只標在出問題的那一行,PR 上沒有總覽 —— reviewer 看不到
    /// 「這次總共破幾組、色盤有沒有偵測失敗、有幾組被豁免/壓掉」。而退化計數(unresolved、
    /// skipped、parse errors)正是本工具的憲法「稽核工具最大的風險是謊報健康」的體現,
    /// 它們必須出現在人會看的那份報告裡,不能只躺在 --json 裡。
    /// </summary>
    public static string ToMarkdown(AuditResult r, int exitCode)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var fail = r.CountOf(Category.Fail);
        var warn = r.CountOf(Category.Warn);

        sb.AppendLine("## XamlContrast — WCAG contrast audit");
        sb.AppendLine();
        sb.AppendLine(string.Create(inv,
            $"{(exitCode == 0 ? "✅ **PASS**" : "❌ **FAIL**")} · **{fail} fail**, {warn} warn · " +
            $"{r.Pairs} text-on-background pair(s) across {r.FileCount} file(s)"));
        sb.AppendLine();

        // 色盤偵測退化要放最前面:退回「只算寫死色碼」時,下面那張表是嚴重不完整的,
        // 讀者必須先知道這件事再看數字。
        if (r.Detection.IsDegraded)
        {
            sb.AppendLine($"> ⚠️ **Palette detection degraded** — {Esc(r.Detection.Description)}");
            sb.AppendLine("> Only hard-coded colors were evaluated; theme-driven colors are not covered by this run.");
            sb.AppendLine();
        }
        if (!r.Config.Thresholds.IsDefault)
        {
            var t = r.Config.Thresholds;
            sb.AppendLine(string.Create(inv,
                $"> ⚠️ **Non-default thresholds** — normal {t.NormalText}, large {t.LargeText}, fail-factor {t.FailFactor}."));
            sb.AppendLine("> The audit standard for this run is not the WCAG default.");
            sb.AppendLine();
        }
        if (r.Pairs == 0)
        {
            sb.AppendLine("> ⚠️ **Zero pairs resolved** — nothing was audited. An empty scan is not a pass.");
            sb.AppendLine();
        }

        foreach (var cat in (ReadOnlySpan<Category>)[Category.Fail, Category.Warn])
        {
            var group = r.Findings.Where(f => f.Category == cat).OrderBy(f => f.Worst).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine($"### {CategoryLabel(cat)} ({group.Count})");
            sb.AppendLine();
            sb.AppendLine("| Location | Element | fg → bg | dark | light | need | symmetry |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var f in group)
            {
                var need = string.Create(inv, $"{f.Need}");
                if (f.Large) need += " (large)";
                sb.AppendLine(string.Create(inv,
                    $"| `{Esc(f.File)}:{f.Line}` | {Esc(f.Element)} | `{Esc(f.Fg)}` → `{Esc(f.Bg)}` | " +
                    $"{f.RatioDark}:1 | {f.RatioLight}:1 | {need} | {SymmetryLabel(f.Symmetry)} |"));
            }
            sb.AppendLine();
        }

        if (fail + warn > 0)
        {
            sb.AppendLine("<sub>`both-low` = the palette itself is too weak, switching theme won't save it. " +
                          "`dark-fails` / `light-fails` = the design intent didn't survive the other theme.</sub>");
            sb.AppendLine();
        }

        // 「放過的東西」一律計數並列出 —— 豁免、排除、壓掉、解析失敗都是本工具沒看的地方。
        var notes = new List<string>();
        if (r.Unresolved > 0)
            notes.Add(string.Create(inv, $"{r.Unresolved} unresolved ({string.Join(", ", Reasons(r).Select(kv => string.Create(inv, $"{ReasonLabel(kv.Key)} {kv.Value}")))})"));
        if (r.Skipped > 0) notes.Add(string.Create(inv, $"{r.Skipped} skipped (translucent, invisible)"));
        if (r.DeadForeground > 0) notes.Add(string.Create(inv, $"{r.DeadForeground} excluded (dead Foreground setter)"));
        if (r.DisabledExempt > 0) notes.Add(string.Create(inv, $"{r.DisabledExempt} exempted (IsEnabled=False; WCAG 1.4.3)"));
        if (r.Suppressed > 0) notes.Add(string.Create(inv, $"{r.Suppressed} suppressed via xamlcontrast-ignore"));
        if (notes.Count > 0)
        {
            sb.AppendLine($"**Not evaluated:** {string.Join(" · ", notes)}");
            sb.AppendLine();
        }
        foreach (var e in r.ParseErrors)
        {
            sb.AppendLine($"> ⚠️ **Parse failed:** {Esc(e)} — this file is missing from the audit above.");
            sb.AppendLine();
        }
        foreach (var w in r.InvalidIgnores)
        {
            sb.AppendLine($"> ⚠️ {Esc(w)}");
            sb.AppendLine();
        }

        sb.AppendLine("<sub>Generated by XamlContrast · static WCAG contrast audit for XAML source</sub>");
        return sb.ToString();
    }

    /// <summary>Markdown 表格用:跳脫會破壞表格/格式的字元。</summary>
    private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}

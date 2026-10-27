using System.Globalization;
using System.Text;

namespace XamlContrast.Core;

public static partial class Report
{
    /// <summary>
    /// 單檔 HTML 報告（--html，1.1）—— 給人看的那一份，CI 上傳成 artifact 之後直接打開。
    /// 每筆 fail／warn 在兩個主題下各畫出「比值是拿哪兩個顏色算的」：字色與底色的色塊、用那一對顏色寫的樣字、比值與門檻
    /// （顏色來自 <see cref="Finding.ColorsIn"/>，與 JSON 的 findings[].colors 同一份：半透明的層已經合成）。
    ///
    /// 跟 Markdown 一樣的立場：退化與「沒評到的東西」一律印出來，計數器是 0 也印 —— 這份報告被人當成「全貌」看，
    /// 少印一個 0 就分不出「沒有」與「沒算」。ok／decorative 收在 &lt;details&gt; 裡（全列，預設收合），unresolved 逐筆列出。
    ///
    /// ⚠ 報告裡的文字（檔名、元素名、鍵名、原始值、偵測描述）全部來自不可信的 XAML，而這個檔會從 CI 的 artifact 直接在瀏覽器打開：
    ///   一律經 <see cref="H"/> 跳脫；不可信的文字不進任何屬性，style 屬性裡只放驗過的 #RRGGBB（<see cref="CssHex"/>）。
    ///   Content-Security-Policy 禁掉 script 與一切外部資源 —— 跳脫哪天漏了一處也執行不了（第二道）。
    /// 不需要 JavaScript（收合用 &lt;details&gt;）；深淺色跟著 prefers-color-scheme；顏色不單獨表意：等級、達標與否一律有文字。
    /// 版面屬 presentation、不凍結（同 console 與 Markdown），機器讀取請用 JSON。
    /// </summary>
    public static string ToHtml(AuditResult r, ReportContext? context = null)
    {
        var ctx = context ?? new ReportContext();
        var inv = CultureInfo.InvariantCulture;
        var fail = r.CountOf(Category.Fail);
        var warn = r.CountOf(Category.Warn);
        var failOn = ctx.FailOn ?? r.Config.FailOn;
        var minCoverage = ctx.MinCoverage ?? r.Config.MinCoverage;
        var strict = ctx.StrictPalette ?? r.Config.StrictPalette;
        var (verdict, verdictClass) = ctx.ExitCode switch
        {
            0 => ("PASS", "v-pass"),
            1 => ("FAIL", "v-fail"),
            3 => ("NOT TRUSTED", "v-untrusted"),
            _ => ((string?)null, ""),
        };

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        // script、外部資源、表單、base 一律禁止；只允許行內樣式（色塊的顏色）
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">\n");
        sb.Append("<meta name=\"color-scheme\" content=\"light dark\">\n<meta name=\"referrer\" content=\"no-referrer\">\n");
        sb.Append(string.Create(inv, $"<title>XamlContrast — {(verdict is null ? "" : verdict + " · ")}{fail} fail, {warn} warn</title>\n"));
        sb.Append("<style>").Append(HtmlCss).Append("</style>\n</head>\n<body>\n<div class=\"wrap\">\n");

        // ── 結論 ──
        sb.Append("<header>\n<p class=\"tool\">XamlContrast · static WCAG contrast audit for XAML source</p>\n");
        sb.Append(string.Create(inv, $"<h1>{(verdict is null ? "" : $"<span class=\"verdict {verdictClass}\">{verdict}</span> ")}{fail} fail · {warn} warn</h1>\n"));
        sb.Append("<p class=\"lede\">").Append(H(VerdictText(r, ctx, failOn, minCoverage, strict)))
          .Append(string.Create(inv, $" {r.Pairs} text-on-background pair(s) across {r.FileCount} file(s), coverage {r.CoveragePercent:F1}% of pairs seen.</p>\n"));
        sb.Append("</header>\n<main>\n");

        // ── 退化與警告放最前面（同 Markdown）：下面的數字要先知道這些才讀得對 ──
        if (r.Detection.IsDegraded)
            Alert(sb, "Palette detection degraded", r.Detection.Description +
                " — only hard-coded colours were evaluated; theme-driven colours are not covered by this run.");
        if (r.Pairs == 0) Alert(sb, "Zero pairs resolved", "nothing was audited. An empty scan is not a pass.");
        if (!r.Config.Thresholds.IsDefault)
        {
            var t = r.Config.Thresholds;
            Alert(sb, "Non-default thresholds", string.Create(inv,
                $"normal {t.NormalText}, large {t.LargeText}, fail-factor {t.FailFactor} — the audit standard for this run is not the WCAG default."));
        }
        foreach (var n in r.UnmatchedLayoutNames)
            Alert(sb, "Config: layout name matched no element", $"'{n}' — names are matched exactly (case-sensitive, without a namespace prefix).");
        foreach (var e in r.ParseErrors) Alert(sb, "Parse failed", e + " — this file is missing from the audit below.");
        foreach (var w in r.InvalidIgnores) Alert(sb, "Ignore comment not applied", w);
        if (ctx.ExitCode == 3 && CoverageHint(r) is { } hint)
            sb.Append("<div class=\"alert\"><strong>Hint</strong><pre class=\"hint\">").Append(H(hint.Trim())).Append("</pre></div>\n");

        Summary(sb, r, ctx, failOn, minCoverage, strict);

        // ── fail／warn：每一筆一張卡，兩個主題各一行色塊 ──
        foreach (var cat in (ReadOnlySpan<Category>)[Category.Fail, Category.Warn])
        {
            var group = r.Findings.Where(f => f.Category == cat).OrderBy(f => f.Worst).ToList();
            var label = CategoryLabel(cat);
            sb.Append(string.Create(inv, $"<section id=\"{label}\">\n<h2>{label} ({group.Count})</h2>\n"));
            if (group.Count == 0) sb.Append("<p class=\"none\">No pairs in this category.</p>\n");
            foreach (var f in group) FindingCard(sb, f, ctx);
            sb.Append("</section>\n");
        }
        if (fail + warn > 0)
            sb.Append("<p class=\"f-meta\">both-low = the palette itself is too weak, switching theme won't save it; " +
                      "dark-fails / light-fails = the design intent didn't survive the other theme.</p>\n");

        // ── 工具沒看到的地方：逐筆（位置、原因、卡住的值）——「漏了多少」之外還要「在哪裡」──
        sb.Append(string.Create(inv, $"<section id=\"unresolved\">\n<h2>unresolved ({r.UnresolvedSites.Count})</h2>\n"));
        if (r.UnresolvedSites.Count == 0) sb.Append("<p class=\"none\">Every pair the tool saw was resolved.</p>\n");
        else
        {
            sb.Append("<p class=\"f-meta\">Pairs the tool could not resolve — not graded, and not counted as passing. " +
                      "The value is what resolution got stuck on (the background's value when the background is the problem).</p>\n");
            sb.Append(string.Create(inv, $"<details{(r.UnresolvedSites.Count <= 50 ? " open" : "")}><summary>{r.UnresolvedSites.Count} unresolved pair(s)</summary>\n"));
            sb.Append("<div class=\"scroll\"><table>\n<thead><tr><th>Reason</th><th>Location</th><th>Value</th></tr></thead>\n<tbody>\n");
            foreach (var s in r.UnresolvedSites.OrderBy(s => s.Reason).ThenBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line))
                sb.Append(string.Create(inv,
                    $"<tr><td><code>{ReasonLabel(s.Reason)}</code></td><td><code>{H($"{s.File}:{s.Line}")}</code></td><td><code>{H(s.Value)}</code></td></tr>\n"));
            sb.Append("</tbody></table></div>\n</details>\n");
        }
        sb.Append("</section>\n");

        // ── ok／decorative：全列，收合（數量大；要看才打開）──
        foreach (var (cat, what) in new[] { (Category.Ok, "passing pairs"), (Category.Decorative, "decorative elements — no text contrast minimum") })
        {
            var group = r.Findings.Where(f => f.Category == cat)
                .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();
            var label = CategoryLabel(cat);
            sb.Append(string.Create(inv, $"<section id=\"{label}\">\n<h2>{label} ({group.Count})</h2>\n"));
            if (group.Count == 0) { sb.Append("<p class=\"none\">No pairs in this category.</p>\n</section>\n"); continue; }
            sb.Append(string.Create(inv, $"<details><summary>{group.Count} {what}</summary>\n<div class=\"scroll\"><table>\n"));
            sb.Append("<thead><tr><th>Location</th><th>Element</th><th>Foreground → background (as written)</th><th>Dark</th><th>Light</th><th>Need</th></tr></thead>\n<tbody>\n");
            foreach (var f in group)
            {
                sb.Append(string.Create(inv,
                    $"<tr><td><code>{H($"{f.File}:{f.Line}")}</code></td><td><code>{H(f.Element)}</code></td>" +
                    $"<td><code>{H(f.Fg)}</code> → <code>{H(f.Bg)}</code></td>"));
                foreach (var side in (ReadOnlySpan<ThemeSide>)[ThemeSide.Dark, ThemeSide.Light])
                {
                    sb.Append("<td class=\"num\">");
                    if (f.RatioIn(side) is not { } ratio) sb.Append(H($"n/a ({ThemeLabel(f.Theme!.Value)} theme only)"));
                    else
                    {
                        Swatches(sb, f.ColorsIn(side));
                        sb.Append(string.Create(inv, $"{ratio}:1"));
                    }
                    sb.Append("</td>");
                }
                sb.Append(string.Create(inv, $"<td class=\"num\">{NeedText(f)}</td></tr>\n"));
            }
            sb.Append("</tbody></table></div>\n</details>\n</section>\n");
        }

        sb.Append("</main>\n<footer>");
        sb.Append(H($"Generated by XamlContrast {ctx.ToolVersion ?? ""}".TrimEnd()));
        sb.Append(" · The colours shown are the opaque values each ratio was computed on: translucent layers and Opacity are already composited. " +
                  "Machine-readable data: the --json report. https://github.com/HSU-YU-MING/cornhsu-xamlcontrast</footer>\n");
        sb.Append("</div>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>結論那一行的說明（退出碼為什麼是這個）。</summary>
    private static string VerdictText(AuditResult r, ReportContext ctx, string failOn, double minCoverage, bool strict)
    {
        var inv = CultureInfo.InvariantCulture;
        var warn = r.CountOf(Category.Warn);
        var baseline = ctx.BaselineStatus is not null;
        return ctx.ExitCode switch
        {
            0 when baseline => "exit 0 — no new or worsened pairs compared with the baseline.",
            0 when warn > 0 => string.Create(inv, $"exit 0 — no fail; {warn} pair(s) below AA are let through by the gate (fail on: {failOn})."),
            0 => "exit 0 — every pair meets the gate.",
            1 when baseline => "exit 1 — new or worsened pairs compared with the baseline.",
            1 => failOn == "warn" ? "exit 1 — pairs below AA (the gate fails the run on any pair below AA)."
                                  : "exit 1 — pairs below the fail line (threshold × fail factor).",
            3 when r.Pairs == 0 => "exit 3 — the result cannot be trusted: 0 pairs resolved, nothing was audited.",
            3 when strict && r.Detection.IsDegraded => "exit 3 — the result cannot be trusted: palette detection degraded and strict palette is on.",
            3 => string.Create(inv, $"exit 3 — the result cannot be trusted: only {r.CoveragePercent:F1}% of colour pairs could be resolved, below the {minCoverage}% floor."),
            _ => "",
        };
    }

    private static void Summary(StringBuilder sb, AuditResult r, ReportContext ctx, string failOn, double minCoverage, bool strict)
    {
        var inv = CultureInfo.InvariantCulture;
        var t = r.Config.Thresholds;
        sb.Append("<section id=\"summary\">\n<h2>Summary</h2>\n<div class=\"cards\">\n");

        Card(sb, "Results", [
            ("<span class=\"badge b-fail\">fail</span>", Num(r.CountOf(Category.Fail))),
            ("<span class=\"badge b-warn\">warn</span>", Num(r.CountOf(Category.Warn))),
            ("<span class=\"badge b-ok\">ok</span>", Num(r.CountOf(Category.Ok))),
            ("<span class=\"badge b-decorative\">decorative</span>", Num(r.CountOf(Category.Decorative))),
        ]);
        Card(sb, "Coverage", [
            ("files", Num(r.FileCount)),
            ("text-on-background pairs", Num(r.Pairs)),
            ("coverage of pairs seen", string.Create(inv, $"{r.CoveragePercent:F1}%")),
            ("minimum coverage", string.Create(inv, $"{minCoverage}%")),
        ]);
        Card(sb, "Gate", [
            ("fail on", H(failOn)),
            ("strict palette", strict ? "on" : "off"),
            ("normal text", string.Create(inv, $"{t.NormalText}:1")),
            ("large text", string.Create(inv, $"{t.LargeText}:1")),
            ("fail factor", string.Create(inv, $"{t.FailFactor}")),
            ("thresholds", t.IsDefault ? "WCAG default" : "<strong>NON-DEFAULT</strong>"),
        ], failOn == "warn" ? "fail on warn: every pair below AA fails the run."
                            : "fail on fail: only pairs below the fail line (threshold × fail factor) fail the run.");
        Card(sb, "Palette", [
            ("mode", H(ModeLabel(r.Detection.Mode))),
            ("keys", Num(r.Detection.Palette.Count)),
            ("single theme", r.Detection.Palette.IsSingleTheme ? "yes" : "no"),
            ("degraded", r.Detection.IsDegraded ? "<strong>YES</strong>" : "no"),
            ("config file", r.Config.SourcePath is not null ? "loaded" : "none"),
        ], r.Detection.Description);

        // 「沒評到的東西」一律列、是 0 也列（同 console 與 Markdown 的立場：不印 0 就分不出「沒有」與「沒算」）
        var notEvaluated = new List<(string, string)>
        {
            ("unresolved (runtime colour, gradient, unknown key…)", Num(r.Unresolved)),
            ("skipped (invisible: transparent or opacity 0)", Num(r.Skipped)),
            ("excluded (dead Foreground setter)", Num(r.DeadForeground)),
            ("exempted (IsEnabled=False; WCAG 1.4.3)", Num(r.DisabledExempt)),
            ("suppressed via xamlcontrast-ignore", Num(r.Suppressed)),
            ("parse errors", Num(r.ParseErrors.Count)),
            ("invalid ignore comments", Num(r.InvalidIgnores.Count)),
        };
        var reasons = Reasons(r).ToList();
        Card(sb, "Not evaluated", notEvaluated, reasons.Count == 0 ? null
            : "unresolved by reason: " + string.Join(", ", reasons.Select(kv => string.Create(inv, $"{ReasonLabel(kv.Key)} {kv.Value}"))));

        var unknown = r.UnresolvedSites.Where(s => s.Reason == UnresolvedReason.UnknownPaletteKey)
            .GroupBy(s => s.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            Card(sb, "Unknown palette keys", unknown.Select(g => (
                    $"<code>{H(g.Key)}</code>{(r.Detection.DroppedKeys.Contains(g.Key) ? " (dropped: unreadable definition)" : "")}",
                    Num(g.Count()))).ToList(),
                "each one is a dead reference, a typo, or a palette file detection missed.");

        if (ctx.BaselineStatus is { } status)
        {
            int Of(string s) => status.Values.Count(v => v == s);
            Card(sb, "Baseline", [("new", Num(Of("new"))), ("worsened", Num(Of("worsened"))), ("known debt", Num(Of("known")))],
                "the gate blocks only new and worsened pairs; known debt may only shrink.");
        }
        sb.Append("</div>\n</section>\n");
    }

    /// <summary>一張摘要卡：標題、左右兩欄的 dl、底下一段說明。⚠ 兩欄都是 HTML（呼叫端負責跳脫：常數標籤不含特殊字元，
    /// 來自掃描內容的一律先經 <see cref="H"/>）；標題與說明是純文字，這裡跳脫。</summary>
    private static void Card(StringBuilder sb, string title, IEnumerable<(string LabelHtml, string ValueHtml)> rows, string? detail = null)
    {
        sb.Append("<div class=\"card\"><h3>").Append(H(title)).Append("</h3><dl>");
        foreach (var (label, value) in rows)
            sb.Append("<dt>").Append(label).Append("</dt><dd>").Append(value).Append("</dd>");
        sb.Append("</dl>");
        if (detail is not null) sb.Append("<p class=\"detail\">").Append(H(detail)).Append("</p>");
        sb.Append("</div>\n");
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static void Alert(StringBuilder sb, string title, string text)
        => sb.Append("<div class=\"alert\" role=\"note\"><strong>Warning: ").Append(H(title)).Append("</strong> — ").Append(H(text)).Append("</div>\n");

    /// <summary>fail／warn 的一張卡：等級（文字）、位置、元素、原始寫法、兩個主題的色塊與比值、門檻與對稱。</summary>
    private static void FindingCard(StringBuilder sb, Finding f, ReportContext ctx)
    {
        var inv = CultureInfo.InvariantCulture;
        var label = CategoryLabel(f.Category);
        sb.Append(string.Create(inv, $"<article class=\"finding {label}\">\n<div class=\"f-head\"><span class=\"badge b-{label}\">{label}</span> "));
        sb.Append("<code>").Append(H($"{f.File}:{f.Line}")).Append("</code> <code>").Append(H(f.Element)).Append("</code>");
        if (ctx.BaselineStatus is { } st && st.TryGetValue(f, out var b)) sb.Append(" <span class=\"f-meta\">baseline: ").Append(H(b)).Append("</span>");
        sb.Append("</div>\n<div class=\"f-raw\">fg <code>").Append(H(f.Fg)).Append("</code> → bg <code>").Append(H(f.Bg)).Append("</code></div>\n");
        sb.Append("<div class=\"themes\">\n");
        foreach (var side in (ReadOnlySpan<ThemeSide>)[ThemeSide.Dark, ThemeSide.Light])
        {
            var name = side == ThemeSide.Dark ? "Dark" : "Light";
            sb.Append("<div class=\"theme\"><span class=\"tname\">").Append(name).Append("</span>");
            if (f.RatioIn(side) is not { } ratio)
            {
                // 只存在於一個主題的配對：不印一個不存在的主題的比值或顏色
                sb.Append("<span class=\"hex\">").Append(H($"n/a ({ThemeLabel(f.Theme!.Value)} theme only)")).Append("</span></div>\n");
                continue;
            }
            var c = f.ColorsIn(side);
            Swatches(sb, c);
            if (c is not null && CssHex(c.Fg) is { } fg && CssHex(c.Bg) is { } bg)
                sb.Append($"<span class=\"sample\" style=\"color:{fg};background:{bg}\">Aa</span>");
            sb.Append(string.Create(inv, $"<span class=\"ratio\">{ratio}:1</span>"));
            if (c is not null) sb.Append("<span class=\"hex mono\">").Append(H($"{c.Fg} on {c.Bg}")).Append("</span>");
            sb.Append(string.Create(inv, $"<span class=\"f-meta\">{(ratio >= f.Need ? "meets" : "below")} {f.Need}</span></div>\n"));
        }
        sb.Append("</div>\n<div class=\"f-meta\">");
        var meta = new List<string> { string.Create(inv, $"need {f.Need}:1") };
        if (f.Large) meta.Add($"large text {f.Size}".TrimEnd());
        else if (f.Size.Length > 0) meta.Add(f.Size);
        meta.Add(SymmetryLabel(f.Symmetry));
        sb.Append(H(string.Join(" · ", meta))).Append("</div>\n</article>\n");
    }

    /// <summary>字色、底色兩個色塊（純裝飾：旁邊一定有色碼的文字，aria-hidden）。顏色不是合法的 #RRGGBB 就不畫 —— style 屬性裡不放別的東西。</summary>
    private static void Swatches(StringBuilder sb, ContrastColors? c)
    {
        if (c is null || CssHex(c.Fg) is not { } fg || CssHex(c.Bg) is not { } bg) return;
        sb.Append($"<span class=\"sw\" style=\"background:{fg}\" aria-hidden=\"true\"></span><span class=\"sw\" style=\"background:{bg}\" aria-hidden=\"true\"></span> ");
    }

    private static string NeedText(Finding f)
        => f.Need <= 0 ? "—" : string.Create(CultureInfo.InvariantCulture, $"{f.Need}:1{(f.Large ? " (large)" : "")}");

    /// <summary>只收 #RRGGBB（<see cref="Wcag.Opaque"/> 的輸出）；其他一律 null —— 進 style 屬性的值不能是任意字串。</summary>
    internal static string? CssHex(string? hex)
        => hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(char.IsAsciiHexDigit) ? hex : null;

    /// <summary>
    /// HTML 的文字（元素內容與屬性值都安全）：先把控制字元、U+2028／2029 與格式字元（雙向覆寫 U+202E 可以讓檔名在畫面上倒著顯示）
    /// 換成看得見的 \n、\uXXXX —— 同 <see cref="ConsoleSafe"/> 的立場，報告裡的文字只能是資料，而且要看得見；
    /// 再跳脫 &amp; &lt; &gt; &quot; '。
    /// </summary>
    internal static string H(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            if ((char.IsControl(c) && c != '\t') || c is (char)0x2028 or (char)0x2029 || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                sb.Append(c switch { '\n' => "\\n", '\r' => "\\r", _ => $"\\u{(int)c:X4}" });
                continue;
            }
            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    // 色彩 token 在 :root，深色模式整組換掉。報告自己的文字配色也要過 AA（一個對比度工具的報告自己不及格就笑話了）
    private const string HtmlCss = """
        :root{--bg:#f6f7f9;--surface:#ffffff;--text:#1b1f24;--muted:#57606a;--line:#d0d7de;--code:#eef1f4;
        --fail:#b42318;--fail-bg:#fdeceb;--warn:#8a4b00;--warn-bg:#fff4dc;--ok:#116329;--ok-bg:#e8f5ec;
        --untrusted:#6e3fc2;--untrusted-bg:#f1ebfc}
        @media (prefers-color-scheme:dark){:root{--bg:#0e1116;--surface:#161b22;--text:#e6edf3;--muted:#9da7b3;--line:#30363d;--code:#21262d;
        --fail:#ff8f85;--fail-bg:#3d1613;--warn:#f5c35b;--warn-bg:#352709;--ok:#7ee2a0;--ok-bg:#0f2e1b;
        --untrusted:#c8a8ff;--untrusted-bg:#2a1d47}}
        *{box-sizing:border-box}
        html{-webkit-text-size-adjust:100%}
        body{margin:0;background:var(--bg);color:var(--text);font:15px/1.55 system-ui,-apple-system,"Segoe UI",Roboto,"Noto Sans","Noto Sans TC","Microsoft JhengHei",sans-serif}
        .wrap{max-width:1120px;margin:0 auto;padding:24px 16px 48px}
        h1{font-size:1.6rem;line-height:1.3;margin:.2rem 0 .4rem}
        h2{font-size:1.2rem;margin:2rem 0 .6rem;padding-bottom:.3rem;border-bottom:1px solid var(--line)}
        h3{font-size:.95rem;margin:0 0 .5rem}
        .tool{color:var(--muted);margin:0;font-size:.9rem}
        .lede{margin:.2rem 0 0}
        code,.mono{font-family:ui-monospace,SFMono-Regular,"Cascadia Mono",Consolas,"Liberation Mono",monospace;font-size:.86em}
        code{background:var(--code);padding:.05em .35em;border-radius:4px;overflow-wrap:anywhere}
        .verdict,.badge{display:inline-block;font:700 .78rem/1.6 ui-monospace,SFMono-Regular,Consolas,monospace;letter-spacing:.04em;text-transform:uppercase;padding:0 .5em;border-radius:4px;border:1px solid currentColor;vertical-align:.15em}
        .verdict{font-size:.95rem;padding:.05em .6em}
        .v-pass,.b-ok{color:var(--ok);background:var(--ok-bg)}
        .v-fail,.b-fail{color:var(--fail);background:var(--fail-bg)}
        .b-warn{color:var(--warn);background:var(--warn-bg)}
        .v-untrusted{color:var(--untrusted);background:var(--untrusted-bg)}
        .b-decorative{color:var(--muted);background:var(--code)}
        .alert{border:1px solid var(--line);border-left:4px solid var(--warn);background:var(--warn-bg);padding:.6rem .8rem;border-radius:6px;margin:.6rem 0;overflow-wrap:anywhere}
        .alert strong{color:var(--warn)}
        pre.hint{white-space:pre-wrap;overflow-wrap:anywhere;margin:.4rem 0 0;font-size:.85rem}
        .cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,250px),1fr));gap:12px}
        .card{background:var(--surface);border:1px solid var(--line);border-radius:8px;padding:.8rem 1rem;min-width:0}
        dl{display:grid;grid-template-columns:1fr auto;gap:.15rem 1rem;margin:0}
        dt{color:var(--muted)}
        dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
        .detail{color:var(--muted);font-size:.85rem;margin:.6rem 0 0;overflow-wrap:anywhere}
        .finding{background:var(--surface);border:1px solid var(--line);border-left:4px solid var(--line);border-radius:8px;padding:.75rem 1rem;margin:.6rem 0}
        .finding.fail{border-left-color:var(--fail)}
        .finding.warn{border-left-color:var(--warn)}
        .f-head{display:flex;flex-wrap:wrap;gap:.4rem .6rem;align-items:baseline}
        .f-raw{margin:.35rem 0;color:var(--muted);overflow-wrap:anywhere}
        .themes{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,300px),1fr));gap:.4rem 1.2rem;margin:.5rem 0}
        .theme{display:flex;flex-wrap:wrap;align-items:center;gap:.45rem}
        .tname{min-width:3.2em;color:var(--muted)}
        .sw{display:inline-block;width:1.15rem;height:1.15rem;border-radius:4px;box-shadow:inset 0 0 0 1px rgba(128,128,128,.6);vertical-align:middle}
        td .sw{width:.9rem;height:.9rem;margin-right:.15rem}
        .sample{display:inline-block;padding:.05rem .55rem;border-radius:4px;font-weight:600;box-shadow:inset 0 0 0 1px rgba(128,128,128,.5)}
        .ratio{font-weight:700;font-variant-numeric:tabular-nums}
        .hex{color:var(--muted)}
        .f-meta{color:var(--muted);font-size:.88rem}
        .none{color:var(--muted)}
        details{background:var(--surface);border:1px solid var(--line);border-radius:8px;padding:.5rem .9rem;margin:.6rem 0}
        summary{cursor:pointer;font-weight:600}
        .scroll{overflow-x:auto;margin-top:.5rem}
        table{border-collapse:collapse;width:100%;font-size:.86rem}
        th,td{text-align:left;vertical-align:top;padding:.35rem .5rem;border-bottom:1px solid var(--line)}
        th{color:var(--muted);font-weight:600}
        td.num{font-variant-numeric:tabular-nums;white-space:nowrap}
        footer{color:var(--muted);font-size:.85rem;margin-top:2.5rem;border-top:1px solid var(--line);padding-top:.8rem;overflow-wrap:anywhere}
        """;
}

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>
/// XAML 樹解析式對比度稽核。對每個有文字色的元素，沿父節點往上找
/// 「最近一個真正生效的背景色」，再把兩邊的資源鍵各自代入深淺兩套值算 WCAG 對比。
///
/// 處理的情況（八條解析規則，一條都不能少 —— 全部是真實專案踩出來的）：
///   1. 元素屬性上的 Background / Foreground（含祖先繼承）
///   2. Background="Transparent" 視為穿透，繼續往上找
///   3. 背景是半透明 ARGB → 與下層合成
///   4. Opacity 沿樹累乘（Border 0.5 裡的 TextBlock 0.5 = 0.25）
///   5. ControlTemplate 內部自成一個子樹（模板根的背景管到模板內的文字）
///   6. Style 裡同時有 Background 與 Foreground 兩個 Setter → 一組配對
///   7. Trigger / DataTrigger 內的 Setter：以所屬 Style 的另一半為對照
///   8. Style 自帶 ControlTemplate 且模板內無 Foreground 消費者 → 死 setter，
///      整組排除並計數回報（QuillNest DataGridCheckBoxStyle 誤報案例）
///
/// 明確不處理（標成「無法解析」而不是猜 —— 不猜比猜錯好）：
///   - 只有單邊 Setter 的 Style（套用位置由型別決定，不在樹上）
///   - TemplateBinding / Binding 來的顏色
///   - 屬性元素語法 &lt;Border.Background&gt;&lt;LinearGradientBrush/&gt;…
/// </summary>
public sealed partial class Auditor
{
    // ── 文字／裝飾分類表（Walk 與 WalkStyles 共用）──
    // Rectangle 當分隔線、ProgressBar 當進度條、Ellipse 當圓點，都是裝飾元素，
    // 用文字標準去要求它們是錯的（分隔線本來就該低調）。
    private static readonly HashSet<string> TextEls = new()
    {
        "TextBlock", "Run", "Label", "AccessText", "TextBox", "PasswordBox",
        "RichTextBox", "Hyperlink", "MenuItem", "CheckBox", "RadioButton",
        "ComboBox", "ComboBoxItem", "Expander", "DataGridTextColumn", "Button",
    };

    // ⚠ 有些控制項的 Foreground 根本不是文字：ProgressBar 的 Foreground 是進度條填色、
    //   Shape 類的是圖形本身。只看「有沒有 Foreground」會把它們全誤判成文字，
    //   然後用 4.5 去要求一條進度條。先排除，再看元素型別。
    private static readonly HashSet<string> NonTextEls = new()
    {
        "ProgressBar", "Rectangle", "Ellipse", "Path", "Polygon", "Polyline",
        "Line", "Border", "Separator", "Slider", "Thumb", "Track", "ScrollBar",
    };

    private static readonly HashSet<string> StyleishEls = new()
    {
        "Style", "Setter", "Trigger", "DataTrigger", "MultiTrigger",
    };

    /// <summary>模板裡會渲染 Foreground 的元素。⚠ 不能只看 ContentPresenter：
    /// Foreground 是繼承屬性，模板裡一顆沒指定 Foreground 的裸 TextBlock 一樣會繼承到。</summary>
    private static readonly HashSet<string> FgConsumers = new()
    {
        "ContentPresenter", "ContentControl", "TextBlock", "AccessText", "Label",
    };

    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    [GeneratedRegex(@"(?<t>\w+)\s*\}?\s*$")]
    private static partial Regex TargetTypeName();

    [GeneratedRegex(@"TemplateBinding\s+Foreground")]
    private static partial Regex TemplateBindingFg();

    private readonly Palette _pal;
    private readonly List<Finding> _findings = new();
    private readonly List<string> _parseErrors = new();
    private int _pairs, _unresolved, _skipped, _deadFg;

    /// <summary>XAML 數值屬性一律以 InvariantCulture 解析 ——
    /// 否則德語系等 locale 的 CI 上 Opacity="0.5" 會靜默解析失敗。</summary>
    private static bool ParseDouble(string s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private Auditor(Palette palette) => _pal = palette;

    public static AuditResult Run(string root, PaletteDetection detection)
    {
        var auditor = new Auditor(detection.Palette);
        var files = PaletteDetector.EnumerateFiles(root, "*.xaml")
            .Where(f => !detection.ExcludedFiles.Contains(f))
            .ToList();

        foreach (var f in files)
        {
            XDocument doc;
            var name = Path.GetFileName(f);
            try { doc = XDocument.Load(f, LoadOptions.SetLineInfo); }
            catch (Exception ex)
            {
                // 解析失敗不能靜默跳過 —— 檔案從報告裡消失就是靜默退化（規劃書 8.1 規則 3）
                auditor._parseErrors.Add($"{name}: {ex.Message}");
                continue;
            }
            if (doc.Root is not null)
            {
                auditor.Walk(doc.Root, null, name, 1.0);
                auditor.WalkStyles(doc, name);
            }
        }

        var result = new AuditResult
        {
            Detection = detection,
            FileCount = files.Count,
            Findings = auditor._findings,
            Pairs = auditor._pairs,
            Unresolved = auditor._unresolved,
            Skipped = auditor._skipped,
            DeadForeground = auditor._deadFg,
            ParseErrors = auditor._parseErrors,
        };
        Grader.Grade(result);
        return result;
    }

    // 遞迴走訪：bg 是目前生效的背景（最後一個非 transparent 的）。
    //
    // op = 累積的 Opacity。WPF 的 Opacity 會往下乘到所有子元素，所以要一路帶著走。
    //
    // ⚠ 2026-07-30 開 App 實測才發現的盲區：CelFlow 畫布的提示文字沒指定 Foreground
    //   （繼承 Fg0 #EEEEEE），但有 Opacity="0.3"，螢幕上取樣到的是 #535353（2.45:1）。
    //   工具當時只看 Foreground 的色票值，回報「Fg0 = 16.28 ✓」—— 完全漏掉。
    //   Opacity 是繼「Foreground 色票」「背景 alpha」之後第三種讓文字變暗的方式。
    private void Walk(XElement el, Resolved? bg, string file, double op)
    {
        var opRaw = el.Attribute("Opacity")?.Value;
        // Opacity 也可能是 Binding；只處理字面數值，其餘維持原值
        if (opRaw is not null && ParseDouble(opRaw, out var opVal)) op *= opVal;

        var localBgRaw = el.Attribute("Background")?.Value;
        if (localBgRaw is not null)
        {
            var r = ColorResolver.Resolve(localBgRaw, _pal);
            if (r is not null)
            {
                if (r.Kind == ColorKind.Alpha)
                {
                    // 半透明遮罩：疊在目前生效的背景上算出實際顏色（WPF 以 sRGB 混合）
                    if (bg is { Dark: not null, Light: not null })
                    {
                        bg = new Resolved(ColorKind.Hard,
                            Dark: Wcag.Composite(r.Rgb!, r.Alpha, bg.Dark),
                            Light: Wcag.Composite(r.Rgb!, r.Alpha, bg.Light),
                            Key: $"{localBgRaw} over {(bg.Key is not null ? "{" + bg.Key + "}" : bg.Dark)}");
                    }
                    // 找不到底色就維持原本的（無法判定，後面會計入 unresolved）
                }
                else if (r.Kind == ColorKind.Other)
                {
                    // 底色來自 Binding / TemplateBinding / 漸層 —— 執行期才知道實際顏色。
                    // 關鍵：不能「往上繼承祖先背景」，那會把子元素的文字色配到錯的底色上
                    // （例：標籤徽章的底色綁使用者選的顏色，白字是對的，但繼承祖先會誤判成白配白）。
                    // 標記為無法判定，子元素一律跳過。
                    bg = new Resolved(ColorKind.Other);
                }
                else if (r.Kind != ColorKind.Transparent)
                {
                    bg = r;
                }
            }
        }

        foreach (var attrName in (ReadOnlySpan<string>)["Foreground", "Fill"])
        {
            var fgRaw = el.Attribute(attrName)?.Value;
            if (fgRaw is null) continue;
            var fg = ColorResolver.Resolve(fgRaw, _pal);
            if (fg is null) continue;
            if (fg.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.Transparent or ColorKind.UnknownKey)
            { _skipped++; continue; }
            if (bg is null) { _unresolved++; continue; }
            if (bg.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.UnknownKey)
            { _unresolved++; continue; }

            // Opacity 0 = 元素當下完全隱形。XAML 裡寫 Opacity="0" 幾乎都是淡入動畫的
            // 初始狀態，穩態是 1。對「看不見的東西」算對比沒有意義，跳過而不是報 1:1。
            if (op < 0.01) { _skipped++; continue; }

            _pairs++;

            var line = el is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

            // ── 這是文字還是裝飾？WCAG 的 4.5:1 只管文字 ──
            var localName = el.Name.LocalName;
            var isText = !NonTextEls.Contains(localName) &&
                         (TextEls.Contains(localName) || attrName == "Foreground");

            // ── WCAG 大字級豁免：≥18pt 或 ≥14pt 粗體只需 3:1 ──
            // ⚠ WPF 的 FontSize 是「裝置獨立像素」(1/96 吋) 不是 point (1/72 吋)，
            //    換算要 ×0.75。18pt = 24px、14pt = 18.67px —— 少了這一步會把
            //    18px 的字誤判成大字級而放過它。
            var fs = 0.0;
            var fsRaw = el.Attribute("FontSize")?.Value;
            if (fsRaw is not null) ParseDouble(fsRaw, out fs);
            var fw = el.Attribute("FontWeight")?.Value;
            var bold = fw is not null && Regex.IsMatch(fw, "Bold|Black|Heavy|SemiBold");
            var pt = fs * 0.75;
            var isLarge = pt >= 18 || (bold && pt >= 14);
            var need = !isText ? 0.0 : isLarge ? 3.0 : 4.5;

            // Opacity < 1 時，螢幕上看到的是「文字色以 op 疊在背景上」的混色結果
            var fgD = fg.Dark!;
            var fgL = fg.Light!;
            if (op < 0.999)
            {
                fgD = Wcag.Composite(fg.Dark!, op, bg.Dark!);
                fgL = Wcag.Composite(fg.Light!, op, bg.Light!);
            }

            _findings.Add(new Finding
            {
                File = file,
                Line = line,
                Element = localName,
                Fg = op < 0.999 ? $"{fgRaw} ×{Math.Round(op, 2)}" : fgRaw,
                Bg = bg.Key is not null ? "{" + bg.Key + "}" : bg.Dark!,
                Mixed = fg.Kind != bg.Kind,
                RatioDark = Wcag.Contrast(fgD, bg.Dark!),
                RatioLight = Wcag.Contrast(fgL, bg.Light!),
                IsText = isText,
                Need = need,
                Size = fs > 0 ? $"{(int)Math.Round(fs, MidpointRounding.ToEven)}px" : "",
                Large = isLarge,
            });
        }

        foreach (var child in el.Elements())
        {
            // Style / Setter / Trigger 這些不是視覺樹，個別處理（WalkStyles），這裡跳過
            if (StyleishEls.Contains(child.Name.LocalName)) continue;
            Walk(child, bg, file, op);
        }
    }

    // Style 裡成對的 Setter（Background + Foreground 同時存在）
    private void WalkStyles(XDocument doc, string file)
    {
        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            var setters = new Dictionary<string, string>();
            foreach (var s in style.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                var prop = s.Attribute("Property")?.Value;
                var val = s.Attribute("Value")?.Value;
                if (prop is not null && val is not null) setters[prop] = val;
            }

            // 觸發器裡的 Setter 也算進來（它們覆蓋同一個 Style 的基礎值）
            var trigSetters = new List<Dictionary<string, string>>();
            foreach (var t in style.Descendants().Where(e => e.Name.LocalName is "Trigger" or "DataTrigger"))
            {
                var ts = new Dictionary<string, string>();
                foreach (var s in t.Elements().Where(e => e.Name.LocalName == "Setter"))
                {
                    var prop = s.Attribute("Property")?.Value;
                    var val = s.Attribute("Value")?.Value;
                    if (prop is not null && val is not null) ts[prop] = val;
                }
                if (ts.Count > 0) trigSetters.Add(ts);
            }

            // ── 規則 8，誤報過濾：模板沒有 Foreground 消費者 → Style 層 Foreground 是死 setter ──
            // 2026-07-30 QuillNest DataGridCheckBoxStyle 實證：ControlTemplate 把 CheckBox
            // 視覺整個換掉，只剩 Border＋寫死白色的勾勾 Path，沒有 ContentPresenter →
            // Style 的 Foreground 不會被任何元素渲染，卻被拿去配模板觸發器裡勾選框的
            // Background，報出假警報。一個消費者都沒有時，這個 Style 所有含 Foreground
            // 的配對（基礎＋觸發態）都不成立 —— 但排除要計數回報，過濾不靜默。
            var tmpl = style.Descendants().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
            var fgDead = false;
            if (tmpl is not null)
            {
                var hasConsumer = tmpl.Descendants().Any(e => FgConsumers.Contains(e.Name.LocalName))
                                  || TemplateBindingFg().IsMatch(tmpl.ToString());
                fgDead = !hasConsumer;
            }

            var key = style.Attribute(XKey)?.Value;
            var tt = style.Attribute("TargetType")?.Value;
            var label = key ?? tt ?? "(anonymous)";
            // TargetType 有 "Button" 與 "{x:Type Button}" 兩種寫法，取最後一個單字
            string? ttName = null;
            if (tt is not null)
            {
                var m = TargetTypeName().Match(tt);
                if (m.Success) ttName = m.Groups["t"].Value;
            }
            var line = style is IXmlLineInfo sli && sli.HasLineInfo() ? sli.LineNumber : 0;

            // 基礎 + 每個觸發器狀態各檢一次
            var states = new List<(string Name, Dictionary<string, string> Set)> { ("base", setters) };
            foreach (var ts in trigSetters)
            {
                var merged = new Dictionary<string, string>(setters);
                foreach (var (k, v) in ts) merged[k] = v;
                states.Add(("trigger", merged));
            }

            foreach (var (stateName, set) in states)
            {
                if (!set.TryGetValue("Foreground", out var fgv) ||
                    !set.TryGetValue("Background", out var bgv)) continue;
                if (fgDead) { _deadFg++; continue; }
                var fg = ColorResolver.Resolve(fgv, _pal);
                var bgr = ColorResolver.Resolve(bgv, _pal);
                if (fg is null || bgr is null) continue;
                if (fg.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.Transparent or ColorKind.UnknownKey) continue;
                if (bgr.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.Transparent or ColorKind.UnknownKey) continue;

                // ⚠ v0.1 的第 5 號 bug（規劃書 8.2）：這裡曾漏了 Need，而分級把
                //   「沒有 Need」當裝飾 → 所有 Style 配對被靜默歸為裝飾、完全不受檢。
                //   分類沿用排除清單：TargetType 是非文字控制項才免檢；
                //   不確定（匿名、沒 TargetType）就當文字報出來 —— 不替使用者放過。
                var isText = ttName is null || !NonTextEls.Contains(ttName);
                var fs = 0.0;
                if (set.TryGetValue("FontSize", out var fsv)) ParseDouble(fsv, out fs);
                var bold = set.TryGetValue("FontWeight", out var fwv) &&
                           Regex.IsMatch(fwv, "Bold|Black|Heavy|SemiBold");
                var pt = fs * 0.75; // WPF FontSize 是 DIP，換 point 要 ×0.75（規劃書 4.3）
                var isLarge = pt >= 18 || (bold && pt >= 14);
                var need = !isText ? 0.0 : isLarge ? 3.0 : 4.5;

                _pairs++;
                _findings.Add(new Finding
                {
                    File = file,
                    Line = line,
                    Element = $"Style[{label}]/{stateName}",
                    Fg = fgv,
                    Bg = bgr.Key is not null ? "{" + bgr.Key + "}" : bgr.Dark!,
                    Mixed = fg.Kind != bgr.Kind,
                    RatioDark = Wcag.Contrast(fg.Dark!, bgr.Dark!),
                    RatioLight = Wcag.Contrast(fg.Light!, bgr.Light!),
                    IsText = isText,
                    Need = need,
                    Size = fs > 0 ? $"{(int)Math.Round(fs, MidpointRounding.ToEven)}px" : "",
                    Large = isLarge,
                });
            }
        }
    }
}

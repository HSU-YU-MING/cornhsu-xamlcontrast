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
        "Style", "Setter", "Trigger", "DataTrigger", "MultiTrigger", "MultiDataTrigger",
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

    // WCAG 大字級的「粗體」指 weight ≥ 700：Bold(700)、ExtraBold/UltraBold(800)、
    // Black/Heavy(900)、ExtraBlack/UltraBlack(950)、數字 700–999。
    // SemiBold/DemiBold 是 600，不算 —— 舊版子字串匹配（"Bold" 命中 "SemiBold"）
    // 把 600 也放寬到 3:1，會漏掉 3.0~4.5 之間真正不合格的文字。錨定整字匹配。
    [GeneratedRegex(@"^\s*(?:Bold|ExtraBold|UltraBold|Black|ExtraBlack|UltraBlack|Heavy|[7-9]\d{2})\s*$")]
    private static partial Regex BoldWeight();

    private static bool IsBold(string? fw) => fw is not null && BoldWeight().IsMatch(fw);

    private readonly Palette _pal;
    private readonly ToolConfig _cfg;
    private readonly HashSet<string> _textEls;
    private readonly HashSet<string> _nonTextEls;
    private readonly List<Finding> _findings = new();
    private readonly List<string> _parseErrors = new();
    private readonly List<string> _invalidIgnores = new();
    private int _pairs, _unresolved, _skipped, _deadFg, _suppressed, _disabled;
    private StyleIndex _styles = null!;
    private string _curFile = ""; // 具名 Style 查找的「同檔優先」範圍（完整路徑）

    [GeneratedRegex(@"^\s*xamlcontrast-ignore(?:\s*:\s*(?<reason>.*\S))?\s*$")]
    private static partial Regex IgnoreComment();

    /// <summary>
    /// ignore 註解（規劃書 4.6）：&lt;!-- xamlcontrast-ignore: 理由 --&gt; 管下一個元素（含子樹）。
    /// 理由必填 —— 沒理由的 ignore 視為無效並警告；被壓掉的計入 suppressed。
    /// 否則 ignore 就是新開的一條靜默退化通道。
    /// </summary>
    private bool IsSuppressedByComment(XElement el, string file)
    {
        // 往前找最近的非空白節點；只有「緊鄰的前一個註解」才算數
        var prev = el.PreviousNode;
        while (prev is XText t && string.IsNullOrWhiteSpace(t.Value)) prev = prev.PreviousNode;
        if (prev is not XComment c) return false;
        var m = IgnoreComment().Match(c.Value);
        if (!m.Success) return false;
        if (!m.Groups["reason"].Success && _cfg.Ignore.RequireReason)
        {
            var line = c is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
            _invalidIgnores.Add($"{file}:{line}: xamlcontrast-ignore without a reason is invalid (not suppressed)");
            return false;
        }
        return true;
    }

    /// <summary>XAML 數值屬性一律以 InvariantCulture 解析 ——
    /// 否則德語系等 locale 的 CI 上 Opacity="0.5" 會靜默解析失敗。</summary>
    private static bool ParseDouble(string s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private Auditor(Palette palette, ToolConfig cfg)
    {
        _pal = palette;
        _cfg = cfg;
        // config 的 classification 覆寫：加進內建分類表（排除清單優先於元素型別判斷）
        _textEls = new HashSet<string>(TextEls.Concat(cfg.Classification.ExtraText));
        _nonTextEls = new HashSet<string>(NonTextEls.Concat(cfg.Classification.ExtraNonText));
    }

    private double NeedFor(bool isText, bool isLarge) =>
        !isText ? 0.0 : isLarge ? _cfg.Thresholds.LargeText : _cfg.Thresholds.NormalText;

    public static AuditResult Run(string root, PaletteDetection detection, ToolConfig? config = null)
    {
        var auditor = new Auditor(detection.Palette, config ?? new ToolConfig());
        // 具名 Style 索引含色盤／主題字典 ——「排除出 UI 掃描」不等於「排除出索引」
        auditor._styles = StyleIndex.Build(root);
        var files = PaletteDetector.EnumerateFiles(root, "*.xaml")
            .Where(f => !detection.ExcludedFiles.Contains(f))
            .ToList();

        foreach (var f in files)
        {
            XDocument doc;
            // 報告用 repo 相對路徑（正斜線）：只給檔名的話，MVVM 專案的同名檔
            // （ItemView.xaml）無法定位，baseline 的鍵還會跨資料夾互相污染。
            // 正斜線讓 baseline 跨 OS 可攜（Windows 本機寫、Linux CI 讀）。
            var name = Path.GetRelativePath(root, f).Replace('\\', '/');
            try { doc = XDocument.Load(f, LoadOptions.SetLineInfo); }
            catch (Exception ex)
            {
                // 解析失敗不能靜默跳過 —— 檔案從報告裡消失就是靜默退化（規劃書 8.1 規則 3）
                auditor._parseErrors.Add($"{name}: {ex.Message}");
                continue;
            }
            if (doc.Root is not null)
            {
                auditor._curFile = f;
                auditor.Walk(doc.Root, null, name, 1.0, suppressed: false);
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
            UnresolvedBy = auditor._unresolvedBy,
            Skipped = auditor._skipped,
            DeadForeground = auditor._deadFg,
            DisabledExempt = auditor._disabled,
            ParseErrors = auditor._parseErrors,
            Suppressed = auditor._suppressed,
            InvalidIgnores = auditor._invalidIgnores,
            Config = auditor._cfg,
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
    private readonly Dictionary<UnresolvedReason, int> _unresolvedBy = new();

    /// <summary>無法解析要連「為什麼」一起記 —— 只給總數的話，使用者看不出
    /// 那 1373 組裡有 1295 組是同一個可補救的原因。</summary>
    private void Unres(UnresolvedReason why)
    {
        _unresolved++;
        _unresolvedBy[why] = _unresolvedBy.GetValueOrDefault(why) + 1;
    }

    private void Unres(ColorKind kind) => Unres(kind switch
    {
        ColorKind.UnknownKey => UnresolvedReason.UnknownPaletteKey,
        ColorKind.Alpha => UnresolvedReason.TranslucentUncomposited,
        _ => UnresolvedReason.BoundOrGradient,
    });

    private void Walk(XElement el, Resolved? bg, string file, double op, bool suppressed)
    {
        if (!suppressed && IsSuppressedByComment(el, file)) suppressed = true;
        var opRaw = el.Attribute("Opacity")?.Value;
        // Opacity 也可能是 Binding；只處理字面數值，其餘維持原值
        if (opRaw is not null && ParseDouble(opRaw, out var opVal)) op *= opVal;

        // 元素套用的 Style（具名引用或 <X.Style> 行內）：底色與觸發態的另一個來源
        var chain = _styles.GetElementChain(el, _curFile);

        // 逐狀態路徑的 alpha 合成要用「更新前」的祖先背景 —— 下面的 bg 更新塊會把
        // 半透明底先合成一次，逐狀態再合成就是疊兩層
        var ancestorBg = bg;

        var localBgRaw = el.Attribute("Background")?.Value;
        var bgVal = localBgRaw
                    ?? (chain is not null && chain.Props.TryGetValue("Background", out var chainBg) ? chainBg.V : null)
                    ?? chain?.TemplateRootBg // 模板根的背景管到內容
                    // 「地板」：根元素沒寫 Background 時，查該型別的隱含樣式（僅此一格，
                    // 不是完整的隱含樣式解析 —— 理由見 StyleIndex.ImplicitRootBackground）
                    ?? (el.Parent is null ? _styles.ImplicitRootBackground(el.Name.LocalName, _curFile) : null);
        if (bgVal is not null)
        {
            var r = ColorResolver.Resolve(bgVal, _pal);
            if (r is not null)
            {
                if (r.Kind == ColorKind.Alpha)
                {
                    // 半透明遮罩：疊在目前生效的背景上算出實際顏色（WPF 以 sRGB 混合；
                    // 半透明「色票」深淺各帶自己的 alpha）
                    if (bg is { Dark: not null, Light: not null })
                    {
                        bg = new Resolved(ColorKind.Hard,
                            Dark: Wcag.Composite(r.Rgb!, r.Alpha, bg.Dark),
                            Light: Wcag.Composite(r.RgbLight!, r.AlphaLight, bg.Light),
                            Key: $"{bgVal} over {(bg.Key is not null ? "{" + bg.Key + "}" : bg.Dark)}");
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

        // ── 元素 × Style 鏈的逐狀態檢查（具名 Style 底色盲區）──
        // CelFlow 實證：刪除鈕 Foreground=RedL 配 BasedOn=DarkBtn，DarkBtn 的底是
        // Bg3、hover Bg4 —— 樹走訪看不到 Style 的底色會誤落到祖先卡片的 Bg2，
        // 低估實況；「忽略」鈕（Fg1 疊 Border0/Border1）則整組漏掉。
        var handledFg = false;
        if (chain is not null)
        {
            handledFg = true; // Foreground 一律由這裡接手，避免與下面的舊路徑重複輸出
            var localFgRaw = el.Attribute("Foreground")?.Value;
            var emitted = new HashSet<string>();
            var allStates = new List<StyleIndex.State> { new() };
            allStates.AddRange(chain.States);
            foreach (var st in allStates)
            {
                // WPF 優先序：元素屬性 > Style 觸發 > Style Setter
                string? fgV = null; object? fgSrc = null;
                if (localFgRaw is not null) { fgV = localFgRaw; fgSrc = "local"; }
                else if (st.Set.TryGetValue("Foreground", out var sf)) { fgV = sf; fgSrc = st.SetSrc["Foreground"]; }
                else if (chain.Props.TryGetValue("Foreground", out var pf)) { fgV = pf.V; fgSrc = pf.Src; }
                if (fgV is null) continue;

                string? bgV = null; object? bgSrc = null;
                // WPF 屬性優先序的分岔：觸發器「直接設在模板根」的底（TargetName=根）
                // 蓋過經 TemplateBinding 進來的宿主本地值；「設在宿主」的觸發 Setter
                // 則輸給宿主的本地屬性（CalendarView 日曆格實證：選取態 DayBorder
                // 直接換 PrimaryButtonBrush，蓋掉 local SurfaceBrush）
                if (st.Set.TryGetValue("Background", out var rootBg) && st.RootTargeted.Contains("Background"))
                { bgV = rootBg; bgSrc = st.SetSrc["Background"]; }
                else if (localBgRaw is not null) { bgV = localBgRaw; bgSrc = "local"; }
                else if (st.Set.TryGetValue("Background", out var sBg)) { bgV = sBg; bgSrc = st.SetSrc["Background"]; }
                else if (chain.Props.TryGetValue("Background", out var pb)) { bgV = pb.V; bgSrc = pb.Src; }
                else if (chain.TemplateRootBg is not null)
                {
                    // 模板根元素自帶的背景（CellDeleteBtn 形狀）。來源標 "tmpl" ——
                    // 它不是 Style 層 setter，WalkStyles 不會在定義處檢到，這裡必須報
                    bgV = chain.TemplateRootBg; bgSrc = "tmpl";
                }

                // 字與底都出自同一個 Style 節點 → WalkStyles 在定義處已檢過，不重複
                if (fgSrc is int fi && bgSrc is int bi && fi == bi) continue;
                // 停用態：WCAG 1.4.3 豁免，計數回報、不評分
                if (st.Disabled) { _disabled++; continue; }

                var fgR = ColorResolver.Resolve(fgV, _pal);
                if (fgR is null) continue;
                // 字色是 Binding／漸層／未知鍵 = 工具看不懂 → unresolved（盲區要誠實計數，
                // 之前全塞 skipped，把盲區藏進「合法豁免」桶）；透明字才是合法跳過
                if (fgR.Kind is ColorKind.Other or ColorKind.UnknownKey)
                { Unres(fgR.Kind); continue; }
                if (fgR.Kind is ColorKind.Alpha or ColorKind.Transparent) { _skipped++; continue; }

                Resolved? bgObj;
                if (bgV is not null)
                {
                    var rb = ColorResolver.Resolve(bgV, _pal);
                    if (rb is null) continue;
                    if (rb.Kind == ColorKind.Transparent) bgObj = bg;
                    else if (rb.Kind == ColorKind.Alpha)
                    {
                        bgObj = ancestorBg is { Dark: not null, Light: not null }
                            ? new Resolved(ColorKind.Hard,
                                Dark: Wcag.Composite(rb.Rgb!, rb.Alpha, ancestorBg.Dark),
                                Light: Wcag.Composite(rb.RgbLight!, rb.AlphaLight, ancestorBg.Light),
                                Key: $"{bgV} over {(ancestorBg.Key is not null ? "{" + ancestorBg.Key + "}" : ancestorBg.Dark)}")
                            : null;
                    }
                    else if (rb.Kind is ColorKind.Other or ColorKind.UnknownKey) { Unres(rb.Kind); continue; }
                    else bgObj = rb;
                }
                else bgObj = bg; // Style 鏈沒設底 → 退回祖先背景
                if (bgObj is null) { Unres(UnresolvedReason.NoAncestorBackground); continue; }
                if (bgObj.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.UnknownKey) { Unres(bgObj.Kind); continue; }
                if (op < 0.01) { _skipped++; continue; }

                // 觸發器只動了無關屬性（如停用態只設 Opacity）→ 與基礎同組，不重複
                if (!emitted.Add($"{fgR.Dark}|{bgObj.Dark}")) continue;

                var isTextC = !_nonTextEls.Contains(el.Name.LocalName);
                var fsC = 0.0;
                var fsRawC = el.Attribute("FontSize")?.Value;
                if (fsRawC is not null) ParseDouble(fsRawC, out fsC);
                else if (st.Set.TryGetValue("FontSize", out var sfs)) ParseDouble(sfs, out fsC);
                else if (chain.Props.TryGetValue("FontSize", out var pfs)) ParseDouble(pfs.V, out fsC);
                var fwV = el.Attribute("FontWeight")?.Value
                          ?? (chain.Props.TryGetValue("FontWeight", out var pfw) ? pfw.V : null);
                var boldC = IsBold(fwV);
                var ptC = fsC * 0.75;
                var isLargeC = ptC >= 18 || (boldC && ptC >= 14);
                var needC = NeedFor(isTextC, isLargeC);

                var fgDC = fgR.Dark!;
                var fgLC = fgR.Light!;
                if (op < 0.999)
                {
                    fgDC = Wcag.Composite(fgR.Dark!, op, bgObj.Dark!);
                    fgLC = Wcag.Composite(fgR.Light!, op, bgObj.Light!);
                }

                _pairs++;
                if (suppressed) { _suppressed++; continue; }
                var lineC = el is IXmlLineInfo cli && cli.HasLineInfo() ? cli.LineNumber : 0;
                _findings.Add(new Finding
                {
                    File = file,
                    Line = lineC,
                    Element = $"{el.Name.LocalName}⊕{chain.Label}/{(st.Cond is null && st.Set.Count == 0 ? "base" : "trigger")}",
                    Fg = op < 0.999 ? $"{fgV} ×{Math.Round(op, 2)}" : fgV,
                    Bg = bgObj.Key is not null ? "{" + bgObj.Key + "}" : bgObj.Dark!,
                    Mixed = fgR.Kind != bgObj.Kind,
                    RatioDark = Wcag.Contrast(fgDC, bgObj.Dark!),
                    RatioLight = Wcag.Contrast(fgLC, bgObj.Light!),
                    IsText = isTextC,
                    Need = needC,
                    Size = fsC > 0 ? $"{(int)Math.Round(fsC, MidpointRounding.ToEven)}px" : "",
                    Large = isLargeC,
                });
            }
        }

        foreach (var attrName in (ReadOnlySpan<string>)["Foreground", "Fill"])
        {
            if (attrName == "Foreground" && handledFg) continue;
            var fgRaw = el.Attribute(attrName)?.Value;
            if (fgRaw is null) continue;
            var fg = ColorResolver.Resolve(fgRaw, _pal);
            if (fg is null) continue;
            // 同上：看不懂 → unresolved；透明 → skipped（與背景側同一套分桶標準）
            if (fg.Kind is ColorKind.Other or ColorKind.UnknownKey) { Unres(fg.Kind); continue; }
            if (fg.Kind is ColorKind.Alpha or ColorKind.Transparent) { _skipped++; continue; }
            if (bg is null) { Unres(UnresolvedReason.NoAncestorBackground); continue; }
            if (bg.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.UnknownKey) { Unres(bg.Kind); continue; }

            // Opacity 0 = 元素當下完全隱形。XAML 裡寫 Opacity="0" 幾乎都是淡入動畫的
            // 初始狀態，穩態是 1。對「看不見的東西」算對比沒有意義，跳過而不是報 1:1。
            if (op < 0.01) { _skipped++; continue; }

            _pairs++;

            // ignore 壓掉的不進 findings，但一定要計數 —— 靜默退化等於謊報
            if (suppressed) { _suppressed++; continue; }

            var line = el is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

            // ── 這是文字還是裝飾？WCAG 的 4.5:1 只管文字 ──
            var localName = el.Name.LocalName;
            var isText = !_nonTextEls.Contains(localName) &&
                         (_textEls.Contains(localName) || attrName == "Foreground");

            // ── WCAG 大字級豁免：≥18pt 或 ≥14pt 粗體只需 3:1 ──
            // ⚠ WPF 的 FontSize 是「裝置獨立像素」(1/96 吋) 不是 point (1/72 吋)，
            //    換算要 ×0.75。18pt = 24px、14pt = 18.67px —— 少了這一步會把
            //    18px 的字誤判成大字級而放過它。
            var fs = 0.0;
            var fsRaw = el.Attribute("FontSize")?.Value;
            if (fsRaw is not null) ParseDouble(fsRaw, out fs);
            var fw = el.Attribute("FontWeight")?.Value;
            var bold = IsBold(fw);
            var pt = fs * 0.75;
            var isLarge = pt >= 18 || (bold && pt >= 14);
            var need = NeedFor(isText, isLarge);

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
            Walk(child, bg, file, op, suppressed);
        }
    }

    // Style 裡成對的 Setter（Background + Foreground 同時存在）
    private void WalkStyles(XDocument doc, string file)
    {
        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            var styleSuppressed = IsSuppressedByComment(style, file);
            var setters = new Dictionary<string, string>();
            foreach (var s in style.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                var prop = s.Attribute("Property")?.Value;
                var val = s.Attribute("Value")?.Value;
                if (prop is not null && val is not null) setters[prop] = val;
            }

            // 規則 13 也適用於定義處：只收「無 TargetName（套宿主）或指向模板根」的
            // Setter。指向內部元素的換底配上宿主字色是錯的配對（內部元素的底不一定
            // 墊在宿主文字下面）—— 不猜。
            var tmplRootEl = style.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "ControlTemplate")
                ?.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
            var tmplRootName = tmplRootEl?.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                               ?? tmplRootEl?.Attribute("Name")?.Value;

            // 觸發器裡的 Setter 也算進來（它們覆蓋同一個 Style 的基礎值）
            var trigSetters = new List<(Dictionary<string, string> Set, bool Disabled)>();
            foreach (var t in style.Descendants().Where(StyleIndex.IsTriggerElement))
            {
                var ts = new Dictionary<string, string>();
                foreach (var s in t.Elements().Where(e => e.Name.LocalName == "Setter"))
                {
                    var prop = s.Attribute("Property")?.Value;
                    var val = s.Attribute("Value")?.Value;
                    if (prop is null || val is null) continue;
                    var tn = s.Attribute("TargetName")?.Value;
                    if (tn is null || (tmplRootName is not null && tn == tmplRootName)) ts[prop] = val;
                }
                if (ts.Count > 0) trigSetters.Add((ts, StyleIndex.IsDisabledTrigger(t)));
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
            var states = new List<(string Name, Dictionary<string, string> Set, bool Disabled)>
            { ("base", setters, false) };
            foreach (var (ts, dis) in trigSetters)
            {
                var merged = new Dictionary<string, string>(setters);
                foreach (var (k, v) in ts) merged[k] = v;
                states.Add(("trigger", merged, dis));
            }

            foreach (var (stateName, set, stDisabled) in states)
            {
                if (!set.TryGetValue("Foreground", out var fgv) ||
                    !set.TryGetValue("Background", out var bgv)) continue;
                if (fgDead) { _deadFg++; continue; }
                // 停用態（IsEnabled=False 觸發）：WCAG 1.4.3 豁免，計數回報、不評分
                if (stDisabled) { _disabled++; continue; }
                var fg = ColorResolver.Resolve(fgv, _pal);
                var bgr = ColorResolver.Resolve(bgv, _pal);
                if (fg is null || bgr is null) continue;
                // 之前這裡是裸 continue —— 配對從報告徹底消失，連計數都沒有，
                // 違反「靜默退化等於謊報」（同檔 ignore 註解那段的規矩）。
                // 任一側看不懂 → unresolved；任一側透明/半透明 → skipped
                if (fg.Kind is ColorKind.Other or ColorKind.UnknownKey ||
                    bgr.Kind is ColorKind.Other or ColorKind.UnknownKey)
                { Unres(fg.Kind is ColorKind.Other or ColorKind.UnknownKey ? fg.Kind : bgr.Kind); continue; }
                if (fg.Kind is ColorKind.Alpha or ColorKind.Transparent ||
                    bgr.Kind is ColorKind.Alpha or ColorKind.Transparent) { _skipped++; continue; }

                // ⚠ v0.1 的第 5 號 bug（規劃書 8.2）：這裡曾漏了 Need，而分級把
                //   「沒有 Need」當裝飾 → 所有 Style 配對被靜默歸為裝飾、完全不受檢。
                //   分類沿用排除清單：TargetType 是非文字控制項才免檢；
                //   不確定（匿名、沒 TargetType）就當文字報出來 —— 不替使用者放過。
                var isText = ttName is null || !_nonTextEls.Contains(ttName);
                var fs = 0.0;
                if (set.TryGetValue("FontSize", out var fsv)) ParseDouble(fsv, out fs);
                var bold = set.TryGetValue("FontWeight", out var fwv) && IsBold(fwv);
                var pt = fs * 0.75; // WPF FontSize 是 DIP，換 point 要 ×0.75（規劃書 4.3）
                var isLarge = pt >= 18 || (bold && pt >= 14);
                var need = NeedFor(isText, isLarge);

                _pairs++;
                if (styleSuppressed) { _suppressed++; continue; }
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

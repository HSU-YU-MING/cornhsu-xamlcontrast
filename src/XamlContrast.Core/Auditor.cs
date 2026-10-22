using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>
/// XAML 樹解析式對比度稽核。對每個有文字色的元素，沿父節點往上找
/// 「最近一個真正生效的背景色」，再把兩邊的資源鍵各自代入深淺兩套值算 WCAG 對比。
///
/// 處理的情況（十七條解析規則，一條都不能少 —— 全部是真實專案踩出來的；
/// 完整清單見規劃書 4.1。下面列的是樹走訪這一層的主幹）：
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
///   - 漸層等非純色 brush（屬性語法與 &lt;Border.Background&gt; 屬性元素語法都一樣；
///     屬性元素由 <see cref="XamlBrush"/> 讀進來，純色照常解析，其餘計入 unresolved）
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

    // 非 readonly:多 App repo 時每個檔案切到它所屬作用域的色盤(PaletteFor)
    private Palette _pal;
    private readonly ToolConfig _cfg;
    private readonly HashSet<string> _textEls;
    private readonly HashSet<string> _nonTextEls;
    private readonly List<Finding> _findings = new();
    private readonly List<string> _parseErrors = new();
    private readonly List<string> _invalidIgnores = new();
    private int _pairs, _unresolved, _skipped, _deadFg, _suppressed, _disabled;
    private StyleIndex _styles = null!;
    private string _curFile = ""; // 具名 Style 查找的「同檔優先」範圍（完整路徑）
    private bool _oneThemeOnly;   // 目前這個檔只屬於一個主題（ThemeFileSides）
    private ThemeSide? _curSide;  // 同上，是哪一個主題（Style 查找依主題解析，見 StyleIndex.Build）
    private readonly Dictionary<(Palette, ThemeSide), Palette> _sideViews = new();

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
        if (!m.Success)
        {
            // ⚠ 寫錯的變體（大小寫不同、冒號後沒有理由、漏冒號、理由換到第二行）舊版整段認不出來：
            //   不壓（方向安全），但也不警告 —— 使用者看不出自己的註解沒生效。看起來像標記就要講出來。
            //   多行理由維持不接受：接受等於放寬，會讓既有的這類註解開始壓掉配對
            if (IgnoreCommentLenient().IsMatch(c.Value))
                _invalidIgnores.Add($"{file}:{LineOfComment(c)}: malformed xamlcontrast-ignore comment (not suppressed)" +
                                    " — write it as 'xamlcontrast-ignore: reason', lowercase, with the reason on the same line");
            return false;
        }
        if (!m.Groups["reason"].Success && _cfg.Ignore.RequireReason)
        {
            _invalidIgnores.Add($"{file}:{LineOfComment(c)}: xamlcontrast-ignore without a reason is invalid (not suppressed)");
            return false;
        }
        return true;
    }

    /// <summary>「看起來像 ignore 標記」：註解開頭、不分大小寫、整個字（xamlcontrast-ignored 不算）。</summary>
    [GeneratedRegex(@"^\s*xamlcontrast-ignore\b", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoreCommentLenient();

    private static int LineOfComment(XComment c) => c is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

    /// <summary>XAML 數值屬性一律以 InvariantCulture 解析 ——
    /// 否則德語系等 locale 的 CI 上 Opacity="0.5" 會靜默解析失敗。</summary>
    private static bool ParseDouble(string s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// 字色疊到已解析的不透明底上之後、螢幕上實際的顏色（深淺各自）。元素 Opacity 與半透明字色
    /// （#AARRGGBB、帶 Opacity 的色票、只有一個主題半透明的色票）都是「以 alpha 疊在底上」，兩者相乘 ——
    /// 確定性的合成，不是猜。
    /// ⚠ 舊設計把半透明字色一律歸 skipped（「合法跳過」），而 skipped 不進覆蓋率分母：Opacity 0.2 的白字
    ///   疊黑底（1.66:1）、只有淺色主題半透明的次要字色（深色那側 1.65:1）整組不在報告裡，覆蓋率照樣 100%。
    /// </summary>
    private static (string Dark, string Light) EffectiveForeground(Resolved fg, double op, Resolved bg)
    {
        var (rd, ad) = fg.Kind == ColorKind.Alpha ? (fg.Rgb!, fg.Alpha * op) : (fg.Dark!, op);
        var (rl, al) = fg.Kind == ColorKind.Alpha ? (fg.RgbLight!, fg.AlphaLight * op) : (fg.Light!, op);
        return (ad < 0.999 ? Wcag.Composite(rd, ad, bg.Dark!) : rd,
                al < 0.999 ? Wcag.Composite(rl, al, bg.Light!) : rl);
    }

    /// <summary>字色看得見的程度（兩側 alpha 取大的）—— 兩側都幾乎為 0 才算隱形、合法跳過。</summary>
    private static double VisibleAlpha(Resolved fg)
        => fg.Kind == ColorKind.Alpha ? Math.Max(fg.Alpha, fg.AlphaLight) : 1.0;

    /// <summary>findings 的 fg 附上累乘的 Opacity（"#FFFFFF ×0.5"）。⚠ 一樣要 InvariantCulture：
    /// fg 是 baseline 鍵的一部分，逗號小數點的機器寫出 "×0,5"，換一台機器比對就全部變成 NEW。</summary>
    private static string WithOpacityNote(string fg, double op)
        => string.Create(CultureInfo.InvariantCulture, $"{fg} ×{Math.Round(op, 2)}");

    private Auditor(Palette palette, ToolConfig cfg)
    {
        _pal = palette;
        _cfg = cfg;
        // config 的 classification 覆寫：加進內建分類表（排除清單優先於元素型別判斷）
        _textEls = new HashSet<string>(TextEls.Concat(cfg.Classification.ExtraText));
        _nonTextEls = new HashSet<string>(NonTextEls.Concat(cfg.Classification.ExtraNonText));
    }

    /// <summary>畫面檔的區域 brush 疊在全域色盤上：字面色兩欄同值；讀不懂的（Binding）記成執行期才知道。</summary>
    private static Palette WithLocalOverrides(Palette global, Dictionary<string, string?> local)
    {
        var pal = new Palette();
        foreach (var (k, v) in global.Entries) pal.Entries[k] = v;
        pal.RuntimeKeys.UnionWith(global.RuntimeKeys);
        foreach (var (k, v) in local)
        {
            if (v is null) { pal.Entries.Remove(k); pal.RuntimeKeys.Add(k); }
            else { pal.Entries[k] = (v, v); pal.RuntimeKeys.Remove(k); }
        }
        return pal;
    }

    /// <summary>色盤的「單一主題視圖」：每個鍵兩欄都用指定主題的值（給只屬於那個主題的字典檔用）。</summary>
    private Palette SideView(Palette pal, ThemeSide side)
    {
        if (_sideViews.TryGetValue((pal, side), out var view)) return view;
        view = new Palette();
        foreach (var (k, (d, l)) in pal.Entries)
            view.Entries[k] = side == ThemeSide.Dark ? (d, d) : (l, l);
        view.RuntimeKeys.UnionWith(pal.RuntimeKeys); // 執行期才知道的鍵在哪個主題都一樣不知道
        return _sideViews[(pal, side)] = view;
    }

    private double NeedFor(bool isText, bool isLarge) =>
        !isText ? 0.0 : isLarge ? _cfg.Thresholds.LargeText : _cfg.Thresholds.NormalText;

    /// <summary>config 的 rootBackground 正規化成 ColorResolver 認得的值：
    /// 色碼／具名色／markup 原樣通過，裸鍵名包成 {DynamicResource X}。</summary>
    public static string? NormalizeRootBackground(string? raw)
        => raw is null ? null :
           raw.StartsWith('#') || raw.StartsWith('{') ? raw :
           ColorResolver.NamedHex(raw) is not null ? raw :
           "{DynamicResource " + raw + "}";

    private string? NormalizedRootBackground => NormalizeRootBackground(_cfg.RootBackground);

    /// <summary>元素是否屬非文字（裝飾）類。⚠ 全名相等不夠 —— WPF 生態滿地都是
    /// 衍生命名的控制項：MahApps 的 MetroProgressBar 對不上排除清單裡的 ProgressBar，
    /// 進度條填色被當文字用 4.5 要求（八專案抽驗實證，MahApps 唯一一筆 fail 就是它）。
    /// 改成「後綴比對」：MetroProgressBar／HandyControl 的 WaveProgressBar 都收斂到
    /// ProgressBar。文字清單同理（ExtendedRadioButton → RadioButton）。
    /// 兩邊都中時取「最長的後綴」—— 名字愈長愈具體。</summary>
    private bool IsNonTextElement(string localName)
    {
        if (_nonTextEls.Contains(localName)) return true;
        if (_textEls.Contains(localName)) return false;
        var nt = _nonTextEls.Where(localName.EndsWith).OrderByDescending(n => n.Length).FirstOrDefault();
        var tx = _textEls.Where(localName.EndsWith).OrderByDescending(n => n.Length).FirstOrDefault();
        return nt is not null && (tx is null || nt.Length > tx.Length);
    }

    public static AuditResult Run(string root, PaletteDetection detection, ToolConfig? config = null)
    {
        var auditor = new Auditor(detection.Palette, config ?? new ToolConfig());
        // 具名 Style 索引含色盤／主題字典 ——「排除出 UI 掃描」不等於「排除出索引」
        // 主題字典的歸屬一併交給索引：同 key 的 Style 依主題各自解析
        auditor._styles = StyleIndex.Build(root, detection.ThemeFileSides.Count > 0 ? detection.SideOf : null);
        auditor._parseErrors.AddRange(detection.ReadErrors); // 偵測時讀不到的檔（被鎖住的 .cs 之類）一樣要喊
        var all = PaletteDetector.EnumerateFiles(root, "*.xaml").ToList();
        // ⚠ 兩邊都 GetFullPath 再比：config 強制模式的色盤檔是 GetFullPath 過的，EnumerateFiles 卻沿用 root 的
        //   原寫法（相對路徑、正斜線、sub/..）—— 不正規化的話排除只在 root 剛好是正規化絕對路徑時成立，
        //   同一個專案的結果跟著 root 的寫法變（Action 一律傳相對路徑）
        var excluded = new HashSet<string>(detection.ExcludedFiles.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        bool IsPaletteFile(string f) => excluded.Contains(Path.GetFullPath(f));
        var files = all.Where(f => !IsPaletteFile(f)).ToList();

        // 色盤檔不是畫面（不算進 files），但裡面的東西一樣要稽核：Style 在定義處檢查（鏈路徑看到「字與底出自
        // 同一個 Style 節點」就略過、交給 WalkStyles），DataTemplate／ControlTemplate 照樣走樹 —— 舊版整檔不走樹，
        // 同一份字典只因為多了一個 keyed brush，裡面的模板就從報告與所有計數器消失。
        foreach (var f in all)
        {
            var paletteFile = IsPaletteFile(f);
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
                // 多 App repo:這個檔案屬於哪個 App,就用哪個 App 的色盤
                // (單 App 或無作用域資訊時 PaletteFor 回傳全域色盤,行為不變)
                // 只屬於一個主題的字典（色盤來自分開的深／淺 XAML 字典時）：兩欄都用那個主題的色票，
                // findings 標 OneThemeOnly —— 拿 Dark.xaml 的 Style 配淺色色票是執行期不存在的組合。
                // 不限色盤檔：不含色票的主題檔（Themes/Dark/Styles.xaml）一樣只屬於那個主題
                var side = detection.SideOf(f);
                auditor._pal = side is { } s ? auditor.SideView(detection.PaletteFor(f), s) : detection.PaletteFor(f);
                auditor._oneThemeOnly = side is not null;
                auditor._curSide = side;
                // 畫面檔自己的區域 brush 在那個畫面裡蓋過全域（全域色盤只讓它們補洞，見 MergedXamlPalette 的區域層）
                if (!paletteFile && PaletteDetector.LocalOverrides(f) is { Count: > 0 } local)
                    auditor._pal = WithLocalOverrides(auditor._pal, local);
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
            UnresolvedSites = auditor._unresolvedSites,
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
    private readonly List<UnresolvedSite> _unresolvedSites = new();

    private static int LineOf(XElement el) => el is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

    /// <summary>無法解析要連「為什麼、在哪裡、卡在哪個值」一起記 —— 只給總數的話，
    /// 使用者看不出 1373 組裡有 1295 組是同一個可補救的原因,更不知道去哪個檔修;
    /// unknown-key 點名鍵名之後,還是免費的死引用／打字錯誤偵測器。</summary>
    private void Unres(UnresolvedReason why, string file, int line, string value)
    {
        _unresolved++;
        _unresolvedBy[why] = _unresolvedBy.GetValueOrDefault(why) + 1;
        _unresolvedSites.Add(new UnresolvedSite(why, file, line, value));
    }

    /// <summary>依解析結果分類;UnknownKey 以「鍵名」當 value,其餘用原始屬性值。</summary>
    private void Unres(Resolved r, string file, int line, string raw)
        => Unres(r.Kind switch
        {
            ColorKind.UnknownKey => UnresolvedReason.UnknownPaletteKey,
            ColorKind.Alpha => UnresolvedReason.TranslucentUncomposited,
            ColorKind.SiblingContent => UnresolvedReason.OverSiblingContent,
            _ => UnresolvedReason.BoundOrGradient,
        }, file, line, r.Kind == ColorKind.UnknownKey && r.Key is not null ? r.Key : raw);

    private void Walk(XElement el, Resolved? bg, string file, double op, bool suppressed)
    {
        if (!suppressed && IsSuppressedByComment(el, file)) suppressed = true;
        var opRaw = el.Attribute("Opacity")?.Value;
        // Opacity 也可能是 Binding；只處理字面數值，其餘維持原值
        if (opRaw is not null && ParseDouble(opRaw, out var opVal)) op *= opVal;

        // 元素套用的 Style（具名引用或 <X.Style> 行內）：底色與觸發態的另一個來源。
        // 色盤來自分開的深／淺字典時，同 key 的 Style 依主題各自解析（StyleIndex.Build 的 sideOf）：Dark.xaml 與
        // Light.xaml 各定義一個 Chip，深色主題下用的是 Dark 的、淺色主題下是 Light 的 —— 拿 Dark 的 Chip 配淺色
        // 色票是執行期不存在的組合（審查素材 b4use）。兩個主題解析到不同的鏈時（perSide），各自只評自己那一側
        var splitThemes = _styles.HasThemeSides && _curSide is null;
        var chain = _styles.GetElementChain(el, _curFile, splitThemes ? ThemeSide.Dark : _curSide);
        var chainLight = splitThemes ? _styles.GetElementChain(el, _curFile, ThemeSide.Light) : chain;
        var perSide = splitThemes && (chain?.SideSpecific == true || chainLight?.SideSpecific == true);
        if (!perSide) chainLight = chain;

        // 逐狀態路徑的 alpha 合成要用「更新前」的祖先背景 —— 下面的 bg 更新塊會把
        // 半透明底先合成一次，逐狀態再合成就是疊兩層
        var ancestorBg = bg;

        var localBgRaw = XamlBrush.Read(el, "Background");
        string? BackgroundOf(StyleIndex.Merged? ch, ThemeSide? side) => localBgRaw
                    ?? (ch is not null && ch.Props.TryGetValue("Background", out var chainBg) ? chainBg.V : null)
                    ?? ch?.TemplateRootBg // 模板根的背景管到內容
                                          // 「地板」：根元素沒寫 Background 時，查該型別的隱含樣式（僅此一格，
                                          // 不是完整的隱含樣式解析 —— 理由見 StyleIndex.ImplicitRootBackground）
                    ?? (el.Parent is null ? _styles.ImplicitRootBackground(el.Name.LocalName, _curFile, side) : null)
                    // 最後一層地板：config 的 rootBackground —— 主題函式庫使用者的逃生口
                    // （視窗底色鍵在 repo、把它連到視窗的隱含樣式在 NuGet 套件裡:
                    //   工具看不到、使用者知道。使用者宣告的就是事實）
                    ?? (el.Parent is null ? NormalizedRootBackground : null);
        var bgVal = BackgroundOf(chain, splitThemes ? ThemeSide.Dark : _curSide);
        var bgValLight = splitThemes ? BackgroundOf(chainLight, ThemeSide.Light) : bgVal;
        bg = bgVal == bgValLight
            ? NextBackground(bgVal, _pal, bg)
            // 兩個主題的底來自不同的 Style（或不同的隱含樣式）：各自在自己的主題視圖裡算，再拼回深淺兩欄
            : BothThemes(NextBackground(bgVal, SideView(_pal, ThemeSide.Dark), OnSide(bg, ThemeSide.Dark)),
                         NextBackground(bgValLight, SideView(_pal, ThemeSide.Light), OnSide(bg, ThemeSide.Light)));

        // ── 元素 × Style 鏈的逐狀態檢查（具名 Style 底色盲區）──
        // CelFlow 實證：刪除鈕 Foreground=RedL 配 BasedOn=DarkBtn，DarkBtn 的底是
        // Bg3、hover Bg4 —— 樹走訪看不到 Style 的底色會誤落到祖先卡片的 Bg2，
        // 低估實況；「忽略」鈕（Fg1 疊 Border0/Border1）則整組漏掉。
        // Foreground 一律由這裡接手，避免與下面的舊路徑重複輸出。perSide 時某一個主題下沒有這個 Style
        // （StaticResource 在那個主題下根本解析不到）＝那個主題下這組不存在，不評
        var handledFg = chain is not null || chainLight is not null;
        if (perSide)
        {
            if (chain is not null) GradeChain(el, chain, ThemeSide.Dark, bg, ancestorBg, localBgRaw, file, op, suppressed);
            if (chainLight is not null) GradeChain(el, chainLight, ThemeSide.Light, bg, ancestorBg, localBgRaw, file, op, suppressed);
        }
        else if (chain is not null) GradeChain(el, chain, null, bg, ancestorBg, localBgRaw, file, op, suppressed);

        WalkForegroundAndChildren(el, bg, file, op, suppressed, handledFg);
    }

    /// <summary>元素自己的底（bgVal）套到目前生效的底上。</summary>
    private static Resolved? NextBackground(string? bgVal, Palette pal, Resolved? bg)
    {
        if (bgVal is null) return bg;
        var r = ColorResolver.Resolve(bgVal, pal);
        if (r is null) return bg;
        if (r.Kind == ColorKind.Alpha)
        {
            // 半透明遮罩：疊在目前生效的背景上算出實際顏色（WPF 以 sRGB 混合；
            // 半透明「色票」深淺各帶自己的 alpha）
            // 找不到底色就維持原本的（無法判定，後面會計入 unresolved）
            return bg is { Dark: not null, Light: not null }
                ? new Resolved(ColorKind.Hard,
                    Dark: Wcag.Composite(r.Rgb!, r.Alpha, bg.Dark),
                    Light: Wcag.Composite(r.RgbLight!, r.AlphaLight, bg.Light),
                    Key: $"{bgVal} over {(bg.Key is not null ? "{" + bg.Key + "}" : bg.Dark)}")
                : bg;
        }
        // 底色來自 Binding / TemplateBinding / 漸層 —— 執行期才知道實際顏色。
        // 關鍵：不能「往上繼承祖先背景」，那會把子元素的文字色配到錯的底色上
        // （例：標籤徽章的底色綁使用者選的顏色，白字是對的，但繼承祖先會誤判成白配白）。
        // 標記為無法判定，子元素一律跳過。
        if (r.Kind == ColorKind.Other) return new Resolved(ColorKind.Other, Raw: bgVal);
        return r.Kind != ColorKind.Transparent ? r : bg;
    }

    /// <summary>色盤值的單一主題投影：兩欄都用指定主題那一欄（配 <see cref="SideView"/> 用）。</summary>
    private static Resolved? OnSide(Resolved? r, ThemeSide side) => r is null ? null : side == ThemeSide.Dark
        ? r with { Light = r.Dark, RgbLight = r.Rgb, AlphaLight = r.Alpha }
        : r with { Dark = r.Light, Rgb = r.RgbLight, Alpha = r.AlphaLight };

    /// <summary>兩個主題各自算出的底拼回一個深淺兩欄的值。任一側算不出來（沒有底、執行期才知道、不在色盤）
    /// 就交出那一側的狀態 —— 後面的字計入 unresolved，不拿另一個主題的值補。</summary>
    private static Resolved? BothThemes(Resolved? dark, Resolved? light)
    {
        if (dark is null || light is null) return null;
        if (dark.Kind is not (ColorKind.Hard or ColorKind.Soft)) return dark;
        if (light.Kind is not (ColorKind.Hard or ColorKind.Soft)) return light;
        var same = dark.Kind == ColorKind.Hard && light.Kind == ColorKind.Hard && dark.Dark == light.Light;
        return new Resolved(same ? ColorKind.Hard : ColorKind.Soft, Dark: dark.Dark, Light: light.Light,
            Key: dark.Key == light.Key ? dark.Key : $"{dark.Key ?? dark.Dark} (dark) / {light.Key ?? light.Light} (light)");
    }

    /// <summary>元素 × Style 鏈的逐狀態檢查。<paramref name="side"/> 不是 null 時只評那一個主題
    /// （同 key 的 Style 依主題各自解析、兩個主題解析到不同的鏈）：色盤用那個主題的視圖、finding 標 OneThemeOnly，
    /// 元素名的 Style 標籤帶 @dark／@light。</summary>
    private void GradeChain(XElement el, StyleIndex.Merged chain, ThemeSide? side, Resolved? bg, Resolved? ancestorBg,
        string? localBgRaw, string file, double op, bool suppressed)
    {
        var pal = _pal;
        var label = chain.Label;
        var oneThemeOnly = _oneThemeOnly;
        if (side is { } s)
        {
            pal = SideView(_pal, s);
            bg = OnSide(bg, s);
            ancestorBg = OnSide(ancestorBg, s);
            label += s == ThemeSide.Dark ? "@dark" : "@light";
            oneThemeOnly = true;
        }
        {
            var localFgRaw = XamlBrush.Read(el, "Foreground");
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

                // 字與底都出自同一個 Style 節點 → WalkStyles 在定義處已檢過，不重複。
                // ⚠ 例外：底是透明／半透明時定義處沒有底下那層可疊（只能記 skipped），這組要在使用處
                //   對著祖先的底評 —— 否則它在哪裡都沒被評分，也不在覆蓋率裡
                if (fgSrc is int fi && bgSrc is int bi && fi == bi &&
                    ColorResolver.Resolve(bgV, pal) is not { Kind: ColorKind.Transparent or ColorKind.Alpha }) continue;
                // 停用態：WCAG 1.4.3 豁免，計數回報、不評分
                if (st.Disabled) { _disabled++; continue; }

                var fgR = ColorResolver.Resolve(fgV, pal);
                if (fgR is null) continue;
                // 字色是 Binding／漸層／未知鍵 = 工具看不懂 → unresolved（盲區要誠實計數，
                // 之前全塞 skipped，把盲區藏進「合法豁免」桶）；透明字才是合法跳過
                if (fgR.Kind is ColorKind.Other or ColorKind.UnknownKey)
                { Unres(fgR, file, LineOf(el), fgV); continue; }
                if (fgR.Kind is ColorKind.Transparent) { _skipped++; continue; } // 半透明字色往下疊到底上評分（見 EffectiveForeground）

                Resolved? bgObj;
                var bgOwnRaw = bgV; // 卡住時要點名的底：透明的 Setter 只是穿透，卡住的是祖先那一層
                if (bgV is not null)
                {
                    var rb = ColorResolver.Resolve(bgV, pal);
                    if (rb is null) continue;
                    if (rb.Kind == ColorKind.Transparent) { bgObj = bg; bgOwnRaw = null; }
                    else if (rb.Kind == ColorKind.Alpha)
                    {
                        bgObj = ancestorBg is { Dark: not null, Light: not null }
                            ? new Resolved(ColorKind.Hard,
                                Dark: Wcag.Composite(rb.Rgb!, rb.Alpha, ancestorBg.Dark),
                                Light: Wcag.Composite(rb.RgbLight!, rb.AlphaLight, ancestorBg.Light),
                                Key: $"{bgV} over {(ancestorBg.Key is not null ? "{" + ancestorBg.Key + "}" : ancestorBg.Dark)}")
                            : null;
                    }
                    else if (rb.Kind is ColorKind.Other or ColorKind.UnknownKey) { Unres(rb, file, LineOf(el), bgV!); continue; }
                    else bgObj = rb;
                }
                else bgObj = bg; // Style 鏈沒設底 → 退回祖先背景
                if (bgObj is null) { Unres(UnresolvedReason.NoAncestorBackground, file, LineOf(el), fgV); continue; }
                if (bgObj.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.UnknownKey or ColorKind.SiblingContent) { Unres(bgObj, file, LineOf(el), bgOwnRaw ?? bgObj.Key ?? bgObj.Raw ?? fgV); continue; }
                if (op * VisibleAlpha(fgR) < 0.01) { _skipped++; continue; }

                var isTextC = !IsNonTextElement(el.Name.LocalName);
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

                var (fgDC, fgLC) = EffectiveForeground(fgR, op, bgObj);

                // 觸發器只動了無關屬性（如停用態只設 Opacity）→ 與基礎同組，不重複。
                // ⚠ 鍵是兩個主題合成後的實際顏色（加上門檻）：舊版只看深色值，hover 換成「深色同值、淺色不同」的色票時，
                //   淺色 1.16:1 的真 fail 被當成重複丟掉（審查素材 dedup；半透明色票深色同 alpha、淺色不同 alpha 也一樣）
                if (!emitted.Add($"{fgDC}|{fgLC}|{bgObj.Dark}|{bgObj.Light}|{needC}")) continue;

                _pairs++;
                if (suppressed) { _suppressed++; continue; }
                var lineC = el is IXmlLineInfo cli && cli.HasLineInfo() ? cli.LineNumber : 0;
                _findings.Add(new Finding
                {
                    File = file,
                    Line = lineC,
                    Element = $"{el.Name.LocalName}⊕{label}/{(st.Cond is null && st.Set.Count == 0 ? "base" : "trigger")}",
                    Fg = op < 0.999 ? WithOpacityNote(fgV, op) : fgV,
                    Bg = bgObj.Key is not null ? "{" + bgObj.Key + "}" : bgObj.Dark!,
                    Mixed = fgR.Kind != bgObj.Kind,
                    RatioDark = Wcag.Contrast(fgDC, bgObj.Dark!),
                    RatioLight = Wcag.Contrast(fgLC, bgObj.Light!),
                    IsText = isTextC,
                    Need = needC,
                    Size = fsC > 0 ? $"{(int)Math.Round(fsC, MidpointRounding.ToEven)}px" : "",
                    Large = isLargeC,
                    OneThemeOnly = oneThemeOnly,
                });
            }
        }
    }

    /// <summary>元素自己的 Foreground／Fill（沒有 Style 鏈接手時）與子元素（含規則 17 的兄弟背板）。</summary>
    private void WalkForegroundAndChildren(XElement el, Resolved? bg, string file, double op, bool suppressed, bool handledFg)
    {
        foreach (var attrName in (ReadOnlySpan<string>)["Foreground", "Fill"])
        {
            if (attrName == "Foreground" && handledFg) continue;
            var fgRaw = XamlBrush.Read(el, attrName);
            if (fgRaw is null) continue;
            var fg = ColorResolver.Resolve(fgRaw, _pal);
            if (fg is null) continue;
            // 同上：看不懂 → unresolved；透明 → skipped（與背景側同一套分桶標準）
            if (fg.Kind is ColorKind.Other or ColorKind.UnknownKey) { Unres(fg, file, LineOf(el), fgRaw); continue; }
            if (fg.Kind is ColorKind.Transparent) { _skipped++; continue; } // 半透明字色往下疊到底上評分（見 EffectiveForeground）
            if (bg is null) { Unres(UnresolvedReason.NoAncestorBackground, file, LineOf(el), fgRaw); continue; }
            if (bg.Kind is ColorKind.Other or ColorKind.Alpha or ColorKind.UnknownKey or ColorKind.SiblingContent) { Unres(bg, file, LineOf(el), bg.Key ?? bg.Raw ?? fgRaw); continue; }

            // Opacity 0 = 元素當下完全隱形。XAML 裡寫 Opacity="0" 幾乎都是淡入動畫的
            // 初始狀態，穩態是 1。對「看不見的東西」算對比沒有意義，跳過而不是報 1:1。
            if (op * VisibleAlpha(fg) < 0.01) { _skipped++; continue; }

            _pairs++;

            // ignore 壓掉的不進 findings，但一定要計數 —— 靜默退化等於謊報
            if (suppressed) { _suppressed++; continue; }

            var line = el is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;

            // ── 這是文字還是裝飾？WCAG 的 4.5:1 只管文字 ──
            var localName = el.Name.LocalName;
            var isText = !IsNonTextElement(localName) &&
                         (_textEls.Contains(localName) || localName.EndsWith("TextBlock") || attrName == "Foreground");

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

            // Opacity < 1 或字色本身半透明時，螢幕上看到的是「文字色以 alpha 疊在背景上」的混色結果
            var (fgD, fgL) = EffectiveForeground(fg, op, bg);

            _findings.Add(new Finding
            {
                File = file,
                Line = line,
                Element = localName,
                Fg = op < 0.999 ? WithOpacityNote(fgRaw, op) : fgRaw,
                Bg = bg.Key is not null ? "{" + bg.Key + "}" : bg.Dark!,
                Mixed = fg.Kind != bg.Kind,
                RatioDark = Wcag.Contrast(fgD, bg.Dark!),
                RatioLight = Wcag.Contrast(fgL, bg.Light!),
                IsText = isText,
                Need = need,
                Size = fs > 0 ? $"{(int)Math.Round(fs, MidpointRounding.ToEven)}px" : "",
                Large = isLarge,
                OneThemeOnly = _oneThemeOnly,
            });
        }

        // ── 規則 17:重疊容器內的兄弟背景 ──
        // Grid 同格子(Canvas/自訂 *Panel 全域)裡,文件順序在前的兄弟畫在下面。
        // 兩半,對應原型檔頭記載的「sibling 背景」盲區:
        //   (a) 兄弟是佔滿格子的純色底(Border/Rectangle 之類)→ 它才是文字真正的
        //       背板,不是祖先 —— 之前這形狀整批 no-ancestor-background 或誤配祖先
        //   (b) 兄弟是內容元素(Image 等)→ 底是一張圖,靜態不可知 —— 之前誤配祖先
        //       報假警報(HandyControl Carousel:白字疊照片被配 RegionBrush,light=1)
        // 防呆:有明確尺寸或非 Stretch 對齊的兄弟不算背板(強調色條 Width="4" 之類)。
        //   Canvas 只套 (b):它不把子元素撐滿,(a) 的「佔滿格子」前提不成立。
        var layout = LayoutOf(el.Name.LocalName);
        var overlap = layout != PanelLayout.SideBySide;
        Dictionary<(int Row, int Col), Resolved>? cellBg = overlap ? new() : null;
        foreach (var child in el.Elements())
        {
            // Style / Setter / Trigger 這些不是視覺樹，個別處理（WalkStyles），這裡跳過
            if (StyleishEls.Contains(child.Name.LocalName)) continue;
            // <Border.Background> 之類是屬性值（上面已由 XamlBrush 讀進來），不是視覺子樹
            if (XamlBrush.IsColorPropertyElement(el, child)) continue;
            var childBg = bg;
            (int, int) cell = default;
            if (overlap)
            {
                cell = CellOf(child, el.Name.LocalName == "Grid");
                if (cellBg!.TryGetValue(cell, out var sb)) childBg = sb;
            }
            Walk(child, childBg, file, op, suppressed);
            if (overlap && SiblingBackdrop(child, stretches: el.Name.LocalName != "Canvas", under: childBg) is { } backdrop)
                cellBg![cell] = layout == PanelLayout.Unknown
                    // 版面未知的自訂面板：它若是疊層，這就是後面兄弟的背板；若是排開，就不是 —— 靜態不可知，不猜
                    ? new Resolved(ColorKind.SiblingContent, Raw: $"<{child.Name.LocalName}> in <{el.Name.LocalName}>")
                    : backdrop;
        }
    }

    private enum PanelLayout { SideBySide, Overlap, Unknown }

    /// <summary>容器的子元素會不會疊在一起。Grid（同一格）、Canvas 會；StackPanel／WrapPanel／DockPanel／
    /// UniformGrid／ToolBarPanel／TabPanel 系（含衍生命名，例 VirtualizingWrapPanel、MyDockPanel）會排開。
    /// ⚠ 其他自訂 *Panel 的版面靜態不可知：舊版「保守視為疊層」，在排開的面板裡就是捏造背板
    ///   （VirtualizingWrapPanel 裡的黑色方塊被當成旁邊白字的底）；當成排開一樣是猜 → Unknown。</summary>
    private static PanelLayout LayoutOf(string name)
    {
        if (name is "Grid" or "Canvas") return PanelLayout.Overlap;
        if (name.EndsWith("StackPanel") || name.EndsWith("WrapPanel") || name.EndsWith("DockPanel") ||
            name.EndsWith("UniformGrid") || name.EndsWith("ToolBarPanel") || name.EndsWith("TabPanel") ||
            name.EndsWith("ToolBarOverflowPanel")) return PanelLayout.SideBySide;
        return name.EndsWith("Panel") ? PanelLayout.Unknown : PanelLayout.SideBySide;
    }

    private static (int, int) CellOf(XElement child, bool isGrid)
    {
        if (!isGrid) return default; // Canvas/自訂 Panel:全部視為同一層
        int P(string attr) => int.TryParse(child.Attribute(attr)?.Value, out var v) ? v : 0;
        return (P("Grid.Row"), P("Grid.Column"));
    }

    /// <summary>這個兄弟元素會不會成為「後面同格子兄弟」的背板。
    /// 回傳 null = 不影響(既非佔滿格子的底、也非內容元素)。</summary>
    private Resolved? SiblingBackdrop(XElement child, bool stretches, Resolved? under)
    {
        var name = child.Name.LocalName;
        // (b) 內容元素:底變成一張圖,不可知
        if (name is "Image" or "MediaElement" || name.EndsWith("Image"))
            return new Resolved(ColorKind.SiblingContent, Raw: "<" + name + ">");

        // (a) 純色背板候選:要佔滿格子 —— 有明確尺寸或非 Stretch 對齊的不算
        // ⚠ Canvas 不套 Stretch:沒內容、沒尺寸的 Border 在 Canvas 裡實際是 0×0。舊版照樣套 17(a),
        //   把一塊不存在的背板捏造出來(白字疊祖先白底 1:1,報成 21:1 合格)。有尺寸的也不行 ——
        //   Canvas.Left/Top 與文字範圍靜態不可知,它在不在字底下判定不了(同 Grid 裡的強調色條)。
        if (!stretches) return null;
        static bool Stretchy(XElement e, string attr)
            => e.Attribute(attr)?.Value is null or "Stretch";
        if (child.Attribute("Width") is not null || child.Attribute("Height") is not null ||
            !Stretchy(child, "HorizontalAlignment") || !Stretchy(child, "VerticalAlignment"))
            return null;
        var raw = XamlBrush.Read(child, name == "Rectangle" ? "Fill" : "Background");
        if (raw is null) return null;
        var r = ColorResolver.Resolve(raw, _pal);
        // 背板元素自己的 Opacity 讓它變半透明（<Rectangle Fill="#000000" Opacity="0.2"/> 的遮罩寫法）——
        // 舊版當成不透明，20% 黑的遮罩被當成純黑。Opacity 與 brush 的 alpha 相乘；讀不懂的 Opacity（Binding）
        // = 執行期才知道；幾乎為 0 = 看不見，不影響
        if (r is not null && child.Attribute("Opacity")?.Value is { } opRaw)
        {
            if (!ParseDouble(opRaw, out var bop) || !double.IsFinite(bop))
                return new Resolved(ColorKind.Other, Raw: $"{raw} (Opacity={opRaw})");
            if (bop < 0.01) return null;
            if (bop < 0.999)
            {
                raw = WithOpacityNote(raw, bop);
                r = r.Kind switch
                {
                    ColorKind.Hard or ColorKind.Soft => new Resolved(ColorKind.Alpha, Key: r.Key,
                        Alpha: bop, Rgb: r.Dark, AlphaLight: bop, RgbLight: r.Light),
                    ColorKind.Alpha => r with { Alpha = r.Alpha * bop, AlphaLight = r.AlphaLight * bop },
                    _ => r,
                };
            }
        }
        // ⚠ 舊版只收確定的純色(Hard/Soft),其餘回傳 null「維持原判定」—— 字改配祖先的底,給出篤定的比值:
        //   白 Grid 裡 #CC000000 的兄弟加黑字報 21:1 ok(寫成巢狀是 1.66:1 fail);Binding 兄弟也報 21:1,
        //   跟同檔「Binding 底不可往上繼承」的規則矛盾。現在比照巢狀的處理(見 Walk 的 bg 更新塊):
        switch (r?.Kind)
        {
            case ColorKind.Hard or ColorKind.Soft:
                return r;
            case ColorKind.Alpha:
                // 半透明:疊在它底下那層上合成;底下沒有可疊的顏色就維持原判定(同巢狀,不猜)
                return under is { Dark: not null, Light: not null }
                    ? new Resolved(ColorKind.Hard,
                        Dark: Wcag.Composite(r.Rgb!, r.Alpha, under.Dark),
                        Light: Wcag.Composite(r.RgbLight!, r.AlphaLight, under.Light),
                        Key: $"{raw} over {(under.Key is not null ? "{" + under.Key + "}" : under.Dark)}")
                    : null;
            case ColorKind.Other:
                return new Resolved(ColorKind.Other, Raw: raw); // Binding／漸層:執行期才知道 → 後面的字計入 unresolved
            case ColorKind.UnknownKey:
                return r;                             // 不在色盤的鍵 → unknown-palette-key
            default:
                return null;                          // Transparent:穿透
        }
    }

    // Style 裡成對的 Setter（Background + Foreground 同時存在）
    private void WalkStyles(XDocument doc, string file)
    {
        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            var styleSuppressed = IsSuppressedByComment(style, file);

            // 合併鏈：BasedOn 逐層合併、同條件觸發器合併（與元素端的鏈路徑同一套，見 StyleIndex.DefinitionChain）。
            // 規則 13 也適用於定義處：觸發器只收「無 TargetName（套宿主）或指向模板根」的 Setter ——
            // 指向內部元素的換底配上宿主字色是錯的配對（內部元素的底不一定墊在宿主文字下面），不猜。
            var (chain, selfId) = _styles.DefinitionChain(style, _curFile);

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

            // 基礎 + 每個（合併後的）觸發器狀態各檢一次。字與底都出自這個 Style 才在定義處報；
            // 任一半來自 BasedOn 的基底 → 那一組由使用處（元素 × Style 鏈）評，兩邊不重複
            var states = new List<(string Name, StyleIndex.State? St)> { ("base", null) };
            states.AddRange(chain.States.Select(s => ("trigger", (StyleIndex.State?)s)));

            foreach (var (stateName, st) in states)
            {
                string? Prop(string p, out int src)
                {
                    if (st is not null && st.Set.TryGetValue(p, out var sv)) { src = st.SetSrc[p]; return sv; }
                    if (chain.Props.TryGetValue(p, out var pv)) { src = pv.Src; return pv.V; }
                    src = -1; return null;
                }
                var fgv = Prop("Foreground", out var fgSrc);
                var bgv = Prop("Background", out var bgSrc);
                if (fgv is null || bgv is null || fgSrc != selfId || bgSrc != selfId) continue;
                var stDisabled = st?.Disabled ?? false;
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
                {
                    var (bad, raw) = fg.Kind is ColorKind.Other or ColorKind.UnknownKey ? (fg, fgv) : (bgr, bgv);
                    Unres(bad, file, line, raw); continue;
                }
                // 透明字、透明／半透明底 → 合法跳過（定義處沒有底下那層可疊；有用到這個 Style 的元素會在使用處評，
                // 見元素 × Style 鏈的同源判定）；半透明字色疊到 Style 自己的不透明底上評分
                if (fg.Kind is ColorKind.Transparent || VisibleAlpha(fg) < 0.01 ||
                    bgr.Kind is ColorKind.Alpha or ColorKind.Transparent) { _skipped++; continue; }
                // 同一個 brush 同時當字色與底色 —— Material 系的模板不透明度慣用手法：
                // 模板把 Background 以 10~12% Opacity 畫成暈染（VisualState 動畫），字全濃度。
                // 字面上 1:1、執行期不是；Opacity 藏在模板動畫裡，靜態看不到 —— 不猜，
                // 歸 unresolved 而不是報一筆必然假的 fail（MaterialDesign 抽驗實證，
                // NavigationPrimaryListBoxItem 等三處全是這形狀）。
                if (fg.Key is not null && fg.Key == bgr.Key)
                { Unres(UnresolvedReason.SameBrushPair, file, line, fg.Key); continue; }

                // ⚠ v0.1 的第 5 號 bug（規劃書 8.2）：這裡曾漏了 Need，而分級把
                //   「沒有 Need」當裝飾 → 所有 Style 配對被靜默歸為裝飾、完全不受檢。
                //   分類沿用排除清單：TargetType 是非文字控制項才免檢；
                //   不確定（匿名、沒 TargetType）就當文字報出來 —— 不替使用者放過。
                var isText = ttName is null || !IsNonTextElement(ttName);
                var fs = 0.0;
                if (Prop("FontSize", out _) is { } fsv) ParseDouble(fsv, out fs);
                var bold = IsBold(Prop("FontWeight", out _));
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
                    RatioDark = Wcag.Contrast(EffectiveForeground(fg, 1.0, bgr).Dark, bgr.Dark!),
                    RatioLight = Wcag.Contrast(EffectiveForeground(fg, 1.0, bgr).Light, bgr.Light!),
                    IsText = isText,
                    Need = need,
                    Size = fs > 0 ? $"{(int)Math.Round(fs, MidpointRounding.ToEven)}px" : "",
                    Large = isLarge,
                    OneThemeOnly = _oneThemeOnly,
                });
            }
        }
    }
}

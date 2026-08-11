using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>
/// 具名 Style 索引與解析（港自原型 4929b69，修「元素自身底色來自具名 Style」盲區）。
///
/// 範圍界定（與原型一致）：
///   - 只解析「具名引用」與「行內 Style」；隱含樣式不套用（雜訊量級不同，檔頭明記）。
///   - BasedOn 鏈逐層合併；衍生 Style 自帶 Template 時不繼承基底的模板觸發器
///     （WPF 換掉整個模板，基底模板的觸發器就不存在了）。
///   - 模板觸發器只收「無 TargetName 的 Setter」（套回模板宿主本身）；
///     有 TargetName 的要解析模板內部樹，不做 —— QuillNest 四筆假警報的成因，已記進盲區表。
///   - 同條件的觸發器跨節點合併（style trigger 蓋 template trigger、衍生蓋基底），
///     否則「style trigger 設字色＋template trigger 設底色」的同一個狀態
///     會被拆成兩個各缺一半的幻影組合。
/// </summary>
internal sealed partial class StyleIndex
{
    internal sealed class State
    {
        public bool Disabled;
        public bool FromTemplate;
        public Dictionary<string, string> Set = new();
        public Dictionary<string, int> SetSrc = new(); // 屬性 → 來源 Style Id
        /// <summary>來自「TargetName=模板根」Setter 的屬性 —— 直接設在根元素上，
        /// WPF 屬性優先序會蓋過經 TemplateBinding 進來的宿主本地值</summary>
        public HashSet<string> RootTargeted = new();
        public string? Cond;
    }

    internal sealed class Record
    {
        public required int Id;
        public required string File;
        public string? Key;
        public string? BasedOn;
        public Dictionary<string, string> Setters = new();
        public List<State> States = new();
        public bool HasTemplate;
        /// <summary>模板根元素自帶的 Background（CellDeleteBtn 形狀：Style 層沒有
        /// Background setter，真正的底是模板根 Border 的色票 —— 少了它會誤配祖先背景）</summary>
        public string? TemplateRootBg;
    }

    internal sealed class Merged
    {
        /// <summary>各屬性最後生效的 Setter（帶來源 Style Id）</summary>
        public Dictionary<string, (string V, int Src)> Props = new();
        public List<State> States = new();
        /// <summary>生效模板的根元素背景（衍生自帶模板時整個換掉，含根背景）</summary>
        public string? TemplateRootBg;
        public required string Label;
    }

    [GeneratedRegex(@"^\{(Dynamic|Static)Resource\s+(?<k>[\w\.]+)\}$")]
    private static partial Regex ResourceRef();

    [GeneratedRegex(@"(^|\.)IsEnabled$")]
    private static partial Regex IsEnabledProp();

    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    private readonly Dictionary<string, Dictionary<string, Record>> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Record> _global = new();
    /// <summary>隱含樣式（只有 TargetType、無 x:Key）依型別名索引。
    /// ⚠ 範圍刻意極窄：只給「檔案根元素的背景」用，見 <see cref="ImplicitRootBackground"/>。</summary>
    private readonly Dictionary<string, Dictionary<string, Record>> _implicitByFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Record> _implicitGlobal = new(StringComparer.Ordinal);
    private int _seq;

    [GeneratedRegex(@"(?<t>\w+)\s*\}?\s*$")]
    private static partial Regex TargetTypeName();

    /// <summary>IsEnabled=False 觸發 = 停用態。WCAG 1.4.3 明文豁免停用中的控制項。
    /// Multi(Data)Trigger 的條件是 AND：任一條是 IsEnabled=False，整個狀態就只在停用時成立。</summary>
    internal static bool IsDisabledTrigger(XElement t)
    {
        if (t.Name.LocalName == "Trigger")
        {
            var p = t.Attribute("Property")?.Value;
            var v = t.Attribute("Value")?.Value;
            return p is not null && v == "False" && IsEnabledProp().IsMatch(p);
        }
        if (t.Name.LocalName == "DataTrigger")
        {
            var b = t.Attribute("Binding")?.Value;
            var v = t.Attribute("Value")?.Value;
            return b is not null && v == "False" && b.Contains("IsEnabled");
        }
        if (t.Name.LocalName is "MultiTrigger" or "MultiDataTrigger")
            return ConditionsOf(t).Any(c =>
                c.Attribute("Value")?.Value == "False" &&
                (c.Attribute("Property")?.Value is { } p && IsEnabledProp().IsMatch(p) ||
                 c.Attribute("Binding")?.Value is { } b && b.Contains("IsEnabled")));
        return false;
    }

    private static IEnumerable<XElement> ConditionsOf(XElement t)
        => t.Descendants().Where(e => e.Name.LocalName == "Condition");

    /// <summary>觸發器種類（含 Multi 組合條件）—— 觸發器收集處共用這一份清單，
    /// 加新種類只改這裡。MultiTrigger 曾是黑洞：跳過清單認得它、收集處不認得，
    /// 組合條件狀態（hover＋選取之類）整批沒被檢查，也沒有任何計數提示。</summary>
    internal static bool IsTriggerElement(XElement e)
        => e.Name.LocalName is "Trigger" or "DataTrigger" or "MultiTrigger" or "MultiDataTrigger";

    /// <summary>條件簽名 —— 同條件的觸發器狀態要跨節點合併。
    /// Multi(Data)Trigger 逐條列出以 &amp; 串接（條件是 AND）。</summary>
    private static string? CondSignature(XElement t)
    {
        if (t.Name.LocalName == "Trigger")
            return $"P:{t.Attribute("Property")?.Value}={t.Attribute("Value")?.Value}";
        if (t.Name.LocalName == "DataTrigger")
            return t.Attribute("Binding") is { } b && t.Attribute("Value") is { } v
                ? $"B:{b.Value}={v.Value}" : null;
        var parts = ConditionsOf(t)
            .Select(c => c.Attribute("Property") is { } p
                ? $"P:{p.Value}={c.Attribute("Value")?.Value}"
                : $"B:{c.Attribute("Binding")?.Value}={c.Attribute("Value")?.Value}")
            .ToList();
        return parts.Count > 0 ? string.Join("&", parts) : null;
    }

    internal Record CreateRecord(XElement style, string file)
    {
        var rec = new Record { Id = ++_seq, File = file };
        foreach (var s in style.Elements().Where(e => e.Name.LocalName == "Setter"))
        {
            var p = s.Attribute("Property")?.Value;
            var v = s.Attribute("Value")?.Value;
            if (p is not null && v is not null) rec.Setters[p] = v;
        }
        var tmplEl = style.Descendants().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
        rec.HasTemplate = tmplEl is not null;
        // 模板根＝第一個視覺元素（跳過 <ControlTemplate.Resources> 之類的屬性元素）
        var tmplRoot = tmplEl?.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
        rec.TemplateRootBg = tmplRoot?.Attribute("Background")?.Value;
        // 模板根的名字：規則 13 —— 觸發器裡「TargetName=模板根」的 Setter 等同
        // 直接設在宿主上（Kindling CellDeleteBtn hover 換底、QuillNest RadioButton
        // 選取態換底，共六筆假警報全是這形狀）。指向內部元素的 TargetName 仍不做。
        var rootName = tmplRoot?.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                       ?? tmplRoot?.Attribute("Name")?.Value;
        foreach (var t in style.Descendants().Where(IsTriggerElement))
        {
            var set = new Dictionary<string, string>();
            var rootTargeted = new HashSet<string>();
            foreach (var s in t.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                var p = s.Attribute("Property")?.Value;
                var v = s.Attribute("Value")?.Value;
                if (p is null || v is null) continue;
                var tn = s.Attribute("TargetName")?.Value;
                // 無 TargetName＝套宿主；TargetName=模板根＝實質也是宿主的視覺（規則 13）
                if (tn is null) { set[p] = v; rootTargeted.Remove(p); }
                else if (rootName is not null && tn == rootName) { set[p] = v; rootTargeted.Add(p); }
            }
            if (set.Count == 0) continue;
            var inTmpl = false;
            for (var anc = t.Parent; anc is not null && !ReferenceEquals(anc, style); anc = anc.Parent)
                if (anc.Name.LocalName == "ControlTemplate") { inTmpl = true; break; }
            // 條件簽名：同條件的狀態之後要跨節點合併
            var cond = CondSignature(t);
            rec.States.Add(new State
            {
                Disabled = IsDisabledTrigger(t),
                FromTemplate = inTmpl,
                Set = set,
                RootTargeted = rootTargeted,
                Cond = cond,
            });
        }
        rec.Key = style.Attribute(XKey)?.Value;
        var bo = style.Attribute("BasedOn")?.Value;
        if (bo is not null)
        {
            var m = ResourceRef().Match(bo);
            if (m.Success) rec.BasedOn = m.Groups["k"].Value;
        }
        return rec;
    }

    /// <summary>
    /// 索引所有檔案的具名 Style —— 含色盤／主題字典（全域樣式就住在那裡；
    /// 「排除出 UI 掃描」不等於「排除出索引」）。查找時同檔優先於全域，
    /// 模擬 WPF 的資源查找順序（CelFlow 的對話框有自己的 DarkBtn，
    /// 與 DarkTheme.xaml 的全域 DarkBtn 同名不同值）。
    /// </summary>
    public static StyleIndex Build(string root)
    {
        var idx = new StyleIndex();
        foreach (var f in PaletteDetector.EnumerateFiles(root, "*.xaml"))
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch { continue; }
            if (doc.Root is null) continue;
            var isGlobal = doc.Root.Name.LocalName is "ResourceDictionary" or "Application";
            foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
            {
                var rec = idx.CreateRecord(style, f);
                if (rec.Key is null)
                {
                    // 隱含樣式：無 x:Key、有 TargetType，自動套用到該型別的每個實例
                    var tt = style.Attribute("TargetType")?.Value;
                    if (tt is null) continue;
                    var m = TargetTypeName().Match(tt);
                    if (!m.Success) continue;
                    var typeName = m.Groups["t"].Value;
                    if (!idx._implicitByFile.TryGetValue(f, out var imap))
                        idx._implicitByFile[f] = imap = new Dictionary<string, Record>(StringComparer.Ordinal);
                    imap[typeName] = rec;
                    if (isGlobal && !idx._implicitGlobal.ContainsKey(typeName)) idx._implicitGlobal[typeName] = rec;
                    continue;
                }
                if (!idx._byFile.TryGetValue(f, out var map))
                    idx._byFile[f] = map = new Dictionary<string, Record>();
                map[rec.Key] = rec;
                if (isGlobal && !idx._global.ContainsKey(rec.Key)) idx._global[rec.Key] = rec;
            }
        }
        return idx;
    }

    private Record? Find(string key, string file)
    {
        if (_byFile.TryGetValue(file, out var map) && map.TryGetValue(key, out var rec)) return rec;
        return _global.GetValueOrDefault(key);
    }

    private Merged MergeChain(Record rec, HashSet<string> seen, string label)
    {
        var merged = new Merged { Label = label };
        if (rec.BasedOn is not null && seen.Add(rec.BasedOn))
        {
            var baseRec = Find(rec.BasedOn, rec.File);
            if (baseRec is not null)
            {
                var m = MergeChain(baseRec, seen, label);
                foreach (var (k, v) in m.Props) merged.Props[k] = v;
                foreach (var s in m.States)
                {
                    if (s.FromTemplate && rec.HasTemplate) continue; // 模板被換掉
                    merged.States.Add(s);
                }
                merged.TemplateRootBg = m.TemplateRootBg;
            }
        }
        foreach (var (p, v) in rec.Setters) merged.Props[p] = (v, rec.Id);
        foreach (var s in rec.States)
        {
            var srcMap = s.Set.Keys.ToDictionary(k => k, _ => rec.Id);
            merged.States.Add(new State
            {
                Disabled = s.Disabled,
                FromTemplate = s.FromTemplate,
                Set = new Dictionary<string, string>(s.Set),
                SetSrc = srcMap,
                RootTargeted = new HashSet<string>(s.RootTargeted),
                Cond = s.Cond,
            });
        }
        if (rec.HasTemplate) merged.TemplateRootBg = rec.TemplateRootBg;
        return merged;
    }

    /// <summary>同條件觸發器合併：WPF 優先序是 style trigger &gt; template trigger、
    /// 衍生（後到）&gt; 基底（先到）。合併時 template 不蓋 style 已設的屬性。</summary>
    private static List<State> MergeSameCondition(List<State> states)
    {
        var byCond = new Dictionary<string, State>();
        var order = new List<string>();
        var anon = 0;
        foreach (var s in states)
        {
            var c = s.Cond ?? $"anon:{++anon}";
            if (!byCond.TryGetValue(c, out var t))
            {
                byCond[c] = new State
                {
                    Disabled = s.Disabled,
                    FromTemplate = s.FromTemplate,
                    Set = new Dictionary<string, string>(s.Set),
                    SetSrc = new Dictionary<string, int>(s.SetSrc),
                    RootTargeted = new HashSet<string>(s.RootTargeted),
                    Cond = s.Cond,
                };
                order.Add(c);
                continue;
            }
            if (s.FromTemplate && !t.FromTemplate)
            {
                // template 觸發只補 style 觸發沒設的屬性
                foreach (var (k, v) in s.Set)
                    if (!t.Set.ContainsKey(k))
                    {
                        t.Set[k] = v; t.SetSrc[k] = s.SetSrc[k];
                        if (s.RootTargeted.Contains(k)) t.RootTargeted.Add(k);
                    }
            }
            else
            {
                foreach (var (k, v) in s.Set)
                {
                    t.Set[k] = v; t.SetSrc[k] = s.SetSrc[k];
                    if (s.RootTargeted.Contains(k)) t.RootTargeted.Add(k); else t.RootTargeted.Remove(k);
                }
                t.FromTemplate = t.FromTemplate && s.FromTemplate;
            }
            t.Disabled = t.Disabled || s.Disabled;
        }
        return order.Select(c => byCond[c]).ToList();
    }

    /// <summary>
    /// 檔案根元素的隱含樣式背景 —— 「地板」。
    ///
    /// 為什麼只做根元素、不做完整的隱含樣式解析：
    ///   完整支援要模擬 WPF 的資源查找（合併字典、Application/Window/元素三層作用域、
    ///   跨組件 pack:// URI），而且 Foreground 是繼承屬性，一旦每個元素都能從隱含樣式
    ///   拿到值，配對數會爆量、假警報跟著來 —— 那是另一個量級的工程（見 ROADMAP）。
    ///
    /// 但外部專案實測指出，覆蓋率的損失幾乎全來自一個很窄的形狀：
    /// 根容器不寫 Background，靠隱含的 &lt;Style TargetType="Window"&gt; 給底，
    /// 於是樹走訪走到頂還是空的，整個檔案的文字全部無底可配。
    /// ScreenToGif：38 個根元素只有 6 個寫了 Background，1295/1373 的 unresolved
    /// 都是「祖先鏈上找不到背景」。四個受測專案則有 71~86% 的根元素直接寫了背景，
    /// 所以這個盲區在它們身上的代價是零 —— 同一個盲區、兩個數量級的差別。
    ///
    /// 只補「根元素」這一格：一個檔案最多影響一個值，不碰繼承語意，
    /// 拿到大部分的覆蓋率而不引入完整解析的雜訊風險。
    /// </summary>
    public string? ImplicitRootBackground(string typeName, string file)
    {
        var rec = (_implicitByFile.TryGetValue(file, out var map) && map.TryGetValue(typeName, out var r))
            ? r : _implicitGlobal.GetValueOrDefault(typeName);
        if (rec is null) return null;
        var merged = MergeChain(rec, new HashSet<string>(), $"Style[implicit {typeName}]");
        return merged.Props.TryGetValue("Background", out var v) ? v.V : merged.TemplateRootBg;
    }

    /// <summary>元素套用的 Style（Style="{StaticResource X}" 或 &lt;X.Style&gt; 行內）。</summary>
    public Merged? GetElementChain(XElement el, string file)
    {
        var sAttr = el.Attribute("Style")?.Value;
        if (sAttr is not null)
        {
            var m = ResourceRef().Match(sAttr);
            if (!m.Success) return null;
            var key = m.Groups["k"].Value;
            var rec = Find(key, file);
            if (rec is null) return null;
            var merged = MergeChain(rec, new HashSet<string> { key }, $"Style[{key}]");
            merged.States = MergeSameCondition(merged.States);
            return merged;
        }
        var propEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == el.Name.LocalName + ".Style");
        var styleEl = propEl?.Elements().FirstOrDefault(e => e.Name.LocalName == "Style");
        if (styleEl is null) return null;
        var inline = CreateRecord(styleEl, file);
        var label = inline.BasedOn is not null ? $"Style[→{inline.BasedOn}]" : "Style[inline]";
        var mergedInline = MergeChain(inline, new HashSet<string>(), label);
        mergedInline.States = MergeSameCondition(mergedInline.States);
        return mergedInline;
    }
}

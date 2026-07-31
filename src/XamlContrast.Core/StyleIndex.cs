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
    }

    internal sealed class Merged
    {
        /// <summary>各屬性最後生效的 Setter（帶來源 Style Id）</summary>
        public Dictionary<string, (string V, int Src)> Props = new();
        public List<State> States = new();
        public required string Label;
    }

    [GeneratedRegex(@"^\{(Dynamic|Static)Resource\s+(?<k>[\w\.]+)\}$")]
    private static partial Regex ResourceRef();

    [GeneratedRegex(@"(^|\.)IsEnabled$")]
    private static partial Regex IsEnabledProp();

    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    private readonly Dictionary<string, Dictionary<string, Record>> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Record> _global = new();
    private int _seq;

    /// <summary>IsEnabled=False 觸發 = 停用態。WCAG 1.4.3 明文豁免停用中的控制項。</summary>
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
        return false;
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
        rec.HasTemplate = style.Descendants().Any(e => e.Name.LocalName == "ControlTemplate");
        foreach (var t in style.Descendants().Where(e => e.Name.LocalName is "Trigger" or "DataTrigger"))
        {
            var set = new Dictionary<string, string>();
            foreach (var s in t.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                var p = s.Attribute("Property")?.Value;
                var v = s.Attribute("Value")?.Value;
                if (p is not null && v is not null && s.Attribute("TargetName") is null) set[p] = v;
            }
            if (set.Count == 0) continue;
            var inTmpl = false;
            for (var anc = t.Parent; anc is not null && !ReferenceEquals(anc, style); anc = anc.Parent)
                if (anc.Name.LocalName == "ControlTemplate") { inTmpl = true; break; }
            // 條件簽名：同條件的狀態之後要跨節點合併
            var cond = t.Name.LocalName == "Trigger"
                ? $"P:{t.Attribute("Property")?.Value}={t.Attribute("Value")?.Value}"
                : t.Attribute("Binding") is { } b && t.Attribute("Value") is { } v2
                    ? $"B:{b.Value}={v2.Value}"
                    : null;
            rec.States.Add(new State
            {
                Disabled = IsDisabledTrigger(t),
                FromTemplate = inTmpl,
                Set = set,
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
                if (rec.Key is null) continue;
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
                Cond = s.Cond,
            });
        }
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
                    Cond = s.Cond,
                };
                order.Add(c);
                continue;
            }
            if (s.FromTemplate && !t.FromTemplate)
            {
                // template 觸發只補 style 觸發沒設的屬性
                foreach (var (k, v) in s.Set)
                    if (!t.Set.ContainsKey(k)) { t.Set[k] = v; t.SetSrc[k] = s.SetSrc[k]; }
            }
            else
            {
                foreach (var (k, v) in s.Set) { t.Set[k] = v; t.SetSrc[k] = s.SetSrc[k]; }
                t.FromTemplate = t.FromTemplate && s.FromTemplate;
            }
            t.Disabled = t.Disabled || s.Disabled;
        }
        return order.Select(c => byCond[c]).ToList();
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

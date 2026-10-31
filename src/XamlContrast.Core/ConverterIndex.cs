using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>轉換器可能回傳的一個固定顏色。Label 是報告與 baseline 鍵裡的寫法（「資源鍵→欄位名」，例：ActiveBrush→On）；
/// Value 是 ColorResolver 認得的字面值（#RRGGBB、#AARRGGBB 或 Transparent）。</summary>
public sealed record ConverterCandidate(string Label, string Value);

/// <summary>一個 IValueConverter 類別的分析結果：Candidates 是 Convert 每一條回傳路徑的固定顏色（全部都固定才有），
/// 否則是 null，NotFixedBecause 說為什麼（給 console 點名）。</summary>
public sealed record ConverterInfo(string Name, string File, int Line, IReadOnlyList<ConverterCandidate>? Candidates, string? NotFixedBecause);

/// <summary>
/// 轉換器回傳的固定顏色（1.2，config 的 <c>converters.constantBrushes</c>，預設關）。
///
/// 「選到時會變色」的按鈕常寫成 <c>Background="{Binding ActiveTool, Converter={StaticResource ActiveBrush}, ConverterParameter=Pen}"</c>，
/// 顏色寫死在 C# 的轉換器裡。工具不執行程式，也不猜條件：只有 <c>Convert</c> 的**每一條**回傳路徑都是用字面值建立的固定 Brush 時，
/// 才把每一個候選色各評一次（等於每個狀態各評一次 —— 不是挑一個猜）。任何一條路徑不是固定字面值（算出來的、
/// <c>DependencyProperty.UnsetValue</c>、<c>Binding.DoNothing</c>、<c>null</c>、讀屬性或外部的值），整個轉換器照舊是 bound-or-gradient。
///
/// 認得的字面值：<c>Brushes.X</c>、<c>new SolidColorBrush(Colors.X)</c>、<c>new SolidColorBrush(Color.FromRgb(r, g, b))</c>、
/// <c>Color.FromArgb(a, r, g, b)</c>（整數字面值 0–255，十進位或 0x），以及同一個類別裡只指派一次的 readonly 欄位、
/// 唯讀屬性（<c>static readonly SolidColorBrush On = new(Color.FromRgb(…))</c>）。條件不評估 —— <c>a ? On : Off</c>、
/// <c>if … return On; return Off;</c>、switch 運算式的每一臂都是候選。
///
/// 這是靜態的文字分析，不是編譯器：看不懂的寫法一律「不是固定的」，往 unresolved 那邊錯。
/// ⚠ 讀 *.cs 找轉換器等於擴大稽核範圍（凍結面第 9 面）：只在 config 打開時做，預設範圍不變。
/// </summary>
internal sealed partial class ConverterIndex
{
    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    private readonly Dictionary<string, ConverterInfo> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _keysByFile = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>ResourceDictionary／Application 裡宣告的轉換器資源：鍵 → 類別名；同一個鍵在兩個地方指到不同類別是 null（不猜是哪一個）。</summary>
    private readonly Dictionary<string, string?> _globalKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, string Class), IReadOnlyList<ConverterCandidate>?> _labelled = new();
    private readonly Dictionary<string, (string Key, ConverterInfo Info)> _used = new(StringComparer.Ordinal);

    /// <summary>這次稽核用到的轉換器（Binding 裡出現、拿來當顏色的），依資源鍵 —— console 點名哪些固定、哪些不是。</summary>
    public IEnumerable<(string Key, ConverterInfo Info)> Used => _used.Values.OrderBy(u => u.Key, StringComparer.Ordinal);

    /// <summary>所有分析過的轉換器類別（測試與除錯用）。</summary>
    public IReadOnlyDictionary<string, ConverterInfo> Classes => _byName;

    public static ConverterIndex Build(string root)
    {
        var idx = new ConverterIndex();
        foreach (var f in PaletteDetector.EnumerateFiles(root, "*.cs"))
        {
            string text;
            try { text = File.ReadAllText(f); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; } // 讀不到的 .cs 已經由色盤偵測點名
            foreach (var info in Analyze(text, Path.GetRelativePath(root, f).Replace('\\', '/')))
            {
                // 同名的類別出現兩次（不同命名空間）：XAML 用的是哪一個要看 xmlns 對應，不猜
                if (idx._byName.TryGetValue(info.Name, out var dup))
                    idx._byName[info.Name] = dup with { Candidates = null, NotFixedBecause = $"more than one class is named {info.Name}" };
                else idx._byName[info.Name] = info;
            }
        }
        if (idx._byName.Count == 0) return idx;

        foreach (var f in PaletteDetector.EnumerateFiles(root, "*.xaml"))
        {
            XDocument doc;
            try { doc = XDocument.Load(f); }
            catch { continue; } // 解析失敗由稽核本身點名
            if (doc.Root is null) continue;
            var isGlobal = doc.Root.Name.LocalName == "ResourceDictionary" || PaletteDetector.IsApplicationRoot(doc.Root);
            foreach (var el in doc.Descendants())
            {
                if (el.Attribute(XKey)?.Value is not { } key || !idx._byName.ContainsKey(el.Name.LocalName)) continue;
                if (!idx._keysByFile.TryGetValue(f, out var map)) idx._keysByFile[f] = map = new(StringComparer.Ordinal);
                map.TryAdd(key, el.Name.LocalName);
                if (!isGlobal) continue;
                if (!idx._globalKeys.TryGetValue(key, out var existing)) idx._globalKeys[key] = el.Name.LocalName;
                else if (existing != el.Name.LocalName) idx._globalKeys[key] = null;
            }
        }
        return idx;
    }

    [GeneratedRegex(@"^Converter\s*=\s*\{\s*(?:Static|Dynamic)Resource\s+(?<k>[\w.]+)\s*\}$")]
    private static partial Regex ConverterArg();

    /// <summary>
    /// 這個屬性值（XAML 原文）若是 <c>{Binding …, Converter={StaticResource K}}</c>、K 是固定顏色的轉換器，回傳它的候選色（標上 K）；
    /// 其他一律 null。FallbackValue／TargetNullValue 會在執行期給出另一個顏色 → 不是只有轉換器那幾個 → null。
    /// <paramref name="xamlFile"/> 是完整路徑：同檔的資源優先，其次 ResourceDictionary／App 的。
    /// </summary>
    public IReadOnlyList<ConverterCandidate>? CandidatesFor(string raw, string xamlFile)
    {
        var v = raw.Trim();
        if (!v.StartsWith("{Binding", StringComparison.Ordinal) || !v.EndsWith('}')) return null;
        if (v.Length > "{Binding".Length && !char.IsWhiteSpace(v["{Binding".Length]) && v["{Binding".Length] != '}') return null; // {BindingX …}
        string? key = null;
        foreach (var arg in SplitTopLevel(v["{Binding".Length..^1]))
        {
            var a = arg.Trim();
            if (a.StartsWith("FallbackValue", StringComparison.Ordinal) || a.StartsWith("TargetNullValue", StringComparison.Ordinal)) return null;
            if (ConverterArg().Match(a) is { Success: true } m) key = m.Groups["k"].Value;
        }
        if (key is null) return null;
        var cls = _keysByFile.TryGetValue(xamlFile, out var map) && map.TryGetValue(key, out var local) ? local
                : _globalKeys.GetValueOrDefault(key);
        if (cls is null || !_byName.TryGetValue(cls, out var info)) return null;
        _used.TryAdd(key, (key, info));
        if (info.Candidates is null) return null;
        if (!_labelled.TryGetValue((key, cls), out var labelled))
            _labelled[(key, cls)] = labelled = [.. info.Candidates.Select(c => c with { Label = $"{key}→{c.Label}" })];
        return labelled;
    }

    /// <summary>markup extension 的參數：只在最外層（不在 {…} 裡）的逗號切開。</summary>
    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') depth--;
            else if (s[i] == ',' && depth == 0) { parts.Add(s[start..i]); start = i + 1; }
        }
        parts.Add(s[start..]);
        return parts;
    }

    // ── C# 的部分：不是編譯器，只認得一小撮寫法；認不得 = 不是固定的 ──

    [GeneratedRegex(@"\bclass\s+(?<name>[A-Za-z_]\w*)(?<rest>[^{;]*)\{")]
    private static partial Regex ClassDecl();

    [GeneratedRegex(@"\bIValueConverter\b")]
    private static partial Regex ImplementsConverter();

    [GeneratedRegex(@"\bobject\s*\??\s+(?:[\w.]+\.)?Convert\s*\(")]
    private static partial Regex ConvertDecl();

    /// <summary>一個 .cs 檔裡每一個實作 IValueConverter 的類別。</summary>
    internal static IEnumerable<ConverterInfo> Analyze(string source, string file)
    {
        var s = Mask(source);
        foreach (Match m in ClassDecl().Matches(s))
        {
            if (!ImplementsConverter().IsMatch(m.Groups["rest"].Value)) continue;
            var name = m.Groups["name"].Value;
            var line = 1 + s.AsSpan(0, m.Index).Count('\n');
            var open = m.Index + m.Length - 1;
            var close = MatchBracket(s, open);
            if (close < 0) { yield return new(name, file, line, null, "could not read the class body"); continue; }
            var body = s[(open + 1)..close];
            var (candidates, why) = DeclarationIsPartial(s, m.Index)
                ? (null, "it is a partial class")
                : AnalyzeClass(body, name);
            yield return new(name, file, line, candidates, why);
        }
    }

    /// <summary>class 關鍵字前面（到上一個 ; { } 為止）的修飾詞裡有 partial。</summary>
    private static bool DeclarationIsPartial(string s, int classAt)
    {
        var start = s.LastIndexOfAny([';', '{', '}'], Math.Max(0, classAt - 1)) + 1;
        return Regex.IsMatch(s[start..classAt], @"\bpartial\b");
    }

    private static (IReadOnlyList<ConverterCandidate>? Candidates, string? Why) AnalyzeClass(string body, string className)
    {
        var decls = ConvertDecl().Matches(body);
        if (decls.Count == 0) return (null, "no Convert method found");
        if (decls.Count > 1) return (null, "more than one Convert method");
        var paren = decls[0].Index + decls[0].Length - 1;
        var after = MatchBracket(body, paren);
        if (after < 0) return (null, "could not read Convert");
        var i = after + 1;
        while (i < body.Length && char.IsWhiteSpace(body[i])) i++;

        var returns = new List<string>();
        string methodBody;
        if (i + 1 < body.Length && body[i] == '=' && body[i + 1] == '>')
        {
            var end = FindTopLevel(body, i + 2, body.Length, ';');
            if (end < 0) return (null, "could not read Convert");
            methodBody = body[(i + 2)..end];
            returns.Add(methodBody);
        }
        else if (i < body.Length && body[i] == '{')
        {
            var end = MatchBracket(body, i);
            if (end < 0) return (null, "could not read Convert");
            methodBody = body[(i + 1)..end];
            if (Regex.IsMatch(methodBody, @"\b(?:delegate|yield|goto)\b")) return (null, "Convert uses a delegate, yield or goto");
            if (HasLambda(methodBody)) return (null, "Convert contains a lambda");
            foreach (Match r in Regex.Matches(methodBody, @"\breturn\b"))
            {
                var end2 = FindTopLevel(methodBody, r.Index + r.Length, methodBody.Length, ';');
                if (end2 < 0) return (null, "could not read a return statement");
                returns.Add(methodBody[(r.Index + r.Length)..end2]);
            }
            if (returns.Count == 0) return (null, "Convert has no return statement");
        }
        else return (null, "could not read Convert");

        var ctx = new Context(body, className);
        var all = new List<ConverterCandidate>();
        foreach (var r in returns)
        {
            var (values, why) = ctx.Brush(r, depth: 0, targetIsSolidBrush: false);
            if (values is null) return (null, why);
            foreach (var v in values)
                if (!all.Any(a => a.Value == v.Value)) all.Add(v);
        }
        return all.Count == 0 ? (null, "Convert never returns a colour") : (all, null);
    }

    /// <summary>Convert 裡的 =&gt;：switch 運算式的臂（緊接在 switch { 的大括號裡）不算 lambda。</summary>
    private static bool HasLambda(string s)
    {
        var armBraces = new HashSet<int>();
        foreach (Match m in Regex.Matches(s, @"\bswitch\s*\{")) armBraces.Add(m.Index + m.Length - 1);
        var stack = new Stack<int>();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') stack.Push(i);
            else if (s[i] == '}' && stack.Count > 0) stack.Pop();
            else if (s[i] == '=' && i + 1 < s.Length && s[i + 1] == '>')
            {
                if (stack.Count == 0 || !armBraces.Contains(stack.Peek())) return true;
            }
        }
        return false;
    }

    /// <summary>一個類別裡的運算式求值：只認得固定的 Brush／Color 字面值。</summary>
    private sealed class Context(string classBody, string className)
    {
        private const int MaxDepth = 6;

        public (List<ConverterCandidate>? Values, string? Why) Brush(string expr, int depth, bool targetIsSolidBrush)
        {
            if (depth > MaxDepth) return (null, "the value goes through too many fields");
            var e = Unwrap(expr);
            if (e.Length == 0) return (null, "returns nothing");
            if (Regex.IsMatch(e, @"^throw\b")) return ([], null); // 丟例外：沒有值

            // a ? b : c —— 條件不評估，兩邊都是候選
            if (SplitTernary(e) is { } t)
                return Union(Brush(t.A, depth, targetIsSolidBrush), Brush(t.B, depth, targetIsSolidBrush));
            // a ?? b —— 兩邊都要是固定的
            if (SplitCoalesce(e) is { } c)
                return Union(Brush(c.A, depth, targetIsSolidBrush), Brush(c.B, depth, targetIsSolidBrush));
            // x switch { … => a, _ => b } —— 每一臂都是候選
            if (SwitchArms(e) is { } arms)
            {
                var acc = (Values: (List<ConverterCandidate>?)[], Why: (string?)null);
                foreach (var arm in arms) acc = Union(acc, Brush(arm, depth, targetIsSolidBrush));
                return acc;
            }
            // (Brush)x
            if (Regex.Match(e, @"^\(\s*[A-Za-z_][\w.]*\s*\)\s*(?<x>[^\s].*)$", RegexOptions.Singleline) is { Success: true } cast &&
                !cast.Groups["x"].Value.StartsWith('?'))
                return Brush(cast.Groups["x"].Value, depth, targetIsSolidBrush);

            if (e is "null") return (null, "returns null");
            if (Regex.IsMatch(e, @"^(?:[\w.]+\.)?DependencyProperty\.UnsetValue$")) return (null, "returns DependencyProperty.UnsetValue");
            if (Regex.IsMatch(e, @"^(?:[\w.]+\.)?Binding\.DoNothing$")) return (null, "returns Binding.DoNothing");

            if (Regex.Match(e, @"^(?:System\.Windows\.Media\.)?Brushes\.(?<n>\w+)$") is { Success: true } b)
                return NamedColour(b.Groups["n"].Value) is { } hex ? ([new($"Brushes.{b.Groups["n"].Value}", hex)], null) : (null, $"unknown colour Brushes.{b.Groups["n"].Value}");

            if (Regex.Match(e, @"^new\s+(?:System\.Windows\.Media\.)?SolidColorBrush\s*\(", RegexOptions.Singleline) is { Success: true } nb)
                return NewSolidBrush(e, nb.Index + nb.Length - 1, depth);
            if (targetIsSolidBrush && Regex.Match(e, @"^new\s*\(") is { Success: true } tn)
                return NewSolidBrush(e, tn.Index + tn.Length - 1, depth);

            if (Member(e) is { } name)
            {
                var (init, isSolidBrush, why) = FindMember(name);
                if (init is null) return (null, why);
                var (values, why2) = Brush(init, depth + 1, isSolidBrush);
                // 欄位裡的固定 Brush 以欄位名當標籤（On／Off），比色碼好讀，色碼改了鍵也不變（惡化由 worst 抓）
                return values is null ? (null, why2) : ([.. values.Select(v => v with { Label = name })], null);
            }
            return (null, "returns a value that is computed, not a fixed brush");
        }

        private (List<ConverterCandidate>? Values, string? Why) NewSolidBrush(string e, int open, int depth)
        {
            var close = MatchBracket(e, open);
            if (close < 0) return (null, "could not read new SolidColorBrush(…)");
            if (close != e.Length - 1) return (null, "the brush is changed after it is created (an object initializer or a call)");
            var arg = e[(open + 1)..close];
            if (FindTopLevel(arg, 0, arg.Length, ',') >= 0 || arg.Trim().Length == 0) return (null, "new SolidColorBrush without a single colour");
            var (hex, label, why) = Colour(arg, depth);
            return hex is null ? (null, why) : ([new(label!, hex)], null);
        }

        /// <summary>Color 的字面值：Colors.X、Color.FromRgb(r, g, b)、Color.FromArgb(a, r, g, b)，或指到這種值的 readonly 欄位。</summary>
        private (string? Hex, string? Label, string? Why) Colour(string expr, int depth)
        {
            if (depth > MaxDepth) return (null, null, "the value goes through too many fields");
            var e = Unwrap(expr);
            if (Regex.Match(e, @"^(?:System\.Windows\.Media\.)?Colors\.(?<n>\w+)$") is { Success: true } named)
                return NamedColour(named.Groups["n"].Value) is { } hex ? (hex, $"Colors.{named.Groups["n"].Value}", null) : (null, null, $"unknown colour Colors.{named.Groups["n"].Value}");
            if (Regex.Match(e, @"^(?:System\.Windows\.Media\.)?Color\.From(?<kind>Rgb|Argb)\s*\((?<args>.*)\)$", RegexOptions.Singleline) is { Success: true } from &&
                MatchBracket(e, e.IndexOf('(')) == e.Length - 1)
            {
                var parts = from.Groups["args"].Value.Split(',');
                var want = from.Groups["kind"].Value == "Rgb" ? 3 : 4;
                if (parts.Length != want) return (null, null, $"Color.From{from.Groups["kind"].Value} with {parts.Length} arguments");
                var bytes = new int[want];
                for (var i = 0; i < want; i++)
                    if (ByteLiteral(parts[i]) is { } v) bytes[i] = v;
                    else return (null, null, "a colour channel that is not a literal number 0-255");
                var hex = want == 3
                    ? string.Create(CultureInfo.InvariantCulture, $"#{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}")
                    : bytes[0] == 255
                        ? string.Create(CultureInfo.InvariantCulture, $"#{bytes[1]:X2}{bytes[2]:X2}{bytes[3]:X2}")
                        : string.Create(CultureInfo.InvariantCulture, $"#{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}{bytes[3]:X2}");
                return (hex, hex, null);
            }
            if (Member(e) is { } name)
            {
                var (init, _, why) = FindMember(name);
                if (init is null) return (null, null, why);
                var (hex, _, why2) = Colour(init, depth + 1);
                return hex is null ? (null, null, why2) : (hex, name, null);
            }
            return (null, null, "a colour that is computed, not a literal");
        }

        /// <summary>這個類別自己的成員名（On、this.On、類別名.On）；其他（外部類別、方法呼叫）是 null。</summary>
        private string? Member(string e)
        {
            var m = Regex.Match(e, @"^(?:(?<owner>this|[A-Za-z_]\w*)\.)?(?<name>[A-Za-z_]\w*)$");
            if (!m.Success) return null;
            if (m.Groups["owner"].Success && m.Groups["owner"].Value != "this" && m.Groups["owner"].Value != className) return null;
            var name = m.Groups["name"].Value;
            return name is "value" or "parameter" or "targetType" or "culture" or "null" or "true" or "false" ? null : name;
        }

        /// <summary>
        /// 成員的初始值：readonly 欄位（只能在宣告處指派這一次）、只有 get 的自動屬性（有初始值、類別裡沒有再指派）、
        /// =&gt; 的唯讀屬性。可以被改的（沒有 readonly 的欄位、有 set 的屬性、建構式或其他地方再指派）不是固定的。
        /// </summary>
        private (string? Init, bool IsSolidBrush, string? Why) FindMember(string name)
        {
            var n = Regex.Escape(name);
            const string Mods = @"(?<mods>(?:(?:public|private|protected|internal|static|readonly|new)\s+)+)";
            const string Type = @"(?<type>[A-Za-z_][\w.]*(?:<[^;=(){}]*>)?\??)";
            var assignments = Regex.Matches(classBody, $@"(?<![\w.]){n}\s*(?:=(?![=>])|\+=|-=|\?\?=)").Count +
                              Regex.Matches(classBody, $@"\bthis\.{n}\s*=(?![=>])").Count;

            if (Regex.Match(classBody, $@"(?<![\w.]){Mods}{Type}\s+{n}\s*=(?![=>])\s*(?<init>[^;]*);") is { Success: true } field)
            {
                if (!Regex.IsMatch(field.Groups["mods"].Value, @"\breadonly\b")) return (null, false, $"{name} is not readonly");
                if (assignments != 1) return (null, false, $"{name} is assigned somewhere else too");
                return (field.Groups["init"].Value, IsSolidBrushType(field.Groups["type"].Value), null);
            }
            if (Regex.Match(classBody, $@"(?<![\w.]){Mods}{Type}\s+{n}\s*\{{\s*get\s*;\s*\}}\s*=\s*(?<init>[^;]*);") is { Success: true } auto)
            {
                if (assignments != 0) return (null, false, $"{name} is assigned somewhere else too");
                return (auto.Groups["init"].Value, IsSolidBrushType(auto.Groups["type"].Value), null);
            }
            if (Regex.Match(classBody, $@"(?<![\w.]){Mods}{Type}\s+{n}\s*=>\s*(?<init>[^;]*);") is { Success: true } expr)
                return (expr.Groups["init"].Value, IsSolidBrushType(expr.Groups["type"].Value), null);
            return (null, false, $"{name} is not a readonly field or get-only property of {className}");
        }

        private static bool IsSolidBrushType(string type) => Regex.IsMatch(type, @"(?:^|\.)SolidColorBrush\??$");
    }

    private static (List<ConverterCandidate>? Values, string? Why) Union(
        (List<ConverterCandidate>? Values, string? Why) a, (List<ConverterCandidate>? Values, string? Why) b)
        => a.Values is null ? a : b.Values is null ? b : ([.. a.Values, .. b.Values], null);

    private static string? NamedColour(string name)
        => name.Equals("Transparent", StringComparison.OrdinalIgnoreCase) ? "Transparent" : ColorResolver.NamedHex(name);

    /// <summary>0–255 的整數字面值：十進位或 0x，可以帶 (byte) 轉型。</summary>
    private static int? ByteLiteral(string s)
    {
        var t = Regex.Replace(s.Trim(), @"^\(\s*byte\s*\)\s*", "");
        int v;
        if (Regex.IsMatch(t, @"^0[xX][0-9A-Fa-f]{1,2}$")) v = int.Parse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        else if (Regex.IsMatch(t, @"^\d{1,3}$")) v = int.Parse(t, CultureInfo.InvariantCulture);
        else return null;
        return v <= 255 ? v : null;
    }

    /// <summary>拿掉前後空白與「整個包起來」的括號。</summary>
    private static string Unwrap(string expr)
    {
        var e = expr.Trim();
        while (e.Length >= 2 && e[0] == '(' && MatchBracket(e, 0) == e.Length - 1) e = e[1..^1].Trim();
        return e;
    }

    /// <summary>最外層的條件運算子 a ? b : c（不含 ?.、??、?[ 與型別後的 ?）。</summary>
    private static (string A, string B)? SplitTernary(string e)
    {
        var depth = 0;
        for (var i = 0; i < e.Length; i++)
        {
            var ch = e[i];
            if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}') depth--;
            else if (ch == '?' && depth == 0 && IsTernaryQuestion(e, i))
            {
                var nested = 0;
                var d = 0;
                for (var j = i + 1; j < e.Length; j++)
                {
                    var cj = e[j];
                    if (cj is '(' or '[' or '{') d++;
                    else if (cj is ')' or ']' or '}') d--;
                    else if (d == 0 && cj == '?' && IsTernaryQuestion(e, j)) nested++;
                    else if (d == 0 && cj == ':' && !(j + 1 < e.Length && e[j + 1] == ':') && !(j > 0 && e[j - 1] == ':'))
                    {
                        if (nested == 0) return (e[(i + 1)..j], e[(j + 1)..]);
                        nested--;
                    }
                }
                return null;
            }
        }
        return null;
    }

    private static bool IsTernaryQuestion(string e, int i)
        => !(i + 1 < e.Length && e[i + 1] is '.' or '?' or '[' or '>' or ')' or ',') && !(i > 0 && e[i - 1] == '?');

    /// <summary>最外層的 a ?? b。</summary>
    private static (string A, string B)? SplitCoalesce(string e)
    {
        var depth = 0;
        for (var i = 0; i + 1 < e.Length; i++)
        {
            var ch = e[i];
            if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}') depth--;
            else if (depth == 0 && ch == '?' && e[i + 1] == '?' && !(i + 2 < e.Length && e[i + 2] == '='))
                return (e[..i], e[(i + 2)..]);
        }
        return null;
    }

    /// <summary>x switch { p =&gt; a, _ =&gt; b } 的每一臂的結果（含 when 的臂）；不是 switch 運算式是 null。</summary>
    private static List<string>? SwitchArms(string e)
    {
        var m = Regex.Match(e, @"\bswitch\s*\{");
        if (!m.Success) return null;
        var open = m.Index + m.Length - 1;
        if (MatchBracket(e, open) != e.Length - 1) return null;
        // switch 前面那一段要在最外層（不是包在括號裡的別的東西）
        if (FindTopLevel(e, 0, m.Index, '(') >= 0 && MatchBracket(e, FindTopLevel(e, 0, m.Index, '(')) > m.Index) return null;
        var inner = e[(open + 1)..^1];
        var arms = new List<string>();
        var start = 0;
        while (start < inner.Length)
        {
            var end = FindTopLevel(inner, start, inner.Length, ',');
            if (end < 0) end = inner.Length;
            var arm = inner[start..end];
            if (arm.Trim().Length > 0)
            {
                var arrow = arm.IndexOf("=>", StringComparison.Ordinal);
                if (arrow < 0) return null;
                arms.Add(arm[(arrow + 2)..]);
            }
            start = end + 1;
        }
        return arms;
    }

    /// <summary>從 from 到 to 之間，括號深度 0 的第一個 ch；沒有是 -1。</summary>
    private static int FindTopLevel(string s, int from, int to, char ch)
    {
        var depth = 0;
        for (var i = from; i < to; i++)
        {
            var c = s[i];
            if (c == ch && depth == 0) return i;
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
        }
        return -1;
    }

    /// <summary>s[open] 是 ( [ {：回傳對應的右括號位置；對不上是 -1。（字串內容已經遮掉，不會干擾。）</summary>
    private static int MatchBracket(string s, int open)
    {
        if (open < 0 || open >= s.Length) return -1;
        var (o, c) = s[open] switch { '(' => ('(', ')'), '[' => ('[', ']'), '{' => ('{', '}'), _ => ('\0', '\0') };
        if (o == '\0') return -1;
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == o) depth++;
            else if (s[i] == c && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// 註解換成空白、字串與字元常值的內容換成空白（引號留著；換行保留，行號不變）—— 之後的括號比對與比對式
    /// 不會被字串裡的 { ( ; 或註解掉的程式碼騙到。認得 // /* */、"…"（含跳脫）、@"…"、$"…"、"""…"""、'x'。
    /// </summary>
    internal static string Mask(string src)
    {
        var sb = new StringBuilder(src.Length);
        var i = 0;
        while (i < src.Length)
        {
            var ch = src[i];
            var next = i + 1 < src.Length ? src[i + 1] : '\0';
            if (ch == '/' && next == '/')
            {
                while (i < src.Length && src[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }
            if (ch == '/' && next == '*')
            {
                sb.Append("  "); i += 2;
                while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/')) { sb.Append(src[i] == '\n' ? '\n' : ' '); i++; }
                if (i < src.Length) { sb.Append("  "); i += 2; }
                continue;
            }
            // 字串的前綴：$ @ 的任意組合
            var p = i;
            while (p < src.Length && src[p] is '$' or '@') p++;
            if (p < src.Length && src[p] == '"')
            {
                var verbatim = src.AsSpan(i, p - i).Contains('@');
                var quotes = 0;
                while (p + quotes < src.Length && src[p + quotes] == '"') quotes++;
                for (var k = i; k < p; k++) sb.Append(src[k]);
                if (quotes >= 3)
                {
                    // raw string：同樣數量的引號結束
                    sb.Append('"', quotes); i = p + quotes;
                    var closing = new string('"', quotes);
                    var endAt = src.IndexOf(closing, i, StringComparison.Ordinal);
                    if (endAt < 0) endAt = src.Length;
                    for (; i < endAt; i++) sb.Append(src[i] == '\n' ? '\n' : ' ');
                    if (i < src.Length) { sb.Append('"', quotes); i += quotes; }
                    continue;
                }
                if (quotes == 2) { sb.Append("\"\""); i = p + 2; continue; } // 空字串
                sb.Append('"'); i = p + 1;
                while (i < src.Length)
                {
                    if (verbatim && src[i] == '"' && i + 1 < src.Length && src[i + 1] == '"') { sb.Append("  "); i += 2; continue; }
                    if (!verbatim && src[i] == '\\' && i + 1 < src.Length) { sb.Append("  "); i += 2; continue; }
                    if (src[i] == '"') break;
                    sb.Append(src[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i < src.Length) { sb.Append('"'); i++; }
                continue;
            }
            if (ch == '\'')
            {
                // 字元常值 'x'、'\n'、'\''
                var end = i + 1;
                if (end < src.Length && src[end] == '\\') end += 2; else end += 1;
                while (end < src.Length && src[end] != '\'' && end - i < 10) end++;
                if (end < src.Length && src[end] == '\'')
                {
                    sb.Append('\''); sb.Append(' ', end - i - 1); sb.Append('\'');
                    i = end + 1;
                    continue;
                }
            }
            sb.Append(ch);
            i++;
        }
        return sb.ToString();
    }
}

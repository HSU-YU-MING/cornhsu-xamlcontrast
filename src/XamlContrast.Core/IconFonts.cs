namespace XamlContrast.Core;

/// <summary>
/// 圖示字型（1.2）：用圖示字型畫出來的 TextBlock／Run／Button 內容是圖形（播放鈕、齒輪、箭頭），不是文字 ——
/// WCAG 對它的要求是 1.4.11 非文字對比 3:1，不是 1.4.3 的 4.5:1。⚠ 1.1 之前一律當文字，用 4.5:1 要求圖示。
///
/// 跟 <c>Auditor.IsNonTextElement</c> 的非文字元素（Rectangle 當分隔線、ProgressBar 的填色）不一樣：那些是裝飾、不設門檻
/// （decorative）；圖示多半是「看懂畫面要用到」的圖形，3:1 是條文的要求。把圖示歸成 decorative 會讓 1.2:1 的圖示整個從報告消失。
///
/// 只認得出來的才算：FontFamily 的每一個候選字型（逗號分隔的後備清單）都是已知的圖示字型才是圖示；認不出來的值
/// （Binding、找不到的資源、混了一般字型的後備清單）照舊當文字、用 4.5 —— 往嚴格那邊錯，不替使用者放過。
/// </summary>
public static class IconFonts
{
    /// <summary>名稱完全相同（不分大小寫）就是圖示字型。</summary>
    private static readonly string[] Exact = ["Segoe MDL2 Assets", "Segoe Fluent Icons", "Segoe UI Symbol"];

    /// <summary>名稱以這些開頭（不分大小寫）就是圖示字型：Material Icons Outlined／Round、Material Symbols Rounded、
    /// Font Awesome 6 Free Solid、FontAwesome（4.x 的寫法）之類的變體。</summary>
    private static readonly string[] Prefixes = ["Material Icons", "Material Symbols", "Font Awesome", "FontAwesome"];

    /// <summary>
    /// 這個 FontFamily 值是不是圖示字型。認得的寫法：<c>字型名</c>、<c>/Fonts/#字型名</c>、<c>./Fonts/#字型名</c>、
    /// <c>pack://application:,,,/組件;component/Fonts/#字型名</c>、逗號分隔的後備清單（每一個都要是圖示字型），
    /// 以及 <c>{StaticResource K}</c>／<c>{DynamicResource K}</c> —— K 在 <paramref name="resources"/>
    /// （專案裡 <c>&lt;FontFamily x:Key="K"&gt;值&lt;/FontFamily&gt;</c> 的定義）找得到才算。
    /// </summary>
    public static bool IsIconFont(string? fontFamily, IReadOnlyDictionary<string, string>? resources = null)
    {
        var value = fontFamily?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        if (value.StartsWith('{'))
        {
            // 資源引用只解一層：資源裡再引用資源（或 Binding）就是認不出來 → 當文字
            return ResourceKey(value) is { } key && resources?.GetValueOrDefault(key) is { } resolved &&
                   !resolved.TrimStart().StartsWith('{') && IsIconFont(resolved);
        }
        var names = FamilyNames(value).ToList();
        return names.Count > 0 && names.All(IsIconFontName);
    }

    /// <summary>單一字型名稱（已拿掉位置與 #）是不是已知的圖示字型。</summary>
    public static bool IsIconFontName(string name)
        => Exact.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)) ||
           Prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 後備清單裡每一個候選的字型名稱：逗號分隔；有 # 的取最後一個 # 之後（前面是字型檔的位置）。
    /// ⚠ pack URI 本身帶逗號（<c>application:,,,</c>）：先把連續三個逗號換掉再切，否則一個 pack URI 會被切成
    /// 「pack://application:」與後半段，前半段當成一般字型 → 整串被判成文字。
    /// </summary>
    internal static IEnumerable<string> FamilyNames(string value)
    {
        const string placeholder = "\u0001\u0001\u0001";
        foreach (var part in value.Replace(",,,", placeholder, StringComparison.Ordinal).Split(','))
        {
            var token = part.Replace(placeholder, ",,,", StringComparison.Ordinal).Trim().Trim('\'', '"').Trim();
            var hash = token.LastIndexOf('#');
            var name = (hash >= 0 ? token[(hash + 1)..] : token).Trim();
            if (name.Length > 0) yield return name;
        }
    }

    /// <summary>{StaticResource K}／{DynamicResource K}／{ThemeResource K}（WinUI，1.4）的 K；其他 markup（Binding 之類）是 null。</summary>
    private static string? ResourceKey(string value)
    {
        foreach (var prefix in (ReadOnlySpan<string>)["{StaticResource ", "{DynamicResource ", "{ThemeResource "])
            if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith('}'))
                return value[prefix.Length..^1].Trim() is { Length: > 0 } k && !k.Contains(' ') && !k.Contains('{') ? k : null;
        return null;
    }
}

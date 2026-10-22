using System.Globalization;
using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>
/// 顏色屬性的兩種寫法：屬性語法（<c>Background="#FFF"</c>）與屬性元素語法
/// （<c>&lt;Border.Background&gt;&lt;SolidColorBrush Color="#FFF"/&gt;&lt;/Border.Background&gt;</c>）。
///
/// ⚠ 舊版只讀屬性。&lt;Border.Background&gt; 是 WPF 很常見的寫法 —— 被忽略之後漸層底的文字
///   配到祖先的底，給出篤定但錯誤的 ok；&lt;TextBlock.Foreground&gt; 那組更是整組消失
///   （pairs、unresolved、skipped 全是 0），Style 的 &lt;Setter.Value&gt; 同理。
///
/// 回傳的字串交給 <see cref="ColorResolver.Resolve"/>：
///   - SolidColorBrush → 它的 Color（帶 Opacity 而 Color 是字面色時折成 #AARRGGBB，走半透明合成）
///   - &lt;StaticResource／DynamicResource ResourceKey="K"/&gt; → {DynamicResource K}
///   - 其他 brush（漸層、ImageBrush、VisualBrush…）與看不懂的 Opacity → "&lt;名稱…&gt;"，
///     解析成 Other（bound-or-gradient）—— 執行期才知道，不猜
/// </summary>
internal static class XamlBrush
{
    /// <summary>元素上的顏色屬性：屬性語法優先，其次 &lt;元素名.屬性&gt; 屬性元素。</summary>
    public static string? Read(XElement el, string prop)
    {
        var a = el.Attribute(prop)?.Value;
        if (a is not null) return a;
        var pe = PropertyElement(el, prop);
        return pe is null ? null : FromPropertyElement(pe);
    }

    /// <summary>Setter 的值：Value 屬性，或 &lt;Setter.Value&gt; 屬性元素。</summary>
    public static string? SetterValue(XElement setter)
    {
        var a = setter.Attribute("Value")?.Value;
        if (a is not null) return a;
        var pe = setter.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value");
        return pe is null ? null : FromPropertyElement(pe);
    }

    /// <summary>這個子元素是不是「本元素的顏色屬性元素」—— 它是屬性值，不是視覺子樹。
    /// 屬性元素可以用基底類別命名（&lt;Panel.Background&gt; 放在 Grid 底下、&lt;Control.Foreground&gt; 放在 Button 底下）。</summary>
    public static bool IsColorPropertyElement(XElement owner, XElement child)
        => PropertyName(child) is "Background" or "Foreground" or "Fill";

    /// <summary>「型別名.屬性」形式的元素名 → 屬性名；不是屬性元素回傳 null。</summary>
    private static string? PropertyName(XElement child)
    {
        var n = child.Name.LocalName;
        var dot = n.IndexOf('.');
        return dot > 0 && dot == n.LastIndexOf('.') && dot < n.Length - 1 ? n[(dot + 1)..] : null;
    }

    /// <summary>元素的屬性元素：先找「元素自己的型別名.屬性」，再找用基底類別命名的（Panel.Background）。
    /// ⚠ 舊版只認前者，&lt;Panel.Background&gt; 被忽略、字配到祖先的底（白字白底報成 21:1）。</summary>
    private static XElement? PropertyElement(XElement el, string prop)
        => el.Elements().FirstOrDefault(e => e.Name.LocalName == el.Name.LocalName + "." + prop)
           ?? el.Elements().FirstOrDefault(e => PropertyName(e) == prop);

    private static string? FromPropertyElement(XElement pe)
    {
        var child = pe.Elements().FirstOrDefault();
        if (child is null)
        {
            // <TextBlock.Foreground>White</TextBlock.Foreground>：型別轉換器吃文字內容
            var text = pe.Value.Trim();
            return text.Length > 0 ? text : null;
        }
        var name = child.Name.LocalName;
        if (name == "SolidColorBrush") return SolidBrush(child);
        if (name is "StaticResource" or "DynamicResource" && child.Attribute("ResourceKey")?.Value is { } key)
            return "{" + name + " " + key + "}";
        return "<" + name + ">";
    }

    private static string SolidBrush(XElement brush)
    {
        // Color 也可能是 <SolidColorBrush.Color><Color>#…</Color></SolidColorBrush.Color>
        var color = brush.Attribute("Color")?.Value
                    ?? brush.Elements().FirstOrDefault(e => e.Name.LocalName == "SolidColorBrush.Color")?.Value.Trim();
        if (string.IsNullOrEmpty(color)) return "<SolidColorBrush>";
        var opRaw = brush.Attribute("Opacity")?.Value;
        if (opRaw is null) return color;
        if (!double.TryParse(opRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var op) || !double.IsFinite(op))
            return $"<SolidColorBrush Color={color} Opacity={opRaw}>"; // Binding 之類 → 執行期才知道
        if (op >= 0.999) return color;
        // 字面色才折得進 #AARRGGBB；色票鍵 + Opacity 深淺各自要合成，這條路不做 → 誠實計入 unresolved
        return ColorResolver.ApplyOpacity(color, op) ?? $"<SolidColorBrush Color={color} Opacity={opRaw}>";
    }
}

using System.Text.RegularExpressions;

namespace XamlContrast.Core;

public enum ColorKind
{
    /// <summary>寫死色碼或具名色：深淺同值</summary>
    Hard,
    /// <summary>色票（資源鍵）：深淺各有值</summary>
    Soft,
    /// <summary>#AARRGGBB 半透明：要與底色合成，帶 A 與 RGB 出去交給呼叫端</summary>
    Alpha,
    Transparent,
    /// <summary>資源鍵不在色盤裡</summary>
    UnknownKey,
    /// <summary>TemplateBinding / Binding / 漸層等 —— 執行期才知道，標為無法解析而不猜</summary>
    Other,
}

public sealed record Resolved(
    ColorKind Kind,
    string? Dark = null,
    string? Light = null,
    string? Key = null,
    double Alpha = 0,
    string? Rgb = null);

/// <summary>把 XAML 屬性值解析成顏色（或「工具知道自己不知道」的標記）。</summary>
public static partial class ColorResolver
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = "#FFFFFF", ["black"] = "#000000", ["gray"] = "#808080",
        ["darkgray"] = "#A9A9A9", ["lightgray"] = "#D3D3D3",
        ["red"] = "#FF0000", ["green"] = "#008000", ["blue"] = "#0000FF",
        ["orange"] = "#FFA500", ["yellow"] = "#FFFF00",
        ["silver"] = "#C0C0C0", ["whitesmoke"] = "#F5F5F5", ["gainsboro"] = "#DCDCDC",
        ["dimgray"] = "#696969",
    };

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Rgb6();

    [GeneratedRegex(@"^#[0-9A-Fa-f]{8}$")]
    private static partial Regex Rgb8();

    [GeneratedRegex(@"^\{(Dynamic|Static)Resource\s+(?<k>[\w\.]+)\}$")]
    private static partial Regex ResourceRef();

    public static Resolved? Resolve(string? value, Palette palette)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();

        if (v == "Transparent") return new Resolved(ColorKind.Transparent);
        if (Rgb6().IsMatch(v))
        {
            var u = v.ToUpperInvariant();
            return new Resolved(ColorKind.Hard, u, u);
        }
        if (Rgb8().IsMatch(v))
        {
            var h = v.TrimStart('#');
            return new Resolved(ColorKind.Alpha,
                Alpha: Convert.ToInt32(h[..2], 16) / 255.0,
                Rgb: "#" + h[2..].ToUpperInvariant());
        }
        if (Named.TryGetValue(v, out var hex))
            return new Resolved(ColorKind.Hard, hex, hex);

        var m = ResourceRef().Match(v);
        if (m.Success)
        {
            var key = m.Groups["k"].Value;
            if (palette.Entries.TryGetValue(key, out var e))
                return new Resolved(ColorKind.Soft, e.Dark, e.Light, Key: key);
            return new Resolved(ColorKind.UnknownKey, Key: key);
        }

        return new Resolved(ColorKind.Other); // TemplateBinding / Binding / 漸層等
    }
}

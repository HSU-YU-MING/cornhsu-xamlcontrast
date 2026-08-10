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

/// <summary>Alpha 種類帶深淺各自的 A/RGB：字面 #AARRGGBB 深淺同值；
/// 半透明「色票」（palette 鍵的值是 8 碼）深淺各帶自己的 alpha。</summary>
public sealed record Resolved(
    ColorKind Kind,
    string? Dark = null,
    string? Light = null,
    string? Key = null,
    double Alpha = 0,
    string? Rgb = null,
    double AlphaLight = 0,
    string? RgbLight = null);

/// <summary>把 XAML 屬性值解析成顏色（或「工具知道自己不知道」的標記）。</summary>
public static partial class ColorResolver
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = "#FFFFFF",
        ["black"] = "#000000",
        ["gray"] = "#808080",
        ["darkgray"] = "#A9A9A9",
        ["lightgray"] = "#D3D3D3",
        ["red"] = "#FF0000",
        ["green"] = "#008000",
        ["blue"] = "#0000FF",
        ["orange"] = "#FFA500",
        ["yellow"] = "#FFFF00",
        ["silver"] = "#C0C0C0",
        ["whitesmoke"] = "#F5F5F5",
        ["gainsboro"] = "#DCDCDC",
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
            var rgb = "#" + h[2..].ToUpperInvariant();
            // ⚠ #FFRRGGBB 的 alpha=255 是「完全不透明」，不是半透明 —— 這是 Blend／VS
            //   設計工具的預設輸出格式，也是 WPF 生態最常見的寫法。把它當半透明會讓
            //   字色整批落進 skipped（外部專案實測：ScreenToGif 的 97 個色票有 79 個
            //   是 #FFRRGGBB，覆蓋率因此掉到 1%）。四個受測專案剛好都手寫六位數，
            //   所以這個洞一直沒現形 —— 樣本同源的代價。
            if (h[..2].Equals("FF", StringComparison.OrdinalIgnoreCase))
                return new Resolved(ColorKind.Hard, rgb, rgb);
            // 真半透明：要跟底下的顏色合成，交給呼叫端處理（字面值深淺同值）
            var a = Convert.ToInt32(h[..2], 16) / 255.0;
            return new Resolved(ColorKind.Alpha, Alpha: a, Rgb: rgb, AlphaLight: a, RgbLight: rgb);
        }
        if (Named.TryGetValue(v, out var hex))
            return new Resolved(ColorKind.Hard, hex, hex);

        var m = ResourceRef().Match(v);
        if (m.Success)
        {
            var key = m.Groups["k"].Value;
            if (palette.Entries.TryGetValue(key, out var e))
            {
                // 色票鍵的值同理：#FF... 是不透明色票，不是半透明疊層
                var darkOpaque = e.Dark.Length != 9 || e.Dark.Substring(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase);
                var lightOpaque = e.Light.Length != 9 || e.Light.Substring(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase);
                if (darkOpaque && lightOpaque)
                {
                    var d = e.Dark.Length == 9 ? "#" + e.Dark[3..] : e.Dark;
                    var l = e.Light.Length == 9 ? "#" + e.Light[3..] : e.Light;
                    return new Resolved(ColorKind.Soft, d, l, Key: key);
                }
                if (e.Dark.Length == 9)
                {
                    // #AARRGGBB 的「半透明色票」（如 CelFlow 的 BlueOverlay/Scrim）：
                    // 之前只有字面 8 碼會走合成，色票鍵的 8 碼值被 Luminance 剝掉 alpha
                    // 當『不透明色』用 —— InfoL 疊 16% 藍被算成疊純藍的假「破 2.68」。
                    // 深淺各帶自己的 alpha。
                    return new Resolved(ColorKind.Alpha, Key: key,
                        Alpha: Convert.ToInt32(e.Dark.Substring(1, 2), 16) / 255.0,
                        Rgb: "#" + e.Dark[3..],
                        AlphaLight: Convert.ToInt32(e.Light.Substring(1, 2), 16) / 255.0,
                        RgbLight: "#" + e.Light[3..]);
                }
                return new Resolved(ColorKind.Soft, e.Dark, e.Light, Key: key);
            }
            return new Resolved(ColorKind.UnknownKey, Key: key);
        }

        return new Resolved(ColorKind.Other); // TemplateBinding / Binding / 漸層等
    }
}

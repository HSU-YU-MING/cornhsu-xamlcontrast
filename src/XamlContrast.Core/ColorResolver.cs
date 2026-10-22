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
    /// <summary>底下墊的是同格子的兄弟「內容」元素（Image 等）—— 實際背景是一張圖，
    /// 靜態不可知。配祖先背景會報假警報（HandyControl Carousel 實證：白字疊照片,
    /// 被配上 RegionBrush 報 light=1 的假 fail）。不猜。</summary>
    SiblingContent,
}

/// <summary>Alpha 種類帶深淺各自的 A/RGB：字面 #AARRGGBB 深淺同值；
/// 半透明「色票」（palette 鍵的值是 8 碼）深淺各帶自己的 alpha。
/// Raw：背景是 Binding／漸層／兄弟圖片時的原始值 —— 它一路往下傳成子元素的底，
/// 子元素卡在它上面時 unresolved[].value 要點名它，不是點名沒問題的字色。
/// Composite：合成出來的底的描述（「A over B」）—— 與色票鍵 Key 分開，Key 永遠是真的資源鍵。</summary>
public sealed record Resolved(
    ColorKind Kind,
    string? Dark = null,
    string? Light = null,
    string? Key = null,
    double Alpha = 0,
    string? Rgb = null,
    double AlphaLight = 0,
    string? RgbLight = null,
    string? Raw = null,
    string? Composite = null);

/// <summary>把 XAML 屬性值解析成顏色（或「工具知道自己不知道」的標記）。</summary>
public static partial class ColorResolver
{
    /// <summary>供色盤偵測共用：具名色 → 色碼（認不得回傳 null）。
    /// HandyControl 的 ColorsDark.xaml 寫 &lt;Color&gt;White&lt;/Color&gt; —— 只認 #hex 會讓
    /// 該鍵的深色值缺失、退回淺色值，深字疊深底報出 1.17:1 的假 fail。</summary>
    internal static string? NamedHex(string name) => Named.GetValueOrDefault(name.Trim());

    /// <summary>
    /// WPF 的具名色（System.Windows.Media.Colors 的 141 色扣掉 Transparent，另外處理）。
    /// ⚠ 舊版只有手抄的 14 色：Navy、Teal、CornflowerBlue 這些 WPF 合法的色名全被歸成 bound-or-gradient ——
    ///   標成「執行期才知道」，其實靜態可解，覆蓋率被低估。WPF 的色名表與 .NET KnownColor 的網頁色
    ///   是同一份（不分大小寫）；KnownColor 另有系統色（WindowText…，WPF 的 Color 不吃）與
    ///   RebeccaPurple（CSS4 才有，WPF 沒有），兩者排除。
    /// </summary>
    private static readonly Dictionary<string, string> Named = BuildNamedColours();

    private static Dictionary<string, string> BuildNamedColours()
    {
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in Enum.GetValues<System.Drawing.KnownColor>())
        {
            var c = System.Drawing.Color.FromKnownColor(k);
            var name = k.ToString();
            if (c.IsSystemColor || name is "Transparent" or "RebeccaPurple") continue;
            named[name] = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
        }
        return named;
    }

    /// <summary>
    /// 字面色碼正規化成 #RRGGBB／#AARRGGBB（大寫）。WPF 也吃 #RGB 與 #ARGB（每一位各自重複一次）——
    /// 舊版不認，<c>Foreground="#AAA"</c> 被歸成 bound-or-gradient（CelFlow 快照 39 筆裡有 13 筆是這個）。
    /// 長度或字元不對回傳 null（#FF0000A 這種七位數打字錯誤不能被默默猜前半段）。
    /// </summary>
    internal static string? NormalizeHex(string value)
    {
        var v = value.Trim();
        if (!HexLiteral().IsMatch(v)) return null;
        var h = v[1..].ToUpperInvariant();
        return h.Length switch
        {
            3 or 4 => "#" + string.Concat(h.Select(ch => new string(ch, 2))),
            6 or 8 => "#" + h,
            _ => null,
        };
    }

    [GeneratedRegex(@"^#(?:[0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexLiteral();

    /// <summary>Transparent 不分大小寫（WPF 的色名都是）。</summary>
    private static bool IsTransparent(string v) => v.Equals("Transparent", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 字面色（#RRGGBB／#AARRGGBB／具名色）乘上 brush 的 Opacity，折成 #AARRGGBB。
    /// SolidColorBrush 的 Opacity 會讓整個 brush 變半透明 —— 忽略它就是把半透明 brush
    /// 當成不透明色評分（白字 Opacity 0.2 疊黑底實際約 1.66:1，會被報成 21:1）。
    /// 不是字面色（資源鍵、Binding）回傳 null，由呼叫端決定怎麼誠實地處理。
    /// </summary>
    internal static string? ApplyOpacity(string color, double opacity)
    {
        var v = color.Trim();
        if (IsTransparent(v)) return "Transparent";
        var hex = v.StartsWith('#') ? NormalizeHex(v) : NamedHex(v);
        if (hex is null) return null;
        var (a, rgb) = hex.Length == 9 ? (Convert.ToInt32(hex[1..3], 16), hex[3..]) : (255, hex[1..]);
        var na = (int)Math.Round(a * Math.Clamp(opacity, 0, 1));
        return "#" + na.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + rgb;
    }

    /// <summary>色票值 → (alpha, #RRGGBB)。7 碼是不透明（alpha = 1）。</summary>
    private static (double Alpha, string Rgb) SplitAlpha(string hex)
        => hex.Length == 9
            ? (Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0, "#" + hex[3..])
            : (1.0, hex);

    [GeneratedRegex(@"^\{(Dynamic|Static)Resource\s+(?<k>[\w\.]+)\}$")]
    private static partial Regex ResourceRef();

    public static Resolved? Resolve(string? value, Palette palette)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();

        if (IsTransparent(v)) return new Resolved(ColorKind.Transparent);
        if (v.StartsWith('#') && NormalizeHex(v) is { } n)
        {
            if (n.Length == 7) return new Resolved(ColorKind.Hard, n, n);
            var rgb = "#" + n[3..];
            // ⚠ #FFRRGGBB 的 alpha=255 是「完全不透明」，不是半透明 —— 這是 Blend／VS
            //   設計工具的預設輸出格式，也是 WPF 生態最常見的寫法。把它當半透明會讓
            //   字色整批落進 skipped（外部專案實測：ScreenToGif 的 97 個色票有 79 個
            //   是 #FFRRGGBB，覆蓋率因此掉到 1%）。四個受測專案剛好都手寫六位數，
            //   所以這個洞一直沒現形 —— 樣本同源的代價。
            if (n[1..3] == "FF") return new Resolved(ColorKind.Hard, rgb, rgb);
            // 真半透明：要跟底下的顏色合成，交給呼叫端處理（字面值深淺同值）
            var a = Convert.ToInt32(n[1..3], 16) / 255.0;
            return new Resolved(ColorKind.Alpha, Alpha: a, Rgb: rgb, AlphaLight: a, RgbLight: rgb);
        }
        if (Named.TryGetValue(v, out var hex))
            return new Resolved(ColorKind.Hard, hex, hex);

        var m = ResourceRef().Match(v);
        if (m.Success)
        {
            var key = m.Groups["k"].Value;
            if (palette.RuntimeKeys.Contains(key)) return new Resolved(ColorKind.Other, Key: key, Raw: v);
            // 畫面的區域 brush 引用了找不到的 Color：點名那個 Color（使用處的 brush 鍵本身是有定義的）
            if (palette.MissingRefs.TryGetValue(key, out var missingColor)) return new Resolved(ColorKind.UnknownKey, Key: missingColor, Raw: v);
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
                // #AARRGGBB 的「半透明色票」（如 CelFlow 的 BlueOverlay/Scrim）：
                // 之前只有字面 8 碼會走合成，色票鍵的 8 碼值被 Luminance 剝掉 alpha
                // 當『不透明色』用 —— InfoL 疊 16% 藍被算成疊純藍的假「破 2.68」。
                // 深淺各帶自己的 alpha。
                // ⚠ 任一側半透明就整個走 alpha，兩側各自拆（7 碼那側 alpha = 1）。舊版只看深色那側的長度：
                //   深色半透明、淺色 7 碼 → 把 "#E0E0E0" 切成 "#E0E0"，算對比時 ArgumentOutOfRangeException、
                //   整支崩潰；淺色半透明、深色 7 碼 → 回傳帶 9 碼淺色值的 Soft，alpha 被剝掉當不透明色，
                //   比值是編出來的（實際 3.91 報 1.3）。合併式色盤裡只有一個主題用半透明遮罩是實際會出現的形狀
                var (ad, rd) = SplitAlpha(e.Dark);
                var (al, rl) = SplitAlpha(e.Light);
                return new Resolved(ColorKind.Alpha, Key: key, Alpha: ad, Rgb: rd, AlphaLight: al, RgbLight: rl);
            }
            return new Resolved(ColorKind.UnknownKey, Key: key);
        }

        return new Resolved(ColorKind.Other); // TemplateBinding / Binding / 漸層等
    }
}

using System.Text;

namespace XamlContrast.Core;

/// <summary>
/// WCAG 2.x 對比度數學。公式只有 20 行 —— 這個工具的價值不在這裡，
/// 在「這段文字實際疊在什麼顏色上」（<see cref="Auditor"/> 的十六條解析規則）。
/// </summary>
public static class Wcag
{
    /// <summary>相對亮度。輸入 #RRGGBB 或 #AARRGGBB（alpha 直接剝掉 —— 呼叫端要先合成）。</summary>
    public static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];
        Span<double> ch = stackalloc double[3];
        for (var i = 0; i < 3; i++)
        {
            var v = Convert.ToInt32(h.Substring(i * 2, 2), 16) / 255.0;
            ch[i] = v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * ch[0] + 0.7152 * ch[1] + 0.0722 * ch[2];
    }

    /// <summary>對比度，四捨五入到小數兩位（與原型一致，基準線比對用同一精度）。</summary>
    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return Math.Round((hi + 0.05) / (lo + 0.05), 2);
    }

    /// <summary>半透明色 fg（不含 alpha 的 RGB）以 alpha 疊在不透明底色 bg 上（WPF 以 sRGB 混合）。</summary>
    public static string Composite(string fg, double alpha, string bg)
    {
        var f = fg.TrimStart('#');
        var b = bg.TrimStart('#');
        if (b.Length == 8) b = b[2..];
        var sb = new StringBuilder("#");
        for (var i = 0; i < 3; i++)
        {
            var cf = Convert.ToInt32(f.Substring(i * 2, 2), 16);
            var cb = Convert.ToInt32(b.Substring(i * 2, 2), 16);
            sb.Append(((int)Math.Round(alpha * cf + (1 - alpha) * cb)).ToString("X2"));
        }
        return sb.ToString();
    }
}

using System.Xml.Linq;

namespace XamlContrast.Core;

/// <summary>
/// 「同一個容器裡誰疊在誰上面」的判定（1.7 從 <see cref="Auditor"/> 抽出來）。
///
/// 兩個地方要用同一套答案：標記樹的規則 17（<c>Auditor.Walk</c> 的兄弟背板）與**模板裡**的同一個形狀
/// （<c>StyleIndex.ContentLayers</c>，1.7）。1.6 之前只有前者，所以這些判斷長在 Auditor 裡；
/// 模板那條路接上來之後再抄一份是最糟的做法 —— 「哪些容器會疊在一起」漂開的話，同一段 XAML
/// 寫在畫面裡與寫在模板裡會得到不同的答案，而且沒有任何測試會紅。
/// </summary>
internal static class Panels
{
    internal enum Layout { SideBySide, Overlap, Unknown }

    /// <summary>Style／Setter／Trigger 這些不是視覺樹（個別由 <c>Auditor.WalkStyles</c> 走）。</summary>
    internal static readonly HashSet<string> Styleish = new()
    {
        "Style", "Setter", "Trigger", "DataTrigger", "MultiTrigger", "MultiDataTrigger",
    };

    /// <summary>容器的子元素會不會疊在一起。Grid（同一格）、Canvas 會；StackPanel／WrapPanel／DockPanel／
    /// UniformGrid／ToolBarPanel／TabPanel 系（含衍生命名，例 VirtualizingWrapPanel、MyDockPanel）會排開。
    /// ⚠ 其他自訂 *Panel 的版面靜態不可知：舊版「保守視為疊層」，在排開的面板裡就是捏造背板
    ///   （VirtualizingWrapPanel 裡的黑色方塊被當成旁邊白字的底）；當成排開一樣是猜 → Unknown。
    /// ⚠ 衍生命名的 *Canvas／*Grid 同理（審查素材 m7）：舊版只看 *Panel，ZoomCanvas 與 CardGrid 落到「排開」——
    ///   疊在 Image 上的字報 21:1 ok、疊在黑色方塊上的白字配祖先白底編出 1:1 fail。它們是不是真的衍生自
    ///   Canvas／Grid、Grid 的格子怎麼切，靜態都不知道 → Unknown。InkCanvas 是 WPF 內建，跟 Canvas 一樣疊在一起。</summary>
    internal static Layout LayoutOf(string name, ToolConfig cfg)
    {
        // config 的宣告優先（使用者知道自己的容器怎麼排）
        if (cfg.Layout.Overlap.Contains(name, StringComparer.Ordinal)) return Layout.Overlap;
        if (cfg.Layout.SideBySide.Contains(name, StringComparer.Ordinal)) return Layout.SideBySide;
        if (name is "Grid" or "Canvas" or "InkCanvas") return Layout.Overlap;
        if (name.EndsWith("StackPanel") || name.EndsWith("WrapPanel") || name.EndsWith("DockPanel") ||
            name.EndsWith("UniformGrid") || name.EndsWith("ToolBarPanel") || name.EndsWith("TabPanel") ||
            name.EndsWith("ToolBarOverflowPanel") || name.EndsWith("DataGrid")) return Layout.SideBySide;
        return name.EndsWith("Panel") || name.EndsWith("Canvas") || name.EndsWith("Grid")
            ? Layout.Unknown : Layout.SideBySide;
    }

    /// <summary>Canvas 系不把子元素撐滿（沒有尺寸的 Border 實際是 0×0），見 <see cref="FillsCell"/>。</summary>
    internal static bool StretchesChildren(string name) => name is not ("Canvas" or "InkCanvas");

    internal static (int Row, int Col) CellOf(XElement child, bool isGrid)
    {
        if (!isGrid) return default; // Canvas/自訂 Panel:全部視為同一層
        int P(string attr) => int.TryParse(child.Attribute(attr)?.Value, out var v) ? v : 0;
        return (P("Grid.Row"), P("Grid.Column"));
    }

    /// <summary>底變成一張圖／一段影片 —— 靜態不可知（規則 17 的 (b) 半）。</summary>
    internal static bool IsContentElement(string name)
        => name is "Image" or "MediaElement" || name.EndsWith("Image", StringComparison.Ordinal);

    /// <summary>這個子元素撐滿它那一格嗎 —— 有明確尺寸或非 Stretch 對齊的不算背板（強調色條 Width="4" 之類）。
    /// ⚠ Canvas 不套（<paramref name="stretches"/> false）：沒內容、沒尺寸的 Border 在 Canvas 裡實際是 0×0，
    ///   舊版照樣算背板，把一塊不存在的底捏造出來。有尺寸的也不行 —— Canvas.Left/Top 與文字範圍靜態不可知。</summary>
    internal static bool FillsCell(XElement child, bool stretches)
    {
        if (!stretches) return false;
        static bool Stretchy(XElement e, string attr) => e.Attribute(attr)?.Value is null or "Stretch";
        return child.Attribute("Width") is null && child.Attribute("Height") is null &&
               Stretchy(child, "HorizontalAlignment") && Stretchy(child, "VerticalAlignment");
    }

    /// <summary>畫底的那個屬性的原始寫法。⚠ 只有 <c>Rectangle</c> 讀 Fill：Ellipse 之類的形狀
    /// 不會蓋滿矩形的那一格（四個角露出來），把它當背板就是在猜文字落在哪一塊。</summary>
    internal static string? BackdropBrush(XElement child)
        => XamlBrush.Read(child, child.Name.LocalName == "Rectangle" ? "Fill" : "Background");
}

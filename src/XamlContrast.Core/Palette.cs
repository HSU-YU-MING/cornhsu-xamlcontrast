namespace XamlContrast.Core;

/// <summary>專案的色票定義。深色 / 淺色兩套值；單一主題專案兩欄同值。</summary>
public sealed class Palette
{
    public Dictionary<string, (string Dark, string Light)> Entries { get; } = new();

    /// <summary>沒有任何色票的深淺值不同 → 單一主題（對稱欄位不適用，只看絕對對比）。</summary>
    public bool IsSingleTheme => Entries.Values.All(v => v.Dark == v.Light);

    public int Count => Entries.Count;

    /// <summary>值在執行期才知道的鍵（畫面的區域 brush 綁 Binding 之類）—— 解析成 bound-or-gradient，
    /// 不是 unknown-palette-key（那會被讀成打字錯誤或死引用）。</summary>
    public HashSet<string> RuntimeKeys { get; } = new();
}

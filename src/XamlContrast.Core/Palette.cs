namespace XamlContrast.Core;

/// <summary>專案的色票定義。深色 / 淺色兩套值；單一主題專案兩欄同值。</summary>
public sealed class Palette
{
    public Dictionary<string, (string Dark, string Light)> Entries { get; } = new();

    /// <summary>沒有任何色票的深淺值不同 → 單一主題（對稱欄位不適用，只看絕對對比）。</summary>
    public bool IsSingleTheme => Entries.Values.All(v => v.Dark == v.Light);

    public int Count => Entries.Count;
}

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

    /// <summary>只有畫面檔的區域資源定義的鍵 → 定義它的畫面（完整路徑）。照樣算進偵測（只在畫面裡放色票的
    /// 專案照樣偵測得到色盤），但稽核時只有定義它的畫面看得到 —— 執行期其他畫面與主題字典查不到它。</summary>
    internal Dictionary<string, HashSet<string>> LocalOnly { get; } = new();

    /// <summary>全域（主題字典與 App）的 Color 表，深淺各一：畫面的區域 brush 引用 Color 時逐側解析用。</summary>
    internal Dictionary<string, string> DarkColors { get; } = new();
    internal Dictionary<string, string> LightColors { get; } = new();

    /// <summary>畫面的區域 brush 引用了在畫面與全域都找不到的 Color：brush 鍵 → 那個 Color 鍵。
    /// 解析成 unknown-palette-key，點名的是找不到的 Color（外部函式庫的 MahApps.Colors.Accent 之類）。</summary>
    internal Dictionary<string, string> MissingRefs { get; } = new();

    /// <summary>複製一份（色票、執行期鍵、區域鍵、Color 表），給各檔的視圖改。</summary>
    internal Palette Clone()
    {
        var p = new Palette();
        foreach (var (k, v) in Entries) p.Entries[k] = v;
        p.RuntimeKeys.UnionWith(RuntimeKeys);
        foreach (var (k, v) in LocalOnly) p.LocalOnly[k] = v;
        foreach (var (k, v) in DarkColors) p.DarkColors[k] = v;
        foreach (var (k, v) in LightColors) p.LightColors[k] = v;
        foreach (var (k, v) in MissingRefs) p.MissingRefs[k] = v;
        return p;
    }
}

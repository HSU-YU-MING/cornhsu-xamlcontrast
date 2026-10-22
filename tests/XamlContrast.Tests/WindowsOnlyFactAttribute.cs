namespace XamlContrast.Tests;

/// <summary>
/// 只在 Windows 上執行的 Fact：其他平台在結果裡顯示為「略過」並附理由，而不是 return 之後算成通過 ——
/// 測試結果也不能謊報健康（一條在 Linux 上什麼都沒驗的測試，不該跟真的驗過的一樣是綠的）。
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = reason;
    }
}

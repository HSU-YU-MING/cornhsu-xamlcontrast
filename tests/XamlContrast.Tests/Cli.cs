using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace XamlContrast.Tests;

/// <summary>
/// 以子行程跑真的 xamlcontrast.dll。退出碼 0／1／2 是對使用者的契約，而旗標解析與
/// DecideExit 的判定順序只存在於 Program.cs —— Core 的單元測試碰不到，只有整支程式
/// 跑一次才驗得到（CHANGELOG 0.6.0 修過的「--strict-palette 在 baseline 模式下失效」
/// 就是判定順序的回歸，之前沒有任何測試在守）。
///
/// ⚠ 斷言只比對 ASCII 子字串：子行程的 stdout 編碼跟著 OS 主控台走（Windows 上是 OEM
///   字碼頁），訊息裡的「—」之類的字元重導之後不保證原樣回來。
/// </summary>
internal static class Cli
{
    internal sealed record Result(int Exit, string Out, string Err)
    {
        public override string ToString() => $"exit {Exit}\n--- stdout ---\n{Out}\n--- stderr ---\n{Err}";
    }

    /// <summary>建置時由 csproj 的 EmbedXamlContrastCliPath 寫進測試組件的中繼資料。</summary>
    public static string DllPath { get; } = typeof(Cli).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "XamlContrastCliDll").Value!;

    public static Result Run(params string[] args) => RunIn(null, args);

    /// <summary>workingDirectory：--json／--baseline 等輸出路徑與 baseline 預設檔名都相對 CWD。</summary>
    public static Result RunIn(string? workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(DotnetHost)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        psi.ArgumentList.Add(DllPath);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"xamlcontrast {string.Join(' ', args)} did not exit");
        }
        p.WaitForExit(); // 確保重導的串流讀完
        return new Result(p.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>讀 CLI 寫出的 JSON／SARIF／baseline。</summary>
    public static JsonElement ReadJson(string path)
        => JsonDocument.Parse(File.ReadAllText(path)).RootElement;

    // 跑測試的那份 runtime 所屬的 dotnet 主程式（<dotnet root>/shared/Microsoft.NETCore.App/<ver>/）
    private static string DotnetHost { get; } = FindDotnetHost();

    private static string FindDotnetHost()
    {
        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { } hostPath && File.Exists(hostPath))
            return hostPath;
        var fromRuntime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", exe));
        return File.Exists(fromRuntime) ? fromRuntime : "dotnet";
    }
}

/// <summary>repo 內的檔案（samples/demo、action.yml）—— 從測試輸出目錄往上找 slnx。</summary>
internal static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(string relative) => Path.Combine(Root, relative);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "XamlContrast.slnx"))) return dir.FullName;
        throw new InvalidOperationException("XamlContrast.slnx not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>CLI 測試共用的最小專案。每個只有一種「形狀」，退出碼的期望才說得清楚。</summary>
internal static class CliProjects
{
    /// <summary>黑底白字 21:1 —— 1 組通過。沒有色盤（寫死色碼），所以色盤偵測是退化的。</summary>
    public static Fixture Passing() => new Fixture().File("Main.xaml", """
        <Grid Background="#000000">
          <TextBlock Foreground="#FFFFFF" Text="21:1"/>
        </Grid>
        """);

    /// <summary>白底淺灰字 ~1.16:1 —— 1 組 fail。</summary>
    public static Fixture Failing() => new Fixture().File("Main.xaml", """
        <Grid Background="#FFFFFF">
          <TextBlock Foreground="#EEEEEE" Text="fail"/>
        </Grid>
        """);

    /// <summary>白底 #8A8A8A ~3.45:1 —— 低於 AA、高於 fail 線：1 組 warn、0 fail。</summary>
    public static Fixture WarnOnly() => new Fixture().File("Main.xaml", """
        <Grid Background="#FFFFFF">
          <TextBlock Foreground="#8A8A8A" Text="warn"/>
        </Grid>
        """);

    /// <summary>沒有任何文字配對 —— 0 組。</summary>
    public static Fixture ZeroPairs() => new Fixture().File("Main.xaml", "<Grid/>");

    /// <summary>1 組解析得出（通過）＋ 3 組底色綁執行期 —— 覆蓋率恰好 25%。</summary>
    public static Fixture QuarterCoverage() => new Fixture().File("Main.xaml", """
        <Grid Background="#000000">
          <TextBlock Foreground="#FFFFFF" Text="resolved"/>
          <Border Background="{Binding A}"><TextBlock Foreground="#FFFFFF" Text="1"/></Border>
          <Border Background="{Binding B}"><TextBlock Foreground="#FFFFFF" Text="2"/></Border>
          <Border Background="{Binding C}"><TextBlock Foreground="#FFFFFF" Text="3"/></Border>
        </Grid>
        """);

    /// <summary>深淺兩套色盤（Bg／Fg／Dim 三鍵）—— 色盤偵測不退化。</summary>
    public static Fixture WithPalette(this Fixture fx) => fx
        .File("Themes/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Dim" Color="#2A2A2A"/>
            </ResourceDictionary>
            """)
        .File("Themes/LightTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FAFAFA"/>
              <SolidColorBrush x:Key="Fg" Color="#212121"/>
              <SolidColorBrush x:Key="Dim" Color="#E8E8E8"/>
            </ResourceDictionary>
            """);
}

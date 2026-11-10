using System.Globalization;
using System.Text;

namespace XamlContrast.Core;

public static partial class Report
{
    /// <summary>
    /// MSBuild 的標準診斷格式（1.4，<c>--msbuild</c>）：<c>完整路徑(行,1): error XC0001: 訊息</c>。
    /// MSBuild 的 Exec 會把這種行當成錯誤／警告記下來，Visual Studio 就列進錯誤清單、點兩下跳到那一行 ——
    /// 不必裝擴充套件，CLI 跑在建置裡就好（README 有 csproj 片段）。
    /// <list type="bullet">
    /// <item><c>XC0001</c>（error）：這次 gate 擋的。<c>--baseline</c> 模式只有新增或惡化的是 error。</item>
    /// <item><c>XC0002</c>（warning）：低於 AA 但這次不擋的（<c>--fail-on fail</c> 下的 warn、baseline 的已知債、<c>--write-baseline</c>）。</item>
    /// <item><c>XC0003</c>（error）：結果不可信（exit 3）——不然建置會在「工具其實沒看到」時安靜地過。</item>
    /// </list>
    /// 用法、設定錯誤（exit 2）在這之前就結束了，訊息在 stderr；csproj 片段靠退出碼接住它。
    /// 檔名與訊息經 <see cref="ConsoleSafe"/>：換行會讓下一行變成另一筆診斷。
    /// </summary>
    public static string ToMsBuild(AuditResult r, string root, bool gateWarn,
        IReadOnlyDictionary<Finding, string>? baselineStatus, bool writingBaseline, int exitCode)
    {
        var sb = new StringBuilder();
        foreach (var f in r.Findings.Where(f => f.Category is Category.Fail or Category.Warn)
                     .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line))
        {
            var gated = f.Category == Category.Fail || gateWarn;
            var known = baselineStatus is not null && baselineStatus.TryGetValue(f, out var s) && s == "known";
            var error = !writingBaseline && gated && !known && (baselineStatus is null || baselineStatus.ContainsKey(f));
            var path = Path.GetFullPath(Path.Combine(root, f.File));
            sb.Append(ConsoleSafe(path))
              .Append('(').Append(Math.Max(f.Line, 1).ToString(CultureInfo.InvariantCulture)).Append(",1): ")
              .Append(error ? "error XC0001: " : "warning XC0002: ")
              .Append(ConsoleSafe(MsBuildMessage(f, known)))
              .Append('\n');
        }
        if (exitCode == 3)
            sb.Append("XamlContrast : error XC0003: the audit result can't be trusted (exit 3) — see the messages above\n");
        return sb.ToString();
    }

    private static string MsBuildMessage(Finding f, bool known)
    {
        static string Ratio(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + ":1";
        var ratios = f.Theme is { } t
            ? $"{(t == ThemeSide.Dark ? "dark" : "light")} theme {Ratio(f.RatioDark)}"
            : f.RatioDark == f.RatioLight ? Ratio(f.RatioDark)
            : $"dark {Ratio(f.RatioDark)}, light {Ratio(f.RatioLight)}";
        return $"{f.Element}: {f.Fg} on {f.Bg} is {ratios}, needs {Ratio(f.Need)} " +
               $"({(f.Category == Category.Fail ? "fail" : "warn")}{(known ? ", known debt in the baseline" : "")})";
    }
}

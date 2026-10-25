using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlContrast.Core;

namespace XamlContrast.Cli;

/// <summary>
/// 匿名使用統計（1.1）：稽核得出判定之後，送一筆只有計數與分桶的 JSON 到 stats.cornhsu.com —— 碩士論文
/// 「XAML App 的對比度問題長什麼樣」的補充證據。唯一真相源是 stats-collector 的 PROTOCOL.md（Parity 2.1 與收集站照同一份做）：
/// 這裡是什麼時候送（第 2 節）、告知（第 4 節）、預覽（第 5 節）、傳輸（第 6 節）、外層欄位（第 7.1 節）；
/// 開關與順序（第 3 節）與 data（第 7.3 節）是純函式，在 Core 的 <see cref="UsageStats"/>。**改任何一條先改協定**，三邊一起改。
///
/// 鐵則：不影響結果。送不出去就算了 —— 不重試、不印錯誤、不改退出碼、不改任何其他輸出（stdout 一個字都不多）。
/// 說明只印在 stderr；不送的時候什麼都不印。
/// </summary>
internal static class Stats
{
    /// <summary>
    /// 預設送不送（協定第 3 節：預設打開，含 CI）。⚠ **倫理審查（或指導教授）若要求主動同意，只改這一個值**：
    /// 改成 false 之後，只有使用者明確說好（*_SHARE_STATS 是 1／true／yes／on，或 config 的 "stats": { "share": true }）才送；
    /// config 與 Action input 的預設值（不寫／'true' = 不表態）不必跟著改。協定說的「第一次在終端機詢問」到那時再做。
    /// 1.1.0 發版前要先拿到這個預設的確認（CLAUDE.md）。
    /// </summary>
    internal const bool ShareByDefault = true;

    private const string Tool = "xamlcontrast";
    private const string InfoUrl = "https://stats.cornhsu.com/";

    // 協定第 6 節：連線 1.5 秒、整體 3 秒
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(3);

    /// <summary>協定第 4 節的本機說明（第一次在這台機器上要送的時候印一次）。</summary>
    private const string FirstNotice = """
        XamlContrast sends anonymous usage statistics: counts and score ranges only - no URLs, names,
        selectors, file paths, colours or screenshots, and no IP address is stored. They support a
        master's thesis on contrast in XAML apps.
        What is sent and why: https://stats.cornhsu.com/
        Preview it:  xamlcontrast ... --stats-preview   (sends nothing)
        Turn it off: "stats": { "share": false } in xamlcontrast.config.json, or XAMLCONTRAST_SHARE_STATS=false,
                     CORNHSU_SHARE_STATS=false or DO_NOT_TRACK=1
        This notice is shown once on this machine.
        """;

    /// <summary>協定第 4 節的 CI 一行（CI 機器每次都是新的：每次都印、不寫標記）。</summary>
    private const string CiNotice = "xamlcontrast: anonymous usage statistics sent (counts only) - https://stats.cornhsu.com/ - turn off with DO_NOT_TRACK=1";

    /// <summary>
    /// 稽核得出判定（0／1／3）、輸出檔都寫完之後呼叫一次。用法錯誤、設定錯誤、寫檔失敗（2）、--push、--help、--version
    /// 都走不到這裡（Program.cs 在那之前就 return 了）。preview：什麼都不送，在 stderr 印出這次會送的 JSON 與會不會送、為什麼。
    /// </summary>
    public static void AfterAudit(AuditResult result, ToolConfig config, int exitCode, string failOn, string? toolVersion, bool preview)
    {
        byte[] body;
        UsageStats.Decision decision;
        try
        {
            decision = Decide(toolVersion, config);
            body = Payload(result, config, exitCode, failOn, toolVersion);
        }
        catch (Exception ex)
        {
            // 統計是附帶的：算不出來（不該發生）也不能讓稽核的退出碼或輸出變樣 —— 預覽時說一聲，平常安靜
            if (preview) Console.Error.WriteLine(Report.ConsoleSafe($"anonymous usage statistics: could not be computed ({ex.Message}); nothing would be sent"));
            return;
        }

        if (preview)
        {
            var err = Console.Error;
            err.WriteLine("anonymous usage statistics (--stats-preview: nothing is sent) - this run would send:");
            err.Write(Encoding.UTF8.GetString(body));
            err.WriteLine();
            err.WriteLine(Report.ConsoleSafe(decision.Send
                ? $"would be sent to {decision.Endpoint} ({decision.Reason})"
                : $"would not be sent: {decision.Reason}"));
            return;
        }
        if (!decision.Send) return; // 不送的時候什麼都不印

        Notify();
        Post(decision.Endpoint, body);
    }

    /// <summary>協定第 3 節的開關（順序、值的比對、開發版 0.0.0-*）在 Core 的 <see cref="UsageStats.Decide"/> —— 純函式，
    /// 單元測試餵得進任何版本字串（0.0.0-ci 之類，子行程測不到）。這裡只接上真的環境變數與那一個常數。</summary>
    internal static UsageStats.Decision Decide(string? toolVersion, ToolConfig config)
        => UsageStats.Decide(Environment.GetEnvironmentVariable, config, toolVersion, ShareByDefault, Tool);

    /// <summary>協定第 7.1 節的外層 + 第 7.3 節的 data，UTF-8、不帶 BOM、縮排（--stats-preview 印的就是送出去的那幾個位元組）。</summary>
    internal static byte[] Payload(AuditResult result, ToolConfig config, int exitCode, string failOn, string? toolVersion)
    {
        var payload = new JsonObject
        {
            ["schema"] = 1,
            ["tool"] = Tool,
            ["toolVersion"] = toolVersion ?? "0.0.0-dev", // 讀不到版本（不會發生）時 Decide 已經當開發版、不送；預覽照樣印得出來
            ["os"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "other",
            ["ci"] = IsCi(),
            // 專案 ID 只來自設定檔（格式在載入時驗過）；沒有就是 null —— 不從 repo、路徑或機器資訊算
            ["projectId"] = config.Stats.ProjectId,
            ["data"] = UsageStats.Data(result, exitCode, failOn),
        };
        // 換行固定 \n：WriteIndented 預設用 Environment.NewLine，Windows 與 Linux 送出的位元組會不一樣
        return JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
    }

    /// <summary>
    /// 協定第 4 節。CI（CI 或 GITHUB_ACTIONS 是 true）：每次一行、不寫標記。本機：第一次印完整說明，之後在使用者設定目錄
    /// 寫標記檔就不再印；標記寫不進去 → 下次照樣印（寧可多印）。GitHub Actions 上另外在 job summary 寫一行（說明的一部分：
    /// 送了什麼、怎麼關；Action 的 share-stats: false 那一行由 action.yml 寫）。全部只碰 stderr 與 job summary，不碰 stdout。
    /// </summary>
    private static void Notify()
    {
        var marker = MarkerPath();
        if (IsCi()) Console.Error.WriteLine(CiNotice);
        else if (marker is null || !File.Exists(marker))
        {
            Console.Error.Write(FirstNotice.Replace("\r\n", "\n"));
            Console.Error.WriteLine();
            if (marker is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                    File.WriteAllText(marker, "XamlContrast showed its anonymous usage statistics notice on this machine.\n" +
                                              "Delete this file to see the notice again. What is sent and why: " + InfoUrl + "\n");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { }
            }
        }

        if (Env("GITHUB_STEP_SUMMARY") is { } summary)
        {
            try
            {
                File.AppendAllText(summary, "**XamlContrast** — anonymous usage statistics sent (counts only, no names, paths or colours) · " +
                    $"[what is sent and why]({InfoUrl}) · turn off with `DO_NOT_TRACK=1`, `XAMLCONTRAST_SHARE_STATS=false` " +
                    "or the action's `share-stats: false`\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { }
        }
    }

    /// <summary>
    /// 協定第 6 節：POST、Content-Type: application/json，連線 1.5 秒／整體 3 秒，不重試、不跟轉址，
    /// 不帶 User-Agent、cookie 或任何自訂標頭（含 .NET 自動加的分散式追蹤標頭 traceparent），回應只看狀態碼 —— 其實連狀態碼也不看：
    /// 任何結果都一樣安靜。
    /// </summary>
    private static void Post(Uri endpoint, byte[] body)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectTimeout = ConnectTimeout,
                ActivityHeadersPropagator = null, // 不帶 traceparent／tracestate
                MaxConnectionsPerServer = 1,
            };
            using var http = new HttpClient(handler) { Timeout = TotalTimeout };
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead);
        }
        // ⚠ 刻意全接：DNS、連不上、TLS、逾時、proxy、任何意外 —— 統計失敗絕不能讓稽核的退出碼或輸出變樣（協定第 1 節）
        catch (Exception) { }
    }

    private static bool IsCi()
        => string.Equals(Env("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(Env("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>&lt;ApplicationData&gt;/cornhsu/xamlcontrast-stats-notice（Windows 是 %APPDATA%、Linux 是 $XDG_CONFIG_HOME 或 ~/.config）。
    /// DoNotVerify：資料夾還不存在也給路徑（寫標記時建）；拿不到就是 null —— 每次都印。</summary>
    private static string? MarkerPath()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "cornhsu", $"{Tool}-stats-notice");
    }

    /// <summary>沒設與空字串一律當沒設（Linux 上 export X= 是有定義的空字串；同 Push.Env）。</summary>
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}

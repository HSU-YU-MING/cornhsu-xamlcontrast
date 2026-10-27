using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Cli;

/// <summary>
/// --push（實驗性）：把 --json 已經寫好的報告原樣送上 Parity.Server 的團隊儀表板。**不稽核** —— 稽核永遠在本機／CI
/// 跑完，這裡只搬檔案（比照 Parity 的 `parity push`：伺服器只收報告、不掃任何東西）。
///
/// 用旗標而不是 `push` 子指令：1.0 凍結了 CLI 語法，而今天的 `xamlcontrast push` 是「稽核名叫 push 的目錄」——
/// 子指令會打破凍結，新旗標只是新增（minor）。
///
/// 實驗性：儀表板公開上線之前，這個模式的旗標、環境變數、退出碼與伺服器協定都可能在 minor 改變，不在 1.x 凍結面內。
/// 退出碼只有兩個：0 送出、2 其他一切。1 與 3 是稽核的判定，上傳不借用 —— 否則 CI 分不出「上傳失敗」與「有違規」。
/// token 只走 XAMLCONTRAST_TOKEN：刻意沒有 --token，旗標值會留在 shell 歷史與 CI log 裡。
/// </summary>
internal static class Push
{
    private const int TimeoutSeconds = 60;

    public static int Run(string reportPath, string? serverFlag, string? commitFlag, string? branchFlag)
    {
        // 伺服器：旗標 → 環境變數。只收絕對的 http／https 網址（Linux 上 "/srv" 也是合法的絕對 URI —— file:///srv —— 所以看 scheme）
        var server = serverFlag ?? Env("XAMLCONTRAST_SERVER");
        if (server is null)
        {
            Err("missing server: pass --server <url>, or set the XAMLCONTRAST_SERVER environment variable");
            return 2;
        }
        if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUri) || serverUri.Scheme is not ("http" or "https"))
        {
            Err($"{(serverFlag is not null ? "--server" : "XAMLCONTRAST_SERVER")} must be an absolute http or https URL, got '{server}'");
            return 2;
        }

        // 前後空白修掉（secret 尾巴多一個換行很常見）；中間還有空白或控制字元就不是 token —— 不印值
        var token = Env("XAMLCONTRAST_TOKEN")?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            Err("missing token: set the XAMLCONTRAST_TOKEN environment variable to the project token printed by the server's " +
                "create-project command (there is no --token flag: flag values end up in shell history and CI logs)");
            return 2;
        }
        if (!token.All(c => c is > ' ' and <= '~'))
        {
            Err("XAMLCONTRAST_TOKEN contains spaces, line breaks or non-ASCII characters — it is not a project token");
            return 2;
        }

        // 送出前先在本機驗：壞檔、別的工具的報告、比這個版本新的格式 —— 在這裡講清楚，不必浪費一趟往返等伺服器回 400／422
        if (ReadReport(reportPath) is not { } report) return 2;

        // commit／branch：旗標 → GitHub Actions 的環境變數 → 不帶（伺服器的欄位可空）。
        // ⚠ 空字串一律當沒設：GITHUB_HEAD_REF 在 PR 以外的事件是**有定義的空字串**，用 ?? 接的話 branch 永遠是 ""
        var commit = commitFlag ?? Env("GITHUB_SHA");
        var branch = branchFlag ?? Env("GITHUB_HEAD_REF") ?? Env("GITHUB_REF_NAME"); // PR 事件：來源分支
        var repoUrl = Env("GITHUB_SERVER_URL") is { } host && Env("GITHUB_REPOSITORY") is { } repo
            ? $"{host.TrimEnd('/')}/{repo}" : null;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(server.TrimEnd('/') + "/"), "api/runs"))
        {
            // 本文是檔案的文字原樣（不重新序列化）：伺服器存的就是 --json 寫出的那一份
            Content = new StringContent(report.Text, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Parity-Tool", "xamlcontrast");
        foreach (var (name, value) in new[]
                 {
                     ("X-Parity-Commit", commit),
                     ("X-Parity-Branch", branch),
                     ("X-Parity-Triggered-By", Env("GITHUB_ACTOR")),
                     ("X-Parity-Repo-Url", repoUrl),
                     // gate 用報告自己記的判定，伺服器不重判 —— 否則 --fail-on fail 或 baseline 模式下會出現「CI 綠燈、儀表板紅字」
                     ("X-Parity-Gate", report.GateFailed is { } failed ? (failed ? "fail" : "pass") : null),
                 })
        {
            if (value is null) continue;
            // 標頭只能帶可見的 ASCII：分支名可以是中文（非 ASCII 的標頭 .NET 直接拒送、Kestrel 預設也拒收）。
            // 少一欄比整次推送失敗好 —— 但要說出來，不靜默
            if (value.All(c => c is >= ' ' and <= '~')) request.Headers.TryAddWithoutValidation(name, value);
            else Err($"note: {name} not sent — the value has characters an HTTP header cannot carry");
        }

        using var http = new HttpClient(new SocketsHttpHandler
        {
            // 不跟轉址：跟了的話 POST 會變成 GET、本文被丟掉，.NET 還會拿掉 Authorization —— 使用者只看到莫名的 401／405。
            // 直接回報 3xx 與 Location（見下面的提示）；token 也因此只會送到使用者指名的那台伺服器
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds),
        };

        HttpResponseMessage response;
        try { response = http.Send(request); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // TLS 之類的失敗外層只說 "see inner exception"，真正的原因在內層
            var inner = ex.InnerException?.Message is { } m && !ex.Message.Contains(m) ? $" ({m})" : "";
            Err($"cannot reach {server}: {ex.Message}{inner}");
            return 2;
        }
        catch (OperationCanceledException)
        {
            // HttpClient.Timeout 到期丟的是 TaskCanceledException
            Err($"no response from {server} within {TimeoutSeconds} seconds");
            return 2;
        }

        using (response)
        {
            var body = ReadBody(response);
            if (response.IsSuccessStatusCode) return Pushed(reportPath, server, report, body);

            var code = (int)response.StatusCode;
            var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;
            Err(ErrorText(body) is { } error ? $"server returned {code} {reason}: {error}" : $"server returned {code} {reason}");
            var hint = code switch
            {
                401 => "hint: the token was not accepted — check XAMLCONTRAST_TOKEN against the project token the server's create-project command printed",
                413 => "hint: the report is larger than the server accepts",
                >= 300 and < 400 when response.Headers.Location is { } location =>
                    $"hint: the server redirects to {location} — pass that address as --server (redirects are not followed)",
                _ => null, // 422（伺服器不支援這個 schemaVersion）等：伺服器自己的訊息已經講清楚
            };
            if (hint is not null) Err(hint);
            return 2;
        }
    }

    private sealed record ReportFile(string Text, bool? GateFailed);

    /// <summary>讀檔並驗信封：存在、UTF-8、JSON 物件、這個版本的 schemaVersion、有 summary 與 findings。不合格 → null（訊息已印）。</summary>
    private static ReportFile? ReadReport(string path)
    {
        if (!File.Exists(path))
        {
            Err($"report not found: {path} — write one first with xamlcontrast <project-root> --json <file>");
            return null;
        }

        string text;
        try
        {
            var bytes = File.ReadAllBytes(path);
            // BOM 拿掉（編輯器另存時會加；JSON 本文開頭的 U+FEFF 伺服器不收）；不是 UTF-8 就不是 --json 寫的
            var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            Err($"{path} is not UTF-8 text — not a report written by xamlcontrast --json");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Err($"cannot read the report {path}: {ex.Message}");
            return null;
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException ex)
        {
            Err($"{path} is not valid JSON: {ex.Message}");
            return null;
        }
        using (doc)
        {
            var root = doc.RootElement;
            const string Expected = "expected the report that xamlcontrast --json writes";
            if (root.ValueKind != JsonValueKind.Object)
            {
                Err($"{path} is not an XamlContrast report — {Expected}");
                return null;
            }
            // Parity 的 report.json 也會被推到同一台伺服器 —— 拿錯檔是最可能的錯，點名它
            if (root.TryGetProperty("reports", out var reports) && reports.ValueKind == JsonValueKind.Array &&
                !root.TryGetProperty("findings", out _))
            {
                Err($"{path} looks like a Parity report (it has a 'reports' array) — push it with `parity push`; " +
                    $"xamlcontrast --push takes the report that xamlcontrast --json writes");
                return null;
            }
            if (!root.TryGetProperty("schemaVersion", out var sv) || sv.ValueKind != JsonValueKind.Number || !sv.TryGetInt32(out var version))
            {
                Err($"{path} is not an XamlContrast report — it has no schemaVersion ({Expected})");
                return null;
            }
            // 比這個版本新的格式：這顆 CLI 不知道它長怎樣，伺服器多半也會回 422 —— 在本機就講清楚
            if (version > Report.JsonSchemaVersion)
            {
                Err($"{path} has schemaVersion {version}, written by a newer XamlContrast (this version pushes {Report.JsonSchemaVersion}) — " +
                    "push it with that version or newer");
                return null;
            }
            if (version < Report.JsonSchemaVersion)
            {
                Err($"{path} has schemaVersion {version}, written by XamlContrast 0.x (this version pushes {Report.JsonSchemaVersion}) — " +
                    "re-run the audit with this version (--json <file>) and push that");
                return null;
            }
            if (!(root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object) ||
                !(root.TryGetProperty("findings", out var findings) && findings.ValueKind == JsonValueKind.Array))
            {
                Err($"{path} is not an XamlContrast report — it has schemaVersion {version} but no summary or findings ({Expected})");
                return null;
            }

            // 報告記的稽核判定：0 通過；1（低於 gate）與 3（結果不可信）都是紅燈；null（函式庫產的報告）→ 不帶，讓伺服器用預設口徑
            bool? gateFailed = root.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.TryGetInt32(out var exit)
                ? exit switch { 0 => false, 1 or 3 => true, _ => null }
                : null;
            return new ReportFile(text, gateFailed);
        }
    }

    /// <summary>2xx：印 run 的摘要；GitHub Actions 上另外在 job summary 加一行連結 —— 紅燈的 PR 到儀表板那頁不必靠人找。</summary>
    private static int Pushed(string path, string server, ReportFile report, string body)
    {
        var ack = default(JsonElement);
        try
        {
            using var doc = JsonDocument.Parse(body);
            ack = doc.RootElement.Clone();
        }
        catch (JsonException) { /* 2xx 但本文不是 JSON：照樣算送出，欄位印 ? */ }

        var project = Text(ack, "project") ?? "?";
        var result = $"{Text(ack, "fail") ?? "?"} fail · {Text(ack, "warn") ?? "?"} warn";
        var viewUrl = Text(ack, "url") is { } url ? server.TrimEnd('/') + url : null;
        Out($"pushed: {path}");
        Out($"  project  {project}");
        Out($"  result   {result}");
        if (viewUrl is not null) Out($"  view     {viewUrl}");

        if (Env("GITHUB_STEP_SUMMARY") is { } summaryPath)
        {
            // 圖示用**伺服器記下的判定**（gateFailed），沒有才退回報告自己的 exitCode；兩邊都不知道就不放 —— 不假裝通過
            var verdict = (Bool(ack, "gateFailed") ?? report.GateFailed) switch { true => " ❌", false => " ✅", null => "" };
            // 伺服器給的文字是資料：中括號會截斷連結文字、換行會把這一行拆成兩行
            var label = $"{project}: {result}{verdict}".Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
            var line = viewUrl is not null
                ? $"**XamlContrast** — [{label}]({viewUrl.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29")})"
                : $"**XamlContrast** — {label}";
            try { File.AppendAllText(summaryPath, Report.ConsoleSafe(line) + "\n"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // 已經送出去了：job summary 是附帶的，寫不進去不改退出碼（改成 2 會讓人重跑、同一份報告推兩次）
                Err($"note: cannot append to the job summary ({summaryPath}): {ex.Message}");
            }
        }
        return 0;
    }

    private static string ReadBody(HttpResponseMessage response)
    {
        try
        {
            using var reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException) { return ""; }
    }

    /// <summary>錯誤回應的說明：JSON 的 error 欄位；不是 JSON（proxy 的 HTML 之類）就取本文開頭；沒有本文（413 常見）→ null。</summary>
    private static string? ErrorText(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (Text(doc.RootElement, "error") is { } error) return error;
        }
        catch (JsonException) { }
        var trimmed = body.Trim();
        return trimmed.Length > 300 ? trimmed[..300] + "…" : trimmed;
    }

    private static string? Text(JsonElement e, string prop)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static bool? Bool(JsonElement e, string prop)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    /// <summary>沒設與空字串一律當沒設（Linux 上 export X= 是有定義的空字串）。</summary>
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    // 伺服器回的文字、使用者給的路徑與網址都是資料：控制字元跳脫（Report.ConsoleSafe），換行後的 ::warning 落不到行首、
    // 變不成 runner 會解析的 workflow command（同 Program.Say）
    private static void Out(string line) => Console.Out.WriteLine(Report.ConsoleSafe(line));
    private static void Err(string line) => Console.Error.WriteLine(Report.ConsoleSafe(line));
}

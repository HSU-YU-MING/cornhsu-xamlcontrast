using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 匿名使用統計（1.1）—— 照 stats-collector 的 PROTOCOL.md 一條一條驗：什麼時候送（第 2 節）、開關與順序（第 3 節）、
/// 告知（第 4 節）、預覽（第 5 節）、傳輸（第 6 節）、資料（第 7 節，收集站多一個欄位、少一個欄位都拒收）。
///
/// ⚠ 研究資料的保護：這裡的每一次執行都**不可能**送到真的收集站。
///   - 收集站一律是 127.0.0.1 上的 stub（<see cref="PushStub"/>），經 CORNHSU_STATS_URL 指過去；
///   - 另外把 HTTP(S)_PROXY 指到第二個 stub（<c>egress</c>）、NO_PROXY 只放行 127.0.0.1：哪條路徑出錯、真的往
///     stats.cornhsu.com 送，請求也只會落到 egress，測試看得到、網路上什麼都沒發生；
///   - CI=true（每次一行、不寫標記檔）：不碰這台機器的使用者設定目錄。只有在 Linux 上、標記檔指到暫存目錄時才測本機說明。
/// 環境變數一律只設在子行程（<see cref="Cli.RunEnv"/>）：runner 自己帶著 CI、GITHUB_ACTIONS、GITHUB_STEP_SUMMARY。
/// </summary>
public class CliStatsTests
{
    private const string CiLine = "xamlcontrast: anonymous usage statistics sent (counts only) - https://stats.cornhsu.com/ - turn off with DO_NOT_TRACK=1";
    private const string FirstNoticeLastLine = "This notice is shown once on this machine.";

    /// <summary>統計會讀的變數全部先拿掉（不吃這台機器或 runner 的值），再套上 overrides。</summary>
    private static Dictionary<string, string?> Env(string? statsUrl, PushStub egress, params (string Name, string? Value)[] overrides)
    {
        var env = new Dictionary<string, string?>
        {
            ["DO_NOT_TRACK"] = null,
            ["CORNHSU_SHARE_STATS"] = null,
            ["XAMLCONTRAST_SHARE_STATS"] = null,
            ["CORNHSU_STATS_URL"] = statsUrl,
            ["CI"] = "true",
            ["GITHUB_ACTIONS"] = null,
            ["GITHUB_STEP_SUMMARY"] = null,
            ["XAMLCONTRAST_SERVER"] = null,
            ["XAMLCONTRAST_TOKEN"] = null,
        };
        // 大小寫兩種都設：Linux 上 .NET 兩種都讀（Windows 的環境變數不分大小寫，等於設同一個）
        foreach (var name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "https_proxy", "http_proxy" }) env[name] = egress.Url;
        foreach (var name in new[] { "ALL_PROXY", "all_proxy" }) env[name] = null;
        foreach (var name in new[] { "NO_PROXY", "no_proxy" }) env[name] = "127.0.0.1,localhost";
        foreach (var (name, value) in overrides) env[name] = value;
        return env;
    }

    private static string ToolVersion() => Cli.Run("--version").Out.Trim();

    private static string OsLabel() => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "other";

    /// <summary>--stats-preview 在 stderr 印的 JSON（標題行與結論行之間）。</summary>
    private static string PreviewJson(string stderr)
    {
        var lines = stderr.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("anonymous usage statistics (--stats-preview: nothing is sent)", StringComparison.Ordinal));
        Assert.True(start >= 0, stderr);
        var end = Array.FindIndex(lines, start, l => l.StartsWith("would ", StringComparison.Ordinal));
        Assert.True(end > start, stderr);
        return string.Join("\n", lines[(start + 1)..end]);
    }

    private static string[] Lines(string s) => s.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact] // 第 7 節，逐欄：demo 的這一筆送出去長什麼樣。外層欄位與順序、data 的每一個 key 與值；標頭只有 Host／Content-Type／Content-Length
           // （不帶 User-Agent、cookie、traceparent 或任何自訂標頭，第 6 節）；本文 UTF-8 不帶 BOM。stdout 與退出碼跟不送的時候一模一樣
    public void DemoPayloadMatchesTheProtocolFieldForField()
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var output = new Fixture();
        var json = Path.Combine(output.Root, "report.json");
        var demo = Repo.PathOf("samples/demo");
        var r = Cli.RunEnv(Env(collector.Url, egress), demo, "--json", json);
        Assert.True(r.Exit == 1, r.ToString());

        var req = Assert.Single(collector.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("/v1/xamlcontrast", req.Target);
        Assert.Equal("application/json", req.Headers["Content-Type"]);
        Assert.Equal(new[] { "Content-Length", "Content-Type", "Host" }, req.Headers.Keys.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        Assert.False(req.Body is [0xEF, 0xBB, 0xBF, ..]);

        var expected = JsonNode.Parse($$"""
            {
              "schema": 1,
              "tool": "xamlcontrast",
              "toolVersion": "{{ToolVersion()}}",
              "os": "{{OsLabel()}}",
              "ci": true,
              "projectId": null,
              "data": {
                "exitCode": 1,
                "failOn": "warn",
                "paletteMode": "pair",
                "paletteDegraded": false,
                "files": 1,
                "pairs": 8,
                "coverage": 100,
                "counts": { "fail": 3, "warn": 0, "ok": 3, "decorative": 1 },
                "unresolvedBy": {},
                "ratios": [1, 1, 1, 1, 0, 2]
              }
            }
            """)!;
        var sent = JsonNode.Parse(Encoding.UTF8.GetString(req.Body))!;
        Assert.True(JsonNode.DeepEquals(expected, sent), sent.ToJsonString());
        // key 的順序也照協定（收集站不在乎，但讀資料的人在乎：跟協定的範例逐行對得上）
        Assert.Equal(expected.AsObject().Select(p => p.Key), sent.AsObject().Select(p => p.Key));
        Assert.Equal(expected["data"]!.AsObject().Select(p => p.Key), sent["data"]!.AsObject().Select(p => p.Key));

        // ratios 是從報告算得出來的：非裝飾配對依最差比值（兩個主題取低的）分桶 —— 與 --json 的 findings 獨立驗一次
        var buckets = new int[6];
        foreach (var f in Cli.ReadJson(json).GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("category").GetString() != "decorative"))
        {
            var worst = new[] { f.GetProperty("ratioDark"), f.GetProperty("ratioLight") }.Where(v => v.ValueKind == JsonValueKind.Number).Min(v => v.GetDouble());
            buckets[worst < 1.5 ? 0 : worst < 2 ? 1 : worst < 3 ? 2 : worst < 4.5 ? 3 : worst < 7 ? 4 : 5]++;
        }
        Assert.Equal(buckets, sent["data"]!["ratios"]!.AsArray().Select(n => n!.GetValue<int>()));

        // 不影響結果：stdout、退出碼與 DO_NOT_TRACK 的那一次一模一樣；stderr 只多 CI 那一行
        var quiet = Cli.RunEnv(Env(collector.Url, egress, ("DO_NOT_TRACK", "1")), demo, "--json", json);
        Assert.Equal(quiet.Exit, r.Exit);
        Assert.Equal(quiet.Out, r.Out);
        Assert.Equal([CiLine], Lines(r.Err));
        Assert.Equal("", quiet.Err);
        Assert.Single(collector.Requests);
        Assert.Empty(egress.Requests);
    }

    [Fact] // data 的其他形狀：unresolved 的原因（只列 > 0 的）、實際生效的 gate（config 的 failOn）、色盤退化、設定檔的 projectId；
           // 結果不可信（exit 3）也是一個判定、照送
    public void PayloadCarriesReasonsTheGateAndTheProjectId()
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.QuarterCoverage();
        fx.File(ToolConfig.FileName, """{ "failOn": "fail", "minCoverage": 0, "stats": { "projectId": "acme_desktop-2026-01" } }""");
        var pass = Cli.RunEnv(Env(collector.Url, egress), fx.Root);
        Assert.True(pass.Exit == 0, pass.ToString());

        fx.File(ToolConfig.FileName, """{ "stats": { "share": true } }""");
        var untrusted = Cli.RunEnv(Env(collector.Url, egress), fx.Root);
        Assert.True(untrusted.Exit == 3, untrusted.ToString());

        Assert.Equal(2, collector.Requests.Count);
        var first = JsonNode.Parse(Encoding.UTF8.GetString(collector.Requests[0].Body))!;
        Assert.Equal("acme_desktop-2026-01", first["projectId"]!.GetValue<string>());
        var d = first["data"]!;
        Assert.Equal(0, d["exitCode"]!.GetValue<int>());
        Assert.Equal("fail", d["failOn"]!.GetValue<string>());
        Assert.Equal("none", d["paletteMode"]!.GetValue<string>());
        Assert.True(d["paletteDegraded"]!.GetValue<bool>());
        Assert.Equal(25, d["coverage"]!.GetValue<double>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "bound-or-gradient": 3 }"""), d["unresolvedBy"]), d["unresolvedBy"]!.ToJsonString());
        Assert.Equal([0, 0, 0, 0, 0, 1], d["ratios"]!.AsArray().Select(n => n!.GetValue<int>()));

        var second = JsonNode.Parse(Encoding.UTF8.GetString(collector.Requests[1].Body))!;
        Assert.Null(second["projectId"]);
        Assert.Equal(3, second["data"]!["exitCode"]!.GetValue<int>());
        Assert.Equal("warn", second["data"]!["failOn"]!.GetValue<string>());
        Assert.Empty(egress.Requests);
    }

    [Theory] // 第 3 節：任一個說不送就不送，依序檢查、第一個說不送的勝（預覽說的就是那一個）。開關寫錯（打錯字、多一個空白）往「不送」那邊錯
    [InlineData(null, "DO_NOT_TRACK is set", "DO_NOT_TRACK=1")]
    [InlineData(null, "DO_NOT_TRACK is set", "DO_NOT_TRACK=true")]
    [InlineData(null, "DO_NOT_TRACK is set", "DO_NOT_TRACK=yes")]
    [InlineData(null, "DO_NOT_TRACK is set", "DO_NOT_TRACK= ")]
    [InlineData(null, "DO_NOT_TRACK is set", "DO_NOT_TRACK=1", "XAMLCONTRAST_SHARE_STATS=true", "CORNHSU_SHARE_STATS=on")] // DO_NOT_TRACK 永遠優先
    [InlineData("""{ "stats": { "share": true } }""", "DO_NOT_TRACK is set", "DO_NOT_TRACK=1")]                          // 設定檔說好也一樣
    [InlineData(null, "CORNHSU_SHARE_STATS is 'false'", "CORNHSU_SHARE_STATS=false")]
    [InlineData(null, "CORNHSU_SHARE_STATS is '0'", "CORNHSU_SHARE_STATS=0", "XAMLCONTRAST_SHARE_STATS=true")]
    [InlineData(null, "XAMLCONTRAST_SHARE_STATS is 'false'", "XAMLCONTRAST_SHARE_STATS=false")]                       // Action 的 share-stats: false
    [InlineData(null, "XAMLCONTRAST_SHARE_STATS is 'off'", "XAMLCONTRAST_SHARE_STATS=off")]
    [InlineData(null, "XAMLCONTRAST_SHARE_STATS is 'no'", "XAMLCONTRAST_SHARE_STATS=no")]
    [InlineData(null, "XAMLCONTRAST_SHARE_STATS is 'ture'", "XAMLCONTRAST_SHARE_STATS=ture")]                         // 打錯字 → 不送
    [InlineData(null, "XAMLCONTRAST_SHARE_STATS is ' true'", "XAMLCONTRAST_SHARE_STATS= true")]                       // 多一個空白 → 不送
    [InlineData("""{ "stats": { "share": false } }""", "\"stats\": { \"share\": false } in xamlcontrast.config.json")]
    [InlineData("""{ "stats": { "share": false } }""", "\"stats\": { \"share\": false }", "XAMLCONTRAST_SHARE_STATS=true", "CORNHSU_SHARE_STATS=1")]
    public void EachSwitchTurnsItOff(string? config, string reason, params string[] env)
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing();
        if (config is not null) fx.File(ToolConfig.FileName, config);
        var overrides = env.Select(e => (e[..e.IndexOf('=')], (string?)e[(e.IndexOf('=') + 1)..])).ToArray();

        var preview = Cli.RunEnv(Env(collector.Url, egress, overrides), fx.Root, "--stats-preview");
        Assert.True(preview.Exit == 0, preview.ToString());
        Assert.Contains($"would not be sent: {reason}", preview.Err);

        var run = Cli.RunEnv(Env(collector.Url, egress, overrides), fx.Root);
        Assert.True(run.Exit == 0, run.ToString());
        Assert.Equal("", run.Err); // 不送的時候什麼都不印
        Assert.Empty(collector.Requests);
        Assert.Empty(egress.Requests);
    }

    [Theory] // 反過來：DO_NOT_TRACK 是 0／false（不分大小寫）或空字串、*_SHARE_STATS 是 1／true／yes／on（不分大小寫）= 不表態 → 照預設送
    [InlineData(null, "DO_NOT_TRACK=0")]
    [InlineData(null, "DO_NOT_TRACK=false")]
    [InlineData(null, "DO_NOT_TRACK=FALSE")]
    [InlineData(null, "DO_NOT_TRACK=")]
    [InlineData(null, "XAMLCONTRAST_SHARE_STATS=1")]
    [InlineData(null, "XAMLCONTRAST_SHARE_STATS=TRUE", "CORNHSU_SHARE_STATS=Yes")]
    [InlineData(null, "CORNHSU_SHARE_STATS=on")]
    [InlineData("""{ "stats": { "share": true } }""")]
    [InlineData("""{ "stats": {} }""")]
    public void NeutralValuesLeaveTheDefaultOn(string? config, params string[] env)
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing();
        if (config is not null) fx.File(ToolConfig.FileName, config);
        var overrides = env.Select(e => (e[..e.IndexOf('=')], (string?)e[(e.IndexOf('=') + 1)..])).ToArray();
        var r = Cli.RunEnv(Env(collector.Url, egress, overrides), fx.Root);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Single(collector.Requests);
        Assert.Empty(egress.Requests);
    }

    [Fact] // 安全網本身要真的有效：不是 127.0.0.1 的目的地一律經 proxy 落到 egress —— 不然「出錯也不會出這台機器」只是一句話。
           // 用 .invalid（保留給測試、永遠解析不到的網域）當目的地，就算 proxy 沒生效也什麼都送不出去
    public void TheEgressGuardCatchesEveryNonLoopbackRequest()
    {
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing();
        var r = Cli.RunEnv(Env("http://stats.example.invalid", egress), fx.Root);
        Assert.True(r.Exit == 0, r.ToString());
        var req = Assert.Single(egress.Requests);
        Assert.Equal("http://stats.example.invalid/v1/xamlcontrast", req.Target); // 經 proxy 的請求帶完整網址
    }

    [DevBuildFact] // 第 3 節第 5 條：從原始碼建出來的（0.0.0-dev）不送，除非設了 CORNHSU_STATS_URL —— 維護者自己的建置不混進研究資料。
                   // 這條拿掉了 DO_NOT_TRACK、也沒有 stub 網址：真的出錯的話，請求會落到 egress（proxy），不會出這台機器
    public void DevelopmentBuildDoesNotSendWithoutAStatsUrl()
    {
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing();
        var preview = Cli.RunEnv(Env(null, egress), fx.Root, "--stats-preview");
        Assert.True(preview.Exit == 0, preview.ToString());
        Assert.Contains("would not be sent: this is a development build (0.0.0-dev); set CORNHSU_STATS_URL to send from one", preview.Err);
        Assert.Contains("\"toolVersion\": \"0.0.0-dev\"", preview.Err);

        var run = Cli.RunEnv(Env(null, egress), fx.Root);
        Assert.True(run.Exit == 0, run.ToString());
        Assert.Equal("", run.Err);
        // CORNHSU_STATS_URL 不是絕對的 http／https 網址 → 當作沒設（開發版照樣不送）
        var bad = Cli.RunEnv(Env("ftp://127.0.0.1/x", egress), fx.Root, "--stats-preview");
        Assert.Contains("would not be sent: this is a development build", bad.Err);
        Assert.Empty(egress.Requests);
    }

    [Fact] // 第 5 節：--stats-preview 照常稽核、什麼都不送，在 stderr 印出這次會送的完整 JSON 與會不會送、為什麼；
           // 退出碼與 stdout 不變、不印說明。印出來的就是送出去的那幾個位元組（拿掉預覽之後真的送一次來比）
    public void PreviewPrintsTheExactPayloadAndSendsNothing()
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        var demo = Repo.PathOf("samples/demo");
        var preview = Cli.RunEnv(Env(collector.Url, egress), demo, "--stats-preview");
        Assert.True(preview.Exit == 1, preview.ToString());
        Assert.Empty(collector.Requests);
        Assert.DoesNotContain(CiLine, preview.Err);
        Assert.Contains($"would be sent to {collector.Url}/v1/xamlcontrast (CORNHSU_STATS_URL is set)", preview.Err);
        var printed = PreviewJson(preview.Err);

        var sent = Cli.RunEnv(Env(collector.Url, egress), demo);
        Assert.Equal(printed, Encoding.UTF8.GetString(Assert.Single(collector.Requests).Body));
        Assert.Equal(sent.Out, preview.Out);
        Assert.Equal(sent.Exit, preview.Exit);

        // 預覽不吃掉其他輸出：--json 照寫
        using var output = new Fixture();
        var withJson = Cli.RunEnv(Env(collector.Url, egress, ("DO_NOT_TRACK", "1")), demo, "--stats-preview", "--json", Path.Combine(output.Root, "r.json"));
        Assert.True(File.Exists(Path.Combine(output.Root, "r.json")), withJson.ToString());
        Assert.Contains("would not be sent: DO_NOT_TRACK is set", withJson.Err);
        Assert.Single(collector.Requests);
        Assert.Empty(egress.Requests);
    }

    [Theory] // 第 1、6 節：收集站連不上、回 500、轉址、不回應 —— 退出碼與 stdout 一個字都不變、stderr 只有 CI 那一行（不印錯誤），
             // 最多多花約 3 秒（整體上限），不重試、不跟轉址
    [InlineData("refused")]
    [InlineData("500")]
    [InlineData("redirect")]
    [InlineData("hang")]
    public void AFailingCollectorNeverChangesTheResult(string mode)
    {
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var redirectTarget = new PushStub(204, null, "No Content");
        using var fx = CliProjects.Failing();
        using var collector = mode switch
        {
            "500" => new PushStub(500, "<html>oops</html>", "Internal Server Error"),
            "redirect" => new PushStub(307, null, "Temporary Redirect", $"Location: {redirectTarget.Url}/v1/xamlcontrast"),
            "hang" => new PushStub(204, null, "No Content") { Hang = true },
            _ => null,
        };
        string url;
        if (collector is not null) url = collector.Url;
        else
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            url = $"http://127.0.0.1:{((System.Net.IPEndPoint)probe.LocalEndpoint).Port}";
            probe.Stop();
        }

        var clock = Stopwatch.StartNew();
        var baseline = Cli.RunEnv(Env(url, egress, ("DO_NOT_TRACK", "1")), fx.Root);
        var quietTime = clock.Elapsed;
        clock.Restart();
        var r = Cli.RunEnv(Env(url, egress), fx.Root);
        var time = clock.Elapsed;

        Assert.True(r.Exit == 1, r.ToString());
        Assert.Equal(baseline.Exit, r.Exit);
        Assert.Equal(baseline.Out, r.Out);
        Assert.Equal([CiLine], Lines(r.Err));
        if (collector is not null) Assert.Single(collector.Requests); // 不重試
        Assert.Empty(redirectTarget.Requests);                          // 不跟轉址
        Assert.Empty(egress.Requests);
        Assert.True(time - quietTime < TimeSpan.FromSeconds(5), $"{mode}: {time} vs {quietTime} without statistics");
        if (mode == "hang") Assert.True(time - quietTime > TimeSpan.FromSeconds(2), $"hang: {time} vs {quietTime} — did it wait for the 3 s limit at all?");
    }

    [Fact] // 第 4 節的 CI：CI 或 GITHUB_ACTIONS 是 true 就每次印一行（不寫標記檔、不看標記檔）；GitHub Actions 上 job summary 每次一行。
           // 不送的時候 job summary 也不碰
    public void OnCiTheOneLineNoticeIsPrintedEveryTime()
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing().File("summary.md", "# earlier step\n");
        var summary = Path.Combine(fx.Root, "summary.md");
        foreach (var ci in new[] { ("CI", "true"), ("CI", "TRUE"), ("GITHUB_ACTIONS", "true") })
        {
            var env = Env(collector.Url, egress, ("CI", null), ci, ("GITHUB_STEP_SUMMARY", summary));
            var r = Cli.RunEnv(env, fx.Root);
            Assert.True(r.Exit == 0, r.ToString());
            Assert.Equal([CiLine], Lines(r.Err));
            Assert.DoesNotContain(FirstNoticeLastLine, r.Err);
        }
        var lines = File.ReadAllText(summary, Encoding.UTF8).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.All(lines[1..], l => Assert.StartsWith("**XamlContrast** — anonymous usage statistics sent (counts only", l));
        Assert.All(lines[1..], l => Assert.Contains("https://stats.cornhsu.com/", l));

        var off = Cli.RunEnv(Env(collector.Url, egress, ("GITHUB_STEP_SUMMARY", summary), ("DO_NOT_TRACK", "1")), fx.Root);
        Assert.Equal("", off.Err);
        Assert.Equal(4, File.ReadAllText(summary, Encoding.UTF8).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(3, collector.Requests.Count);
        Assert.Empty(egress.Requests);
    }

    [LinuxOnlyFact] // 第 4 節的本機：第一次要送的時候印完整說明、寫標記檔，之後不再印（照樣送）；預覽不印也不寫；標記寫不進去 → 下次照樣印。
                    // 只在 Linux 跑：標記在 ApplicationData，Linux 上跟著 XDG_CONFIG_HOME 走、可以指到暫存目錄；Windows 的 %APPDATA%
                    // 不吃環境變數（Shell API），在這裡跑會寫到開發者真的設定目錄
    public void LocallyTheFullNoticeIsShownOncePerMachine()
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing();
        using var home = new Fixture();
        var config = Directory.CreateDirectory(Path.Combine(home.Root, "config")).FullName;
        var marker = Path.Combine(config, "cornhsu", "xamlcontrast-stats-notice");
        var env = Env(collector.Url, egress, ("CI", null), ("XDG_CONFIG_HOME", config), ("HOME", home.Root));

        var preview = Cli.RunEnv(env, fx.Root, "--stats-preview");
        Assert.DoesNotContain(FirstNoticeLastLine, preview.Err);
        Assert.False(File.Exists(marker));

        var first = Cli.RunEnv(env, fx.Root);
        Assert.True(first.Exit == 0, first.ToString());
        Assert.Contains("XamlContrast sends anonymous usage statistics: counts and score ranges only", first.Err);
        Assert.Contains("Turn it off: \"stats\": { \"share\": false } in xamlcontrast.config.json, or XAMLCONTRAST_SHARE_STATS=false,", first.Err);
        Assert.Contains(FirstNoticeLastLine, first.Err);
        Assert.DoesNotContain(CiLine, first.Err);
        Assert.DoesNotContain("anonymous usage statistics", first.Out); // 說明只在 stderr
        Assert.True(File.Exists(marker));

        var second = Cli.RunEnv(env, fx.Root);
        Assert.Equal("", second.Err);
        Assert.Equal(2, collector.Requests.Count);

        // 標記寫不進去（設定目錄其實是一個檔案）→ 每次都印
        var blocked = Path.Combine(home.Root, "not-a-dir");
        File.WriteAllText(blocked, "");
        var blockedEnv = Env(collector.Url, egress, ("CI", null), ("XDG_CONFIG_HOME", blocked), ("HOME", home.Root));
        Assert.Contains(FirstNoticeLastLine, Cli.RunEnv(blockedEnv, fx.Root).Err);
        Assert.Contains(FirstNoticeLastLine, Cli.RunEnv(blockedEnv, fx.Root).Err);
        Assert.Equal(4, collector.Requests.Count);
        Assert.Empty(egress.Requests);
    }

    [Theory] // 第 2 節：只有稽核得出判定（0／1／3）才送。用法錯誤、設定錯誤（含格式不對的 projectId）、輸出寫不出去（exit 2）、
             // --help、--version、--push 都不送
    [InlineData(new[] { "{root}", "--fail-on", "bogus" }, 2, "--fail-on takes")]
    [InlineData(new[] { "{root}/no-such-dir" }, 2, "root not found")]
    [InlineData(new[] { "{root}", "--json", "{root}/no-such-dir/r.json" }, 2, "cannot write json")]
    [InlineData(new[] { "{root}", "--html", "{root}/no-such-dir/r.html", "--stats-preview" }, 2, "cannot write html")]
    [InlineData(new[] { "--version" }, 0, null)]
    [InlineData(new[] { "--help" }, 0, null)]
    public void NothingIsSentWithoutAnAuditVerdict(string[] args, int exit, string? expectInStderr)
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing();
        var r = Cli.RunEnv(Env(collector.Url, egress), args.Select(a => a.Replace("{root}", fx.Root)).ToArray());
        Assert.True(r.Exit == exit, r.ToString());
        if (expectInStderr is not null) Assert.Contains(expectInStderr, r.Err);
        Assert.DoesNotContain("anonymous usage statistics", r.Err);
        Assert.Empty(collector.Requests);
        Assert.Empty(egress.Requests);
    }

    [Fact] // 設定檔的 projectId 格式不對 → config error、exit 2、不送（不是安靜丟掉 ID 照送，也不是送一筆收集站會拒收的）
    public void InvalidProjectIdIsAConfigErrorAndNothingIsSent()
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var fx = CliProjects.Passing().File(ToolConfig.FileName, """{ "stats": { "projectId": "acme/app" } }""");
        var r = Cli.RunEnv(Env(collector.Url, egress), fx.Root);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("config error: stats.projectId must be 16-64 characters, got 8", r.Err);
        Assert.Empty(collector.Requests);
        Assert.Empty(egress.Requests);
    }

    [Fact] // --push（實驗性）只上傳、不稽核：不送統計；--stats-preview 是稽核的旗標，與 --push 並用是用法錯誤
    public void PushSendsNoStatistics()
    {
        using var collector = new PushStub(204, null, "No Content");
        using var egress = new PushStub(502, null, "Bad Gateway");
        using var dashboard = new PushStub(201, """{ "id": "1", "project": "demo", "tool": "xamlcontrast", "gateFailed": false, "fail": 0, "warn": 0, "coverage": 100, "url": "/runs/1" }""");
        using var fx = CliProjects.Passing();
        var report = Path.Combine(fx.Root, "report.json");
        Assert.Equal(0, Cli.RunEnv(Env(collector.Url, egress, ("DO_NOT_TRACK", "1")), fx.Root, "--json", report).Exit);

        var env = Env(collector.Url, egress, ("XAMLCONTRAST_SERVER", dashboard.Url), ("XAMLCONTRAST_TOKEN", "xc-test-token"));
        var push = Cli.RunEnv(env, "--push", report);
        Assert.True(push.Exit == 0, push.ToString());
        Assert.Single(dashboard.Requests);
        var both = Cli.RunEnv(env, "--push", report, "--stats-preview");
        Assert.True(both.Exit == 2, both.ToString());
        Assert.Contains("drop --stats-preview", both.Err);
        Assert.Empty(collector.Requests);
        Assert.Empty(egress.Requests);
    }

    [Fact] // --help 寫出 --stats-preview 與怎麼關掉（凍結面：CLI 旗標的對外說明）
    public void HelpDescribesTheStatisticsAndHowToTurnThemOff()
    {
        var r = Cli.Run("--help");
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Contains("--stats-preview", r.Out);
        foreach (var off in new[] { "DO_NOT_TRACK=1", "XAMLCONTRAST_SHARE_STATS=false", "\"stats\": { \"share\": false }", "https://stats.cornhsu.com/" })
            Assert.Contains(off, r.Out);
    }

    [Theory] // 維護者自己的 CI 不混進研究資料：兩個 workflow 都在最上層設 DO_NOT_TRACK（release 用真的版號建置再跑測試與 demo ——
             // 少了這一行，每發一次版就送出幾筆真的統計）。拿掉它的人會在這裡紅
    [InlineData(".github/workflows/ci.yml")]
    [InlineData(".github/workflows/release.yml")]
    public void WorkflowsTurnStatisticsOffForTheMaintainersOwnRuns(string workflow)
    {
        var yml = File.ReadAllText(Repo.PathOf(workflow)).Replace("\r\n", "\n");
        Assert.Matches(new System.Text.RegularExpressions.Regex(@"^env:\n  DO_NOT_TRACK: '1'$", System.Text.RegularExpressions.RegexOptions.Multiline), yml);
    }

    // ── data 的計算（Core 的 UsageStats；不經子行程）──

    [Theory] // 分桶邊界左閉右開：1.5、2、3、4.5、7 —— 恰好落在邊界上的進上面那一桶
    [InlineData(1.0, 0)]
    [InlineData(1.49, 0)]
    [InlineData(1.5, 1)]
    [InlineData(1.99, 1)]
    [InlineData(2.0, 2)]
    [InlineData(3.0, 3)]
    [InlineData(4.49, 3)]
    [InlineData(4.5, 4)]
    [InlineData(6.99, 4)]
    [InlineData(7.0, 5)]
    [InlineData(21.0, 5)]
    public void RatioBucketsAreLeftClosed(double ratio, int bucket) => Assert.Equal(bucket, UsageStats.RatioBucket(ratio));

    [Fact] // ratios 只算非裝飾配對、用最差比值；只存在於一個主題的配對用那個主題的比值（另一個主題內部填的是同一個值）
    public void RatiosCountNonDecorativePairsByTheirWorstRatio()
    {
        using var fx = new Fixture().WithPalette();
        fx.File("Main.xaml", """
            <StackPanel Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="ok both themes: ≥ 7"/>
              <TextBlock Foreground="{DynamicResource Dim}" Text="fail both themes: &lt; 1.5"/>
              <TextBlock Foreground="#767676" Text="dark 4.x, light 4.x"/>
              <Rectangle Fill="{DynamicResource Dim}" Height="1"/>
            </StackPanel>
            """);
        var r = fx.Run();
        var data = UsageStats.Data(r, 1, "warn");
        var expected = new int[6];
        foreach (var f in r.Findings.Where(f => f.Category != Category.Decorative)) expected[UsageStats.RatioBucket(Math.Min(f.RatioDark, f.RatioLight))]++;
        Assert.Equal(expected, data["ratios"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(r.Findings.Count(f => f.Category != Category.Decorative), expected.Sum());
        Assert.Equal(1, data["counts"]!["decorative"]!.GetValue<int>());
        Assert.Throws<ArgumentOutOfRangeException>(() => UsageStats.Data(r, 2, "warn")); // 2 不是判定，不送
    }

    /// <summary>只在 Linux 上執行（其他平台顯示為略過並附理由）。</summary>
    public sealed class LinuxOnlyFactAttribute : FactAttribute
    {
        public LinuxOnlyFactAttribute()
        {
            if (!OperatingSystem.IsLinux()) Skip = "標記檔在 ApplicationData：只有 Linux 能用 XDG_CONFIG_HOME 指到暫存目錄（Windows 的 %APPDATA% 不吃環境變數）；CI 的 ubuntu 會跑這條";
        }
    }

    /// <summary>測的 CLI 是從原始碼建的 0.0.0-dev 才執行。release.yml 用真的版號建置再跑測試 —— 那時這條沒有意義
    /// （而且那個建置本來就會送，所以 workflow 層設了 DO_NOT_TRACK，這裡的其他測試則一律指到 stub）。</summary>
    public sealed class DevBuildFactAttribute : FactAttribute
    {
        public DevBuildFactAttribute()
        {
            var version = FileVersionInfo.GetVersionInfo(Cli.DllPath).ProductVersion?.Split('+')[0];
            if (version != "0.0.0-dev") Skip = $"這次建置的版本是 {version}，不是開發版 0.0.0-dev";
        }
    }
}

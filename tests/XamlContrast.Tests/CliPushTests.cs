using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// --push（實驗性）：把 --json 寫好的報告送上 Parity.Server。伺服器是 127.0.0.1 上的 stub（<see cref="PushStub"/>），
/// 斷言的是線上協定本身 —— 伺服器端照這個形狀實作，這裡一變，儀表板就收不到或收錯。
/// 退出碼只有 0（送出）與 2（其他一切）：1 與 3 是稽核的判定，上傳失敗不能長得像「有違規」。
/// 環境變數一律只設在子行程上（<see cref="Cli.RunEnv"/>）：CI 的 runner 自己就帶著 GITHUB_SHA、GITHUB_STEP_SUMMARY……
/// </summary>
public class CliPushTests
{
    private const string Token = "xc-test-token-0123456789";
    private const string RunId = "6f1c2d3e-0000-4000-8000-000000000001";

    private static readonly string[] PushVariables =
    [
        "XAMLCONTRAST_SERVER", "XAMLCONTRAST_TOKEN", "GITHUB_SHA", "GITHUB_HEAD_REF", "GITHUB_REF_NAME",
        "GITHUB_ACTOR", "GITHUB_SERVER_URL", "GITHUB_REPOSITORY", "GITHUB_STEP_SUMMARY",
    ];

    /// <summary>子行程的環境：push 讀的變數全部先拿掉（不吃 runner 的值），token 給測試用的值，再套上 overrides。</summary>
    private static Dictionary<string, string?> Env(params (string Name, string? Value)[] overrides)
    {
        var env = PushVariables.ToDictionary(v => v, _ => (string?)null);
        env["XAMLCONTRAST_TOKEN"] = Token;
        foreach (var (name, value) in overrides) env[name] = value;
        return env;
    }

    /// <summary>形狀照 --json 的最小報告：這個版本的 schemaVersion、summary 物件、findings 陣列、exitCode。</summary>
    private static string MinimalReport(int? exitCode) => JsonSerializer.Serialize(new
    {
        schemaVersion = Report.JsonSchemaVersion,
        toolVersion = "1.0.0",
        exitCode,
        summary = new { },
        unresolvedSites = Array.Empty<object>(),
        findings = Array.Empty<object>(),
    });

    /// <summary>伺服器的 201（協定：id、project、tool、gateFailed、fail、warn、coverage、url）。</summary>
    private static string Created(bool? gateFailed = true, string project = "demo-app", string url = $"/runs/{RunId}")
        => JsonSerializer.Serialize(new { id = RunId, project, tool = "xamlcontrast", gateFailed, fail = 2, warn = 3, coverage = 87.5, url });

    private static string[] Lines(string s) => s.Split('\n');

    [Fact] // 協定的核心：POST {server}/api/runs、Bearer token、本文是檔案原樣（真的 --json 寫出的那一份，不重新序列化）
    public void SendsTheReportVerbatimWithTheProtocolHeaders()
    {
        using var fx = CliProjects.Failing();
        var report = Path.Combine(fx.Root, "report.json");
        var audit = Cli.Run(fx.Root, "--json", report);
        Assert.True(audit.Exit == 1, audit.ToString()); // 前提：1 組 fail，報告的 exitCode 是 1

        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(
            ("XAMLCONTRAST_SERVER", stub.Url),
            ("GITHUB_SHA", "0123456789abcdef0123456789abcdef01234567"),
            ("GITHUB_HEAD_REF", "feat/contrast"), // PR 事件的來源分支，勝過 GITHUB_REF_NAME（那是 42/merge）
            ("GITHUB_REF_NAME", "42/merge"),
            ("GITHUB_ACTOR", "octocat"),
            ("GITHUB_SERVER_URL", "https://github.com/"),
            ("GITHUB_REPOSITORY", "acme/app")), "--push", report);

        Assert.True(r.Exit == 0, r.ToString());
        var req = Assert.Single(stub.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("/api/runs", req.Target);
        Assert.Equal($"Bearer {Token}", req.Headers["Authorization"]);
        Assert.Equal("application/json; charset=utf-8", req.Headers["Content-Type"]);
        Assert.Equal("xamlcontrast", req.Headers["X-Parity-Tool"]);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", req.Headers["X-Parity-Commit"]);
        Assert.Equal("feat/contrast", req.Headers["X-Parity-Branch"]);
        Assert.Equal("octocat", req.Headers["X-Parity-Triggered-By"]);
        Assert.Equal("https://github.com/acme/app", req.Headers["X-Parity-Repo-Url"]); // 伺服器網址尾巴的 / 修掉
        Assert.Equal("fail", req.Headers["X-Parity-Gate"]);
        Assert.Equal(File.ReadAllBytes(report), req.Body);
    }

    [Theory] // 伺服器網址有沒有 / 結尾、是不是掛在子路徑下，都接成 <server>/api/runs
    [InlineData("", "/api/runs")]
    [InlineData("/", "/api/runs")]
    [InlineData("/team", "/team/api/runs")]
    [InlineData("/team/", "/team/api/runs")]
    public void ServerUrlIsJoinedWithApiRuns(string suffix, string expectedTarget)
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(), "--push", Path.Combine(fx.Root, "report.json"), "--server", stub.Url + suffix);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Equal(expectedTarget, Assert.Single(stub.Requests).Target);
    }

    [Fact] // 旗標勝過 CI 的環境變數；沒有值的標頭整個不帶（不送空字串）
    public void FlagsWinOverTheEnvironmentAndMissingValuesAreOmitted()
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        var report = Path.Combine(fx.Root, "report.json");
        using var stub = new PushStub(201, Created());

        var flags = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url), ("GITHUB_SHA", "envsha"), ("GITHUB_HEAD_REF", "env-branch")),
            "--push", report, "--commit", "flagsha", "--branch", "flag-branch");
        Assert.True(flags.Exit == 0, flags.ToString());

        // push 事件：GITHUB_HEAD_REF 是有定義的空字串 —— 要退到 GITHUB_REF_NAME，不是送一個空的分支名
        var pushEvent = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url), ("GITHUB_HEAD_REF", ""), ("GITHUB_REF_NAME", "main")),
            "--push", report);
        Assert.True(pushEvent.Exit == 0, pushEvent.ToString());

        // 本機跑：沒有任何 GITHUB_*。只有伺服器網址、沒有 repository 也不帶 repo 網址
        var local = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url), ("GITHUB_SERVER_URL", "https://github.com")), "--push", report);
        Assert.True(local.Exit == 0, local.ToString());

        Assert.Equal(3, stub.Requests.Count);
        var (a, b, c) = (stub.Requests[0].Headers, stub.Requests[1].Headers, stub.Requests[2].Headers);
        Assert.Equal("flagsha", a["X-Parity-Commit"]);
        Assert.Equal("flag-branch", a["X-Parity-Branch"]);
        Assert.Equal("main", b["X-Parity-Branch"]);
        Assert.False(b.ContainsKey("X-Parity-Commit"));
        foreach (var name in new[] { "X-Parity-Commit", "X-Parity-Branch", "X-Parity-Triggered-By", "X-Parity-Repo-Url" })
            Assert.False(c.ContainsKey(name), name);
        Assert.Equal("xamlcontrast", c["X-Parity-Tool"]); // 這一個一律帶
    }

    [Theory] // gate 取報告自己記的判定（伺服器不重判）：0 通過；1 低於 gate、3 結果不可信都是紅燈；null（函式庫產的報告）不帶
    [InlineData(0, "pass")]
    [InlineData(1, "fail")]
    [InlineData(3, "fail")]
    [InlineData(null, null)]
    public void GateHeaderFollowsTheReportsExitCode(int? exitCode, string? expected)
    {
        using var fx = new Fixture().File("report.json", MinimalReport(exitCode));
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url)), "--push", Path.Combine(fx.Root, "report.json"));
        Assert.True(r.Exit == 0, r.ToString());
        var headers = Assert.Single(stub.Requests).Headers;
        if (expected is null) Assert.False(headers.ContainsKey("X-Parity-Gate"));
        else Assert.Equal(expected, headers["X-Parity-Gate"]);
    }

    [Fact] // 201 → exit 0，印 project／result／儀表板連結。報告的 exitCode 是 1 也一樣是 0：上傳成功不是稽核判定
    public void CreatedPrintsTheRunAndExitsZero()
    {
        using var fx = new Fixture().File("report.json", MinimalReport(1));
        var report = Path.Combine(fx.Root, "report.json");
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url + "/")), "--push", report);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Contains($"pushed: {report}", r.Out);
        Assert.Contains("  project  demo-app", r.Out);
        Assert.Contains("  result   2 fail ", r.Out);
        Assert.Contains(" 3 warn", r.Out);
        Assert.Contains($"  view     {stub.Url}/runs/{RunId}", r.Out); // 伺服器網址的 / 不重複
        Assert.Equal("", r.Err.Trim());
    }

    [Theory] // 非 2xx → exit 2，stderr 帶狀態碼與伺服器 JSON 的 error；401／413 另附提示（413 常常沒有本文）
    [InlineData(401, "Unauthorized", """{ "error": "invalid project token" }""", "invalid project token", "XAMLCONTRAST_TOKEN")]
    [InlineData(422, "Unprocessable Entity", """{ "error": "schemaVersion 6 is not supported for xamlcontrast reports" }""", "is not supported for xamlcontrast reports", null)]
    [InlineData(400, "Bad Request", """{ "error": "findings must be an array" }""", "findings must be an array", null)]
    [InlineData(413, "Payload Too Large", null, "server returned 413 Payload Too Large", "larger than the server accepts")]
    [InlineData(500, "Internal Server Error", "<html>oops</html>", "<html>oops</html>", null)] // 不是 JSON：取本文開頭
    public void ServerErrorsExitTwoWithTheServersMessage(int status, string reason, string? body, string expectInStderr, string? hint)
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        using var stub = new PushStub(status, body, reason);
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url)), "--push", Path.Combine(fx.Root, "report.json"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains($"server returned {status} {reason}", r.Err);
        Assert.Contains(expectInStderr, r.Err);
        if (hint is not null) Assert.Contains(hint, r.Err);
        Assert.DoesNotContain("pushed:", r.Out);
        Assert.DoesNotContain("   at ", r.Err);
    }

    [Fact] // 不跟轉址：跟了的話 POST 變 GET、本文丟掉、Authorization 被拿掉 —— 回報 3xx 與 Location，token 只送到指名的伺服器
    public void RedirectIsReportedNotFollowed()
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        using var stub = new PushStub(307, null, "Temporary Redirect", "Location: https://dashboard.example.test/api/runs");
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url)), "--push", Path.Combine(fx.Root, "report.json"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Single(stub.Requests);
        Assert.Contains("server returned 307", r.Err);
        Assert.Contains("redirects to https://dashboard.example.test/api/runs", r.Err);
    }

    [Fact] // 連不上（埠沒人聽）→ exit 2 並說人話，不是 .NET 堆疊
    public void ConnectionRefusedExitsTwo()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var fx = new Fixture().File("report.json", MinimalReport(0));
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", $"http://127.0.0.1:{port}")), "--push", Path.Combine(fx.Root, "report.json"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains($"cannot reach http://127.0.0.1:{port}", r.Err);
        Assert.DoesNotContain("   at ", r.Err);
        Assert.DoesNotContain("Unhandled exception", r.Err);
    }

    [Theory] // token 只讀 XAMLCONTRAST_TOKEN；沒有（或只有空白）→ exit 2，指出去哪裡拿，**一個請求都不送**
    [InlineData(null, "missing token: set the XAMLCONTRAST_TOKEN environment variable")]
    [InlineData("", "missing token: set the XAMLCONTRAST_TOKEN environment variable")]
    [InlineData("   ", "missing token: set the XAMLCONTRAST_TOKEN environment variable")]
    [InlineData("two words", "XAMLCONTRAST_TOKEN contains spaces")]
    public void MissingTokenExitsTwoAndSendsNothing(string? token, string expectInStderr)
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url), ("XAMLCONTRAST_TOKEN", token)), "--push", Path.Combine(fx.Root, "report.json"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
        if (token is null) Assert.Contains("create-project", r.Err);
        Assert.Empty(stub.Requests);
    }

    [Fact] // 刻意沒有 --token：旗標值會留在 shell 歷史與 CI log 裡
    public void ThereIsNoTokenFlag()
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", "http://127.0.0.1:1")), "--push", Path.Combine(fx.Root, "report.json"), "--token", Token);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("unknown argument: --token", r.Err);
    }

    [Theory] // 伺服器：--server，否則 XAMLCONTRAST_SERVER；只收絕對的 http／https 網址（Linux 上 /srv 也是絕對 URI —— file:///srv）
    [InlineData(null, null, "missing server: pass --server <url>")]
    [InlineData("ftp://dashboard.example.test", null, "--server must be an absolute http or https URL, got 'ftp://dashboard.example.test'")]
    [InlineData("dashboard.example.test", null, "--server must be an absolute http or https URL")]
    [InlineData("/srv/dashboard", null, "--server must be an absolute http or https URL")]
    [InlineData(null, "file:///srv/dashboard", "XAMLCONTRAST_SERVER must be an absolute http or https URL")]
    public void ServerMustBeAnAbsoluteHttpUrl(string? flag, string? env, string expectInStderr)
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        string[] args = flag is null
            ? ["--push", Path.Combine(fx.Root, "report.json")]
            : ["--push", Path.Combine(fx.Root, "report.json"), "--server", flag];
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", env)), args);
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
    }

    [Theory] // 送出前在本機驗信封：不是這個工具的報告 → exit 2、不送。最可能拿錯的是 Parity 的 report.json（同一台伺服器）
    [InlineData("""{ "schemaVersion": 3, "reports": [] }""", "looks like a Parity report (it has a 'reports' array)")]
    [InlineData("[]", "is not an XamlContrast report")]
    [InlineData("not json", "is not valid JSON")]
    [InlineData("", "is not valid JSON")]
    [InlineData("""{ "summary": {}, "findings": [] }""", "has no schemaVersion")]
    [InlineData("""{ "schemaVersion": "{current}", "summary": {}, "findings": [] }""", "has no schemaVersion")]
    [InlineData("""{ "schemaVersion": {current} }""", "but no summary or findings")]
    [InlineData("""{ "schemaVersion": {current}, "summary": {}, "findings": {} }""", "but no summary or findings")]
    [InlineData("""{ "schemaVersion": {older}, "summary": {}, "findings": [] }""", "written by XamlContrast 0.x")]
    [InlineData("""{ "schemaVersion": {newer}, "summary": {}, "findings": [] }""", "written by a newer XamlContrast")]
    public void NotThisToolsReportExitsTwoAndSendsNothing(string content, string expectInStderr)
    {
        using var fx = new Fixture().File("report.json", content
            .Replace("{current}", $"{Report.JsonSchemaVersion}")
            .Replace("{older}", $"{Report.JsonSchemaVersion - 1}")
            .Replace("{newer}", $"{Report.JsonSchemaVersion + 1}"));
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url)), "--push", Path.Combine(fx.Root, "report.json"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
        Assert.Empty(stub.Requests);
        Assert.DoesNotContain("   at ", r.Err);
    }

    [Fact] // 報告檔不存在 → exit 2，指出怎麼產生；不送
    public void MissingReportExitsTwo()
    {
        using var fx = new Fixture();
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url)), "--push", Path.Combine(fx.Root, "nope.json"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("report not found: ", r.Err);
        Assert.Contains("--json <file>", r.Err);
        Assert.Empty(stub.Requests);
    }

    [Fact] // 編輯器另存時加的 BOM 拿掉再送（JSON 開頭的 U+FEFF 伺服器不收）；其餘一個位元組都不動
    public void ByteOrderMarkIsNotSent()
    {
        using var fx = new Fixture();
        var report = Path.Combine(fx.Root, "report.json");
        var json = Encoding.UTF8.GetBytes(MinimalReport(0));
        File.WriteAllBytes(report, [0xEF, 0xBB, 0xBF, .. json]);
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url)), "--push", report);
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Equal(json, Assert.Single(stub.Requests).Body);
    }

    [Theory] // push 只上傳、不稽核：任何稽核的輸入都是用法錯誤（不然 `--push r.json --fail-on fail` 看起來像套用了 gate）
    [InlineData(new[] { "{root}" }, "the project root")]
    [InlineData(new[] { "--", "{root}" }, "the project root")]
    [InlineData(new[] { "--" }, "drop --")]
    [InlineData(new[] { "--json", "{root}/out.json" }, "--json")]
    [InlineData(new[] { "--sarif", "{root}/out.sarif" }, "--sarif")]
    [InlineData(new[] { "--md", "{root}/out.md" }, "--md")]
    [InlineData(new[] { "--html", "{root}/out.html" }, "--html")]
    [InlineData(new[] { "--baseline", "{root}/b.json" }, "--baseline")]
    [InlineData(new[] { "--write-baseline", "{root}/b.json" }, "--write-baseline")]
    [InlineData(new[] { "--fail-on", "fail" }, "--fail-on")]
    [InlineData(new[] { "--min-coverage", "0" }, "--min-coverage")]
    [InlineData(new[] { "--strict-palette" }, "--strict-palette")]
    [InlineData(new[] { "--show-ok" }, "--show-ok")]
    [InlineData(new[] { "--show-unresolved" }, "--show-unresolved")]
    public void PushDoesNotAudit(string[] extra, string expectNamed)
    {
        using var fx = CliProjects.Failing().File("report.json", MinimalReport(1));
        using var stub = new PushStub(201, Created());
        var env = Env(("XAMLCONTRAST_SERVER", stub.Url));
        var report = Path.Combine(fx.Root, "report.json");
        foreach (var args in new[]
                 {
                     new[] { "--push", report }.Concat(extra.Select(a => a.Replace("{root}", fx.Root))).ToArray(),
                     extra.Select(a => a.Replace("{root}", fx.Root)).Where(a => a != "--").Concat(["--push", report]).ToArray(), // 順序無關
                 })
        {
            if (args.Length == 2) continue; // 只有 `--` 的那組，第二種排法沒有東西可排
            var r = Cli.RunEnv(env, args);
            Assert.True(r.Exit == 2, r.ToString());
            Assert.Contains("--push uploads an existing report and does not audit", r.Err);
            Assert.Contains(expectNamed, r.Err);
            Assert.Contains("run the audit with --json <file> first, then --push <file>", r.Err);
            Assert.DoesNotContain("pairs", r.Out); // 沒有稽核
        }
        Assert.Empty(stub.Requests);
        Assert.False(File.Exists(Path.Combine(fx.Root, "out.json")));
    }

    [Theory] // push 專用的旗標沒有 --push → exit 2（不是靜默忽略、也不是去稽核）；值必填、不吃掉下一個旗標
    [InlineData(new[] { "{root}", "--server", "http://127.0.0.1:1" }, "--server only works with --push")]
    [InlineData(new[] { "{root}", "--commit", "abc123" }, "--commit only works with --push")]
    [InlineData(new[] { "{root}", "--branch", "main" }, "--branch only works with --push")]
    [InlineData(new[] { "--server", "http://127.0.0.1:1" }, "--server only works with --push")]
    [InlineData(new[] { "--push" }, "--push takes a file path")]
    [InlineData(new[] { "--push", "--server", "http://127.0.0.1:1" }, "--push takes a file path, got '--server'")]
    [InlineData(new[] { "--push", "{root}/report.json", "--server" }, "--server takes a URL")]
    [InlineData(new[] { "--push", "{root}/report.json", "--commit", "" }, "--commit takes a commit SHA")]
    [InlineData(new[] { "--push", "{root}/report.json", "--branch", "--show-ok" }, "--branch takes a branch name, got '--show-ok'")]
    public void PushFlagsNeedPushAndAValue(string[] args, string expectInStderr)
    {
        using var fx = CliProjects.Failing().File("report.json", MinimalReport(1));
        var r = Cli.RunEnv(Env(), args.Select(a => a.Replace("{root}", fx.Root)).ToArray());
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains(expectInStderr, r.Err);
        Assert.DoesNotContain("pairs", r.Out);
    }

    [Theory] // GitHub Actions：job summary 加一行儀表板連結。圖示看伺服器記下的 gateFailed，沒有才退回報告的 exitCode；都不知道就不放
    [InlineData(true, 0, " ❌")]
    [InlineData(false, 1, " ✅")]
    [InlineData(null, 1, " ❌")]
    [InlineData(null, 3, " ❌")]
    [InlineData(null, 0, " ✅")]
    [InlineData(null, null, "")]
    public void JobSummaryGetsOneLine(bool? gateFailed, int? exitCode, string icon)
    {
        using var fx = new Fixture().File("report.json", MinimalReport(exitCode)).File("summary.md", "# earlier step\n");
        var summary = Path.Combine(fx.Root, "summary.md");
        using var stub = new PushStub(201, Created(gateFailed));
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url), ("GITHUB_STEP_SUMMARY", summary)),
            "--push", Path.Combine(fx.Root, "report.json"));
        Assert.True(r.Exit == 0, r.ToString());
        Assert.Equal($"# earlier step\n**XamlContrast** — [demo-app: 2 fail · 3 warn{icon}]({stub.Url}/runs/{RunId})\n",
            File.ReadAllText(summary, Encoding.UTF8));
    }

    [Fact] // 伺服器回的文字是不可信的資料：錯誤訊息或專案名裡的 \n::warning 不能落在行首變成 runner 會解析的 workflow command
    public void ServerTextCannotStartAWorkflowCommand()
    {
        using var fx = new Fixture().File("report.json", MinimalReport(1));
        var report = Path.Combine(fx.Root, "report.json");

        using (var bad = new PushStub(400, """{ "error": "bad report\n::warning file=README.md,line=1::INJECTED by the server" }""", "Bad Request"))
        {
            var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", bad.Url)), "--push", report);
            Assert.True(r.Exit == 2, r.ToString());
            Assert.DoesNotContain(Lines(r.Err), l => l.TrimStart().StartsWith("::"));
            Assert.Contains("bad report\\n::warning file=README.md,line=1::INJECTED by the server", r.Err); // 看得見，只是不在行首
        }

        var summary = Path.Combine(fx.Root, "summary.md");
        using (var ok = new PushStub(201, Created(project: "app\n::error::INJECTED project", url: "/runs/1\r\n::add-mask::x")))
        {
            var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", ok.Url), ("GITHUB_STEP_SUMMARY", summary)), "--push", report);
            Assert.True(r.Exit == 0, r.ToString());
            Assert.DoesNotContain(Lines(r.Out), l => l.TrimStart().StartsWith("::"));
            Assert.Contains("app\\n::error::INJECTED project", r.Out);
            var written = File.ReadAllText(summary, Encoding.UTF8);
            Assert.Single(written.Split('\n', StringSplitOptions.RemoveEmptyEntries)); // 一行，沒有被拆開
        }
    }

    [Fact] // 標頭只能帶可見的 ASCII：中文分支名不送那一欄（.NET 拒送、Kestrel 預設拒收），但要說出來，推送照常
    public void NonAsciiBranchIsLeftOutWithANote()
    {
        using var fx = new Fixture().File("report.json", MinimalReport(0));
        using var stub = new PushStub(201, Created());
        var r = Cli.RunEnv(Env(("XAMLCONTRAST_SERVER", stub.Url)), "--push", Path.Combine(fx.Root, "report.json"), "--branch", "功能/推送");
        Assert.True(r.Exit == 0, r.ToString());
        Assert.False(Assert.Single(stub.Requests).Headers.ContainsKey("X-Parity-Branch"));
        Assert.Contains("note: X-Parity-Branch not sent", r.Err);
    }

    [Fact] // --help 列出 push 的旗標並標成實驗性（凍結面之外）；token 只走環境變數也要寫出來
    public void HelpListsThePushFlagsAsExperimental()
    {
        var r = Cli.Run("--help");
        Assert.True(r.Exit == 0, r.ToString());
        foreach (var flag in new[] { "--push <path>", "--server <url>", "--commit <sha>", "--branch <name>" })
            Assert.Contains(Lines(r.Out), l => l.Contains(flag) && l.Contains("(experimental)"));
        Assert.Contains("xamlcontrast --push <report.json>", r.Out);
        Assert.Contains("XAMLCONTRAST_TOKEN", r.Out);
        Assert.Contains("XAMLCONTRAST_SERVER", r.Out);
        Assert.DoesNotContain("--token", r.Out);
    }
}

/// <summary>
/// 127.0.0.1 上的最小 HTTP/1.1 伺服器（隨機埠）：記下每一個請求、回固定的狀態碼與本文，回完就關連線。
/// 用 TcpListener 不用 HttpListener —— Windows 上 HttpListener 走 http.sys，非系統管理員要先保留 URL ACL 才綁得上。
/// 請求在回應之前就記下來，所以子行程結束時 <see cref="Requests"/> 一定已經有它。
/// </summary>
internal sealed class PushStub : IDisposable
{
    internal sealed record Request(string Method, string Target, Dictionary<string, string> Headers, byte[] Body);

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<Request> requests = new();
    private readonly int status;
    private readonly string? body;
    private readonly string reason;
    private readonly string[] extraHeaders;

    public PushStub(int status, string? body, string reason = "", params string[] extraHeaders)
    {
        (this.status, this.body, this.reason, this.extraHeaders) = (status, body, reason, extraHeaders);
        listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

    /// <summary>true：收下請求、記下來，然後一直不回（逾時測試用：統計的整體上限 3 秒）。</summary>
    public bool Hang { get; init; }

    public IReadOnlyList<Request> Requests => requests.ToArray();

    private async Task AcceptLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var received = new MemoryStream();
                var chunk = new byte[8192];
                int headerEnd;
                while ((headerEnd = IndexOfBlankLine(received)) < 0)
                {
                    var n = await stream.ReadAsync(chunk, stop.Token);
                    if (n == 0) return;
                    received.Write(chunk, 0, n);
                }

                var head = Encoding.ASCII.GetString(received.GetBuffer(), 0, headerEnd).Split("\r\n");
                var requestLine = head[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in head.Skip(1))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }
                var length = headers.TryGetValue("Content-Length", out var cl) ? int.Parse(cl) : 0;
                while (received.Length - (headerEnd + 4) < length)
                {
                    var n = await stream.ReadAsync(chunk, stop.Token);
                    if (n == 0) break;
                    received.Write(chunk, 0, n);
                }
                var requestBody = received.GetBuffer().AsSpan(headerEnd + 4, (int)Math.Min(length, received.Length - (headerEnd + 4))).ToArray();
                requests.Enqueue(new Request(requestLine[0], requestLine.Length > 1 ? requestLine[1] : "", headers, requestBody));
                if (Hang)
                {
                    await Task.Delay(Timeout.Infinite, stop.Token);
                    return;
                }

                var payload = body is null ? [] : Encoding.UTF8.GetBytes(body);
                var response = new StringBuilder($"HTTP/1.1 {status} {reason}\r\n");
                if (body is not null) response.Append("Content-Type: application/json; charset=utf-8\r\n");
                foreach (var h in extraHeaders) response.Append(h).Append("\r\n");
                response.Append($"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response.ToString()), stop.Token);
                await stream.WriteAsync(payload, stop.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { /* 對方斷線：記到的就記到了 */ }
        }
    }

    private static int IndexOfBlankLine(MemoryStream received)
        => received.GetBuffer().AsSpan(0, (int)received.Length).IndexOf("\r\n\r\n"u8);

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        stop.Dispose();
    }
}

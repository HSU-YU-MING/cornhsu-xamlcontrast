using System.Text.Json;
using System.Text.RegularExpressions;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// HTML 報告（--html，1.1）：單檔、不需要 JavaScript、不載入任何外部資源，從 CI 的 artifact 直接打開。
/// 這裡守兩件事。一是內容：每筆 fail／warn 畫出兩個主題下算比值的那一對顏色（與 JSON 的 colors 同一份），
/// 「沒評到的東西」的計數器一律印、是 0 也印。二是安全：報告裡的文字全部來自不可信的 XAML ——
/// 任何一段都不能變成標籤或屬性（腳本、事件處理器、外部資源），只能是看得見的文字。
/// </summary>
public class HtmlReportTests
{
    /// <summary>報告裡會出現的每一種標籤。掃描內容要是變成了標籤（&lt;script&gt;、&lt;img&gt;），這張表會擋下來。</summary>
    private static readonly HashSet<string> Tags =
    [
        "!DOCTYPE", "html", "head", "meta", "title", "style", "body", "div", "header", "main", "section", "article", "footer",
        "p", "h1", "h2", "h3", "span", "strong", "code", "pre", "details", "summary", "table", "thead", "tbody", "tr", "th", "td",
        "dl", "dt", "dd",
    ];

    /// <summary>報告用到的屬性；值都來自固定字彙（class、id）或驗過的 #RRGGBB（style）—— 掃描內容一律不進屬性。</summary>
    private static readonly HashSet<string> Attributes = ["lang", "charset", "name", "content", "http-equiv", "class", "style", "aria-hidden", "role", "id"];

    private static string HtmlOf(AuditResult r, int? exitCode = 1)
        => Report.ToHtml(r, new ReportContext { ToolVersion = "1.1.0", ExitCode = exitCode, FailOn = "warn", MinCoverage = 50, StrictPalette = false });

    /// <summary>結構上的保證：只有已知的標籤與屬性；style 只有色碼；沒有 script、沒有外部資源。</summary>
    private static void AssertOnlyOwnMarkup(string html)
    {
        foreach (Match m in Regex.Matches(html, @"<(?<close>/?)(?<tag>[!A-Za-z][A-Za-z0-9]*)"))
            Assert.True(Tags.Contains(m.Groups["tag"].Value), $"unexpected tag <{m.Groups["close"].Value}{m.Groups["tag"].Value}>");
        var style = html.IndexOf("</style>", StringComparison.Ordinal);
        foreach (Match tag in Regex.Matches(html[(style + 8)..], @"<[A-Za-z][A-Za-z0-9]*(?<attrs>[^>]*)>"))
            foreach (Match a in Regex.Matches(tag.Groups["attrs"].Value, @"\s(?<name>[A-Za-z-]+)(?:=""(?<value>[^""]*)"")?"))
            {
                var (name, value) = (a.Groups["name"].Value, a.Groups["value"].Value);
                Assert.True(Attributes.Contains(name) || name == "open", $"unexpected attribute {name}=\"{value}\"");
                if (name == "style") Assert.Matches("^(color:#[0-9A-F]{6};)?background:#[0-9A-F]{6}$", value);
                if (name is "class" or "id") Assert.Matches("^[a-z -]+$", value);
            }
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        // 外部資源：標籤與屬性的白名單已經擋掉 src／href／link；樣式表裡也不能有 url() 或 @import
        var css = html[html.IndexOf("<style>", StringComparison.Ordinal)..style];
        foreach (var external in new[] { "url(", "@import", "//" })
            Assert.DoesNotContain(external, css, StringComparison.OrdinalIgnoreCase);
        // 第二道：CSP 禁掉 script 與外部資源（跳脫哪天漏了一處也執行不了）
        Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'", html);
    }

    [Fact] // demo（真的 CLI）：三筆 fail 各一張卡，兩個主題各畫字色／底色的色塊與樣字，比值與 JSON 的 colors 是同一份；
           // 退化與「沒評到」的計數器是 0 也印；ok／decorative 收合
    public void DemoReportShowsEachFailingPairsColoursPerTheme()
    {
        using var output = new Fixture();
        var html = Path.Combine(output.Root, "report.html");
        var json = Path.Combine(output.Root, "report.json");
        var r = Cli.Run(Repo.PathOf("samples/demo"), "--html", html, "--json", json);
        Assert.True(r.Exit == 1, r.ToString());
        Assert.Contains($"html written: {html}", r.Out);
        var text = File.ReadAllText(html);
        AssertOnlyOwnMarkup(text);

        Assert.Contains("<span class=\"verdict v-fail\">FAIL</span> 3 fail · 0 warn", text);
        Assert.Contains("<h2>fail (3)</h2>", text);
        Assert.Equal(3, Regex.Matches(text, "<article class=\"finding fail\">").Count);
        Assert.Contains("<h2>warn (0)</h2>", text);
        // 寫死的白字：深色 16.67:1、淺色白配白 1:1 —— 色塊、樣字、比值、色碼都在
        Assert.Contains("<span class=\"sample\" style=\"color:#FFFFFF;background:#1E1E1E\">Aa</span><span class=\"ratio\">16.67:1</span>", text);
        Assert.Contains("<span class=\"sample\" style=\"color:#FFFFFF;background:#FFFFFF\">Aa</span><span class=\"ratio\">1:1</span>", text);
        Assert.Contains("<code>MainWindow.xaml:30</code> <code>TextBlock</code>", text);
        Assert.Contains("fg <code>White</code> → bg <code>{Surface}</code>", text);
        // 每一筆 fail 的每一個主題：HTML 畫的就是 JSON 的 colors（同一份資料，兩種呈現）
        foreach (var f in Cli.ReadJson(json).GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("category").GetString() == "fail"))
            foreach (var theme in new[] { "dark", "light" })
            {
                var c = f.GetProperty("colors").GetProperty(theme);
                Assert.Contains($"style=\"color:{c.GetProperty("fg").GetString()};background:{c.GetProperty("bg").GetString()}\"", text);
            }

        // 「沒評到的東西」一律列出，0 也列
        foreach (var (label, n) in new[]
                 {
                     ("unresolved (runtime colour, gradient, unknown key…)", 0), ("skipped (invisible: transparent or opacity 0)", 0),
                     ("excluded (dead Foreground setter)", 0), ("exempted (IsEnabled=False; WCAG 1.4.3)", 1),
                     ("suppressed via xamlcontrast-ignore", 1), ("parse errors", 0), ("invalid ignore comments", 0),
                 })
            Assert.Contains($"<dt>{label}</dt><dd>{n}</dd>", text);
        Assert.Contains("<h2>unresolved (0)</h2>", text);
        Assert.Contains("<details><summary>3 passing pairs</summary>", text);
        Assert.Contains("<details><summary>1 decorative elements — no text contrast minimum</summary>", text);
        Assert.Contains("<dt>mode</dt><dd>pair</dd>", text);
        Assert.Contains("<dt>fail on</dt><dd>warn</dd>", text);
    }

    [Fact] // 報告會從 CI 的 artifact 直接打開，而裡面的文字來自不可信的 XAML：Style 的 key（元素標籤）、Binding 的原始值
           // （unresolved 的值）、偵測描述、檔名。腳本標籤、跳出屬性的引號、事件處理器 —— 一律只能是看得見的文字
    public void ScannedTextNeverBecomesMarkup()
    {
        using var fx = new Fixture();
        fx.File("Views/Main.xaml", """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <UserControl.Resources>
                <Style x:Key="&lt;script&gt;alert(1)&lt;/script&gt;" TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#777777"/><Setter Property="Background" Value="#888888"/>
                </Style>
                <Style x:Key="&quot;&gt;&lt;img src=x onerror=alert(2)&gt;" TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#767676"/><Setter Property="Background" Value="#FFFFFF"/>
                </Style>
                <Style x:Key="report&#x202E;lmth.exe&#10;::error::INJECTED" TargetType="TextBlock">
                  <Setter Property="Foreground" Value="#EEEEEE"/><Setter Property="Background" Value="#FFFFFF"/>
                </Style>
              </UserControl.Resources>
              <StackPanel Background="#FFFFFF">
                <Border Background="{Binding &lt;script&gt;alert(3)&lt;/script&gt;}"><TextBlock Foreground="#000000" Text="unresolved"/></Border>
                <TextBlock Foreground="#111111" Text="ok"/>
              </StackPanel>
            </UserControl>
            """);
        // Windows 不允許檔名裡有 < > "，其他平台可以 —— 檔名一樣是報告裡的文字
        if (!OperatingSystem.IsWindows())
            fx.File("Views/\"><svg onload=alert(4)>.xaml", """<Grid Background="#FFFFFF"><TextBlock Foreground="#EEEEEE"/></Grid>""");
        var html = HtmlOf(fx.Run());

        AssertOnlyOwnMarkup(html);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Style[&lt;script&gt;alert(1)&lt;/script&gt;]/base", html);
        Assert.Contains("Style[&quot;&gt;&lt;img src=x onerror=alert(2)&gt;]/base", html);
        Assert.Contains("{Binding &lt;script&gt;alert(3)&lt;/script&gt;}", html); // unresolved 的值
        // 雙向覆寫與換行換成看得見的跳脫：檔名不會在畫面上倒著顯示，換行之後的字也不會被當成另一行
        Assert.Contains("report\\u202Elmth.exe\\n::error::INJECTED", html);
        Assert.DoesNotContain(((char)0x202E).ToString(), html, StringComparison.Ordinal); // Ordinal：文化比較會忽略格式字元、恆真
        if (!OperatingSystem.IsWindows()) Assert.Contains("Views/&quot;&gt;&lt;svg onload=alert(4)&gt;.xaml", html);
    }

    [Fact] // 只存在於一個主題的配對：另一個主題印 n/a，不畫那個主題的色塊（同 JSON：不印一個不存在的主題的比值或顏色）
    public void OneThemePairsShowNoColoursForTheOtherTheme()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/><SolidColorBrush x:Key="Fg" Color="#EEEEEE"/><SolidColorBrush x:Key="Line" Color="#333333"/>
              <Style x:Key="Chip" TargetType="Button"><Setter Property="Background" Value="{DynamicResource Bg}"/><Setter Property="Foreground" Value="#2A2A2A"/></Style>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/><SolidColorBrush x:Key="Fg" Color="#1B1B1B"/><SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        var r = fx.Run();
        var f = Assert.Single(r.Findings, f => f.Category == Category.Fail);
        Assert.Equal(ThemeSide.Dark, f.Theme);
        var html = HtmlOf(r);
        AssertOnlyOwnMarkup(html);
        var card = Regex.Match(html, "<article class=\"finding fail\">[\\s\\S]*?</article>").Value;
        Assert.Contains("<span class=\"tname\">Light</span><span class=\"hex\">n/a (dark theme only)</span>", card);
        Assert.Single(Regex.Matches(card, "class=\"sample\"")); // 只有深色那一行有樣字
        Assert.Contains("style=\"color:#2A2A2A;background:#111111\"", card);
    }

    [Fact] // 退出碼 3 的報告要說「這份結果不可信」與原因，不是 PASS 或 FAIL
    public void UntrustedResultSaysWhy()
    {
        using var fx = CliProjects.QuarterCoverage();
        var html = HtmlOf(fx.Run(), exitCode: 3);
        Assert.Contains("<span class=\"verdict v-untrusted\">NOT TRUSTED</span>", html);
        Assert.Contains("only 25.0% of colour pairs could be resolved, below the 50% floor", html);
        Assert.Contains("<h2>unresolved (3)</h2>", html);
        Assert.Equal(3, Regex.Matches(html, "<td><code>bound-or-gradient</code></td>").Count);
        Assert.Contains("{Binding A}", html);
    }

    [Fact] // 函式庫呼叫端：沒有 context（沒有判定）、自己建的 finding 沒有顏色 —— 照樣產得出報告：不印判定、不畫色塊，比值照印
    public void LibraryCallWithoutContextOrColoursStillRenders()
    {
        using var fx = CliProjects.Failing();
        var r = fx.Run();
        var orig = Assert.Single(r.Findings);
        r.Findings[0] = new Finding
        {
            File = orig.File,
            Line = orig.Line,
            Element = orig.Element,
            Fg = orig.Fg,
            Bg = orig.Bg,
            RatioDark = orig.RatioDark,
            RatioLight = orig.RatioLight,
            IsText = true,
            Need = orig.Need,
            Category = orig.Category,
            Symmetry = orig.Symmetry,
        };
        var html = Report.ToHtml(r);
        AssertOnlyOwnMarkup(html);
        Assert.DoesNotContain("class=\"verdict", html);
        Assert.DoesNotContain("class=\"sw\"", html);
        Assert.Contains($"<span class=\"ratio\">{orig.RatioDark.ToString(System.Globalization.CultureInfo.InvariantCulture)}:1</span>", html);
    }

    [Fact] // 寫不出去（目錄不存在）落在退出碼契約內：2 並說人話，同 --json／--md
    public void UnwritableHtmlPathExitsTwo()
    {
        using var fx = CliProjects.Passing();
        var r = Cli.Run(fx.Root, "--html", Path.Combine(fx.Root, "no-such-dir", "report.html"));
        Assert.True(r.Exit == 2, r.ToString());
        Assert.Contains("cannot write html to ", r.Err);
        Assert.DoesNotContain("   at ", r.Err);
    }

    [Fact] // --html 的值必填、不能是下一個旗標（同 --json）
    public void HtmlFlagNeedsAPath()
    {
        using var fx = CliProjects.Passing();
        var missing = Cli.Run(fx.Root, "--html");
        Assert.True(missing.Exit == 2, missing.ToString());
        Assert.Contains("--html takes a file path", missing.Err);
        var swallowed = Cli.Run(fx.Root, "--html", "--strict-palette");
        Assert.True(swallowed.Exit == 2, swallowed.ToString());
        Assert.Contains("--html takes a file path, got '--strict-palette'", swallowed.Err);
    }

    [Fact] // 摘要的數字與 JSON 的 summary 一致（同一份稽核結果的兩種呈現不能各說各話）
    public void SummaryMatchesTheJsonSummary()
    {
        using var fx = CliProjects.QuarterCoverage();
        var r = fx.Run();
        var s = JsonDocument.Parse(Report.ToJson(r)).RootElement.GetProperty("summary");
        var html = HtmlOf(r, exitCode: 3);
        Assert.Contains($"<dt>text-on-background pairs</dt><dd>{s.GetProperty("pairs").GetInt32()}</dd>", html);
        Assert.Contains($"<dt>files</dt><dd>{s.GetProperty("files").GetInt32()}</dd>", html);
        Assert.Contains($"<dt>unresolved (runtime colour, gradient, unknown key…)</dt><dd>{s.GetProperty("unresolved").GetInt32()}</dd>", html);
        Assert.Contains("unresolved by reason: bound-or-gradient 3", html);
    }
}

using System.Text;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 報告會貼成 PR 留言(Action 的 comment 預設打開),而警示裡的字串不是工具寫的:檔案路徑、XML 解析器的訊息、設定檔的名稱都來自 repo。
/// 它們不能在留言裡變成連結、圖片或 @ 提及。GitHub 只在 code span 裡不處理這些(反斜線擋不住提及,也擋不住 www. 開頭的自動連結),
/// 所以一律要留在 code span 內。表格的欄位早就是這樣(審查 1.0),這裡補的是表格外的四個警示。
/// 修的時候實際交給 GitHub 渲染比對過;這裡照 CommonMark 的規則把 code span 拿掉,看剩下的文字,不必連網。
/// </summary>
public class MarkdownInjectionTests
{
    /// <summary>這些檔名在三個作業系統上都合法:真的走一次稽核,不是手寫 AuditResult。</summary>
    public static TheoryData<string> HostileFileNames =>
    [
        "[click](evil.html).xaml",
        "![pixel](evil.png).xaml",
        "@someone x.xaml",          // 空格隔開才算提及
        "www.evil.example x.xaml",  // 空格隔開才會自動連結
        "a` [click](evil.html) `b.xaml", // 反引號:包一層就跳得出來
    ];

    /// <summary>設定檔擋含冒號的名稱,但 // 開頭的網址不需要冒號(GitHub 實測渲染成外部連結)。</summary>
    public static TheoryData<string> HostileLayoutNames =>
    [
        "[click](//evil.example/a)",
        "![pixel](//evil.example/p.png)",
        "@someone",
        "www.evil.example",
        "a` [click](//evil.example/b) `c",
    ];

    /// <summary>照 CommonMark 拿掉 code span:開頭的反引號串要配到一樣長的,配不到就只是一般文字。</summary>
    private static string OutsideCodeSpans(string markdown)
    {
        var outside = new StringBuilder();
        foreach (var line in markdown.Split('\n'))
        {
            for (var i = 0; i < line.Length;)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    outside.Append(line, i, 2); // code span 外的反斜線才是跳脫
                    i += 2;
                }
                else if (line[i] != '`')
                {
                    outside.Append(line[i++]);
                }
                else
                {
                    var length = RunAt(line, i);
                    var close = FindRun(line, i + length, length);
                    if (close < 0)
                    {
                        outside.Append(line, i, length);
                        i += length;
                    }
                    else
                    {
                        i = close + length;
                    }
                }
            }
            outside.Append('\n');
        }
        return outside.ToString();
    }

    private static int RunAt(string line, int start)
    {
        var end = start;
        while (end < line.Length && line[end] == '`') end++;
        return end - start;
    }

    private static int FindRun(string line, int from, int length)
    {
        for (var i = from; i < line.Length;)
        {
            if (line[i] != '`')
            {
                i++;
                continue;
            }
            var run = RunAt(line, i);
            if (run == length) return i;
            i += run;
        }
        return -1;
    }

    private static void AssertNothingEscaped(string markdown)
    {
        var outside = OutsideCodeSpans(markdown);
        Assert.DoesNotContain("evil", outside);
        Assert.DoesNotContain("@someone", outside);
        Assert.DoesNotContain("![", outside);
    }

    [Fact]
    public void The_check_itself_sees_what_escaped_a_code_span()
    {
        // 這條檢查失效的話,下面全部都會靜靜地綠
        Assert.Contains("evil", OutsideCodeSpans("[x](evil.html)"));
        Assert.Contains("evil", OutsideCodeSpans("`a` [x](evil.html) `b`")); // 單反引號包住帶反引號的名字
        Assert.DoesNotContain("evil", OutsideCodeSpans("`` a` [x](evil.html) `b ``"));
        Assert.Contains("evil", OutsideCodeSpans("` [x](evil.html)")); // 沒有配對的反引號不算 code span
    }

    [Theory]
    [MemberData(nameof(HostileFileNames))]
    public void A_file_name_in_a_parse_failure_cannot_write_markdown(string name)
    {
        using var fx = new Fixture();
        fx.File(name, "<a><b></a>");

        var md = Report.ToMarkdown(fx.Run(), 1);

        Assert.Contains("**Parse failed:**", md);
        Assert.Contains(name, md); // 還是要點名是哪個檔,只是不能變成語法
        AssertNothingEscaped(md);
    }

    [Theory]
    [MemberData(nameof(HostileFileNames))]
    public void A_file_name_in_an_invalid_ignore_cannot_write_markdown(string name)
    {
        using var fx = new Fixture();
        fx.File(name, """
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore -->
              <TextBlock Foreground="#111111" Text="x"/>
            </Grid>
            """);

        var md = Report.ToMarkdown(fx.Run(), 1);

        Assert.Contains("without a reason is invalid", md);
        Assert.Contains(name, md);
        AssertNothingEscaped(md);
    }

    [Theory]
    [MemberData(nameof(HostileLayoutNames))]
    public void A_layout_name_in_the_config_cannot_write_markdown(string name)
    {
        using var fx = new Fixture();
        var json = System.Text.Json.JsonSerializer.Serialize(new { layout = new { overlap = new[] { name } } });
        fx.File(ToolConfig.FileName, json);
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBlock Foreground="#111111" Text="x"/></Grid>""");
        var cfg = ToolConfig.Load(fx.Root);

        var md = Report.ToMarkdown(Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg), 0);

        Assert.Contains("matched no element", md);
        Assert.Contains(name, md);
        AssertNothingEscaped(md);
    }

    [Fact]
    public void A_skipped_palette_path_in_the_degraded_warning_cannot_write_markdown()
    {
        // 色盤偵測退化的描述會列出被跳過的候選檔路徑,不是只有工具自己寫的句子
        using var fx = new Fixture();
        fx.File("tests/[click](evil.html).xaml", """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Accent">#336699</Color>
            </ResourceDictionary>
            """);
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBlock Foreground="#111111" Text="x"/></Grid>""");

        var result = fx.Run();
        var md = Report.ToMarkdown(result, 0);

        Assert.True(result.Detection.IsDegraded);
        Assert.Contains("tests/[click](evil.html).xaml", result.Detection.Description); // 確認真的走到會帶路徑的那條
        AssertNothingEscaped(md);
    }

    [Fact]
    public void Outside_a_table_a_pipe_is_not_escaped_because_a_code_span_would_print_the_backslash()
    {
        // 表格裡要跳脫 |(GFM),表格外的 code span 不處理反斜線,跳脫了就會印出來
        using var fx = new Fixture();
        fx.File(ToolConfig.FileName, """{ "layout": { "overlap": ["A|B"] } }""");
        fx.File("Main.xaml", """<Grid Background="#FFFFFF"><TextBlock Foreground="#111111" Text="x"/></Grid>""");
        var cfg = ToolConfig.Load(fx.Root);

        var md = Report.ToMarkdown(Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg), 0);

        Assert.Contains("layout name `A|B` matched no element", md);
    }
}

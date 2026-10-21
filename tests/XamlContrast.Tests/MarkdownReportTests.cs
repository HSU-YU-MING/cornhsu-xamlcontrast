using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// Markdown 報告(--md,給 PR 留言用)。
///
/// 這裡守的是本工具的憲法在 Markdown 這條輸出線上也成立:**放過的東西一律要現形**。
/// 行內 ::error 註記只標出問題那一行,PR 上沒有總覽;如果 Markdown 只列 findings、
/// 不列退化計數,reviewer 會以為「表上三筆就是全部」—— 那正是謊報健康。
/// </summary>
public class MarkdownReportTests
{
    [Fact] // 表頭要標明結論與規模,落差逐條列出
    public void RendersVerdictAndFindings()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#DDDDDD" Text="太淡"/>
            </Grid>
            """);
        var r = fx.Run();
        var md = Report.ToMarkdown(r, exitCode: 1);

        Assert.Contains("## XamlContrast — WCAG contrast audit", md);
        Assert.Contains("❌ **FAIL**", md);
        Assert.Contains("**1 fail**", md);
        Assert.Contains("#DDDDDD", md);
        Assert.Contains("| Location | Element |", md);   // 有表格,不是一坨純文字
    }

    [Fact] // exitCode 0 要顯示 PASS —— 報告的結論必須與退出碼一致,不能各說各話
    public void PassVerdictFollowsExitCode()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#111111" Text="夠深"/>
            </Grid>
            """);
        var r = fx.Run();
        var md = Report.ToMarkdown(r, exitCode: 0);

        Assert.Contains("✅ **PASS**", md);
        Assert.DoesNotContain("❌", md);
    }

    [Fact] // 豁免與壓掉必須出現在報告上 —— 這是「不謊報健康」的核心
    public void NotEvaluatedCountsAreVisible()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore: 浮水印,刻意低對比 -->
              <TextBlock Foreground="#EEEEEE" Text="浮水印"/>
              <TextBlock Foreground="#DDDDDD" Text="這個要報"/>
            </Grid>
            """);
        var r = fx.Run();
        var md = Report.ToMarkdown(r, exitCode: 1);

        Assert.Contains("**Not evaluated:**", md);
        Assert.Contains("suppressed", md);
    }

    [Fact] // 色盤偵測退化時,要在表格之前大聲說 —— 否則下面的數字會被當成完整結果
    public void DegradedPaletteIsAnnouncedBeforeTheTable()
    {
        using var fx = new Fixture();
        // 沒有任何主題字典 → 偵測退化,只算得到寫死色碼
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#DDDDDD" Text="太淡"/>
            </Grid>
            """);
        var r = fx.Run();
        // 前提要斷言而不是「不成立就默默讓路」—— 偵測策略一變，這條會靜默通過、什麼都沒測
        Assert.True(r.Detection.IsDegraded, "前提：沒有主題字典時偵測是退化的");

        var md = Report.ToMarkdown(r, exitCode: 1);
        Assert.Contains("Palette detection degraded", md);
        Assert.True(md.IndexOf("Palette detection degraded", StringComparison.Ordinal)
                    < md.IndexOf("| Location", StringComparison.Ordinal),
            "退化警告必須排在落差表之前");
    }

    [Fact] // 對照組：偵測到色盤就不能掛退化警告 —— 狼來了的警告跟沒有警告一樣沒用
    public void HealthyPaletteIsNotAnnouncedAsDegraded()
    {
        using var fx = new Fixture().WithPalette().File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Dim}" Text="太淡"/>
            </Grid>
            """);
        var md = Report.ToMarkdown(fx.Run(), exitCode: 1);
        Assert.Contains("| Location", md);
        Assert.DoesNotContain("degraded", md);
    }

    [Fact] // 大字級的門檻是 3.0 —— 兩份人看的報告都要標明「這筆是用大字級標準評的」，
           // 否則讀者看到 2.61:1 列在 warn 而不是 fail，會以為工具算錯
    public void LargeTextIsMarkedInBothHumanReports()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#A0A0A0" FontSize="24" Text="24px = 18pt 大字級，2.61:1"/>
              <TextBlock Foreground="#EEEEEE" FontSize="12" Text="一般字級"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.CountOf(Category.Warn));
        Assert.Equal(1, r.CountOf(Category.Fail));

        var console = Report.ToConsole(r);
        Assert.Contains("need 3 large-text 24px", console);
        Assert.Contains("need 4.5 12px", console);
        Assert.DoesNotContain("large-text 12px", console);

        var md = Report.ToMarkdown(r, exitCode: 1);
        Assert.Contains("| 2.61:1 | 2.61:1 | 3 (large) |", md);
        Assert.Contains("| 1.16:1 | 1.16:1 | 4.5 |", md);
    }

    [Fact] // 0 配對:空掃不是通過,Markdown 也要說清楚
    public void ZeroPairsIsCalledOut()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", "<Grid/>");
        var r = fx.Run();
        Assert.Equal(0, r.Pairs); // 前提斷言，不是不成立就默默讓路

        var md = Report.ToMarkdown(r, exitCode: 1);
        Assert.Contains("Zero pairs resolved", md);
    }

    [Fact] // 表格跳脫:元素名或色值含 | 不能把表格弄壞
    public void PipeCharactersAreEscaped()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#DDDDDD" Text="太淡"/>
            </Grid>
            """);
        var r = fx.Run();
        var orig = r.Findings[0];
        // Finding 的欄位是 init-only,換掉整筆而不是改欄位
        r.Findings[0] = new Finding
        {
            File = orig.File,
            Line = orig.Line,
            Element = "Weird|Name",   // 含管線的元素名
            Fg = orig.Fg,
            Bg = orig.Bg,
            RatioDark = orig.RatioDark,
            RatioLight = orig.RatioLight,
            IsText = orig.IsText,
            Need = orig.Need,
            Size = orig.Size,
            Large = orig.Large,
            Category = orig.Category,
            Symmetry = orig.Symmetry,
        };

        var md = Report.ToMarkdown(r, exitCode: 1);
        Assert.Contains(@"Weird\|Name", md);
        // 跳脫後每一列的欄位數不變(7 欄 → 8 個分隔線)
        var row = md.Split('\n').First(l => l.Contains("Weird"));
        Assert.Equal(8, row.Count(c => c == '|') - 1);   // 被跳脫的那個不算欄位分隔
    }
}

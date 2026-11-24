using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>M4 導入模式（規劃書 4.6）：ignore 註解與 baseline ratchet。</summary>
public class IgnoreAndBaselineTests
{
    private const string LowContrastText = """<TextBlock Foreground="#EEEEEE" Text="浮水印"/>""";

    [Fact] // ignore 管下一個元素（含子樹），且壓掉要計數 —— 靜默退化等於謊報
    public void IgnoreSuppressesNextElementAndCounts()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore: 浮水印文字，刻意低對比 -->
              {LowContrastText}
              <TextBlock Foreground="#DDDDDD" Text="這個沒被 ignore，要報"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Suppressed);                    // 壓掉的有計數
        var f = Assert.Single(r.Findings);                // 只壓「下一個元素」，不是整個檔
        Assert.Equal("#DDDDDD", f.Fg);
        Assert.Equal(Category.Fail, f.Category);
        Assert.Contains("suppressed 1 pair(s)", Report.ToConsole(r));
        Assert.Contains("\"suppressed\": 1", Report.ToJson(r));
    }

    [Fact] // 「下一個元素連同整個子樹」也包含子樹裡的 Style 定義（盤點 10.3 第 2 點，1.0 前要定）。0.x 只看緊鄰 <Style> 的註解：
           // 容器上的 ignore 壓得到容器內「元素 × Style 鏈」的配對，卻壓不到容器 Resources 裡 Style 定義處的配對 ——
           // 同一個註解、同一個子樹，一半壓一半不壓
    public void ContainerIgnoreCoversStyleDefinitionsInsideIt()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              <!-- xamlcontrast-ignore: legacy panel, redesign tracked elsewhere -->
              <Grid Background="#FFFFFF">
                <Grid.Resources>
                  <Style x:Key="Faint" TargetType="Button">
                    <Setter Property="Background" Value="#FFFFFF"/>
                    <Setter Property="Foreground" Value="#EEEEEE"/>
                  </Style>
                </Grid.Resources>
                <TextBlock Foreground="#EEEEEE" Text="also inside"/>
              </Grid>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Empty(r.Findings);
        Assert.Equal(2, r.Suppressed); // Style 定義處那一組＋TextBlock
        Assert.Empty(r.InvalidIgnores);
    }

    [Fact] // 理由必填 —— 沒理由的 ignore 無效、不壓、要警告
    public void IgnoreWithoutReasonIsInvalidAndDoesNotSuppress()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore -->
              {LowContrastText}
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Suppressed);
        Assert.Single(r.Findings);                        // 照報
        Assert.Single(r.InvalidIgnores);
        Assert.Contains("without a reason", Report.ToConsole(r));
    }

    [Theory] // 寫錯的變體：舊版整段認不出來 —— 不壓（方向安全），但也不警告，使用者看不出自己的註解沒生效。
             // 現在一律不壓、記進 invalidIgnores 並說明正確寫法。多行理由維持不接受（接受等於放寬、會讓
             // 既有註解開始壓掉配對），只是不再沉默
    [InlineData("xamlcontrast-ignore:")]                                    // 冒號後沒有理由
    [InlineData("xamlcontrast-ignore:   ")]
    [InlineData("xamlcontrast-ignore: watermark text,\n  intentionally faint")] // 理由換到第二行
    [InlineData("XamlContrast-Ignore: watermark")]                          // 大小寫不同
    [InlineData("xamlcontrast-ignore 浮水印")]                               // 漏了冒號
    public void MalformedIgnoreCommentIsWarnedAboutAndDoesNotSuppress(string comment)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <!-- {comment} -->
              {LowContrastText}
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(0, r.Suppressed);
        Assert.Single(r.Findings);                        // 照報
        var w = Assert.Single(r.InvalidIgnores);
        Assert.StartsWith("Main.xaml:2: ", w);
        Assert.Contains("malformed xamlcontrast-ignore", w);
        Assert.Contains("not suppressed", w);
    }

    [Theory] // 對照組：不是 ignore 標記的註解不能誤報（標記要在註解開頭、而且是整個字）
    [InlineData("TODO: xamlcontrast-ignore: later")]
    [InlineData("xamlcontrast-ignored by design")]
    [InlineData("an ordinary comment")]
    public void OrdinaryCommentsAreNotTreatedAsMalformedIgnores(string comment)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <!-- {comment} -->
              {LowContrastText}
            </Grid>
            """);
        Assert.Empty(fx.Run().InvalidIgnores);
    }

    [Fact] // ignore 也能壓 Style 配對
    public void IgnoreSuppressesStylePair()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid.Resources>
                <!-- xamlcontrast-ignore: 停用態樣式，刻意低對比 -->
                <Style x:Key="Disabled" TargetType="Button">
                  <Setter Property="Background" Value="#303030"/>
                  <Setter Property="Foreground" Value="#404040"/>
                </Style>
              </Grid.Resources>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Suppressed);
        Assert.Empty(r.Findings);
    }

    [Fact] // 「下一個元素」含整個子樹：容器上的 ignore 壓掉裡面每一組，每一組都計數；容器之後的兄弟不受影響
    public void IgnoreCoversTheWholeSubtreeAndCountsEachPair()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore: 浮水印區塊，刻意低對比 -->
              <Border>
                <TextBlock Foreground="#EEEEEE" Text="直接子元素"/>
                <StackPanel><TextBlock Foreground="#DDDDDD" Text="孫元素"/></StackPanel>
              </Border>
              <TextBlock Foreground="#CCCCCC" Text="區塊之後的兄弟，要報"/>
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(3, r.Pairs);
        Assert.Equal(2, r.Suppressed);
        Assert.Equal("#CCCCCC", Assert.Single(r.Findings).Fg);
    }

    [Theory] // 夾在中間的一般註解會跳過（1.9 起），元素與文字內容不會；提到關鍵字的註解不是標記
    [InlineData("<!-- 一般註解 -->", false)]
    [InlineData("<!-- TODO: xamlcontrast-ignore: 之後再說 -->", false)] // 錨定在註解開頭，不是子字串
    [InlineData("<!-- xamlcontrast-ignore: 浮水印 -->\n<!-- 另一個註解 -->", true)] // 1.8 以前是 false：只看最靠近元素的那一個
    [InlineData("<!-- xamlcontrast-ignore: 浮水印 -->\n<Border/>", false)]  // 被上面的 Border 用掉了
    [InlineData("<!-- xamlcontrast-ignore:浮水印 -->", true)]              // 冒號後不必空白
    [InlineData("<!--   xamlcontrast-ignore :  浮水印  -->", true)]        // 前後與冒號兩側的空白都容忍
    public void OnlyAnIgnoreCommentBeforeTheElementSuppresses(string before, bool suppressed)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              {before}
              {LowContrastText}
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(suppressed ? 1 : 0, r.Suppressed);
        Assert.Equal(suppressed ? 0 : 1, r.Findings.Count);
        Assert.Empty(r.InvalidIgnores); // 這幾種都不是「沒附理由的 ignore」
    }

    // ── 疊在同一個元素上的註解（1.9）：跳過不相關的往前找，中間隔了元素才停 ──
    // 四件套的 ignore 會疊在同一個元素上。1.8.3 只看最靠近元素的那一個，xamlcontrast-ignore 不放最後就失效（suppressed 0、exit 1）

    private const string Mine = "<!-- xamlcontrast-ignore: c -->";
    private const string A11y = "<!-- xamla11y-ignore: a -->";
    private const string Fit = "<!-- xamlfit-ignore: f -->";
    private const string Keys = "<!-- xamlkeys-ignore: k -->";
    private const string Note = "<!-- 一般說明：這一行是浮水印 -->";

    private static AuditResult RunWithBefore(string before, string element = LowContrastText)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#FFFFFF">
              {before}
              {element}
            </Grid>
            """);
        return fx.Run();
    }

    public static TheoryData<string> StackedOrders()
    {
        var data = new TheoryData<string>();
        // 兩支、三支、四支、夾一般說明：每一種順序都要有效
        foreach (var set in new[]
                 {
                     new[] { Mine, A11y }, new[] { Mine, Fit }, new[] { Mine, Keys }, new[] { Mine, Note },
                     new[] { Mine, A11y, Fit }, new[] { Mine, A11y, Note }, new[] { Mine, Note, Note },
                     new[] { Mine, A11y, Fit, Keys }, new[] { Mine, A11y, Fit, Note },
                 })
            foreach (var order in Permutations(set).Select(p => string.Join("\n  ", p)).Distinct())
                data.Add(order);
        data.Add(Mine + A11y); // 寫在同一行、中間沒有空白
        data.Add(Mine + "<!-- -->");
        data.Add(Mine + "<!-- xamlcontrast-ignored by design -->");             // 不是整個字：不是標記
        data.Add(Mine + "<!-- TODO: remove the xamlcontrast-ignore above -->"); // 在談標記，不是在用它
        data.Add(Mine + "<!-- xamlcontrast-ignore: 比較近的那個 -->");
        data.Add(Mine + "<?note 處理指令不是內容?>");
        data.Add(Mine + A11y + "<?note x?>" + Note);
        return data;
    }

    private static IEnumerable<string[]> Permutations(string[] items)
        => items.Length <= 1
            ? [items]
            : items.SelectMany((x, i) => Permutations([.. items.Take(i), .. items.Skip(i + 1)]).Select(rest => (string[])[x, .. rest]));

    [Theory]
    [MemberData(nameof(StackedOrders))]
    public void StackedCommentsDoNotDisableTheIgnoreInAnyOrder(string before)
    {
        var r = RunWithBefore(before);
        Assert.Equal(1, r.Suppressed);
        Assert.Empty(r.Findings);
        Assert.Empty(r.InvalidIgnores);
    }

    [Theory] // Style 定義處與容器（祖先查詢）走同一個函式，也要跳得過
    [InlineData(Mine + A11y + Fit)]
    [InlineData(Mine + Note + A11y)]
    [InlineData(A11y + Mine + Note)]
    public void StackedIgnoreCoversStylesAndSubtrees(string before)
    {
        const string style = """
            <Style x:Key="Faint" TargetType="Button">
              <Setter Property="Background" Value="#FFFFFF"/>
              <Setter Property="Foreground" Value="#EEEEEE"/>
            </Style>
            """;
        var own = RunWithBefore("", $"<Grid.Resources>{before}{style}</Grid.Resources>");
        Assert.Equal(1, own.Suppressed);
        Assert.Empty(own.Findings);

        var container = RunWithBefore(before,
            $"""<Grid Background="#FFFFFF"><Grid.Resources>{style}</Grid.Resources>{LowContrastText}</Grid>""");
        Assert.Equal(2, container.Suppressed);
        Assert.Empty(container.Findings);
    }

    [Theory] // 一個註解還是只管下一個元素：隔了元素、屬性元素、文字內容就停，後面疊再多註解也一樣
    [InlineData(Mine + "<Border/>" + A11y)]
    [InlineData(Mine + "<Border/>" + Note + Fit)]
    [InlineData(Mine + A11y + "<Grid.RowDefinitions/>" + Note)]
    [InlineData(A11y + Fit + Note)]
    public void AnIgnoreDoesNotReachPastAnElement(string before)
    {
        var r = RunWithBefore(before);
        Assert.Equal(0, r.Suppressed);
        Assert.Contains(r.Findings, f => f.Fg == "#EEEEEE");
        Assert.Empty(r.InvalidIgnores);
    }

    [Theory] // 被註解掉的元素也是隔了元素：ignore 原本管的是它，不能因為它被註解掉就改壓下一個。
             // 停下要講出來 —— 不講的話，使用者只看到問題重新報出來，要自己想到是中間那段註解的關係
    [InlineData("xamlcontrast-ignore: c", "<!-- <TextBlock Text='草稿'/> -->")]
    [InlineData("xamlcontrast-ignore: c", "<!--\n  <TextBlock Text='草稿'/>\n-->" + A11y)]
    [InlineData("xamlcontrast-ignore: c", Note + "<!-- <A/> --><!-- <B/> -->" + Fit)]
    [InlineData("XamlContrast-Ignore: c", "<!-- <TextBlock Text='草稿'/> -->")] // 自己也寫錯：照樣講一次，不講兩次
    [InlineData("xamlcontrast-ignore: c", "文字內容")]                           // 文字也是內容
    [InlineData("xamlcontrast-ignore: c", A11y + "文字內容" + Note)]
    [InlineData("xamlcontrast-ignore: c", "<![CDATA[文字內容]]>" + Note)]
    public void AnIgnoreStopsAtACommentedOutElementOrTextAndSaysSo(string marker, string between)
    {
        var r = RunWithBefore($"<!-- {marker} -->\n{between}");
        Assert.Equal(0, r.Suppressed);
        Assert.Single(r.Findings);
        var w = Assert.Single(r.InvalidIgnores);
        Assert.StartsWith("Main.xaml:2: ", w, StringComparison.Ordinal);
        Assert.Contains("commented-out element or text", w, StringComparison.Ordinal);
        Assert.Contains("not suppressed", w, StringComparison.Ordinal);
    }

    [Theory] // 對照組：有自己的標記在後面、那個元素還在、或根本沒有標記，就沒有東西要講
    [InlineData(Mine + "<!-- <TextBlock Text='草稿'/> -->" + Mine, 1)]
    [InlineData("<!-- <TextBlock Text='草稿'/> -->" + Mine, 1)]
    [InlineData(Mine + "<Border/><!-- <TextBlock Text='草稿'/> -->", 0)]
    [InlineData(A11y + "<!-- <TextBlock Text='草稿'/> -->", 0)]
    [InlineData(A11y + "文字內容", 0)]
    [InlineData("文字內容" + Mine, 1)]
    public void ACommentedOutElementAloneIsNotWarnedAbout(string before, int suppressed)
    {
        var r = RunWithBefore(before);
        Assert.Equal(suppressed, r.Suppressed);
        Assert.Empty(r.InvalidIgnores);
    }

    [Theory] // 寫錯的標記不會因為後面疊了別的註解就安靜消失：照樣不壓、照樣警告，行號是那個註解自己的
    [InlineData("xamlcontrast-ignore", "without a reason")]
    [InlineData("xamlcontrast-ignore:", "malformed")]
    [InlineData("XamlContrast-Ignore: watermark", "malformed")]
    [InlineData("xamlcontrast-ignore 浮水印", "malformed")]
    [InlineData("xamlcontrast-ignore: watermark text,\n  intentionally faint", "malformed")]
    public void AnInvalidIgnoreBehindOtherCommentsIsStillWarnedAbout(string comment, string message)
    {
        foreach (var after in new[] { A11y, Note, Note + "\n" + Fit + "\n" + A11y })
        {
            var r = RunWithBefore($"<!-- {comment} -->\n{after}");
            Assert.Equal(0, r.Suppressed);
            Assert.Single(r.Findings);
            var w = Assert.Single(r.InvalidIgnores);
            Assert.StartsWith("Main.xaml:2: ", w, StringComparison.Ordinal);
            Assert.Contains(message, w, StringComparison.Ordinal);
            Assert.Contains("not suppressed", w, StringComparison.Ordinal);
        }
    }

    [Theory] // 自己的標記有兩個時，最靠近元素的那個說了算 —— 跟 1.8 一樣。寫錯的不當成「不相關」跳過去：
             // 跳過去的話，警告說 not suppressed、實際上卻被前面那個壓掉了
    [InlineData("<!-- xamlcontrast-ignore -->")]
    [InlineData("<!-- XamlContrast-Ignore: watermark -->")]
    public void TheNearestOwnMarkerDecides(string invalid)
    {
        var nearestInvalid = RunWithBefore(Mine + invalid + A11y);
        Assert.Equal(0, nearestInvalid.Suppressed);
        Assert.Single(nearestInvalid.Findings);
        Assert.Single(nearestInvalid.InvalidIgnores);

        // 原本有效的不能失效：有效的在最近，前面疊了寫錯的 —— 照壓
        var nearestValid = RunWithBefore(invalid + Mine);
        Assert.Equal(1, nearestValid.Suppressed);
        Assert.Empty(nearestValid.Findings);
    }

    [Fact] // requireReason 關掉時沒寫理由的也是有效標記，疊放一樣跳得過
    public void StackedBareIgnoreWorksWhenAReasonIsNotRequired()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              <!-- xamlcontrast-ignore -->
              {A11y}
              {LowContrastText}
            </Grid>
            """);
        fx.File(ToolConfig.FileName, """{ "ignore": { "requireReason": false } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        Assert.Equal(1, r.Suppressed);
        Assert.Empty(r.InvalidIgnores);
    }

    [Fact] // 根元素前面的註解在文件層（沒有父元素）
    public void StackedCommentsBeforeTheRootElementWork()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <?xml version="1.0" encoding="utf-8"?>
            {Mine}
            {Note}
            <Grid Background="#FFFFFF">
              {LowContrastText}
            </Grid>
            """);
        var r = fx.Run();
        Assert.Equal(1, r.Suppressed);
        Assert.Empty(r.Findings);
    }

    [Fact] // 統計分類用的理由是那個標記自己的，不是夾在中間的註解的
    public void StackedIgnoreKeepsItsOwnReason()
        => Assert.Equal("c", Assert.Single(RunWithBefore(Mine + A11y + Note).SuppressedReasons).Key);

    // ── baseline ratchet：承認既有債、只擋新增與惡化 ──

    private static AuditResult RunWith(params string[] extraTextBlocks)
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", $"""
            <Grid Background="#FFFFFF">
              {string.Join("\n  ", extraTextBlocks)}
            </Grid>
            """);
        return fx.Run();
    }

    [Fact]
    public void KnownDebtPassesNewFailureBlocks()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="既有債"/>""");
        var baseline = Baseline.Write(before);

        // 同一筆債 → 過
        var same = RunWith("""<TextBlock Foreground="#EEEEEE" Text="既有債"/>""");
        var cmp1 = Baseline.Compare(same, baseline);
        Assert.True(cmp1.Passes);
        Assert.Equal(1, cmp1.KnownDebt);

        // 新增一筆不同的破 → 擋
        var worse = RunWith(
            """<TextBlock Foreground="#EEEEEE" Text="既有債"/>""",
            """<TextBlock Foreground="#DDDDDD" Text="新的破"/>""");
        var cmp2 = Baseline.Compare(worse, baseline);
        Assert.False(cmp2.Passes);
        var nf = Assert.Single(cmp2.NewFailures);
        Assert.Equal("#DDDDDD", nf.Fg);
    }

    [Fact] // 同鍵的處數變多也是惡化（行號不進鍵 —— 行號會漂移）
    public void SameKeyMoreOccurrencesIsWorsening()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="a"/>""");
        var baseline = Baseline.Write(before);

        var doubled = RunWith(
            """<TextBlock Foreground="#EEEEEE" Text="a"/>""",
            """<TextBlock Foreground="#EEEEEE" Text="b"/>""");
        var cmp = Baseline.Compare(doubled, baseline);
        Assert.False(cmp.Passes);
        var w = Assert.Single(cmp.WorsenedFailures);
        Assert.Equal(2, w.Count);
    }

    [Fact] // 債只准減：修掉之後 paid off 要看得見
    public void PaidDebtIsVisible()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="待修"/>""");
        var baseline = Baseline.Write(before);

        var fixedUp = RunWith("""<TextBlock Foreground="#111111" Text="修好了"/>""");
        var cmp = Baseline.Compare(fixedUp, baseline);
        Assert.True(cmp.Passes);
        Assert.Equal(0, cmp.KnownDebt);
        Assert.Equal(1, cmp.PaidDebt);
    }

    [Fact] // paid off 的單位要跟 known debt 一致（處數，不是鍵數）——
           // 同鍵 3 處一次修完只顯示「還了 1」會低報進度
    public void PaidDebtCountsOccurrencesNotKeys()
    {
        var before = RunWith(
            """<TextBlock Foreground="#EEEEEE" Text="a"/>""",
            """<TextBlock Foreground="#EEEEEE" Text="b"/>""",
            """<TextBlock Foreground="#EEEEEE" Text="c"/>""");
        var baseline = Baseline.Write(before);
        // 同一組字底配對 3 處 —— 鍵只有一個，債是 3
        Assert.Equal(3, Baseline.Compare(before, baseline).KnownDebt);

        var fixedUp = RunWith("""<TextBlock Foreground="#111111" Text="全修好了"/>""");
        var cmp = Baseline.Compare(fixedUp, baseline);
        Assert.True(cmp.Passes);
        Assert.Equal(0, cmp.KnownDebt);
        Assert.Equal(3, cmp.PaidDebt); // 舊版數鍵 → 1，跟 KnownDebt 的 3 對不上
    }

    [Fact] // 色盤漂移：鍵不變（{DynamicResource Fg}）但色盤值調暗 → 比值惡化要擋
    public void PaletteDriftWorseningIsCaught()
    {
        static AuditResult RunWithPalette(string fgColor)
        {
            using var fx = new Fixture();
            fx.File("Styles/DarkTheme.xaml", $"""
                <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/>
                  <SolidColorBrush x:Key="Fg" Color="{fgColor}"/>
                  <SolidColorBrush x:Key="Other" Color="#123456"/>
                </ResourceDictionary>
                """);
            fx.File("Main.xaml", """
                <Grid Background="{DynamicResource Bg}">
                  <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
                </Grid>
                """);
            return fx.Run();
        }

        var baseline = Baseline.Write(RunWithPalette("#DDDDDD")); // 1.35:1，破，入債
        var drifted = RunWithPalette("#EEEEEE");                  // 更淡 → 1.18:1，鍵沒變
        var cmp = Baseline.Compare(drifted, baseline);
        Assert.False(cmp.Passes); // 鍵相同、處數相同 —— 但比值惡化不能靜默放行
        Assert.Single(cmp.WorsenedFailures);
    }

    [Fact] // 行號漂移不觸發假性新增：同鍵不同行仍是已知債
    public void LineDriftDoesNotCreateFalseNewFailures()
    {
        var before = RunWith("""<TextBlock Foreground="#EEEEEE" Text="x"/>""");
        var baseline = Baseline.Write(before);

        // 前面插了別的元素，行號整個位移
        var shifted = RunWith(
            """<Border Background="#FFFFFF"><TextBlock Foreground="#111111" Text="新的、過的"/></Border>""",
            """<TextBlock Foreground="#EEEEEE" Text="x"/>""");
        var cmp = Baseline.Compare(shifted, baseline);
        Assert.True(cmp.Passes);
    }
}

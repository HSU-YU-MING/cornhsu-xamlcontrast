using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// baseline 檔的格式與比對邊界。這個檔被 commit 進使用者的 repo、由 CI 讀 ——
/// 形狀變了，使用者的 CI 第二天就紅（或更糟：靜默放行）。
/// 鍵 = file|element|fg|bg，刻意不含行號；Count 與 Worst 少任何一欄都有一整類惡化靜默放行
/// （CLAUDE.md「baseline 的鍵」）。行號漂移、處數變多、色盤漂移三條在 IgnoreAndBaselineTests。
/// </summary>
public class BaselineFormatTests
{
    private static AuditResult Run(params (string File, string Xaml)[] files)
    {
        using var fx = new Fixture();
        foreach (var (f, x) in files) fx.File(f, x);
        return fx.Run();
    }

    private static string OnWhite(params string[] children) => $"""
        <Grid Background="#FFFFFF">
          {string.Join("\n  ", children)}
        </Grid>
        """;

    private const string FailText = """<TextBlock Foreground="#EEEEEE" Text="fail"/>""";

    /// <summary>手寫一份 baseline（數值經 JsonSerializer，不吃 locale）。</summary>
    private static string BaselineJson(params object[] entries) => JsonSerializer.Serialize(new { schemaVersion = 1, entries });

    [Fact] // 寫出格式：根只有 schemaVersion（1.0 起）＋ comment ＋ entries；欄位 camelCase；收「低於 AA」的每一組（fail＋warn，1.0 起）；同鍵聚合
           // （count／worst=最差）；依 file、element、fg、bg 以 ordinal 排序 —— 跨 OS 寫出的檔逐字相同，diff 才看得懂
    public void WriteEmitsBelowAaPairsAggregatedAndSortedOrdinally()
    {
        var r = Run(
            ("a.xaml", OnWhite(
                """<TextBlock Foreground="#EEEEEE" Text="fail 1"/>""",
                """<TextBlock Foreground="#EEEEEE" Text="fail 1 again, same key"/>""",
                """<TextBlock Foreground="#8A8A8A" Text="warn — debt too since 1.0"/>""",
                """<TextBlock Foreground="#111111" Text="ok"/>""",
                """<Label Foreground="#DDDDDD" Content="fail 2"/>""")),
            ("B.xaml", OnWhite(FailText)));
        var root = JsonDocument.Parse(Baseline.Write(r)).RootElement;

        Assert.Equal(["schemaVersion", "keyVersion", "comment", "entries"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Baseline.KeyVersion, root.GetProperty("keyVersion").GetInt32());
        Assert.Equal("XamlContrast baseline — known debt; may only shrink", root.GetProperty("comment").GetString());

        var entries = root.GetProperty("entries").EnumerateArray().ToList();
        Assert.All(entries, e => Assert.Equal(
            ["file", "element", "fg", "bg", "count", "worst"], e.EnumerateObject().Select(p => p.Name)));

        // ordinal：大寫 B 排在小寫 a 前面（掃描順序是不分大小寫的 a → B，排序不是照抄掃描順序）；
        // 同檔內 Label 排在 TextBlock 前面（文件順序是 TextBlock 先）
        Assert.Equal(
            ["B.xaml|TextBlock|#EEEEEE", "a.xaml|Label|#DDDDDD", "a.xaml|TextBlock|#8A8A8A", "a.xaml|TextBlock|#EEEEEE"],
            entries.Select(e => $"{e.GetProperty("file").GetString()}|{e.GetProperty("element").GetString()}|{e.GetProperty("fg").GetString()}"));

        var aggregated = entries.Single(e => e.GetProperty("file").GetString() == "a.xaml" &&
                                             e.GetProperty("element").GetString() == "TextBlock" &&
                                             e.GetProperty("fg").GetString() == "#EEEEEE");
        Assert.Equal(2, aggregated.GetProperty("count").GetInt32());
        var worst = r.Findings.Where(f => f.File == "a.xaml" && f.Fg == "#EEEEEE").Min(f => f.Worst);
        Assert.Equal(worst, aggregated.GetProperty("worst").GetDouble());
        Assert.Equal("#FFFFFF", aggregated.GetProperty("bg").GetString());
    }

    [Theory] // 鍵的每一段都算數：同一組顏色換了檔案、換了元素、換了底，都是新的破 ——
             // 鍵少任何一段，別處的新問題就會被當成已知債放行
    [InlineData("Other.xaml", "TextBlock", "#FFFFFF")]
    [InlineData("Main.xaml", "Label", "#FFFFFF")]
    [InlineData("Main.xaml", "TextBlock", "#F5F5F5")]
    public void EveryKeyComponentDistinguishesFailures(string file, string element, string bg)
    {
        var baseline = Baseline.Write(Run(("Main.xaml", OnWhite(FailText))));
        var current = Run((file, $"""
            <Grid Background="{bg}">
              <{element} Foreground="#EEEEEE"/>
            </Grid>
            """));
        var cmp = Baseline.Compare(current, baseline);
        Assert.False(cmp.Passes);
        Assert.Single(cmp.NewFailures);
        Assert.Equal(1, cmp.PaidDebt); // 原本那一筆不見了 —— 算還債，但不抵銷新的
    }

    [Fact] // 缺 entries → 例外（CLI 轉成 exit 2「baseline is not valid」）—— 不是當成「沒有債」
    public void MissingEntriesIsAnError()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, """{ "schemaVersion": 1, "comment": "no entries" }"""));
        Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, """{ "schemaVersion": 1, "entries": null }"""));
    }

    [Fact] // 欄位名不分大小寫：手寫或其他工具產出的 PascalCase 檔照樣讀得進
    public void EntryFieldsAreCaseInsensitive()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        var cmp = Baseline.Compare(r, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            entries = new[] { new { File = f.File, Element = f.Element, Fg = f.Fg, Bg = f.Bg, Count = 1, Worst = f.Worst } },
        }));
        Assert.True(cmp.Passes);
        Assert.Equal(1, cmp.KnownDebt);
    }

    [Fact] // 頂層的 entries（與 schemaVersion）也不分大小寫 —— 舊版欄位名不分、頂層卻用 GetProperty("entries") 分大小寫，
           // 整份 PascalCase 的檔 exit 2，訊息還是不知所云的 "The given key was not present in the dictionary."
    public void TopLevelEntriesIsCaseInsensitiveToo()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        var cmp = Baseline.Compare(r, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Entries = new[] { new { File = f.File, Element = f.Element, Fg = f.Fg, Bg = f.Bg, Count = 1, Worst = f.Worst } },
        }));
        Assert.True(cmp.Passes);
        Assert.Equal(1, cmp.KnownDebt);
    }

    [Fact] // 真的沒有 entries：訊息要說出缺的是什麼
    public void MissingEntriesSaysSo()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var ex = Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, """{ "schemaVersion": 1, "nope": [] }"""));
        Assert.Contains("'entries'", ex.Message);
    }

    [Fact] // Worst 的容差是 0.01：四捨五入的雜訊不算惡化，超過才算
    public void WorstToleranceIsOneHundredth()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        object Entry(double worst) => new { file = f.File, element = f.Element, fg = f.Fg, bg = f.Bg, count = 1, worst };

        Assert.True(Baseline.Compare(r, BaselineJson(Entry(f.Worst + 0.009))).Passes);
        var drifted = Baseline.Compare(r, BaselineJson(Entry(f.Worst + 0.011)));
        Assert.False(drifted.Passes);
        Assert.Equal(f.Worst, Assert.Single(drifted.WorsenedFailures).Worst); // 回報的是現在的比值
    }

    [Fact] // 同鍵處數下降（3 → 1）：還在債內、照過；paid off 只算整個鍵消失的處數，部分償還不計
    public void PartialPaydownPassesButIsNotCountedAsPaid()
    {
        var baseline = Baseline.Write(Run(("Main.xaml", OnWhite(FailText, FailText, FailText))));
        var cmp = Baseline.Compare(Run(("Main.xaml", OnWhite(FailText))), baseline);
        Assert.True(cmp.Passes);
        Assert.Equal(1, cmp.KnownDebt);
        Assert.Equal(0, cmp.PaidDebt);
    }

    // ── 1.0 的嚴格讀取（決策檔 H17，選項 A）：缺 count／worst、沒有或不認得 schemaVersion → 例外（CLI 轉成 exit 2）──
    //    0.x 缺欄時補 0：缺 worst → 色盤漂移檢查靜默失效（比值再差也不會 < 0 − 0.01）；缺 count → 每一處都算惡化。
    //    工具自己寫出的檔一定齊全，只有手改或外部產生的會中 —— 但中了就是一整類惡化靜默放行

    [Theory]
    [InlineData("worst")]
    [InlineData("count")]
    public void EntryWithoutCountOrWorstIsAnError(string missing)
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        var entry = new Dictionary<string, object> { ["file"] = f.File, ["element"] = f.Element, ["fg"] = f.Fg, ["bg"] = f.Bg, ["count"] = 1, ["worst"] = f.Worst };
        entry.Remove(missing);
        var ex = Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, BaselineJson(entry)));
        Assert.Contains($"no '{missing}'", ex.Message);
    }

    [Fact] // 沒有 schemaVersion = 0.x 寫的檔。1.0 改了 baseline 收的東西（低於 AA 的每一組，不只 fail），
           // 舊檔照讀的話所有 warn 都會變成 NEW —— 大聲失敗並說出怎麼辦，比一牆的假 NEW 好
    public void BaselineWithoutASchemaVersionWasWrittenByZeroX()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var ex = Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, """{ "entries": [] }"""));
        Assert.Contains("written by XamlContrast 0.x", ex.Message);
        Assert.Contains("--write-baseline", ex.Message);
    }

    [Theory] // 比自己新的版本（或不是整數）：不猜著讀
    [InlineData("2")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    public void UnknownSchemaVersionIsAnError(string version)
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var ex = Assert.ThrowsAny<Exception>(() => Baseline.Compare(r, $$"""{ "schemaVersion": {{version}}, "entries": [] }"""));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Theory] // 值的範圍（審查 1.0 C7）：count 至少 1、worst 是對比值（1～21）。0.x 照收 —— count 0 讓每一處都算惡化，
             // worst 0 讓色盤漂移永遠抓不到（比值再差也不會 < 0 − 0.01），跟缺欄一樣是一整類惡化靜默放行。
             // 1.0 之後再收緊就是 2.0，所以現在定
    [InlineData(0, 1.5, "count")]
    [InlineData(-1, 1.5, "count")]
    [InlineData(1, 0, "worst")]
    [InlineData(1, -2, "worst")]
    [InlineData(1, 0.5, "worst")]
    [InlineData(1, 22, "worst")]
    public void EntryValuesOutOfRangeAreAnError(int count, double worst, string field)
    {
        var ex = Assert.ThrowsAny<FormatException>(() => Baseline.Parse(BaselineJson(
            new { file = "Main.xaml", element = "TextBlock", fg = "#EEEEEE", bg = "#FFFFFF", count, worst })));
        Assert.Contains("entry 1", ex.Message);
        Assert.Contains($"'{field}'", ex.Message);
    }

    [Theory] // 型別錯誤點名第幾筆、哪一欄、期望的型別 —— 舊版是 .NET 的訊息「could not be converted to
             // XamlContrast.Core.Baseline+Entry」（審查 1.0 最終驗證，可接受項）
    [InlineData("\"count\": \"1\", \"worst\": 1.49", "entry 1 has 'count' \"1\" — it must be a whole number")]
    [InlineData("\"count\": 1.5, \"worst\": 1.49", "entry 1 has 'count' 1.5 — it must be a whole number")]
    [InlineData("\"count\": 1, \"worst\": \"1.49\"", "entry 1 has 'worst' \"1.49\" — it must be a number")]
    public void EntryFieldTypesAreNamed(string countAndWorst, string expected)
    {
        var json = $$"""{ "schemaVersion": 1, "entries": [ { "file": "Main.xaml", "element": "TextBlock", "fg": "#EEEEEE", "bg": "#FFFFFF", {{countAndWorst}} } ] }""";
        var ex = Assert.ThrowsAny<FormatException>(() => Baseline.Parse(json));
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("XamlContrast.Core", ex.Message);
        Assert.DoesNotContain("System.", ex.Message);
    }

    [Fact]
    public void EntryTextFieldsMustBeStrings()
    {
        var ex = Assert.ThrowsAny<FormatException>(() => Baseline.Parse(
            """{ "schemaVersion": 1, "entries": [ { "file": 3, "element": "TextBlock", "fg": "#EEEEEE", "bg": "#FFFFFF", "count": 1, "worst": 1.2 } ] }"""));
        Assert.Contains("entry 1 has 'file' 3 — it must be a string", ex.Message);
    }

    [Theory] // 範圍內的極值照收
    [InlineData(1, 1.0)]
    [InlineData(250, 21.0)]
    public void EntryValuesAtTheEdgesAreAccepted(int count, double worst)
        => Assert.Single(Baseline.Parse(BaselineJson(
            new { file = "Main.xaml", element = "TextBlock", fg = "#EEEEEE", bg = "#FFFFFF", count, worst })).Entries);

    [Fact] // 同一個鍵出現兩次：手改或合併衝突的痕跡。0.x 後者勝、前者靜默消失 —— 改成點名是哪兩筆
    public void DuplicateKeysAreAnError()
    {
        var e = new { file = "Main.xaml", element = "TextBlock", fg = "#EEEEEE", bg = "#FFFFFF", count = 1, worst = 1.16 };
        var other = new { file = "Main.xaml", element = "Label", fg = "#EEEEEE", bg = "#FFFFFF", count = 1, worst = 1.16 };
        var ex = Assert.ThrowsAny<FormatException>(() => Baseline.Parse(BaselineJson(e, other, e with { count = 2 })));
        Assert.Contains("entries 1 and 3", ex.Message);
        Assert.Contains("Main.xaml|TextBlock|#EEEEEE|#FFFFFF", ex.Message);
    }

    // ── keyVersion（審查 1.0 A2，演進規則 5「舊產物失效時要大聲失敗」）：鍵的產生方式（element 的標籤、bg 的合成描述……）
    //    屬稽核語意，1.x 的 minor 可以改。改的那一版遞增 keyVersion；讀到舊的 keyVersion 就明確叫人重寫，
    //    不是冒出一整排假的 NEW（同一組配對、新的鍵）加一整排假的 paid off ──

    [Fact] // 缺 keyVersion 視為 1（手寫的檔、只帶 schemaVersion 的檔照樣讀得進）
    public void MissingKeyVersionIsReadAsOne()
    {
        var r = Run(("Main.xaml", OnWhite(FailText)));
        var f = r.Findings.Single();
        var known = Baseline.Parse(BaselineJson(new { file = f.File, element = f.Element, fg = f.Fg, bg = f.Bg, count = 1, worst = f.Worst }));
        Assert.Single(known.Entries);
        Assert.True(Baseline.Compare(r, known).Passes);
    }

    [Fact] // 這個版本產生鍵的方式比檔案新：鍵對不上，照讀會把每一筆既有債報成 NEW —— 改成點名並叫人重寫
    public void OlderKeyVersionAsksForARegeneratedBaseline()
    {
        var ex = Assert.ThrowsAny<FormatException>(() => Baseline.Parse("""{ "schemaVersion": 1, "entries": [] }""", currentKeyVersion: 2));
        Assert.Contains("keyVersion 1", ex.Message);
        Assert.Contains("regenerate it with --write-baseline", ex.Message);
    }

    [Theory] // 比自己新的 keyVersion，或不是正整數：不猜著讀。比自己新的要叫人升級工具，不是叫人用舊工具重寫（審查 1.0 最終驗證 S7）
    [InlineData("2", "upgrade XamlContrast")]
    [InlineData("0", "positive integer")]
    [InlineData("\"1\"", "positive integer")]
    [InlineData("1.5", "positive integer")]
    public void UnknownKeyVersionIsAnError(string version, string expect)
    {
        var ex = Assert.ThrowsAny<FormatException>(() => Baseline.Parse($$"""{ "schemaVersion": 1, "keyVersion": {{version}}, "entries": [] }"""));
        Assert.Contains("keyVersion", ex.Message);
        Assert.Contains(expect, ex.Message);
    }

    [Fact] // 0.x 的 baseline 被拒時，「regenerate it with --write-baseline」只說一次 —— 例外訊息已經帶了，
           // CLI 那一行提示是給沒帶的訊息用的（審查 1.0 C5：舊版印兩次）
    public void CliSaysRegenerateOnceForAZeroXBaseline()
    {
        using var fx = CliProjects.Failing();
        fx.File("baseline.json", """{ "entries": [] }""");
        var cli = Cli.Run(fx.Root, "--baseline", Path.Combine(fx.Root, "baseline.json"));
        Assert.True(cli.Exit == 2, cli.ToString());
        Assert.Equal(1, cli.Err.Split("--write-baseline").Length - 1);
    }

    [Theory] // 比這個版本新的 baseline（schemaVersion 或 keyVersion）：該做的是升級工具。舊版的 CLI 一律補一行
             // 「regenerate it with --write-baseline」—— 照做的話，是用舊工具把新工具寫的債清單蓋掉（審查 1.0 最終驗證 S7）
    [InlineData("""{ "schemaVersion": 2, "entries": [] }""")]
    [InlineData("""{ "schemaVersion": 1, "keyVersion": 2, "entries": [] }""")]
    public void BaselineFromANewerVersionAsksForAnUpgrade(string json)
    {
        using var fx = CliProjects.Failing();
        fx.File("baseline.json", json);
        var cli = Cli.Run(fx.Root, "--baseline", Path.Combine(fx.Root, "baseline.json"));
        Assert.True(cli.Exit == 2, cli.ToString());
        Assert.Contains("upgrade XamlContrast", cli.Err);
        Assert.DoesNotContain("regenerate", cli.Err);
    }

    [Fact] // CLI：讀不了的 baseline → exit 2、stderr 說出原因與怎麼重寫
    public void CliRejectsAnIncompleteBaseline()
    {
        using var fx = CliProjects.Failing();
        var f = fx.Run().Findings.Single();
        fx.File("baseline.json", BaselineJson(new { file = f.File, element = f.Element, fg = f.Fg, bg = f.Bg, count = 1 }));
        var cli = Cli.Run(fx.Root, "--baseline", Path.Combine(fx.Root, "baseline.json"));
        Assert.True(cli.Exit == 2, cli.ToString());
        Assert.Contains("no 'worst'", cli.Err);
        Assert.Contains("regenerate it with --write-baseline", cli.Err);
    }
}

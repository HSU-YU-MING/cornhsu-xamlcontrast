using System.Globalization;
using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// --json 的形狀是 1.0 凍結面之一（CLAUDE.md：「JSON 形狀變了就遞增 Report.cs 的 schemaVersion」）。
/// 這裡把每個 schemaVersion 的形狀登記下來：形狀一變、或 schemaVersion 一變，這裡一定紅 ——
/// 逼改的人做兩件事：遞增 schemaVersion、在 <see cref="ShapeBySchemaVersion"/> 新增一列
/// （並確認 action.yml 那幾段 jq 還讀得到欄位，見 ActionContractTests）。
/// </summary>
public class JsonReportContractTests
{
    /// <summary>
    /// ⚠ 已發佈的列不可改寫 —— 那等於默默改了已發佈的格式。形狀變了就新增一列。
    /// 路徑格式：物件屬性用「.」、陣列元素用「[]」、動態鍵的字典（unresolvedBy、unknownKeys）
    /// 收成「.*」；冒號後是 JsonValueKind（True/False 合併成 Boolean）。
    /// 陣列元素取所有元素形狀的聯集 —— 選填欄位（symmetry、fontSize）的出現規則另有測試。
    /// </summary>
    private static readonly Dictionary<int, string[]> ShapeBySchemaVersion = new()
    {
        [5] =
        [
            "findings:Array",
            "findings[]:Object",
            "findings[].bg:String",
            "findings[].category:String",
            "findings[].element:String",
            "findings[].fg:String",
            "findings[].file:String",
            "findings[].fontSize:String",
            "findings[].isText:Boolean",
            "findings[].largeText:Boolean",
            "findings[].line:Number",
            "findings[].ratioDark:Number",
            "findings[].ratioLight:Number",
            "findings[].symmetry:String",
            "findings[].threshold:Number",
            "schemaVersion:Number",
            "summary.configLoaded:Boolean",
            "summary.counts.decorative:Number",
            "summary.counts.fail:Number",
            "summary.counts.ok:Number",
            "summary.counts.warn:Number",
            "summary.counts:Object",
            "summary.coverage:Number",
            "summary.deadForeground:Number",
            "summary.disabledExempt:Number",
            "summary.files:Number",
            "summary.invalidIgnores:Array",
            "summary.invalidIgnores[]:String",
            "summary.pairs:Number",
            "summary.paletteDetail:String",
            "summary.paletteKeys:Number",
            "summary.paletteMode:String",
            "summary.paletteSource:String",
            "summary.parseErrorFiles:Array",
            "summary.parseErrorFiles[]:String",
            "summary.parseErrors:Number",
            "summary.singleTheme:Boolean",
            "summary.skipped:Number",
            "summary.suppressed:Number",
            "summary.thresholds.failFactor:Number",
            "summary.thresholds.largeText:Number",
            "summary.thresholds.normalText:Number",
            "summary.thresholds:Object",
            "summary.unknownKeys.*:Number",
            "summary.unknownKeys:Object",
            "summary.unresolved:Number",
            "summary.unresolvedBy.*:Number",
            "summary.unresolvedBy:Object",
            "summary:Object",
            "unresolved:Array",
            "unresolved[].file:String",
            "unresolved[].line:Number",
            "unresolved[].reason:String",
            "unresolved[].value:String",
            "unresolved[]:Object",
        ],
    };

    private static readonly HashSet<string> DynamicKeyMaps = ["summary.unresolvedBy", "summary.unknownKeys"];

    /// <summary>讓每一個選填欄位都出現的專案：未知鍵、Binding、解析失敗檔、無理由 ignore、
    /// 有理由 ignore、FontSize、停用態、死 setter、Opacity 0。</summary>
    private static Fixture Everything()
    {
        var fx = new Fixture().WithPalette();
        fx.File("Broken.xaml", "<Grid><未閉合");
        fx.File("Main.xaml", """
            <Grid xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{DynamicResource Bg}">
              <Grid.Resources>
                <Style x:Key="Btn" TargetType="Button">
                  <Setter Property="Background" Value="#000000"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Style.Triggers>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Foreground" Value="#303030"/>
                    </Trigger>
                  </Style.Triggers>
                </Style>
                <Style x:Key="Dead" TargetType="CheckBox">
                  <Setter Property="Background" Value="#FFFFFF"/>
                  <Setter Property="Foreground" Value="#FFFFFF"/>
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="CheckBox">
                        <Border Background="#FFFFFF"><Path Stroke="#FFFFFF"/></Border>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Grid.Resources>
              <!-- 排成一列：Grid 的子元素會疊在同一格，佔滿格子的兄弟（下面的 Binding 底）會變成後面兄弟的背板（規則 17） -->
              <StackPanel>
              <TextBlock Foreground="{DynamicResource Fg}" Text="ok"/>
              <TextBlock Foreground="{DynamicResource Dim}" FontSize="12" Text="fail, 12px"/>
              <TextBlock Foreground="{DynamicResource Zed}" Text="unknown key 1"/>
              <TextBlock Foreground="{DynamicResource Zed}" Text="unknown key 2"/>
              <TextBlock Foreground="{DynamicResource Beta}" Text="unknown key 3"/>
              <TextBlock Foreground="{DynamicResource Alpha}" Text="unknown key 4"/>
              <Border Background="{Binding UserColour}"><TextBlock Foreground="#FFFFFF" Text="bound"/></Border>
              <!-- xamlcontrast-ignore -->
              <TextBlock Foreground="{DynamicResource Fg}" Text="ignore without a reason: invalid, still reported"/>
              <!-- xamlcontrast-ignore: watermark -->
              <TextBlock Foreground="{DynamicResource Dim}" Text="suppressed"/>
              <TextBlock Opacity="0" Foreground="#FFFFFF" Text="skipped"/>
              </StackPanel>
            </Grid>
            """);
        return fx;
    }

    private static JsonElement JsonOf(Fixture fx) => JsonDocument.Parse(Report.ToJson(fx.Run())).RootElement;

    private static string KindOf(JsonElement e) => e.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? "Boolean" : e.ValueKind.ToString();

    private static void Flatten(JsonElement e, string path, SortedSet<string> into)
    {
        if (path.Length > 0) into.Add($"{path}:{KindOf(e)}");
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
                Flatten(p.Value, DynamicKeyMaps.Contains(path) ? $"{path}.*"
                               : path.Length == 0 ? p.Name : $"{path}.{p.Name}", into);
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray())
                Flatten(item, $"{path}[]", into);
    }

    [Fact]
    public void JsonShapeIsRegisteredForItsSchemaVersion()
    {
        using var fx = Everything();
        var root = JsonOf(fx);
        var version = root.GetProperty("schemaVersion").GetInt32();

        // 輸出的必須是登記過的最新版 —— 新增一列卻忘了遞增 Report.cs，這裡會紅
        Assert.Equal(ShapeBySchemaVersion.Keys.Max(), version);
        Assert.True(ShapeBySchemaVersion.TryGetValue(version, out var expected),
            $"schemaVersion {version} 沒有登記形狀 —— 遞增 schemaVersion 時要在 ShapeBySchemaVersion 新增一列");

        var actual = new SortedSet<string>(StringComparer.Ordinal);
        Flatten(root, "", actual);
        Assert.Equal(expected.OrderBy(s => s, StringComparer.Ordinal), actual);
    }

    [Fact] // 選填欄位的出現規則：symmetry 只在 fail/warn、fontSize 只在有 FontSize 時
    public void OptionalFindingFieldsAppearOnlyWhenTheyApply()
    {
        using var fx = Everything();
        var findings = JsonOf(fx).GetProperty("findings").EnumerateArray().ToList();
        foreach (var f in findings)
        {
            var category = f.GetProperty("category").GetString();
            Assert.Equal(category is "fail" or "warn", f.TryGetProperty("symmetry", out _));
        }
        var sized = Assert.Single(findings, f => f.TryGetProperty("fontSize", out _));
        Assert.Equal("12px", sized.GetProperty("fontSize").GetString());
    }

    [Fact] // 空值政策：沒資料的清單整個省略（null 不輸出），unresolvedBy 例外 —— 一律輸出、可為 {}
    public void EmptyOptionalFieldsAreOmittedButUnresolvedByIsAlwaysPresent()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="21:1"/>
            </Grid>
            """);
        var root = JsonOf(fx);
        Assert.False(root.TryGetProperty("unresolved", out _));
        var summary = root.GetProperty("summary");
        Assert.False(summary.TryGetProperty("unknownKeys", out _));
        Assert.False(summary.TryGetProperty("parseErrorFiles", out _));
        Assert.False(summary.TryGetProperty("invalidIgnores", out _));
        var by = summary.GetProperty("unresolvedBy");
        Assert.Equal(JsonValueKind.Object, by.ValueKind);
        Assert.Empty(by.EnumerateObject());
        var f = Assert.Single(root.GetProperty("findings").EnumerateArray().ToList());
        Assert.False(f.TryGetProperty("fontSize", out _));
        Assert.False(f.TryGetProperty("symmetry", out _));
        Assert.Equal("none", summary.GetProperty("paletteSource").GetString());
        Assert.Equal("none", summary.GetProperty("paletteMode").GetString());
    }

    [Fact] // 計數器之間的恆等式 —— 任何一個計數「漏算」或「重算」都會讓它們對不上
    public void JsonCountersAreInternallyConsistent()
    {
        using var fx = Everything();
        var root = JsonOf(fx);
        var s = root.GetProperty("summary");
        int N(JsonElement e, string name) => e.GetProperty(name).GetInt32();

        var findings = root.GetProperty("findings").GetArrayLength();
        var counts = s.GetProperty("counts");
        Assert.Equal(findings, N(counts, "fail") + N(counts, "warn") + N(counts, "ok") + N(counts, "decorative"));
        Assert.Equal(findings, N(s, "pairs") - N(s, "suppressed"));

        var unresolved = N(s, "unresolved");
        Assert.Equal(unresolved, s.GetProperty("unresolvedBy").EnumerateObject().Sum(p => p.Value.GetInt32()));
        Assert.Equal(unresolved, root.GetProperty("unresolved").GetArrayLength());
        Assert.Equal(s.GetProperty("unresolvedBy").GetProperty("unknown-palette-key").GetInt32(),
                     s.GetProperty("unknownKeys").EnumerateObject().Sum(p => p.Value.GetInt32()));
        Assert.Equal(N(s, "parseErrors"), s.GetProperty("parseErrorFiles").GetArrayLength());

        var pairs = N(s, "pairs");
        Assert.Equal(Math.Round(100.0 * pairs / (pairs + unresolved), 1), s.GetProperty("coverage").GetDouble());

        // 這個素材每個計數器都非零 —— 恆等式不是在比 0 == 0
        Assert.All(new[] { "pairs", "unresolved", "suppressed", "skipped", "deadForeground", "disabledExempt", "parseErrors" },
            name => Assert.True(N(s, name) > 0, name));
        Assert.Equal(1, s.GetProperty("invalidIgnores").GetArrayLength());
    }

    [Fact] // summary.coverage 四捨五入到小數一位：1 / 3 → 33.3，不是 33.333…
           // （Everything() 的比值剛好除得盡，上一條的恆等式驗不到四捨五入本身）
    public void CoverageIsRoundedToOneDecimal()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="resolved"/>
              <Grid Background="{Binding X}"><TextBlock Foreground="#FFFFFF" Text="unresolved 1"/></Grid>
              <Grid Background="{Binding Y}"><TextBlock Foreground="#FFFFFF" Text="unresolved 2"/></Grid>
            </Grid>
            """);
        var s = JsonOf(fx).GetProperty("summary");
        Assert.Equal(1, s.GetProperty("pairs").GetInt32());
        Assert.Equal(2, s.GetProperty("unresolved").GetInt32());
        Assert.Equal(33.3, s.GetProperty("coverage").GetDouble());
    }

    [Fact] // unresolved[].value：unknown-palette-key 那幾筆放的是鍵名，依發現順序 —— 這是把未知鍵
           // 對回行號的唯一管道（summary.unknownKeys 只有計數）。其他原因的 value 語意尚待決定
           // （公開介面盤點 4.3、4.7 第 10 點：背景側卡住時現在放的是前景值），所以只釘這一種
    public void UnresolvedSitesNameTheUnknownKeyAtEachLocation()
    {
        using var fx = Everything();
        var sites = JsonOf(fx).GetProperty("unresolved").EnumerateArray()
            .Where(x => x.GetProperty("reason").GetString() == "unknown-palette-key")
            .Select(x => $"{x.GetProperty("file").GetString()}:{x.GetProperty("line").GetInt32()} {x.GetProperty("value").GetString()}")
            .ToArray();
        Assert.Equal(["Main.xaml:28 Zed", "Main.xaml:29 Zed", "Main.xaml:30 Beta", "Main.xaml:31 Alpha"], sites);
    }

    [Fact] // unresolved[].value 是「卡住的那個值」：問題出在背景時，舊版放的是前景的值（bg.Key ?? fgRaw、bgV ?? fgV）——
           // `--show-unresolved` 印 "bound-or-gradient #FFFFFF"，點名的是沒問題的字色，不是卡住的 {Binding Z}
    public void UnresolvedValueNamesTheBackgroundWhenTheBackgroundIsTheProblem()
    {
        using var fx = new Fixture();
        fx.File("Main.xaml", """
            <StackPanel xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="#000000">
              <StackPanel.Resources>
                <Style x:Key="Pad" TargetType="Button"><Setter Property="Padding" Value="4"/></Style>
              </StackPanel.Resources>
              <Border Background="{Binding Z}"><TextBlock Foreground="#FFFFFF" Text="tree walk"/></Border>
              <Border Background="{Binding Y}"><Button Style="{StaticResource Pad}" Foreground="#FFFFFF" Content="style chain"/></Border>
              <Border><Border.Background><LinearGradientBrush/></Border.Background><TextBlock Foreground="#FFFFFF" Text="gradient"/></Border>
              <Grid><Image Source="a.png"/><TextBlock Foreground="#FFFFFF" Text="over a picture"/></Grid>
              <TextBlock Foreground="{Binding Fg}" Text="foreground side: still the foreground value"/>
            </StackPanel>
            """);
        var values = fx.Run().UnresolvedSites.Select(s => s.Value).ToList();
        Assert.Equal(["{Binding Z}", "{Binding Y}", "<LinearGradientBrush>", "<Image>", "{Binding Fg}"], values);
    }

    [Fact] // summary 的描述欄位要說實話：這份結果是哪個色盤、哪份 config、哪套門檻跑出來的。
           // 形狀測試只驗型別 —— 值寫反了（singleTheme、configLoaded）形狀照樣對，讀報告的人卻被誤導
    public void SummaryDescribesThePaletteConfigAndThresholdsOfTheRun()
    {
        using var fx = new Fixture().WithPalette().File("Main.xaml", """
            <Grid Background="{DynamicResource Bg}">
              <TextBlock Foreground="{DynamicResource Fg}" Text="x"/>
            </Grid>
            """);
        fx.File(ToolConfig.FileName, """{ "thresholds": { "normalText": 7.0 } }""");
        var cfg = ToolConfig.Load(fx.Root);
        var r = Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root, cfg), cfg);
        var s = JsonDocument.Parse(Report.ToJson(r)).RootElement.GetProperty("summary");

        Assert.Equal("project", s.GetProperty("paletteSource").GetString());
        Assert.Equal("pair", s.GetProperty("paletteMode").GetString());
        Assert.Equal(r.Detection.Description, s.GetProperty("paletteDetail").GetString());
        Assert.Equal(3, s.GetProperty("paletteKeys").GetInt32());
        Assert.False(s.GetProperty("singleTheme").GetBoolean());
        Assert.True(s.GetProperty("configLoaded").GetBoolean());
        var t = s.GetProperty("thresholds");
        Assert.Equal(7.0, t.GetProperty("normalText").GetDouble());   // config 覆寫的值
        Assert.Equal(3.0, t.GetProperty("largeText").GetDouble());    // 沒覆寫的維持預設
        Assert.Equal(0.67, t.GetProperty("failFactor").GetDouble());
        Assert.Equal(1, s.GetProperty("files").GetInt32());           // 兩個主題檔是色盤，不算畫面

        // 對照組：沒有 config、寫死色碼 —— 每個描述欄位都要跟著翻
        using var bare = new Fixture().File("Main.xaml", """<Grid Background="#000000"><TextBlock Foreground="#FFFFFF"/></Grid>""");
        var b = JsonOf(bare).GetProperty("summary");
        Assert.Equal("none", b.GetProperty("paletteSource").GetString());
        Assert.Equal(0, b.GetProperty("paletteKeys").GetInt32());
        Assert.True(b.GetProperty("singleTheme").GetBoolean());       // 空色盤：寫死色碼深淺恆等
        Assert.False(b.GetProperty("configLoaded").GetBoolean());
        Assert.Equal(4.5, b.GetProperty("thresholds").GetProperty("normalText").GetDouble());

        // 第三組：只有深色一套的專案色盤（CelFlow 形狀）—— 色盤來自專案，但對稱欄不適用
        using var single = new Fixture().File("Themes/DarkTheme.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/>
              <SolidColorBrush x:Key="Fg" Color="#EEEEEE"/>
              <SolidColorBrush x:Key="Dim" Color="#2A2A2A"/>
            </ResourceDictionary>
            """).File("Main.xaml", """<Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Fg}"/></Grid>""");
        var one = JsonOf(single).GetProperty("summary");
        Assert.Equal("project", one.GetProperty("paletteSource").GetString());
        Assert.Equal("single", one.GetProperty("paletteMode").GetString());
        Assert.Equal(3, one.GetProperty("paletteKeys").GetInt32());
        Assert.True(one.GetProperty("singleTheme").GetBoolean());
    }

    [Fact] // unknownKeys 依次數遞減、同數依鍵名（ordinal）—— 點名清單的順序要可重現
    public void UnknownKeysAreOrderedByCountThenName()
    {
        using var fx = Everything();
        var keys = JsonOf(fx).GetProperty("summary").GetProperty("unknownKeys").EnumerateObject()
            .Select(p => $"{p.Name}={p.Value.GetInt32()}").ToArray();
        Assert.Equal(["Zed=2", "Alpha=1", "Beta=1"], keys);
    }

    [Fact] // 列舉值是給 CI 腳本 match 的（action.yml 的 jq 認 "fail"/"warn"）—— 每一個標籤都釘住
    public void EnumLabelsArePinned()
    {
        Assert.Equal(
            ["ok", "warn", "fail", "decorative"],
            new[] { Category.Ok, Category.Warn, Category.Fail, Category.Decorative }.Select(Report.CategoryLabel));
        Assert.Equal(
            ["both-low", "dark-fails", "light-fails", "single-theme", "n/a"],
            new[] { Symmetry.BothLow, Symmetry.DarkFails, Symmetry.LightFails, Symmetry.SingleTheme, Symmetry.NotApplicable }
                .Select(Report.SymmetryLabel));
        Assert.Equal(
            ["no-ancestor-background", "bound-or-gradient", "unknown-palette-key",
             "translucent-uncomposited", "same-brush-pair", "over-sibling-content"],
            Enum.GetValues<UnresolvedReason>().Select(Report.ReasonLabel));
    }

    // ── SARIF（--sarif）：不在官方凍結定義內，但 code scanning 用 ruleId + 位置追蹤 alert，
    //    改 ruleId 會讓既有 alert 全部關閉再重開 —— 事實上的契約 ──

    private static AuditResult FailAndWarn()
    {
        using var fx = new Fixture();
        fx.File("Views/Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="fail"/>
              <TextBlock Foreground="#8A8A8A" Text="warn"/>
              <TextBlock Foreground="#111111" Text="ok"/>
            </Grid>
            """);
        return fx.Run();
    }

    [Fact]
    public void SarifContractIsPinned()
    {
        var r = FailAndWarn();
        string sarif;
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // 訊息文字的數字格式不在這條的範圍
        try { sarif = Report.ToSarif(r, "1.2.3-test"); }
        finally { CultureInfo.CurrentCulture = prev; }
        var root = JsonDocument.Parse(sarif).RootElement;

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        var run = Assert.Single(root.GetProperty("runs").EnumerateArray().ToList());
        var driver = run.GetProperty("tool").GetProperty("driver");
        Assert.Equal("XamlContrast", driver.GetProperty("name").GetString());
        Assert.Equal("1.2.3-test", driver.GetProperty("version").GetString());
        var rule = Assert.Single(driver.GetProperty("rules").EnumerateArray().ToList());
        Assert.Equal("wcag-contrast", rule.GetProperty("id").GetString());
        Assert.Equal("WcagContrast", rule.GetProperty("name").GetString());

        var results = run.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        Assert.All(results, x => Assert.Equal("wcag-contrast", x.GetProperty("ruleId").GetString()));

        var fail = r.Findings.Single(f => f.Category == Category.Fail);
        var failResult = Assert.Single(results, x => x.GetProperty("level").GetString() == "error");
        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture,
                $"Contrast {fail.RatioDark}:1 dark / {fail.RatioLight}:1 light — needs 4.5:1 (fg=#EEEEEE, bg=#FFFFFF, single-theme, TextBlock)"),
            failResult.GetProperty("message").GetProperty("text").GetString());
        var loc = Assert.Single(failResult.GetProperty("locations").EnumerateArray().ToList()).GetProperty("physicalLocation");
        Assert.Equal("Views/Main.xaml", loc.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(fail.Line, loc.GetProperty("region").GetProperty("startLine").GetInt32());
        Assert.Single(results, x => x.GetProperty("level").GetString() == "warning");
    }

    [Fact] // SARIF 的 startLine 從 1 起算：沒有行號資訊（0）的 finding 夾成 1，否則 code scanning 拒收整份
    public void SarifStartLineIsClampedToOne()
    {
        var r = FailAndWarn();
        var orig = r.Findings.Single(f => f.Category == Category.Fail);
        r.Findings.Clear();
        r.Findings.Add(new Finding
        {
            File = orig.File,
            Line = 0,
            Element = orig.Element,
            Fg = orig.Fg,
            Bg = orig.Bg,
            RatioDark = orig.RatioDark,
            RatioLight = orig.RatioLight,
            IsText = orig.IsText,
            Need = orig.Need,
            Category = orig.Category,
            Symmetry = orig.Symmetry,
        });
        var result = JsonDocument.Parse(Report.ToSarif(r, "0.0.0")).RootElement
            .GetProperty("runs")[0].GetProperty("results")[0];
        Assert.Equal(1, result.GetProperty("locations")[0].GetProperty("physicalLocation")
            .GetProperty("region").GetProperty("startLine").GetInt32());
    }
}

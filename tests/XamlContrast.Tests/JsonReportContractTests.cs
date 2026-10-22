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
        // 6（1.0）：盤點 4.7 的命名與空值整理一次做完 —— 頂層 unresolved → unresolvedSites（與 summary.unresolved 不再同名）、
        // findings[].largeText → isLargeText、fontSize 改成數字（DIP，不取整）、拿掉 paletteSource（由 paletteMode 推得）；
        // 每個欄位一律輸出（沒有值是 null、清單可為空），不再省略；新增 toolVersion、exitCode、summary.failOn／minCoverage／
        // strictPalette（實際生效的 gate）、summary.invalidIgnoreCount、findings[].theme（只屬於一個主題的配對）、
        // findings[].baseline（--baseline 模式下的 known／new／worsened）
        [6] =
        [
            "exitCode:Number",
            "findings:Array",
            "findings[]:Object",
            "findings[].baseline:Null",
            "findings[].bg:String",
            "findings[].category:String",
            "findings[].element:String",
            "findings[].fg:String",
            "findings[].file:String",
            "findings[].fontSize:Null",
            "findings[].fontSize:Number",
            "findings[].isLargeText:Boolean",
            "findings[].isText:Boolean",
            "findings[].line:Number",
            "findings[].ratioDark:Number",
            "findings[].ratioLight:Number",
            "findings[].symmetry:Null",
            "findings[].symmetry:String",
            "findings[].theme:Null",
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
            "summary.failOn:String",
            "summary.files:Number",
            "summary.invalidIgnoreCount:Number",
            "summary.invalidIgnores:Array",
            "summary.invalidIgnores[]:String",
            "summary.minCoverage:Number",
            "summary.pairs:Number",
            "summary.paletteDegraded:Boolean",
            "summary.paletteDetail:String",
            "summary.paletteKeys:Number",
            "summary.paletteMode:String",
            "summary.parseErrorFiles:Array",
            "summary.parseErrorFiles[]:String",
            "summary.parseErrors:Number",
            "summary.singleTheme:Boolean",
            "summary.skipped:Number",
            "summary.strictPalette:Boolean",
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
            "toolVersion:String",
            "unresolvedSites:Array",
            "unresolvedSites[].file:String",
            "unresolvedSites[].line:Number",
            "unresolvedSites[].reason:String",
            "unresolvedSites[].value:String",
            "unresolvedSites[]:Object",
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

    // CLI 會帶上工具版本與退出碼；函式庫呼叫端沒給的話這兩欄是 null
    private static JsonElement JsonOf(Fixture fx)
        => JsonDocument.Parse(Report.ToJson(fx.Run(), new ReportContext { ToolVersion = "1.0.0", ExitCode = 1 })).RootElement;

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

    [Fact] // docs/report.schema.json 與實際輸出一致：每一層物件的欄位一個不多一個不少、全部 required，列舉值與工具的標籤相同。
           // 形狀測試釘的是型別，這條釘的是「發佈出去的那份 schema 沒有說謊」
    public void PublishedJsonSchemaMatchesTheOutput()
    {
        var schema = JsonDocument.Parse(File.ReadAllText(Repo.PathOf("docs/report.schema.json"))).RootElement;
        Assert.Equal(6, schema.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());

        using var fx = Everything();
        var root = JsonOf(fx);
        void Same(JsonElement node, JsonElement actual, string path)
        {
            var props = node.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
            Assert.True(props.SequenceEqual(actual.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)), $"{path}: schema 的欄位與輸出不同");
            Assert.True(props.SequenceEqual(node.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal)), $"{path}: required 要列出全部欄位");
        }
        var p = schema.GetProperty("properties");
        Same(schema, root, "$");
        Same(p.GetProperty("summary"), root.GetProperty("summary"), "summary");
        Same(p.GetProperty("summary").GetProperty("properties").GetProperty("thresholds"), root.GetProperty("summary").GetProperty("thresholds"), "thresholds");
        Same(p.GetProperty("summary").GetProperty("properties").GetProperty("counts"), root.GetProperty("summary").GetProperty("counts"), "counts");
        Same(p.GetProperty("unresolvedSites").GetProperty("items"), root.GetProperty("unresolvedSites")[0], "unresolvedSites[]");
        Same(p.GetProperty("findings").GetProperty("items"), root.GetProperty("findings")[0], "findings[]");

        string?[] EnumOf(JsonElement e) => e.GetProperty("enum").EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray();
        var finding = p.GetProperty("findings").GetProperty("items").GetProperty("properties");
        Assert.Equal(new[] { Category.Ok, Category.Warn, Category.Fail, Category.Decorative }.Select(Report.CategoryLabel), EnumOf(finding.GetProperty("category")));
        IEnumerable<string?> symmetry = [.. new[] { Symmetry.BothLow, Symmetry.DarkFails, Symmetry.LightFails, Symmetry.SingleTheme }.Select(Report.SymmetryLabel), null];
        Assert.Equal(symmetry, EnumOf(finding.GetProperty("symmetry")));
        IEnumerable<string?> baseline = ["known", "new", "worsened", null];
        Assert.Equal(baseline, EnumOf(finding.GetProperty("baseline")));
    }

    /// <summary>開放集在 schema 裡的寫法：description 裡「Current values:」之後以反引號列出的值。</summary>
    internal static string[] DocumentedValues(JsonElement node)
    {
        var d = node.GetProperty("description").GetString()!;
        var at = d.IndexOf("Current values:", StringComparison.Ordinal);
        Assert.True(at >= 0, $"開放集的 description 要有「Current values:」：{d}");
        return System.Text.RegularExpressions.Regex.Matches(d[at..], "`(?<v>[^`]+)`").Select(m => m.Groups["v"].Value).ToArray();
    }

    [Fact] // 開放集（審查 1.0 A1、B1）：unresolved 原因、paletteMode、failOn（以後可能有 --fail-on none）、
           // theme（以後可能有 HighContrast 主題）在 1.x 可以加新值 —— schema 不能寫成封閉的 enum，
           // 不然用 schema 驗證的消費端，在 1.y 加第一個新值那天就紅。型別是 string、目前的值列在 description，
           // 實作會輸出的每一個值都要列在文件上（實作 ⊆ 文件）。category、symmetry、baseline 維持封閉
    public void OpenSetsAreStringsThatDocumentTheirCurrentValues()
    {
        var schema = JsonDocument.Parse(File.ReadAllText(Repo.PathOf("docs/report.schema.json"))).RootElement;
        var summary = schema.GetProperty("properties").GetProperty("summary").GetProperty("properties");
        var finding = schema.GetProperty("properties").GetProperty("findings").GetProperty("items").GetProperty("properties");
        foreach (var (node, emitted, nullable) in new (JsonElement, IEnumerable<string>, bool)[]
        {
            (schema.GetProperty("$defs").GetProperty("reason"), System.Enum.GetValues<UnresolvedReason>().Select(Report.ReasonLabel), false),
            (summary.GetProperty("paletteMode"), System.Enum.GetValues<PaletteMode>().Select(Report.ModeLabel), false),
            (summary.GetProperty("failOn"), ["warn", "fail"], false),
            (finding.GetProperty("theme"), System.Enum.GetValues<ThemeSide>().Select(Report.ThemeLabel), true),
        })
        {
            Assert.False(node.TryGetProperty("enum", out _), "開放集不能是 enum");
            var type = node.GetProperty("type");
            Assert.Equal(nullable ? "[\"string\",\"null\"]" : "\"string\"", type.GetRawText().Replace(" ", ""));
            Assert.Contains("open set", node.GetProperty("description").GetString());
            Assert.Subset(DocumentedValues(node).ToHashSet(), emitted.ToHashSet());
        }
        foreach (var closed in new[] { finding.GetProperty("category"), finding.GetProperty("symmetry"), finding.GetProperty("baseline") })
            Assert.True(closed.TryGetProperty("enum", out _), "封閉集照舊是 enum");
    }

    [Fact] // 6：每一個 finding 的每一個欄位都在 —— 沒有值的是 null（symmetry 在 ok／decorative、fontSize 在沒設字級時）。
           // fontSize 是 DIP 的數字（不取整；0.x 是 "12px" 字串、銀行家捨入）
    public void EveryFindingFieldIsPresentAndNullWhenItDoesNotApply()
    {
        using var fx = Everything();
        var findings = JsonOf(fx).GetProperty("findings").EnumerateArray().ToList();
        string[] fields = ["file", "line", "element", "fg", "bg", "ratioDark", "ratioLight", "threshold", "category",
                           "symmetry", "isText", "isLargeText", "fontSize", "theme", "baseline"];
        foreach (var f in findings)
        {
            Assert.Equal(fields, f.EnumerateObject().Select(p => p.Name));
            var category = f.GetProperty("category").GetString();
            Assert.Equal(category is "fail" or "warn", f.GetProperty("symmetry").ValueKind == JsonValueKind.String);
        }
        var sized = Assert.Single(findings, f => f.GetProperty("fontSize").ValueKind == JsonValueKind.Number);
        Assert.Equal(12, sized.GetProperty("fontSize").GetDouble());
    }

    [Fact] // 6 的空值政策：一律輸出 —— 沒資料的清單是 []、字典是 {}（0.x 有的省略、有的輸出 {}，消費端得兩種都處理）
    public void EmptyCollectionsArePresentAndEmpty()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid Background="#000000">
              <TextBlock Foreground="#FFFFFF" Text="21:1"/>
            </Grid>
            """);
        var root = JsonOf(fx);
        Assert.Equal(0, root.GetProperty("unresolvedSites").GetArrayLength());
        Assert.False(root.TryGetProperty("unresolved", out _)); // 6 改名，不再與 summary.unresolved 同名
        var summary = root.GetProperty("summary");
        Assert.Empty(summary.GetProperty("unknownKeys").EnumerateObject());
        Assert.Equal(0, summary.GetProperty("parseErrorFiles").GetArrayLength());
        Assert.Equal(0, summary.GetProperty("invalidIgnores").GetArrayLength());
        Assert.Equal(0, summary.GetProperty("invalidIgnoreCount").GetInt32());
        Assert.Empty(summary.GetProperty("unresolvedBy").EnumerateObject());
        Assert.False(summary.TryGetProperty("paletteSource", out _)); // 6 拿掉：由 paletteMode 推得
        Assert.Equal("none", summary.GetProperty("paletteMode").GetString());
        var f = Assert.Single(root.GetProperty("findings").EnumerateArray().ToList());
        Assert.Equal(JsonValueKind.Null, f.GetProperty("fontSize").ValueKind);
        Assert.Equal(JsonValueKind.Null, f.GetProperty("symmetry").ValueKind);
        Assert.Equal(JsonValueKind.Null, f.GetProperty("theme").ValueKind);
        Assert.Equal(JsonValueKind.Null, f.GetProperty("baseline").ValueKind);
    }

    [Fact] // 實際生效的 gate 寫進 summary：預設 gate 在 1.0 改了，讀報告的人要知道這份結果是用哪把尺量的
    public void SummaryCarriesTheGateInEffect()
    {
        using var fx = new Fixture().File("Main.xaml", """<Grid Background="#000000"><TextBlock Foreground="#FFFFFF"/></Grid>""");
        var lib = JsonDocument.Parse(Report.ToJson(fx.Run())).RootElement;
        Assert.Equal("warn", lib.GetProperty("summary").GetProperty("failOn").GetString());
        Assert.Equal(50, lib.GetProperty("summary").GetProperty("minCoverage").GetDouble());
        Assert.False(lib.GetProperty("summary").GetProperty("strictPalette").GetBoolean());
        Assert.Equal(JsonValueKind.Null, lib.GetProperty("toolVersion").ValueKind); // 函式庫呼叫沒給
        Assert.Equal(JsonValueKind.Null, lib.GetProperty("exitCode").ValueKind);

        var json = Path.Combine(fx.Root, "r.json");
        var cli = Cli.Run(fx.Root, "--json", json, "--fail-on", "fail", "--min-coverage", "30", "--strict-palette");
        Assert.True(cli.Exit == 3, cli.ToString()); // 寫死色碼的專案：色盤退化
        var s = Cli.ReadJson(json);
        Assert.Equal(3, s.GetProperty("exitCode").GetInt32());
        Assert.Equal("fail", s.GetProperty("summary").GetProperty("failOn").GetString());
        Assert.Equal(30, s.GetProperty("summary").GetProperty("minCoverage").GetDouble());
        Assert.True(s.GetProperty("summary").GetProperty("strictPalette").GetBoolean());
        Assert.Matches(@"^\d+\.\d+\.\d+", s.GetProperty("toolVersion").GetString());
    }

    [Theory] // 色盤退化要機器可讀（審查 1.0 B4）：summary.paletteDegraded。之前只能從 paletteMode／paletteDetail 的字串猜，
             // README 說「工具沒看到的東西都是機器可讀的」卻少了這一項。config 明選 mode:none 是使用者的決定，不算退化
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(true, """{ "palette": { "mode": "none" } }""", false)]
    public void SummaryCarriesWhetherPaletteDetectionDegraded(bool hardcodedOnly, string? config, bool degraded)
    {
        using var fx = hardcodedOnly ? CliProjects.Passing() : CliProjects.Passing().WithPalette();
        if (config is not null) fx.File(ToolConfig.FileName, config);
        var detection = PaletteDetector.Detect(fx.Root, ToolConfig.Load(fx.Root));
        var r = Auditor.Run(fx.Root, detection, ToolConfig.Load(fx.Root));
        var s = JsonDocument.Parse(Report.ToJson(r)).RootElement.GetProperty("summary");
        Assert.Equal(degraded, s.GetProperty("paletteDegraded").GetBoolean());
        Assert.Equal(detection.IsDegraded, degraded);
    }

    [Fact] // 只屬於一個主題的配對（Dark.xaml 的 Style、依主題解析到的 @dark／@light 鏈）帶 theme：ratioLight 是那個主題的值
    public void OneThemeFindingsNameTheirTheme()
    {
        using var fx = new Fixture();
        fx.File("Themes/Dark.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#111111"/><SolidColorBrush x:Key="Fg" Color="#EEEEEE"/><SolidColorBrush x:Key="Line" Color="#333333"/>
              <Style x:Key="Chip" TargetType="Button"><Setter Property="Background" Value="{DynamicResource Bg}"/><Setter Property="Foreground" Value="#EEEEEE"/></Style>
            </ResourceDictionary>
            """);
        fx.File("Themes/Light.xaml", """
            <ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="Bg" Color="#FFFFFF"/><SolidColorBrush x:Key="Fg" Color="#1B1B1B"/><SolidColorBrush x:Key="Line" Color="#CCCCCC"/>
            </ResourceDictionary>
            """);
        fx.File("Views/Main.xaml", """<Grid Background="{DynamicResource Bg}"><TextBlock Foreground="{DynamicResource Fg}"/></Grid>""");
        var findings = JsonOf(fx).GetProperty("findings").EnumerateArray().ToList();
        Assert.Equal("dark", Assert.Single(findings, f => f.GetProperty("file").GetString() == "Themes/Dark.xaml").GetProperty("theme").GetString());
        Assert.Equal(JsonValueKind.Null, Assert.Single(findings, f => f.GetProperty("file").GetString() == "Views/Main.xaml").GetProperty("theme").ValueKind);
    }

    [Fact] // --baseline 模式下，gate 擋的每一筆帶 known／new／worsened（Action 的註記才分得出既有債與新問題）
    public void BaselineModeMarksEachGatedFinding()
    {
        using var fx = new Fixture().File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="known"/>
            </Grid>
            """);
        var baseline = Path.Combine(fx.Root, "baseline.json");
        Assert.Equal(0, Cli.Run(fx.Root, "--write-baseline", baseline).Exit);
        fx.File("Main.xaml", """
            <Grid Background="#FFFFFF">
              <TextBlock Foreground="#EEEEEE" Text="known"/>
              <TextBlock Foreground="#DDDDDD" Text="new"/>
              <TextBlock Foreground="#000000" Text="ok"/>
            </Grid>
            """);
        var json = Path.Combine(fx.Root, "r.json");
        Assert.Equal(1, Cli.Run(fx.Root, "--baseline", baseline, "--json", json).Exit);
        var marks = Cli.ReadJson(json).GetProperty("findings").EnumerateArray()
            .Select(f => $"{f.GetProperty("fg").GetString()}={(f.GetProperty("baseline").ValueKind == JsonValueKind.Null ? "null" : f.GetProperty("baseline").GetString())}")
            .ToArray();
        Assert.Equal(["#EEEEEE=known", "#DDDDDD=new", "#000000=null"], marks);
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
        Assert.Equal(unresolved, root.GetProperty("unresolvedSites").GetArrayLength());
        Assert.Equal(s.GetProperty("unresolvedBy").GetProperty("unknown-palette-key").GetInt32(),
                     s.GetProperty("unknownKeys").EnumerateObject().Sum(p => p.Value.GetInt32()));
        Assert.Equal(N(s, "parseErrors"), s.GetProperty("parseErrorFiles").GetArrayLength());

        var pairs = N(s, "pairs");
        Assert.Equal(Math.Round(100.0 * pairs / (pairs + unresolved), 1, MidpointRounding.AwayFromZero), s.GetProperty("coverage").GetDouble());

        // 這個素材每個計數器都非零 —— 恆等式不是在比 0 == 0
        Assert.All(new[] { "pairs", "unresolved", "suppressed", "skipped", "deadForeground", "disabledExempt", "parseErrors" },
            name => Assert.True(N(s, name) > 0, name));
        Assert.Equal(1, s.GetProperty("invalidIgnores").GetArrayLength());
        Assert.Equal(1, N(s, "invalidIgnoreCount"));
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
        var sites = JsonOf(fx).GetProperty("unresolvedSites").EnumerateArray()
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
        Assert.Equal("none", b.GetProperty("paletteMode").GetString());
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
             "translucent-uncomposited", "same-brush-pair", "over-sibling-content", "in-style-template"],
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

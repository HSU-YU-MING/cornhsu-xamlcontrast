using System.Text.Json;
using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 轉換器回傳的固定顏色（1.2，config 的 converters.constantBrushes，預設關）。
/// 第一半是 C# 的分析（ConverterIndex.Analyze）：每一條回傳路徑都是固定的 Brush 字面值才算，任何一條不是就整個不算 ——
/// 不猜。第二半是稽核：每個候選色各評一次（一個狀態一組）、計數器（converterPairs）、哪些情況照舊看不懂。
/// 素材是說明裡的合成範例與自己寫的最小變形，沒有用任何真實專案的程式碼。
/// </summary>
public class ConverterTests
{
    // 說明裡的合成範例（選到時會變色的工具列按鈕）
    private const string ActiveBrush = """
        using System;
        using System.Globalization;
        using System.Windows.Data;
        using System.Windows.Media;

        namespace Demo;

        public class ActiveBrushConverter : IValueConverter {
            private static readonly SolidColorBrush On  = new(Color.FromRgb(0x53, 0x4A, 0xB7));
            private static readonly SolidColorBrush Off = new(Color.FromRgb(0x2A, 0x2A, 0x2A));
            public object Convert(object v, Type t, object p, CultureInfo c) => Equals(v?.ToString(), p) ? On : Off;
            public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
        }
        """;

    private static ConverterInfo Analyze(string source) => Assert.Single(ConverterIndex.Analyze(source, "C.cs"));

    private static string Converter(string members, string convert, string name = "C") => $$"""
        public class {{name}} : IValueConverter
        {
        {{members}}
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture){{convert}}
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
        """;

    // ── C# 的分析 ──

    [Fact] // 說明裡的範例：兩個 static readonly 欄位、運算式主體的三元運算 → 兩個候選，標籤是欄位名
    public void TheSyntheticExampleHasTwoFixedColours()
    {
        var info = Analyze(ActiveBrush);
        Assert.Equal("ActiveBrushConverter", info.Name);
        Assert.Null(info.NotFixedBecause);
        Assert.Equal([new ConverterCandidate("On", "#534AB7"), new ConverterCandidate("Off", "#2A2A2A")], info.Candidates);
    }

    [Theory] // 認得的寫法：每一條回傳路徑都是固定的 Brush 字面值
    [InlineData("", """
         { if (value is true) return Brushes.White; return Brushes.Black; }
        """, "Brushes.White=#FFFFFF|Brushes.Black=#000000")]
    [InlineData("", """
         => value switch { "Pen" => Brushes.Red, "Eraser" => new SolidColorBrush(Colors.Blue), _ => Brushes.Transparent };
        """, "Brushes.Red=#FF0000|Colors.Blue=#0000FF|Brushes.Transparent=Transparent")]
    [InlineData("", """
         => (bool)value ? new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)) : (Brush)new SolidColorBrush(Color.FromArgb(255, 18, 52, 86));
        """, "#80000000=#80000000|#123456=#123456")]
    [InlineData("""
            public static Brush Accent { get; } = new SolidColorBrush(Color.FromRgb(1, 2, 3));
            private static SolidColorBrush Dim => new(Colors.Gray);
            private readonly Color _base = Color.FromRgb(0x10, 0x20, 0x30);
            private readonly SolidColorBrush _plain;
        """, """
         { switch (value) { case 1: return Accent; case 2: return this.Dim; default: return new SolidColorBrush(_base); } }
        """, "Accent=#010203|Dim=#808080|_base=#102030")]
    [InlineData("""
            // private static readonly SolidColorBrush Old = new(Colors.Red);  ← 註解掉的不算
            private static readonly SolidColorBrush On = new(Colors.White); /* return null; */
        """, """
         { var s = "return null; } {"; return value is null ? On : C.On; }
        """, "On=#FFFFFF")]
    public void FixedBrushesAreRecognised(string members, string convert, string expected)
    {
        var info = Analyze(Converter(members, convert));
        Assert.True(info.Candidates is not null, info.NotFixedBecause);
        Assert.Equal(expected, string.Join("|", info.Candidates!.Select(c => $"{c.Label}={c.Value}")));
    }

    [Theory] // 任何一條回傳路徑不是固定字面值 → 整個不算（照舊 bound-or-gradient），而且說得出為什麼
    [InlineData("", " => value is true ? Brushes.Red : DependencyProperty.UnsetValue;", "UnsetValue")]
    [InlineData("", " => value is true ? Brushes.Red : Binding.DoNothing;", "DoNothing")]
    [InlineData("", " => value is true ? Brushes.Red : null;", "null")]
    [InlineData("", " => value;", "computed")]
    [InlineData("", " => value as Brush ?? Brushes.Red;", "computed")]
    [InlineData("", " => value is Color c ? new SolidColorBrush(c) : Brushes.Red;", "c is not")]
    [InlineData("", " => new SolidColorBrush(Color.FromRgb((byte)value, 0, 0));", "literal number")]
    [InlineData("", " => new SolidColorBrush(Colors.Red) { Opacity = 0.5 };", "object initializer")]
    [InlineData("", " => CreateBrush(value);", "computed")]
    [InlineData("", " => Theme.Accent;", "computed")]
    [InlineData("", " => (Brush)Application.Current.Resources[\"Accent\"];", "computed")]
    [InlineData("    private static SolidColorBrush On = new(Colors.Red);\n", " => On;", "not readonly")]
    [InlineData("    private readonly SolidColorBrush On = new(Colors.Red);\n    public C(Color c) { On = new SolidColorBrush(c); }\n", " => On;", "assigned somewhere else")]
    [InlineData("    public Brush On { get; set; } = Brushes.Red;\n", " => On;", "not a readonly field")]
    [InlineData("", " { Func<Brush> f = () => Brushes.Red; return f(); }", "lambda")]
    [InlineData("", " { return Brushes.Red; }\n    public object Convert(object[] values, Type t, object p, CultureInfo c) { return Brushes.Blue; }", "more than one Convert")]
    public void AnythingElseIsNotFixed(string members, string convert, string why)
    {
        var info = Analyze(Converter(members, convert));
        Assert.Null(info.Candidates);
        Assert.Contains(why, info.NotFixedBecause, StringComparison.Ordinal);
    }

    [Fact] // 只看實作 IValueConverter 的類別；partial class 的 Convert 可能在另一個檔 → 不算
    public void OnlyWholeValueConverterClassesAreAnalysed()
    {
        var infos = ConverterIndex.Analyze("""
            public class NotAConverter { public object Convert(object v) => Brushes.Red; }
            public partial class Split : IValueConverter { }
            public sealed class Multi : IMultiValueConverter { public object Convert(object[] v, Type t, object p, CultureInfo c) => Brushes.Red; }
            """, "C.cs").ToList();
        var split = Assert.Single(infos);
        Assert.Equal("Split", split.Name);
        Assert.Null(split.Candidates);
        Assert.Contains("partial", split.NotFixedBecause);
    }

    [Fact] // 註解與字串的內容不影響括號比對與行號
    public void MaskKeepsLengthAndLinesAndHidesCommentsAndStrings()
    {
        const string src = "a /* } { */ b // { x\n\"{ ; }\" 'x' @\"q\"\"{\" \"\"\"\nraw {\n\"\"\" c";
        var masked = ConverterIndex.Mask(src);
        Assert.Equal(src.Length, masked.Length);
        Assert.Equal(src.Count(c => c == '\n'), masked.Count(c => c == '\n'));
        Assert.DoesNotContain('{', masked.Replace("raw", ""));
        Assert.EndsWith(" c", masked, StringComparison.Ordinal);
    }

    // ── 稽核 ──

    private const string Config = """{ "converters": { "constantBrushes": true } }""";

    /// <summary>深灰底的視窗，ActiveBrush／其他轉換器宣告在資源裡；<paramref name="body"/> 放進 StackPanel。</summary>
    private static Fixture Project(string body, string? config = Config, string cs = ActiveBrush, string resources = "")
    {
        var fx = new Fixture();
        fx.File("Converters.cs", cs);
        if (config is not null) fx.File(ToolConfig.FileName, config);
        fx.File("Main.xaml", $$"""
            <Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Demo" Background="#1E1E1E">
              <Window.Resources>
                <local:ActiveBrushConverter x:Key="ActiveBrush"/>
                {{resources}}
              </Window.Resources>
              <StackPanel>
                {{body}}
              </StackPanel>
            </Window>
            """);
        return fx;
    }

    private static AuditResult Run(Fixture fx) => Auditor.Run(fx.Root, PaletteDetector.Detect(fx.Root), ToolConfig.Load(fx.Root));

    private const string PenButton = """<Button Background="{Binding ActiveTool, Converter={StaticResource ActiveBrush}, ConverterParameter=Pen}" Foreground="#FFFFFF" Content="Pen"/>""";

    [Fact] // 預設關：跟 1.1 一模一樣 —— bound-or-gradient、converterPairs 0、不列轉換器
    public void OffByDefaultNothingChanges()
    {
        using var fx = Project(PenButton, config: null);
        var r = Run(fx);
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);
        Assert.Equal(0, r.ConverterPairs);
        Assert.Null(r.ConvertersUsed);
        Assert.DoesNotContain("converter colours", Report.ToConsole(r), StringComparison.Ordinal);
    }

    [Fact] // 打開：每個候選色各評一次，底寫成「資源鍵→欄位名」；converterPairs 數得出來，unresolved 少掉那一筆
    public void EachFixedColourIsGradedOnce()
    {
        using var fx = Project(PenButton);
        var r = Run(fx);
        Assert.Equal(0, r.Unresolved);
        Assert.Equal(2, r.Pairs);
        Assert.Equal(2, r.ConverterPairs);
        Assert.Equal(["ActiveBrush→On", "ActiveBrush→Off"], r.Findings.Select(f => f.Bg));
        Assert.Equal([Wcag.Contrast("#FFFFFF", "#534AB7"), Wcag.Contrast("#FFFFFF", "#2A2A2A")], r.Findings.Select(f => f.RatioDark));
        Assert.All(r.Findings, f => Assert.Equal("#FFFFFF", f.Fg));

        using var json = JsonDocument.Parse(Report.ToJson(r));
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("converterPairs").GetInt32());
        Assert.Contains("converter colours: 2 pair(s) graded once per fixed colour of 1 converter(s) (ActiveBrush: 2)", Report.ToConsole(r), StringComparison.Ordinal);
    }

    [Fact] // 子元素的字也一樣：Border 的底來自轉換器，裡面的 TextBlock 每個候選各一組；中間再畫一層不透明的底就跟轉換器無關
    public void ChildTextIsGradedOnEachColourUntilSomethingOpaqueCoversIt()
    {
        using var fx = Project("""
            <Border Background="{Binding ActiveTool, Converter={StaticResource ActiveBrush}, ConverterParameter=Eraser}">
              <StackPanel>
                <TextBlock Foreground="#9A9A9A" Text="on the converter's colour"/>
                <Border Background="#FFFFFF"><TextBlock Foreground="#000000" Text="on white"/></Border>
              </StackPanel>
            </Border>
            """);
        var r = Run(fx);
        Assert.Equal(["ActiveBrush→On", "ActiveBrush→Off", "#FFFFFF"], r.Findings.Select(f => f.Bg));
        Assert.Equal(2, r.ConverterPairs);
        Assert.Equal(3, r.Pairs);
    }

    [Fact] // Transparent 的候選：那個狀態看到的是後面的底；半透明的候選疊在後面的底上；元素的 Opacity 群組照樣算
    public void TransparentAndTranslucentCandidatesAreCompositedOverWhatIsBehind()
    {
        const string cs = """
            public class SoftConverter : IValueConverter {
                public object Convert(object v, Type t, object p, CultureInfo c) => v is true ? new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
            }
            """;
        using var fx = Project("""
            <Border Background="#000000">
              <StackPanel>
                <Border Background="{Binding A, Converter={StaticResource Soft}}"><TextBlock Foreground="#FFFFFF" Text="x"/></Border>
                <Border Opacity="0.5" Background="{Binding A, Converter={StaticResource Soft}}"><TextBlock Foreground="#FFFFFF" Text="faded"/></Border>
              </StackPanel>
            </Border>
            """, cs: cs, resources: """<local:SoftConverter x:Key="Soft"/>""");
        var r = Run(fx);
        Assert.Equal(0, r.Unresolved);
        Assert.Equal(4, r.ConverterPairs);
        var plain = r.Findings.Where(f => f.Line == r.Findings.Min(x => x.Line)).ToList();
        var halfWhiteOverBlack = Wcag.Composite("#FFFFFF", 0x80 / 255.0, "#000000");
        Assert.Equal([Wcag.Contrast("#FFFFFF", halfWhiteOverBlack), Wcag.Contrast("#FFFFFF", "#000000")], plain.Select(f => f.RatioDark));
        Assert.Equal(["Soft→#80FFFFFF over #000000", "#000000"], plain.Select(f => f.Bg));
        // Opacity 0.5 的那一層：底與字一起變淡再疊到黑底上（群組照原本的算法算）—— 兩個狀態各一組、比值都比沒變淡的低
        var faded = r.Findings.Except(plain).ToList();
        Assert.Equal(2, faded.Count);
        Assert.All(faded.Zip(plain), p => Assert.True(p.First.RatioDark < p.Second.RatioDark, $"{p.First.RatioDark} vs {p.Second.RatioDark}"));
    }

    [Fact] // 字色來自轉換器、底是一個不透明的顏色 → 每個候選字色各一組（標籤在 fg）
    public void ForegroundCandidatesAreGradedOnASolidBackground()
    {
        using var fx = Project("""<TextBlock Foreground="{Binding ActiveTool, Converter={StaticResource ActiveBrush}}" Text="x"/>""");
        var r = Run(fx);
        Assert.Equal(["ActiveBrush→On", "ActiveBrush→Off"], r.Findings.Select(f => f.Fg));
        Assert.All(r.Findings, f => Assert.Equal("#1E1E1E", f.Bg));
        Assert.Equal(2, r.ConverterPairs);
    }

    [Theory] // 照舊看不懂（bound-or-gradient，一筆）：字與底都來自轉換器（哪個配哪個不知道，全部組合裡有不存在的）、
             // 有 FallbackValue／TargetNullValue（執行期還有別的顏色）、半透明的一層疊在轉換器的底上（展開時拿不回那一層）
    [InlineData("""<Border Background="{Binding A, Converter={StaticResource ActiveBrush}}"><TextBlock Foreground="{Binding B, Converter={StaticResource ActiveBrush}}" Text="x"/></Border>""")]
    [InlineData("""<Border Background="{Binding A, Converter={StaticResource ActiveBrush}, FallbackValue=Red}"><TextBlock Foreground="#FFFFFF" Text="x"/></Border>""")]
    [InlineData("""<Border Background="{Binding A, Converter={StaticResource ActiveBrush}, TargetNullValue=Red}"><TextBlock Foreground="#FFFFFF" Text="x"/></Border>""")]
    [InlineData("""<Border Background="{Binding A, Converter={StaticResource ActiveBrush}}"><Border Background="#80FFFFFF"><TextBlock Foreground="#FFFFFF" Text="x"/></Border></Border>""")]
    [InlineData("""<Border Background="{Binding A, Converter={StaticResource NoSuchConverter}}"><TextBlock Foreground="#FFFFFF" Text="x"/></Border>""")]
    public void UnknownCombinationsStayUnresolved(string body)
    {
        using var fx = Project(body);
        var r = Run(fx);
        Assert.Empty(r.Findings);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);
        Assert.Equal(0, r.ConverterPairs);
    }

    [Fact] // 不是固定顏色的轉換器：照舊 bound-or-gradient，console 點名它與原因
    public void AConverterThatIsNotFixedIsNamed()
    {
        const string cs = ActiveBrush + """

            public class StatusConverter : IValueConverter {
                public object Convert(object v, Type t, object p, CultureInfo c) => v is int n && n > 0 ? Brushes.Red : DependencyProperty.UnsetValue;
                public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
            }
            """;
        using var fx = Project("""<Border Background="{Binding Errors, Converter={StaticResource Status}}"><TextBlock Foreground="#FFFFFF" Text="x"/></Border>""",
            cs: cs, resources: """<local:StatusConverter x:Key="Status"/>""");
        var r = Run(fx);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);
        var use = Assert.Single(r.ConvertersUsed!);
        Assert.Equal("Status", use.Key);
        Assert.Contains("not fixed, still unresolved: Status (StatusConverter, Converters.cs:", Report.ToConsole(r), StringComparison.Ordinal);
        Assert.Contains("returns DependencyProperty.UnsetValue", Report.ToConsole(r), StringComparison.Ordinal);
    }

    [Fact] // Style 的 Setter 也一樣：定義處（字與底都出自這個 Style）每個候選一組；元素套 Style、本地 Background 是轉換器時走鏈路徑
    public void StyleSettersAndStyledElementsUseTheCandidates()
    {
        using var fx = Project("""
            <Button Style="{StaticResource Tool}" Content="defined in the style"/>
            <Button Style="{StaticResource Plain}" Background="{Binding A, Converter={StaticResource ActiveBrush}}" Content="local background"/>
            """, resources: """
            <Style x:Key="Tool" TargetType="Button">
              <Setter Property="Background" Value="{Binding A, Converter={StaticResource ActiveBrush}}"/>
              <Setter Property="Foreground" Value="#FFFFFF"/>
            </Style>
            <Style x:Key="Plain" TargetType="Button"><Setter Property="Foreground" Value="#EEEEEE"/></Style>
            """);
        var r = Run(fx);
        Assert.Equal(0, r.Unresolved);
        Assert.Equal(["Button⊕Style[Plain]/base ActiveBrush→Off", "Button⊕Style[Plain]/base ActiveBrush→On",
                      "Style[Tool]/base ActiveBrush→Off", "Style[Tool]/base ActiveBrush→On"],
            r.Findings.Select(f => $"{f.Element} {f.Bg}").Order(StringComparer.Ordinal));
        Assert.Equal(4, r.ConverterPairs);
    }

    [Fact] // 兩個不同的候選是兩個 baseline 鍵（底的寫法不同）；鍵裡是欄位名不是色碼 —— C# 裡把顏色調暗，鍵不變、worst 抓得到惡化
    public void EachCandidateHasItsOwnStableBaselineKey()
    {
        using var fx = Project("""<Button Background="{Binding A, Converter={StaticResource ActiveBrush}}" Foreground="#3A3A3A" Content="x"/>""");
        var r = Run(fx);
        var keys = r.Findings.Select(f => $"{f.File}|{f.Element}|{f.Fg}|{f.Bg}").ToList();
        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(keys, k => Assert.DoesNotContain("#534AB7", k, StringComparison.Ordinal));
    }

    [Fact] // 同名的類別在兩個命名空間 → 不知道 XAML 用的是哪一個 → 不算
    public void AmbiguousClassNamesAreNotFixed()
    {
        using var fx = Project(PenButton);
        fx.File("Other/Converters.cs", ActiveBrush.Replace("namespace Demo;", "namespace Other;"));
        var r = Run(fx);
        Assert.Equal(1, r.UnresolvedBy[UnresolvedReason.BoundOrGradient]);
        Assert.Contains("more than one class is named ActiveBrushConverter", Assert.Single(r.ConvertersUsed!).Info.NotFixedBecause);
    }

    [Theory] // config：converters 要是物件、constantBrushes 要是 true／false、不認得的屬性是設定錯誤（exit 2 的那一種）
    [InlineData("""{ "converters": null }""", "converters must be an object")]
    [InlineData("""{ "converters": { "constantBrushes": "yes" } }""", "converters.constantBrushes must be true or false")]
    [InlineData("""{ "converters": { "constantBrush": true } }""", "unknown property 'converters.constantBrush'")]
    public void TheConfigSectionIsValidated(string json, string message)
    {
        using var fx = new Fixture();
        fx.File(ToolConfig.FileName, json);
        var ex = Assert.Throws<ConfigException>(() => ToolConfig.Load(fx.Root));
        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }
}

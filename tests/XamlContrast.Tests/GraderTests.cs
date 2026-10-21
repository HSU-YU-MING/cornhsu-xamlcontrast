using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>
/// 分級（絕對對比）與對稱（跨主題）的邊界 —— 兩個獨立維度（Grader.cs）。
/// 直接餵比值給 Grader，邊界才能精確到小數第二位；JSON 的 category／symmetry 就是這裡的輸出。
/// </summary>
public class GraderTests
{
    private static Finding Grade(double dark, double light, double need, bool singleTheme = false)
    {
        var palette = new Palette();
        palette.Entries["K"] = singleTheme ? ("#111111", "#111111") : ("#111111", "#FAFAFA");
        var f = new Finding
        {
            File = "Main.xaml",
            Element = "TextBlock",
            Fg = "#FFFFFF",
            Bg = "#000000",
            RatioDark = dark,
            RatioLight = light,
            Need = need,
            IsText = need > 0,
        };
        var result = new AuditResult
        {
            Detection = new PaletteDetection
            {
                Palette = palette,
                Mode = PaletteMode.Pair,
                ExcludedFiles = [],
                Description = "test",
            },
            FileCount = 1,
            Findings = [f],
            Pairs = 1,
            Unresolved = 0,
            UnresolvedBy = new(),
            UnresolvedSites = [],
            Skipped = 0,
            DeadForeground = 0,
            DisabledExempt = 0,
            ParseErrors = [],
            InvalidIgnores = [],
        };
        Grader.Grade(result);
        return f;
    }

    [Theory] // ok：最差一邊 ≥ 門檻（恰等於算過）；fail：< 門檻 × failFactor（預設 0.67 → 3.015／2.01）；其間 warn；門檻 0 = 裝飾
    [InlineData(4.5, 4.5, Category.Ok)]
    [InlineData(4.49, 4.5, Category.Warn)]
    [InlineData(3.02, 4.5, Category.Warn)]
    [InlineData(3.01, 4.5, Category.Fail)]
    [InlineData(3.0, 3.0, Category.Ok)]
    [InlineData(2.02, 3.0, Category.Warn)]
    [InlineData(2.0, 3.0, Category.Fail)]
    [InlineData(1.0, 0.0, Category.Decorative)]
    public void CategoryBoundaries(double worst, double need, Category expected)
        => Assert.Equal(expected, Grade(worst, worst, need).Category);

    [Fact] // 分級看「最差的那一邊」—— 深色 21:1 救不了淺色 1.5:1
    public void CategoryUsesTheWorseTheme()
        => Assert.Equal(Category.Fail, Grade(21.0, 1.5, 4.5).Category);

    [Theory] // 對稱只在 fail／warn 上分類；差距 < 1.5 = both-low（色票本身不夠），≥ 1.5 看哪一邊掉下去；
             // 單一主題專案一律 single-theme
    [InlineData(2.5, 2.5, false, Symmetry.BothLow)]
    [InlineData(3.99, 2.5, false, Symmetry.BothLow)]     // 差 1.49
    [InlineData(4.0, 2.5, false, Symmetry.LightFails)]   // 差 1.5 —— 不再算 both-low
    [InlineData(2.5, 4.0, false, Symmetry.DarkFails)]
    [InlineData(2.5, 9.0, true, Symmetry.SingleTheme)]
    [InlineData(21.0, 21.0, false, Symmetry.NotApplicable)] // 合格的配對沒有問題要救
    public void SymmetryBoundaries(double dark, double light, bool singleTheme, Symmetry expected)
    {
        var f = Grade(dark, light, 4.5, singleTheme);
        Assert.Equal(expected, f.Symmetry);
        Assert.Equal(Math.Round(Math.Abs(dark - light), 2), f.Gap);
    }

    [Fact] // 裝飾元素同理：不適用 AA，就沒有對稱可言
    public void DecorativeHasNoSymmetry()
        => Assert.Equal(Symmetry.NotApplicable, Grade(1.0, 3.0, 0.0).Symmetry);
}

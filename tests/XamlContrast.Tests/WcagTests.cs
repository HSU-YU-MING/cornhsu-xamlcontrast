using XamlContrast.Core;

namespace XamlContrast.Tests;

public class WcagTests
{
    [Fact]
    public void BlackOnWhiteIs21() => Assert.Equal(21.0, Wcag.Contrast("#000000", "#FFFFFF"));

    [Fact]
    public void SameColorIs1() => Assert.Equal(1.0, Wcag.Contrast("#42A5F5", "#42A5F5"));

    [Fact]
    public void KnownAaBoundary()
    {
        // #767676 疊白是 WCAG 常引用的 AA 臨界例（≈4.54:1）
        Assert.Equal(4.54, Wcag.Contrast("#767676", "#FFFFFF"));
    }

    [Fact]
    public void ArgbStripsAlphaForLuminance()
    {
        // 8 位 hex 的亮度只看 RGB —— alpha 的合成是呼叫端的責任
        Assert.Equal(Wcag.Contrast("#FF0000", "#FFFFFF"), Wcag.Contrast("#80FF0000", "#FFFFFF"));
    }

    [Fact]
    public void CompositeHalfBlackOverWhiteIsMidGray()
    {
        // 0.5 黑疊白 ≈ #808080（四捨五入）
        Assert.Equal("#808080", Wcag.Composite("#000000", 0.5, "#FFFFFF"));
    }

    [Fact]
    public void CompositeAlphaZeroIsBackground()
        => Assert.Equal("#FFFFFF", Wcag.Composite("#000000", 0.0, "#FFFFFF"));

    [Fact]
    public void CompositeAlphaOneIsForeground()
        => Assert.Equal("#000000", Wcag.Composite("#000000", 1.0, "#FFFFFF"));
}

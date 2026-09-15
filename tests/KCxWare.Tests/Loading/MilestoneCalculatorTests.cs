using KCxWare.Core.Loading;

namespace KCxWare.Tests.Loading;

public class MilestoneCalculatorTests
{
    [Theory]
    [InlineData(20, 20, true)]
    [InlineData(20, 19, false)]
    [InlineData(40, 40, true)]
    [InlineData(40, 39, false)]
    [InlineData(60, 60, true)]
    [InlineData(80, 80, true)]
    [InlineData(100, 100, true)]
    [InlineData(100, 99, false)]
    public void IsActive_ReflectsThresholdBoundary(int threshold, int progress, bool expected)
    {
        Assert.Equal(expected, MilestoneCalculator.IsActive(threshold, progress, indeterminate: false));
    }

    [Fact]
    public void IsActive_CumulativeAtEachBoundary()
    {
        // At 60% progress, 20% and 40% must still be lit alongside 60%; 80% and 100% must not.
        Assert.True(MilestoneCalculator.IsActive(20, 60, false));
        Assert.True(MilestoneCalculator.IsActive(40, 60, false));
        Assert.True(MilestoneCalculator.IsActive(60, 60, false));
        Assert.False(MilestoneCalculator.IsActive(80, 60, false));
        Assert.False(MilestoneCalculator.IsActive(100, 60, false));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(100)]
    public void IsActive_NeverLitWhileIndeterminate_EvenAtFullProgressValue(int threshold)
    {
        // Indeterminate operations must never report fabricated/measured milestone progress.
        Assert.False(MilestoneCalculator.IsActive(threshold, 100, indeterminate: true));
    }

    [Fact]
    public void All_AlternatesCyanMagentaAndEndsCombined()
    {
        Assert.Equal(MilestoneAccent.Cyan, MilestoneCalculator.All[0].Accent);
        Assert.Equal(MilestoneAccent.Magenta, MilestoneCalculator.All[1].Accent);
        Assert.Equal(MilestoneAccent.Cyan, MilestoneCalculator.All[2].Accent);
        Assert.Equal(MilestoneAccent.Magenta, MilestoneCalculator.All[3].Accent);
        Assert.Equal(MilestoneAccent.Combined, MilestoneCalculator.All[4].Accent);
    }
}

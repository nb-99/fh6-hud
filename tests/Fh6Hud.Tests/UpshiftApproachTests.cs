using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class UpshiftApproachTests
{
    private const float ShiftRpm = 6000f;
    private const float WindowStartRpm = 4800f; // 0.8 * 6000

    [Fact]
    public void NoTarget_ProducesNoCue()
    {
        Assert.False(UpshiftApproach.IsInWindow(ShiftRpm, null));
        Assert.Equal(0, UpshiftApproach.ActiveLightCount(ShiftRpm, null));
        Assert.Equal(0f, UpshiftApproach.NormalizedProgress(ShiftRpm, null));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-100f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidTargets_BehaveSafely(float target)
    {
        Assert.False(UpshiftApproach.IsInWindow(ShiftRpm, target));
        Assert.Equal(0, UpshiftApproach.ActiveLightCount(ShiftRpm, target));
        Assert.Equal(0f, UpshiftApproach.NormalizedProgress(ShiftRpm, target));
    }

    [Fact]
    public void InvalidRpm_BehavesSafely()
    {
        Assert.False(UpshiftApproach.IsInWindow(float.NaN, ShiftRpm));
        Assert.Equal(0, UpshiftApproach.ActiveLightCount(float.NaN, ShiftRpm));
        Assert.Equal(0f, UpshiftApproach.NormalizedProgress(float.NaN, ShiftRpm));
        Assert.Equal(0, UpshiftApproach.ActiveLightCount(-1f, ShiftRpm));
    }

    [Fact]
    public void Window_OpensAtEightyPercentOfShiftRpm()
    {
        Assert.True(UpshiftApproach.IsInWindow(WindowStartRpm, ShiftRpm));
        Assert.Equal(0f, UpshiftApproach.NormalizedProgress(WindowStartRpm, ShiftRpm), 6);
    }

    [Fact]
    public void BelowWindowStart_ProducesNoCue()
    {
        Assert.False(UpshiftApproach.IsInWindow(WindowStartRpm - 1f, ShiftRpm));
        Assert.Equal(0, UpshiftApproach.ActiveLightCount(WindowStartRpm - 1f, ShiftRpm));
        Assert.Equal(0f, UpshiftApproach.NormalizedProgress(WindowStartRpm - 1f, ShiftRpm), 6);
    }

    [Fact]
    public void FirstLight_IsActiveImmediatelyAtWindowStart()
    {
        Assert.Equal(1, UpshiftApproach.ActiveLightCount(WindowStartRpm, ShiftRpm));
    }

    [Theory]
    [InlineData(4800f, 1)]
    [InlineData(5000f, 1)]
    [InlineData(5001f, 2)]
    [InlineData(5200f, 2)]
    [InlineData(5201f, 3)]
    [InlineData(5400f, 3)]
    [InlineData(5401f, 4)]
    [InlineData(5600f, 4)]
    [InlineData(5601f, 5)]
    [InlineData(5800f, 5)]
    [InlineData(5801f, 6)]
    [InlineData(6000f, 6)]
    public void ActiveLightCount_ActivatesAtEvenlySpacedThresholds(float rpm, int expected)
    {
        Assert.Equal(expected, UpshiftApproach.ActiveLightCount(rpm, ShiftRpm));
    }

    [Theory]
    [InlineData(4800f, 0.0)]
    [InlineData(5400f, 0.5)]
    [InlineData(6000f, 1.0)]
    public void NormalizedProgress_ScalesAcrossTheWindow(float rpm, double expected)
    {
        Assert.Equal(expected, UpshiftApproach.NormalizedProgress(rpm, ShiftRpm), 6);
    }

    [Fact]
    public void FullProgress_AtTheShiftPoint()
    {
        // The shift point itself belongs to the terminal latch, but the
        // progress model reports full progress and all six lights there.
        Assert.Equal(1f, UpshiftApproach.NormalizedProgress(ShiftRpm, ShiftRpm), 6);
        Assert.Equal(6, UpshiftApproach.ActiveLightCount(ShiftRpm, ShiftRpm));
        Assert.False(UpshiftApproach.IsInWindow(ShiftRpm, ShiftRpm));
    }

    [Fact]
    public void Progress_ClampsAboveTheTarget()
    {
        Assert.Equal(1f, UpshiftApproach.NormalizedProgress(6500f, ShiftRpm), 6);
        Assert.Equal(6, UpshiftApproach.ActiveLightCount(9000f, ShiftRpm));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LightGrouping_FirstTwoAreYellow(int index)
    {
        Assert.Equal(UpshiftApproach.LightGroup.Yellow, UpshiftApproach.GroupOf(index));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LightGrouping_MiddleTwoAreOrange(int index)
    {
        Assert.Equal(UpshiftApproach.LightGroup.Orange, UpshiftApproach.GroupOf(index));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void LightGrouping_LastTwoAreRed(int index)
    {
        Assert.Equal(UpshiftApproach.LightGroup.Red, UpshiftApproach.GroupOf(index));
    }

    [Fact]
    public void LightGrouping_RejectsOutOfRangeIndices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UpshiftApproach.GroupOf(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UpshiftApproach.GroupOf(UpshiftApproach.LightCount));
    }

    [Fact]
    public void RedlineFallback_EngineMaxRpmIsANormalTarget()
    {
        // The advisor returns EngineMaxRpm when no power crossover exists.
        // The approach cue must treat that exactly like any other target.
        const float maxRpm = 7000f;
        const float redlineWindowStart = 5600f;

        Assert.True(UpshiftApproach.IsInWindow(redlineWindowStart, maxRpm));
        Assert.Equal(1, UpshiftApproach.ActiveLightCount(redlineWindowStart, maxRpm));
        Assert.Equal(0.5f, UpshiftApproach.NormalizedProgress(6300f, maxRpm), 6);
        Assert.Equal(3, UpshiftApproach.ActiveLightCount(6300f, maxRpm));
        Assert.Equal(1f, UpshiftApproach.NormalizedProgress(maxRpm, maxRpm), 6);
        Assert.Equal(6, UpshiftApproach.ActiveLightCount(maxRpm, maxRpm));
    }
}

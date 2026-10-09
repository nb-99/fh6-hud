using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class PowerCurveAxisTests
{
    [Theory]
    [InlineData(0, 7000, 0)]
    [InlineData(3500, 7000, 50)]
    [InlineData(7000, 7000, 100)]
    [InlineData(9000, 7000, 100)] // clamped at the right edge
    [InlineData(-100, 7000, 0)]   // clamped at the left edge
    public void XForRpm_MapsLinearlyAcrossTheCurve(double rpm, double maxRpm, double expected)
    {
        Assert.Equal(expected, PowerCurveAxis.XForRpm(rpm, maxRpm, 100), 6);
    }

    [Fact]
    public void XForRpm_IsZeroWhenMaxRpmUnknown()
    {
        Assert.Equal(0, PowerCurveAxis.XForRpm(3000, 0, 100));
    }

    [Fact]
    public void YForPower_PeakIsAtTheTopAndZeroAtTheBottom()
    {
        Assert.Equal(0, PowerCurveAxis.YForPower(500_000, 500_000, 88), 6);
        Assert.Equal(88, PowerCurveAxis.YForPower(0, 500_000, 88), 6);
        Assert.Equal(44, PowerCurveAxis.YForPower(250_000, 500_000, 88), 6);
    }

    private static readonly string[] ExpectedMajorLabels = { "1k", "2k", "3k", "4k", "5k", "6k", "7k" };
    private static readonly string[] ExpectedPowerLabels = { "100 PS", "200 PS", "300 PS", "400 PS" };
    private static readonly string[] ExpectedFewerPowerLabels = { "50 PS", "100 PS", "150 PS", "200 PS" };

    [Fact]
    public void RpmTicks_EveryFiveHundredWithLabelsOnEveryThousand()
    {
        var ticks = PowerCurveAxis.RpmTicks(7000, 700);

        // 500, 1000, ..., 7000 -> 14 lines, including one at max RPM.
        Assert.Equal(14, ticks.Count);
        Assert.Equal(50, ticks[0].X, 6); // 500 / 7000 of 700 px
        Assert.Equal(700, ticks[^1].X, 6);

        var majors = ticks.Where(t => t.Major).ToList();
        Assert.Equal(7, majors.Count);
        Assert.Equal(ExpectedMajorLabels, majors.Select(t => t.Label), StringComparer.Ordinal);
        Assert.All(ticks.Where(t => !t.Major), t => Assert.Null(t.Label));
    }

    [Fact]
    public void RpmTicks_LinesAlignWithTheCurveTransform()
    {
        // A grid line at 6000 RPM must land exactly where the curve/dot put 6000 RPM.
        var ticks = PowerCurveAxis.RpmTicks(7000, 700);
        var sixK = ticks.Single(t => string.Equals(t.Label, "6k", StringComparison.Ordinal));
        Assert.Equal(PowerCurveAxis.XForRpm(6000, 7000, 700), sixK.X, 6);
    }

    [Fact]
    public void RpmTicks_EmptyWithoutMaxRpmOrWidth()
    {
        Assert.Empty(PowerCurveAxis.RpmTicks(0, 700));
        Assert.Empty(PowerCurveAxis.RpmTicks(7000, 0));
    }

    [Fact]
    public void PowerTicks_UseRoundPsStepsBelowThePeak()
    {
        // 435 PS peak: 50 PS steps would give 8 lines (too busy), 100 PS gives 4.
        double peakW = 435f * PowerCurveTracker.WattsPerPs;
        var ticks = PowerCurveAxis.PowerTicks(peakW, 88);

        Assert.Equal(ExpectedPowerLabels, ticks.Select(t => t.Label), StringComparer.Ordinal);
        Assert.Equal(88 - 100 / 435.0 * 88, ticks[0].Y, 6);
    }

    [Fact]
    public void PowerTicks_NoLineAtOrAboveThePeak()
    {
        // 210 PS peak with 50 PS spacing: lines at 50..200 only; nothing at or above the peak.
        double peakW = 210f * PowerCurveTracker.WattsPerPs;
        var ticks = PowerCurveAxis.PowerTicks(peakW, 88);

        Assert.Equal(ExpectedFewerPowerLabels, ticks.Select(t => t.Label), StringComparer.Ordinal);
        Assert.All(ticks, t => Assert.True(t.Y > 0 && t.Y < 88));
    }

    [Fact]
    public void FindPowerBand_SpansTheContiguousRangeAroundThePeak()
    {
        // 7000 RPM redline = 71 buckets. Buckets 57..62 are within 95% of the
        // 100-unit peak at bucket 60; the rest of the curve is well below.
        var buckets = Enumerable.Repeat(50f, 71).ToArray();
        for (int i = 57; i <= 62; i++)
        {
            buckets[i] = 96f;
        }

        buckets[60] = 100f;

        var band = PowerCurveAxis.FindPowerBand(buckets, 100, 7000);

        Assert.Equal(new PowerCurveAxis.PowerBand(5700, 6300), band);
    }

    [Fact]
    public void FindPowerBand_StopsAtTheFirstBucketBelowThreshold()
    {
        // Bucket 61 is 94 (< 95% of 100), so the band ends at bucket 60 even
        // though bucket 62 is above the threshold again.
        var buckets = new float[71];
        buckets[59] = 96f;
        buckets[60] = 100f;
        buckets[61] = 94f;
        buckets[62] = 99f;

        var band = PowerCurveAxis.FindPowerBand(buckets, 100, 7000);

        Assert.Equal(new PowerCurveAxis.PowerBand(5900, 6100), band);
    }

    [Fact]
    public void FindPowerBand_ClampsTheUpperEdgeToRedline()
    {
        var buckets = new float[71];
        buckets[69] = 96f;
        buckets[70] = 100f;

        var band = PowerCurveAxis.FindPowerBand(buckets, 100, 7000);

        Assert.Equal(new PowerCurveAxis.PowerBand(6900, 7000), band);
    }

    [Fact]
    public void FindPowerBand_SinglePeakBucketIsOneBucketWide()
    {
        var buckets = new float[71];
        buckets[40] = 100f;

        var band = PowerCurveAxis.FindPowerBand(buckets, 100, 7000);

        Assert.Equal(new PowerCurveAxis.PowerBand(4000, 4100), band);
    }

    [Fact]
    public void FindPowerBand_NullWithoutPowerData()
    {
        Assert.Null(PowerCurveAxis.FindPowerBand(new float[71], 100, 7000));
        Assert.Null(PowerCurveAxis.FindPowerBand(Array.Empty<float>(), 100, 7000));
    }

    [Fact]
    public void PowerBandLabel_ShowsThresholdAndRange()
    {
        Assert.Equal("95%+ 5700–6300", PowerCurveAxis.PowerBandLabel(new PowerCurveAxis.PowerBand(5700, 6300)));
    }

    [Fact]
    public void PowerTicks_EmptyWithoutPower()
    {
        Assert.Empty(PowerCurveAxis.PowerTicks(0, 88));
    }
}

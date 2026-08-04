using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class PowerCurveTrackerTests
{
    [Fact]
    public void TracksPerBucketPeaks_AndMaxPower()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        tracker.AddSample(2000f, 100_000f);
        tracker.AddSample(2000f, 120_000f);
        tracker.AddSample(2000f, 90_000f);
        tracker.AddSample(5000f, 300_000f);

        Assert.Equal(120_000f, tracker.Buckets[20]);
        Assert.Equal(300_000f, tracker.Buckets[50]);
        Assert.Equal(300_000f, tracker.MaxPowerW);
    }

    [Fact]
    public void MaxRpmChange_ResetsCurve()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        tracker.AddSample(3000f, 200_000f);
        Assert.Equal(200_000f, tracker.MaxPowerW);

        tracker.Configure(9000f);
        Assert.Equal(0f, tracker.MaxPowerW);
        Assert.Equal(91, tracker.BucketCount);
    }

    [Fact]
    public void IgnoreSamplesBeforeConfigure()
    {
        var tracker = new PowerCurveTracker();
        Assert.False(tracker.AddSample(3000f, 200_000f));
        Assert.Equal(0f, tracker.MaxPowerW);
    }

    [Fact]
    public void WattsToPs_Converts()
    {
        Assert.Equal(1f, PowerCurveTracker.WattsToPs(735.49875f), 4);
        Assert.Equal(435.08f, PowerCurveTracker.WattsToPs(320_000f), 2);
    }
}

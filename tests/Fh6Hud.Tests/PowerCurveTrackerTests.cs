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
    public void MaxPowerRpm_IsBucketMidpoint_OfMaxBucket()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        tracker.AddSample(2000f, 100_000f);
        Assert.Equal(2050f, tracker.MaxPowerRpm);

        tracker.AddSample(5000f, 300_000f);
        Assert.Equal(5050f, tracker.MaxPowerRpm);

        // A later lower sample must not move the max-power RPM.
        tracker.AddSample(6000f, 200_000f);
        Assert.Equal(5050f, tracker.MaxPowerRpm);

        // Reset and reconfigure must forget the RPM again.
        tracker.Reset();
        tracker.Configure(7000f);
        Assert.Equal(0f, tracker.MaxPowerRpm);
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
    public void Reset_ClearsCurve_EvenWhenMaxRpmUnchanged()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        tracker.AddSample(3000f, 200_000f);
        Assert.Equal(200_000f, tracker.MaxPowerW);

        tracker.Reset();
        Assert.Equal(0f, tracker.MaxPowerW);
        Assert.Equal(0, tracker.BucketCount);

        // Reconfiguring with the same max RPM must reinitialize buckets —
        // without Reset, Configure would early-return and keep old data.
        tracker.Configure(7000f);
        Assert.Equal(71, tracker.BucketCount);
        Assert.Equal(0f, tracker.MaxPowerW);
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

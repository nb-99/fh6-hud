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
    public void MaxPowerRpm_DoesNotExceedConfiguredRedline()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        tracker.AddSample(7000f, 300_000f);
        Assert.Equal(7000f, tracker.MaxPowerRpm);

        tracker.AddSample(7500f, 350_000f);
        Assert.Equal(7000f, tracker.MaxPowerRpm);
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

    private const byte FullThrottle = 255;

    /// <summary>Feeds a full-throttle pull of identical samples at one RPM and power.</summary>
    private static void FeedPull(PowerCurveTracker tracker, float rpm, float powerW, byte gear = 3, int samples = PowerCurveTracker.MinSamplesPerPull)
    {
        for (int i = 0; i < samples; i++)
        {
            tracker.AddPullSample(rpm, powerW, FullThrottle, gear);
        }
    }

    /// <summary>Releases the throttle, which ends the current pull.</summary>
    private static void ReleaseThrottle(PowerCurveTracker tracker) =>
        tracker.AddPullSample(0f, 0f, 0, 3);

    [Fact]
    public void PullIsNotVisibleUntilItEnds()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        int version = tracker.Version;

        FeedPull(tracker, 3000f, 200_000f);

        Assert.Equal(0f, tracker.Buckets[30]);
        Assert.Equal(0f, tracker.MaxPowerW);
        Assert.Equal(version, tracker.Version);

        ReleaseThrottle(tracker);

        Assert.Equal(200_000f, tracker.Buckets[30]);
        Assert.Equal(200_000f, tracker.MaxPowerW);
        Assert.True(tracker.Version > version);
    }

    [Fact]
    public void NewestPull_ReplacesAnOlderSpike()
    {
        // A grip-induced spike is the first pull; the next normal pull must
        // replace it, instead of the spike staying as an all-time maximum.
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        FeedPull(tracker, 3000f, 400_000f);
        ReleaseThrottle(tracker);
        Assert.Equal(400_000f, tracker.MaxPowerW);

        FeedPull(tracker, 3000f, 300_000f);
        ReleaseThrottle(tracker);

        Assert.Equal(300_000f, tracker.Buckets[30]);
        Assert.Equal(300_000f, tracker.MaxPowerW);
    }

    [Fact]
    public void NewestPull_LeavesBucketsItDidNotReachUntouched()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        FeedPull(tracker, 3000f, 400_000f);
        FeedPull(tracker, 5000f, 350_000f);
        ReleaseThrottle(tracker);

        FeedPull(tracker, 3000f, 300_000f);
        ReleaseThrottle(tracker);

        Assert.Equal(350_000f, tracker.Buckets[50]);
        Assert.Equal(350_000f, tracker.MaxPowerW);
        Assert.Equal(5050f, tracker.MaxPowerRpm);
    }

    [Fact]
    public void Pull_KeepsThePeakWithinThatPull()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        for (int i = 0; i < PowerCurveTracker.MinSamplesPerPull; i++)
        {
            float power = i switch { 2 => 150_000f, 5 => 120_000f, _ => 100_000f };
            tracker.AddPullSample(3000f, power, FullThrottle, 3);
        }

        ReleaseThrottle(tracker);

        Assert.Equal(150_000f, tracker.Buckets[30]);
    }

    [Fact]
    public void ShortPull_IsDiscarded()
    {
        // One-frame throttle taps must not overwrite the curve.
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        FeedPull(tracker, 3000f, 400_000f, samples: PowerCurveTracker.MinSamplesPerPull - 1);
        ReleaseThrottle(tracker);

        Assert.Equal(0f, tracker.Buckets[30]);
        Assert.Equal(0f, tracker.MaxPowerW);
    }

    [Fact]
    public void EndPull_CommitsAPullWithoutAThrottleRelease()
    {
        // Telemetry going stale (menu, loading) ends the pull explicitly.
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        FeedPull(tracker, 3000f, 200_000f);
        tracker.EndPull();

        Assert.Equal(200_000f, tracker.Buckets[30]);
    }

    [Fact]
    public void PullCounts_CountCommittedPullsPerGearUsed()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        FeedPull(tracker, 3000f, 200_000f, gear: 3);
        FeedPull(tracker, 4000f, 220_000f, gear: 4);
        ReleaseThrottle(tracker);
        Assert.Equal(1, tracker.GetPullCount(3));
        Assert.Equal(1, tracker.GetPullCount(4));
        Assert.Equal(0, tracker.GetPullCount(5));

        FeedPull(tracker, 3000f, 210_000f, gear: 3);
        ReleaseThrottle(tracker);
        Assert.Equal(2, tracker.GetPullCount(3));
    }

    [Fact]
    public void Reset_ClearsPendingPullAndPullCounts()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        FeedPull(tracker, 3000f, 200_000f);
        ReleaseThrottle(tracker);

        FeedPull(tracker, 4000f, 300_000f); // still pending when the car changes
        tracker.Reset();
        tracker.Configure(7000f);
        ReleaseThrottle(tracker);

        Assert.Equal(0f, tracker.MaxPowerW);
        Assert.All(tracker.Buckets, b => Assert.Equal(0f, b));
        Assert.Equal(0, tracker.GetPullCount(3));
    }

    [Fact]
    public void WattsToPs_Converts()
    {
        Assert.Equal(1f, PowerCurveTracker.WattsToPs(735.49875f), 4);
        Assert.Equal(435.08f, PowerCurveTracker.WattsToPs(320_000f), 2);
    }
}

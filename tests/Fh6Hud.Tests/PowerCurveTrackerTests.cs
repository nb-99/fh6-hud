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

    private static Fh6Packet Sample(float rpm, float powerW, uint timestampMs, byte accel = 255, byte gear = 3) =>
        Fh6Packet.Parse(new Fh6PacketBuilder()
            .IsRaceOn(1)
            .EngineMaxRpm(7000f)
            .CurrentEngineRpm(rpm)
            .PowerWatts(powerW)
            .TimestampMs(timestampMs)
            .Accel(accel)
            .Gear(gear)
            .Build())!;

    /// <summary>Full throttle at a steady RPM and power, sampled every 16 ms from <paramref name="fromMs"/>.</summary>
    private static void Drive(PowerCurveTracker tracker, float rpm, float powerW, uint fromMs, uint toMs)
    {
        for (uint t = fromMs; t <= toMs; t += 16)
        {
            tracker.Observe(Sample(rpm, powerW, t));
        }
    }

    [Fact]
    public void Observe_UpdatesTheCurveWhileDriving_WithoutAnyRelease()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        int version = tracker.Version;

        // First second at full throttle: boost is still building, nothing recorded yet.
        Drive(tracker, 3000f, 200_000f, fromMs: 0, toMs: 500);
        Assert.Equal(0f, tracker.Buckets[30]);
        Assert.Equal(version, tracker.Version);

        Drive(tracker, 3000f, 200_000f, fromMs: 1000, toMs: 1000);
        Assert.Equal(200_000f, tracker.Buckets[30]);
        Assert.Equal(200_000f, tracker.MaxPowerW);
    }

    [Fact]
    public void BoostBuildingAfterALongRelease_IsNotRecorded()
    {
        // Good full-range run, then a long lift and throttle reapplied at
        // 5000 RPM. The first second after reapplying must not overwrite the curve.
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        Drive(tracker, 5000f, 400_000f, fromMs: 0, toMs: 2000);
        Assert.Equal(400_000f, tracker.Buckets[50]);

        tracker.Observe(Sample(5000f, 0f, 3000, accel: 0));  // released
        Drive(tracker, 5000f, 150_000f, fromMs: 5000, toMs: 5800);

        Assert.Equal(400_000f, tracker.Buckets[50]);
    }

    [Fact]
    public void AfterTheSettleWindow_TheNewRunReplacesTheOldValue()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        Drive(tracker, 5000f, 400_000f, fromMs: 0, toMs: 2000);

        tracker.Observe(Sample(5000f, 0f, 3000, accel: 0));
        Drive(tracker, 5000f, 300_000f, fromMs: 5000, toMs: 7000);

        Assert.Equal(300_000f, tracker.Buckets[50]);
        Assert.Equal(300_000f, tracker.MaxPowerW);
    }

    [Fact]
    public void ShortLift_LikeAGearChange_RecordsImmediately()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        Drive(tracker, 6000f, 350_000f, fromMs: 0, toMs: 2000);

        // 100 ms lift for a gear change, then power comes back at a lower RPM.
        tracker.Observe(Sample(6000f, 0f, 2100, accel: 0));
        tracker.Observe(Sample(4500f, 280_000f, 2200));

        Assert.Equal(280_000f, tracker.Buckets[45]);
    }

    [Fact]
    public void ANewRun_LeavesBucketsItDidNotReachUntouched()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        Drive(tracker, 3000f, 400_000f, fromMs: 0, toMs: 2000);
        Drive(tracker, 5000f, 350_000f, fromMs: 2000, toMs: 2500);

        tracker.Observe(Sample(3000f, 0f, 3000, accel: 0));
        Drive(tracker, 3000f, 300_000f, fromMs: 5000, toMs: 7000);

        Assert.Equal(350_000f, tracker.Buckets[50]);
        Assert.Equal(350_000f, tracker.MaxPowerW);
        Assert.Equal(5050f, tracker.MaxPowerRpm);
    }

    [Fact]
    public void WithinOneRun_TheHighestSampleInABucketIsKept()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);

        tracker.Observe(Sample(3000f, 100_000f, 0));
        tracker.Observe(Sample(3000f, 150_000f, 1000));
        tracker.Observe(Sample(3000f, 120_000f, 1016));

        Assert.Equal(150_000f, tracker.Buckets[30]);
    }

    [Fact]
    public void SameRunAgain_IsANoOp()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        Drive(tracker, 3000f, 200_000f, fromMs: 0, toMs: 1000);

        // The UI calls Observe every frame with the same packet: the first call
        // may change the curve, repeats must not.
        var packet = Sample(3000f, 200_000f, 1000);
        tracker.Observe(packet);
        int version = tracker.Version;
        tracker.Observe(packet);
        Assert.Equal(version, tracker.Version);
    }

    [Fact]
    public void EndRun_MakesTheNextFullThrottleWaitForBoostAgain()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        Drive(tracker, 3000f, 200_000f, fromMs: 0, toMs: 2000);

        // Telemetry went stale (menu). When the game resumes, the first second is not recorded.
        tracker.EndRun();
        Drive(tracker, 3000f, 100_000f, fromMs: 9000, toMs: 9500);

        Assert.Equal(200_000f, tracker.Buckets[30]);
    }

    [Fact]
    public void Settle_WorksAcrossTheTimestampWrap()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        uint start = uint.MaxValue - 200;

        // Settling ends 1000 ms later, which wraps past zero.
        tracker.Observe(Sample(3000f, 200_000f, start));
        Assert.Equal(0f, tracker.Buckets[30]);

        tracker.Observe(Sample(3000f, 200_000f, unchecked(start + 1000)));
        Assert.Equal(200_000f, tracker.Buckets[30]);
    }

    [Fact]
    public void PowerAt_ReturnsSampledBucketValue()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        tracker.AddSample(2000f, 100_000f);

        Assert.Equal(100_000f, tracker.PowerAt(2000f));
        // Past the last sampled bucket there is no data to interpolate toward.
        Assert.Equal(0f, tracker.PowerAt(2050f));
    }

    [Fact]
    public void PowerAt_BridgesAnUnsampledBucket_InsteadOfReadingZero()
    {
        // Bucket 21 was never sampled. It must not read as 0 W (the chart dip
        // and the shift advisor's dip both came from that).
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        tracker.AddSample(2000f, 100_000f);
        tracker.AddSample(2200f, 120_000f);

        Assert.Equal(0f, tracker.Buckets[21]);
        Assert.Equal(110_000f, tracker.PowerAt(2100f), 3);
        Assert.Equal(105_000f, tracker.PowerAt(2050f), 3);
    }

    [Fact]
    public void PowerAt_IsZeroOutsideTheSampledSpan()
    {
        var tracker = new PowerCurveTracker();
        tracker.Configure(7000f);
        tracker.AddSample(2000f, 100_000f);
        tracker.AddSample(2200f, 120_000f);

        Assert.Equal(0f, tracker.PowerAt(1000f));
        Assert.Equal(0f, tracker.PowerAt(5000f));
    }

    [Fact]
    public void WattsToPs_Converts()
    {
        Assert.Equal(1f, PowerCurveTracker.WattsToPs(735.49875f), 4);
        Assert.Equal(435.08f, PowerCurveTracker.WattsToPs(320_000f), 2);
    }
}

using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class ShiftPointAdvisorTests
{
    private const float MaxRpm = 7000f;

    /// <summary>Power curve: 120 kW at 1000 RPM, linear to 300 kW peak at 5000, then falls 30 W/RPM.</summary>
    private static float SyntheticPower(float rpm) =>
        rpm <= 5000f
            ? 120_000f + (rpm - 1000f) * 45f
            : 300_000f - (rpm - 5000f) * 30f;

    private static PowerCurveTracker CurveWithPeakAt5000(float fromRpm = 25f)
    {
        var curve = new PowerCurveTracker();
        curve.Configure(MaxRpm);
        for (float rpm = fromRpm; rpm <= MaxRpm; rpm += 25f)
        {
            curve.AddSample(rpm, SyntheticPower(rpm));
        }

        return curve;
    }

    private static GearRatioTracker RatiosWithGear1And2()
    {
        var ratios = new GearRatioTracker();
        // gear 1: 300 rpm per m/s, gear 2: 200 rpm per m/s (step-down factor 2/3)
        Feed(ratios, gear: 1, rpm: 6000f, speedMs: 20f);
        Feed(ratios, gear: 2, rpm: 6000f, speedMs: 30f);
        return ratios;
    }

    private static void Feed(GearRatioTracker tracker, byte gear, float rpm, float speedMs)
    {
        var bytes = new Fh6PacketBuilder()
            .IsRaceOn(1)
            .EngineMaxRpm(MaxRpm)
            .CurrentEngineRpm(rpm)
            .SpeedMs(speedMs)
            .Gear(gear)
            .Build();
        var packet = Fh6Packet.Parse(bytes)!;
        for (int i = 0; i < GearRatioTracker.MinSamplesPerGear; i++)
        {
            tracker.AddSample(packet);
        }
    }

    [Fact]
    public void ShiftPoint_IsWhereCurrentGearPowerMeetsNextGearPower()
    {
        // Analytic crossover for the synthetic curve with factor 2/3:
        // 450000 - 30r = 75000 + 30r  =>  r = 6250 rpm.
        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);

        float shift = advisor.GetShiftRpm(1)!.Value;
        Assert.InRange(shift, 6250f - 150f, 6250f + 150f);
    }

    [Fact]
    public void NoCrossover_ShiftAtRedline()
    {
        // Flat top: power never falls after the peak, so redline is optimal.
        var curve = new PowerCurveTracker();
        curve.Configure(MaxRpm);
        for (float rpm = 25f; rpm <= MaxRpm; rpm += 25f)
        {
            curve.AddSample(rpm, Math.Min(rpm * 60f, 300_000f));
        }

        var advisor = new ShiftPointAdvisor(curve, RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);

        Assert.Equal(MaxRpm, advisor.GetShiftRpm(1)!.Value);
    }

    [Fact]
    public void MissingNextGearRatio_NoAdvice()
    {
        var ratios = new GearRatioTracker();
        Feed(ratios, gear: 1, rpm: 6000f, speedMs: 20f); // gear 2 never learned

        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), ratios);
        advisor.Recalculate(MaxRpm);

        Assert.Null(advisor.GetShiftRpm(1));
        Assert.False(advisor.ShouldUpshift(1, MaxRpm));
    }

    [Fact]
    public void UnsampledNextGearPowerRegion_NoAdvice()
    {
        // Only the upper half of the rev range has power samples; the post-shift
        // RPM of gear 2 (rpm * 2/3 of the upper range) lands in the gap.
        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(fromRpm: 4000f), RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);

        Assert.Null(advisor.GetShiftRpm(1));
    }

    [Fact]
    public void TopGear_HasNoShiftPoint()
    {
        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);

        Assert.Null(advisor.GetShiftRpm(2)); // no gear 3 to compare against
    }

    [Fact]
    public void ShouldUpshift_LatchesWithHysteresis()
    {
        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);
        float shift = advisor.GetShiftRpm(1)!.Value;

        Assert.False(advisor.ShouldUpshift(1, shift - 300f));
        Assert.True(advisor.ShouldUpshift(1, shift));
        Assert.True(advisor.ShouldUpshift(1, shift - 100f)); // latched inside the band
        Assert.False(advisor.ShouldUpshift(1, shift - 300f)); // dropped out of the band
        Assert.False(advisor.ShouldUpshift(1, shift - 10f)); // stays off until threshold again
    }

    [Fact]
    public void ShouldUpshift_ResetsLatchOnGearChange()
    {
        var ratios = RatiosWithGear1And2();
        Feed(ratios, gear: 3, rpm: 6000f, speedMs: 50f); // taller 3rd: shift point ~6579 rpm

        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), ratios);
        advisor.Recalculate(MaxRpm);
        float shift1 = advisor.GetShiftRpm(1)!.Value;

        Assert.True(advisor.ShouldUpshift(1, shift1));
        // Same RPM in the next gear is below its (higher) shift point; the gear-1
        // latch must not carry over and make it read true.
        Assert.False(advisor.ShouldUpshift(2, shift1));
    }

    [Fact]
    public void Recalculate_IsCachedUntilInputsChange()
    {
        var curve = CurveWithPeakAt5000();
        var ratios = RatiosWithGear1And2();
        var advisor = new ShiftPointAdvisor(curve, ratios);
        advisor.Recalculate(MaxRpm);
        float first = advisor.GetShiftRpm(1)!.Value;

        advisor.Recalculate(MaxRpm); // no new samples: same result
        Assert.Equal(first, advisor.GetShiftRpm(1)!.Value);

        curve.AddSample(6800f, 400_000f); // curve changes: recompute still consistent
        advisor.Recalculate(MaxRpm);
        Assert.NotNull(advisor.GetShiftRpm(1));
    }

    [Fact]
    public void DownshiftRpm_IsTheLowerGearsShiftPointSeenFromThisGear_MinusSafetyMargin()
    {
        // Gear 1 shift ~6250 rpm, ratios 300/200 → gear 2's downshift
        // threshold = (6250 − 400) · 200/300 ≈ 3900 rpm.
        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);

        Assert.InRange(advisor.GetDownshiftRpm(2)!.Value, 3900f - 100f, 3900f + 100f);
        Assert.Null(advisor.GetDownshiftRpm(1)); // nothing below first gear
    }

    [Fact]
    public void Downshift_EngagesBelowThresholdWithPowerGain_AndLatches()
    {
        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);
        float down = advisor.GetDownshiftRpm(2)!.Value; // ≈3900

        // 3800 rpm in gear 2 → 5700 rpm in gear 1: 279 kW vs 246 kW → gain.
        Assert.True(advisor.ShouldDownshift(2, 3800f));
        // Latched through the hysteresis band above the threshold...
        Assert.True(advisor.ShouldDownshift(2, down + 100f));
        // ...and released beyond it.
        Assert.False(advisor.ShouldDownshift(2, down + 200f));
        // A gainful downshift near the boundary is refused: post-shift RPM
        // would land right at the lower gear's shift point again.
        Assert.False(advisor.ShouldDownshift(2, down + 400f));
        // Well below it the gain is large and the advice returns.
        Assert.True(advisor.ShouldDownshift(2, 3000f));
    }

    [Fact]
    public void Downshift_RefusesLimiterBounce_AfterUpshiftAtRedline()
    {
        // Power rising all the way to redline → no crossover → the lower
        // gear's shift point falls back to redline (7000 rpm). Without the
        // safety margin this is the exact state that caused the bug: an
        // upshift at the limiter lands at 7000 · 200/300 ≈ 4667 rpm in gear
        // 2, which sat right on the old downshift threshold.
        var rising = new PowerCurveTracker();
        rising.Configure(MaxRpm);
        for (float rpm = 25f; rpm <= MaxRpm; rpm += 25f)
        {
            rising.AddSample(rpm, rpm * 50f); // 350 kW at redline, monotonically rising
        }

        var advisor = new ShiftPointAdvisor(rising, RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);

        // threshold = (7000 − 400) · 200/300 = 4400 rpm.
        Assert.InRange(advisor.GetDownshiftRpm(2)!.Value, 4400f - 100f, 4400f + 100f);

        // Right after the limiter-bounce upshift: huge power gain exists
        // (back to 7000 rpm in gear 1), but the margin refuses the shift.
        Assert.False(advisor.ShouldDownshift(2, 4667f));
        Assert.False(advisor.ShouldDownshift(2, 4500f));
        // Deep inside the zone it is still allowed (post-shift max ≈ 6600).
        Assert.True(advisor.ShouldDownshift(2, 4000f));
    }

    [Fact]
    public void Downshift_NoAdvice_WhenPowerIsEqual()
    {
        // A flat power curve means a downshift gains nothing → no advice,
        // even though the threshold itself exists (redline fallback).
        var flat = new PowerCurveTracker();
        flat.Configure(MaxRpm);
        for (float rpm = 25f; rpm <= MaxRpm; rpm += 25f)
        {
            flat.AddSample(rpm, 300_000f);
        }

        var advisor = new ShiftPointAdvisor(flat, RatiosWithGear1And2());
        advisor.Recalculate(MaxRpm);

        Assert.NotNull(advisor.GetDownshiftRpm(2));
        Assert.False(advisor.ShouldDownshift(2, 3000f));
    }

    [Fact]
    public void Downshift_NoAdvice_WhenDataMissing()
    {
        var ratios = new GearRatioTracker();
        Feed(ratios, gear: 1, rpm: 6000f, speedMs: 20f); // gear 2 never learned

        var advisor = new ShiftPointAdvisor(CurveWithPeakAt5000(), ratios);
        advisor.Recalculate(MaxRpm);

        Assert.Null(advisor.GetDownshiftRpm(2));
        Assert.False(advisor.ShouldDownshift(2, 3000f));
        Assert.False(advisor.ShouldDownshift(1, 4000f)); // first gear has no lower gear
    }
}

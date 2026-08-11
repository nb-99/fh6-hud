namespace Fh6Hud.Telemetry;

/// <summary>
/// Computes the optimal upshift RPM per gear from the learned power curve and
/// gear ratios. At a given road speed, wheel torque is proportional to engine
/// power (wheel speed is fixed by road speed), so the fastest gear is simply
/// the one making more power. The optimal upshift from gear <c>n</c> is
/// therefore the RPM where the power in the current gear falls to the power
/// the engine would make after the shift:
/// <c>P(rpm) &lt;= P(rpm * ratio_{n+1} / ratio_n)</c>. If the curves never
/// cross, shifting at redline is optimal.
/// </summary>
/// <remarks>
/// Results are only produced once both gear ratios and the relevant power
/// curve regions have been sampled (a few full-throttle pulls); until then
/// <see cref="GetShiftRpm"/> returns null and the indicator stays off rather
/// than guessing. Advice is meaningless for D/R/N/P (the actual gear is
/// unknown in D) and for the top gear (no next gear to compare against).
/// </remarks>
public sealed class ShiftPointAdvisor
{
    private const float StepRpm = 25f;

    /// <summary>RPM band below the shift point that keeps the indicator latched.</summary>
    private const float HysteresisRpm = 150f;

    /// <summary>Minimum relative power gain before a downshift is suggested (keeps the near-boundary from flickering).</summary>
    private const float PowerDeadband = 0.015f;

    /// <summary>
    /// Headroom kept below the lower gear's shift point before a downshift is
    /// suggested. Without it, cars whose power keeps rising to redline put the
    /// downshift threshold exactly at the RPM you land on after an upshift at
    /// the limiter — the HUD would advise shifting straight back down and
    /// bouncing the limiter again. A little less power in the higher gear is
    /// better than that.
    /// </summary>
    private const float DownshiftSafetyMarginRpm = 400f;

    private readonly PowerCurveTracker _curve;
    private readonly GearRatioTracker _ratios;
    private readonly Dictionary<int, float> _shiftRpmByGear = new();

    private int _computedCurveVersion = -1;
    private int _computedRatioVersion = -1;
    private float _computedMaxRpm = -1f;
    private bool _latched;
    private int _latchedGear;
    private bool _downLatched;
    private int _downLatchedGear;

    public ShiftPointAdvisor(PowerCurveTracker curve, GearRatioTracker ratios)
    {
        _curve = curve;
        _ratios = ratios;
    }

    /// <summary>
    /// Recomputes per-gear shift points when the curve or ratios changed since
    /// the last call. Cheap no-op while inputs are unchanged.
    /// </summary>
    public void Recalculate(float maxRpm)
    {
        if (_computedCurveVersion == _curve.Version
            && _computedRatioVersion == _ratios.Version
            && Math.Abs(_computedMaxRpm - maxRpm) < 1f)
        {
            return;
        }

        _computedCurveVersion = _curve.Version;
        _computedRatioVersion = _ratios.Version;
        _computedMaxRpm = maxRpm;
        _shiftRpmByGear.Clear();

        if (maxRpm <= 0f || _curve.MaxPowerW <= 0f || _curve.BucketCount == 0)
        {
            return;
        }

        float peakRpm = PeakPowerRpm();
        for (int gear = 1; gear < GearRatioTracker.MaxForwardGear; gear++)
        {
            float? shift = ComputeShiftRpm(gear, peakRpm, maxRpm);
            if (shift is { } rpm)
            {
                _shiftRpmByGear[gear] = rpm;
            }
        }
    }

    /// <summary>Optimal upshift RPM for the gear, or null when there is not enough data yet.</summary>
    public float? GetShiftRpm(int gear) =>
        _shiftRpmByGear.TryGetValue(gear, out var rpm) ? rpm : null;

    /// <summary>
    /// Whether the driver should upshift now. Latches with a hysteresis band
    /// so the indicator does not flicker when RPM hovers around the shift point.
    /// </summary>
    public bool ShouldUpshift(int gear, float currentRpm)
    {
        float? shiftRpm = GetShiftRpm(gear);
        if (shiftRpm is not { } threshold)
        {
            _latched = false;
            return false;
        }

        if (_latched && (_latchedGear != gear || currentRpm < threshold - HysteresisRpm))
        {
            _latched = false;
        }

        if (!_latched && currentRpm >= threshold)
        {
            _latched = true;
            _latchedGear = gear;
        }

        return _latched;
    }

    /// <summary>Drops the latch; learned data is kept (used on car switch).</summary>
    public void ResetLatch()
    {
        _latched = false;
        _downLatched = false;
    }

    /// <summary>
    /// RPM in the gear at or below which downshifting produces more power.
    /// This is the lower gear's optimal shift point seen from this gear
    /// (<c>shiftRpm(g-1) · ratio_g / ratio_{g-1}</c>), kept
    /// <see cref="DownshiftSafetyMarginRpm"/> below that shift point: a
    /// downshift within the zone can neither bump the rev limiter nor land
    /// past the lower gear's own shift point (where you would immediately
    /// need to upshift again). Null when the required data is not learned
    /// yet, for gear 1, or in non-learnable gears (D/R/N/P).
    /// </summary>
    public float? GetDownshiftRpm(int gear)
    {
        if (!GearRatioTracker.IsLearnableGear((byte)gear) || gear <= 1)
        {
            return null;
        }

        float? upshiftPoint = GetShiftRpm(gear - 1);
        float? currentRatio = _ratios.GetRatio(gear);
        float? lowerRatio = _ratios.GetRatio(gear - 1);
        if (upshiftPoint is not { } up
            || up <= DownshiftSafetyMarginRpm
            || currentRatio is not > 0f
            || lowerRatio is not > 0f)
        {
            return null;
        }

        // The road speed at which the lower gear reaches (a safety margin
        // before) its shift point, expressed as the RPM this gear would show
        // at that same speed. The margin guarantees the post-shift RPM lands
        // comfortably below the lower gear's shift point and its redline.
        return (up - DownshiftSafetyMarginRpm) * (currentRatio.Value / lowerRatio.Value);
    }

    /// <summary>
    /// Whether the driver should downshift now: the lower gear must make
    /// meaningfully more power at the post-shift RPM without immediately
    /// needing to shift back up. Latches with a hysteresis band like the
    /// upshift advice.
    /// </summary>
    public bool ShouldDownshift(int gear, float currentRpm)
    {
        float? downRpm = GetDownshiftRpm(gear);
        if (downRpm is not { } threshold)
        {
            _downLatched = false;
            return false;
        }

        if (_downLatched && _downLatchedGear == gear)
        {
            // Latched: only release well above the threshold.
            if (currentRpm <= threshold + HysteresisRpm)
            {
                return true;
            }

            _downLatched = false;
            return false;
        }

        if (currentRpm <= threshold && HasDownshiftPowerGain(gear, currentRpm))
        {
            _downLatched = true;
            _downLatchedGear = gear;
            return true;
        }

        _downLatched = false;
        return false;
    }

    /// <summary>
    /// True when the lower gear makes at least <see cref="PowerDeadband"/> more
    /// power at the post-downshift RPM than the current gear makes now. The
    /// deadband keeps the advice quiet near the boundary where the two powers
    /// are nearly equal (e.g. flat power curves).
    /// </summary>
    private bool HasDownshiftPowerGain(int gear, float rpm)
    {
        float lowerRatio = _ratios.GetRatio(gear - 1)!.Value;
        float currentRatio = _ratios.GetRatio(gear)!.Value;
        float powerNow = PowerAt(rpm);
        float powerAfter = PowerAt(rpm * (lowerRatio / currentRatio));
        return powerNow > 0f
            && powerAfter > 0f
            && powerAfter > powerNow * (1f + PowerDeadband);
    }

    private float? ComputeShiftRpm(int gear, float peakRpm, float maxRpm)
    {
        float? ratioCurrent = _ratios.GetRatio(gear);
        float? ratioNext = _ratios.GetRatio(gear + 1);
        if (ratioCurrent is not > 0f || ratioNext is not > 0f)
        {
            return null;
        }

        float stepDown = ratioNext.Value / ratioCurrent.Value;
        if (stepDown >= 1f)
        {
            return null; // inconsistent samples (e.g. mid-relearn); wait for clean data
        }

        // Before the power peak, power still rises with RPM, so a crossover is
        // impossible there — scan from the peak toward redline.
        for (float rpm = Math.Max(peakRpm, PowerCurveTracker.BucketRpm); rpm <= maxRpm; rpm += StepRpm)
        {
            float powerNow = PowerAt(rpm);
            if (powerNow <= 0f)
            {
                continue; // current-gear region not sampled yet; keep scanning
            }

            float powerAfter = PowerAt(rpm * stepDown);
            if (powerAfter <= 0f)
            {
                // Next gear's post-shift RPM range has never been sampled:
                // refuse to guess for this gear rather than default to redline.
                return null;
            }

            // Shift only when the next gear makes strictly more power: equal
            // power means no benefit, and treating it as a crossover would
            // make flat power curves "shift at idle".
            if (powerNow < powerAfter)
            {
                return rpm;
            }
        }

        return maxRpm; // no crossover: redline is optimal for this gear
    }

    /// <summary>Bucket-interpolated power at an RPM; 0 where the curve has no samples.</summary>
    private float PowerAt(float rpm)
    {
        var buckets = _curve.Buckets;
        if (buckets.Count == 0 || rpm < 0f)
        {
            return 0f;
        }

        float position = rpm / PowerCurveTracker.BucketRpm;
        int lower = (int)position;
        if (lower >= buckets.Count - 1)
        {
            return buckets[^1];
        }

        float fraction = position - lower;
        return buckets[lower] + (buckets[lower + 1] - buckets[lower]) * fraction;
    }

    private float PeakPowerRpm()
    {
        var buckets = _curve.Buckets;
        int peakIndex = 0;
        for (int i = 1; i < buckets.Count; i++)
        {
            if (buckets[i] > buckets[peakIndex])
            {
                peakIndex = i;
            }
        }

        return peakIndex * PowerCurveTracker.BucketRpm;
    }
}

namespace Fh6Hud.Telemetry;

/// <summary>
/// Learns each gear's ratio from telemetry as RPM per m/s
/// (<c>CurrentEngineRpm / SpeedMs</c>). Gear ratios are not part of the Data
/// Out packet, but for shift-point calculation only their <i>relative</i> size
/// matters, and rpm-per-speed is proportional to the true overall ratio.
/// </summary>
/// <remarks>
/// A sample is accepted only while the clutch is fully engaged, the car is
/// moving, and the driven wheels are within the grip limit — clutch slip or
/// wheel spin would corrupt the average. Each gear keeps a running mean that
/// saturates at <see cref="MaxSamplesPerGear"/> samples and then behaves like
/// a slow exponential average, so an in-game gearing change (tuning) gradually
/// re-converges without a reset. Call <see cref="Reset"/> on a car switch.
/// </remarks>
public sealed class GearRatioTracker
{
    /// <summary>Samples per gear before the mean saturates (~3.3s at 60 Hz in one gear).</summary>
    public const int MaxSamplesPerGear = 200;

    /// <summary>Minimum samples before <see cref="GetRatio"/> trusts a gear.</summary>
    public const int MinSamplesPerGear = 10;

    private const float MinSpeedMs = 2f;

    // Spec: tire slip ratio = 0 means 100% grip, |ratio| > 1.0 means loss of grip.
    private const float MaxSlipRatio = 1.0f;

    private readonly Dictionary<int, GearStats> _byGear = new();
    private int _version;

    private struct GearStats
    {
        public double Sum;
        public int Count;
    }

    /// <summary>Increments whenever an accepted sample (or a reset) changes the learned ratios.</summary>
    public int Version => _version;

    /// <summary>
    /// True for gears the HUD can reason about: forward manual gears 1-19.
    /// 0 = neutral, 20 = reverse, 21 = drive (D — the actual gear is not
    /// reported, so no ratio can be learned), 22 = park, as in earlier titles.
    /// </summary>
    public static bool IsLearnableGear(byte gear) => gear is >= 1 and < 20;

    /// <summary>Records one telemetry packet. Returns true when the sample was accepted.</summary>
    public bool AddSample(Fh6Packet packet)
    {
        if (!IsLearnableGear(packet.Gear)
            || packet.Clutch != 0 // clutch pedal pressed (0..255, 0 = released)
            || packet.SpeedMs < MinSpeedMs
            || packet.CurrentEngineRpm <= 0f)
        {
            return false;
        }

        float slip = MaxDrivenSlip(packet);
        if (slip > MaxSlipRatio)
        {
            return false;
        }

        float ratio = packet.CurrentEngineRpm / packet.SpeedMs;
        if (!float.IsFinite(ratio) || ratio <= 0f)
        {
            return false;
        }

        _byGear.TryGetValue(packet.Gear, out var stats);
        if (stats.Count < MaxSamplesPerGear)
        {
            stats.Sum += ratio;
            stats.Count++;
        }
        else
        {
            // Saturated: keep Count fixed so the mean decays toward new samples
            // (mean' = mean + (x - mean) / n  <=>  Sum' = Sum + x - Sum / n).
            stats.Sum += ratio - stats.Sum / stats.Count;
        }

        _byGear[packet.Gear] = stats;
        _version++;
        return true;
    }

    /// <summary>Learned rpm-per-(m/s) for a gear, or null while there is too little data.</summary>
    public float? GetRatio(int gear)
    {
        if (_byGear.TryGetValue(gear, out var stats) && stats.Count >= MinSamplesPerGear)
        {
            return (float)(stats.Sum / stats.Count);
        }

        return null;
    }

    public int GetSampleCount(int gear) =>
        _byGear.TryGetValue(gear, out var stats) ? stats.Count : 0;

    /// <summary>Drops all learned ratios (used on car switch, like the power curve).</summary>
    public void Reset()
    {
        _byGear.Clear();
        _version++;
    }

    /// <summary>
    /// Highest |slip ratio| across the driven wheels. DrivetrainType:
    /// 0 = FWD, 1 = RWD, 2 = AWD. Wheel spin only corrupts the rpm/speed ratio
    /// on driven wheels, so free-rolling wheels are ignored.
    /// </summary>
    private static float MaxDrivenSlip(Fh6Packet p)
    {
        float front = Math.Max(Math.Abs(p.TireSlipRatioFrontLeft), Math.Abs(p.TireSlipRatioFrontRight));
        float rear = Math.Max(Math.Abs(p.TireSlipRatioRearLeft), Math.Abs(p.TireSlipRatioRearRight));
        return p.DrivetrainType switch
        {
            0 => front,
            1 => rear,
            _ => Math.Max(front, rear), // AWD (2) and unknown values: be strict
        };
    }
}

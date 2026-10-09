namespace Fh6Hud.Telemetry;

/// <summary>
/// Tracks engine power (watts) per RPM bucket so a dyno-style power curve and
/// the max power can be displayed and used for shift advice.
/// </summary>
/// <remarks>
/// Two ways in:
/// <list type="bullet">
/// <item><see cref="AddSample"/> raises a bucket's peak immediately (max-hold).
/// It is the low-level primitive and never forgets a value on its own.</item>
/// <item><see cref="AddPullSample"/> is the telemetry path. Samples are grouped
/// into full-throttle <i>pulls</i>; when a pull ends (throttle released or
/// <see cref="EndPull"/>), its per-bucket peaks overwrite the stored buckets it
/// touched. The newest pull wins, so a grip spike or a de-tune is replaced by
/// the next normal pull instead of living forever as an all-time maximum.
/// Buckets the newest pull did not reach keep their older values.</item>
/// </list>
/// </remarks>
public sealed class PowerCurveTracker
{
    public const float WattsPerPs = 735.49875f;
    public const float BucketRpm = 100f;

    /// <summary>Accelerator byte at or above which a sample counts as full throttle (~98%).</summary>
    public const byte FullThrottleAccel = 250;

    /// <summary>
    /// Minimum samples in a pull before it may replace stored data. At ~60 Hz
    /// this is about 0.17 s, so a one-frame throttle tap cannot overwrite the curve.
    /// </summary>
    public const int MinSamplesPerPull = 10;

    private const int GearSlots = GearRatioTracker.MaxForwardGear + 1;

    private float[] _powerByBucket = Array.Empty<float>();
    private float _maxRpm;
    private float _maxPowerW;
    private float _maxPowerRpm;
    private bool _dirty;
    private int _version;

    // Current full-throttle pull, not yet committed to _powerByBucket.
    private float[] _pendingByBucket = Array.Empty<float>();
    private readonly List<int> _pendingBuckets = new();
    private int _pendingSamples;
    private int _pendingGearMask;

    // Committed pulls per forward gear (index = gear).
    private readonly int[] _pullsByGear = new int[GearSlots];

    public float MaxRpm => _maxRpm;

    public float MaxPowerW => _maxPowerW;

    public float MaxPowerPs => _maxPowerW / WattsPerPs;

    /// <summary>
    /// RPM at which the max power was sampled (bucket midpoint — buckets are
    /// <see cref="BucketRpm"/> wide, so this is an estimate at bucket
    /// resolution). 0 until a sample arrived.
    /// </summary>
    public float MaxPowerRpm => _maxPowerRpm;

    public bool IsDirty
    {
        get => _dirty;
        set => _dirty = value;
    }

    /// <summary>
    /// Increments whenever the committed curve changes (accepted sample, pull
    /// commit, configure, reset). Lets consumers like <see cref="ShiftPointAdvisor"/>
    /// cache derived results without touching <see cref="IsDirty"/>, which the UI owns.
    /// </summary>
    public int Version => _version;

    public int BucketCount => _powerByBucket.Length;

    public IReadOnlyList<float> Buckets => _powerByBucket;

    /// <summary>
    /// Number of committed full-throttle pulls in which the gear was in use.
    /// Used to show how far shift learning has progressed for that gear.
    /// </summary>
    public int GetPullCount(int gear) =>
        gear >= 1 && gear < GearSlots ? _pullsByGear[gear] : 0;

    /// <summary>Reconfigures buckets if max RPM changed (e.g. different car). Resets all data.</summary>
    public void Configure(float maxRpm)
    {
        if (maxRpm <= 0 || Math.Abs(maxRpm - _maxRpm) < 1f)
        {
            return;
        }

        _maxRpm = maxRpm;
        int count = (int)(maxRpm / BucketRpm) + 1;
        _powerByBucket = new float[count];
        _maxPowerW = 0;
        _maxPowerRpm = 0;
        ClearPull();
        _dirty = true;
        _version++;
    }

    /// <summary>
    /// Drops all sampled data, pending pull and pull counts, and forgets the
    /// configured max RPM, so the next <see cref="Configure"/> call
    /// re-initializes from scratch. Used on car switch: two cars may share a
    /// redline, so <c>Configure</c> alone would keep the previous car's curve.
    /// </summary>
    public void Reset()
    {
        _powerByBucket = Array.Empty<float>();
        _maxRpm = 0;
        _maxPowerW = 0;
        _maxPowerRpm = 0;
        ClearPull();
        Array.Clear(_pullsByGear);
        _dirty = true;
        _version++;
    }

    /// <summary>
    /// Records a sample directly into its bucket (max-hold). Returns true if a
    /// bucket peak increased. Telemetry should use <see cref="AddPullSample"/>.
    /// </summary>
    public bool AddSample(float rpm, float powerW)
    {
        if (_powerByBucket.Length == 0 || rpm < 0)
        {
            return false;
        }

        int idx = BucketIndex(rpm);
        if (!(powerW > _powerByBucket[idx]))
        {
            return false;
        }

        _powerByBucket[idx] = powerW;
        if (powerW > _maxPowerW)
        {
            _maxPowerW = powerW;
            // The peak landed somewhere inside the bucket; the midpoint is the
            // best estimate at bucket resolution.
            _maxPowerRpm = Math.Min(_maxRpm, idx * BucketRpm + BucketRpm / 2f);
        }

        _dirty = true;
        _version++;
        return true;
    }

    /// <summary>
    /// Records one telemetry sample. Full-throttle samples accumulate into the
    /// current pull (per-bucket peak). A sample below full throttle ends the
    /// pull, which commits it if it was long enough (see <see cref="EndPull"/>).
    /// </summary>
    public void AddPullSample(float rpm, float powerW, byte accel, byte gear)
    {
        if (_powerByBucket.Length == 0 || rpm < 0)
        {
            return;
        }

        if (accel < FullThrottleAccel)
        {
            EndPull();
            return;
        }

        if (!(powerW > 0f))
        {
            return;
        }

        int idx = BucketIndex(rpm);
        if (!(powerW > _pendingByBucket[idx]))
        {
            // Still counts toward the pull's length even if it is not a new peak.
            _pendingSamples++;
            return;
        }

        if (_pendingByBucket[idx] <= 0f)
        {
            _pendingBuckets.Add(idx);
        }

        _pendingByBucket[idx] = powerW;
        _pendingSamples++;
        if (GearRatioTracker.IsLearnableGear(gear))
        {
            _pendingGearMask |= 1 << gear;
        }
    }

    /// <summary>
    /// Ends the current pull. A pull with at least <see cref="MinSamplesPerPull"/>
    /// samples replaces the buckets it reached and bumps <see cref="Version"/>;
    /// a shorter one is discarded. Call when telemetry goes stale so a pull
    /// never spans a menu or loading screen.
    /// </summary>
    public void EndPull()
    {
        if (_pendingSamples == 0)
        {
            return;
        }

        if (_pendingSamples >= MinSamplesPerPull)
        {
            CommitPull();
        }

        ClearPull();
    }

    public static float WattsToPs(float watts) => watts / WattsPerPs;

    private int BucketIndex(float rpm)
    {
        int idx = (int)(rpm / BucketRpm);
        return Math.Min(idx, _powerByBucket.Length - 1);
    }

    private void CommitPull()
    {
        foreach (int idx in _pendingBuckets)
        {
            _powerByBucket[idx] = _pendingByBucket[idx];
        }

        for (int gear = 1; gear < GearSlots; gear++)
        {
            if ((_pendingGearMask & (1 << gear)) != 0)
            {
                _pullsByGear[gear]++;
            }
        }

        RecalculateMaxPower();
        _dirty = true;
        _version++;
    }

    private void RecalculateMaxPower()
    {
        _maxPowerW = 0;
        _maxPowerRpm = 0;
        for (int idx = 0; idx < _powerByBucket.Length; idx++)
        {
            if (_powerByBucket[idx] > _maxPowerW)
            {
                _maxPowerW = _powerByBucket[idx];
                _maxPowerRpm = Math.Min(_maxRpm, idx * BucketRpm + BucketRpm / 2f);
            }
        }
    }

    private void ClearPull()
    {
        if (_pendingByBucket.Length != _powerByBucket.Length)
        {
            _pendingByBucket = new float[_powerByBucket.Length];
        }
        else
        {
            foreach (int idx in _pendingBuckets)
            {
                _pendingByBucket[idx] = 0f;
            }
        }

        _pendingBuckets.Clear();
        _pendingSamples = 0;
        _pendingGearMask = 0;
    }
}

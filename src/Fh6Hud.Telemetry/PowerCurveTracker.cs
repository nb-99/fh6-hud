namespace Fh6Hud.Telemetry;

/// <summary>
/// Tracks engine power (watts) per RPM bucket so a dyno-style power curve and
/// the max power can be displayed and used for shift advice. The curve updates
/// live while driving at full throttle.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Observe"/> is the telemetry path. Each full-throttle sample
/// updates its bucket immediately. A bucket takes the value from the most
/// recent full-throttle <i>run</i> that reached it (newest wins); within one
/// run it keeps the highest value. Buckets a run did not reach keep their
/// older values.</item>
/// <item>Power is ignored while boost is still building: after a long release
/// (more than <see cref="ShortLiftMs"/>), the first <see cref="SettleMs"/> of
/// renewed full throttle are not recorded. A short lift, such as a gear change,
/// counts as continuous and records immediately.</item>
/// <item><see cref="AddSample"/> is the low-level max-hold primitive used by
/// tests and tooling. It does not apply the run or settle rules.</item>
/// </list>
/// </remarks>
public sealed class PowerCurveTracker
{
    public const float WattsPerPs = 735.49875f;
    public const float BucketRpm = 100f;

    /// <summary>Accelerator byte at or above which a sample counts as full throttle (~98%).</summary>
    public const byte FullThrottleAccel = 250;

    /// <summary>Time after a long release before renewed full throttle is recorded (boost build-up).</summary>
    public const int SettleMs = 1000;

    /// <summary>Releases shorter than this (e.g. a gear change) do not restart settling.</summary>
    public const int ShortLiftMs = 500;

    private const int GearSlots = GearRatioTracker.MaxForwardGear + 1;

    private float[] _powerByBucket = Array.Empty<float>();
    private int[] _runByBucket = Array.Empty<int>();
    private float _maxRpm;
    private float _maxPowerW;
    private float _maxPowerRpm;
    private bool _dirty;
    private int _version;

    // Full-throttle run state (see Observe).
    private int _runId;
    private bool _fullThrottle;
    private bool _hasRelease;
    private uint _releasedAtMs;
    private bool _settled;
    private uint _settleAtMs;

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
    /// Increments whenever the curve changes (accepted sample, configure, reset).
    /// Lets consumers like <see cref="ShiftPointAdvisor"/> cache derived results
    /// without touching <see cref="IsDirty"/>, which the UI owns.
    /// </summary>
    public int Version => _version;

    public int BucketCount => _powerByBucket.Length;

    /// <summary>Per-bucket peak power in watts. 0 means the bucket has no sample.</summary>
    public IReadOnlyList<float> Buckets => _powerByBucket;

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
        _runByBucket = new int[count];
        _maxPowerW = 0;
        _maxPowerRpm = 0;
        EndRun();
        _dirty = true;
        _version++;
    }

    /// <summary>
    /// Drops all sampled data and forgets the configured max RPM, so the next
    /// <see cref="Configure"/> call re-initializes from scratch. Used on car
    /// switch: two cars may share a redline, so <c>Configure</c> alone would
    /// keep the previous car's curve.
    /// </summary>
    public void Reset()
    {
        _powerByBucket = Array.Empty<float>();
        _runByBucket = Array.Empty<int>();
        _maxRpm = 0;
        _maxPowerW = 0;
        _maxPowerRpm = 0;
        EndRun();
        _dirty = true;
        _version++;
    }

    /// <summary>
    /// Records a sample directly into its bucket (max-hold). Returns true if a
    /// bucket peak increased. Telemetry should use <see cref="Observe"/>.
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
    /// Records one telemetry packet. Call once per UI frame with the latest
    /// packet; repeated calls with the same packet change nothing.
    /// </summary>
    public void Observe(Fh6Packet packet)
    {
        if (_powerByBucket.Length == 0 || packet.CurrentEngineRpm < 0f)
        {
            return;
        }

        if (packet.Accel < FullThrottleAccel)
        {
            if (_fullThrottle)
            {
                _fullThrottle = false;
                _hasRelease = true;
                _releasedAtMs = packet.TimestampMs;
            }

            return;
        }

        if (!_fullThrottle)
        {
            StartRun(packet.TimestampMs);
        }

        if (!_settled)
        {
            if (ElapsedMs(_settleAtMs, packet.TimestampMs) < 0)
            {
                return; // boost still building
            }

            _settled = true;
        }

        if (!(packet.PowerWatts > 0f))
        {
            return;
        }

        int idx = BucketIndex(packet.CurrentEngineRpm);
        if (_runByBucket[idx] != _runId)
        {
            // First settled sample of this run in this bucket: newest run wins.
            _powerByBucket[idx] = packet.PowerWatts;
            _runByBucket[idx] = _runId;
        }
        else if (packet.PowerWatts > _powerByBucket[idx])
        {
            _powerByBucket[idx] = packet.PowerWatts;
        }
        else
        {
            return;
        }

        RecalculateMaxPower();
        _dirty = true;
        _version++;
    }

    /// <summary>
    /// Forgets the current full-throttle run, so the next full throttle waits
    /// for boost to build again. Call when telemetry goes stale (menu, loading).
    /// </summary>
    public void EndRun()
    {
        _fullThrottle = false;
        _hasRelease = false;
        _settled = false;
    }

    /// <summary>
    /// Power at an RPM, interpolated between the nearest sampled buckets on
    /// either side. A bucket with no sample (value 0) is unknown, not zero
    /// power: it is bridged rather than read as a dip. Returns 0 below the
    /// first sampled bucket and above the last one, where there is no data.
    /// </summary>
    public float PowerAt(float rpm)
    {
        int len = _powerByBucket.Length;
        if (len == 0 || rpm < 0f)
        {
            return 0f;
        }

        float position = Math.Min(rpm / BucketRpm, len - 1);
        int lower = (int)position;
        while (lower >= 0 && _powerByBucket[lower] <= 0f)
        {
            lower--;
        }

        if (lower < 0)
        {
            return 0f;
        }

        if (position - lower <= 0f)
        {
            return _powerByBucket[lower];
        }

        int upper = lower + 1;
        while (upper < len && _powerByBucket[upper] <= 0f)
        {
            upper++;
        }

        if (upper >= len)
        {
            return 0f;
        }

        float fraction = (position - lower) / (upper - lower);
        return _powerByBucket[lower] + (_powerByBucket[upper] - _powerByBucket[lower]) * fraction;
    }

    public static float WattsToPs(float watts) => watts / WattsPerPs;

    private void StartRun(uint nowMs)
    {
        _fullThrottle = true;
        _runId++;

        // A short lift (gear change) keeps the boost up: record right away.
        // Otherwise wait for the boost to build.
        bool shortLift = _hasRelease && ElapsedMs(_releasedAtMs, nowMs) < ShortLiftMs;
        _settled = shortLift;
        _settleAtMs = unchecked(nowMs + (uint)SettleMs);
    }

    /// <summary>
    /// Milliseconds from <paramref name="startMs"/> to <paramref name="nowMs"/>,
    /// correct across the U32 timestamp wrap. Negative values mean "not yet".
    /// </summary>
    private static long ElapsedMs(uint startMs, uint nowMs)
    {
        uint delta = unchecked(nowMs - startMs);
        return delta > int.MaxValue ? -(long)(uint.MaxValue - delta) - 1 : delta;
    }

    private int BucketIndex(float rpm)
    {
        int idx = (int)(rpm / BucketRpm);
        return Math.Min(idx, _powerByBucket.Length - 1);
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
}

namespace Fh6Hud.Telemetry;

/// <summary>
/// Tracks engine power (watts) per RPM bucket so a dyno-style power curve and
/// the max power can be displayed and used for shift advice. The curve updates
/// live while driving at full throttle.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Observe"/> is the telemetry path. Each full-throttle sample
/// is recorded into its 100 RPM bucket, which keeps its last
/// <see cref="SamplesPerBucket"/> samples. The bucket's shown value is the
/// <b>median</b> of those samples. One odd reading (a dip at a gear change, a
/// grip spike, boost still building) is outvoted by its neighbours in time,
/// while a real change such as a detune still takes over within a few passes
/// through that RPM.</item>
/// <item>Power is ignored while boost is still building: after a long release
/// (more than <see cref="ShortLiftMs"/>), the first <see cref="SettleMs"/> of
/// renewed full throttle are not recorded. A short lift, such as a gear change,
/// counts as continuous and records immediately.</item>
/// <item><see cref="AddSample"/> records one sample without the settle rules,
/// for tests and tooling.</item>
/// </list>
/// </remarks>
public sealed class PowerCurveTracker
{
    public const float WattsPerPs = 735.49875f;
    public const float BucketRpm = 100f;

    /// <summary>Accelerator byte at or above which a sample counts as full throttle (~98%).</summary>
    public const byte FullThrottleAccel = 250;

    /// <summary>Number of recent samples kept per bucket; the shown value is their median.</summary>
    public const int SamplesPerBucket = 5;

    /// <summary>Time after a long release before renewed full throttle is recorded (boost build-up).</summary>
    public const int SettleMs = 1000;

    /// <summary>Releases shorter than this (e.g. a gear change) do not restart settling.</summary>
    public const int ShortLiftMs = 500;

    private float[] _powerByBucket = Array.Empty<float>();

    // Ring buffer of recent samples: bucket i owns [i * SamplesPerBucket, +SamplesPerBucket).
    private float[] _samples = Array.Empty<float>();
    private int[] _sampleCount = Array.Empty<int>();
    private int[] _sampleNext = Array.Empty<int>();

    private float _maxRpm;
    private float _maxPowerW;
    private float _maxPowerRpm;
    private bool _dirty;
    private int _version;

    // Full-throttle state (see Observe).
    private bool _hasObserved;
    private uint _lastObservedMs;
    private bool _fullThrottle;
    private bool _hasRelease;
    private uint _releasedAtMs;
    private bool _settled;
    private uint _settleAtMs;

    public float MaxRpm => _maxRpm;

    public float MaxPowerW => _maxPowerW;

    public float MaxPowerPs => _maxPowerW / WattsPerPs;

    /// <summary>
    /// RPM at which the max power is shown (bucket midpoint — buckets are
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

    /// <summary>Per-bucket power in watts (median of recent samples). 0 means the bucket has no sample.</summary>
    public IReadOnlyList<float> Buckets => _powerByBucket;

    /// <summary>Reconfigures buckets if max RPM changed (e.g. different car). Resets all data.</summary>
    public void Configure(float maxRpm)
    {
        if (maxRpm <= 0 || Math.Abs(maxRpm - _maxRpm) < 1f)
        {
            return;
        }

        _maxRpm = maxRpm;
        AllocateBuckets((int)(maxRpm / BucketRpm) + 1);
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
        AllocateBuckets(0);
        _maxRpm = 0;
        _maxPowerW = 0;
        _maxPowerRpm = 0;
        EndRun();
        _dirty = true;
        _version++;
    }

    /// <summary>
    /// Records one power sample into its bucket, without the settle rules.
    /// Returns false when the curve is not configured or the power is not positive.
    /// </summary>
    public bool AddSample(float rpm, float powerW)
    {
        if (_powerByBucket.Length == 0 || rpm < 0 || !(powerW > 0f))
        {
            return false;
        }

        Record(BucketIndex(rpm), powerW);
        return true;
    }

    /// <summary>
    /// Records one telemetry packet. Call once per UI frame with the latest
    /// packet; a packet already seen (same timestamp) changes nothing.
    /// </summary>
    public void Observe(Fh6Packet packet)
    {
        if (_powerByBucket.Length == 0 || packet.CurrentEngineRpm < 0f)
        {
            return;
        }

        if (_hasObserved && packet.TimestampMs == _lastObservedMs)
        {
            return; // the UI re-reads the latest packet every frame
        }

        _hasObserved = true;
        _lastObservedMs = packet.TimestampMs;

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

        Record(BucketIndex(packet.CurrentEngineRpm), packet.PowerWatts);
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

    private void AllocateBuckets(int count)
    {
        _powerByBucket = new float[count];
        _samples = new float[count * SamplesPerBucket];
        _sampleCount = new int[count];
        _sampleNext = new int[count];
    }

    private void StartRun(uint nowMs)
    {
        _fullThrottle = true;

        // A short lift (gear change) keeps the boost up: record right away.
        // Otherwise wait for the boost to build.
        bool shortLift = _hasRelease && ElapsedMs(_releasedAtMs, nowMs) < ShortLiftMs;
        _settled = shortLift;
        _settleAtMs = unchecked(nowMs + (uint)SettleMs);
    }

    /// <summary>
    /// Adds a sample to a bucket's window and updates the bucket to the median
    /// of that window, then refreshes max power.
    /// </summary>
    private void Record(int idx, float powerW)
    {
        int baseIdx = idx * SamplesPerBucket;
        _samples[baseIdx + _sampleNext[idx]] = powerW;
        _sampleNext[idx] = (_sampleNext[idx] + 1) % SamplesPerBucket;
        _sampleCount[idx] = Math.Min(_sampleCount[idx] + 1, SamplesPerBucket);

        _powerByBucket[idx] = MedianOf(baseIdx, _sampleCount[idx]);
        RecalculateMaxPower();
        _dirty = true;
        _version++;
    }

    /// <summary>Median of the first <paramref name="count"/> samples starting at <paramref name="start"/>.</summary>
    private float MedianOf(int start, int count)
    {
        Span<float> sorted = stackalloc float[SamplesPerBucket];
        for (int i = 0; i < count; i++)
        {
            float value = _samples[start + i];
            int j = i - 1;
            while (j >= 0 && sorted[j] > value)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }

            sorted[j + 1] = value;
        }

        return count % 2 == 1
            ? sorted[count / 2]
            : (sorted[count / 2 - 1] + sorted[count / 2]) / 2f;
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

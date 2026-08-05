namespace Fh6Hud.Telemetry;

/// <summary>
/// Accumulates peak engine power (watts) per RPM bucket from telemetry
/// samples so a dyno-style power curve and the max power can be displayed.
/// Max power is the highest sampled power; the curve is the per-bucket peak.
/// </summary>
public sealed class PowerCurveTracker
{
    public const float WattsPerPs = 735.49875f;
    private const float BucketRpm = 100f;

    private float[] _powerByBucket = Array.Empty<float>();
    private float _maxRpm;
    private float _maxPowerW;
    private bool _dirty;

    public float MaxRpm => _maxRpm;

    public float MaxPowerW => _maxPowerW;

    public float MaxPowerPs => _maxPowerW / WattsPerPs;

    public bool IsDirty
    {
        get => _dirty;
        set => _dirty = value;
    }

    public int BucketCount => _powerByBucket.Length;

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
        _maxPowerW = 0;
        _dirty = true;
    }

    /// <summary>Records a sample. Returns true if a bucket peak increased.</summary>
    public bool AddSample(float rpm, float powerW)
    {
        if (_powerByBucket.Length == 0 || rpm < 0)
        {
            return false;
        }

        int idx = (int)(rpm / BucketRpm);
        if (idx >= _powerByBucket.Length)
        {
            idx = _powerByBucket.Length - 1;
        }

        if (!(powerW > _powerByBucket[idx]))
        {
            return false;
        }

        _powerByBucket[idx] = powerW;
        if (powerW > _maxPowerW)
        {
            _maxPowerW = powerW;
        }

        _dirty = true;
        return true;
    }

    public static float WattsToPs(float watts) => watts / WattsPerPs;
}
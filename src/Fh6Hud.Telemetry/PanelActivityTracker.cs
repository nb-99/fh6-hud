using System.Runtime.InteropServices;

namespace Fh6Hud;

/// <summary>
/// Remembers when a panel's relevant telemetry last changed, so the panel can
/// auto-hide while its data is stale (e.g. tire temps in the tuning menu).
/// A value counts as changed only when it moves more than the tolerance away
/// from the last accepted value, so sensor jitter does not keep a panel alive
/// and slow drift still registers once it adds up.
/// </summary>
public sealed class PanelActivityTracker
{
    private readonly float _tolerance;
    private Sample _reference;
    private double _lastChangeSeconds;
    private bool _hasReference;

    public PanelActivityTracker(float tolerance)
    {
        _tolerance = tolerance;
    }

    /// <summary>Records a sample taken at <paramref name="nowSeconds"/> (monotonic clock).</summary>
    public void Observe(Sample sample, double nowSeconds)
    {
        if (!_hasReference || Differs(sample, _reference, _tolerance))
        {
            _reference = sample;
            _lastChangeSeconds = nowSeconds;
            _hasReference = true;
        }
    }

    /// <summary>True when nothing has changed for at least <paramref name="staleAfterSeconds"/>.</summary>
    public bool IsStale(double nowSeconds, double staleAfterSeconds) =>
        _hasReference && nowSeconds - _lastChangeSeconds >= staleAfterSeconds;

    /// <summary>Forgets the history, e.g. when the car changes.</summary>
    public void Reset() => _hasReference = false;

    private static bool Differs(Sample a, Sample b, float tolerance) =>
        DiffersSlot(a.A, b.A, tolerance)
        || DiffersSlot(a.B, b.B, tolerance)
        || DiffersSlot(a.C, b.C, tolerance)
        || DiffersSlot(a.D, b.D, tolerance);

    private static bool DiffersSlot(float a, float b, float tolerance)
    {
        if (float.IsNaN(a) || float.IsNaN(b))
        {
            // NaN means "no data"; NaN -> NaN is not a change, NaN <-> number is.
            return float.IsNaN(a) != float.IsNaN(b);
        }

        return MathF.Abs(a - b) > tolerance;
    }

    /// <summary>
    /// Up to four telemetry values a panel depends on. Unused slots stay 0 and
    /// are constant, so they never register as a change.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct Sample(float A, float B = 0f, float C = 0f, float D = 0f);
}

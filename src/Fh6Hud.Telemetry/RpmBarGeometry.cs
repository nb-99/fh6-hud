namespace Fh6Hud.Telemetry;

/// <summary>
/// Pure geometry for the engine panel's rev bar, extracted so the bar's
/// behavior can be unit-tested without WPF. The bar is a track with a fixed
/// redline zone at the right end (the top <see cref="RedlineZoneFraction"/>
/// of the rev range) and a blue fill that grows from the left with current
/// RPM. The zone is rendered on top of the fill (it is the last child of the
/// track grid), so the blue slides underneath it and the red band always
/// stays a fixed, fully visible strip — it never shrinks or moves.
/// </summary>
public static class RpmBarGeometry
{
    /// <summary>Fraction of the track occupied by the redline zone (90%..100% of max RPM).</summary>
    public const double RedlineZoneFraction = 0.10;

    /// <summary>Fraction of the track where the redline zone begins (its left edge).</summary>
    public static double RedlineStartFraction => 1.0 - RedlineZoneFraction;

    /// <summary>
    /// Fill fraction (0..1) for the current-RPM bar: 0 at idle, 1 at redline.
    /// 0 when max RPM is unknown.
    /// </summary>
    public static double FillFraction(double rpm, double maxRpm) =>
        maxRpm > 0 ? Math.Clamp(rpm / maxRpm, 0.0, 1.0) : 0.0;

    /// <summary>
    /// Width fraction of the blue fill: like <see cref="FillFraction"/>, but
    /// clamped to stop at the redline zone's left edge. The fill must never
    /// cross under the red band — the zone sits on top of the fill (last
    /// child of the track grid) and is opaque, so an overlap would be hidden
    /// behind it and the blue would appear to stop at the wrong place.
    /// </summary>
    public static double FillWidthFraction(double rpm, double maxRpm) =>
        Math.Min(FillFraction(rpm, maxRpm), RedlineStartFraction);
}

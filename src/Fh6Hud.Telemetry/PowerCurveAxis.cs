using System.Globalization;

namespace Fh6Hud.Telemetry;

/// <summary>
/// Pure axis math for the engine panel's power curve: maps RPM and power to
/// canvas coordinates and produces the grid ticks/labels. Kept free of WPF so
/// the curve, the power dot and the grid all share one transform and the
/// layout can be unit-tested.
/// </summary>
public static class PowerCurveAxis
{
    /// <summary>Spacing of the faint vertical grid lines.</summary>
    public const float RpmMinorStep = 500f;

    /// <summary>Spacing of the labelled vertical grid lines.</summary>
    public const float RpmMajorStep = 1000f;

    /// <summary>Aim for at most this many horizontal power lines.</summary>
    private const double MaxPowerLines = 5.0;

    /// <summary>Candidate horizontal grid spacings in PS, smallest first.</summary>
    private static readonly float[] PowerStepsPs = { 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2500, 5000 };

    /// <summary>A vertical grid line. <see cref="Label"/> is set only on major lines.</summary>
    public readonly record struct RpmTick(double X, bool Major, string? Label);

    /// <summary>A horizontal grid line labelled in PS.</summary>
    public readonly record struct PowerTick(double Y, string Label);

    /// <summary>
    /// X coordinate for an RPM value across a canvas of the given width. The
    /// scale is linear from 0 to <paramref name="maxRpm"/>; 0 when unknown.
    /// </summary>
    public static double XForRpm(double rpm, double maxRpm, double width) =>
        maxRpm > 0 ? Math.Clamp(rpm / maxRpm, 0.0, 1.0) * width : 0.0;

    /// <summary>
    /// Y coordinate for a power value (watts) on a canvas of the given height,
    /// with 0 at the bottom and <paramref name="maxPowerW"/> at the top.
    /// </summary>
    public static double YForPower(double powerW, double maxPowerW, double height) =>
        maxPowerW > 0 ? height - Math.Clamp(powerW / maxPowerW, 0.0, 1.0) * height : height;

    /// <summary>
    /// Vertical grid lines every <see cref="RpmMinorStep"/> up to and including
    /// max RPM. Every <see cref="RpmMajorStep"/> is a major line with a
    /// label such as "1k" or "6.5k".
    /// </summary>
    public static IReadOnlyList<RpmTick> RpmTicks(double maxRpm, double width)
    {
        var ticks = new List<RpmTick>();
        if (maxRpm <= 0 || width <= 0)
        {
            return ticks;
        }

        for (int k = 1; k * RpmMinorStep <= maxRpm + 0.5; k++)
        {
            double rpm = k * RpmMinorStep;
            bool major = Math.Abs(rpm % RpmMajorStep) < 0.5;
            ticks.Add(new RpmTick(
                XForRpm(rpm, maxRpm, width),
                major,
                major ? FormatRpm(rpm) : null));
        }

        return ticks;
    }

    /// <summary>
    /// Horizontal grid lines at round PS values strictly below the peak, with
    /// the spacing chosen so there are roughly 1 to <see cref="MaxPowerLines"/>
    /// lines. Empty while there is no power data.
    /// </summary>
    public static IReadOnlyList<PowerTick> PowerTicks(double maxPowerW, double height)
    {
        var ticks = new List<PowerTick>();
        double maxPs = maxPowerW / PowerCurveTracker.WattsPerPs;
        if (maxPs <= 0 || height <= 0)
        {
            return ticks;
        }

        float step = PowerStepsPs.FirstOrDefault(s => maxPs / s <= MaxPowerLines);
        if (step <= 0)
        {
            step = PowerStepsPs[^1];
        }

        for (int k = 1; k * step < maxPs; k++)
        {
            double ps = k * step;
            ticks.Add(new PowerTick(
                YForPower(ps * PowerCurveTracker.WattsPerPs, maxPowerW, height),
                $"{ps.ToString("F0", CultureInfo.InvariantCulture)} PS"));
        }

        return ticks;
    }

    private static string FormatRpm(double rpm) =>
        rpm >= 1000
            ? (rpm / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : rpm.ToString("F0", CultureInfo.InvariantCulture);
}

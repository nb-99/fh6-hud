namespace Fh6Hud.Telemetry;

/// <summary>
/// Deterministic progress model for the progressive upshift approach cue: a
/// row of six lights that fills from left to right while the engine is inside
/// the final 20% of the RPM distance to the advisor's optimal shift point.
/// Pure math only — no WPF types — so the thresholds and clamping rules are
/// unit-testable without a UI. The color grouping (two yellow, two orange,
/// two red) is derived here; the panel maps groups to brushes.
/// </summary>
/// <remarks>
/// Contract (from the shift-cue improvement specification):
/// - Approach window opens at <c>0.8 * shiftRpm</c>.
/// - Normalized progress is
///   <c>clamp((currentRpm - 0.8 * shiftRpm) / (0.2 * shiftRpm), 0, 1)</c>.
/// - The first light is active immediately when the cue enters the window.
/// - The active-light count is <c>ceil(progress * 6)</c>, with the first-light
///   rule handling the exact window boundary.
/// - Values are clamped and must behave safely for invalid or unavailable RPM
///   inputs (null/zero/negative/NaN/Infinity targets, NaN RPM).
/// A target of <c>EngineMaxRpm</c> (the advisor's redline fallback when no
/// power crossover exists) is just a normal target — no special casing here.
/// </remarks>
public static class UpshiftApproach
{
    /// <summary>Number of discrete lights in the approach cue.</summary>
    public const int LightCount = 6;

    /// <summary>RPM fraction of the shift point where the approach window opens.</summary>
    public const double WindowStartFraction = 0.8;

    /// <summary>Width of the approach window as a fraction of the shift point.</summary>
    public const double WindowFraction = 0.2;

    /// <summary>Color group of each light in the six-light progression.</summary>
    public enum LightGroup
    {
        Yellow,
        Orange,
        Red,
    }

    /// <summary>
    /// True while the engine is inside the approach window
    /// (<c>[0.8 * shiftRpm, shiftRpm)</c>). The terminal upshift latch owns
    /// the shift point itself, so the window is exclusive at the top.
    /// </summary>
    public static bool IsInWindow(float currentRpm, float? shiftRpm)
    {
        if (shiftRpm is not { } shift || !IsUsable(shift) || float.IsNaN(currentRpm))
        {
            return false;
        }

        return currentRpm >= WindowStartFraction * shift && currentRpm < shift;
    }

    /// <summary>
    /// Normalized progress through the approach window, clamped to
    /// <c>[0, 1]</c> — 0 at or below the window start, 1 at (or above) the
    /// shift point. 0 for invalid or unavailable inputs.
    /// </summary>
    public static float NormalizedProgress(float currentRpm, float? shiftRpm)
    {
        if (shiftRpm is not { } shift || !IsUsable(shift) || float.IsNaN(currentRpm))
        {
            return 0f;
        }

        double start = WindowStartFraction * shift;
        if (currentRpm < start)
        {
            return 0f;
        }

        double progress = (currentRpm - start) / (WindowFraction * shift);
        return (float)Math.Clamp(progress, 0d, 1d);
    }

    /// <summary>
    /// Number of active lights (0..6) for the given RPM and target: 0 outside
    /// the window or for invalid inputs, 1 immediately at the window start,
    /// and 6 at (or above) the shift point.
    /// </summary>
    public static int ActiveLightCount(float currentRpm, float? shiftRpm)
    {
        if (shiftRpm is not { } shift || !IsUsable(shift) || float.IsNaN(currentRpm))
        {
            return 0;
        }

        double start = WindowStartFraction * shift;
        if (currentRpm < start)
        {
            return 0;
        }

        double progress = Math.Clamp((currentRpm - start) / (WindowFraction * shift), 0d, 1d);
        return Math.Max(1, (int)Math.Ceiling(progress * LightCount));
    }

    /// <summary>
    /// Color group of a zero-based light index: indices 0-1 are yellow, 2-3
    /// orange, 4-5 red.
    /// </summary>
    public static LightGroup GroupOf(int lightIndex)
    {
        if (lightIndex < 0 || lightIndex >= LightCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lightIndex),
                lightIndex,
                $"Light index must be in [0, {LightCount - 1}].");
        }

        return lightIndex switch
        {
            < 2 => LightGroup.Yellow,
            < 4 => LightGroup.Orange,
            _ => LightGroup.Red,
        };
    }

    private static bool IsUsable(float shiftRpm) =>
        shiftRpm > 0f && !float.IsNaN(shiftRpm) && !float.IsInfinity(shiftRpm);
}

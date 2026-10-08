namespace Fh6Hud;

/// <summary>
/// Position and presentation of one HUD panel. <see cref="X"/>/<see cref="Y"/>
/// are fractions of the work area, so layouts survive resolution and DPI
/// changes; they locate the panel's <see cref="Anchor"/> point (0 = left/top
/// edge of the work area, 1 = right/bottom edge).
/// </summary>
public sealed class PanelPlacement
{
    public const double MinScale = 0.5;
    public const double MaxScale = 3.0;

    public double X { get; set; }

    public double Y { get; set; }

    public PanelAnchor Anchor { get; set; } = PanelAnchor.TopLeft;

    /// <summary>
    /// Uniform content scale (1.0 = design size). Panels are resized by
    /// scaling rather than stretching because their child layouts have fixed
    /// column and font sizes.
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Hidden through the right-click menu. Persisted across restarts.</summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Hide while the panel's own telemetry has not changed for
    /// <see cref="HudConfig.IdleHideSeconds"/>, and show again when it changes.
    /// </summary>
    public bool AutoHide { get; set; }

    /// <summary>
    /// Per-panel idle timeout in seconds. Null uses <see cref="HudConfig.IdleHideSeconds"/>.
    /// </summary>
    public double? IdleHideSeconds { get; set; }

    /// <summary>The idle timeout this panel uses, falling back to the global setting.</summary>
    public double EffectiveIdleHideSeconds(double globalSeconds) =>
        IdleHideSeconds is { } seconds && double.IsFinite(seconds) && seconds >= 0 ? seconds : globalSeconds;

    /// <summary>Clamps a scale to the supported range; non-finite values become 1.0.</summary>
    public static double ClampScale(double scale) =>
        double.IsFinite(scale) ? Math.Clamp(scale, MinScale, MaxScale) : 1.0;
}

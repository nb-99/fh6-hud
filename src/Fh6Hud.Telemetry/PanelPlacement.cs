namespace Fh6Hud;

/// <summary>Which point of a panel window its placement coordinates refer to.</summary>
public enum PanelAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCenter,
    BottomCenter,
}

/// <summary>Well-known keys for the HUD's panels in <see cref="HudConfig.Panels"/>.</summary>
public static class PanelKeys
{
    public const string Tires = "Tires";
    public const string Engine = "Engine";
    public const string Intervals = "Intervals";
    public const string Speedo = "Speedo";
    public const string Status = "Status";
}

/// <summary>
/// Position of one HUD panel as fractions of the work area, so layouts survive
/// resolution and DPI changes. <see cref="X"/>/<see cref="Y"/> locate the
/// panel's <see cref="Anchor"/> point (0 = left/top edge of the work area,
/// 1 = right/bottom edge).
/// </summary>
public sealed class PanelPlacement
{
    public double X { get; set; }

    public double Y { get; set; }

    public PanelAnchor Anchor { get; set; } = PanelAnchor.TopLeft;
}

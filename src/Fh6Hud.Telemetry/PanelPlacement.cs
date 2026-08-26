namespace Fh6Hud;

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

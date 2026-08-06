using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>Large current-speed readout with the gear chip.</summary>
public partial class SpeedoPanel : PanelWindow
{
    public SpeedoPanel(HudState state)
        : base(state, PanelKeys.Speedo)
    {
        InitializeComponent();
    }

    protected override void Render(Fh6Packet packet)
    {
        SetText(SpeedText, $"{packet.SpeedKmh:F0}");
        SetText(GearText, GearLabel(packet.Gear));
    }

    /// <summary>
    /// Maps the raw gear byte to a display label. 0 = neutral, 20 = reverse,
    /// 21 = drive (D), 22 = park (P) — inherited from earlier Forza titles;
    /// verify against live FH6 data before relying on these values.
    /// </summary>
    private static string GearLabel(byte gear) => gear switch
    {
        0 => "N",
        20 => "R",
        21 => "D",
        22 => "P",
        _ => gear.ToString(),
    };
}

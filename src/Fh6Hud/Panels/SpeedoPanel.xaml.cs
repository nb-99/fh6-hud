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
    /// Maps the raw FH6 gear byte to a display label. FH6 reports reverse as
    /// 0 and neutral as 11; forward gears are reported as 1 through 10.
    /// </summary>
    private static string GearLabel(byte gear) => gear switch
    {
        0 => "R",
        11 => "N",
        _ => gear.ToString(),
    };
}

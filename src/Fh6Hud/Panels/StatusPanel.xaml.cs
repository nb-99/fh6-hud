using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>
/// Title, connection status, and footer hints. Unlike the content panels this
/// one stays visible when there is no live data (it is the no-data chip and
/// keeps the right-click menu reachable).
/// </summary>
public partial class StatusPanel : PanelWindow
{
    private readonly SolidColorBrush _okBrush;
    private readonly SolidColorBrush _hotBrush;

    public StatusPanel(HudState state)
        : base(state, PanelKeys.Status)
    {
        InitializeComponent();

        _okBrush = (SolidColorBrush)FindResource("OkBrush");
        _hotBrush = (SolidColorBrush)FindResource("HotBrush");

        if (State.Config.LoadFailed)
        {
            FooterLeft.Text = "CONFIG INVALID  |  USING DEFAULTS";
        }
    }

    protected override bool HideWhenNoData => false;

    protected override void Render(Fh6Packet packet) => UpdateChrome();

    protected override void RenderNoData() => UpdateChrome();

    private void UpdateChrome()
    {
        if (State.ListenerError is { } error)
        {
            StatusDot.Fill = _hotBrush;
            SetText(StatusText, error);
        }
        else if (State.Live)
        {
            StatusDot.Fill = _okBrush;
            SetText(StatusText, $"UDP {State.Listener?.Port}  |  LIVE");
        }
        else
        {
            StatusDot.Fill = _hotBrush;
            SetText(StatusText, State.NoDataMessage);
        }

        SetText(ClickThroughHint, !HotkeyAvailable
            ? "CTRL+ALT+H  UNAVAILABLE"
            : ClickThrough ? "CTRL+ALT+H  TO RESTORE" : "CTRL+ALT+H  CLICK-THROUGH");
    }
}

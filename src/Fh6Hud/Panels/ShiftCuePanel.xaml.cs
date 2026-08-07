using System.Windows;
using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>
/// The live upshift/downshift recommendation in its own draggable panel. The
/// advisor thresholds and flashing cadence remain the same as the original
/// engine-panel cue.
/// </summary>
public partial class ShiftCuePanel : PanelWindow
{
    /// <summary>Throttle input (0-255) above which the upshift light is shown.</summary>
    private const byte UpshiftThrottleThreshold = 200;

    /// <summary>Throttle input (0-255) above which the downshift light is shown.</summary>
    private const byte DownshiftThrottleThreshold = 128;

    private readonly SolidColorBrush _coldBrush;
    private readonly SolidColorBrush _hotBrush;
    private readonly SolidColorBrush _mutedBrush;
    private readonly SolidColorBrush _cardBrush;
    private readonly SolidColorBrush _shiftUpFillBrush;
    private readonly SolidColorBrush _shiftDownFillBrush;

    public ShiftCuePanel(HudState state)
        : base(state, PanelKeys.ShiftCue)
    {
        InitializeComponent();

        _coldBrush = (SolidColorBrush)FindResource("ColdBrush");
        _hotBrush = (SolidColorBrush)FindResource("HotBrush");
        _mutedBrush = (SolidColorBrush)FindResource("MutedBrush");
        _cardBrush = (SolidColorBrush)FindResource("CardBrush");
        _shiftUpFillBrush = (SolidColorBrush)FindResource("ShiftUpFillBrush");
        _shiftDownFillBrush = (SolidColorBrush)FindResource("ShiftDownFillBrush");
    }

    protected override bool HideWhenNoData => false;

    protected override void Render(Fh6Packet packet)
    {
        bool up = State.ShiftAdvisor.ShouldUpshift(packet.Gear, packet.CurrentEngineRpm)
                   && packet.Accel >= UpshiftThrottleThreshold;
        bool down = State.ShiftAdvisor.ShouldDownshift(packet.Gear, packet.CurrentEngineRpm)
                    && packet.Accel >= DownshiftThrottleThreshold;

        if (!up && !down)
        {
            ShowPlaceholderOrHide();
            return;
        }

        Visibility = Visibility.Visible;
        ShiftCue.Visibility = (Environment.TickCount64 / 200) % 2 == 0
            ? Visibility.Visible
            : Visibility.Hidden;

        if (up)
        {
            ShiftCueArrow.Text = "▲";
            ShiftCueText.Text = "UPSHIFT";
            ShiftCueArrow.Foreground = _hotBrush;
            ShiftCueText.Foreground = _hotBrush;
            ShiftCue.Background = _shiftUpFillBrush;
            ShiftCue.BorderBrush = _hotBrush;
        }
        else
        {
            ShiftCueArrow.Text = "▼";
            ShiftCueText.Text = "DOWNSHIFT";
            ShiftCueArrow.Foreground = _coldBrush;
            ShiftCueText.Foreground = _coldBrush;
            ShiftCue.Background = _shiftDownFillBrush;
            ShiftCue.BorderBrush = _coldBrush;
        }
    }

    protected override void RenderNoData() => ShowPlaceholderOrHide();

    private void ShowPlaceholderOrHide()
    {
        if (ClickThrough)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        ShiftCue.Visibility = Visibility.Visible;
        ShiftCueArrow.Text = "•";
        ShiftCueText.Text = "SHIFT CUE";
        ShiftCueArrow.Foreground = _mutedBrush;
        ShiftCueText.Foreground = _mutedBrush;
        ShiftCue.Background = _cardBrush;
        ShiftCue.BorderBrush = _mutedBrush;
    }
}

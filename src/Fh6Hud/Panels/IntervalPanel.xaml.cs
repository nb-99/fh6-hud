using System.Windows.Controls;
using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>
/// 0-100 / 100-200 / 200-300 km/h interval timers with best times, plus the
/// 0-300 km/h speed progress bar matching the timer zones.
/// </summary>
public partial class IntervalPanel : PanelWindow
{
    private const float SpeedBarMaxKmh = 300f;

    private readonly SolidColorBrush _readyBrush;
    private readonly SolidColorBrush _timingBrush;
    private readonly SolidColorBrush _doneBrush;

    public IntervalPanel(HudState state)
        : base(state, PanelKeys.Intervals)
    {
        InitializeComponent();

        _readyBrush = (SolidColorBrush)FindResource("ReadyBrush");
        _timingBrush = (SolidColorBrush)FindResource("TimingBrush");
        _doneBrush = (SolidColorBrush)FindResource("DoneBrush");
    }

    protected override void Render(Fh6Packet packet)
    {
        SpeedBarFill.Width = SpeedBarTrack.ActualWidth * Math.Clamp(packet.SpeedKmh / SpeedBarMaxKmh, 0f, 1f);
        UpdateIntervalRow(State.Timer0To100, T0_100_Time, T0_100_State);
        UpdateIntervalRow(State.Timer100To200, T100_200_Time, T100_200_State);
        UpdateIntervalRow(State.Timer200To300, T200_300_Time, T200_300_State);
    }

    private void UpdateIntervalRow(SpeedIntervalTimer timer, TextBlock timeBlock, TextBlock stateBlock)
    {
        switch (timer.CurrentState)
        {
            case SpeedIntervalTimer.State.Waiting:
                SetText(timeBlock, "--.--");
                SetText(stateBlock, timer.HasBest ? $"BEST {timer.BestElapsed.TotalSeconds:F2}" : "READY");
                timeBlock.Foreground = _readyBrush;
                break;

            case SpeedIntervalTimer.State.Running:
                SetText(timeBlock, $"{timer.Elapsed.TotalSeconds:F2}");
                SetText(stateBlock, "TIMING");
                timeBlock.Foreground = _timingBrush;
                break;

            case SpeedIntervalTimer.State.Done:
                SetText(timeBlock, $"{timer.Elapsed.TotalSeconds:F2}");
                SetText(stateBlock, "DONE");
                timeBlock.Foreground = _doneBrush;
                break;
        }
    }
}

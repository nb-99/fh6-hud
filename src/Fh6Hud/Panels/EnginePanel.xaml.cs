using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>
/// Engine RPM with redline bar, the learned power curve, current/max power,
/// and the shift lights: a big flashing "▲ UPSHIFT" pill when the optimal
/// upshift point of the current gear is reached, and a "▼ DOWNSHIFT" pill
/// when a lower gear would make more power (without hitting the limiter or
/// needing to shift back up). The title slot keeps a steady "SHIFT @ n" hint
/// with the learned upshift point, and a marker line on the curve shows it.
/// </summary>
public partial class EnginePanel : PanelWindow
{
    /// <summary>Throttle input (0-255) above which the upshift light is shown.</summary>
    private const byte UpshiftThrottleThreshold = 200;

    /// <summary>Throttle input (0-255) above which the downshift light is shown.</summary>
    private const byte DownshiftThrottleThreshold = 128;

    private readonly SolidColorBrush _accentBrush;
    private readonly SolidColorBrush _coldBrush;
    private readonly SolidColorBrush _hotBrush;
    private readonly SolidColorBrush _mutedBrush;
    private readonly SolidColorBrush _shiftUpFillBrush;
    private readonly SolidColorBrush _shiftDownFillBrush;

    public EnginePanel(HudState state)
        : base(state, PanelKeys.Engine)
    {
        InitializeComponent();

        _accentBrush = (SolidColorBrush)FindResource("AccentBrush");
        _coldBrush = (SolidColorBrush)FindResource("ColdBrush");
        _hotBrush = (SolidColorBrush)FindResource("HotBrush");
        _mutedBrush = (SolidColorBrush)FindResource("MutedBrush");
        _shiftUpFillBrush = (SolidColorBrush)FindResource("ShiftUpFillBrush");
        _shiftDownFillBrush = (SolidColorBrush)FindResource("ShiftDownFillBrush");
    }

    protected override void Render(Fh6Packet packet)
    {
        float maxRpm = packet.EngineMaxRpm;
        SetText(RpmText, $"{packet.CurrentEngineRpm:F0}");
        SetText(RpmMaxText, maxRpm > 0 ? $"/ {maxRpm:F0} RPM" : "/ ---- RPM");
        RpmBarFill.Width = RpmBarTrack.ActualWidth * RpmBarGeometry.FillWidthFraction(packet.CurrentEngineRpm, maxRpm);

        if (State.PowerCurve.IsDirty)
        {
            RebuildPowerCurve();
        }

        SetText(MaxPsText, $"{State.PowerCurve.MaxPowerPs:F0} PS");
        SetText(CurPsText, $"{PowerCurveTracker.WattsToPs(packet.PowerWatts):F0} PS");
        UpdatePowerCurveDot(packet.CurrentEngineRpm, packet.PowerWatts);
        UpdateShiftAdvice(packet);
    }

    private void UpdateShiftAdvice(Fh6Packet packet)
    {
        float? shiftRpm = GearRatioTracker.IsLearnableGear(packet.Gear)
            ? State.ShiftAdvisor.GetShiftRpm(packet.Gear)
            : null;

        // Title: steady hint with the learned upshift point for this gear.
        SetText(ShiftText, shiftRpm is { } rpm ? $"SHIFT @ {rpm:F0}" : "SHIFT --");
        ShiftText.Foreground = shiftRpm is null ? _mutedBrush : _accentBrush;

        UpdateShiftLight(packet);
    }

    private void UpdateShiftLight(Fh6Packet packet)
    {
        bool up = State.ShiftAdvisor.ShouldUpshift(packet.Gear, packet.CurrentEngineRpm)
                  && packet.Accel >= UpshiftThrottleThreshold;
        bool down = State.ShiftAdvisor.ShouldDownshift(packet.Gear, packet.CurrentEngineRpm)
                    && packet.Accel >= DownshiftThrottleThreshold;

        if (!up && !down)
        {
            ShiftLight.Visibility = Visibility.Collapsed;
            return;
        }

        // Flash at ~2.5 Hz; Hidden (not Collapsed) so the panel size stays put
        // while blinking.
        ShiftLight.Visibility = (Environment.TickCount64 / 200) % 2 == 0
            ? Visibility.Visible
            : Visibility.Hidden;

        if (up)
        {
            ShiftLightArrow.Text = "▲";
            ShiftLightText.Text = "UPSHIFT";
            ShiftLightArrow.Foreground = _hotBrush;
            ShiftLightText.Foreground = _hotBrush;
            ShiftLight.Background = _shiftUpFillBrush;
            ShiftLight.BorderBrush = _hotBrush;
        }
        else
        {
            ShiftLightArrow.Text = "▼";
            ShiftLightText.Text = "DOWNSHIFT";
            ShiftLightArrow.Foreground = _coldBrush;
            ShiftLightText.Foreground = _coldBrush;
            ShiftLight.Background = _shiftDownFillBrush;
            ShiftLight.BorderBrush = _coldBrush;
        }
    }

    private void RebuildPowerCurve()
    {
        double w = PowerCurveCanvas.ActualWidth;
        double h = PowerCurveCanvas.ActualHeight;
        var buckets = State.PowerCurve.Buckets;
        float maxPower = State.PowerCurve.MaxPowerW;

        if (w <= 0 || h <= 0 || buckets.Count == 0 || maxPower <= 0)
        {
            // Nothing to draw yet (e.g. right after a car switch): clear the
            // previous car's curve instead of leaving it on screen.
            PowerCurveLine.Points = null;
            PowerCurveDot.Visibility = Visibility.Collapsed;
            State.PowerCurve.IsDirty = false;
            return;
        }

        // Only draw up to the last sampled bucket: while the curve is still
        // learning, the unsampled high-RPM tail would otherwise plunge to the
        // bottom of the canvas like a vertical cliff.
        int n = buckets.Count;
        int lastSampled = n - 1;
        while (lastSampled > 0 && buckets[lastSampled] <= 0)
        {
            lastSampled--;
        }

        var points = new PointCollection();
        double step = n > 1 ? w / (n - 1) : w;
        for (int i = 0; i <= lastSampled; i++)
        {
            points.Add(new Point(i * step, h - buckets[i] / maxPower * h));
        }

        PowerCurveLine.Points = points;
        State.PowerCurve.IsDirty = false;
    }

    private void UpdatePowerCurveDot(float rpm, float powerW)
    {
        double w = PowerCurveCanvas.ActualWidth;
        double h = PowerCurveCanvas.ActualHeight;
        float maxRpm = State.PowerCurve.MaxRpm;
        float maxPower = State.PowerCurve.MaxPowerW;

        if (w <= 0 || h <= 0 || maxRpm <= 0 || maxPower <= 0)
        {
            PowerCurveDot.Visibility = Visibility.Collapsed;
            return;
        }

        double x = Math.Clamp(rpm / maxRpm, 0f, 1f) * w;
        double y = h - Math.Clamp(powerW / maxPower, 0f, 1f) * h;
        Canvas.SetLeft(PowerCurveDot, x - 3);
        Canvas.SetTop(PowerCurveDot, y - 3);
        PowerCurveDot.Visibility = Visibility.Visible;
    }
}

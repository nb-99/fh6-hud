using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>
/// Engine RPM with redline bar, the learned power curve, current/max power,
/// and the learned shift point. The title slot keeps a steady "SHIFT @ n" hint
/// with the learned upshift point; the live upshift/downshift cue is rendered
/// by <see cref="ShiftCuePanel"/>.
/// </summary>
public partial class EnginePanel : PanelWindow
{
    private readonly SolidColorBrush _accentBrush;
    private readonly SolidColorBrush _mutedBrush;
    private int _renderedPowerCurveVersion = -1;

    public EnginePanel(HudState state)
        : base(state, PanelKeys.Engine)
    {
        InitializeComponent();

        _accentBrush = (SolidColorBrush)FindResource("AccentBrush");
        _mutedBrush = (SolidColorBrush)FindResource("MutedBrush");
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

        if (_renderedPowerCurveVersion != State.PowerCurve.Version)
        {
            SetText(MaxPsText, State.PowerCurve.MaxPowerPs > 0 ? $"{State.PowerCurve.MaxPowerPs:F0} PS" : "--- PS");
            SetText(MaxPowerRpmText, State.PowerCurve.MaxPowerRpm > 0 ? $"@ {State.PowerCurve.MaxPowerRpm:F0} RPM" : "@ ---- RPM");
            _renderedPowerCurveVersion = State.PowerCurve.Version;
        }
        SetText(CurPsText, $"{PowerCurveTracker.WattsToPs(packet.PowerWatts):F0} PS");
        UpdatePowerCurveDot(packet.CurrentEngineRpm, packet.PowerWatts);
        UpdateShiftHint(packet);
    }

    private void UpdateShiftHint(Fh6Packet packet)
    {
        float? shiftRpm = GearRatioTracker.IsLearnableGear(packet.Gear)
            ? State.ShiftAdvisor.GetShiftRpm(packet.Gear)
            : null;

        // The top gear can never produce a shift point (no next gear to
        // compare against), and reverse/neutral are non-applicable — those
        // keep the neutral "SHIFT --". A learnable forward gear that has not
        // yet produced a point is "learning" (data still insufficient).
        bool learning = shiftRpm is null
                        && GearRatioTracker.IsLearnableGear(packet.Gear)
                        && packet.Gear < GearRatioTracker.MaxForwardGear;

        // Title: steady hint with the learned upshift point for this gear.
        SetText(
            ShiftText,
            shiftRpm is { } rpm ? $"SHIFT @ {rpm:F0}" : learning ? "SHIFT LEARNING" : "SHIFT --");
        ShiftText.Foreground = shiftRpm is null ? _mutedBrush : _accentBrush;
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

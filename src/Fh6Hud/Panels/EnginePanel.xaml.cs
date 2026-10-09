using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

    // Canvas size the curve points were last built for. A resize changes the
    // x/y scale, so the curve must be rebuilt even when the data is unchanged.
    private double _curveWidth = -1;

    // Inputs the axis grid was last built for; the grid is rebuilt only when
    // one of them changes (this runs on every render tick).
    private double _gridWidth = -1;
    private double _gridHeight = -1;
    private float _gridMaxRpm = -1;
    private float _gridMaxPowerW = -1;
    private int _gridCurveVersion = -1;

    public EnginePanel(HudState state)
        : base(state, PanelKeys.Engine)
    {
        InitializeComponent();

        _accentBrush = (SolidColorBrush)FindResource("AccentBrush");
        _mutedBrush = (SolidColorBrush)FindResource("MutedBrush");
    }

    protected override bool SupportsAutoHide => true;

    // Idle RPM wobbles by a few revs; 25 RPM is a deliberate rev, not jitter.
    protected override float ActivityTolerance => 25f;

    protected override PanelActivityTracker.Sample ReadActivity(Fh6Packet packet) =>
        new(packet.CurrentEngineRpm);

    protected override void Render(Fh6Packet packet)
    {
        float maxRpm = packet.EngineMaxRpm;
        SetText(RpmText, $"{packet.CurrentEngineRpm:F0}");
        SetText(RpmMaxText, maxRpm > 0 ? $"/ {maxRpm:F0} RPM" : "/ ---- RPM");
        RpmBarFill.Width = RpmBarTrack.ActualWidth * RpmBarGeometry.FillWidthFraction(packet.CurrentEngineRpm, maxRpm);

        if (State.PowerCurve.IsDirty || PowerCurveCanvas.ActualWidth != _curveWidth)
        {
            RebuildPowerCurve();
        }

        RebuildAxis();

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

        _curveWidth = w;

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

        // Same RPM->x transform as the axis grid and the power dot, so a
        // point at, say, 6000 RPM sits exactly on the 6k grid line.
        var points = new PointCollection();
        float maxRpm = State.PowerCurve.MaxRpm;
        for (int i = 0; i <= lastSampled; i++)
        {
            double x = PowerCurveAxis.XForRpm(i * PowerCurveTracker.BucketRpm, maxRpm, w);
            double y = PowerCurveAxis.YForPower(buckets[i], maxPower, h);
            points.Add(new Point(x, y));
        }

        PowerCurveLine.Points = points;
        State.PowerCurve.IsDirty = false;
    }

    /// <summary>
    /// Redraws the faint RPM/PS grid and its labels when the curve's scale or
    /// the canvas size changes. Does nothing while the inputs are unchanged.
    /// </summary>
    private void RebuildAxis()
    {
        double w = PowerCurveCanvas.ActualWidth;
        double h = PowerCurveCanvas.ActualHeight;
        float maxRpm = State.PowerCurve.MaxRpm;
        float maxPower = State.PowerCurve.MaxPowerW;

        int curveVersion = State.PowerCurve.Version;
        if (w == _gridWidth && h == _gridHeight && maxRpm == _gridMaxRpm && maxPower == _gridMaxPowerW
            && curveVersion == _gridCurveVersion)
        {
            return;
        }

        _gridWidth = w;
        _gridHeight = h;
        _gridMaxRpm = maxRpm;
        _gridMaxPowerW = maxPower;
        _gridCurveVersion = curveVersion;

        PowerCurveGrid.Children.Clear();
        PowerCurveAxisLabels.Children.Clear();
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // The band goes first so the grid lines and labels draw over it.
        AddPowerBand(maxRpm, w, h);
        AddRpmGrid(maxRpm, w, h);
        AddPowerGrid(maxPower, w, h);
    }

    /// <summary>
    /// Shades the RPM range where power is within 5% of peak and labels it
    /// (e.g. "95%+ 5700–6300"), so "where is my max power" reads at a glance.
    /// </summary>
    private void AddPowerBand(float maxRpm, double w, double h)
    {
        var band = PowerCurveAxis.FindPowerBand(
            State.PowerCurve.Buckets, PowerCurveTracker.BucketRpm, maxRpm);
        if (band is not { } b)
        {
            return;
        }

        double x1 = PowerCurveAxis.XForRpm(b.LowRpm, maxRpm, w);
        double x2 = PowerCurveAxis.XForRpm(b.HighRpm, maxRpm, w);
        var shade = new Rectangle
        {
            Width = Math.Max(0, x2 - x1),
            Height = h,
            Fill = _accentBrush,
            Opacity = 0.14,
        };
        Canvas.SetLeft(shade, x1);
        PowerCurveGrid.Children.Add(shade);

        // Label at the bottom of the band: the high-power area is at the top,
        // so the bottom is usually free of curve.
        var text = CreateAxisLabel(PowerCurveAxis.PowerBandLabel(b));
        text.Foreground = _accentBrush;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(text, Math.Clamp(x1 + 3, 0, Math.Max(0, w - text.DesiredSize.Width)));
        Canvas.SetTop(text, Math.Max(0, h - text.DesiredSize.Height - 1));
        PowerCurveGrid.Children.Add(text);
    }

    private void AddRpmGrid(float maxRpm, double w, double h)
    {
        foreach (var tick in PowerCurveAxis.RpmTicks(maxRpm, w))
        {
            // Major lines are a little stronger than the minor ones so the
            // 1k/2k/… reference points stand out without a busy grid.
            PowerCurveGrid.Children.Add(new Line
            {
                X1 = tick.X,
                X2 = tick.X,
                Y1 = 0,
                Y2 = h,
                Stroke = _mutedBrush,
                StrokeThickness = 1,
                Opacity = tick.Major ? 0.35 : 0.15,
            });

            if (tick.Label is { } label)
            {
                var text = CreateAxisLabel(label);
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // Centre the label on its line, but keep it inside the panel
                // at the left and right edges.
                double left = Math.Clamp(tick.X - text.DesiredSize.Width / 2, 0, Math.Max(0, w - text.DesiredSize.Width));
                Canvas.SetLeft(text, left);
                PowerCurveAxisLabels.Children.Add(text);
            }
        }
    }

    private void AddPowerGrid(float maxPower, double w, double h)
    {
        foreach (var tick in PowerCurveAxis.PowerTicks(maxPower, h))
        {
            PowerCurveGrid.Children.Add(new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = tick.Y,
                Y2 = tick.Y,
                Stroke = _mutedBrush,
                StrokeThickness = 1,
                Opacity = 0.15,
            });

            // Label sits just above its line, or just below it when the line
            // is at the very top of the canvas.
            var text = CreateAxisLabel(tick.Label);
            double top = tick.Y - 12;
            Canvas.SetLeft(text, 2);
            Canvas.SetTop(text, top < 0 ? tick.Y + 1 : top);
            PowerCurveGrid.Children.Add(text);
        }
    }

    private TextBlock CreateAxisLabel(string text) => new()
    {
        Text = text,
        FontFamily = (FontFamily)FindResource("DigitFont"),
        FontSize = 9,
        Foreground = _mutedBrush,
    };

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

        double x = PowerCurveAxis.XForRpm(rpm, maxRpm, w);
        double y = PowerCurveAxis.YForPower(powerW, maxPower, h);
        Canvas.SetLeft(PowerCurveDot, x - 3);
        Canvas.SetTop(PowerCurveDot, y - 3);
        PowerCurveDot.Visibility = Visibility.Visible;
    }
}

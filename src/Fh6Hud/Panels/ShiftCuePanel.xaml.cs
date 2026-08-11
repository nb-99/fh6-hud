using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Panels;

/// <summary>
/// The live upshift/downshift recommendation in its own draggable panel. The
/// advisor thresholds remain the same as the original engine-panel cue.
/// </summary>
public partial class ShiftCuePanel : PanelWindow
{
    private enum CueDirection
    {
        Upshift,
        Downshift,
    }

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
    private readonly Func<long> _clock;
    private CueDirection? _activeCue;
    private string _lastDiagnosticKey = "";
    private long _lastDiagnosticAt;

    public ShiftCuePanel(HudState state, Func<long>? clock = null)
        : base(state, PanelKeys.ShiftCue)
    {
        InitializeComponent();

        _coldBrush = (SolidColorBrush)FindResource("ColdBrush");
        _hotBrush = (SolidColorBrush)FindResource("HotBrush");
        _mutedBrush = (SolidColorBrush)FindResource("MutedBrush");
        _cardBrush = (SolidColorBrush)FindResource("CardBrush");
        _shiftUpFillBrush = (SolidColorBrush)FindResource("ShiftUpFillBrush");
        _shiftDownFillBrush = (SolidColorBrush)FindResource("ShiftDownFillBrush");
        _clock = clock ?? (() => Environment.TickCount64);
    }

    protected override bool HideWhenNoData => false;

    protected override void Render(Fh6Packet packet)
    {
        bool upAdvice = State.ShiftAdvisor.ShouldUpshift(packet.Gear, packet.CurrentEngineRpm);
        bool upGate = packet.Accel >= UpshiftThrottleThreshold;
        bool up = upAdvice && upGate;
        bool downEvaluated = !up;
        bool downAdvice = downEvaluated
                           && State.ShiftAdvisor.ShouldDownshift(packet.Gear, packet.CurrentEngineRpm);
        bool downGate = packet.Accel >= DownshiftThrottleThreshold;
        bool down = downAdvice && downGate;
        float? upRpm = State.ShiftAdvisor.GetShiftRpm(packet.Gear);
        float? downRpm = State.ShiftAdvisor.GetDownshiftRpm(packet.Gear);

        if (!up && !down)
        {
            _activeCue = null;
            ShowPlaceholderOrHide();
            LogDiagnostic(
                packet,
                mode: "NONE",
                upAdvice,
                upGate,
                downEvaluated,
                downAdvice,
                downGate,
                upRpm,
                downRpm);
            return;
        }

        CueDirection direction = up ? CueDirection.Upshift : CueDirection.Downshift;
        long now = _clock();
        bool newlyActivated = _activeCue != direction;
        _activeCue = direction;

        Visibility = Visibility.Visible;
        ShiftPlaceholder.Visibility = Visibility.Collapsed;
        // Always expose the first frame of a new recommendation. Real drivers
        // can shift again within one 200 ms blink phase; using only the global
        // phase would otherwise make the placeholder disappear while the live
        // pill remains hidden for the entire recommendation.
        ShiftLight.Visibility = Visibility.Visible;
        ShiftLight.Opacity = newlyActivated || (now / 200) % 2 == 0 ? 1 : 0;

        // Preserve the established arbitration: upshift wins if a stale
        // downshift latch overlaps it. Downshift is evaluated only when the
        // upshift recommendation is inactive, so the two states stay exclusive.
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

        LogDiagnostic(
            packet,
            mode: up ? "UP" : "DOWN",
            upAdvice,
            upGate,
            downEvaluated,
            downAdvice,
            downGate,
            upRpm,
            downRpm);
    }

    protected override void RenderNoData()
    {
        ShowPlaceholderOrHide();
        string key = $"NODATA|{State.Live}|{ClickThrough}|{Visibility}|{ShiftPlaceholder.Visibility}|{ShiftLight.Visibility}";
        if (string.Equals(key, _lastDiagnosticKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastDiagnosticKey = key;
        _lastDiagnosticAt = Environment.TickCount64;
        HudLog.Debug(
            $"[SHIFT-DIAG] mode=NODATA live={State.Live} clickThrough={ClickThrough} " +
            $"window={Visibility} placeholder={ShiftPlaceholder.Visibility} light={ShiftLight.Visibility}");
    }

    private void LogDiagnostic(
        Fh6Packet packet,
        string mode,
        bool upAdvice,
        bool upGate,
        bool downEvaluated,
        bool downAdvice,
        bool downGate,
        float? upRpm,
        float? downRpm)
    {
        string key =
            $"{mode}|{packet.Gear}|{upRpm.HasValue}|{downRpm.HasValue}|{upAdvice}|{upGate}|" +
            $"{downEvaluated}|{downAdvice}|{downGate}|{ClickThrough}|{Visibility}";
        long now = Environment.TickCount64;
        if (string.Equals(key, _lastDiagnosticKey, StringComparison.Ordinal)
            && (string.Equals(mode, "NONE", StringComparison.Ordinal)
                || now - _lastDiagnosticAt < 1000))
        {
            return;
        }

        _lastDiagnosticKey = key;
        _lastDiagnosticAt = now;
        HudLog.Debug(
            $"[SHIFT-DIAG] mode={mode} live={State.Live} clickThrough={ClickThrough} " +
            $"window={Visibility} light={ShiftLight.Visibility} gear={packet.Gear} " +
            $"rpm={packet.CurrentEngineRpm.ToString("F0", CultureInfo.InvariantCulture)} " +
            $"maxRpm={packet.EngineMaxRpm.ToString("F0", CultureInfo.InvariantCulture)} " +
            $"accel={packet.Accel} brake={packet.Brake} clutch={packet.Clutch} " +
            $"speedMs={packet.SpeedMs.ToString("F1", CultureInfo.InvariantCulture)} " +
            $"upAdvice={upAdvice} upGate={upGate} " +
            $"upRpm={FormatRpm(upRpm)} downEvaluated={downEvaluated} " +
            $"downAdvice={downAdvice} downGate={downGate} downRpm={FormatRpm(downRpm)} " +
            $"ratioSamples={State.GearRatios.GetSampleCount(packet.Gear)} " +
            $"powerBuckets={State.PowerCurve.BucketCount} " +
            $"maxPower={State.PowerCurve.MaxPowerW.ToString("F0", CultureInfo.InvariantCulture)}");
    }

    private static string FormatRpm(float? rpm) =>
        rpm is { } value
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : "-";

    private void ShowPlaceholderOrHide()
    {
        if (ClickThrough)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        ShiftLight.Visibility = Visibility.Collapsed;
        ShiftLight.Opacity = 1;
        ShiftPlaceholder.Visibility = Visibility.Visible;
        PlaceholderArrow.Text = "↕";
        PlaceholderText.Text = "SHIFT CUE";
        PlaceholderArrow.Foreground = _mutedBrush;
        PlaceholderText.Foreground = _mutedBrush;
        ShiftPlaceholder.Background = _cardBrush;
        ShiftPlaceholder.BorderBrush = _mutedBrush;
    }
}

using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Fh6Hud.Telemetry;

namespace Fh6Hud;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const int WsExToolWindow = 0x80;
    private const int SwpNoMove = 0x2;
    private const int SwpNoSize = 0x1;
    private const int SwpNoActivate = 0x10;
    private const int SwpFrameChanged = 0x20;
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const int ModControlAlt = 0x2 | 0x1;
    private const double StaleAfterSeconds = 2.0;
    private const float SpeedBarMaxKmh = 300f;

    private readonly HudConfig _config;
    private readonly SpeedIntervalTimer _timer0To100 = new(3f, 100f);
    private readonly SpeedIntervalTimer _timer100To200 = new(100f, 200f);
    private readonly SpeedIntervalTimer _timer200To300 = new(200f, 300f);
    private readonly PowerCurveTracker _powerCurve = new();
    private string _tireCompound = "Race";

    private UdpTelemetryListener? _listener;
    private Fh6Packet? _latest;
    private DateTime _lastPacketAt = DateTime.MinValue;
    private bool _clickThrough;
    private HwndSource? _hwndSource;

    private readonly SolidColorBrush _coldBrush;
    private readonly SolidColorBrush _okBrush;
    private readonly SolidColorBrush _hotBrush;
    private readonly SolidColorBrush _readyBrush;
    private readonly SolidColorBrush _timingBrush;
    private readonly SolidColorBrush _doneBrush;
    private readonly SolidColorBrush _dimBrush;
    private readonly SolidColorBrush _accentBrush;
    private readonly SolidColorBrush _secondaryBrush;
    private readonly SolidColorBrush _cardBrush;
    private readonly SolidColorBrush _cardBorderBrush;
    private readonly SolidColorBrush _coldFillBrush;
    private readonly SolidColorBrush _okFillBrush;
    private readonly SolidColorBrush _hotFillBrush;

    public MainWindow()
    {
        InitializeComponent();

        _config = HudConfig.Load();
        _tireCompound = _config.TireCompound;
        ApplyCompound(TireCompound.Find(_tireCompound));

        _coldBrush = (SolidColorBrush)FindResource("ColdBrush");
        _okBrush = (SolidColorBrush)FindResource("OkBrush");
        _hotBrush = (SolidColorBrush)FindResource("HotBrush");
        _readyBrush = (SolidColorBrush)FindResource("ReadyBrush");
        _timingBrush = (SolidColorBrush)FindResource("TimingBrush");
        _doneBrush = (SolidColorBrush)FindResource("DoneBrush");
        _dimBrush = (SolidColorBrush)FindResource("DimBrush");
        _accentBrush = (SolidColorBrush)FindResource("AccentBrush");
        _secondaryBrush = (SolidColorBrush)FindResource("SecondaryBrush");
        _cardBrush = (SolidColorBrush)FindResource("CardBrush");
        _cardBorderBrush = (SolidColorBrush)FindResource("CardBorderBrush");
        _coldFillBrush = (SolidColorBrush)FindResource("TireColdFillBrush");
        _okFillBrush = (SolidColorBrush)FindResource("TireOkFillBrush");
        _hotFillBrush = (SolidColorBrush)FindResource("TireHotFillBrush");

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + 16;
        Top = work.Bottom - ActualHeight - 16;

        _hwndSource = (HwndSource?)PresentationSource.FromVisual(this);
        _hwndSource?.AddHook(WndProc);

        try
        {
            _listener = new UdpTelemetryListener(_config.Port);
            _listener.PacketReceived += OnPacketReceived;
            StatusText.Text = $"UDP {_listener.Port}  |  READY";
        }
        catch (SocketException)
        {
            StatusText.Text = $"PORT {_config.Port} UNAVAILABLE";
        }

        RegisterHotKey(_hwndSource?.Handle ?? IntPtr.Zero, HotkeyId, ModControlAlt, (uint)'H');
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        if (_hwndSource?.Handle is { } handle)
        {
            UnregisterHotKey(handle, HotkeyId);
        }

        _listener?.Dispose();
    }

    private void OnPacketReceived(object? sender, Fh6Packet packet)
    {
        _latest = packet;
        _lastPacketAt = DateTime.UtcNow;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var packet = _latest;
        if (packet is null)
        {
            return;
        }

        bool stale = (DateTime.UtcNow - _lastPacketAt).TotalSeconds > StaleAfterSeconds;
        SetText(SpeedText, $"{packet.SpeedKmh:F0}");
        StatusDot.Fill = stale ? _hotBrush : _okBrush;
        SpeedBarFill.Width = SpeedBarTrack.ActualWidth * Math.Clamp(packet.SpeedKmh / SpeedBarMaxKmh, 0f, 1f);

        if (stale)
        {
            SetText(StatusText, "NO DATA  |  DRIVING?");
            return;
        }

        SetText(StatusText, $"UDP {_listener?.Port}  |  LIVE");
        UpdateTireTemp(TireFlCard, TireFl, TireFlState, packet.TireTempFrontLeftC);
        UpdateTireTemp(TireFrCard, TireFr, TireFrState, packet.TireTempFrontRightC);
        UpdateTireTemp(TireRlCard, TireRl, TireRlState, packet.TireTempRearLeftC);
        UpdateTireTemp(TireRrCard, TireRr, TireRrState, packet.TireTempRearRightC);

        float maxRpm = packet.EngineMaxRpm;
        SetText(RpmText, $"{packet.CurrentEngineRpm:F0}");
        SetText(RpmMaxText, maxRpm > 0 ? $"/ {maxRpm:F0} RPM" : "/ ---- RPM");
        RpmBarFill.Width = maxRpm > 0
            ? RpmBarTrack.ActualWidth * Math.Clamp(packet.CurrentEngineRpm / maxRpm, 0f, 1f)
            : 0;

        _powerCurve.Configure(maxRpm);
        _powerCurve.AddSample(packet.CurrentEngineRpm, packet.PowerWatts);
        if (_powerCurve.IsDirty)
        {
            RebuildPowerCurve();
        }

        SetText(MaxPsText, $"{_powerCurve.MaxPowerPs:F0} PS");
        SetText(CurPsText, $"{PowerCurveTracker.WattsToPs(packet.PowerWatts):F0} PS");
        UpdatePowerCurveDot(packet.CurrentEngineRpm, packet.PowerWatts);

        _timer0To100.Update(packet.SpeedKmh, packet.TimestampMs);
        _timer100To200.Update(packet.SpeedKmh, packet.TimestampMs);
        _timer200To300.Update(packet.SpeedKmh, packet.TimestampMs);
        UpdateIntervalRow(_timer0To100, T0_100_Time, T0_100_State);
        UpdateIntervalRow(_timer100To200, T100_200_Time, T100_200_State);
        UpdateIntervalRow(_timer200To300, T200_300_Time, T200_300_State);
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

    private void RebuildPowerCurve()
    {
        double w = PowerCurveCanvas.ActualWidth;
        double h = PowerCurveCanvas.ActualHeight;
        var buckets = _powerCurve.Buckets;
        float maxPower = _powerCurve.MaxPowerW;

        if (w <= 0 || h <= 0 || buckets.Count == 0 || maxPower <= 0)
        {
            _powerCurve.IsDirty = false;
            return;
        }

        var points = new PointCollection();
        int n = buckets.Count;
        double step = n > 1 ? w / (n - 1) : w;
        for (int i = 0; i < n; i++)
        {
            points.Add(new Point(i * step, h - buckets[i] / maxPower * h));
        }

        PowerCurveLine.Points = points;
        _powerCurve.IsDirty = false;
    }

    private void UpdatePowerCurveDot(float rpm, float powerW)
    {
        double w = PowerCurveCanvas.ActualWidth;
        double h = PowerCurveCanvas.ActualHeight;
        float maxRpm = _powerCurve.MaxRpm;
        float maxPower = _powerCurve.MaxPowerW;

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

    private void UpdateTireTemp(Border card, TextBlock valueBlock, TextBlock stateBlock, float tempC)
    {
        if (!float.IsFinite(tempC))
        {
            SetText(valueBlock, "--°");
            SetText(stateBlock, "--");
            valueBlock.Foreground = _dimBrush;
            stateBlock.Foreground = _dimBrush;
            card.Background = _cardBrush;
            card.BorderBrush = _cardBorderBrush;
            return;
        }

        SolidColorBrush stateBrush;
        SolidColorBrush fillBrush;
        string state;
        if (tempC < _config.TireOptMinC)
        {
            stateBrush = _coldBrush;
            fillBrush = _coldFillBrush;
            state = "COLD";
        }
        else if (tempC > _config.TireOptMaxC)
        {
            stateBrush = _hotBrush;
            fillBrush = _hotFillBrush;
            state = "HOT";
        }
        else
        {
            stateBrush = _okBrush;
            fillBrush = _okFillBrush;
            state = "IN RANGE";
        }

        SetText(valueBlock, $"{tempC:F0}°");
        SetText(stateBlock, state);
        valueBlock.Foreground = stateBrush;
        stateBlock.Foreground = stateBrush;
        card.Background = fillBrush;
        card.BorderBrush = stateBrush;
    }

    private void ApplyCompound(TireCompound.Preset? preset)
    {
        if (preset is null)
        {
            return;
        }

        _tireCompound = preset.Name;
        _config.TireCompound = preset.Name;
        _config.TireOptMinC = preset.MinC;
        _config.TireOptMaxC = preset.MaxC;
        TireRangeText.Text = $"TARGET {preset.MinC:F0}-{preset.MaxC:F0} °C";

        foreach (var child in TireCompoundMenu.Items.OfType<MenuItem>())
        {
            child.IsChecked = child.Tag is string name && name.Equals(_tireCompound, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void Compound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string name)
        {
            ApplyCompound(TireCompound.Find(name));
            StatusText.Text = $"TIRES {_tireCompound}  |  {_config.TireOptMinC:F0}-{_config.TireOptMaxC:F0} °C";
        }
    }

    private static void SetText(TextBlock block, string text)
    {
        if (block.Text != text)
        {
            block.Text = text;
        }
    }

    private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_clickThrough)
        {
            DragMove();
        }
    }

    private void ToggleClickThrough_Click(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void ToggleClickThrough()
    {
        if (_hwndSource?.Handle is not { } handle)
        {
            return;
        }

        _clickThrough = !_clickThrough;
        var style = GetWindowLongPtr(handle, GwlExStyle);
        style = _clickThrough
            ? style | WsExTransparent | WsExLayered | WsExToolWindow
            : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, style);
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
        StatusText.Text = _clickThrough ? "CLICK-THROUGH ON" : "CLICK-THROUGH OFF";
    }

    private void ResetTimers_Click(object sender, RoutedEventArgs e)
    {
        _timer0To100.ResetAll();
        _timer100To200.ResetAll();
        _timer200To300.ResetAll();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleClickThrough();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

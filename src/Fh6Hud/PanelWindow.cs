using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Fh6Hud.Telemetry;

namespace Fh6Hud;

/// <summary>
/// Base class for the HUD's panel windows: transparent, frameless,
/// always-on-top tool windows. Each panel is draggable on its own and
/// persists its position to config.json as fractions of the work area (see
/// <see cref="PanelPlacement"/>). All panels share the same right-click menu
/// and the global Ctrl+Alt+H click-through toggle.
/// </summary>
public abstract class PanelWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const int WsExToolWindow = 0x80;
    private const int SwpNoMove = 0x2;
    private const int SwpNoSize = 0x1;
    private const int SwpNoActivate = 0x10;
    private const int SwpFrameChanged = 0x20;
    private const int SwShownNoActivate = 4;
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const int ModControlAlt = 0x2 | 0x1;

    /// <summary>
    /// Panels offered in the right-click Panels and Auto-hide menus. The status
    /// panel is deliberately absent: it is the only panel that cannot be hidden.
    /// </summary>
    private static readonly (string Key, string Label)[] HideablePanels =
    {
        (PanelKeys.Tires, "Tire temps"),
        (PanelKeys.Engine, "Engine / RPM"),
        (PanelKeys.ShiftCue, "Shift cue"),
        (PanelKeys.Intervals, "Intervals"),
        (PanelKeys.Speedo, "Speedometer"),
    };

    private static readonly List<PanelWindow> All = new();
    private static bool _clickThrough;
    private static bool _hotkeyAttempted;
    private static HwndSource? _hotkeySource;

    /// <summary>Width of the edge band (DIP) that starts a resize drag.</summary>
    private const double ResizeGripDip = 6;

    private HwndSource? _source;
    private bool? _lastRenderTraceLive;
    private bool _presentationRepairQueued;
    private bool _presentationRepairRunning;
    private bool _presentationRepairAgain;
    private bool _hiddenLastFrame;

    private readonly ScaleTransform _scale;
    private PanelActivityTracker? _activity;
    private ResizeEdge? _resizeEdge;
    private Point _resizePointerStart;
    private ResizeStart _resizeStart;

    protected PanelWindow(HudState state, string panelKey)
    {
        State = state;
        PanelKey = panelKey;

        double scale = PanelPlacement.ClampScale(State.Config.Panels[PanelKey].Scale);
        _scale = new ScaleTransform(scale, scale);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        ContextMenu = BuildMenu();
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += OnSizeChanged;
        Closed += OnClosed;
        // Debug logging: activation is exactly what the affected machine needs
        // to correlate "HUD clicked → starts refreshing" with render stalls.
        Activated += (_, _) => HudLog.Debug($"{GetType().Name} activated");
        Deactivated += (_, _) => HudLog.Debug($"{GetType().Name} deactivated");
        All.Add(this);
    }
    protected HudState State { get; }

    protected string PanelKey { get; }

    /// <summary>
    /// False for panels the user must never hide. The status panel keeps the
    /// right-click menu reachable, so it is the only way back to other panels.
    /// </summary>
    protected virtual bool CanHide => true;

    /// <summary>False disables the edge-drag resize grip for this panel.</summary>
    protected virtual bool CanResize => true;

    /// <summary>True for panels whose data can drive idle auto-hide.</summary>
    protected virtual bool SupportsAutoHide => false;

    /// <summary>Change tolerance for <see cref="ReadActivity"/> values (sensor jitter).</summary>
    protected virtual float ActivityTolerance => 0f;

    /// <summary>The telemetry this panel displays, used to detect when it goes stale.</summary>
    protected virtual PanelActivityTracker.Sample ReadActivity(Fh6Packet packet) => default;

    /// <summary>Monotonic clock in seconds; overridable so tests can drive idle timing.</summary>
    protected virtual double NowSeconds =>
        System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>
    /// Returns the native window state needed to distinguish a WPF visibility
    /// decision from a window that Windows can actually present.
    /// </summary>
    protected string GetNativePresentationDiagnostics()
    {
        IntPtr handle = _source?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return "hwnd=0";
        }

        long extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        string bounds = GetWindowRect(handle, out NativeRect rect)
            ? $"({rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top})"
            : "?";
        return $"hwnd=0x{handle.ToInt64():X} nativeVisible={IsWindowVisible(handle)} " +
               $"exStyle=0x{extendedStyle:X} transparent={(extendedStyle & WsExTransparent) != 0} " +
               $"layered={(extendedStyle & WsExLayered) != 0} tool={(extendedStyle & WsExToolWindow) != 0} " +
               $"nativeBounds={bounds}";
    }

    /// <summary>
    /// WPF can leave a collapsed, layered window hidden at a 2x2 native size
    /// when content changes back to visible while click-through is enabled.
    /// Reconcile the native HWND after rendering without activating it.
    /// </summary>
    private void QueueNativePresentationRepair()
    {
        if (!NeedsNativePresentationRepair())
        {
            return;
        }

        if (_presentationRepairRunning)
        {
            _presentationRepairAgain = true;
            return;
        }

        if (_presentationRepairQueued)
        {
            return;
        }

        _presentationRepairQueued = true;
        try
        {
            // Rendering callbacks must not synchronously force WPF layout or
            // native window changes. Queue the repair so any reentrant render
            // request can only coalesce behind this operation.
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(RepairNativePresentation));
        }
        catch (Exception ex)
        {
            _presentationRepairQueued = false;
            HudLog.Error($"presentation repair queue failed panel={GetType().Name}", ex);
        }
    }

    private bool NeedsNativePresentationRepair()
    {
        if (Visibility != Visibility.Visible
            || _source?.Handle is not { } handle
            || handle == IntPtr.Zero)
        {
            return false;
        }

        if (IsWindowVisible(handle)
            && GetWindowRect(handle, out NativeRect currentRect)
            && currentRect.Right - currentRect.Left > 2
            && currentRect.Bottom - currentRect.Top > 2)
        {
            return false;
        }

        // A mounted click-through panel intentionally has no visual content
        // while idle and may therefore be 2x2. Wait for its content to expand
        // before treating that size as a repair failure.
        return Math.Max(ActualWidth, DesiredSize.Width) > 2
            && Math.Max(ActualHeight, DesiredSize.Height) > 2;
    }

    private void RepairNativePresentation()
    {
        _presentationRepairQueued = false;
        if (_presentationRepairRunning)
        {
            _presentationRepairAgain = true;
            return;
        }

        _presentationRepairRunning = true;
        try
        {
            if (NeedsNativePresentationRepair())
            {
                RepairNativePresentationCore();
            }
        }
        catch (Exception ex)
        {
            // Presentation repair is best effort. An HWND/layout failure must
            // be visible in the log without taking down the render loop.
            HudLog.Error($"presentation repair failed panel={GetType().Name}", ex);
        }
        finally
        {
            _presentationRepairRunning = false;
            if (_presentationRepairAgain)
            {
                _presentationRepairAgain = false;
                QueueNativePresentationRepair();
            }
        }
    }

    private void RepairNativePresentationCore()
    {
        IntPtr handle = _source!.Handle;
        long repairStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        LogPresentationTrace("begin", repairStarted);

        LogPresentationTrace("UpdateLayout begin", repairStarted);
        UpdateLayout();
        LogPresentationTrace("UpdateLayout end", repairStarted);

        LogPresentationTrace("ApplyExtendedStyle begin", repairStarted);
        ApplyExtendedStyle();
        LogPresentationTrace("ApplyExtendedStyle end", repairStarted);

        int width = Math.Max(1, (int)Math.Ceiling(Math.Max(ActualWidth, DesiredSize.Width)));
        int height = Math.Max(1, (int)Math.Ceiling(Math.Max(ActualHeight, DesiredSize.Height)));

        LogPresentationTrace("ShowWindow begin", repairStarted);
        ShowWindow(handle, SwShownNoActivate);
        LogPresentationTrace("ShowWindow end", repairStarted);

        LogPresentationTrace("SetWindowPos begin", repairStarted);
        SetWindowPos(
            handle,
            new IntPtr(-1),
            0,
            0,
            width,
            height,
            SwpNoMove | SwpNoActivate | SwpFrameChanged);
        LogPresentationTrace("SetWindowPos end", repairStarted);
        HudLog.Health(
            $"[PRESENTATION-REPAIR] panel={GetType().Name} size={width}x{height} " +
             GetNativePresentationDiagnostics());
    }

    private void LogPresentationTrace(string stage, long started)
    {
        double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        HudLog.Health(
            $"[PRESENTATION-TRACE] panel={GetType().Name} stage={stage} " +
            $"elapsed={elapsedMs:0.0}ms visibility={Visibility} " +
            $"actual={ActualWidth:0.0}x{ActualHeight:0.0} " +
            $"desired={DesiredSize.Width:0.0}x{DesiredSize.Height:0.0}");
    }

    /// <summary>Global toggle: while true, every panel passes input to the game.</summary>
    public static bool ClickThrough => _clickThrough;

    /// <summary>False when Ctrl+Alt+H could not be registered (taken by another app).</summary>
    public static bool HotkeyAvailable { get; private set; } = true;

    /// <summary>Panels hide while there is no live data; the status panel stays.</summary>
    protected virtual bool HideWhenNoData => true;

    /// <summary>Per-frame entry point called from the App frame timer, after HudState.Tick.</summary>
    public void RenderTick()
    {
        bool live = State.Live;
        bool traceTransition = _lastRenderTraceLive != live;
        if (traceTransition)
        {
            _lastRenderTraceLive = live;
            HudLog.Health(
                $"[RENDER-TRACE] begin panel={GetType().Name} live={live} " +
                $"visibility={Visibility} clickThrough={ClickThrough}");
        }

        bool completed = false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            RenderFrame(live, traceTransition);
            completed = true;
        }
        finally
        {
            if (traceTransition)
            {
                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                HudLog.Health(
                    $"[RENDER-TRACE] end panel={GetType().Name} live={live} " +
                    $"completed={completed} elapsed={elapsedMs:0.0}ms visibility={Visibility}");
            }
        }
    }

    private void RenderFrame(bool live, bool traceTransition)
    {
        if (IsHiddenByPolicy(live))
        {
            if (Visibility != Visibility.Collapsed)
            {
                Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (!live)
        {
            if (HideWhenNoData && Visibility != Visibility.Collapsed)
            {
                Visibility = Visibility.Collapsed;
            }

            RenderNoData();
            if (traceTransition)
            {
                HudLog.Health($"[RENDER-TRACE] body-complete panel={GetType().Name} stage=RenderNoData");
                HudLog.Health($"[RENDER-TRACE] presentation-queued panel={GetType().Name}");
            }

            QueueNativePresentationRepair();
            return;
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }

        Render(State.Latest!);
        if (traceTransition)
        {
            HudLog.Health($"[RENDER-TRACE] body-complete panel={GetType().Name} stage=Render");
            HudLog.Health($"[RENDER-TRACE] presentation-queued panel={GetType().Name}");
        }

        QueueNativePresentationRepair();
    }

    /// <summary>
    /// Decides whether the user's preferences hide the panel this frame: a
    /// manual hide, or auto-hide after its own data went stale. Runs every frame
    /// (even while hidden) so the activity tracker keeps watching for the data
    /// to change again.
    /// </summary>
    private bool IsHiddenByPolicy(bool live)
    {
        var placement = State.Config.Panels[PanelKey];
        if (live && SupportsAutoHide)
        {
            _activity ??= new PanelActivityTracker(ActivityTolerance);
            _activity.Observe(ReadActivity(State.Latest!), NowSeconds);
        }

        bool userHidden = CanHide && placement.Hidden;
        bool idleHidden = live
            && SupportsAutoHide
            && placement.AutoHide
            && _activity?.IsStale(NowSeconds, placement.EffectiveIdleHideSeconds(State.Config.IdleHideSeconds)) == true;
        bool hidden = userHidden || idleHidden;
        if (hidden != _hiddenLastFrame)
        {
            _hiddenLastFrame = hidden;
            HudLog.Debug($"panel={PanelKey} hidden={hidden} reason={(userHidden ? "user" : idleHidden ? "idle" : "none")}");
        }

        return hidden;
    }

    protected abstract void Render(Fh6Packet packet);

    protected virtual void RenderNoData()
    {
    }

    protected static void SetText(TextBlock block, string text)
    {
        if (!string.Equals(block.Text, text, StringComparison.Ordinal))
        {
            block.Text = text;
        }
    }

    public static void ToggleClickThroughAll()
    {
        _clickThrough = !_clickThrough;
        foreach (var window in All)
        {
            window.ApplyExtendedStyle();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        ApplyExtendedStyle();

        // The hotkey is global to the app, so the first panel registers it on
        // its own handle; panels all close together at shutdown.
        if (!_hotkeyAttempted)
        {
            _hotkeyAttempted = true;
            _hotkeySource = _source;
            _hotkeySource?.AddHook(WndProc);
            HotkeyAvailable = RegisterHotKey(_hotkeySource?.Handle ?? IntPtr.Zero, HotkeyId, ModControlAlt, (uint)'H');
        }

        AnchorToPlacement();
    }

    private void ApplyExtendedStyle()
    {
        if (_source?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        // ToolWindow always (six panel windows must not flood Alt-Tab);
        // Transparent only while click-through is on (Layered stays once set,
        // mirroring the original single-window behavior).
        var style = GetWindowLongPtr(handle, GwlExStyle) | WsExToolWindow;
        style = _clickThrough ? style | WsExTransparent | WsExLayered : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, style);
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // SizeToContent means the panel's size changes with its content (e.g.
        // the speed readout gaining a digit); re-anchor so the anchor point
        // (e.g. the right edge of a right-aligned panel) stays put. During an
        // edge resize the drag handler stores the new placement itself.
        if (_resizeEdge is null)
        {
            AnchorToPlacement();
        }
    }

    /// <summary>Applies the scale to the content so the window's SizeToContent follows it.</summary>
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (newContent is FrameworkElement element)
        {
            element.LayoutTransform = _scale;
        }
    }

    private void AnchorToPlacement()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var placement = State.Config.Panels[PanelKey];
        var work = SystemParameters.WorkArea;
        Left = work.Left + placement.X * work.Width - AnchorOffsetX(placement.Anchor, ActualWidth);
        Top = work.Top + placement.Y * work.Height - AnchorOffsetY(placement.Anchor, ActualHeight);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough)
        {
            return;
        }

        if (ResizeEdgeAt(e.GetPosition(this)) is { } edge)
        {
            BeginResize(edge, e);
            return;
        }

        MoveWindowForDrag();
        PersistPlacement();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_resizeEdge is { } edge)
        {
            UpdateResize(edge, e);
            return;
        }

        Cursor = _clickThrough ? null : (ResizeEdgeAt(e.GetPosition(this)) switch
        {
            ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
            _ => null,
        });
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndResize();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndResize();
    }

    /// <summary>Moves the window for a left-button drag.</summary>
    protected virtual void MoveWindowForDrag() => DragMove();

    /// <summary>
    /// The band along each window edge that starts a resize instead of a move.
    /// Horizontal edges win in the corners.
    /// </summary>
    private ResizeEdge? ResizeEdgeAt(Point p)
    {
        if (!CanResize || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        if (p.X <= ResizeGripDip)
        {
            return ResizeEdge.Left;
        }

        if (p.X >= ActualWidth - ResizeGripDip)
        {
            return ResizeEdge.Right;
        }

        if (p.Y <= ResizeGripDip)
        {
            return ResizeEdge.Top;
        }

        if (p.Y >= ActualHeight - ResizeGripDip)
        {
            return ResizeEdge.Bottom;
        }

        return null;
    }

    private void BeginResize(ResizeEdge edge, MouseButtonEventArgs e)
    {
        _resizeEdge = edge;
        _resizePointerStart = PointToScreen(e.GetPosition(this));
        _resizeStart = new ResizeStart(Left, Top, ActualWidth, ActualHeight, _scale.ScaleX);
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// Scales the panel uniformly so the dragged edge follows the pointer. The
    /// edge opposite the dragged one stays fixed (right edge for a left drag,
    /// bottom edge for a top drag). The other axis keeps its top or left edge.
    /// </summary>
    private void UpdateResize(ResizeEdge edge, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndResize();
            return;
        }

        Point pointer = PointToScreen(e.GetPosition(this));
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double dx = (pointer.X - _resizePointerStart.X) / dpi.DpiScaleX;
        double dy = (pointer.Y - _resizePointerStart.Y) / dpi.DpiScaleY;
        bool horizontal = edge is ResizeEdge.Left or ResizeEdge.Right;
        double delta = edge switch
        {
            ResizeEdge.Right => dx,
            ResizeEdge.Left => -dx,
            ResizeEdge.Bottom => dy,
            _ => -dy,
        };

        ResizeStart start = _resizeStart;
        double extent = horizontal ? start.Width : start.Height;
        if (extent <= 0)
        {
            return;
        }

        double scale = PanelPlacement.ClampScale(start.Scale * (extent + delta) / extent);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
        UpdateLayout();

        double left = edge == ResizeEdge.Left ? start.Left + start.Width - ActualWidth : start.Left;
        double top = edge == ResizeEdge.Top ? start.Top + start.Height - ActualHeight : start.Top;
        State.Config.Panels[PanelKey].Scale = scale;
        if (SetPlacementFromRect(left, top))
        {
            AnchorToPlacement();
        }
    }

    private void EndResize()
    {
        if (_resizeEdge is null)
        {
            return;
        }

        _resizeEdge = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        State.Config.Save();
    }

    private void PersistPlacement()
    {
        if (SetPlacementFromRect(Left, Top))
        {
            State.Config.Save();
        }
    }

    /// <summary>
    /// Stores the anchor position for the window's current size, given its
    /// top-left corner on screen. Returns false when there is no work area.
    /// </summary>
    private bool SetPlacementFromRect(double left, double top)
    {
        var placement = State.Config.Panels[PanelKey];
        var work = SystemParameters.WorkArea;
        if (work.Width <= 0 || work.Height <= 0)
        {
            return false;
        }

        placement.X = (left + AnchorOffsetX(placement.Anchor, ActualWidth) - work.Left) / work.Width;
        placement.Y = (top + AnchorOffsetY(placement.Anchor, ActualHeight) - work.Top) / work.Height;
        return true;
    }

    private static double AnchorOffsetX(PanelAnchor anchor, double width) => anchor switch
    {
        PanelAnchor.TopRight or PanelAnchor.BottomRight => width,
        PanelAnchor.TopCenter or PanelAnchor.BottomCenter or PanelAnchor.Center => width / 2,
        _ => 0,
    };

    private static double AnchorOffsetY(PanelAnchor anchor, double height) => anchor switch
    {
        PanelAnchor.BottomLeft or PanelAnchor.BottomRight or PanelAnchor.BottomCenter => height,
        PanelAnchor.Center => height / 2,
        _ => 0,
    };

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var compound = new MenuItem { Header = "Tire compound" };
        foreach (var preset in TireCompound.All)
        {
            var item = new MenuItem { Header = preset.Name, Tag = preset.Name, IsCheckable = true };
            item.Click += (_, _) => State.ApplyCompound(preset.Name);
            compound.Items.Add(item);
        }

        menu.Items.Add(compound);
        menu.Opened += (_, _) => SyncCompoundChecks(compound);

        var reset = new MenuItem { Header = "Reset all timers" };
        reset.Click += (_, _) => State.ResetTimers();
        menu.Items.Add(reset);

        var clickThrough = new MenuItem { Header = "Click-through (pass input to game)" };
        clickThrough.Click += (_, _) => ToggleClickThroughAll();
        menu.Items.Add(clickThrough);

        var panels = new MenuItem { Header = "Panels" };
        foreach (var (key, label) in HideablePanels)
        {
            var item = new MenuItem { Header = label, Tag = key, IsCheckable = true };
            item.Click += (_, _) => TogglePanelHidden(key);
            panels.Items.Add(item);
        }

        panels.Items.Add(new Separator());
        var showAll = new MenuItem { Header = "Show all panels" };
        showAll.Click += (_, _) => ShowAllPanels();
        panels.Items.Add(showAll);
        menu.Items.Add(panels);

        var autoHide = new MenuItem { Header = "Auto-hide when idle" };
        foreach (var (key, label) in HideablePanels)
        {
            var item = new MenuItem { Header = label, Tag = key, IsCheckable = true };
            item.Click += (_, _) => TogglePanelAutoHide(key);
            autoHide.Items.Add(item);
        }

        menu.Items.Add(autoHide);
        menu.Opened += (_, _) => SyncPanelChecks(panels, autoHide);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    private void SyncCompoundChecks(MenuItem compoundMenu)
    {
        foreach (var entry in compoundMenu.Items)
        {
            if (entry is MenuItem item)
            {
                item.IsChecked = item.Tag is string name
                    && name.Equals(State.Config.TireCompound, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void SyncPanelChecks(MenuItem hideMenu, MenuItem autoHideMenu)
    {
        foreach (var entry in hideMenu.Items)
        {
            if (entry is MenuItem { Tag: string key } item)
            {
                item.IsChecked = !State.Config.Panels[key].Hidden;
            }
        }

        foreach (var entry in autoHideMenu.Items)
        {
            if (entry is MenuItem { Tag: string key } item)
            {
                item.IsChecked = State.Config.Panels[key].AutoHide;
            }
        }
    }

    private void TogglePanelHidden(string key)
    {
        var placement = State.Config.Panels[key];
        placement.Hidden = !placement.Hidden;
        State.Config.Save();
    }

    private void TogglePanelAutoHide(string key)
    {
        var placement = State.Config.Panels[key];
        placement.AutoHide = !placement.AutoHide;
        State.Config.Save();
    }

    private void ShowAllPanels()
    {
        foreach (var (key, _) in HideablePanels)
        {
            State.Config.Panels[key].Hidden = false;
        }

        State.Config.Save();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        All.Remove(this);
        if (ReferenceEquals(_source, _hotkeySource))
        {
            if (HotkeyAvailable && _source?.Handle is { } handle && handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, HotkeyId);
            }

            _hotkeySource = null;
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleClickThroughAll();
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
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private enum ResizeEdge
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    /// <summary>Window geometry and scale captured when an edge drag starts.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ResizeStart(double Left, double Top, double Width, double Height, double Scale);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

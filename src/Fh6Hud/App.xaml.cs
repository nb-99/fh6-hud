using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Fh6Hud.Telemetry;

namespace Fh6Hud;

/// <summary>
/// Creates the shared telemetry state and the panel windows, and drives the
/// per-frame update: one <see cref="HudState.Tick"/>, then every panel renders
/// its slice. Panels only close via Quit, which shuts the whole app down.
/// A <c>--port N</c> argument overrides the configured port for this process
/// (test instances can then run beside the production app without sharing
/// its port); <c>--debug</c> enables hud.log for a single run.
/// </summary>
public partial class App : Application
{
    private const int WatchdogIntervalMs = 2000;
    private const int WatchdogReportEveryTicks = 5; // 10 s

    private readonly List<PanelWindow> _panels = new();
    private HudState? _state;
    private DispatcherTimer? _watchdog;

    // Counters for the watchdog: render ticks since last interval, and the
    // listener's packet counter at the previous interval.
    private int _renderTicks;
    private long _lastPackets;
    private int _watchdogTicks;
    private int _reportRenderTicks;
    private long _reportPackets;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool debug = e.Args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase));
        string logPath = Path.Combine(AppContext.BaseDirectory, "hud.log");
        HudLog.Initialize(logPath, debug);
        WireExceptionLogging();

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _state = new HudState();
        _state.Initialize(HudConfig.ParsePortOverride(e.Args));

        HudLog.Initialize(logPath, debug || _state.Config.DebugLog);
        if (_state.ListenerError is not null)
        {
            HudLog.Error(_state.ListenerError);
        }

        // The status panel is created first: it owns the global hotkey
        // registration and stays visible even without telemetry.
        _panels.Add(new Panels.StatusPanel(_state));
        _panels.Add(new Panels.TirePanel(_state));
        _panels.Add(new Panels.EnginePanel(_state));
        _panels.Add(new Panels.IntervalPanel(_state));
        _panels.Add(new Panels.SpeedoPanel(_state));

        foreach (var panel in _panels)
        {
            panel.Show();
        }

        CompositionTarget.Rendering += OnRendering;
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(WatchdogIntervalMs) };
        _watchdog.Tick += OnWatchdogTick;
        _watchdog.Start();

        HudLog.Info($"started port={(int?)_state.Listener?.Port ?? _state.Config.Port} debug={HudLog.Enabled}");
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        _renderTicks++;
        _state!.Tick();
        foreach (var panel in _panels)
        {
            panel.RenderTick();
        }
    }

    /// <summary>
    /// Runs on the dispatcher (proven alive even when rendering stalls — the
    /// global hotkey already works in that state) and compares how many UI
    /// frames were produced against how many packets arrived. This is the
    /// decisive measurement for "HUD frozen while the game has focus": a
    /// healthy HUD shows both rates at ~60/s; a stalled render loop shows
    /// packets flowing with zero frames.
    /// </summary>
    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        int renders = _renderTicks;
        _renderTicks = 0;

        long packets = _state!.PacketsReceived;
        long newPackets = packets - _lastPackets;
        _lastPackets = packets;

        double ageMs = (DateTime.UtcNow - _state.LastPacketAtUtc).TotalMilliseconds;
        _watchdogTicks++;
        _reportRenderTicks += renders;
        _reportPackets += newPackets;

        if (renders == 0 && newPackets > 0)
        {
            HudLog.Error(
                $"RENDER STALLED: 0 UI frames in {WatchdogIntervalMs} ms while {newPackets} packets arrived " +
                $"(last packet {ageMs:0} ms ago, live={_state.Live})");
        }

        if (_watchdogTicks % WatchdogReportEveryTicks == 0)
        {
            double seconds = WatchdogIntervalMs * WatchdogReportEveryTicks / 1000.0;
            HudLog.Info(
                $"DIAG packets={_reportPackets / seconds:0.0}/s renders={_reportRenderTicks / seconds:0.0}/s " +
                $"lastPacketAge={ageMs:0}ms live={_state.Live} parseFailures={_state.ParseFailures} " +
                $"receiveErrors={_state.ReceiveErrors}");
            _reportPackets = 0;
            _reportRenderTicks = 0;
        }
    }

    private void WireExceptionLogging()
    {
        DispatcherUnhandledException += (_, e) =>
            HudLog.Error("unhandled dispatcher exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HudLog.Error("unhandled app exception",
                e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            HudLog.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HudLog.Info("shutdown");
        CompositionTarget.Rendering -= OnRendering;
        _watchdog?.Stop();
        _state?.Dispose();
        base.OnExit(e);
    }
}

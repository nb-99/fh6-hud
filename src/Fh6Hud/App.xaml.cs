using System.IO;
using System.Windows;
using System.Windows.Media;
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
    private System.Threading.Timer? _watchdog;

    // Counters for the watchdog: render ticks since last interval, and the
    // listener's packet counter at the previous interval.
    private long _renderTicks;
    private long _lastPackets;
    private int _watchdogTicks;
    private int _reportRenderTicks;
    private long _reportPackets;
    private long _lastRenderTimestamp;
    private int _watchdogCallbackRunning;
    private bool _stallReported;

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
        _panels.Add(new Panels.ShiftCuePanel(_state));
        _panels.Add(new Panels.IntervalPanel(_state));
        _panels.Add(new Panels.SpeedoPanel(_state));

        foreach (var panel in _panels)
        {
            panel.Show();
        }

        CompositionTarget.Rendering += OnRendering;
        // This must run off the WPF dispatcher. A dispatcher timer cannot
        // detect the exact failure mode we care about because it freezes with
        // the render loop.
        _watchdog = new System.Threading.Timer(
            OnWatchdogTick,
            state: null,
            dueTime: WatchdogIntervalMs,
            period: WatchdogIntervalMs);

        HudLog.Info($"started port={(int?)_state.Listener?.Port ?? _state.Config.Port} debug={HudLog.Enabled}");
        HudLog.Health($"[HUD-HEALTH] started port={(int?)_state.Listener?.Port ?? _state.Config.Port} " +
                     $"debug={HudLog.Enabled} hotkeyAvailable={PanelWindow.HotkeyAvailable}");
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _renderTicks);
        Volatile.Write(ref _lastRenderTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
        _state!.Tick();
        foreach (var panel in _panels)
        {
            try
            {
                panel.RenderTick();
            }
            catch (Exception ex)
            {
                HudLog.Error($"render failed panel={panel.GetType().Name}", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Runs on a thread-pool thread and compares how many UI frames were
    /// produced against how many packets arrived. Keeping this independent of
    /// WPF is the decisive measurement for "HUD frozen while the game has
    /// focus": a healthy HUD shows both rates at ~60/s; a stalled render loop
    /// shows packets flowing with zero frames.
    /// </summary>
    private void OnWatchdogTick(object? state)
    {
        if (Interlocked.Exchange(ref _watchdogCallbackRunning, 1) != 0)
        {
            return;
        }

        try
        {
            if (_state is not { } hudState)
            {
                return;
            }

            int renders = (int)Interlocked.Exchange(ref _renderTicks, 0);
            long packets = hudState.PacketsReceived;
            long newPackets = packets - _lastPackets;
            _lastPackets = packets;

            double ageMs = (DateTime.UtcNow - hudState.LastPacketAtUtc).TotalMilliseconds;
            long renderTimestamp = Volatile.Read(ref _lastRenderTimestamp);
            double renderAgeMs = renderTimestamp == 0
                ? double.PositiveInfinity
                : (System.Diagnostics.Stopwatch.GetTimestamp() - renderTimestamp)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _watchdogTicks++;
            _reportRenderTicks += renders;
            _reportPackets += newPackets;

            bool renderStalled = renderTimestamp != 0 && renderAgeMs > WatchdogIntervalMs * 1.5;
            if (renderStalled)
            {
                if (!_stallReported || _watchdogTicks % WatchdogReportEveryTicks == 0)
                {
                    HudLog.Error(
                        $"RENDER STALLED: frames={renders} packets={newPackets} " +
                        $"renderAge={renderAgeMs:0}ms packetAge={ageMs:0}ms " +
                        $"live={hudState.Live} parseFailures={hudState.ParseFailures} " +
                        $"receiveErrors={hudState.ReceiveErrors}");
                }

                _stallReported = true;
            }
            else
            {
                _stallReported = false;
            }

            if (_watchdogTicks % WatchdogReportEveryTicks == 0)
            {
                double seconds = WatchdogIntervalMs * WatchdogReportEveryTicks / 1000.0;
                HudLog.Health(
                    $"[HUD-WATCHDOG] packets={_reportPackets / seconds:0.0}/s " +
                    $"renders={_reportRenderTicks / seconds:0.0}/s " +
                    $"renderAge={renderAgeMs:0}ms packetAge={ageMs:0}ms live={hudState.Live} " +
                    $"parseFailures={hudState.ParseFailures} receiveErrors={hudState.ReceiveErrors}");
                _reportPackets = 0;
                _reportRenderTicks = 0;
            }
        }
        catch (Exception ex)
        {
            // Diagnostics must not become a second failure path.
            HudLog.Error("watchdog failed", ex);
        }
        finally
        {
            Volatile.Write(ref _watchdogCallbackRunning, 0);
        }
    }

    private void WireExceptionLogging()
    {
        DispatcherUnhandledException += (_, e) =>
            HudLog.Error(
                $"unhandled dispatcher exception renderTicks={Interlocked.Read(ref _renderTicks)} " +
                $"packets={_state?.PacketsReceived ?? 0}",
                e.Exception);
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
        _watchdog?.Dispose();
        _state?.Dispose();
        base.OnExit(e);
    }
}

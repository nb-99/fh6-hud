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
/// its port).
/// </summary>
public partial class App : Application
{
    private readonly List<PanelWindow> _panels = new();
    private HudState? _state;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _state = new HudState();
        _state.Initialize(HudConfig.ParsePortOverride(e.Args));

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
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        _state!.Tick();
        foreach (var panel in _panels)
        {
            panel.RenderTick();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _state?.Dispose();
        base.OnExit(e);
    }
}

using System.Net.Sockets;
using Fh6Hud.Telemetry;

namespace Fh6Hud;

/// <summary>
/// Central telemetry state shared by all panel windows: owns the UDP listener,
/// the latest packet, and the stateful trackers (power curve, gear ratios,
/// shift points, interval timers). <see cref="Tick"/> runs once per UI frame
/// from the App's CompositionTarget.Rendering handler; panels then render
/// their slice via <c>PanelWindow.RenderTick</c>.
/// </summary>
/// <remarks>
/// Listener callbacks arrive on a background thread — only the latest-packet
/// reference crosses threads (atomic reference assignment), exactly like the
/// previous single-window version.
/// </remarks>
public sealed class HudState : IDisposable
{
    private const double StaleAfterSeconds = 2.0;

    private int _lastCarOrdinal = int.MinValue;

    public HudState()
    {
        ShiftAdvisor = new ShiftPointAdvisor(PowerCurve, GearRatios);
    }

    public HudConfig Config { get; private set; } = null!;

    public UdpTelemetryListener? Listener { get; private set; }

    /// <summary>Set when the configured port could not be bound.</summary>
    public string? ListenerError { get; private set; }

    public Fh6Packet? Latest { get; private set; }

    public DateTime LastPacketAtUtc { get; private set; } = DateTime.MinValue;

    public PowerCurveTracker PowerCurve { get; } = new();

    public GearRatioTracker GearRatios { get; } = new();

    public ShiftPointAdvisor ShiftAdvisor { get; }

    public SpeedIntervalTimer Timer0To100 { get; } = new(3f, 100f);

    public SpeedIntervalTimer Timer100To200 { get; } = new(100f, 200f);

    public SpeedIntervalTimer Timer200To300 { get; } = new(200f, 300f);

    /// <summary>True while fresh race telemetry flows (driving, not in menus).</summary>
    public bool Live { get; private set; }

    /// <summary>Reason the HUD is not live, for the status panel.</summary>
    public string NoDataMessage { get; private set; } = "FH6 // NO DATA";

    /// <summary>
    /// Loads the config and starts the UDP listener.
    /// </summary>
    /// <param name="portOverride">
    /// Binds this port instead of <see cref="HudConfig.Port"/> (CLI
    /// <c>--port</c> for test instances). <c>Config.Port</c> is deliberately
    /// left untouched so a later <c>Save()</c> cannot persist the test port
    /// into config.json.
    /// </param>
    public void Initialize(int? portOverride = null)
    {
        Config = HudConfig.Load();
        ApplyCompound(Config.TireCompound, save: false);

        int bindPort = portOverride ?? Config.Port;
        try
        {
            Listener = new UdpTelemetryListener(bindPort);
            Listener.PacketReceived += OnPacketReceived;
        }
        catch (SocketException)
        {
            ListenerError = $"PORT {bindPort} UNAVAILABLE";
        }
    }

    /// <summary>Advances all per-frame state from the latest packet. Call once per UI frame.</summary>
    public void Tick()
    {
        var packet = Latest;
        bool stale = (DateTime.UtcNow - LastPacketAtUtc).TotalSeconds > StaleAfterSeconds;
        bool live = packet is not null && !stale && packet.IsRaceOn != 0;

        if (live != Live)
        {
            Live = live;
            if (packet is null || stale)
            {
                NoDataMessage = "FH6 // NO DATA";
            }
            else
            {
                NoDataMessage = packet.IsRaceOn == 0 ? "FH6 // IN MENU" : "FH6 // NO DATA";
            }

            HudLog.Info($"telemetry live={live} ({NoDataMessage})");
        }

        if (!Live)
        {
            return; // timers hold: Update() is not called while there is no live data
        }

        if (packet!.CarOrdinal != _lastCarOrdinal)
        {
            _lastCarOrdinal = packet.CarOrdinal;
            PowerCurve.Reset();
            GearRatios.Reset();
            ShiftAdvisor.ResetLatch();
        }

        PowerCurve.Configure(packet.EngineMaxRpm);
        PowerCurve.AddSample(packet.CurrentEngineRpm, packet.PowerWatts);
        GearRatios.AddSample(packet);
        ShiftAdvisor.Recalculate(packet.EngineMaxRpm);

        Timer0To100.Update(packet.SpeedKmh, packet.TimestampMs);
        Timer100To200.Update(packet.SpeedKmh, packet.TimestampMs);
        Timer200To300.Update(packet.SpeedKmh, packet.TimestampMs);
    }

    public void ApplyCompound(string name, bool save = true)
    {
        var preset = TireCompound.Find(name);
        if (preset is null)
        {
            return;
        }

        Config.TireCompound = preset.Name;
        Config.TireOptMinC = preset.MinC;
        Config.TireOptMaxC = preset.MaxC;
        if (save)
        {
            Config.Save();
        }
    }

    public void ResetTimers()
    {
        Timer0To100.ResetAll();
        Timer100To200.ResetAll();
        Timer200To300.ResetAll();
    }

    private void OnPacketReceived(object? sender, Fh6Packet packet)
    {
        Latest = packet;
        LastPacketAtUtc = DateTime.UtcNow;
    }

    public void Dispose() => Listener?.Dispose();
}

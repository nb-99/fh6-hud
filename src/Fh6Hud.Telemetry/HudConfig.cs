using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Hud;

public sealed class HudConfig
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<PanelAnchor>() },
    };

    /// <summary>UDP port the game sends Data Out packets to (avoid 5200-5300).</summary>
    public int Port { get; set; } = 45000;

    /// <summary>Selected tire compound preset name (see Telemetry/TireCompound.cs).</summary>
    public string TireCompound { get; set; } = "Rally";

    /// <summary>Optimal tire operating temperature range in °C. Below = cold, above = hot.</summary>
    public float TireOptMinC { get; set; } = 72f;

    public float TireOptMaxC { get; set; } = 90f;

    /// <summary>
    /// Per-panel positions (see <see cref="PanelPlacement"/>), keyed by
    /// <see cref="PanelKeys"/>. Updated and saved when a panel is dragged.
    /// </summary>
    public Dictionary<string, PanelPlacement> Panels { get; set; } = CreateDefaultPanels();

    /// <summary>First-run layout, as fractions of the work area.</summary>
    public static Dictionary<string, PanelPlacement> CreateDefaultPanels() => new()
    {
        // Tire temps bottom-left, 20% in from the left and bottom edges.
        [PanelKeys.Tires] = new() { X = 0.20, Y = 0.80, Anchor = PanelAnchor.BottomLeft },
        // Engine/RPM bottom-right, right edge at 80% of the screen width.
        [PanelKeys.Engine] = new() { X = 0.80, Y = 0.80, Anchor = PanelAnchor.BottomRight },
        // Interval timers on the right edge at 25% of the screen height...
        [PanelKeys.Intervals] = new() { X = 1.00, Y = 0.25, Anchor = PanelAnchor.TopRight },
        // ...with the speedometer below them.
        [PanelKeys.Speedo] = new() { X = 1.00, Y = 0.42, Anchor = PanelAnchor.TopRight },
        // Status line and hints bottom-middle.
        [PanelKeys.Status] = new() { X = 0.50, Y = 0.92, Anchor = PanelAnchor.BottomCenter },
    };

    [JsonIgnore]
    public string SourcePath { get; private set; } = "";

    /// <summary>
    /// True when the config file existed but could not be parsed; the app then
    /// runs on defaults and should tell the user (see FUNC-5).
    /// </summary>
    [JsonIgnore]
    public bool LoadFailed { get; private set; }

    /// <summary>The parse error when <see cref="LoadFailed"/> is true, for diagnostics.</summary>
    [JsonIgnore]
    public string? LoadError { get; private set; }

    public static HudConfig Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            return new HudConfig { SourcePath = path };
        }

        try
        {
            var config = JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(path), SerializerOptions);
            if (config is null)
            {
                return new HudConfig { SourcePath = path, LoadFailed = true };
            }

            // A hand-edited config may name only some panels; fill in the rest.
            config.Panels ??= CreateDefaultPanels();
            foreach (var (key, placement) in CreateDefaultPanels())
            {
                config.Panels.TryAdd(key, placement);
            }

            config.SourcePath = path;
            return config;
        }
        catch (JsonException ex)
        {
            return new HudConfig { SourcePath = path, LoadFailed = true, LoadError = ex.Message };
        }
    }

    /// <summary>
    /// Reads a <c>--port</c> value from command-line args (both
    /// <c>--port 45001</c> and <c>--port=45001</c>). Lets a test/development
    /// instance of the HUD bind a different port than the production app
    /// while the game keeps streaming to the configured one. Returns null
    /// when absent or invalid — callers then use the config value.
    /// </summary>
    public static int? ParsePortOverride(string[] args)
    {
        const string prefix = "--port=";
        for (int i = 0; i < args.Length; i++)
        {
            string? value = null;
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = args[i][prefix.Length..];
            }
            else if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                value = args[++i];
            }

            if (value is not null && int.TryParse(value, out int port) && port is > 0 and <= 65535)
            {
                return port;
            }
        }

        return null;
    }

    /// <summary>Writes the current values back to the config file (default: the loaded path).</summary>
    public void Save(string? path = null)
    {
        path ??= SourcePath;
        if (string.IsNullOrEmpty(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, FileName);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }
}

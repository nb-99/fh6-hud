using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Hud;

public sealed class HudConfig
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>UDP port the game sends Data Out packets to (avoid 5200-5300).</summary>
    public int Port { get; set; } = 45000;

    /// <summary>Selected tire compound preset name (see Telemetry/TireCompound.cs).</summary>
    public string TireCompound { get; set; } = "Rally";

    /// <summary>Optimal tire operating temperature range in °C. Below = cold, above = hot.</summary>
    public float TireOptMinC { get; set; } = 72f;

    public float TireOptMaxC { get; set; } = 90f;

    [JsonIgnore]
    public string SourcePath { get; private set; } = "";

    /// <summary>
    /// True when the config file existed but could not be parsed; the app then
    /// runs on defaults and should tell the user (see FUNC-5).
    /// </summary>
    [JsonIgnore]
    public bool LoadFailed { get; private set; }

    public static HudConfig Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            return new HudConfig { SourcePath = path };
        }

        try
        {
            var config = JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(path));
            if (config is null)
            {
                return new HudConfig { SourcePath = path, LoadFailed = true };
            }

            config.SourcePath = path;
            return config;
        }
        catch (JsonException)
        {
            return new HudConfig { SourcePath = path, LoadFailed = true };
        }
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

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Hud;

public sealed class HudConfig
{
    public const string FileName = "config.json";

    /// <summary>UDP port the game sends Data Out packets to (avoid 5200-5300).</summary>
    public int Port { get; set; } = 45000;

    /// <summary>Selected tire compound preset name (see Telemetry/TireCompound.cs).</summary>
    public string TireCompound { get; set; } = "Race";

    /// <summary>Optimal tire operating temperature range in °C. Below = cold, above = hot.</summary>
    public float TireOptMinC { get; set; } = 86f;

    public float TireOptMaxC { get; set; } = 104f;

    [JsonIgnore]
    public string SourcePath { get; private set; } = "";

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
            config!.SourcePath = path;
            return config;
        }
        catch (JsonException)
        {
            return new HudConfig { SourcePath = path };
        }
    }
}

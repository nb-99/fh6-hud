namespace Fh6Hud.Telemetry;

/// <summary>
/// Tire compound presets with their approximate optimal operating temperature
/// ranges in Celsius. FH6 Data Out does NOT expose the equipped tire compound,
/// so the driver must select it manually.
/// </summary>
/// <remarks>
/// Ranges are community-observed approximations `[SRC]` (the official spec
/// omits them; stock tires are reported around 180-195 °F = 82-91 °C) and are
/// editable starting points — calibrate them against observed in-game behavior.
/// </remarks>
public static class TireCompound
{
    public sealed class Preset
    {
        public Preset(string name, float minC, float maxC)
        {
            Name = name;
            MinC = minC;
            MaxC = maxC;
        }

        public string Name { get; }

        public float MinC { get; }

        public float MaxC { get; }
    }

    public static readonly IReadOnlyList<Preset> All = new[]
    {
        new Preset("Standard", 82f, 91f),
        new Preset("Street", 75f, 95f),
        new Preset("Sport", 82f, 102f),
        new Preset("Rally", 72f, 90f),
        new Preset("Semi-Slick", 84f, 106f),
        new Preset("Slick", 90f, 110f),
        new Preset("Offroad", 68f, 88f),
        new Preset("Snow", 62f, 85f),
        new Preset("Vintage", 78f, 98f),
        new Preset("Vintage Race", 84f, 104f),
    };

    public static Preset? Find(string? name) =>
        string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

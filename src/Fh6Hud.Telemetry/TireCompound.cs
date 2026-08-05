namespace Fh6Hud.Telemetry;

/// <summary>
/// Tire compound presets with their approximate optimal operating temperature
/// ranges in Celsius. FH6 Data Out does NOT expose the equipped tire compound,
/// so the driver must select it manually; these defaults are editable starting
/// points to be refined against observed in-game behavior.
/// </summary>
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
        new Preset("Street", 75f, 95f),
        new Preset("Sport", 82f, 102f),
        new Preset("Race", 86f, 104f),
        new Preset("Slick", 90f, 110f),
        new Preset("Rally", 72f, 90f),
        new Preset("Drag", 80f, 95f),
    };

    public static Preset? Find(string? name) =>
        string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
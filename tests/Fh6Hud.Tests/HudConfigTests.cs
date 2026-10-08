using Fh6Hud;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class HudConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fh6-hud-tests-" + Guid.NewGuid().ToString("N"));

    public HudConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsAndSetsSourcePath()
    {
        var path = PathFor("missing.json");

        var config = HudConfig.Load(path);

        Assert.False(config.LoadFailed);
        Assert.Equal(path, config.SourcePath);
        Assert.Equal(45000, config.Port);
        Assert.Equal("Rally", config.TireCompound);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaultsWithLoadFailed()
    {
        var path = PathFor("bad.json");
        File.WriteAllText(path, "{ not json !!");

        var config = HudConfig.Load(path);

        Assert.True(config.LoadFailed);
        Assert.Equal(path, config.SourcePath);
        Assert.Equal(45000, config.Port);
    }

    [Fact]
    public void Load_NullJsonLiteral_ReturnsDefaultsWithLoadFailed()
    {
        var path = PathFor("null.json");
        File.WriteAllText(path, "null");

        var config = HudConfig.Load(path);

        Assert.True(config.LoadFailed);
    }

    [Fact]
    public void Load_ValidConfig_RoundTrips()
    {
        var path = PathFor("valid.json");
        File.WriteAllText(path, """{"Port": 12345, "DebugLog": true, "TireCompound": "Slick", "TireOptMinC": 90.0, "TireOptMaxC": 110.0}""");

        var config = HudConfig.Load(path);

        Assert.False(config.LoadFailed);
        Assert.Equal(12345, config.Port);
        Assert.True(config.DebugLog);
        Assert.Equal("Slick", config.TireCompound);
        Assert.Equal(90f, config.TireOptMinC);
        Assert.Equal(110f, config.TireOptMaxC);
    }

    [Fact]
    public void Save_WritesFileThatReloadsToSameValues()
    {
        var path = PathFor("roundtrip.json");
        var config = HudConfig.Load(path);
        config.Port = 54321;
        config.DebugLog = true;
        config.TireCompound = "Snow";
        config.TireOptMinC = 62f;
        config.TireOptMaxC = 85f;

        config.Save(path);

        var reloaded = HudConfig.Load(path);
        Assert.False(reloaded.LoadFailed);
        Assert.Equal(54321, reloaded.Port);
        Assert.True(reloaded.DebugLog);
        Assert.Equal("Snow", reloaded.TireCompound);
        Assert.Equal(62f, reloaded.TireOptMinC);
        Assert.Equal(85f, reloaded.TireOptMaxC);
    }

    [Fact]
    public void Save_DoesNotPersistRuntimeFlags()
    {
        var path = PathFor("flags.json");
        var config = HudConfig.Load(path);
        config.Save(path);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("SourcePath", json, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadFailed", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingFile_ProvidesDefaultPanelLayout()
    {
        var config = HudConfig.Load(PathFor("missing-panels.json"));

        Assert.Equal(6, config.Panels.Count);
        var tires = config.Panels[PanelKeys.Tires];
        Assert.Equal(0.20, tires.X);
        Assert.Equal(0.80, tires.Y);
        Assert.Equal(PanelAnchor.BottomLeft, tires.Anchor);
        Assert.Equal(PanelAnchor.TopRight, config.Panels[PanelKeys.Intervals].Anchor);
        Assert.Equal(0.50, config.Panels[PanelKeys.ShiftCue].X);
        Assert.Equal(0.50, config.Panels[PanelKeys.ShiftCue].Y);
        Assert.Equal(PanelAnchor.Center, config.Panels[PanelKeys.ShiftCue].Anchor);
    }

    [Fact]
    public void Load_PartialPanels_FillsInMissingDefaults()
    {
        var path = PathFor("partial-panels.json");
        File.WriteAllText(path, """{"Panels": {"Engine": {"X": 0.5, "Y": 0.5, "Anchor": "TopLeft"}}}""");

        var config = HudConfig.Load(path);

        Assert.False(config.LoadFailed);
        Assert.Equal(6, config.Panels.Count);
        Assert.Equal(0.5, config.Panels[PanelKeys.Engine].X);
        Assert.Equal(0.20, config.Panels[PanelKeys.Tires].X); // default filled in
    }

    [Fact]
    public void Save_RoundTripsPanelPlacements()
    {
        var path = PathFor("panels-roundtrip.json");
        var config = HudConfig.Load(path);
        config.Panels[PanelKeys.Speedo] = new PanelPlacement { X = 0.33, Y = 0.66, Anchor = PanelAnchor.BottomRight };

        config.Save(path);

        var reloaded = HudConfig.Load(path);
        var speedo = reloaded.Panels[PanelKeys.Speedo];
        Assert.Equal(0.33, speedo.X);
        Assert.Equal(0.66, speedo.Y);
        Assert.Equal(PanelAnchor.BottomRight, speedo.Anchor);
    }

    [Fact]
    public void Load_MissingFile_DefaultsKeepPanelsVisibleAndUnscaled()
    {
        var config = HudConfig.Load(PathFor("defaults-presentation.json"));

        Assert.Equal(10.0, config.IdleHideSeconds);
        foreach (var placement in config.Panels.Values)
        {
            Assert.Equal(1.0, placement.Scale);
            Assert.False(placement.Hidden);
            Assert.False(placement.AutoHide);
        }
    }

    [Fact]
    public void Load_LegacyPlacementWithoutNewFields_UsesDefaults()
    {
        var path = PathFor("legacy-placement.json");
        File.WriteAllText(path, """{"Panels": {"Speedo": {"X": 0.9, "Y": 0.1, "Anchor": "TopRight"}}}""");

        var speedo = HudConfig.Load(path).Panels[PanelKeys.Speedo];

        Assert.Equal(0.9, speedo.X);
        Assert.Equal(1.0, speedo.Scale);
        Assert.False(speedo.Hidden);
        Assert.False(speedo.AutoHide);
    }

    [Fact]
    public void Save_RoundTripsScaleVisibilityAndIdleThreshold()
    {
        var path = PathFor("presentation-roundtrip.json");
        var config = HudConfig.Load(path);
        config.IdleHideSeconds = 4.5;
        config.Panels[PanelKeys.Tires].Scale = 1.75;
        config.Panels[PanelKeys.Tires].Hidden = true;
        config.Panels[PanelKeys.Engine].AutoHide = true;

        config.Save(path);

        var reloaded = HudConfig.Load(path);
        Assert.Equal(4.5, reloaded.IdleHideSeconds);
        Assert.Equal(1.75, reloaded.Panels[PanelKeys.Tires].Scale);
        Assert.True(reloaded.Panels[PanelKeys.Tires].Hidden);
        Assert.True(reloaded.Panels[PanelKeys.Engine].AutoHide);
        Assert.False(reloaded.Panels[PanelKeys.Speedo].Hidden);
    }

    [Fact]
    public void Save_RoundTripsPerPanelIdleHideSeconds()
    {
        var path = PathFor("per-panel-idle.json");
        var config = HudConfig.Load(path);
        config.Panels[PanelKeys.Engine].IdleHideSeconds = 30.0;

        config.Save(path);

        var reloaded = HudConfig.Load(path);
        Assert.Equal(30.0, reloaded.Panels[PanelKeys.Engine].IdleHideSeconds);
        Assert.Equal(2.0, reloaded.Panels[PanelKeys.Tires].IdleHideSeconds);
        Assert.Null(reloaded.Panels[PanelKeys.Speedo].IdleHideSeconds);
    }

    [Theory]
    [InlineData(null, 10.0, 10.0)]
    [InlineData(2.0, 10.0, 2.0)]
    [InlineData(0.0, 10.0, 0.0)]
    [InlineData(-1.0, 10.0, 10.0)]
    [InlineData(double.NaN, 10.0, 10.0)]
    public void EffectiveIdleHideSeconds_UsesOverrideElseGlobal(double? perPanel, double global, double expected)
    {
        var placement = new PanelPlacement { IdleHideSeconds = perPanel };

        Assert.Equal(expected, placement.EffectiveIdleHideSeconds(global));
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.1, PanelPlacement.MinScale)]
    [InlineData(9.0, PanelPlacement.MaxScale)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void ClampScale_KeepsScaleInSupportedRange(double input, double expected)
    {
        Assert.Equal(expected, PanelPlacement.ClampScale(input));
    }

    [Theory]
    [InlineData(new[] { "--port", "45001" }, 45001)]
    [InlineData(new[] { "--port=45001" }, 45001)]
    [InlineData(new[] { "ignored", "--port", "45002", "tail" }, 45002)]
    [InlineData(new[] { "--port", "0" }, null)]       // out of range
    [InlineData(new[] { "--port", "70000" }, null)]   // out of range
    [InlineData(new[] { "--port", "abc" }, null)]     // not a number
    [InlineData(new[] { "--port" }, null)]            // missing value
    [InlineData(new[] { "--scenario", "launch" }, null)] // no port arg
    [InlineData(new string[0], null)]
    public void ParsePortOverride_ReturnsFirstValidPortOrNull(string[] args, int? expected)
    {
        Assert.Equal(expected, HudConfig.ParsePortOverride(args));
    }

    private static readonly string[] InvalidThenValidPortArgs = { "--port", "abc", "--port", "45001" };

    [Fact]
    public void ParsePortOverride_FirstValidPortWins_OverInvalidEarlierOne()
    {
        Assert.Equal(45001, HudConfig.ParsePortOverride(InvalidThenValidPortArgs));
    }
}

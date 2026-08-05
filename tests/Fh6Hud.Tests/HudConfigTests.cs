using Fh6Hud;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class HudConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fh6-hud-tests-" + Guid.NewGuid().ToString("N"));

    public HudConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
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
        File.WriteAllText(path, """{"Port": 12345, "TireCompound": "Slick", "TireOptMinC": 90.0, "TireOptMaxC": 110.0}""");

        var config = HudConfig.Load(path);

        Assert.False(config.LoadFailed);
        Assert.Equal(12345, config.Port);
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
        config.TireCompound = "Snow";
        config.TireOptMinC = 62f;
        config.TireOptMaxC = 85f;

        config.Save(path);

        var reloaded = HudConfig.Load(path);
        Assert.False(reloaded.LoadFailed);
        Assert.Equal(54321, reloaded.Port);
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
        Assert.DoesNotContain("SourcePath", json);
        Assert.DoesNotContain("LoadFailed", json);
    }
}

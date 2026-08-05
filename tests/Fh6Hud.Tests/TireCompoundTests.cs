using Fh6Hud;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class TireCompoundTests
{
    public static readonly string[] ExpectedNames =
    {
        "Standard",
        "Street",
        "Sport",
        "Rally",
        "Semi-Slick",
        "Slick",
        "Offroad",
        "Snow",
        "Vintage",
        "Vintage Race",
    };

    [Theory]
    [InlineData("rally")]
    [InlineData("RALLY")]
    [InlineData("Semi-Slick")]
    [InlineData("semi-slick")]
    [InlineData("vintage race")]
    public void Find_IsCaseInsensitive(string name)
    {
        Assert.NotNull(TireCompound.Find(name));
    }

    [Fact]
    public void Find_Unknown_ReturnsNull()
    {
        Assert.Null(TireCompound.Find("Drift"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Find_NullOrBlank_ReturnsNull(string? name)
    {
        Assert.Null(TireCompound.Find(name));
    }

    [Fact]
    public void AllPresets_HaveMinBelowMax()
    {
        foreach (var preset in TireCompound.All)
        {
            Assert.True(preset.MinC < preset.MaxC, $"{preset.Name}: min {preset.MinC} >= max {preset.MaxC}");
        }
    }

    [Fact]
    public void ContainsCompleteCompoundSet()
    {
        var names = TireCompound.All.Select(p => p.Name).ToArray();
        Assert.Equal(ExpectedNames, names);
    }

    [Fact]
    public void DefaultCompound_Rally_IsInSet()
    {
        var config = new HudConfig();
        Assert.Equal("Rally", config.TireCompound);
        Assert.NotNull(TireCompound.Find(config.TireCompound));
    }
}

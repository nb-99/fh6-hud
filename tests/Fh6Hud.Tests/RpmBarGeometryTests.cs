using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class RpmBarGeometryTests
{
    [Theory]
    [InlineData(0, 7000, 0)]
    [InlineData(3500, 7000, 0.5)]
    [InlineData(6300, 7000, 0.9)]
    [InlineData(7000, 7000, 1.0)]
    [InlineData(9000, 7000, 1.0)] // clamped at redline
    [InlineData(-100, 7000, 0)]    // clamped at zero
    [InlineData(3500, 0, 0)]       // max RPM unknown
    public void FillFraction_ScalesRpmToBar(double rpm, double maxRpm, double expected)
    {
        Assert.Equal(expected, RpmBarGeometry.FillFraction(rpm, maxRpm), 6);
    }

    [Fact]
    public void RedlineZone_IsFixedAtTheTopTenPercent()
    {
        // The zone's position is a constant band — it never depends on RPM.
        Assert.Equal(0.10, RpmBarGeometry.RedlineZoneFraction, 6);
        Assert.Equal(0.90, RpmBarGeometry.RedlineStartFraction, 6);
    }

    [Fact]
    public void FillWidth_NeverEntersTheRedlineZone()
    {
        // Root cause of the "moving red": the blue fill crossed into the
        // translucent redline zone, blending with it, and the blend's edge
        // tracked the fill (it moved toward the blue and back). The fill must
        // stop exactly at the zone's left edge.
        for (double rpm = 0; rpm <= 7000; rpm += 100)
        {
            double width = RpmBarGeometry.FillWidthFraction(rpm, 7000);
            Assert.InRange(width, 0.0, RpmBarGeometry.RedlineStartFraction + 1e-9);
        }

        // At redline the bar sits against the zone; below the zone it scales
        // 1:1 with RPM.
        Assert.Equal(0.90, RpmBarGeometry.FillWidthFraction(7000, 7000), 6);
        Assert.Equal(0.45, RpmBarGeometry.FillWidthFraction(3150, 7000), 6);
    }
}

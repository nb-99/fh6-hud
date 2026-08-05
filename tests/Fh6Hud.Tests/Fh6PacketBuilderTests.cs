using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class Fh6PacketBuilderTests
{
    [Fact]
    public void Build_ProducesPacket_ParsableToSameValues()
    {
        var bytes = new Fh6PacketBuilder()
            .IsRaceOn(1)
            .TimestampMs(777u)
            .SpeedMs(27.78f)
            .PowerWatts(200000f)
            .TorqueNm(420f)
            .TireTemp(91f, 92f, 93f, 94f)
            .Gear(3)
            .Build();

        Assert.Equal(Fh6Packet.PacketSize, bytes.Length);

        var packet = Fh6Packet.Parse(bytes);
        Assert.NotNull(packet);
        Assert.Equal(1, packet!.IsRaceOn);
        Assert.Equal(777u, packet.TimestampMs);
        Assert.Equal(27.78f, packet.SpeedMs);
        Assert.Equal(200000f, packet.PowerWatts);
        Assert.Equal(420f, packet.TorqueNm);
        Assert.Equal(91f, packet.TireTempFrontLeft);
        Assert.Equal(92f, packet.TireTempFrontRight);
        Assert.Equal(93f, packet.TireTempRearLeft);
        Assert.Equal(94f, packet.TireTempRearRight);
        Assert.Equal(3, packet.Gear);
    }

    [Fact]
    public void Build_ReturnsIndependentCopy()
    {
        var builder = new Fh6PacketBuilder().IsRaceOn(1).SpeedMs(27.78f);

        var first = builder.Build();
        first[0] = 99;

        var second = builder.Build();
        var packet = Fh6Packet.Parse(second);
        Assert.NotNull(packet);
        Assert.Equal(1, packet!.IsRaceOn);
        Assert.Equal(27.78f, packet.SpeedMs);
    }

    [Fact]
    public void TireTempC_WritesFh6RawValueAndParsesBackToCelsius()
    {
        var bytes = new Fh6PacketBuilder()
            .TireTempC(60f, 70f, 80f, 90f)
            .Build();

        var packet = Fh6Packet.Parse(bytes);

        Assert.NotNull(packet);
        Assert.Equal(60f, packet!.TireTempFrontLeftC, 3);
        Assert.Equal(70f, packet.TireTempFrontRightC, 3);
        Assert.Equal(80f, packet.TireTempRearLeftC, 3);
        Assert.Equal(90f, packet.TireTempRearRightC, 3);
    }
}

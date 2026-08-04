using System.Buffers.Binary;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class Fh6PacketTests
{
    private static byte[] BuildPacket()
    {
        var data = new byte[Fh6Packet.PacketSize];
        WriteI32(data, 0, 1);              // IsRaceOn
        WriteU32(data, 4, 12345u);         // TimestampMS
        WriteF32(data, 8, 7000f);          // EngineMaxRpm
        WriteF32(data, 12, 900f);          // EngineIdleRpm
        WriteF32(data, 16, 4500f);         // CurrentEngineRpm
        WriteF32(data, 20, 1.5f);          // AccelerationX
        WriteF32(data, 24, 2.5f);          // AccelerationY
        WriteF32(data, 28, 9.81f);         // AccelerationZ
        WriteF32(data, 32, 0.1f);          // VelocityX
        WriteF32(data, 36, 0.2f);          // VelocityY
        WriteF32(data, 40, 30f);           // VelocityZ
        WriteF32(data, 44, 0.01f);         // AngularVelocityX
        WriteF32(data, 48, 0.02f);         // AngularVelocityY
        WriteF32(data, 52, 0.03f);         // AngularVelocityZ
        WriteF32(data, 56, 0.1f);          // Yaw
        WriteF32(data, 60, 0.2f);          // Pitch
        WriteF32(data, 64, 0.3f);          // Roll
        for (int i = 68; i < 84; i += 4)   // suspension travel normalized
        {
            WriteF32(data, i, 0.5f);
        }

        for (int i = 84; i < 100; i += 4)  // tire slip ratio
        {
            WriteF32(data, i, 0.1f);
        }

        for (int i = 100; i < 116; i += 4) // wheel rotation speed
        {
            WriteF32(data, i, 100f);
        }

        for (int i = 116; i < 132; i += 4) // on rumble strip
        {
            WriteI32(data, i, 1);
        }

        for (int i = 132; i < 148; i += 4) // in puddle
        {
            WriteI32(data, i, 0);
        }

        for (int i = 148; i < 164; i += 4) // surface rumble
        {
            WriteF32(data, i, 0.25f);
        }

        for (int i = 164; i < 180; i += 4) // slip angle
        {
            WriteF32(data, i, 0.15f);
        }

        for (int i = 180; i < 196; i += 4) // combined slip
        {
            WriteF32(data, i, 0.2f);
        }

        for (int i = 196; i < 212; i += 4) // suspension meters
        {
            WriteF32(data, i, 0.05f);
        }

        WriteI32(data, 212, 125);          // CarOrdinal
        WriteI32(data, 216, 5);            // CarClass
        WriteI32(data, 220, 850);          // CarPerformanceIndex
        WriteI32(data, 224, 2);            // DrivetrainType
        WriteI32(data, 228, 8);            // NumCylinders
        WriteU32(data, 232, 42u);          // CarGroup
        WriteF32(data, 236, 1.2f);         // SmashableVelDiff
        WriteF32(data, 240, 150f);         // SmashableMass
        WriteF32(data, 244, 123.5f);       // PositionX
        WriteF32(data, 248, 456.7f);       // PositionY
        WriteF32(data, 252, 789.1f);       // PositionZ
        WriteF32(data, 256, 27.78f);       // Speed (100 km/h)
        WriteF32(data, 260, 250000f);      // Power
        WriteF32(data, 264, 450f);         // Torque
        WriteF32(data, 268, 95f);          // TireTempFL
        WriteF32(data, 272, 80f);          // TireTempFR
        WriteF32(data, 276, 112f);         // TireTempRL
        WriteF32(data, 280, 100f);         // TireTempRR
        WriteF32(data, 284, 12.5f);        // Boost
        WriteF32(data, 288, 0.75f);        // Fuel
        WriteF32(data, 292, 5000f);        // DistanceTraveled
        WriteF32(data, 296, 91.2f);        // BestLap
        WriteF32(data, 300, 93.4f);        // LastLap
        WriteF32(data, 304, 92.1f);        // CurrentLap
        WriteF32(data, 308, 300.5f);       // CurrentRaceTime
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(312), 7); // LapNumber
        data[314] = 3;                     // RacePosition
        data[315] = 255;                   // Accel
        data[316] = 0;                     // Brake
        data[317] = 0;                     // Clutch
        data[318] = 0;                     // HandBrake
        data[319] = 2;                     // Gear
        data[320] = unchecked((byte)(-50)); // Steer
        data[321] = 100;                   // NormalizedDrivingLine
        data[322] = 0;                     // NormalizedAIBrakeDifference
        return data;
    }

    [Fact]
    public void Parse_ValidPacket_ReadsAllFields()
    {
        var packet = Fh6Packet.Parse(BuildPacket());

        Assert.NotNull(packet);
        Assert.Equal(1, packet!.IsRaceOn);
        Assert.Equal(12345u, packet.TimestampMs);
        Assert.Equal(7000f, packet.EngineMaxRpm);
        Assert.Equal(900f, packet.EngineIdleRpm);
        Assert.Equal(4500f, packet.CurrentEngineRpm);
        Assert.Equal(9.81f, packet.AccelerationZ);
        Assert.Equal(30f, packet.VelocityZ);
        Assert.Equal(125, packet.CarOrdinal);
        Assert.Equal(5, packet.CarClass);
        Assert.Equal(850, packet.CarPerformanceIndex);
        Assert.Equal(2, packet.DrivetrainType);
        Assert.Equal(8, packet.NumCylinders);
        Assert.Equal(42u, packet.CarGroup);
        Assert.Equal(150f, packet.SmashableMass);
        Assert.Equal(123.5f, packet.PositionX);
        Assert.Equal(27.78f, packet.SpeedMs);
        Assert.Equal(100.008f, packet.SpeedKmh, 2);
        Assert.Equal(250000f, packet.PowerWatts);
        Assert.Equal(450f, packet.TorqueNm);
        Assert.Equal(95f, packet.TireTempFrontLeft);
        Assert.Equal(80f, packet.TireTempFrontRight);
        Assert.Equal(112f, packet.TireTempRearLeft);
        Assert.Equal(100f, packet.TireTempRearRight);
        Assert.Equal(35f, packet.TireTempFrontLeftC);
        Assert.Equal(26.667f, packet.TireTempFrontRightC, 2);
        Assert.Equal(12.5f, packet.BoostPsi);
        Assert.Equal(0.75f, packet.Fuel);
        Assert.Equal(91.2f, packet.BestLap);
        Assert.Equal(300.5f, packet.CurrentRaceTime);
        Assert.Equal(7, packet.LapNumber);
        Assert.Equal(3, packet.RacePosition);
        Assert.Equal(255, packet.Accel);
        Assert.Equal(0, packet.Brake);
        Assert.Equal(2, packet.Gear);
        Assert.Equal(-50, packet.Steer);
        Assert.Equal(100, packet.NormalizedDrivingLine);
    }

    [Theory]
    [InlineData(140f, 60f)]
    [InlineData(158f, 70f)]
    public void FahrenheitToCelsius_ConvertsObservedFh6Values(float raw, float expectedCelsius)
    {
        Assert.Equal(expectedCelsius, Fh6Packet.FahrenheitToCelsius(raw), 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(323)]
    [InlineData(325)]
    public void Parse_WrongLength_ReturnsNull(int length)
    {
        Assert.Null(Fh6Packet.Parse(new byte[length]));
    }

    private static void WriteI32(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);

    private static void WriteU32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    private static void WriteF32(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), BitConverter.SingleToInt32Bits(value));
}

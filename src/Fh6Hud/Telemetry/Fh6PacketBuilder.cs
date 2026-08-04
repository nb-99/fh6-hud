using System.Buffers.Binary;

namespace Fh6Hud.Telemetry;

/// <summary>Builds valid FH6 "Data Out" datagrams (324 bytes, little-endian).</summary>
public sealed class Fh6PacketBuilder
{
    private readonly byte[] _data = new byte[Fh6Packet.PacketSize];

    public Fh6PacketBuilder IsRaceOn(int value)
    {
        WriteI32(0, value);
        return this;
    }

    public Fh6PacketBuilder TimestampMs(uint value)
    {
        WriteU32(4, value);
        return this;
    }

    public Fh6PacketBuilder EngineMaxRpm(float value)
    {
        WriteF32(8, value);
        return this;
    }

    public Fh6PacketBuilder CurrentEngineRpm(float value)
    {
        WriteF32(16, value);
        return this;
    }

    public Fh6PacketBuilder SpeedMs(float value)
    {
        WriteF32(256, value);
        return this;
    }

    public Fh6PacketBuilder PowerWatts(float value)
    {
        WriteF32(260, value);
        return this;
    }

    public Fh6PacketBuilder TorqueNm(float value)
    {
        WriteF32(264, value);
        return this;
    }

    public Fh6PacketBuilder TireTemp(float frontLeft, float frontRight, float rearLeft, float rearRight)
    {
        WriteF32(268, frontLeft);
        WriteF32(272, frontRight);
        WriteF32(276, rearLeft);
        WriteF32(280, rearRight);
        return this;
    }

    public Fh6PacketBuilder TireTempC(float frontLeft, float frontRight, float rearLeft, float rearRight) =>
        TireTemp(
            Fh6Packet.CelsiusToFahrenheit(frontLeft),
            Fh6Packet.CelsiusToFahrenheit(frontRight),
            Fh6Packet.CelsiusToFahrenheit(rearLeft),
            Fh6Packet.CelsiusToFahrenheit(rearRight));

    public Fh6PacketBuilder Gear(byte value)
    {
        _data[319] = value;
        return this;
    }

    public byte[] Build() => _data;

    private void WriteI32(int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(offset), value);

    private void WriteU32(int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(offset), value);

    private void WriteF32(int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(offset), BitConverter.SingleToInt32Bits(value));
}

using System.Buffers.Binary;

namespace Fh6Hud.Telemetry;

/// <summary>Parsed FH6 "Data Out" telemetry packet (324 bytes, little-endian).</summary>
public sealed class Fh6Packet
{
    public const int PacketSize = 324;

    public required int IsRaceOn { get; init; }
    public required uint TimestampMs { get; init; }
    public required float EngineMaxRpm { get; init; }
    public required float EngineIdleRpm { get; init; }
    public required float CurrentEngineRpm { get; init; }
    public required float AccelerationX { get; init; }
    public required float AccelerationY { get; init; }
    public required float AccelerationZ { get; init; }
    public required float VelocityX { get; init; }
    public required float VelocityY { get; init; }
    public required float VelocityZ { get; init; }
    public required float AngularVelocityX { get; init; }
    public required float AngularVelocityY { get; init; }
    public required float AngularVelocityZ { get; init; }
    public required float Yaw { get; init; }
    public required float Pitch { get; init; }
    public required float Roll { get; init; }
    public required float NormalizedSuspensionTravelFrontLeft { get; init; }
    public required float NormalizedSuspensionTravelFrontRight { get; init; }
    public required float NormalizedSuspensionTravelRearLeft { get; init; }
    public required float NormalizedSuspensionTravelRearRight { get; init; }
    public required float TireSlipRatioFrontLeft { get; init; }
    public required float TireSlipRatioFrontRight { get; init; }
    public required float TireSlipRatioRearLeft { get; init; }
    public required float TireSlipRatioRearRight { get; init; }
    public required float WheelRotationSpeedFrontLeft { get; init; }
    public required float WheelRotationSpeedFrontRight { get; init; }
    public required float WheelRotationSpeedRearLeft { get; init; }
    public required float WheelRotationSpeedRearRight { get; init; }
    public required int WheelOnRumbleStripFrontLeft { get; init; }
    public required int WheelOnRumbleStripFrontRight { get; init; }
    public required int WheelOnRumbleStripRearLeft { get; init; }
    public required int WheelOnRumbleStripRearRight { get; init; }
    public required int WheelInPuddleFrontLeft { get; init; }
    public required int WheelInPuddleFrontRight { get; init; }
    public required int WheelInPuddleRearLeft { get; init; }
    public required int WheelInPuddleRearRight { get; init; }
    public required float SurfaceRumbleFrontLeft { get; init; }
    public required float SurfaceRumbleFrontRight { get; init; }
    public required float SurfaceRumbleRearLeft { get; init; }
    public required float SurfaceRumbleRearRight { get; init; }
    public required float TireSlipAngleFrontLeft { get; init; }
    public required float TireSlipAngleFrontRight { get; init; }
    public required float TireSlipAngleRearLeft { get; init; }
    public required float TireSlipAngleRearRight { get; init; }
    public required float TireCombinedSlipFrontLeft { get; init; }
    public required float TireCombinedSlipFrontRight { get; init; }
    public required float TireCombinedSlipRearLeft { get; init; }
    public required float TireCombinedSlipRearRight { get; init; }
    public required float SuspensionTravelMetersFrontLeft { get; init; }
    public required float SuspensionTravelMetersFrontRight { get; init; }
    public required float SuspensionTravelMetersRearLeft { get; init; }
    public required float SuspensionTravelMetersRearRight { get; init; }
    public required int CarOrdinal { get; init; }
    public required int CarClass { get; init; }
    public required int CarPerformanceIndex { get; init; }
    public required int DrivetrainType { get; init; }
    public required int NumCylinders { get; init; }
    public required uint CarGroup { get; init; }
    public required float SmashableVelDiff { get; init; }
    public required float SmashableMass { get; init; }
    public required float PositionX { get; init; }
    public required float PositionY { get; init; }
    public required float PositionZ { get; init; }
    public required float SpeedMs { get; init; }
    public required float PowerWatts { get; init; }
    public required float TorqueNm { get; init; }
    public required float TireTempFrontLeft { get; init; }
    public required float TireTempFrontRight { get; init; }
    public required float TireTempRearLeft { get; init; }
    public required float TireTempRearRight { get; init; }
    public required float BoostPsi { get; init; }
    public required float Fuel { get; init; }
    public required float DistanceTraveled { get; init; }
    public required float BestLap { get; init; }
    public required float LastLap { get; init; }
    public required float CurrentLap { get; init; }
    public required float CurrentRaceTime { get; init; }
    public required ushort LapNumber { get; init; }
    public required byte RacePosition { get; init; }
    public required byte Accel { get; init; }
    public required byte Brake { get; init; }
    public required byte Clutch { get; init; }
    public required byte HandBrake { get; init; }
    public required byte Gear { get; init; }
    public required sbyte Steer { get; init; }
    public required sbyte NormalizedDrivingLine { get; init; }
    public required sbyte NormalizedAiBrakeDifference { get; init; }

    public float SpeedKmh => SpeedMs * 3.6f;

    // FH6's raw tire values observed in-game are Fahrenheit-like; expose the
    // converted Celsius values so the UI and thresholds use one unit.
    public float TireTempFrontLeftC => FahrenheitToCelsius(TireTempFrontLeft);
    public float TireTempFrontRightC => FahrenheitToCelsius(TireTempFrontRight);
    public float TireTempRearLeftC => FahrenheitToCelsius(TireTempRearLeft);
    public float TireTempRearRightC => FahrenheitToCelsius(TireTempRearRight);

    public static float FahrenheitToCelsius(float fahrenheit) => (fahrenheit - 32f) * (5f / 9f);

    public static float CelsiusToFahrenheit(float celsius) => celsius * (9f / 5f) + 32f;

    /// <summary>Parses a raw 324-byte FH6 datagram. Returns null if the buffer length is invalid.</summary>
    public static Fh6Packet? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != PacketSize)
        {
            return null;
        }

        var pos = 0;
        return new Fh6Packet
        {
            IsRaceOn = ReadI32(ref pos, data),
            TimestampMs = ReadU32(ref pos, data),
            EngineMaxRpm = ReadF32(ref pos, data),
            EngineIdleRpm = ReadF32(ref pos, data),
            CurrentEngineRpm = ReadF32(ref pos, data),
            AccelerationX = ReadF32(ref pos, data),
            AccelerationY = ReadF32(ref pos, data),
            AccelerationZ = ReadF32(ref pos, data),
            VelocityX = ReadF32(ref pos, data),
            VelocityY = ReadF32(ref pos, data),
            VelocityZ = ReadF32(ref pos, data),
            AngularVelocityX = ReadF32(ref pos, data),
            AngularVelocityY = ReadF32(ref pos, data),
            AngularVelocityZ = ReadF32(ref pos, data),
            Yaw = ReadF32(ref pos, data),
            Pitch = ReadF32(ref pos, data),
            Roll = ReadF32(ref pos, data),
            NormalizedSuspensionTravelFrontLeft = ReadF32(ref pos, data),
            NormalizedSuspensionTravelFrontRight = ReadF32(ref pos, data),
            NormalizedSuspensionTravelRearLeft = ReadF32(ref pos, data),
            NormalizedSuspensionTravelRearRight = ReadF32(ref pos, data),
            TireSlipRatioFrontLeft = ReadF32(ref pos, data),
            TireSlipRatioFrontRight = ReadF32(ref pos, data),
            TireSlipRatioRearLeft = ReadF32(ref pos, data),
            TireSlipRatioRearRight = ReadF32(ref pos, data),
            WheelRotationSpeedFrontLeft = ReadF32(ref pos, data),
            WheelRotationSpeedFrontRight = ReadF32(ref pos, data),
            WheelRotationSpeedRearLeft = ReadF32(ref pos, data),
            WheelRotationSpeedRearRight = ReadF32(ref pos, data),
            WheelOnRumbleStripFrontLeft = ReadI32(ref pos, data),
            WheelOnRumbleStripFrontRight = ReadI32(ref pos, data),
            WheelOnRumbleStripRearLeft = ReadI32(ref pos, data),
            WheelOnRumbleStripRearRight = ReadI32(ref pos, data),
            WheelInPuddleFrontLeft = ReadI32(ref pos, data),
            WheelInPuddleFrontRight = ReadI32(ref pos, data),
            WheelInPuddleRearLeft = ReadI32(ref pos, data),
            WheelInPuddleRearRight = ReadI32(ref pos, data),
            SurfaceRumbleFrontLeft = ReadF32(ref pos, data),
            SurfaceRumbleFrontRight = ReadF32(ref pos, data),
            SurfaceRumbleRearLeft = ReadF32(ref pos, data),
            SurfaceRumbleRearRight = ReadF32(ref pos, data),
            TireSlipAngleFrontLeft = ReadF32(ref pos, data),
            TireSlipAngleFrontRight = ReadF32(ref pos, data),
            TireSlipAngleRearLeft = ReadF32(ref pos, data),
            TireSlipAngleRearRight = ReadF32(ref pos, data),
            TireCombinedSlipFrontLeft = ReadF32(ref pos, data),
            TireCombinedSlipFrontRight = ReadF32(ref pos, data),
            TireCombinedSlipRearLeft = ReadF32(ref pos, data),
            TireCombinedSlipRearRight = ReadF32(ref pos, data),
            SuspensionTravelMetersFrontLeft = ReadF32(ref pos, data),
            SuspensionTravelMetersFrontRight = ReadF32(ref pos, data),
            SuspensionTravelMetersRearLeft = ReadF32(ref pos, data),
            SuspensionTravelMetersRearRight = ReadF32(ref pos, data),
            CarOrdinal = ReadI32(ref pos, data),
            CarClass = ReadI32(ref pos, data),
            CarPerformanceIndex = ReadI32(ref pos, data),
            DrivetrainType = ReadI32(ref pos, data),
            NumCylinders = ReadI32(ref pos, data),
            CarGroup = ReadU32(ref pos, data),
            SmashableVelDiff = ReadF32(ref pos, data),
            SmashableMass = ReadF32(ref pos, data),
            PositionX = ReadF32(ref pos, data),
            PositionY = ReadF32(ref pos, data),
            PositionZ = ReadF32(ref pos, data),
            SpeedMs = ReadF32(ref pos, data),
            PowerWatts = ReadF32(ref pos, data),
            TorqueNm = ReadF32(ref pos, data),
            TireTempFrontLeft = ReadF32(ref pos, data),
            TireTempFrontRight = ReadF32(ref pos, data),
            TireTempRearLeft = ReadF32(ref pos, data),
            TireTempRearRight = ReadF32(ref pos, data),
            BoostPsi = ReadF32(ref pos, data),
            Fuel = ReadF32(ref pos, data),
            DistanceTraveled = ReadF32(ref pos, data),
            BestLap = ReadF32(ref pos, data),
            LastLap = ReadF32(ref pos, data),
            CurrentLap = ReadF32(ref pos, data),
            CurrentRaceTime = ReadF32(ref pos, data),
            LapNumber = ReadU16(ref pos, data),
            RacePosition = data[pos++],
            Accel = data[pos++],
            Brake = data[pos++],
            Clutch = data[pos++],
            HandBrake = data[pos++],
            Gear = data[pos++],
            Steer = unchecked((sbyte)data[pos++]),
            NormalizedDrivingLine = unchecked((sbyte)data[pos++]),
            NormalizedAiBrakeDifference = unchecked((sbyte)data[pos++]),
        };
    }

    private static int ReadI32(ref int pos, ReadOnlySpan<byte> data) =>
        (int)BinaryPrimitives.ReadInt32LittleEndian(Slice4(ref pos, data));

    private static uint ReadU32(ref int pos, ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Slice4(ref pos, data));

    private static ushort ReadU16(ref int pos, ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(At2(ref pos)));

    private static float ReadF32(ref int pos, ReadOnlySpan<byte> data) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Slice4(ref pos, data)));

    private static ReadOnlySpan<byte> Slice4(ref int pos, ReadOnlySpan<byte> data) =>
        data.Slice(At4(ref pos));

    private static int At4(ref int pos)
    {
        int start = pos;
        pos += 4;
        return start;
    }

    private static int At2(ref int pos)
    {
        int start = pos;
        pos += 2;
        return start;
    }
}

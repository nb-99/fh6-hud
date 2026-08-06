using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class GearRatioTrackerTests
{
    private static Fh6Packet Packet(
        byte gear,
        float rpm,
        float speedMs,
        byte clutch = 0,
        int drivetrain = 0,
        float slipFl = 0f,
        float slipFr = 0f,
        float slipRl = 0f,
        float slipRr = 0f)
    {
        var bytes = new Fh6PacketBuilder()
            .IsRaceOn(1)
            .EngineMaxRpm(7000f)
            .CurrentEngineRpm(rpm)
            .SpeedMs(speedMs)
            .Gear(gear)
            .Clutch(clutch)
            .DrivetrainType(drivetrain)
            .TireSlipRatio(slipFl, slipFr, slipRl, slipRr)
            .Build();
        return Fh6Packet.Parse(bytes)!;
    }

    private static void Feed(GearRatioTracker tracker, byte gear, float rpm, float speedMs, int count) {
        for (int i = 0; i < count; i++)
        {
            tracker.AddSample(Packet(gear, rpm, speedMs));
        }
    }

    [Fact]
    public void LearnsRatioAsRpmPerSpeed()
    {
        var tracker = new GearRatioTracker();
        Feed(tracker, gear: 1, rpm: 6000f, speedMs: 20f, count: GearRatioTracker.MinSamplesPerGear);

        Assert.Equal(300f, tracker.GetRatio(1)!.Value, 3);
        Assert.Equal(GearRatioTracker.MinSamplesPerGear, tracker.GetSampleCount(1));
    }

    [Fact]
    public void ReturnsNull_UntilMinimumSamplesReached()
    {
        var tracker = new GearRatioTracker();
        Feed(tracker, gear: 1, rpm: 6000f, speedMs: 20f, count: GearRatioTracker.MinSamplesPerGear - 1);

        Assert.Null(tracker.GetRatio(1));
    }

    [Theory]
    [InlineData((byte)0)]  // neutral
    [InlineData((byte)20)] // reverse
    [InlineData((byte)21)] // drive (actual gear unknown)
    [InlineData((byte)22)] // park
    public void IgnoresNonForwardGears(byte gear)
    {
        var tracker = new GearRatioTracker();
        Assert.False(tracker.AddSample(Packet(gear, rpm: 6000f, speedMs: 20f)));
        Assert.Equal(0, tracker.GetSampleCount(gear));
    }

    [Fact]
    public void IgnoresClutchSlip()
    {
        var tracker = new GearRatioTracker();
        Assert.False(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, clutch: 200)));
        Assert.Equal(0, tracker.GetSampleCount(1));
    }

    [Fact]
    public void IgnoresNearStandstill()
    {
        var tracker = new GearRatioTracker();
        Assert.False(tracker.AddSample(Packet(gear: 1, rpm: 3000f, speedMs: 0.5f)));
        Assert.Equal(0, tracker.GetSampleCount(1));
    }

    [Fact]
    public void Fwd_IgnoresFrontWheelSpin_ButNotRear()
    {
        var tracker = new GearRatioTracker();
        Assert.False(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, drivetrain: 0, slipFl: 1.2f)));
        Assert.True(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, drivetrain: 0, slipRl: 1.2f)));
    }

    [Fact]
    public void Rwd_IgnoresRearWheelSpin_ButNotFront()
    {
        var tracker = new GearRatioTracker();
        Assert.False(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, drivetrain: 1, slipRr: 1.2f)));
        Assert.True(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, drivetrain: 1, slipFl: 1.2f)));
    }

    [Fact]
    public void Awd_IgnoresAnyWheelSpin()
    {
        var tracker = new GearRatioTracker();
        Assert.False(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, drivetrain: 2, slipFr: 1.2f)));
        Assert.False(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, drivetrain: 2, slipRl: 1.2f)));
        Assert.True(tracker.AddSample(Packet(gear: 1, rpm: 6000f, speedMs: 20f, drivetrain: 2)));
    }

    [Fact]
    public void SampleCount_Saturates_AndMeanKeepsAdapting()
    {
        var tracker = new GearRatioTracker();
        Feed(tracker, gear: 1, rpm: 6000f, speedMs: 20f, count: GearRatioTracker.MaxSamplesPerGear);
        Assert.Equal(GearRatioTracker.MaxSamplesPerGear, tracker.GetSampleCount(1));
        Assert.Equal(300f, tracker.GetRatio(1)!.Value, 3);

        // Ratio change (e.g. final-drive tuning): the saturated mean must move.
        // After 2x cap samples of 350: mean ~= 350 - 50*e^-2 ~= 343.
        Feed(tracker, gear: 1, rpm: 7000f, speedMs: 20f, count: GearRatioTracker.MaxSamplesPerGear * 2);
        Assert.Equal(GearRatioTracker.MaxSamplesPerGear, tracker.GetSampleCount(1));
        Assert.InRange(tracker.GetRatio(1)!.Value, 340f, 350f);
    }

    [Fact]
    public void Reset_ClearsRatios()
    {
        var tracker = new GearRatioTracker();
        Feed(tracker, gear: 1, rpm: 6000f, speedMs: 20f, count: GearRatioTracker.MinSamplesPerGear);
        Assert.NotNull(tracker.GetRatio(1));

        tracker.Reset();
        Assert.Null(tracker.GetRatio(1));
        Assert.Equal(0, tracker.GetSampleCount(1));
    }
}

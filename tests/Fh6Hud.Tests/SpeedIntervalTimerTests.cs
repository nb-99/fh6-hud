using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class SpeedIntervalTimerTests
{
    [Fact]
    public void StartsOnUpwardCrossing_NotOnFirstSample()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(0f, 0u);
        timer.Update(2f, 10u);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);

        timer.Update(3.5f, 20u);
        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);
    }

    [Fact]
    public void BrakingDownward_NeverStartsTiming()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(120f, 0u);
        timer.Update(90f, 10u);
        timer.Update(50f, 20u);
        timer.Update(5f, 30u);
        timer.Update(2f, 40u);
        timer.Update(0f, 50u);

        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
    }

    [Fact]
    public void CompletesRun_RecordsBest_AndReArmsAfterDroppingBelowStart()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(0f, 0u);
        timer.Update(5f, 0u);
        timer.Update(120f, 30u);

        Assert.Equal(SpeedIntervalTimer.State.Done, timer.CurrentState);
        Assert.Equal(TimeSpan.FromMilliseconds(30), timer.Elapsed);
        Assert.True(timer.HasBest);
        Assert.Equal(timer.Elapsed, timer.BestElapsed);

        timer.Update(80f, 31u);
        Assert.Equal(SpeedIntervalTimer.State.Done, timer.CurrentState);

        timer.Update(0f, 32u);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
    }

    [Fact]
    public void AbortsWhenSpeedDropsBelowReArmThreshold_WhileRunning()
    {
        var timer = new SpeedIntervalTimer(3f, 100f, hysteresisKmh: 2f);
        timer.Update(0f, 0u);
        timer.Update(5f, 0u);
        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);

        timer.Update(0.5f, 10u);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
    }

    [Fact]
    public void TelemetryGap_DoesNotInflateElapsed()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(0f, 0u);
        timer.Update(5f, 0u);
        timer.Update(50f, 100u);
        Assert.Equal(TimeSpan.FromMilliseconds(100), timer.Elapsed);

        // A pause/menu freezes the game clock: the resumed packet carries the
        // same timestamp, so elapsed must hold at 100 ms instead of counting
        // the wall-clock gap (the pre-fix Stopwatch behavior).
        timer.Update(80f, 100u);
        Assert.Equal(TimeSpan.FromMilliseconds(100), timer.Elapsed);
        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);
    }

    [Fact]
    public void TimestampWrap_DoesNotBreakElapsed()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(0f, uint.MaxValue - 10u);
        timer.Update(5f, uint.MaxValue - 10u);
        timer.Update(80f, 20u);

        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);
        Assert.Equal(TimeSpan.FromMilliseconds(31), timer.Elapsed);
    }

    [Fact]
    public void KeepsBestAcrossRuns()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        RunOne(timer, firstRunMs: 60, secondRunMs: 20);
        var firstBest = timer.BestElapsed;
        RunOne(timer, firstRunMs: 10, secondRunMs: 20);
        var secondBest = timer.BestElapsed;

        Assert.True(firstBest > TimeSpan.Zero);
        Assert.True(secondBest > TimeSpan.Zero);
        Assert.True(secondBest < firstBest);
        Assert.Equal(secondBest, timer.BestElapsed);
    }

    [Fact]
    public void Times100To200Interval()
    {
        var timer = new SpeedIntervalTimer(100f, 200f);
        timer.Update(90f, 0u);
        timer.Update(99f, 10u);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);

        timer.Update(100.5f, 20u);
        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);

        timer.Update(150f, 50u);
        Assert.Equal(TimeSpan.FromMilliseconds(30), timer.Elapsed);

        timer.Update(200.5f, 80u);
        Assert.Equal(SpeedIntervalTimer.State.Done, timer.CurrentState);
        Assert.Equal(TimeSpan.FromMilliseconds(60), timer.Elapsed);
    }

    [Fact]
    public void ResetAll_ClearsBest()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        RunOne(timer, firstRunMs: 30, secondRunMs: 20);
        Assert.True(timer.HasBest);

        timer.ResetAll();
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.False(timer.HasBest);
    }

    private static void RunOne(SpeedIntervalTimer timer, int firstRunMs, int secondRunMs)
    {
        uint t = 0;
        timer.Update(0f, t);
        timer.Update(5f, t);
        timer.Update(150f, t + (uint)firstRunMs);
        timer.Update(0f, t + (uint)firstRunMs + 1u);
        timer.Update(5f, t + (uint)firstRunMs + 1u);
        timer.Update(150f, t + (uint)(firstRunMs + 1 + secondRunMs));
        timer.Update(0f, t + (uint)(firstRunMs + 1 + secondRunMs) + 1u);
    }
}

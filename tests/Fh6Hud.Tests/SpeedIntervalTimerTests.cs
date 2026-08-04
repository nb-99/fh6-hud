using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class SpeedIntervalTimerTests
{
    [Fact]
    public void StartsOnUpwardCrossing_NotOnFirstSample()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(0f);
        timer.Update(2f);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);

        timer.Update(3.5f);
        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);
    }

    [Fact]
    public void BrakingDownward_NeverStartsTiming()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(120f);
        timer.Update(90f);
        timer.Update(50f);
        timer.Update(5f);
        timer.Update(2f);
        timer.Update(0f);

        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
    }

    [Fact]
    public void CompletesRun_RecordsBest_AndReArmsAfterDroppingBelowStart()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        timer.Update(0f);
        timer.Update(5f);
        Thread.Sleep(30);
        timer.Update(120f);

        Assert.Equal(SpeedIntervalTimer.State.Done, timer.CurrentState);
        Assert.True(timer.Elapsed > TimeSpan.Zero);
        Assert.True(timer.HasBest);
        Assert.Equal(timer.Elapsed, timer.BestElapsed);

        timer.Update(80f);
        Assert.Equal(SpeedIntervalTimer.State.Done, timer.CurrentState);

        timer.Update(0f);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
    }

    [Fact]
    public void AbortsWhenSpeedDropsBelowReArmThreshold_WhileRunning()
    {
        var timer = new SpeedIntervalTimer(3f, 100f, hysteresisKmh: 2f);
        timer.Update(0f);
        timer.Update(5f);
        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);

        timer.Update(0.5f);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
    }

    [Fact]
    public void KeepsBestAcrossRuns()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        RunOne(timer, slowMs: 60);
        var firstBest = timer.BestElapsed;
        RunOne(timer, slowMs: 10);
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
        timer.Update(90f);
        timer.Update(99f);
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);

        timer.Update(100.5f);
        Assert.Equal(SpeedIntervalTimer.State.Running, timer.CurrentState);

        Thread.Sleep(30);
        timer.Update(150f);
        Assert.True(timer.Elapsed > TimeSpan.Zero);

        timer.Update(200.5f);
        Assert.Equal(SpeedIntervalTimer.State.Done, timer.CurrentState);
        Assert.True(timer.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public void ResetAll_ClearsBest()
    {
        var timer = new SpeedIntervalTimer(3f, 100f);
        RunOne(timer, slowMs: 30);
        Assert.True(timer.HasBest);

        timer.ResetAll();
        Assert.Equal(SpeedIntervalTimer.State.Waiting, timer.CurrentState);
        Assert.False(timer.HasBest);
    }

    private static void RunOne(SpeedIntervalTimer timer, int slowMs)
    {
        timer.Update(0f);
        timer.Update(5f);
        Thread.Sleep(slowMs);
        timer.Update(150f);
        timer.Update(0f);
        timer.Update(5f);
        Thread.Sleep(20);
        timer.Update(150f);
        timer.Update(0f);
    }
}

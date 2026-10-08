using Fh6Hud;

namespace Fh6Hud.Tests;

public class PanelActivityTrackerTests
{
    [Fact]
    public void IsStale_BeforeAnySample_IsFalse()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.1f);

        Assert.False(tracker.IsStale(nowSeconds: 1000, staleAfterSeconds: 10));
    }

    [Fact]
    public void IsStale_BecomesTrueOnceUnchangedForThreshold()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.1f);
        tracker.Observe(new PanelActivityTracker.Sample(80f), nowSeconds: 0);

        Assert.False(tracker.IsStale(nowSeconds: 9.9, staleAfterSeconds: 10));
        Assert.True(tracker.IsStale(nowSeconds: 10, staleAfterSeconds: 10));
    }

    [Fact]
    public void Observe_ChangeBeyondTolerance_RestartsTheClock()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.1f);
        tracker.Observe(new PanelActivityTracker.Sample(80f), nowSeconds: 0);

        tracker.Observe(new PanelActivityTracker.Sample(80.5f), nowSeconds: 9);

        Assert.False(tracker.IsStale(nowSeconds: 15, staleAfterSeconds: 10));
        Assert.True(tracker.IsStale(nowSeconds: 19, staleAfterSeconds: 10));
    }

    [Fact]
    public void Observe_JitterWithinTolerance_DoesNotRestartTheClock()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.5f);
        tracker.Observe(new PanelActivityTracker.Sample(80f), nowSeconds: 0);

        tracker.Observe(new PanelActivityTracker.Sample(80.2f), nowSeconds: 5);
        tracker.Observe(new PanelActivityTracker.Sample(79.8f), nowSeconds: 9);

        Assert.True(tracker.IsStale(nowSeconds: 10, staleAfterSeconds: 10));
    }

    [Fact]
    public void Observe_SlowDrift_CountsOnceItAccumulatesPastTolerance()
    {
        // Each step is within tolerance of the previous sample, but not of
        // the last accepted reference, so the drift must register eventually.
        var tracker = new PanelActivityTracker(tolerance: 0.5f);
        tracker.Observe(new PanelActivityTracker.Sample(80f), nowSeconds: 0);

        tracker.Observe(new PanelActivityTracker.Sample(80.4f), nowSeconds: 1);
        tracker.Observe(new PanelActivityTracker.Sample(80.8f), nowSeconds: 2);

        Assert.False(tracker.IsStale(nowSeconds: 11.5, staleAfterSeconds: 10));
        Assert.True(tracker.IsStale(nowSeconds: 12, staleAfterSeconds: 10));
    }

    [Fact]
    public void Observe_AnySlotChanging_CountsAsActivity()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.1f);
        tracker.Observe(new PanelActivityTracker.Sample(70f, 71f, 72f, 73f), nowSeconds: 0);

        tracker.Observe(new PanelActivityTracker.Sample(70f, 71f, 72f, 74f), nowSeconds: 8);

        Assert.False(tracker.IsStale(nowSeconds: 15, staleAfterSeconds: 10));
    }

    [Fact]
    public void Observe_NaNToNaN_IsNotActivity()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.1f);
        tracker.Observe(new PanelActivityTracker.Sample(float.NaN), nowSeconds: 0);

        tracker.Observe(new PanelActivityTracker.Sample(float.NaN), nowSeconds: 10);

        Assert.True(tracker.IsStale(nowSeconds: 10, staleAfterSeconds: 10));
    }

    [Fact]
    public void Observe_NaNToNumber_IsActivity()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.1f);
        tracker.Observe(new PanelActivityTracker.Sample(float.NaN), nowSeconds: 0);

        tracker.Observe(new PanelActivityTracker.Sample(50f), nowSeconds: 10);

        Assert.False(tracker.IsStale(nowSeconds: 10, staleAfterSeconds: 10));
    }

    [Fact]
    public void Reset_ForgetsHistory()
    {
        var tracker = new PanelActivityTracker(tolerance: 0.1f);
        tracker.Observe(new PanelActivityTracker.Sample(80f), nowSeconds: 0);

        tracker.Reset();

        Assert.False(tracker.IsStale(nowSeconds: 100, staleAfterSeconds: 10));
    }
}

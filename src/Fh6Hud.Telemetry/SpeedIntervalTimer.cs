namespace Fh6Hud.Telemetry;

/// <summary>
/// Times a speed interval (e.g. 0-100, 100-200, 200-300 km/h). The clock starts
/// the moment speed rises through the start threshold and stops at the target;
/// downward crossings never trigger a run, so braking cannot start a timer.
/// A run is re-armed only after speed drops below the start threshold (with a
/// small hysteresis band to ignore noise), and the best (minimum) elapsed time
/// across completed runs is retained.
/// </summary>
/// <remarks>
/// Elapsed time is derived from the packet <c>TimestampMs</c> field rather than
/// a wall clock, so telemetry gaps (pauses, menus) cannot advance a running
/// timer — a run simply holds until packets resume.
/// </remarks>
public sealed class SpeedIntervalTimer
{
    public enum State
    {
        Waiting,
        Running,
        Done,
    }

    private readonly float _startKmh;
    private readonly float _reArmKmh;
    private readonly float _targetKmh;
    private float _previousSpeed = float.NaN;
    private uint _startTimestampMs;
    private TimeSpan _finalTime;
    private TimeSpan _bestTime;

    public SpeedIntervalTimer(float startKmh, float targetKmh, float hysteresisKmh = 2f)
    {
        _startKmh = startKmh;
        _reArmKmh = startKmh - hysteresisKmh;
        _targetKmh = targetKmh;
    }

    public State CurrentState { get; private set; } = State.Waiting;

    /// <summary>Live elapsed time while running; the frozen interval result when done.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Best (minimum) completed-run time; zero if no run has completed.</summary>
    public TimeSpan BestElapsed => _bestTime;

    public bool HasBest => _bestTime > TimeSpan.Zero;

    public string Label => $"{(int)_startKmh}-{(int)_targetKmh}";

    /// <summary>
    /// Advances the state machine with a speed sample from a packet.
    /// </summary>
    /// <param name="speedKmh">Current speed in km/h.</param>
    /// <param name="timestampMs">The packet's <c>TimestampMs</c> value.</param>
    public void Update(float speedKmh, uint timestampMs)
    {
        switch (CurrentState)
        {
            case State.Waiting:
                if (CrossedUpward(speedKmh, _startKmh))
                {
                    _startTimestampMs = timestampMs;
                    Elapsed = TimeSpan.Zero;
                    CurrentState = State.Running;
                }

                break;

            case State.Running:
                Elapsed = ElapsedSince(_startTimestampMs, timestampMs);
                if (speedKmh >= _targetKmh)
                {
                    _finalTime = Elapsed;
                    if (_bestTime == TimeSpan.Zero || _finalTime < _bestTime)
                    {
                        _bestTime = _finalTime;
                    }

                    CurrentState = State.Done;
                }
                else if (speedKmh < _reArmKmh)
                {
                    Reset();
                }

                break;

            case State.Done:
                if (speedKmh < _reArmKmh)
                {
                    Reset();
                }

                break;
        }

        _previousSpeed = speedKmh;
    }

    /// <summary>
    /// Elapsed time between two packet timestamps. Modular arithmetic keeps the
    /// delta correct across the U32 wrap (TimestampMs overflows to 0 eventually).
    /// </summary>
    private static TimeSpan ElapsedSince(uint startMs, uint nowMs)
    {
        uint delta = unchecked(nowMs - startMs);
        return TimeSpan.FromMilliseconds(delta);
    }

    private bool CrossedUpward(float current, float threshold)
    {
        if (float.IsNaN(_previousSpeed))
        {
            return false;
        }

        return _previousSpeed < threshold && current >= threshold;
    }

    public void Reset()
    {
        _startTimestampMs = 0;
        _finalTime = default;
        Elapsed = default;
        CurrentState = State.Waiting;
    }

    /// <summary>Clears the best time as well as the current run (used by the reset menu).</summary>
    public void ResetAll()
    {
        Reset();
        _bestTime = default;
    }
}

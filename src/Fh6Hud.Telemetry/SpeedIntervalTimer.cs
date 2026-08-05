using System.Diagnostics;

namespace Fh6Hud.Telemetry;

/// <summary>
/// Times a speed interval (e.g. 0-100, 100-200, 200-300 km/h). The clock starts
/// the moment speed rises through the start threshold and stops at the target;
/// downward crossings never trigger a run, so braking cannot start a timer.
/// A run is re-armed only after speed drops below the start threshold (with a
/// small hysteresis band to ignore noise), and the best (minimum) elapsed time
/// across completed runs is retained.
/// </summary>
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
    private readonly Stopwatch _stopwatch = new();
    private float _previousSpeed = float.NaN;
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

    public void Update(float speedKmh)
    {
        switch (CurrentState)
        {
            case State.Waiting:
                if (CrossedUpward(speedKmh, _startKmh))
                {
                    _stopwatch.Restart();
                    CurrentState = State.Running;
                }

                break;

            case State.Running:
                Elapsed = _stopwatch.Elapsed;
                if (speedKmh >= _targetKmh)
                {
                    _stopwatch.Stop();
                    _finalTime = _stopwatch.Elapsed;
                    Elapsed = _finalTime;
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
        _stopwatch.Reset();
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
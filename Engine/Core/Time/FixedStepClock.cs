using System.Diagnostics;

namespace TACTIX.Engine.Core.Time;

public sealed class FixedStepClock
{
    private readonly Stopwatch _sw = new();
    private long _lastTicks;
    private double _accumulator;

    public double FixedDeltaSeconds { get; }
    public double UnscaledDeltaSeconds { get; private set; }

    public FixedStepClock(double fixedDeltaSeconds)
    {
        if (fixedDeltaSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        FixedDeltaSeconds = fixedDeltaSeconds;
    }

    public void Start()
    {
        _sw.Start();
        _lastTicks = _sw.ElapsedTicks;
        _accumulator = 0;
        UnscaledDeltaSeconds = 0;
    }

    public int Pump()
    {
        var ticks = _sw.ElapsedTicks;
        var dt = (ticks - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = ticks;

        UnscaledDeltaSeconds = dt;
        _accumulator += dt;

        var steps = 0;
        while (_accumulator >= FixedDeltaSeconds)
        {
            _accumulator -= FixedDeltaSeconds;
            steps++;
        }

        return steps;
    }

    public double Alpha => FixedDeltaSeconds <= 0 ? 0 : Math.Clamp(_accumulator / FixedDeltaSeconds, 0, 1);
}
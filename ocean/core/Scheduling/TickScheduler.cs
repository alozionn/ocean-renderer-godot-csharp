using System;

namespace Ocean.Core.Scheduling;

/// <summary>
/// Fixed-rate ticks on staggered phases: client i first ticks at i * interval / clients, so at high frame rates
/// a frame records at most one client. When frames are slower than the rate, everything due ticks every frame.
/// </summary>
public sealed class TickScheduler
{
    readonly double[] _next, _last;
    double _interval;

    public TickScheduler(int clients, double rate, double clock = 0)
    {
        _next = new double[clients];
        _last = new double[clients];
        Reset(rate, clock);
    }

    public void Reset(double rate, double clock)
    {
        _interval = 1.0 / rate;
        for (int i = 0; i < _next.Length; i++)
        {
            _last[i] = clock;
            _next[i] = clock + i * _interval / _next.Length;
        }
    }

    /// <summary>Changes the rate, keeping each client's phase within the interval.</summary>
    public void SetRate(double rate, double clock)
    {
        double old = _interval;
        _interval = 1.0 / rate;
        if (Math.Abs(_interval - old) < 1e-12) return;
        for (int i = 0; i < _next.Length; i++) _next[i] = clock + (_next[i] - clock) * _interval / old;
    }

    /// <summary>True when the client is due (or forced); returns the time since its last tick.</summary>
    public bool TryTick(int client, double clock, bool force, out double dt)
    {
        dt = 0;
        if (!force && clock < _next[client]) return false;
        dt = clock - _last[client];
        _last[client] = clock;
        _next[client] = Math.Max(_next[client] + _interval, clock);
        return true;
    }
}

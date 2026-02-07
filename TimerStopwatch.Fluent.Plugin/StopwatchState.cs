using System;

namespace TimerStopwatch.Fluent.Plugin;

internal static class StopwatchState
{
    private static readonly object LockObj = new();
    private static bool _isRunning;
    private static TimeSpan _elapsed = TimeSpan.Zero;
    private static DateTimeOffset _startedUtc;

    public static void Start()
    {
        lock (LockObj)
        {
            if (_isRunning)
                return;
            _isRunning = true;
            _startedUtc = DateTimeOffset.UtcNow;
        }
    }

    public static void Stop()
    {
        lock (LockObj)
        {
            if (!_isRunning)
                return;
            _elapsed += DateTimeOffset.UtcNow - _startedUtc;
            _isRunning = false;
        }
    }

    public static void Reset()
    {
        lock (LockObj)
        {
            _elapsed = TimeSpan.Zero;
            if (_isRunning)
                _startedUtc = DateTimeOffset.UtcNow;
        }
    }

    public static bool IsRunning()
    {
        lock (LockObj)
        {
            return _isRunning;
        }
    }

    public static TimeSpan GetElapsed()
    {
        lock (LockObj)
        {
            if (!_isRunning)
                return _elapsed;
            return _elapsed + (DateTimeOffset.UtcNow - _startedUtc);
        }
    }
}


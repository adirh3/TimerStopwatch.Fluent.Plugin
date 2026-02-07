using System;
using Blast.Core.Results;

namespace TimerStopwatch.Fluent.Plugin;

internal enum TimerStopwatchAction
{
    StartTimer,
    CancelTimer,
    StartStopwatch,
    StopStopwatch,
    ResetStopwatch
}

internal readonly record struct TimerStopwatchCommand(TimerStopwatchAction Action, TimeSpan Duration);

internal sealed class TimerStopwatchSearchResult : CustomSearchResult
{
    public required TimerStopwatchCommand Command { get; init; }
}

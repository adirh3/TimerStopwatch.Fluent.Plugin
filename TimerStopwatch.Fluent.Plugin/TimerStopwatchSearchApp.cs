using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Blast.API.OperationSystem;
using Blast.Core;
using Blast.Core.Interfaces;
using Blast.Core.Objects;
using Blast.Core.Results;

namespace TimerStopwatch.Fluent.Plugin;

internal sealed class PluginSearchOperation : SearchOperationBase
{
    public PluginSearchOperation(string operationName, string description, string iconGlyph)
        : base(operationName, description, iconGlyph)
    {
    }
}

public sealed class TimerStopwatchSearchApp : ISearchApplication
{
    private const string AppName = "Timer / Stopwatch";
    private const string AppDescription = "Start timers and control a stopwatch from Fluent Search";
    private const string TimerTag = "timer";
    private const string StopwatchTag = "stopwatch";
    private const string TimerIcon = "\uE823";
    private const string StopwatchIcon = "\uEA38";

    private readonly SearchApplicationInfo _applicationInfo;
    private readonly SearchTag[] _defaultSearchTags;
    private readonly object _timerLock = new();

    private readonly ISearchOperation _timerOperation;
    private readonly ISearchOperation _stopwatchOperation;

    private Timer? _timer;
    private int _timerGeneration;
    private DateTimeOffset? _timerDueTimeUtc;
    private TimeSpan _timerDuration;

    public TimerStopwatchSearchApp()
    {
        _timerOperation = new PluginSearchOperation("Run", "Run timer command", "\uE768")
        {
            HideMainWindow = true
        };

        _stopwatchOperation = new PluginSearchOperation("Run", "Run stopwatch command", "\uE768")
        {
            HideMainWindow = false
        };

        _defaultSearchTags =
        [
            new SearchTag(true)
            {
                Name = "Timer",
                Value = TimerTag,
                IconGlyph = TimerIcon,
                Description = "Start or cancel countdown timers"
            },
            new SearchTag(true)
            {
                Name = "Stopwatch",
                Value = StopwatchTag,
                IconGlyph = StopwatchIcon,
                Description = "Start, stop, or reset stopwatch"
            }
        ];

        _applicationInfo = new SearchApplicationInfo(AppName, AppDescription,
            new[] { _timerOperation, _stopwatchOperation })
        {
            SearchTagOnly = true,
            MinimumSearchLength = 0,
            MinimumTagSearchLength = 0,
            IsProcessSearchEnabled = false,
            IsProcessSearchOffline = false,
            SearchAllTime = ApplicationSearchTime.Fast,
            ApplicationIconGlyph = TimerIcon,
            DefaultSearchTags = _defaultSearchTags,
            SearchTagName = TimerTag,
            SearchTagDescription = "Search in timer and stopwatch"
        };
    }

    public SearchApplicationInfo GetApplicationInfo() => _applicationInfo;

    public IAsyncEnumerable<ISearchResult> SearchAsync(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        if (searchRequest.SearchType == SearchType.SearchProcess)
            return SynchronousAsyncEnumerable.Empty;

        return new SynchronousAsyncEnumerable(GetResults(searchRequest, cancellationToken));
    }

    public ValueTask<IHandleResult> HandleSearchResult(ISearchResult searchResult)
    {
        if (searchResult is not TimerStopwatchSearchResult result)
            return ValueTask.FromResult<IHandleResult>(new HandleResult(false, false));

        return result.Command.Action switch
        {
            TimerStopwatchAction.StartTimer => StartTimer(result.Command.Duration),
            TimerStopwatchAction.CancelTimer => ValueTask.FromResult<IHandleResult>(CancelTimer()),
            TimerStopwatchAction.StartStopwatch => ValueTask.FromResult<IHandleResult>(StartStopwatch()),
            TimerStopwatchAction.StopStopwatch => ValueTask.FromResult<IHandleResult>(StopStopwatch()),
            TimerStopwatchAction.ResetStopwatch => ValueTask.FromResult<IHandleResult>(ResetStopwatch()),
            _ => ValueTask.FromResult<IHandleResult>(new HandleResult(false, false))
        };
    }

    private IEnumerable<ISearchResult> GetResults(SearchRequest searchRequest, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;

        string text = (searchRequest.DisplayedSearchText ?? string.Empty).Trim();
        bool timerTagUsed = ContainsTag(searchRequest.SearchTags, TimerTag);
        bool stopwatchTagUsed = ContainsTag(searchRequest.SearchTags, StopwatchTag);

        if (stopwatchTagUsed)
        {
            foreach (ISearchResult result in BuildStopwatchResults(text))
                yield return result;
            yield break;
        }

        if (timerTagUsed)
        {
            foreach (ISearchResult result in BuildTimerResults(text))
                yield return result;
        }
    }

    private IEnumerable<ISearchResult> BuildTimerResults(string input)
    {
        TimeSpan? remaining = GetTimerRemaining();
        string normalized = input.Trim();

        if (normalized.StartsWith("cancel", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("stop", StringComparison.OrdinalIgnoreCase))
        {
            if (remaining.HasValue)
            {
                yield return CreateTimerResult(
                    "Cancel running timer",
                    $"Remaining: {FormatDuration(remaining.Value)}",
                    new TimerStopwatchCommand(TimerStopwatchAction.CancelTimer, TimeSpan.Zero),
                    8);
            }

            yield break;
        }

        if (TryParseDuration(normalized, out TimeSpan duration))
        {
            yield return CreateTimerResult(
                $"Start timer for {FormatDuration(duration)}",
                "Press Enter to start countdown",
                new TimerStopwatchCommand(TimerStopwatchAction.StartTimer, duration),
                10);
        }

        if (remaining.HasValue)
        {
            yield return CreateTimerResult(
                $"Cancel timer ({FormatDuration(remaining.Value)} left)",
                "Stop the currently running timer",
                new TimerStopwatchCommand(TimerStopwatchAction.CancelTimer, TimeSpan.Zero),
                6);
        }
    }

    private IEnumerable<ISearchResult> BuildStopwatchResults(string input)
    {
        string normalized = input.Trim();
        TimeSpan elapsed = StopwatchState.GetElapsed();

        if (normalized.StartsWith("start", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateStopwatchResult(
                "Start stopwatch",
                $"Current elapsed: {FormatStopwatch(elapsed)}",
                new TimerStopwatchCommand(TimerStopwatchAction.StartStopwatch, TimeSpan.Zero),
                10);
            yield break;
        }

        if (normalized.StartsWith("stop", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("pause", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateStopwatchResult(
                "Stop stopwatch",
                $"Current elapsed: {FormatStopwatch(elapsed)}",
                new TimerStopwatchCommand(TimerStopwatchAction.StopStopwatch, TimeSpan.Zero),
                10);
            yield break;
        }

        if (normalized.StartsWith("reset", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("clear", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateStopwatchResult(
                "Reset stopwatch",
                $"Current elapsed: {FormatStopwatch(elapsed)}",
                new TimerStopwatchCommand(TimerStopwatchAction.ResetStopwatch, TimeSpan.Zero),
                10);
            yield break;
        }

        bool running = StopwatchState.IsRunning();
        if (running)
        {
            yield return CreateStopwatchResult(
                "Stop stopwatch",
                $"Running: {FormatStopwatch(elapsed)}",
                new TimerStopwatchCommand(TimerStopwatchAction.StopStopwatch, TimeSpan.Zero),
                8);
            yield return CreateStopwatchResult(
                "Reset stopwatch",
                $"Running: {FormatStopwatch(elapsed)}",
                new TimerStopwatchCommand(TimerStopwatchAction.ResetStopwatch, TimeSpan.Zero),
                7);
        }
        else
        {
            yield return CreateStopwatchResult(
                "Start stopwatch",
                $"Elapsed: {FormatStopwatch(elapsed)}",
                new TimerStopwatchCommand(TimerStopwatchAction.StartStopwatch, TimeSpan.Zero),
                8);

            if (elapsed > TimeSpan.Zero)
            {
                yield return CreateStopwatchResult(
                    "Reset stopwatch",
                    $"Elapsed: {FormatStopwatch(elapsed)}",
                    new TimerStopwatchCommand(TimerStopwatchAction.ResetStopwatch, TimeSpan.Zero),
                    7);
            }
        }
    }

    private TimerStopwatchSearchResult CreateTimerResult(string title, string context, TimerStopwatchCommand command,
        double score)
    {
        return CreateResult(title, context, TimerIcon, command, score, _timerOperation, enableStopwatchPreview: false);
    }

    private TimerStopwatchSearchResult CreateStopwatchResult(string title, string context, TimerStopwatchCommand command,
        double score)
    {
        return CreateResult(title, context, StopwatchIcon, command, score, _stopwatchOperation,
            enableStopwatchPreview: true);
    }

    private TimerStopwatchSearchResult CreateResult(string title, string context, string iconGlyph,
        TimerStopwatchCommand command, double score, ISearchOperation operation, bool enableStopwatchPreview)
    {
        var ops = new ObservableCollection<ISearchOperation> { operation };
        var result = new TimerStopwatchSearchResult
        {
            Command = command,
            ResultName = title,
            DisplayedName = title,
            Context = context,
            AdditionalInformation = context,
            IconGlyph = iconGlyph,
            UseIconGlyph = true,
            Score = score,
            SearchObjectId = $"{command.Action}:{command.Duration}",
            SupportedOperations = ops,
            Tags = new ObservableCollection<SearchTag>(_defaultSearchTags),
            CanPin = false,
            ShouldCacheResult = false
        };

        // Make stopwatch preview show immediately on selection.
        if (enableStopwatchPreview)
        {
            result.ForceAutomaticPreview = true;
            result.ResultPreviewControlBuilder = StopwatchPreviewControlBuilder.Instance;
        }

        return result;
    }

    private ValueTask<IHandleResult> StartTimer(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return ValueTask.FromResult<IHandleResult>(new HandleResult(false, false));

        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
            _timerGeneration++;
            _timerDuration = duration;
            _timerDueTimeUtc = DateTimeOffset.UtcNow.Add(duration);
            int generation = _timerGeneration;
            _timer = new Timer(static state =>
            {
                var tuple = (Tuple<TimerStopwatchSearchApp, int>)state!;
                tuple.Item1.OnTimerElapsed(tuple.Item2);
            }, Tuple.Create(this, generation), dueTime: duration, period: Timeout.InfiniteTimeSpan);
        }

        return ValueTask.FromResult<IHandleResult>(new HandleResult(true, false));
    }

    private void OnTimerElapsed(int generation)
    {
        TimeSpan duration;
        lock (_timerLock)
        {
            if (generation != _timerGeneration)
                return;

            duration = _timerDuration;
            _timer?.Dispose();
            _timer = null;
            _timerDueTimeUtc = null;
        }

        // Notifications can be invoked from background threads; use reflection to support both host signatures.
        TryShowNotification(new NotificationModel
        {
            Title = "Timer finished",
            Content = $"Your {FormatDuration(duration)} timer is done."
        });
    }

    private IHandleResult CancelTimer()
    {
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
            _timerGeneration++;
            _timerDueTimeUtc = null;
        }

        return new HandleResult(true, false);
    }

    private IHandleResult StartStopwatch()
    {
        StopwatchState.Start();
        return new HandleResult(true, true);
    }

    private IHandleResult StopStopwatch()
    {
        StopwatchState.Stop();
        return new HandleResult(true, true);
    }

    private IHandleResult ResetStopwatch()
    {
        StopwatchState.Reset();
        return new HandleResult(true, true);
    }

    private TimeSpan? GetTimerRemaining()
    {
        lock (_timerLock)
        {
            if (_timerDueTimeUtc == null)
                return null;
            TimeSpan remaining = _timerDueTimeUtc.Value - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    private static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string text = input.Trim().ToLowerInvariant();

        if (TryParseClockFormat(text, out duration))
            return duration > TimeSpan.Zero;

        var regex = new Regex(@"(?<value>\d+)\s*(?<unit>h|hr|hrs|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        MatchCollection matches = regex.Matches(text);
        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups["value"].Value, out int value))
                    continue;

                string unit = match.Groups["unit"].Value.ToLowerInvariant();
                duration += unit.StartsWith("h", StringComparison.Ordinal)
                    ? TimeSpan.FromHours(value)
                    : unit.StartsWith("m", StringComparison.Ordinal)
                        ? TimeSpan.FromMinutes(value)
                        : TimeSpan.FromSeconds(value);
            }

            string leftovers = regex.Replace(text, string.Empty).Trim();
            return string.IsNullOrEmpty(leftovers) && duration > TimeSpan.Zero;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes))
        {
            duration = TimeSpan.FromMinutes(minutes);
            return duration > TimeSpan.Zero;
        }

        return false;
    }

    private static bool TryParseClockFormat(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        string[] parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 3)
            return false;

        if (!int.TryParse(parts[0], out int p0) ||
            !int.TryParse(parts[1], out int p1))
            return false;

        int p2 = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out p2))
            return false;

        duration = parts.Length == 2
            ? new TimeSpan(0, p0, p1)
            : new TimeSpan(p0, p1, p2);
        return true;
    }

    private static bool ContainsTag(string[] tags, string value)
    {
        if (tags == null)
            return false;

        foreach (string tag in tags)
        {
            if (string.Equals(tag, value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return duration.ToString(@"h\:mm\:ss");
        if (duration.TotalMinutes >= 1)
            return duration.ToString(@"m\:ss");
        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))}s";
    }

    private static string FormatStopwatch(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return elapsed.ToString(@"h\:mm\:ss\.f");
        return elapsed.ToString(@"m\:ss\.f");
    }

    private static void TryShowNotification(NotificationModel model)
    {
        object manager = OsUtils.OsNotificationManager;
        if (manager == null)
            return;

        Type type = manager.GetType();
        try
        {
            // Newer Fluent Search: ShowNotification(NotificationModel, Action onActivated = null)
            MethodInfo? m2 = type.GetMethod("ShowNotification", new[] { typeof(NotificationModel), typeof(Action) });
            if (m2 != null)
            {
                m2.Invoke(manager, new object?[] { model, null });
                return;
            }

            // Older Fluent Search: ShowNotification(NotificationModel)
            MethodInfo? m1 = type.GetMethod("ShowNotification", new[] { typeof(NotificationModel) });
            if (m1 != null)
            {
                m1.Invoke(manager, new object?[] { model });
            }
        }
        catch
        {
            // Ignore notification failures; timer should not crash the host.
        }
    }
}

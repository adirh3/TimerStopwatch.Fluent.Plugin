using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Blast.Core;
using Blast.Core.Interfaces;
using Blast.Core.Objects;
using Xunit;

namespace TimerStopwatch.Fluent.Plugin.Tests;

public sealed class PluginContractTests
{
    private sealed class TestApp : Application
    {
    }

    private static void EnsureAvaloniaApp()
    {
        if (Application.Current != null)
            return;

        // Minimal initialization so Blast.Core localization helpers don't crash in headless test runs.
        AppBuilder.Configure<TestApp>()
            .UsePlatformDetect()
            .SetupWithoutStarting();
    }

    [Fact]
    public void SearchApp_implements_ISearchApplication_with_parameterless_ctor()
    {
        EnsureAvaloniaApp();
        Type appType = typeof(TimerStopwatchSearchApp);
        Assert.True(typeof(ISearchApplication).IsAssignableFrom(appType));
        Assert.NotNull(appType.GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public async Task SearchAsync_returns_a_result_for_timer_tag()
    {
        EnsureAvaloniaApp();
        var app = new TimerStopwatchSearchApp();
        SearchApplicationInfo appInfo = app.GetApplicationInfo();

        Assert.NotNull(appInfo);
        Assert.NotNull(appInfo.DefaultSearchTags);
        Assert.NotEmpty(appInfo.DefaultSearchTags);

        var request = new SearchRequest("5m", "timer", SearchType.SearchAll);

        bool yielded = false;
        await foreach (ISearchResult result in app.SearchAsync(request, CancellationToken.None))
        {
            yielded = true;
            Assert.NotNull(result);
            break;
        }

        Assert.True(yielded);
    }

    [Fact]
    public async Task SearchAsync_returns_a_result_for_stopwatch_tag()
    {
        EnsureAvaloniaApp();
        var app = new TimerStopwatchSearchApp();
        var request = new SearchRequest("start", "stopwatch", SearchType.SearchAll);

        bool yielded = false;
        await foreach (ISearchResult _ in app.SearchAsync(request, CancellationToken.None))
        {
            yielded = true;
            break;
        }

        Assert.True(yielded);
    }

    [Fact]
    public async Task SearchAsync_returns_no_results_for_unrelated_tag()
    {
        EnsureAvaloniaApp();
        var app = new TimerStopwatchSearchApp();
        var request = new SearchRequest("smoke", "not-the-plugin-tag", SearchType.SearchAll);

        bool yielded = false;
        await foreach (ISearchResult _ in app.SearchAsync(request, CancellationToken.None))
        {
            yielded = true;
            break;
        }

        Assert.False(yielded);
    }

}

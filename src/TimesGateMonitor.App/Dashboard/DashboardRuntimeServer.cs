using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.App.Dashboard;

internal sealed class DashboardRuntimeServer : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly ConcurrentDictionary<string, long> _requestCounts = new(StringComparer.Ordinal);
    private readonly long[] _screenRequestCounts = new long[DisplayFrame.ScreenCount];
    private readonly long[] _screenValueRequestCounts = new long[DisplayFrame.ScreenCount];
    private readonly long[] _lastScreenRequestUtcTicks = new long[DisplayFrame.ScreenCount];
    private IReadOnlyDictionary<int, byte[]> _backgrounds = new Dictionary<int, byte[]>();
    private HardwareSnapshot _latestSnapshot = new();

    public DashboardRuntimeServer(IPAddress listenAddress, int port)
    {
        ArgumentNullException.ThrowIfNull(listenAddress);
        BaseUri = new Uri($"http://{listenAddress}:{port}/", UriKind.Absolute);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(listenAddress, port));
        _application = builder.Build();

        _application.MapGet(
            "/background/screen-{screenIndex:int}.gif",
            (int screenIndex, HttpContext context) => GetBackground(screenIndex, context));

        foreach (var widget in DashboardWidgetCatalog.All)
        {
            MapWidget(widget.Key, DashboardWidgetDisplayMode.Value);
            if (widget.SupportsBar)
            {
                MapWidget(widget.Key, DashboardWidgetDisplayMode.Bar);
            }

            for (var screenIndex = 1; screenIndex <= DisplayFrame.ScreenCount; screenIndex++)
            {
                MapWidget(widget.Key, DashboardWidgetDisplayMode.Value, screenIndex);
                if (widget.SupportsBar)
                {
                    MapWidget(widget.Key, DashboardWidgetDisplayMode.Bar, screenIndex);
                }
            }
        }

        MapValue("/value/system/time-large", _ => DashboardWidgetValueFormatter.LargeClock());
        MapValue("/value/blank", _ => " ");
        for (var screenIndex = 1; screenIndex <= DisplayFrame.ScreenCount; screenIndex++)
        {
            MapValue($"/screen/{screenIndex}/value/blank", _ => " ", screenIndex);
        }
    }

    public Uri BaseUri { get; }

    public long BackgroundRequestCount => _requestCounts
        .Where(pair => pair.Key.StartsWith("/background/", StringComparison.Ordinal))
        .Sum(pair => pair.Value);

    public long ValueRequestCount => _requestCounts
        .Where(pair => pair.Key.StartsWith("/value/", StringComparison.Ordinal))
        .Sum(pair => pair.Value);

    public long GetBackgroundRequestCount(int screenIndex) =>
        _requestCounts.GetValueOrDefault($"/background/screen-{screenIndex}.gif");

    public long GetScreenRequestCount(int screenIndex) =>
        Interlocked.Read(ref _screenRequestCounts[ToArrayIndex(screenIndex)]);

    public long TotalScreenRequestCount => Enumerable
        .Range(1, DisplayFrame.ScreenCount)
        .Sum(GetScreenRequestCount);

    public long GetScreenValueRequestCount(int screenIndex) =>
        Interlocked.Read(ref _screenValueRequestCounts[ToArrayIndex(screenIndex)]);

    public DateTimeOffset? GetLastScreenRequestUtc(int screenIndex)
    {
        var ticks = Interlocked.Read(ref _lastScreenRequestUtcTicks[ToArrayIndex(screenIndex)]);
        return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public void MarkScreenConfigured(int screenIndex)
    {
        var index = ToArrayIndex(screenIndex);
        Interlocked.Exchange(ref _lastScreenRequestUtcTicks[index], DateTimeOffset.UtcNow.UtcTicks);
    }

    private HardwareSnapshot Snapshot => Volatile.Read(ref _latestSnapshot);

    public void SetBackgrounds(IReadOnlyDictionary<int, byte[]> backgrounds)
    {
        ArgumentNullException.ThrowIfNull(backgrounds);
        Volatile.Write(ref _backgrounds, backgrounds);
    }

    public void Update(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _latestSnapshot, snapshot);
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _application.StartAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        using var stopSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _application.StopAsync(stopSource.Token);
        await _application.DisposeAsync();
    }

    private IResult GetBackground(int screenIndex, HttpContext context)
    {
        NoCache(context);
        Count($"/background/screen-{screenIndex}.gif");
        MarkScreenRequest(screenIndex, isValueRequest: false);
        var backgrounds = Volatile.Read(ref _backgrounds);
        return backgrounds.TryGetValue(screenIndex, out var gif)
            ? Results.Bytes(gif, "image/gif")
            : Results.NotFound();
    }

    private void MapValue(
        string route,
        Func<HardwareSnapshot, string> valueFactory,
        int? screenIndex = null)
    {
        _application.MapGet(route, (HttpContext context) =>
        {
            NoCache(context);
            Count(route);
            if (screenIndex.HasValue)
            {
                MarkScreenRequest(screenIndex.Value, isValueRequest: true);
            }

            return CreateValueResponse(valueFactory(Snapshot));
        });
    }

    private static IResult CreateValueResponse(string value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { dispData = value });
        return Results.Bytes(payload, "application/json; charset=utf-8");
    }

    private void MapWidget(
        string widgetKey,
        DashboardWidgetDisplayMode displayMode,
        int? screenIndex = null)
    {
        var widget = DashboardWidgetCatalog.GetRequired(widgetKey);
        var routePrefix = screenIndex.HasValue ? $"/screen/{screenIndex.Value}/" : "/";
        MapValue(
            $"{routePrefix}{widget.Path(displayMode)}",
            snapshot => DashboardWidgetValueFormatter.Format(widgetKey, displayMode, snapshot),
            screenIndex);
    }

    private void Count(string route) =>
        _requestCounts.AddOrUpdate(route, 1, static (_, count) => count + 1);

    private void MarkScreenRequest(int screenIndex, bool isValueRequest)
    {
        var index = ToArrayIndex(screenIndex);
        Interlocked.Increment(ref _screenRequestCounts[index]);
        if (isValueRequest)
        {
            Interlocked.Increment(ref _screenValueRequestCounts[index]);
        }

        Interlocked.Exchange(ref _lastScreenRequestUtcTicks[index], DateTimeOffset.UtcNow.UtcTicks);
    }

    private static int ToArrayIndex(int screenIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(screenIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(screenIndex, DisplayFrame.ScreenCount);
        return screenIndex - 1;
    }

    private static void NoCache(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
}

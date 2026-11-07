using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.Core.Formatting;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Diagnostic.TextProofOfConcept;

internal sealed class NetworkTextDashboardServer : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly ConcurrentDictionary<string, long> _requestCounts = new(StringComparer.Ordinal);
    private HardwareSnapshot _latestSnapshot = new();

    public NetworkTextDashboardServer(
        IPAddress listenAddress,
        int port,
        IReadOnlyDictionary<int, byte[]> backgroundGifs)
    {
        ArgumentNullException.ThrowIfNull(listenAddress);
        ArgumentNullException.ThrowIfNull(backgroundGifs);

        if (backgroundGifs.Count != DisplayFrame.ScreenCount)
        {
            throw new ArgumentException("Five console backgrounds are required.", nameof(backgroundGifs));
        }

        BaseUri = new Uri($"http://{listenAddress}:{port}/", UriKind.Absolute);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(listenAddress, port));
        _application = builder.Build();

        foreach (var (screenIndex, gif) in backgroundGifs)
        {
            var route = $"/background/screen-{screenIndex}.gif";
            _application.MapGet(route, () =>
            {
                Count(route);
                return Results.Bytes(gif, "image/gif");
            });
        }

        MapValue("/value/gpu/load", snapshot => $"GPU: {NetworkTextValueFormatter.Percent(snapshot.GpuLoadPercent)}");
        MapValue("/value/gpu/temp", snapshot => $"TEMP: {Temperature(snapshot.GpuTemperatureCelsius)}C");
        MapValue("/value/gpu/tdp", snapshot => $"TDP: {Power(snapshot.GpuPowerWatts)}");

        MapValue("/value/cpu/load", snapshot => $"CPU: {NetworkTextValueFormatter.Percent(snapshot.CpuLoadPercent)}");
        MapValue("/value/cpu/temp", snapshot => $"TEMP: {Temperature(snapshot.CpuTemperatureCelsius)}C");
        MapValue("/value/cpu/tdp", snapshot => $"TDP: {Power(snapshot.CpuPowerWatts)}");

        MapValue("/value/core/cpu", snapshot => LoadAndTemperature(
            "CPU",
            snapshot.CpuLoadPercent,
            snapshot.CpuTemperatureCelsius));
        MapValue("/value/core/gpu", snapshot => LoadAndTemperature(
            "GPU",
            snapshot.GpuLoadPercent,
            snapshot.GpuTemperatureCelsius));
        MapValue("/value/core/ram", snapshot =>
            $"RAM: {Gigabytes(snapshot.RamUsedGigabytes)}/{Gigabytes(snapshot.RamTotalGigabytes)}");

        MapValue("/value/ram/load", snapshot => CompactLoad("R", snapshot.RamLoadPercent));
        MapValue("/value/ram/used", snapshot =>
            $"USE {Gigabytes(snapshot.RamUsedGigabytes)}/{Gigabytes(snapshot.RamTotalGigabytes)}G");
        MapValue("/value/ram/free", snapshot => $"FREE {Gigabytes(snapshot.RamAvailableGigabytes)}G");

        MapValue("/value/system/time", _ => $"TIME: {DateTime.Now:HH:mm:ss}");
        MapValue("/value/system/up", snapshot => $"UP {Uptime(snapshot.Uptime)}");
        MapValue("/value/system/temp", TemperaturePair);
    }

    public Uri BaseUri { get; }

    public long BackgroundRequestCount => _requestCounts
        .Where(pair => pair.Key.StartsWith("/background/", StringComparison.Ordinal))
        .Sum(pair => pair.Value);

    public long ValueRequestCount => _requestCounts
        .Where(pair => pair.Key.StartsWith("/value/", StringComparison.Ordinal))
        .Sum(pair => pair.Value);

    private HardwareSnapshot Snapshot => Volatile.Read(ref _latestSnapshot);

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

    private void MapValue(string route, Func<HardwareSnapshot, string> valueFactory)
    {
        _application.MapGet(route, (HttpContext context) =>
        {
            NoCache(context);
            Count(route);
            return Results.Json(new { DispData = valueFactory(Snapshot) });
        });
    }

    private void Count(string route) =>
        _requestCounts.AddOrUpdate(route, 1, static (_, count) => count + 1);

    private static string TemperaturePair(HardwareSnapshot snapshot) =>
        $"TEMP C{Temperature(snapshot.CpuTemperatureCelsius)} G{Temperature(snapshot.GpuTemperatureCelsius)}";

    private static string LoadAndTemperature(string label, double? load, double? temperature) =>
        $"{label}: {NetworkTextValueFormatter.Percent(load)} {Temperature(temperature)}C";

    private static string CompactLoad(string label, double? value)
    {
        const int segments = 6;
        if (!value.HasValue)
        {
            return $"{label}-- [??????]";
        }

        var clamped = Math.Clamp(value.Value, 0, 100);
        var filled = (int)Math.Round(clamped * segments / 100d, MidpointRounding.AwayFromZero);
        return $"{label}{Math.Round(clamped):00} [{new string('■', filled)}{new string('□', segments - filled)}]";
    }

    private static string Temperature(double? value) => value.HasValue
        ? Math.Round(value.Value).ToString("0", CultureInfo.InvariantCulture)
        : "--";

    private static string Power(double? watts) => watts.HasValue
        ? $"{watts.Value.ToString("0", CultureInfo.InvariantCulture)} W"
        : "-- W";

    private static string Gigabytes(double? value) => value.HasValue
        ? value.Value.ToString("0.0", CultureInfo.InvariantCulture)
        : "--";

    private static string Uptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays}D{uptime.Hours:00}H"
        : $"{uptime.Hours:00}:{uptime.Minutes:00}";

    private static void NoCache(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
}

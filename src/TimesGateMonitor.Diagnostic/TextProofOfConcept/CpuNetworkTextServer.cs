using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.Core.Formatting;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Diagnostic.TextProofOfConcept;

internal sealed class CpuNetworkTextServer : IAsyncDisposable
{
    private readonly byte[] _backgroundGif;
    private readonly WebApplication _application;
    private HardwareSnapshot _latestSnapshot = new();
    private long _backgroundRequests;
    private long _loadRequests;
    private long _barRequests;
    private long _temperatureRequests;

    public CpuNetworkTextServer(IPAddress listenAddress, int port, byte[] backgroundGif)
    {
        ArgumentNullException.ThrowIfNull(listenAddress);
        ArgumentNullException.ThrowIfNull(backgroundGif);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);

        _backgroundGif = backgroundGif;
        BaseUri = new Uri($"http://{listenAddress}:{port}/", UriKind.Absolute);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(listenAddress, port));
        _application = builder.Build();

        _application.MapGet("/background/cpu.gif", () =>
        {
            Interlocked.Increment(ref _backgroundRequests);
            return Results.Bytes(_backgroundGif, "image/gif");
        });
        _application.MapGet("/value/cpu-load", (HttpContext context) =>
        {
            NoCache(context);
            Interlocked.Increment(ref _loadRequests);
            return Results.Json(new { DispData = NetworkTextValueFormatter.Percent(Snapshot.CpuLoadPercent) });
        });
        _application.MapGet("/value/cpu-bar", (HttpContext context) =>
        {
            NoCache(context);
            Interlocked.Increment(ref _barRequests);
            return Results.Json(new { DispData = NetworkTextValueFormatter.SquareBar(Snapshot.CpuLoadPercent) });
        });
        _application.MapGet("/value/cpu-temp", (HttpContext context) =>
        {
            NoCache(context);
            Interlocked.Increment(ref _temperatureRequests);
            return Results.Json(new { DispData = NetworkTextValueFormatter.Temperature(Snapshot.CpuTemperatureCelsius) });
        });
    }

    public Uri BaseUri { get; }

    public (long Background, long Load, long Bar, long Temperature) RequestCounts =>
        (Interlocked.Read(ref _backgroundRequests),
            Interlocked.Read(ref _loadRequests),
            Interlocked.Read(ref _barRequests),
            Interlocked.Read(ref _temperatureRequests));

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

    private static void NoCache(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
}

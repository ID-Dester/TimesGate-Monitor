using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.App.Dashboard;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Tests.App;

internal static class DashboardRecoveryTests
{
    private static readonly int[] AllScreens = [1, 2, 3, 4, 5];

    public static async Task FirstPartialApplyCanRecoverAsync()
    {
        using var handler = new DeviceHandler { FailScreen = 5 };
        await using var controller = CreateController(handler);
        var screens = DashboardModuleCatalog.CreateDefaults();
        var port = GetPort();

        await ExpectFailureAsync(() => ApplyAsync(controller, screens, port), "Screen 5 failed");
        TestAssert.SequenceEqual(AllScreens, handler.Screens,
            "The regression must reach LCD5 after four successful commands.");

        handler.FailScreen = null;
        handler.Screens.Clear();
        await controller.ReconnectNowAsync();
        TestAssert.SequenceEqual(AllScreens, handler.Screens,
            "A first partial Apply must retain the complete recovery target, including LCD5.");
    }

    public static async Task FailedReapplyKeepsNewTargetAsync()
    {
        using var handler = new DeviceHandler();
        await using var controller = CreateController(handler);
        var screens = DashboardModuleCatalog.CreateDefaults();
        var port = GetPort();
        await ApplyAsync(controller, screens, port);

        screens[4].Slots[0] = new DashboardWidgetAssignment("cpu_summary");
        handler.FailScreen = 1;
        await ExpectFailureAsync(() => ApplyAsync(controller, screens, port), "Screen 1 failed");
        // Recovery must own a clone rather than observe later unsaved UI edits.
        screens[4].Slots[0] = new DashboardWidgetAssignment("gpu_summary");
        handler.FailScreen = null;
        await controller.ReconnectNowAsync();
        TestAssert.True(handler.LastScreenFiveValues.Contains("/screen/5/value/core/cpu"),
            "Recovery must use the failed Apply's requested layout rather than the old session or later edits.");
    }

    public static async Task FailedHealthCheckDoesNotStartRecoveryAsync()
    {
        using var handler = new DeviceHandler { FailHealth = true };
        await using var controller = CreateController(handler);
        await ExpectFailureAsync(
            () => ApplyAsync(controller, DashboardModuleCatalog.CreateDefaults(), GetPort()),
            "Connection check failed");
        await ExpectFailureAsync(() => controller.ReconnectNowAsync(), "Apply a complete dashboard");
        TestAssert.Equal(0, handler.Screens.Count, "A failed health check must not send configuration commands.");
    }

    public static async Task UnconfirmedScreensRemainMonitoredAsync()
    {
        using var log = new IncidentLogger();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(log));
        using var handler = new DeviceHandler { FailScreen = 1 };
        await using var controller = new DashboardController(loggerFactory,
            hardwareMonitor: new StubMonitor(), messageHandler: handler);
        controller.SetAutoReconnect(false);
        await ExpectFailureAsync(
            () => ApplyAsync(controller, DashboardModuleCatalog.CreateDefaults(), GetPort()), "Screen 1 failed");

        // No LCD has been acknowledged. The real monitor must still observe all five
        // desired screens and report their silence even with automatic writes disabled.
        await log.IncidentDetected.Task.WaitAsync(ScreenHeartbeatPolicy.CheckInterval + TimeSpan.FromSeconds(10));
        TestAssert.Equal(1, handler.Screens.Count,
            "Disabling automatic reconnect must prevent configuration writes while monitoring continues.");
    }

    public static async Task CancelledFirstApplyDoesNotStartRecoveryAsync()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new DeviceHandler { CancelApply = cancellation };
        await using var controller = CreateController(handler);
        var cancelled = false;
        try
        {
            await ApplyAsync(controller, DashboardModuleCatalog.CreateDefaults(), GetPort(), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        TestAssert.True(cancelled, "The regression must exercise caller cancellation during Apply.");
        await ExpectFailureAsync(() => controller.ReconnectNowAsync(), "Apply a complete dashboard");
    }

    private static DashboardController CreateController(DeviceHandler handler)
    {
        var controller = new DashboardController(hardwareMonitor: new StubMonitor(), messageHandler: handler);
        controller.SetAutoReconnect(false);
        return controller;
    }

    private static Task ApplyAsync(
        DashboardController controller,
        DashboardScreenConfiguration[] screens,
        int port,
        CancellationToken cancellationToken = default) =>
        controller.ApplyAsync(screens, "192.0.2.1", 0, 50, IPAddress.Loopback, port, null,
            new Progress<string>(), cancellationToken, forceApply: true);

    private static int GetPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task ExpectFailureAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(message, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected failure containing '{message}'.");
    }

    private sealed class StubMonitor : IHardwareMonitor
    {
        public bool IsInitialized { get; private set; }
        public void Initialize() => IsInitialized = true;
        public HardwareSnapshot CaptureSnapshot() => new();
        public void Dispose() { }
    }

    private sealed class IncidentLogger : ILoggerProvider, ILogger
    {
        public TaskCompletionSource IncidentDetected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Dispose() { }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 3114)
            {
                IncidentDetected.TrySetResult();
            }
        }
    }

    private sealed class DeviceHandler : HttpMessageHandler
    {
        private readonly HttpClient _runtimeClient = new(new HttpClientHandler { UseProxy = false });
        public int? FailScreen { get; set; }
        public bool FailHealth { get; init; }
        public CancellationTokenSource? CancelApply { get; init; }
        public List<int> Screens { get; } = [];
        public string[] LastScreenFiveValues { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var command = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = command.RootElement;
            if (FailHealth)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            if (root.TryGetProperty("LcdIndex", out var lcd))
            {
                var screen = lcd.GetInt32() + 1;
                Screens.Add(screen);
                if (CancelApply is not null)
                {
                    CancelApply.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (screen == FailScreen)
                {
                    throw new TaskCanceledException("Simulated device timeout.");
                }

                var background = root.GetProperty("BackgroudGif").GetString()!;
                using var backgroundResponse = await _runtimeClient.GetAsync(background, cancellationToken);
                backgroundResponse.EnsureSuccessStatusCode();
                var values = root.GetProperty("ItemList").EnumerateArray()
                    .Where(item => item.GetProperty("type").GetInt32() == 23)
                    .Select(item => new Uri(item.GetProperty("TextString").GetString()!)).ToArray();
                foreach (var value in values)
                {
                    using var response = await _runtimeClient.GetAsync(value, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    TestAssert.Equal((long)body.Length, response.Content.Headers.ContentLength!.Value,
                        "Network Text responses must have an exact UTF-8 Content-Length.");
                    TestAssert.False(response.Headers.TransferEncodingChunked == true,
                        "Network Text responses must not use chunked encoding.");
                    using var json = JsonDocument.Parse(body);
                    TestAssert.Equal(JsonValueKind.String, json.RootElement.GetProperty("dispData").ValueKind,
                        "Network Text responses must contain a string dispData value.");
                }

                if (screen == 5)
                {
                    LastScreenFiveValues = values.Select(value => value.AbsolutePath).ToArray();
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"error_code\":0}") };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _runtimeClient.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

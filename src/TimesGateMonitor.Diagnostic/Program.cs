using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Core.Monitoring;
using TimesGateMonitor.Hardware.LibreHardwareMonitor;
using TimesGateMonitor.Rendering.Renderers;
using TimesGateMonitor.Rendering.Renderers.Hardware;
using TimesGateMonitor.TimesGate.Services;
using TimesGateMonitor.Diagnostic.TextProofOfConcept;

namespace TimesGateMonitor.Diagnostic;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        DiagnosticArguments options;

        try
        {
            options = DiagnosticArguments.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintHelp();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            builder.AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss ";
            });
        });
        services.AddSingleton<IDiagnosticFrameRenderer, DiagnosticFrameRenderer>();
        services.AddSingleton<IHardwareScreenRenderer, CpuScreenRenderer>();
        services.AddSingleton<IHardwareScreenRenderer, GpuScreenRenderer>();
        services.AddSingleton<IHardwareScreenRenderer, CenterScreenRenderer>();
        services.AddSingleton<IHardwareScreenRenderer, RamScreenRenderer>();
        services.AddSingleton<IHardwareScreenRenderer, SystemScreenRenderer>();
        services.AddSingleton<IHardwareDashboardRenderer, HardwareDashboardRenderer>();
        services.AddSingleton<LibreHardwareMonitorService>();
        services.AddSingleton<IHardwareMonitor>(provider =>
            provider.GetRequiredService<LibreHardwareMonitorService>());
        services.AddHttpClient<ITimesGateService, TimesGateService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        await using var provider = services.BuildServiceProvider();

        if (options.SensorDump)
        {
            var hardwareMonitor = provider.GetRequiredService<LibreHardwareMonitorService>();
            hardwareMonitor.Initialize();
            _ = hardwareMonitor.CaptureSensorSamples();
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            PrintSensorDump(hardwareMonitor.CaptureSensorSamples());
            return 0;
        }

        if (options.HardwareOnce)
        {
            var hardwareMonitor = provider.GetRequiredService<IHardwareMonitor>();
            hardwareMonitor.Initialize();
            _ = hardwareMonitor.CaptureSnapshot();
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            var snapshot = hardwareMonitor.CaptureSnapshot();
            PrintHardwareSnapshot(snapshot);

            if (!string.IsNullOrWhiteSpace(options.PreviewDirectory))
            {
                var dashboard = provider.GetRequiredService<IHardwareDashboardRenderer>();
                var hardwareFrames = dashboard.Render(snapshot, new DashboardRenderOptions());
                await SaveFramesAsync(hardwareFrames, options.PreviewDirectory, "hardware");
            }

            return 0;
        }

        if (options.TextDashboard)
        {
            return await RunTextDashboardAsync(provider, options);
        }

        if (options.TextProofOfConcept)
        {
            return await RunTextProofOfConceptAsync(provider, options);
        }

        var renderer = provider.GetRequiredService<IDiagnosticFrameRenderer>();
        var frames = options.Monitor
            ? Array.Empty<DisplayFrame>()
            : renderer.Render(new DiagnosticFrameOptions());

        if (!options.Monitor && !string.IsNullOrWhiteSpace(options.PreviewDirectory))
        {
            await SaveFramesAsync(frames, options.PreviewDirectory, "diagnostic");
        }

        if (options.RenderOnly)
        {
            Console.WriteLine("Render-only diagnostic completed successfully.");
            return 0;
        }

        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        var connection = new TimesGateConnection(options.Host!, options.LocalToken);
        var timesGate = provider.GetRequiredService<ITimesGateService>();

        Console.WriteLine($"Checking Times Gate at {connection.Endpoint} ...");
        var health = await timesGate.HealthCheckAsync(connection, cancellationSource.Token);

        if (!health.IsSuccess)
        {
            Console.Error.WriteLine($"Health check failed: {health.ErrorMessage}");
            return 1;
        }

        var initialization = await timesGate.InitializeFrameSequenceAsync(
            connection,
            cancellationSource.Token);
        if (!initialization.IsSuccess)
        {
            Console.Error.WriteLine($"Frame initialization failed: {initialization.ErrorMessage}");
            return 1;
        }

        var brightness = await timesGate.SetBrightnessAsync(
            connection,
            options.Brightness,
            cancellationSource.Token);

        if (!brightness.IsSuccess)
        {
            Console.Error.WriteLine($"Brightness command failed: {brightness.ErrorMessage}");
            return 1;
        }

        if (options.Monitor)
        {
            return await RunMonitoringAsync(
                provider.GetRequiredService<IHardwareMonitor>(),
                provider.GetRequiredService<IHardwareDashboardRenderer>(),
                timesGate,
                connection,
                options,
                cancellationSource.Token);
        }

        foreach (var frame in frames)
        {
            var result = await timesGate.SendFrameAsync(
                connection,
                frame,
                TimeSpan.FromSeconds(1),
                cancellationSource.Token);

            if (!result.IsSuccess)
            {
                Console.Error.WriteLine($"Screen {frame.ScreenIndex} failed: {result.ErrorMessage}");
                return 1;
            }

            Console.WriteLine($"Screen {frame.ScreenIndex}: sent.");
            await Task.Delay(TimeSpan.FromMilliseconds(125), cancellationSource.Token);
        }

        Console.WriteLine("Diagnostic milestone completed: all five LCD commands succeeded.");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            TimesGate Monitor diagnostic milestone

            Run live hardware monitoring:
              TimesGateMonitor --monitor --ip 192.0.2.181 --token REPLACE_WITH_LOCAL_TOKEN [--update-rate 1000]

            Test loading-free network text on CPU screen 1:
              TimesGateMonitor --text-poc --ip 192.0.2.181 --token REPLACE_WITH_LOCAL_TOKEN --serve-ip 192.0.2.159

            Run the five-screen console network-text dashboard:
              TimesGateMonitor --text-dashboard --ip 192.0.2.181 --token REPLACE_WITH_LOCAL_TOKEN --serve-ip 192.0.2.159

            Send to a device:
              TimesGateMonitor.Diagnostic --ip 192.0.2.181 --token REPLACE_WITH_LOCAL_TOKEN [--brightness 70]

            Render JPEG files without a device:
              TimesGateMonitor.Diagnostic --render-only --preview-dir diagnostic-frames

            Options:
              --ip, --host       Times Gate IP/host, optionally including scheme and port
              --token            LocalToken shown in the Divoom app device settings
              --brightness       0..100 (default: 70)
              --preview-dir      Also save the exact five JPEG frames being sent
              --render-only      Do not contact a Times Gate
              --hardware-once    Print one real HardwareSnapshot without using Times Gate
              --sensor-dump      Print normalized raw CPU/GPU/RAM sensors
              --monitor          Continuously render and send real hardware values
              --update-rate      Display update period: 500, 1000, 2000, or 5000 ms
              --cycles           Stop after N display updates; 0 means until Ctrl+C
              --text-poc         Configure CPU screen 1 to pull live text from this PC
              --text-dashboard   Configure all five screens for console network text
              --serve-ip         LAN IPv4 address of this PC, reachable by Times Gate
              --serve-port       Local HTTP port for network text (default: 8765)
              --duration         Stop text proof of concept after N seconds; 0 means Ctrl+C
              --help, -h         Show help
            """);
    }

    private static async Task<int> RunTextDashboardAsync(
        IServiceProvider provider,
        DiagnosticArguments options)
    {
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        if (options.DurationSeconds > 0)
        {
            cancellationSource.CancelAfter(TimeSpan.FromSeconds(options.DurationSeconds));
        }

        var cancellationToken = cancellationSource.Token;
        var hardwareMonitor = provider.GetRequiredService<IHardwareMonitor>();
        var backgrounds = ConsoleDashboardBackgroundRenderer.RenderAll();

        if (!string.IsNullOrWhiteSpace(options.PreviewDirectory))
        {
            var previewDirectory = Path.GetFullPath(options.PreviewDirectory);
            Directory.CreateDirectory(previewDirectory);
            foreach (var (screenIndex, gif) in backgrounds)
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(previewDirectory, $"screen-{screenIndex}-console.gif"),
                    gif,
                    cancellationToken);
            }

            Console.WriteLine($"Saved five console backgrounds to: {previewDirectory}");
        }

        await using var textServer = new NetworkTextDashboardServer(
            options.ServeAddress!,
            options.ServePort,
            backgrounds);

        try
        {
            hardwareMonitor.Initialize();
            textServer.Update(hardwareMonitor.CaptureSnapshot());
            await textServer.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not start the local text server: {exception.Message}");
            return 1;
        }

        var connection = new TimesGateConnection(options.Host!, options.LocalToken);
        var timesGate = provider.GetRequiredService<ITimesGateService>();
        Console.WriteLine($"Network-text server: {textServer.BaseUri}");
        Console.WriteLine($"Checking Times Gate at {connection.Endpoint} ...");

        var health = await timesGate.HealthCheckAsync(connection, cancellationToken);
        if (!health.IsSuccess)
        {
            Console.Error.WriteLine($"Health check failed: {health.ErrorMessage}");
            return 1;
        }

        var brightness = await timesGate.SetBrightnessAsync(
            connection,
            options.Brightness,
            cancellationToken);
        if (!brightness.IsSuccess)
        {
            Console.Error.WriteLine($"Brightness command failed: {brightness.ErrorMessage}");
            return 1;
        }

        var layouts = NetworkTextDashboardLayout.Create(textServer.BaseUri);
        foreach (var layout in layouts)
        {
            var result = await timesGate.ConfigureNetworkTextScreenAsync(
                connection,
                layout.ScreenIndex,
                layout.BackgroundUrl,
                layout.Items,
                cancellationToken);

            if (!result.IsSuccess)
            {
                Console.Error.WriteLine(
                    $"Screen {layout.ScreenIndex} configuration failed: {result.ErrorMessage}");
                return 1;
            }

            Console.WriteLine($"Screen {layout.ScreenIndex}: console network text configured.");

            if (layout.ScreenIndex < DisplayFrame.ScreenCount)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            }
        }

        Console.WriteLine("Five-screen dashboard is live. Press Ctrl+C to stop.");
        var smoother = new HardwareSnapshotSmoother();
        var statusClock = Stopwatch.StartNew();
        var warnedAboutFirewall = false;

        try
        {
            using var pollingTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await pollingTimer.WaitForNextTickAsync(cancellationToken))
            {
                textServer.Update(smoother.Apply(hardwareMonitor.CaptureSnapshot()));

                if (statusClock.Elapsed < TimeSpan.FromSeconds(5))
                {
                    continue;
                }

                statusClock.Restart();
                Console.WriteLine(
                    $"Times Gate requests: backgrounds={textServer.BackgroundRequestCount}, " +
                    $"values={textServer.ValueRequestCount}");

                if (!warnedAboutFirewall &&
                    textServer.BackgroundRequestCount + textServer.ValueRequestCount == 0)
                {
                    warnedAboutFirewall = true;
                    Console.WriteLine(
                        "No device requests received. Allow this executable/port through Windows Firewall for the private network.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Duration and Ctrl+C both use the same graceful shutdown path.
        }

        Console.WriteLine(
            $"Dashboard stopped. Requests: backgrounds={textServer.BackgroundRequestCount}, " +
            $"values={textServer.ValueRequestCount}.");
        return 0;
    }

    private static async Task<int> RunTextProofOfConceptAsync(
        IServiceProvider provider,
        DiagnosticArguments options)
    {
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        if (options.DurationSeconds > 0)
        {
            cancellationSource.CancelAfter(TimeSpan.FromSeconds(options.DurationSeconds));
        }

        var cancellationToken = cancellationSource.Token;
        var hardwareMonitor = provider.GetRequiredService<IHardwareMonitor>();
        var backgroundGif = CpuConsoleBackgroundRenderer.Render();

        if (!string.IsNullOrWhiteSpace(options.PreviewDirectory))
        {
            var previewDirectory = Path.GetFullPath(options.PreviewDirectory);
            Directory.CreateDirectory(previewDirectory);
            var previewPath = Path.Combine(previewDirectory, "cpu-console-background.gif");
            await File.WriteAllBytesAsync(previewPath, backgroundGif, cancellationToken);
            Console.WriteLine($"Saved console background to: {previewPath}");
        }

        await using var textServer = new CpuNetworkTextServer(
            options.ServeAddress!,
            options.ServePort,
            backgroundGif);

        try
        {
            hardwareMonitor.Initialize();
            textServer.Update(hardwareMonitor.CaptureSnapshot());
            await textServer.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not start the local text server: {exception.Message}");
            return 1;
        }

        var connection = new TimesGateConnection(options.Host!, options.LocalToken);
        var timesGate = provider.GetRequiredService<ITimesGateService>();
        Console.WriteLine($"Network-text server: {textServer.BaseUri}");
        Console.WriteLine($"Checking Times Gate at {connection.Endpoint} ...");

        var health = await timesGate.HealthCheckAsync(connection, cancellationToken);
        if (!health.IsSuccess)
        {
            Console.Error.WriteLine($"Health check failed: {health.ErrorMessage}");
            return 1;
        }

        var brightness = await timesGate.SetBrightnessAsync(
            connection,
            options.Brightness,
            cancellationToken);
        if (!brightness.IsSuccess)
        {
            Console.Error.WriteLine($"Brightness command failed: {brightness.ErrorMessage}");
            return 1;
        }

        var items = new NetworkTextItem[]
        {
            new(1, 69, 27, 4, 52, 16, new Uri(textServer.BaseUri, "value/cpu-load")),
            new(2, 7, 52, 4, 116, 16, new Uri(textServer.BaseUri, "value/cpu-bar")),
            new(3, 69, 77, 4, 52, 16, new Uri(textServer.BaseUri, "value/cpu-temp"))
        };
        var configuration = await timesGate.ConfigureNetworkTextScreenAsync(
            connection,
            screenIndex: 1,
            new Uri(textServer.BaseUri, "background/cpu.gif"),
            items,
            cancellationToken);

        if (!configuration.IsSuccess)
        {
            Console.Error.WriteLine($"Network-text configuration failed: {configuration.ErrorMessage}");
            return 1;
        }

        Console.WriteLine("CPU screen 1 configured. Waiting for Times Gate GET requests...");
        Console.WriteLine("Press Ctrl+C to stop; keep this process running because it serves live values.");

        var smoother = new HardwareSnapshotSmoother();
        var statusClock = Stopwatch.StartNew();
        var warnedAboutFirewall = false;

        try
        {
            using var pollingTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await pollingTimer.WaitForNextTickAsync(cancellationToken))
            {
                textServer.Update(smoother.Apply(hardwareMonitor.CaptureSnapshot()));

                if (statusClock.Elapsed < TimeSpan.FromSeconds(5))
                {
                    continue;
                }

                statusClock.Restart();
                var counts = textServer.RequestCounts;
                Console.WriteLine(
                    $"Times Gate requests: background={counts.Background}, " +
                    $"load={counts.Load}, bar={counts.Bar}, temp={counts.Temperature}");

                if (!warnedAboutFirewall && counts.Background + counts.Load + counts.Bar + counts.Temperature == 0)
                {
                    warnedAboutFirewall = true;
                    Console.WriteLine(
                        "No device requests received. Allow this executable/port through Windows Firewall for the private network.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Duration and Ctrl+C both use the same graceful shutdown path.
        }

        var finalCounts = textServer.RequestCounts;
        Console.WriteLine(
            $"Text proof of concept stopped. Requests: background={finalCounts.Background}, " +
            $"load={finalCounts.Load}, bar={finalCounts.Bar}, temp={finalCounts.Temperature}.");
        return 0;
    }

    private static async Task<int> RunMonitoringAsync(
        IHardwareMonitor hardwareMonitor,
        IHardwareDashboardRenderer dashboard,
        ITimesGateService timesGate,
        TimesGateConnection connection,
        DiagnosticArguments options,
        CancellationToken cancellationToken)
    {
        hardwareMonitor.Initialize();
        var smoother = new HardwareSnapshotSmoother();
        var renderOptions = new DashboardRenderOptions();
        var displayDuration = TimeSpan.FromMilliseconds(options.UpdateRateMilliseconds);
        var updateClock = Stopwatch.StartNew();
        var updateCount = 0;
        var nextUpdateAt = TimeSpan.Zero;

        Console.WriteLine(
            $"Live monitoring started ({options.UpdateRateMilliseconds} ms). Press Ctrl+C to stop.");

        try
        {
            using var pollingTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

            while (!cancellationToken.IsCancellationRequested)
            {
                var snapshot = smoother.Apply(hardwareMonitor.CaptureSnapshot());

                if (updateClock.Elapsed >= nextUpdateAt)
                {
                    var frames = dashboard.Render(snapshot, renderOptions);

                    if (!string.IsNullOrWhiteSpace(options.PreviewDirectory))
                    {
                        await SaveFramesAsync(frames, options.PreviewDirectory, "hardware", cancellationToken);
                    }

                    foreach (var frame in frames)
                    {
                        var result = await timesGate.SendFrameAsync(
                            connection,
                            frame,
                            displayDuration,
                            cancellationToken);

                        if (!result.IsSuccess)
                        {
                            Console.Error.WriteLine(
                                $"Screen {frame.ScreenIndex} failed: {result.ErrorMessage}");
                            return 1;
                        }
                    }

                    updateCount++;
                    PrintMonitorLine(snapshot, updateCount);
                    nextUpdateAt = updateClock.Elapsed + displayDuration;

                    if (options.Cycles > 0 && updateCount >= options.Cycles)
                    {
                        Console.WriteLine($"Monitoring completed after {updateCount} update(s).");
                        return 0;
                    }
                }

                if (!await pollingTimer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C is a normal, graceful shutdown path.
        }

        Console.WriteLine("Monitoring stopped.");
        return 0;
    }

    private static async Task SaveFramesAsync(
        IEnumerable<DisplayFrame> frames,
        string previewDirectory,
        string description,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(previewDirectory);
        Directory.CreateDirectory(directory);

        foreach (var frame in frames)
        {
            var path = Path.Combine(directory, $"screen-{frame.ScreenIndex}.jpg");
            await File.WriteAllBytesAsync(path, frame.JpegData.ToArray(), cancellationToken);
        }

        Console.WriteLine($"Saved five {description} frames to: {directory}");
    }

    private static void PrintMonitorLine(HardwareSnapshot snapshot, int updateCount)
    {
        Console.WriteLine(
            $"#{updateCount} " +
            $"CPU {Format(snapshot.CpuLoadPercent, "%")}/{Format(snapshot.CpuTemperatureCelsius, "°C")}  " +
            $"GPU {Format(snapshot.GpuLoadPercent, "%")}/{Format(snapshot.GpuTemperatureCelsius, "°C")}  " +
            $"RAM {Format(snapshot.RamLoadPercent, "%")}");
    }

    private static void PrintHardwareSnapshot(HardwareSnapshot snapshot)
    {
        Console.WriteLine("HardwareSnapshot:");
        Console.WriteLine($"  CPU load:  {Format(snapshot.CpuLoadPercent, "%")}");
        Console.WriteLine($"  CPU temp:  {Format(snapshot.CpuTemperatureCelsius, " °C")}");
        Console.WriteLine($"  CPU clock: {Format(snapshot.CpuClockMegahertz, " MHz")}");
        Console.WriteLine($"  CPU power: {Format(snapshot.CpuPowerWatts, " W")}");
        Console.WriteLine($"  GPU:       {snapshot.GpuName ?? "--"}");
        Console.WriteLine($"  GPU load:  {Format(snapshot.GpuLoadPercent, "%")}");
        Console.WriteLine($"  GPU temp:  {Format(snapshot.GpuTemperatureCelsius, " °C")}");
        Console.WriteLine($"  GPU clock: {Format(snapshot.GpuClockMegahertz, " MHz")}");
        Console.WriteLine($"  GPU power: {Format(snapshot.GpuPowerWatts, " W")}");
        Console.WriteLine($"  VRAM:      {Format(snapshot.VramUsedGigabytes, " GB")} / {Format(snapshot.VramTotalGigabytes, " GB")}");
        Console.WriteLine($"  RAM load:  {Format(snapshot.RamLoadPercent, "%")}");
        Console.WriteLine($"  RAM:       {Format(snapshot.RamUsedGigabytes, " GB")} / {Format(snapshot.RamTotalGigabytes, " GB")}");
        Console.WriteLine($"  Available: {Format(snapshot.RamAvailableGigabytes, " GB")}");
        Console.WriteLine($"  Uptime:    {snapshot.Uptime.ToString(@"dd\.hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    private static string Format(double? value, string suffix) =>
        value.HasValue
            ? $"{value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}{suffix}"
            : "--";

    private static void PrintSensorDump(IEnumerable<HardwareSensorSample> samples)
    {
        foreach (var device in samples
            .Where(sample => sample.DeviceKind != HardwareDeviceKind.Unknown)
            .GroupBy(sample => new { sample.DeviceId, sample.DeviceName, sample.DeviceKind }))
        {
            Console.WriteLine($"{device.Key.DeviceKind}: {device.Key.DeviceName} [{device.Key.DeviceId}]");

            foreach (var sensor in device.OrderBy(sample => sample.MetricKind).ThenBy(sample => sample.SensorName))
            {
                Console.WriteLine($"  {sensor.MetricKind,-20} {sensor.SensorName,-40} {sensor.Value,10:0.###}");
            }
        }
    }
}

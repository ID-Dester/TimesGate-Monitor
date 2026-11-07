using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.App.Dashboard;
using TimesGateMonitor.App.Infrastructure;

namespace TimesGateMonitor.App;

public partial class App : System.Windows.Application, IDisposable
{
    private SingleInstanceLease? _instanceLease;
    private ILoggerFactory? _loggerFactory;
    private TrayIconService? _trayIcon;
    private ILogger<App>? _logger;
    private bool _disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var screenshotMode = e.Args.Length == 2 && e.Args[0].EndsWith("screenshot", StringComparison.Ordinal);
        if (!screenshotMode && !SingleInstanceLease.TryAcquire(out _instanceLease))
        {
            Shutdown();
            return;
        }

        RollingFileLoggerProvider? fileLoggerProvider = null;
        try
        {
            fileLoggerProvider = new RollingFileLoggerProvider(AppDataPaths.Logs);
        }
        catch (UnauthorizedAccessException)
        {
            // Monitoring remains usable when the profile directory is read-only.
        }
        catch (IOException)
        {
            // Monitoring remains usable when the log directory is temporarily unavailable.
        }

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            if (fileLoggerProvider is not null)
            {
                builder.AddProvider(fileLoggerProvider);
            }
        });
        _logger = _loggerFactory.CreateLogger<App>();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        var preferences = ApplicationPreferencesStore.Load();
        if (!screenshotMode)
        {
            var sensorTaskInstalled = SensorHostManager.IsInstalled();
            var currentSensorSetup =
                preferences.CpuSensorSetupVersion >= SensorHostManager.CurrentSetupVersion;
            if (sensorTaskInstalled && currentSensorSetup && preferences.CpuSensorHostEnabled)
            {
                preferences.CpuSensorPromptCompleted = true;
                ApplicationPreferencesStore.Save(preferences);
                _ = SensorHostManager.TryStart();
            }
            else if (preferences.CpuSensorHostEnabled || !currentSensorSetup || sensorTaskInstalled)
            {
                // Never trust or automatically run a task created by an older setup version.
                // Offer an explicit repair choice and never trigger UAC automatically.
                preferences.CpuSensorPromptCompleted = false;
                preferences.CpuSensorHostEnabled = false;
                ApplicationPreferencesStore.Save(preferences);
            }
        }
        var controller = new DashboardController(
            _loggerFactory,
            () => preferences.CpuSensorHostEnabled && SensorHostManager.TryStart());
        controller.SetAutoReconnect(preferences.AutoReconnect);
        var window = new MainWindow(controller, preferences) { LivePreviewEnabled = !screenshotMode };
        MainWindow = window;
        window.Show();

        if (!screenshotMode)
        {
            InitializeTray(window, controller);
            if (preferences.StartMinimized || e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                window.HideToTray();
            }
        }

        if (screenshotMode)
        {
            var screenshotPath = Path.GetFullPath(e.Args[1]);
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                () =>
                {
                    switch (e.Args[0])
                    {
                        case "--widget-picker-screenshot":
                            window.SaveWidgetPickerScreenshot(screenshotPath);
                            break;
                        case "--widget-settings-screenshot":
                            window.SaveWidgetSettingsScreenshot(screenshotPath);
                            break;
                        case "--clock-settings-screenshot":
                            window.SaveClockSettingsScreenshot(screenshotPath);
                            break;
                        case "--app-settings-screenshot":
                            window.SaveApplicationSettingsScreenshot(screenshotPath);
                            break;
                        case "--sensor-access-screenshot":
                            window.SaveCpuSensorAccessScreenshot(screenshotPath);
                            break;
                        case "--token-error-screenshot":
                            window.SaveTokenErrorScreenshot(screenshotPath);
                            break;
                        case "--media-import-screenshot":
                            window.SaveMediaImportScreenshot(screenshotPath);
                            break;
                        case "--public-screenshot":
                            window.SavePublicScreenshot(screenshotPath);
                            break;
                        default:
                            window.SaveScreenshot(screenshotPath);
                            break;
                    }

                    window.Close();
                });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        if (_logger is { } logger)
        {
            LogStopped(logger);
        }
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon?.Dispose();
        _loggerFactory?.Dispose();
        _instanceLease?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void InitializeTray(MainWindow window, DashboardController controller)
    {
        _trayIcon = new TrayIconService();
        _trayIcon.OpenRequested += () => Dispatcher.Invoke(window.ShowFromTray);
        _trayIcon.PauseChanged += paused => Dispatcher.Invoke(() => controller.SetPaused(paused));
        _trayIcon.ReconnectRequested += () => Dispatcher.InvokeAsync(async () => await window.ReconnectAsync());
        _trayIcon.OpenLogsRequested += () => OpenLogsDirectory();
        _trayIcon.ExitRequested += () => Dispatcher.Invoke(window.RequestApplicationExit);
        controller.ConnectionStateChanged += (state, _) =>
            _trayIcon?.SetStatus(state.ToString());
        if (_logger is { } logger)
        {
            LogStarted(logger, AppDataPaths.Logs);
        }
    }

    private static void OpenLogsDirectory()
    {
        try
        {
            Directory.CreateDirectory(AppDataPaths.Logs);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppDataPaths.Logs,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The settings window reports the same path if the user opens it manually.
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is { } logger)
        {
            LogUnhandledUiException(logger, e.Exception);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is { } logger)
        {
            LogUnobservedTaskException(logger, e.Exception);
        }
        e.SetObserved();
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "TGm started without elevation. Logs: {LogsPath}")]
    private static partial void LogStarted(ILogger logger, string logsPath);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "TGm stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Critical, Message = "Unhandled UI exception.")]
    private static partial void LogUnhandledUiException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error, Message = "Unobserved background task exception.")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}

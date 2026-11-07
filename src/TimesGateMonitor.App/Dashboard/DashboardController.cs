using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TimesGateMonitor.App.Infrastructure;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Core.Monitoring;
using TimesGateMonitor.Hardware.LibreHardwareMonitor;
using TimesGateMonitor.Hardware.SensorHost;
using TimesGateMonitor.Rendering.Renderers.Hardware;
using TimesGateMonitor.TimesGate.Services;
using MediaColor = System.Windows.Media.Color;

namespace TimesGateMonitor.App.Dashboard;

internal enum DashboardConnectionState
{
    Idle,
    Applying,
    Live,
    Reconnecting,
    Paused,
    Error
}

internal sealed partial class DashboardController : IAsyncDisposable
{
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly HttpClient _httpClient;
    private readonly IHardwareMonitor _hardwareMonitor;
    private readonly TimesGateService _timesGate;
    private readonly ILogger<DashboardController> _logger;
    private readonly DashboardScreenConfiguration?[] _appliedScreens =
        new DashboardScreenConfiguration?[DisplayFrame.ScreenCount];
    private readonly DateTimeOffset?[] _lastAutomaticScreenRestoreUtc =
        new DateTimeOffset?[DisplayFrame.ScreenCount];
    private DateTimeOffset? _lastAutomaticDashboardRestoreUtc;
    private DashboardRuntimeServer? _server;
    private Task? _pollingTask;
    private Task? _reconnectTask;
    private IPAddress? _listenAddress;
    private int _listenPort;
    private int _backgroundGeneration;
    private int? _appliedBrightness;
    private string? _appliedDeviceHost;
    private HardwareSnapshot _latestSnapshot = new();
    private Task? _hardwareStartTask;
    private AppliedSession? _lastSession;
    private int _paused;
    private bool _autoReconnectEnabled = true;
    private DateTimeOffset? _listenAddressMissingSinceUtc;
    private DateTimeOffset? _lastHeartbeatJournalUtc;
    private DateTimeOffset? _dashboardIncidentDetectedUtc;
    private DateTimeOffset? _dashboardIncidentEstimatedStartUtc;
    private int _minimumSensorUpdateSeconds = 5;
    private bool _disposed;

    public DashboardController(
        ILoggerFactory? loggerFactory = null,
        Func<bool>? sensorHostStarter = null,
        IHardwareMonitor? hardwareMonitor = null,
        HttpMessageHandler? messageHandler = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<DashboardController>();
        _httpClient = messageHandler is null ? new HttpClient() : new HttpClient(messageHandler);
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _hardwareMonitor = hardwareMonitor ?? new SensorHostAwareHardwareMonitor(
            () => new LibreHardwareMonitorService(
                loggerFactory.CreateLogger<LibreHardwareMonitorService>()),
            new SensorHostClient(),
            loggerFactory.CreateLogger<SensorHostAwareHardwareMonitor>(),
            sensorHostStarter);
        _timesGate = new TimesGateService(
            _httpClient,
            loggerFactory.CreateLogger<TimesGateService>());
    }

    public bool IsLive => _server is not null;

    public HardwareSnapshot LatestSnapshot => Volatile.Read(ref _latestSnapshot);

    public event Action<HardwareSnapshot>? SnapshotUpdated;

    public event Action<DashboardConnectionState, string>? ConnectionStateChanged;

    public event Action<string>? ProgressChanged;

    public event Action<IPAddress>? ListenAddressChanged;

    public Task StartPreviewAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _hardwareStartTask ??= InitializeHardwarePollingAsync(_lifetimeSource.Token);
    }

    public async Task ApplyAsync(
        IReadOnlyList<DashboardScreenConfiguration> screens,
        string deviceHost,
        int localToken,
        int brightness,
        IPAddress listenAddress,
        int listenPort,
        int? onlyScreenIndex,
        IProgress<string> progress,
        CancellationToken cancellationToken,
        bool forceApply = false,
        bool connectionVerified = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(listenAddress);
        ArgumentNullException.ThrowIfNull(progress);

        if (screens.Count != DisplayFrame.ScreenCount)
        {
            throw new ArgumentException("Exactly five screen configurations are required.", nameof(screens));
        }

        if (onlyScreenIndex is < 0 or >= DisplayFrame.ScreenCount)
        {
            throw new ArgumentOutOfRangeException(nameof(onlyScreenIndex));
        }

        await _applyLock.WaitAsync(cancellationToken);
        var screenApplyStarted = false;
        try
        {
            var applyTarget = onlyScreenIndex.HasValue
                ? $"LCD {onlyScreenIndex.Value + 1}"
                : "all LCDs";
            LogApplyStarted(
                _logger,
                applyTarget,
                deviceHost,
                listenAddress,
                listenPort,
                forceApply);
            SetState(DashboardConnectionState.Applying, "Applying configuration");
            Report(progress, "STARTING LOCAL SENSOR SERVER…");
            await EnsureRuntimeAsync(listenAddress, listenPort, cancellationToken);
            var server = _server ?? throw new InvalidOperationException("Dashboard server did not start.");
            var minimumSensorUpdateSeconds = Volatile.Read(ref _minimumSensorUpdateSeconds);

            var backgrounds = screens
                .Select((screen, index) => new
                {
                    ScreenIndex = index + 1,
                    Gif = RenderBackground(screen, minimumSensorUpdateSeconds)
                })
                .ToDictionary(item => item.ScreenIndex, item => item.Gif);
            server.SetBackgrounds(backgrounds);

            var deviceChanged = !string.Equals(_appliedDeviceHost, deviceHost, StringComparison.OrdinalIgnoreCase);
            if (deviceChanged)
            {
                Array.Clear(_appliedScreens);
                Array.Clear(_lastAutomaticScreenRestoreUtc);
                _appliedBrightness = null;
                _appliedDeviceHost = deviceHost;
                _lastHeartbeatJournalUtc = null;
                ClearDashboardIncident();
            }
            var changedScreens = Enumerable.Range(0, screens.Count)
                .Where(index => !onlyScreenIndex.HasValue || index == onlyScreenIndex.Value)
                .Where(index => ScreenApplyPolicy.ShouldApply(
                    forceApply,
                    ScreenEquals(_appliedScreens[index], screens[index])))
                .ToArray();
            var brightnessChanged = _appliedBrightness != brightness;

            if (changedScreens.Length == 0 && !brightnessChanged)
            {
                if (!onlyScreenIndex.HasValue)
                {
                    RememberSession(screens, deviceHost, localToken, brightness, listenAddress, listenPort);
                }

                Report(progress, "LIVE · NO DEVICE CHANGES");
                SetState(DashboardConnectionState.Live, "Connected");
                LogApplyCompleted(_logger, applyTarget, deviceHost, 0, FormatScreenActivity(server));
                return;
            }

            var connection = new TimesGateConnection(deviceHost, localToken);
            if (!connectionVerified)
            {
                Report(progress, "CHECKING TIMES GATE…");
                var health = await _timesGate.HealthCheckAsync(connection, cancellationToken);
                EnsureSuccess(health, "Connection check");
            }

            if (brightnessChanged)
            {
                var brightnessResult = await _timesGate.SetBrightnessAsync(
                    connection,
                    brightness,
                    cancellationToken);
                EnsureSuccess(brightnessResult, "Brightness");
                _appliedBrightness = brightness;
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            }

            var generation = Interlocked.Increment(ref _backgroundGeneration);
            screenApplyStarted = true;

            for (var changedIndex = 0; changedIndex < changedScreens.Length; changedIndex++)
            {
                var index = changedScreens[changedIndex];
                var screenIndex = index + 1;
                var screen = screens[index];
                Report(progress,
                    $"APPLYING CHANGED SCREEN {changedIndex + 1}/{changedScreens.Length} · LCD {screenIndex} · {screen.Title}");

                var items = CreateItems(
                    screen,
                    screenIndex,
                    server.BaseUri,
                    minimumSensorUpdateSeconds);
                var backgroundUrl = new Uri(
                    server.BaseUri,
                    $"background/screen-{screenIndex}.gif?v={generation}");
                var backgroundRequestsBefore = server.GetBackgroundRequestCount(screenIndex);
                var valueRequestsBefore = server.GetScreenValueRequestCount(screenIndex);
                var result = await _timesGate.ConfigureNetworkTextScreenAsync(
                    connection,
                    screenIndex,
                    backgroundUrl,
                    items,
                    cancellationToken);
                EnsureSuccess(result, $"Screen {screenIndex}");
                server.MarkScreenConfigured(screenIndex);

                await WaitForScreenReadyAsync(
                    server,
                    screenIndex,
                    backgroundRequestsBefore,
                    valueRequestsBefore,
                    items.Any(item => item.RequiresNetworkValue),
                    cancellationToken);
                _appliedScreens[index] = screen.Clone();

                if (changedIndex < changedScreens.Length - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }

            if (!onlyScreenIndex.HasValue)
            {
                RememberSession(screens, deviceHost, localToken, brightness, listenAddress, listenPort);
            }

            Report(progress, "LIVE · CONFIGURATION APPLIED");
            SetState(DashboardConnectionState.Live, "Connected");
            LogApplyCompleted(
                _logger,
                applyTarget,
                deviceHost,
                changedScreens.Length,
                FormatScreenActivity(server));
            if (!onlyScreenIndex.HasValue &&
                !connectionVerified &&
                _dashboardIncidentDetectedUtc is { } detectedUtc)
            {
                var recoveredUtc = DateTimeOffset.UtcNow;
                LogDashboardIncidentRecovered(
                    _logger,
                    deviceHost,
                    detectedUtc,
                    recoveredUtc,
                    "dashboard reapplied manually",
                    FormatScreenActivity(server));
                ClearDashboardIncident();
            }
        }
        catch (OperationCanceledException)
        {
            SetState(DashboardConnectionState.Idle, "Operation cancelled");
            throw;
        }
        catch (Exception exception)
        {
            if (screenApplyStarted && !onlyScreenIndex.HasValue)
            {
                // A timeout can occur after the device accepted a command. Keep the
                // complete requested session so even never-confirmed LCDs can recover.
                RememberSession(screens, deviceHost, localToken, brightness, listenAddress, listenPort);
            }

            LogApplyFailed(_logger, deviceHost, exception);
            SetState(DashboardConnectionState.Error, "Connection error");
            throw;
        }
        finally
        {
            _applyLock.Release();
        }
    }

    public void SetAutoReconnect(bool enabled)
    {
        _autoReconnectEnabled = enabled;
        LogAutoReconnectChanged(_logger, enabled ? "enabled" : "disabled");
    }

    public void SetSensorRefreshSeconds(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, 30);
        Volatile.Write(ref _minimumSensorUpdateSeconds, seconds);
        LogSensorRefreshChanged(_logger, seconds);
    }

    public void SetPaused(bool paused)
    {
        Interlocked.Exchange(ref _paused, paused ? 1 : 0);
        SetState(
            paused ? DashboardConnectionState.Paused : (_lastSession is null ? DashboardConnectionState.Idle : DashboardConnectionState.Live),
            paused ? "Monitoring paused" : "Monitoring resumed");
    }

    public async Task ReconnectNowAsync(CancellationToken cancellationToken = default)
    {
        var session = Volatile.Read(ref _lastSession)
            ?? throw new InvalidOperationException("Apply a complete dashboard before reconnecting.");
        await RestoreSessionAsync(session, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeSource.Cancel();

        await AwaitShutdownTaskAsync(_pollingTask);
        await AwaitShutdownTaskAsync(_reconnectTask);

        await _applyLock.WaitAsync();
        try
        {
            if (_server is not null)
            {
                await _server.DisposeAsync();
            }

            _hardwareMonitor.Dispose();
            _timesGate.Dispose();
            _httpClient.Dispose();
            _lifetimeSource.Dispose();
        }
        finally
        {
            _applyLock.Release();
            _applyLock.Dispose();
        }
    }

    private async Task EnsureRuntimeAsync(
        IPAddress listenAddress,
        int listenPort,
        CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            if (!Equals(_listenAddress, listenAddress) || _listenPort != listenPort)
            {
                await _server.DisposeAsync();
                _server = null;
                _listenAddress = null;
                _listenPort = 0;
                Array.Clear(_appliedScreens);
            }
            else
            {
                return;
            }
        }

        await StartPreviewAsync();
        cancellationToken.ThrowIfCancellationRequested();
        _listenAddress = listenAddress;
        _listenPort = listenPort;

        var server = new DashboardRuntimeServer(listenAddress, listenPort);
        server.Update(LatestSnapshot);
        await server.StartAsync(cancellationToken);
        _server = server;
    }

    private async Task InitializeHardwarePollingAsync(CancellationToken cancellationToken)
    {
        _hardwareMonitor.Initialize();
        PublishSnapshot(_hardwareMonitor.CaptureSnapshot());
        await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
        PublishSnapshot(_hardwareMonitor.CaptureSnapshot());
        _pollingTask = Task.Run(() => PollHardwareAsync(cancellationToken), cancellationToken);
    }

    private async Task PollHardwareAsync(CancellationToken cancellationToken)
    {
        var smoother = new HardwareSnapshotSmoother();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        DateTimeOffset? lastFailureLogUtc = null;

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (Volatile.Read(ref _paused) != 0)
            {
                continue;
            }

            try
            {
                PublishSnapshot(smoother.Apply(_hardwareMonitor.CaptureSnapshot()));
                lastFailureLogUtc = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                if (lastFailureLogUtc is null || nowUtc - lastFailureLogUtc >= TimeSpan.FromMinutes(1))
                {
                    LogHardwarePollingFailed(_logger, exception);
                    lastFailureLogUtc = nowUtc;
                }
            }
        }
    }

    private async Task MonitorConnectionAsync(CancellationToken cancellationToken)
    {
        var backoff = new ReconnectBackoff();
        var nextDelay = ScreenHeartbeatPolicy.CheckInterval;
        var wasUnavailable = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(nextDelay, cancellationToken);

            var session = Volatile.Read(ref _lastSession);
            if (session is null || Volatile.Read(ref _paused) != 0)
            {
                nextDelay = ScreenHeartbeatPolicy.CheckInterval;
                continue;
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var server = Volatile.Read(ref _server);
            var monitoredScreenIndices = Enumerable.Range(0, session.Screens.Length).ToArray();
            var silentScreenIndices = server is null
                ? monitoredScreenIndices
                : monitoredScreenIndices
                    .Where(index => ScreenHeartbeatPolicy.IsSilent(
                        server.GetLastScreenRequestUtc(index + 1),
                        nowUtc))
                    .ToArray();
            var allScreensSilent = monitoredScreenIndices.Length == DisplayFrame.ScreenCount &&
                                   (server is null || ScreenHeartbeatPolicy.IsFullDashboardSilent(
                                       monitoredScreenIndices
                                           .Select(index => server.GetLastScreenRequestUtc(index + 1))
                                           .ToArray(),
                                       nowUtc));

            if (server is not null &&
                (_lastHeartbeatJournalUtc is null ||
                 nowUtc - _lastHeartbeatJournalUtc.Value >= ScreenHeartbeatPolicy.JournalInterval))
            {
                LogHeartbeatJournal(
                    _logger,
                    session.DeviceHost,
                    _autoReconnectEnabled,
                    FormatScreenActivity(server));
                _lastHeartbeatJournalUtc = nowUtc;
            }

            if (allScreensSilent && _dashboardIncidentDetectedUtc is null)
            {
                _dashboardIncidentDetectedUtc = nowUtc;
                _dashboardIncidentEstimatedStartUtc = server is null
                    ? null
                    : ScreenHeartbeatPolicy.EstimateSilenceStartedUtc(
                        monitoredScreenIndices.Select(index => server.GetLastScreenRequestUtc(index + 1)));
                LogDashboardIncidentDetected(
                    _logger,
                    session.DeviceHost,
                    nowUtc,
                    _dashboardIncidentEstimatedStartUtc,
                    _autoReconnectEnabled,
                    server is null ? "runtime server unavailable" : FormatScreenActivity(server));
            }
            else if (!allScreensSilent &&
                     silentScreenIndices.Length == 0 &&
                     _dashboardIncidentDetectedUtc is { } detectedUtc)
            {
                LogDashboardIncidentRecovered(
                    _logger,
                    session.DeviceHost,
                    detectedUtc,
                    nowUtc,
                    "device requests resumed",
                    server is null ? "runtime server unavailable" : FormatScreenActivity(server));
                ClearDashboardIncident();
            }

            if (!_autoReconnectEnabled)
            {
                nextDelay = ScreenHeartbeatPolicy.CheckInterval;
                continue;
            }

            if (!LocalIpv4AddressDetector.IsAddressAssigned(session.ListenAddress))
            {
                _listenAddressMissingSinceUtc ??= nowUtc;
                if (nowUtc - _listenAddressMissingSinceUtc.Value < ScreenHeartbeatPolicy.AddressChangeGracePeriod)
                {
                    SetState(DashboardConnectionState.Reconnecting, "PC LAN address changed · waiting");
                    nextDelay = ScreenHeartbeatPolicy.CheckInterval;
                    continue;
                }

                if (!IPAddress.TryParse(
                        LocalIpv4AddressDetector.TryGetPreferredAddress(session.DeviceHost),
                        out var replacementAddress) ||
                    replacementAddress.Equals(session.ListenAddress))
                {
                    SetState(DashboardConnectionState.Reconnecting, "PC LAN unavailable · retrying");
                    nextDelay = backoff.NextDelay();
                    continue;
                }

                try
                {
                    var updatedSession = session with { ListenAddress = replacementAddress };
                    await RestoreSessionAsync(updatedSession, cancellationToken);
                    _listenAddressMissingSinceUtc = null;
                    wasUnavailable = false;
                    backoff.Reset();
                    nextDelay = ScreenHeartbeatPolicy.CheckInterval;
                    ListenAddressChanged?.Invoke(replacementAddress);
                    LogListenAddressChanged(_logger, session.ListenAddress, replacementAddress);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    wasUnavailable = true;
                    LogAutomaticRestoreFailed(_logger, exception);
                    SetState(DashboardConnectionState.Reconnecting, "LAN address restore failed · retrying");
                    nextDelay = backoff.NextDelay();
                }

                continue;
            }

            _listenAddressMissingSinceUtc = null;

            if (server is null)
            {
                nextDelay = ScreenHeartbeatPolicy.CheckInterval;
                continue;
            }

            var restoreFullDashboard = allScreensSilent &&
                                       ScreenHeartbeatPolicy.CanRestore(
                                           _lastAutomaticDashboardRestoreUtc,
                                           nowUtc);
            var staleScreenIndex = silentScreenIndices
                .FirstOrDefault(
                    index => ScreenHeartbeatPolicy.CanRestore(
                        _lastAutomaticScreenRestoreUtc[index],
                        nowUtc),
                    -1);
            if (!restoreFullDashboard && staleScreenIndex < 0)
            {
                backoff.Reset();
                nextDelay = ScreenHeartbeatPolicy.CheckInterval;
                if (wasUnavailable)
                {
                    wasUnavailable = false;
                    SetState(DashboardConnectionState.Live, "Connected");
                }
                continue;
            }

            if (!wasUnavailable &&
                ScreenHeartbeatPolicy.ShouldInspectSingleSilentScreen(
                    restoreFullDashboard,
                    staleScreenIndex))
            {
                LogScreenSilent(
                    _logger,
                    staleScreenIndex + 1,
                    server.GetLastScreenRequestUtc(staleScreenIndex + 1));
            }

            if (!await _applyLock.WaitAsync(0, cancellationToken))
            {
                nextDelay = ScreenHeartbeatPolicy.CheckInterval;
                continue;
            }

            TimesGateCommandResult health;
            try
            {
                health = await _timesGate.HealthCheckAsync(
                    new TimesGateConnection(session.DeviceHost, session.LocalToken),
                    cancellationToken);
            }
            finally
            {
                _applyLock.Release();
            }

            if (!health.IsSuccess)
            {
                wasUnavailable = true;
                SetState(DashboardConnectionState.Reconnecting, "Times Gate unavailable · retrying");
                LogHealthCheckFailed(_logger, session.DeviceHost, health.ErrorMessage);
                nextDelay = backoff.NextDelay();
                continue;
            }

            try
            {
                if (restoreFullDashboard)
                {
                    await RestoreSessionAsync(
                        session,
                        cancellationToken,
                        connectionVerified: true);
                    var restoredAtUtc = DateTimeOffset.UtcNow;
                    _lastAutomaticDashboardRestoreUtc = restoredAtUtc;
                    foreach (var screenIndex in monitoredScreenIndices)
                    {
                        _lastAutomaticScreenRestoreUtc[screenIndex] = restoredAtUtc;
                    }

                    if (_dashboardIncidentDetectedUtc is { } detectedUtc)
                    {
                        LogDashboardIncidentRecovered(
                            _logger,
                            session.DeviceHost,
                            detectedUtc,
                            restoredAtUtc,
                            "dashboard restored automatically",
                            FormatScreenActivity(Volatile.Read(ref _server)));
                        ClearDashboardIncident();
                    }
                }
                else
                {
                    await RestoreScreenAsync(session, staleScreenIndex, cancellationToken);
                    _lastAutomaticScreenRestoreUtc[staleScreenIndex] = DateTimeOffset.UtcNow;
                }

                wasUnavailable = false;
                backoff.Reset();
                nextDelay = ScreenHeartbeatPolicy.CheckInterval;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogAutomaticRestoreFailed(_logger, exception);
                SetState(DashboardConnectionState.Reconnecting, "LCD restore failed · retrying");
                nextDelay = backoff.NextDelay();
            }
        }
    }

    private async Task RestoreScreenAsync(
        AppliedSession session,
        int screenIndex,
        CancellationToken cancellationToken)
    {
        SetState(DashboardConnectionState.Reconnecting, $"Restoring LCD {screenIndex + 1}");
        var progress = new Progress<string>(message => ProgressChanged?.Invoke(message));
        await ApplyAsync(
            session.Screens.Select(screen => screen.Clone()).ToArray(),
            session.DeviceHost,
            session.LocalToken,
            session.Brightness,
            session.ListenAddress,
            session.ListenPort,
            onlyScreenIndex: screenIndex,
            progress,
            cancellationToken,
            forceApply: true,
            connectionVerified: true);
        LogScreenRestored(_logger, screenIndex + 1, session.DeviceHost);
    }

    private async Task RestoreSessionAsync(
        AppliedSession session,
        CancellationToken cancellationToken,
        bool connectionVerified = false)
    {
        SetState(DashboardConnectionState.Reconnecting, "Restoring dashboard");
        var progress = new Progress<string>(message => ProgressChanged?.Invoke(message));
        await ApplyAsync(
            session.Screens.Select(screen => screen.Clone()).ToArray(),
            session.DeviceHost,
            session.LocalToken,
            session.Brightness,
            session.ListenAddress,
            session.ListenPort,
            onlyScreenIndex: null,
            progress,
            cancellationToken,
            forceApply: true,
            connectionVerified: connectionVerified);
        LogDashboardRestored(_logger, session.DeviceHost);
    }

    private void RememberSession(
        IReadOnlyList<DashboardScreenConfiguration> screens,
        string deviceHost,
        int localToken,
        int brightness,
        IPAddress listenAddress,
        int listenPort)
    {
        Volatile.Write(ref _lastSession, new AppliedSession(
            screens.Select(screen => screen.Clone()).ToArray(),
            deviceHost,
            localToken,
            brightness,
            listenAddress,
            listenPort));
        _reconnectTask ??= Task.Run(
            () => MonitorConnectionWithWatchdogAsync(_lifetimeSource.Token),
            _lifetimeSource.Token);
    }

    private async Task MonitorConnectionWithWatchdogAsync(CancellationToken cancellationToken)
    {
        var restartAttempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (restartAttempt > 0)
                {
                    LogConnectionMonitorRestarted(_logger, restartAttempt);
                }

                await MonitorConnectionAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                restartAttempt++;
                LogConnectionMonitorFailed(_logger, restartAttempt, exception);
                await Task.Delay(ScreenHeartbeatPolicy.CheckInterval, cancellationToken);
            }
        }
    }

    private void Report(IProgress<string> progress, string message)
    {
        progress.Report(message);
        ProgressChanged?.Invoke(message);
    }

    private void SetState(DashboardConnectionState state, string message)
    {
        ConnectionStateChanged?.Invoke(state, message);
        LogDashboardState(_logger, state, message);
    }

    private static string FormatScreenActivity(DashboardRuntimeServer? server)
    {
        if (server is null)
        {
            return "runtime server unavailable";
        }

        return string.Join(
            "; ",
            Enumerable.Range(1, DisplayFrame.ScreenCount).Select(screenIndex =>
            {
                var lastRequestUtc = server.GetLastScreenRequestUtc(screenIndex);
                var lastRequest = lastRequestUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "never";
                return $"LCD{screenIndex}: requests={server.GetScreenRequestCount(screenIndex)}, " +
                       $"values={server.GetScreenValueRequestCount(screenIndex)}, last={lastRequest}";
            }));
    }

    private void ClearDashboardIncident()
    {
        _dashboardIncidentDetectedUtc = null;
        _dashboardIncidentEstimatedStartUtc = null;
    }

    private static async Task AwaitShutdownTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Application shutdown uses the normal cancellation path.
        }
    }

    private void PublishSnapshot(HardwareSnapshot snapshot)
    {
        Volatile.Write(ref _latestSnapshot, snapshot);
        Volatile.Read(ref _server)?.Update(snapshot);
        SnapshotUpdated?.Invoke(snapshot);
    }

    internal static List<NetworkTextItem> CreateItems(
        DashboardScreenConfiguration screen,
        int screenIndex,
        Uri baseUri,
        int minimumSensorUpdateSeconds = 5)
    {
        var firstTextId = ((screenIndex - 1) * 3) + 1;
        if (string.Equals(screen.WidgetKey, "clock_large", StringComparison.Ordinal))
        {
            var showSeconds = screen.ClockShowSeconds;
            return
            [
                new NetworkTextItem(
                    firstTextId,
                    X: showSeconds ? 32 : 44,
                    Y: 50,
                    Font: 42,
                    TextWidth: showSeconds ? 64 : 40,
                    TextHeight: 16,
                    ValueUrl: ScreenValueUri(baseUri, screenIndex, "value/blank"),
                    Color: screen.TextColorHex,
                    UpdateSeconds: showSeconds ? 1 : 60,
                    Alignment: 1,
                    ItemType: showSeconds ? 6 : 5,
                    TextString: string.Empty),
                CreateHeartbeatItem(firstTextId + 1, screenIndex, baseUri, screen.TextColorHex)
            ];
        }

        if (screen.SolidColorHex is not null || screen.CustomMediaPath is not null)
        {
            return
            [
                new NetworkTextItem(
                    firstTextId,
                    X: 0,
                    Y: 0,
                    Font: 4,
                    TextWidth: 1,
                    TextHeight: 1,
                    ValueUrl: ScreenValueUri(baseUri, screenIndex, "value/blank"),
                    Color: screen.TextColorHex,
                    UpdateSeconds: ScreenHeartbeatPolicy.HeartbeatUpdateSeconds)
            ];
        }

        int[] rowY = [29, 53, 77];
        var items = new List<NetworkTextItem>(screen.Slots.Length);
        var heartbeatAssigned = false;
        for (var row = 0; row < screen.Slots.Length; row++)
        {
            var assignment = screen.Slots[row];
            if (assignment is null)
            {
                continue;
            }

            var widget = DashboardWidgetCatalog.GetRequired(assignment.WidgetKey);
            var effectiveUpdateSeconds = EffectiveUpdateSeconds(widget, minimumSensorUpdateSeconds);
            var updateSeconds = heartbeatAssigned
                ? effectiveUpdateSeconds
                : Math.Min(effectiveUpdateSeconds, ScreenHeartbeatPolicy.HeartbeatUpdateSeconds);
            heartbeatAssigned = true;
            items.Add(new NetworkTextItem(
                firstTextId + row,
                X: 18,
                Y: rowY[row],
                Font: 4,
                TextWidth: 104,
                TextHeight: 16,
                ValueUrl: ScreenValueUri(baseUri, screenIndex, widget.Path(assignment.DisplayMode)),
                Color: screen.TextColorHex,
                UpdateSeconds: updateSeconds));
        }

        if (items.Count == 0)
        {
            items.Add(new NetworkTextItem(
                firstTextId,
                X: 0,
                Y: 0,
                Font: 4,
                TextWidth: 1,
                TextHeight: 1,
                ValueUrl: ScreenValueUri(baseUri, screenIndex, "value/blank"),
                Color: screen.TextColorHex,
                UpdateSeconds: ScreenHeartbeatPolicy.HeartbeatUpdateSeconds));
        }

        return items;
    }

    private static int EffectiveUpdateSeconds(
        DashboardWidgetDefinition widget,
        int minimumSensorUpdateSeconds) =>
        widget.Key is "clock" or "date"
            ? widget.UpdateSeconds
            : Math.Max(widget.UpdateSeconds, Math.Clamp(minimumSensorUpdateSeconds, 5, 30));

    private static NetworkTextItem CreateHeartbeatItem(
        int textId,
        int screenIndex,
        Uri baseUri,
        string color) =>
        new(
            textId,
            X: 0,
            Y: 0,
            Font: 4,
            TextWidth: 1,
            TextHeight: 1,
            ValueUrl: ScreenValueUri(baseUri, screenIndex, "value/blank"),
            Color: color,
            UpdateSeconds: ScreenHeartbeatPolicy.HeartbeatUpdateSeconds);

    internal static Uri ScreenValueUri(Uri baseUri, int screenIndex, string valuePath) =>
        new(baseUri, $"screen/{screenIndex}/{valuePath.TrimStart('/')}");

    private static async Task WaitForScreenReadyAsync(
        DashboardRuntimeServer server,
        int screenIndex,
        long previousBackgroundCount,
        long previousValueCount,
        bool requiresNetworkValue,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(7);
        while (DateTime.UtcNow < deadline)
        {
            if (server.GetBackgroundRequestCount(screenIndex) > previousBackgroundCount &&
                (!requiresNetworkValue || server.GetScreenValueRequestCount(screenIndex) > previousValueCount))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    internal static bool ScreenEquals(
        DashboardScreenConfiguration? left,
        DashboardScreenConfiguration right)
    {
        if (left is null ||
            !string.Equals(left.Title, right.Title, StringComparison.Ordinal) ||
            !string.Equals(left.WidgetKey, right.WidgetKey, StringComparison.Ordinal) ||
            !string.Equals(left.SolidColorHex, right.SolidColorHex, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.CustomMediaPath, right.CustomMediaPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.FrameColorHex, right.FrameColorHex, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.TextColorHex, right.TextColorHex, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.HeaderIconKey, right.HeaderIconKey, StringComparison.Ordinal) ||
            left.ClockShowSeconds != right.ClockShowSeconds ||
            left.Slots.Length != right.Slots.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Slots.Length; index++)
        {
            if (left.Slots[index] != right.Slots[index])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] RenderBackground(
        DashboardScreenConfiguration screen,
        int minimumSensorUpdateSeconds)
    {
        if (screen.CustomMediaPath is not null)
        {
            return CustomMediaBackgroundRenderer.Render(screen.CustomMediaPath);
        }

        if (screen.SolidColorHex is not null)
        {
            return ConsoleDashboardBackgroundRenderer.RenderSolid(ParseColor(screen.SolidColorHex));
        }

        var icon = DashboardHeaderIconCatalog.GetRequired(screen.HeaderIconKey);
        var frameColor = ParseColor(screen.FrameColorHex);
        var textColor = ParseColor(screen.TextColorHex);
        var iconColor = ParseColor(icon.FixedColorHex ?? screen.TextColorHex);

        if (string.Equals(screen.WidgetKey, "clock_large", StringComparison.Ordinal))
        {
            return ConsoleDashboardBackgroundRenderer.RenderLargeClock(
                screen.Title,
                frameColor,
                textColor,
                icon.Symbol,
                iconColor,
                icon.Key);
        }

        var updateSeconds = screen.Slots
            .Where(assignment => assignment is not null)
            .Select(assignment => EffectiveUpdateSeconds(
                DashboardWidgetCatalog.GetRequired(assignment!.WidgetKey),
                minimumSensorUpdateSeconds))
            .DefaultIfEmpty(5)
            .Min();
        return ConsoleDashboardBackgroundRenderer.RenderConsole(
            screen.Title,
            updateSeconds,
            frameColor,
            textColor,
            icon.Symbol,
            iconColor,
            icon.Key);
    }

    private static MediaColor ParseColor(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"Invalid color '{hex}'.");
        }

        return MediaColor.FromRgb(
            byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static void EnsureSuccess(TimesGateCommandResult result, string operation)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{operation} failed: {result.ErrorMessage}");
        }
    }

    private sealed record AppliedSession(
        DashboardScreenConfiguration[] Screens,
        string DeviceHost,
        int LocalToken,
        int Brightness,
        IPAddress ListenAddress,
        int ListenPort);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "Dashboard apply failed for {Host}.")]
    private static partial void LogApplyFailed(ILogger logger, string host, Exception exception);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information, Message = "Automatic reconnect {State}.")]
    private static partial void LogAutoReconnectChanged(ILogger logger, string state);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning, Message = "Times Gate health check failed for {Host}: {Error}")]
    private static partial void LogHealthCheckFailed(ILogger logger, string host, string? error);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Warning, Message = "Automatic dashboard restore failed.")]
    private static partial void LogAutomaticRestoreFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3105, Level = LogLevel.Information, Message = "Dashboard restored after reconnecting to {Host}.")]
    private static partial void LogDashboardRestored(ILogger logger, string host);

    [LoggerMessage(EventId = 3106, Level = LogLevel.Information, Message = "Dashboard state: {State}. {Message}")]
    private static partial void LogDashboardState(
        ILogger logger,
        DashboardConnectionState state,
        string message);

    [LoggerMessage(EventId = 3107, Level = LogLevel.Information, Message = "LCD {ScreenIndex} restored automatically on {Host}.")]
    private static partial void LogScreenRestored(ILogger logger, int screenIndex, string host);

    [LoggerMessage(EventId = 3108, Level = LogLevel.Warning, Message = "LCD {ScreenIndex} heartbeat is silent. Last request: {LastRequestUtc}.")]
    private static partial void LogScreenSilent(
        ILogger logger,
        int screenIndex,
        DateTimeOffset? lastRequestUtc);

    [LoggerMessage(EventId = 3109, Level = LogLevel.Information, Message = "PC LAN address changed from {OldAddress} to {NewAddress}; dashboard URLs were refreshed.")]
    private static partial void LogListenAddressChanged(
        ILogger logger,
        IPAddress oldAddress,
        IPAddress newAddress);

    [LoggerMessage(EventId = 3110, Level = LogLevel.Warning, Message = "All five LCD heartbeats are silent; treating this as a full Times Gate reset.")]
    private static partial void LogDashboardSilent(ILogger logger);

    [LoggerMessage(EventId = 3111, Level = LogLevel.Information, Message = "Applying {Target} to {Host}; sensor server {ListenAddress}:{ListenPort}; force={ForceApply}.")]
    private static partial void LogApplyStarted(
        ILogger logger,
        string target,
        string host,
        IPAddress listenAddress,
        int listenPort,
        bool forceApply);

    [LoggerMessage(EventId = 3112, Level = LogLevel.Information, Message = "Applied {Target} to {Host}; changed screens={ChangedScreens}. {Activity}")]
    private static partial void LogApplyCompleted(
        ILogger logger,
        string target,
        string host,
        int changedScreens,
        string activity);

    [LoggerMessage(EventId = 3113, Level = LogLevel.Information, Message = "Heartbeat journal for {Host}; auto-reconnect={AutoReconnect}. {Activity}")]
    private static partial void LogHeartbeatJournal(
        ILogger logger,
        string host,
        bool autoReconnect,
        string activity);

    [LoggerMessage(EventId = 3114, Level = LogLevel.Warning, Message = "Possible Times Gate reset on {Host}; observed={ObservedUtc}; estimated interruption={EstimatedStartUtc}; auto-reconnect={AutoReconnect}. {Activity}")]
    private static partial void LogDashboardIncidentDetected(
        ILogger logger,
        string host,
        DateTimeOffset observedUtc,
        DateTimeOffset? estimatedStartUtc,
        bool autoReconnect,
        string activity);

    [LoggerMessage(EventId = 3115, Level = LogLevel.Information, Message = "Times Gate incident recovered on {Host}; detected={DetectedUtc}; recovered={RecoveredUtc}; recovery={Recovery}. {Activity}")]
    private static partial void LogDashboardIncidentRecovered(
        ILogger logger,
        string host,
        DateTimeOffset detectedUtc,
        DateTimeOffset recoveredUtc,
        string recovery,
        string activity);

    [LoggerMessage(EventId = 3116, Level = LogLevel.Information, Message = "Minimum sensor refresh interval changed to {Seconds} seconds.")]
    private static partial void LogSensorRefreshChanged(ILogger logger, int seconds);

    [LoggerMessage(EventId = 3119, Level = LogLevel.Error, Message = "Times Gate connection monitor failed unexpectedly; restart attempt={RestartAttempt}.")]
    private static partial void LogConnectionMonitorFailed(
        ILogger logger,
        int restartAttempt,
        Exception exception);

    [LoggerMessage(EventId = 3120, Level = LogLevel.Information, Message = "Times Gate connection monitor restarted; attempt={RestartAttempt}.")]
    private static partial void LogConnectionMonitorRestarted(ILogger logger, int restartAttempt);

    [LoggerMessage(EventId = 3121, Level = LogLevel.Error, Message = "Hardware polling failed; TGm will keep the last snapshot and retry.")]
    private static partial void LogHardwarePollingFailed(ILogger logger, Exception exception);
}

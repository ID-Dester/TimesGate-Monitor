using Microsoft.Extensions.Logging;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Hardware.SensorHost;

public sealed partial class SensorHostAwareHardwareMonitor : IHardwareMonitor
{
    private readonly object _sync = new();
    private readonly Func<IHardwareMonitor> _localFactory;
    private readonly SensorHostClient _client;
    private readonly Func<bool>? _sensorHostStarter;
    private readonly ILogger<SensorHostAwareHardwareMonitor> _logger;
    private readonly SensorHostRecoveryPolicy _recoveryPolicy = new();
    private IHardwareMonitor? _localMonitor;
    private SourceKind _source;
    private bool _initialized;
    private bool _disposed;

    public SensorHostAwareHardwareMonitor(
        Func<IHardwareMonitor> localFactory,
        SensorHostClient client,
        ILogger<SensorHostAwareHardwareMonitor> logger,
        Func<bool>? sensorHostStarter = null)
    {
        _localFactory = localFactory;
        _client = client;
        _logger = logger;
        _sensorHostStarter = sensorHostStarter;
    }

    public bool IsInitialized => _initialized;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _initialized = true;
    }

    public HardwareSnapshot CaptureSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("Initialize must be called before capturing hardware sensors.");
        }

        lock (_sync)
        {
            if (_client.TryCapture(out var remoteSnapshot))
            {
                _recoveryPolicy.RecordSuccess();
                _localMonitor?.Dispose();
                _localMonitor = null;
                ChangeSource(SourceKind.SensorHost);
                return remoteSnapshot;
            }

            if (_sensorHostStarter is not null &&
                _recoveryPolicy.RecordFailureAndShouldRequestStart(
                    DateTimeOffset.UtcNow))
            {
                var accepted = false;
                try
                {
                    accepted = _sensorHostStarter();
                }
                catch
                {
                    // Recovery is best effort. Local sensors remain available.
                }

                LogRecoveryRequested(
                    _logger,
                    accepted,
                    _recoveryPolicy.ConsecutiveFailures);
            }

            _localMonitor ??= _localFactory();
            if (!_localMonitor.IsInitialized)
            {
                _localMonitor.Initialize();
            }

            ChangeSource(SourceKind.LocalFallback);
            return _localMonitor.CaptureSnapshot();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _localMonitor?.Dispose();
            _localMonitor = null;
            _disposed = true;
        }
    }

    private void ChangeSource(SourceKind source)
    {
        if (_source == source)
        {
            return;
        }

        _source = source;
        LogSourceChanged(_logger, source.ToString());
    }

    private enum SourceKind
    {
        None,
        LocalFallback,
        SensorHost
    }

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Information,
        Message = "Hardware snapshot source changed to {Source}.")]
    private static partial void LogSourceChanged(ILogger logger, string source);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Warning,
        Message = "Sensor host stopped answering after {ConsecutiveFailures} consecutive failures. Scheduled-task restart requested; request accepted: {Accepted}.")]
    private static partial void LogRecoveryRequested(
        ILogger logger,
        bool accepted,
        int consecutiveFailures);
}

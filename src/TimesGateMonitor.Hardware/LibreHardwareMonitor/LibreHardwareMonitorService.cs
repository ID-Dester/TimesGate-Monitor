using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Hardware.Windows;

namespace TimesGateMonitor.Hardware.LibreHardwareMonitor;

public sealed partial class LibreHardwareMonitorService : IHardwareMonitor
{
    private readonly ILogger<LibreHardwareMonitorService> _logger;
    private readonly UpdateVisitor _updateVisitor = new();
    private readonly WindowsCpuLoadSampler _cpuLoadFallback = new();
    private Computer? _computer;
    private bool _disposed;
    private bool _sensorSummaryLogged;
    private bool _cpuLoadFallbackLogged;

    public LibreHardwareMonitorService(ILogger<LibreHardwareMonitorService> logger)
    {
        _logger = logger;
    }

    public bool IsInitialized => _computer is not null;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_computer is not null)
        {
            return;
        }

        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsNetworkEnabled = false,
            IsStorageEnabled = false,
            IsBatteryEnabled = false,
            IsPsuEnabled = false
        };

        try
        {
            computer.Open();
            _computer = computer;
            LogInitialized(_logger, computer.Hardware.Count);
        }
        catch
        {
            computer.Close();
            throw;
        }
    }

    public HardwareSnapshot CaptureSnapshot()
    {
        var samples = CaptureSensorSamples();

        var snapshot = HardwareSnapshotMapper.Map(
            samples,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            DateTimeOffset.UtcNow);

        if (!snapshot.CpuLoadPercent.HasValue)
        {
            var fallbackLoad = _cpuLoadFallback.Capture();
            if (fallbackLoad.HasValue)
            {
                snapshot = snapshot with { CpuLoadPercent = fallbackLoad };

                if (!_cpuLoadFallbackLogged)
                {
                    LogCpuLoadFallbackEnabled(_logger);
                    _cpuLoadFallbackLogged = true;
                }
            }
        }

        if (!_sensorSummaryLogged)
        {
            LogSensorSummary(
                _logger,
                samples.Count,
                snapshot.CpuLoadPercent.HasValue,
                snapshot.CpuTemperatureCelsius.HasValue,
                snapshot.GpuLoadPercent.HasValue,
                snapshot.GpuTemperatureCelsius.HasValue,
                snapshot.RamLoadPercent.HasValue);

            if (!snapshot.CpuTemperatureCelsius.HasValue || !snapshot.CpuPowerWatts.HasValue)
            {
                var pawnIo = PawnIoRuntimeStatus.Capture();
                LogCpuLowLevelSensorsUnavailable(
                    _logger,
                    pawnIo.IsInstalled,
                    pawnIo.Version?.ToString() ?? "--",
                    pawnIo.IsAdministrator);
            }

            _sensorSummaryLogged = true;
        }

        return snapshot;
    }

    public IReadOnlyList<HardwareSensorSample> CaptureSensorSamples()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var computer = _computer ?? throw new InvalidOperationException(
            "Initialize must be called before capturing hardware sensors.");

        var samples = new List<HardwareSensorSample>(capacity: 128);

        foreach (var hardware in computer.Hardware)
        {
            try
            {
                hardware.Accept(_updateVisitor);
                CollectSamples(hardware, samples);
            }
            catch (Exception exception)
            {
                LogHardwareUpdateFailed(_logger, hardware.Name, exception);
            }
        }

        return samples;
    }

    private static void CollectSamples(IHardware hardware, ICollection<HardwareSensorSample> samples)
    {
        var deviceKind = MapHardwareType(hardware.HardwareType);

        foreach (var sensor in hardware.Sensors)
        {
            if (!sensor.Value.HasValue || !float.IsFinite(sensor.Value.Value))
            {
                continue;
            }

            var metricKind = MapSensorType(sensor.SensorType);
            if (metricKind == HardwareMetricKind.Unknown)
            {
                continue;
            }

            samples.Add(new HardwareSensorSample(
                hardware.Identifier.ToString(),
                hardware.Name,
                deviceKind,
                sensor.Name,
                metricKind,
                sensor.Value.Value));
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            CollectSamples(subHardware, samples);
        }
    }

    private static HardwareDeviceKind MapHardwareType(HardwareType hardwareType) => hardwareType switch
    {
        HardwareType.Cpu => HardwareDeviceKind.Cpu,
        HardwareType.GpuNvidia => HardwareDeviceKind.GpuNvidia,
        HardwareType.GpuAmd => HardwareDeviceKind.GpuAmd,
        HardwareType.GpuIntel => HardwareDeviceKind.GpuIntel,
        HardwareType.Memory => HardwareDeviceKind.Memory,
        _ => HardwareDeviceKind.Unknown
    };

    private static HardwareMetricKind MapSensorType(SensorType sensorType) => sensorType switch
    {
        SensorType.Load => HardwareMetricKind.Load,
        SensorType.Temperature => HardwareMetricKind.Temperature,
        SensorType.Clock => HardwareMetricKind.Clock,
        SensorType.Power => HardwareMetricKind.Power,
        SensorType.Data => HardwareMetricKind.DataGigabytes,
        SensorType.SmallData => HardwareMetricKind.SmallDataMegabytes,
        _ => HardwareMetricKind.Unknown
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _computer?.Close();
        _computer = null;
        _disposed = true;
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "LibreHardwareMonitor initialized with {HardwareCount} top-level hardware devices.")]
    private static partial void LogInitialized(ILogger logger, int hardwareCount);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Failed to update hardware device {HardwareName}.")]
    private static partial void LogHardwareUpdateFailed(
        ILogger logger,
        string hardwareName,
        Exception exception);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Collected {SensorCount} sensors. CPU load: {HasCpuLoad}; CPU temp: {HasCpuTemp}; GPU load: {HasGpuLoad}; GPU temp: {HasGpuTemp}; RAM: {HasRam}.")]
    private static partial void LogSensorSummary(
        ILogger logger,
        int sensorCount,
        bool hasCpuLoad,
        bool hasCpuTemp,
        bool hasGpuLoad,
        bool hasGpuTemp,
        bool hasRam);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "CPU temperature/power sensors are unavailable. PawnIO installed: {PawnIoInstalled}; version: {PawnIoVersion}; process is administrator: {IsAdministrator}. Verify the PawnIO installation and driver access if values remain unavailable.")]
    private static partial void LogCpuLowLevelSensorsUnavailable(
        ILogger logger,
        bool pawnIoInstalled,
        string pawnIoVersion,
        bool isAdministrator);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "CPU load is supplied by the Windows system-times fallback because the hardware sensor is unavailable.")]
    private static partial void LogCpuLoadFallbackEnabled(ILogger logger);
}

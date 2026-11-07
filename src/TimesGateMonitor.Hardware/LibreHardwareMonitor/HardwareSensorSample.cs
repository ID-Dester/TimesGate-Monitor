namespace TimesGateMonitor.Hardware.LibreHardwareMonitor;

public sealed record HardwareSensorSample(
    string DeviceId,
    string DeviceName,
    HardwareDeviceKind DeviceKind,
    string SensorName,
    HardwareMetricKind MetricKind,
    double Value);

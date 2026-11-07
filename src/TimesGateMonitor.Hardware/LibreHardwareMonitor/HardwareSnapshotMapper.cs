using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Hardware.LibreHardwareMonitor;

public static class HardwareSnapshotMapper
{
    public static HardwareSnapshot Map(
        IReadOnlyCollection<HardwareSensorSample> samples,
        TimeSpan uptime,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var cpu = samples.Where(sample => sample.DeviceKind == HardwareDeviceKind.Cpu).ToArray();
        var memory = SelectPhysicalMemory(samples);
        var gpu = SelectGpu(samples);

        var ramUsed = FindNamed(memory, HardwareMetricKind.DataGigabytes, "Memory Used");
        var ramAvailable = FindNamed(memory, HardwareMetricKind.DataGigabytes, "Memory Available");
        double? ramTotal = ramUsed.HasValue && ramAvailable.HasValue
            ? ramUsed.Value + ramAvailable.Value
            : null;

        return new HardwareSnapshot
        {
            CapturedAt = capturedAt,
            CpuLoadPercent = FindNamed(cpu, HardwareMetricKind.Load, "CPU Total")
                ?? Average(cpu, HardwareMetricKind.Load, "CPU Core"),
            CpuTemperatureCelsius = PositiveOrNull(FindNamed(
                    cpu,
                    HardwareMetricKind.Temperature,
                    "CPU Package",
                    "Core (Tctl/Tdie)",
                    "CPU (Tctl/Tdie)")
                ?? Maximum(cpu, HardwareMetricKind.Temperature)),
            CpuClockMegahertz = PositiveOrNull(
                Average(cpu, HardwareMetricKind.Clock, "CPU Core")
                ?? Average(cpu, HardwareMetricKind.Clock, "Core #")
                ?? FindNamed(cpu, HardwareMetricKind.Clock, "Cores (Average)")
                ?? FindFirst(cpu, HardwareMetricKind.Clock)),
            CpuPowerWatts = PositiveOrNull(
                FindNamed(cpu, HardwareMetricKind.Power, "CPU Package", "Package")
                ?? FindContaining(cpu, HardwareMetricKind.Power, "Package")),
            GpuName = gpu.FirstOrDefault()?.DeviceName,
            GpuLoadPercent = FindNamed(gpu, HardwareMetricKind.Load, "GPU Core")
                ?? FindContaining(gpu, HardwareMetricKind.Load, "GPU Core"),
            GpuTemperatureCelsius = FindNamed(gpu, HardwareMetricKind.Temperature, "GPU Core")
                ?? FindContaining(gpu, HardwareMetricKind.Temperature, "GPU"),
            GpuClockMegahertz = FindNamed(gpu, HardwareMetricKind.Clock, "GPU Core")
                ?? FindContaining(gpu, HardwareMetricKind.Clock, "GPU Core"),
            GpuPowerWatts = FindNamed(gpu, HardwareMetricKind.Power, "GPU Package", "GPU Power")
                ?? FindContaining(gpu, HardwareMetricKind.Power, "GPU"),
            VramUsedGigabytes = FindStorageValue(gpu, "GPU Memory Used", "D3D Dedicated Memory Used"),
            VramTotalGigabytes = FindStorageValue(gpu, "GPU Memory Total", "D3D Dedicated Memory Total"),
            RamLoadPercent = FindNamed(memory, HardwareMetricKind.Load, "Memory"),
            RamUsedGigabytes = ramUsed,
            RamTotalGigabytes = ramTotal,
            RamAvailableGigabytes = ramAvailable,
            Uptime = uptime
        };
    }

    private static HardwareSensorSample[] SelectGpu(IReadOnlyCollection<HardwareSensorSample> samples)
    {
        var selectedDevice = samples
            .Where(sample => IsGpu(sample.DeviceKind))
            .GroupBy(sample => new { sample.DeviceId, sample.DeviceName, sample.DeviceKind })
            .OrderBy(group => GpuPriority(group.Key.DeviceKind))
            .ThenByDescending(group =>
                group.Any(sample => sample.MetricKind == HardwareMetricKind.Load &&
                    sample.SensorName.Contains("GPU Core", StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();

        return selectedDevice?.ToArray() ?? [];
    }

    private static HardwareSensorSample[] SelectPhysicalMemory(
        IReadOnlyCollection<HardwareSensorSample> samples)
    {
        var selectedDevice = samples
            .Where(sample => sample.DeviceKind == HardwareDeviceKind.Memory)
            .GroupBy(sample => new { sample.DeviceId, sample.DeviceName })
            .OrderByDescending(group =>
                string.Equals(group.Key.DeviceId, "/ram", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(group =>
                group.Key.DeviceName.Contains("Total", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return selectedDevice?.ToArray() ?? [];
    }

    private static bool IsGpu(HardwareDeviceKind kind) =>
        kind is HardwareDeviceKind.GpuNvidia or HardwareDeviceKind.GpuAmd or HardwareDeviceKind.GpuIntel;

    private static int GpuPriority(HardwareDeviceKind kind) => kind switch
    {
        HardwareDeviceKind.GpuNvidia => 0,
        HardwareDeviceKind.GpuAmd => 0,
        HardwareDeviceKind.GpuIntel => 1,
        _ => 2
    };

    private static double? FindNamed(
        IEnumerable<HardwareSensorSample> samples,
        HardwareMetricKind metricKind,
        params string[] names) =>
        names.Select(name => samples.FirstOrDefault(sample =>
                sample.MetricKind == metricKind &&
                string.Equals(sample.SensorName, name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(sample => sample is not null) is { } match
                ? FiniteOrNull(match.Value)
                : null;

    private static double? FindContaining(
        IEnumerable<HardwareSensorSample> samples,
        HardwareMetricKind metricKind,
        string text) =>
        FiniteOrNull(samples.FirstOrDefault(sample =>
            sample.MetricKind == metricKind &&
            sample.SensorName.Contains(text, StringComparison.OrdinalIgnoreCase))?.Value);

    private static double? FindFirst(
        IEnumerable<HardwareSensorSample> samples,
        HardwareMetricKind metricKind) =>
        FiniteOrNull(samples.FirstOrDefault(sample => sample.MetricKind == metricKind)?.Value);

    private static double? Average(
        IEnumerable<HardwareSensorSample> samples,
        HardwareMetricKind metricKind,
        string namePrefix)
    {
        var values = samples
            .Where(sample =>
                sample.MetricKind == metricKind &&
                sample.SensorName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) &&
                double.IsFinite(sample.Value))
            .Select(sample => sample.Value)
            .ToArray();

        return values.Length > 0 ? values.Average() : null;
    }

    private static double? Maximum(
        IEnumerable<HardwareSensorSample> samples,
        HardwareMetricKind metricKind)
    {
        var values = samples
            .Where(sample => sample.MetricKind == metricKind && double.IsFinite(sample.Value))
            .Select(sample => sample.Value)
            .ToArray();

        return values.Length > 0 ? values.Max() : null;
    }

    private static double? FindStorageValue(
        IEnumerable<HardwareSensorSample> samples,
        params string[] names)
    {
        foreach (var name in names)
        {
            var match = samples.FirstOrDefault(sample =>
                (sample.MetricKind is HardwareMetricKind.DataGigabytes or
                    HardwareMetricKind.SmallDataMegabytes) &&
                string.Equals(sample.SensorName, name, StringComparison.OrdinalIgnoreCase));

            if (match is null || !double.IsFinite(match.Value))
            {
                continue;
            }

            return match.MetricKind == HardwareMetricKind.SmallDataMegabytes
                ? match.Value / 1024d
                : match.Value;
        }

        return null;
    }

    private static double? FiniteOrNull(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value : null;

    private static double? PositiveOrNull(double? value) =>
        value.HasValue && double.IsFinite(value.Value) && value.Value > 0 ? value.Value : null;
}

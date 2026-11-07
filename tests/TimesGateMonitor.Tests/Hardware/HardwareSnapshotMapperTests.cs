using TimesGateMonitor.Hardware.LibreHardwareMonitor;

namespace TimesGateMonitor.Tests.Hardware;

internal static class HardwareSnapshotMapperTests
{
    public static void Run()
    {
        HardwareSensorSample[] samples =
        [
            new("/cpu/0", "CPU", HardwareDeviceKind.Cpu, "CPU Total", HardwareMetricKind.Load, 25),
            new("/cpu/0", "CPU", HardwareDeviceKind.Cpu, "CPU Package", HardwareMetricKind.Temperature, 0),
            new("/cpu/0", "CPU", HardwareDeviceKind.Cpu, "CPU Core #1", HardwareMetricKind.Clock, 0),
            new("/gpu-intel/0", "Integrated GPU", HardwareDeviceKind.GpuIntel, "GPU Core", HardwareMetricKind.Load, 3),
            new("/gpu-nvidia/0", "Discrete GPU", HardwareDeviceKind.GpuNvidia, "GPU Core", HardwareMetricKind.Load, 72),
            new("/gpu-nvidia/0", "Discrete GPU", HardwareDeviceKind.GpuNvidia, "GPU Core", HardwareMetricKind.Temperature, 58),
            new("/gpu-nvidia/0", "Discrete GPU", HardwareDeviceKind.GpuNvidia, "GPU Memory Used", HardwareMetricKind.SmallDataMegabytes, 4096),
            new("/gpu-nvidia/0", "Discrete GPU", HardwareDeviceKind.GpuNvidia, "GPU Memory Total", HardwareMetricKind.SmallDataMegabytes, 12288),
            new("/vram", "Virtual Memory", HardwareDeviceKind.Memory, "Memory Used", HardwareMetricKind.DataGigabytes, 40),
            new("/vram", "Virtual Memory", HardwareDeviceKind.Memory, "Memory Available", HardwareMetricKind.DataGigabytes, 24),
            new("/ram", "Generic Memory", HardwareDeviceKind.Memory, "Memory", HardwareMetricKind.Load, 50),
            new("/ram", "Generic Memory", HardwareDeviceKind.Memory, "Memory Used", HardwareMetricKind.DataGigabytes, 16),
            new("/ram", "Generic Memory", HardwareDeviceKind.Memory, "Memory Available", HardwareMetricKind.DataGigabytes, 16)
        ];

        var snapshot = HardwareSnapshotMapper.Map(samples, TimeSpan.FromHours(2), DateTimeOffset.UnixEpoch);

        TestAssert.Equal(25d, snapshot.CpuLoadPercent!.Value, "CPU total load mapping failed.");
        TestAssert.True(snapshot.CpuTemperatureCelsius is null, "Zero CPU temperature must be unavailable.");
        TestAssert.True(snapshot.CpuClockMegahertz is null, "Zero CPU clock must be unavailable.");
        TestAssert.Equal("Discrete GPU", snapshot.GpuName!, "Discrete GPU must be preferred.");
        TestAssert.Equal(72d, snapshot.GpuLoadPercent!.Value, "GPU load mapping failed.");
        TestAssert.Equal(4d, snapshot.VramUsedGigabytes!.Value, "VRAM MB-to-GB conversion failed.");
        TestAssert.Equal(12d, snapshot.VramTotalGigabytes!.Value, "VRAM total conversion failed.");
        TestAssert.Equal(32d, snapshot.RamTotalGigabytes!.Value, "Physical RAM must be selected over virtual memory.");
        TestAssert.Equal(TimeSpan.FromHours(2), snapshot.Uptime, "Uptime mapping failed.");
    }
}

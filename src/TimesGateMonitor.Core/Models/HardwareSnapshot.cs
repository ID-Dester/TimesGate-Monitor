namespace TimesGateMonitor.Core.Models;

/// <summary>
/// Immutable, renderer-friendly view of the latest PC sensor values.
/// A null measurement means that the sensor is currently unavailable.
/// </summary>
public sealed record HardwareSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public double? CpuLoadPercent { get; init; }

    public double? CpuTemperatureCelsius { get; init; }

    public double? CpuClockMegahertz { get; init; }

    public double? CpuPowerWatts { get; init; }

    public string? GpuName { get; init; }

    public double? GpuLoadPercent { get; init; }

    public double? GpuTemperatureCelsius { get; init; }

    public double? GpuClockMegahertz { get; init; }

    public double? GpuPowerWatts { get; init; }

    public double? VramUsedGigabytes { get; init; }

    public double? VramTotalGigabytes { get; init; }

    public double? RamLoadPercent { get; init; }

    public double? RamUsedGigabytes { get; init; }

    public double? RamTotalGigabytes { get; init; }

    public double? RamAvailableGigabytes { get; init; }

    public TimeSpan Uptime { get; init; }
}

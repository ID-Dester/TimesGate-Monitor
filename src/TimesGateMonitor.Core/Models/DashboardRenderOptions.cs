namespace TimesGateMonitor.Core.Models;

public sealed record DashboardRenderOptions
{
    public uint AccentColor { get; init; } = 0xFFFF8C42;

    public uint BackgroundColor { get; init; } = 0xFF090C10;

    public uint ForegroundColor { get; init; } = 0xFFF5F7FA;

    public uint MutedColor { get; init; } = 0xFF9AA5B1;

    public uint TrackColor { get; init; } = 0xFF252A31;

    public uint WarmColor { get; init; } = 0xFFFFB000;

    public uint HotColor { get; init; } = 0xFFFF453A;

    public TemperatureThresholds CpuTemperatureThresholds { get; init; } =
        TemperatureThresholds.CpuDefault;

    public TemperatureThresholds GpuTemperatureThresholds { get; init; } =
        TemperatureThresholds.GpuDefault;
}

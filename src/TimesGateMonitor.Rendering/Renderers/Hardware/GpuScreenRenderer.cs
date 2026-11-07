using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public sealed class GpuScreenRenderer : HardwareScreenRendererBase
{
    public override int ScreenIndex => 2;

    protected override void Draw(
        DashboardCanvas canvas,
        HardwareSnapshot snapshot,
        DashboardRenderOptions options)
    {
        canvas.Title("GPU");
        canvas.ProgressBar(snapshot.GpuLoadPercent, y: 27);
        canvas.DrawCentered(HardwareValueFormatter.Percent(snapshot.GpuLoadPercent), 38, 21, canvas.Foreground, bold: true);
        canvas.MetricRow(
            "TEMP",
            HardwareValueFormatter.Temperature(snapshot.GpuTemperatureCelsius),
            67,
            canvas.TemperatureBrush(options.GpuTemperatureThresholds.Classify(snapshot.GpuTemperatureCelsius)),
            size: 9.5);
        canvas.MetricRow("CLK", HardwareValueFormatter.Clock(snapshot.GpuClockMegahertz), 82, size: 9.5);
        canvas.MetricRow("VRAM", HardwareValueFormatter.Gigabytes(snapshot.VramUsedGigabytes), 97, size: 9.5);
        canvas.MetricRow("PWR", HardwareValueFormatter.Power(snapshot.GpuPowerWatts), 112, size: 9.5);
    }
}

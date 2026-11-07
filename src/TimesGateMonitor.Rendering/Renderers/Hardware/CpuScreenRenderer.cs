using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public sealed class CpuScreenRenderer : HardwareScreenRendererBase
{
    public override int ScreenIndex => 1;

    protected override void Draw(
        DashboardCanvas canvas,
        HardwareSnapshot snapshot,
        DashboardRenderOptions options)
    {
        canvas.Title("CPU");
        canvas.ProgressBar(snapshot.CpuLoadPercent, y: 29);
        canvas.DrawCentered(HardwareValueFormatter.Percent(snapshot.CpuLoadPercent), 41, 23, canvas.Foreground, bold: true);
        canvas.MetricRow(
            "TEMP",
            HardwareValueFormatter.Temperature(snapshot.CpuTemperatureCelsius),
            74,
            canvas.TemperatureBrush(options.CpuTemperatureThresholds.Classify(snapshot.CpuTemperatureCelsius)));
        canvas.MetricRow("CLK", HardwareValueFormatter.Clock(snapshot.CpuClockMegahertz), 92);
        canvas.MetricRow("PWR", HardwareValueFormatter.Power(snapshot.CpuPowerWatts), 110);
    }
}

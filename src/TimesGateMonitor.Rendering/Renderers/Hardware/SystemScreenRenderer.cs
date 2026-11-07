using System.Globalization;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public sealed class SystemScreenRenderer : HardwareScreenRendererBase
{
    public override int ScreenIndex => 5;

    protected override void Draw(
        DashboardCanvas canvas,
        HardwareSnapshot snapshot,
        DashboardRenderOptions options)
    {
        canvas.Title("SYSTEM");
        canvas.DrawCentered("TIME", 28, 9, canvas.Muted, bold: true);
        canvas.DrawCentered(
            DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            39,
            18,
            canvas.Foreground,
            bold: true);
        canvas.DrawCentered("UP", 70, 9, canvas.Muted, bold: true);
        canvas.DrawCentered(HardwareValueFormatter.Uptime(snapshot.Uptime), 80, 16, canvas.Accent, bold: true);
        canvas.MetricRow(
            "CPU",
            HardwareValueFormatter.CompactTemperature(snapshot.CpuTemperatureCelsius),
            107,
            canvas.TemperatureBrush(options.CpuTemperatureThresholds.Classify(snapshot.CpuTemperatureCelsius)),
            size: 9.5);
        canvas.MetricRow(
            "GPU",
            HardwareValueFormatter.CompactTemperature(snapshot.GpuTemperatureCelsius),
            118,
            canvas.TemperatureBrush(options.GpuTemperatureThresholds.Classify(snapshot.GpuTemperatureCelsius)),
            size: 9.5);
    }
}

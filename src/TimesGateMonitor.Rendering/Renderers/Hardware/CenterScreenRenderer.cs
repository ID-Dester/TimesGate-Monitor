using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public sealed class CenterScreenRenderer : HardwareScreenRendererBase
{
    public override int ScreenIndex => 3;

    protected override void Draw(
        DashboardCanvas canvas,
        HardwareSnapshot snapshot,
        DashboardRenderOptions options)
    {
        canvas.DrawText("CPU", 9, 6, 12, canvas.Accent, bold: true);
        canvas.DrawRight(HardwareValueFormatter.Percent(snapshot.CpuLoadPercent), 119, 6, 12, canvas.Foreground, bold: true);
        canvas.ProgressBar(snapshot.CpuLoadPercent, y: 25, height: 8);

        var cpuTemperatureColor = canvas.TemperatureBrush(
            options.CpuTemperatureThresholds.Classify(snapshot.CpuTemperatureCelsius));
        var gpuTemperatureColor = canvas.TemperatureBrush(
            options.GpuTemperatureThresholds.Classify(snapshot.GpuTemperatureCelsius));
        canvas.DrawText(HardwareValueFormatter.CompactTemperature(snapshot.CpuTemperatureCelsius), 12, 43, 22, cpuTemperatureColor, bold: true);
        canvas.DrawRight(HardwareValueFormatter.CompactTemperature(snapshot.GpuTemperatureCelsius), 116, 43, 22, gpuTemperatureColor, bold: true);
        canvas.DrawText("CPU", 22, 68, 9, canvas.Muted, bold: true);
        canvas.DrawRight("GPU", 106, 68, 9, canvas.Muted, bold: true);

        canvas.ProgressBar(snapshot.GpuLoadPercent, y: 88, height: 8);
        canvas.DrawText("GPU", 9, 103, 12, canvas.Accent, bold: true);
        canvas.DrawRight(HardwareValueFormatter.Percent(snapshot.GpuLoadPercent), 119, 103, 12, canvas.Foreground, bold: true);
    }
}

using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public sealed class RamScreenRenderer : HardwareScreenRendererBase
{
    public override int ScreenIndex => 4;

    protected override void Draw(
        DashboardCanvas canvas,
        HardwareSnapshot snapshot,
        DashboardRenderOptions options)
    {
        canvas.Title("RAM");
        canvas.ProgressBar(snapshot.RamLoadPercent, y: 30);
        canvas.DrawCentered(HardwareValueFormatter.Percent(snapshot.RamLoadPercent), 42, 23, canvas.Foreground, bold: true);
        canvas.DrawCentered(
            HardwareValueFormatter.RatioGigabytes(snapshot.RamUsedGigabytes, snapshot.RamTotalGigabytes),
            76,
            12,
            canvas.Foreground,
            bold: true);
        canvas.DrawCentered("FREE", 98, 9, canvas.Muted, bold: true);
        canvas.DrawCentered(
            HardwareValueFormatter.Gigabytes(snapshot.RamAvailableGigabytes),
            108,
            13,
            canvas.Accent,
            bold: true);
    }
}

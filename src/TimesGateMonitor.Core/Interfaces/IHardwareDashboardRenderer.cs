using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Core.Interfaces;

public interface IHardwareDashboardRenderer
{
    IReadOnlyList<DisplayFrame> Render(
        HardwareSnapshot snapshot,
        DashboardRenderOptions options);
}

using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Core.Interfaces;

public interface IHardwareScreenRenderer
{
    int ScreenIndex { get; }

    DisplayFrame Render(HardwareSnapshot snapshot, DashboardRenderOptions options);
}

using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Core.Interfaces;

public interface IHardwareMonitor : IDisposable
{
    bool IsInitialized { get; }

    void Initialize();

    HardwareSnapshot CaptureSnapshot();
}

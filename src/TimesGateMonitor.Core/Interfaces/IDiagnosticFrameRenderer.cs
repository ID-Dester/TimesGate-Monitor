using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Core.Interfaces;

public interface IDiagnosticFrameRenderer
{
    IReadOnlyList<DisplayFrame> Render(DiagnosticFrameOptions options);
}

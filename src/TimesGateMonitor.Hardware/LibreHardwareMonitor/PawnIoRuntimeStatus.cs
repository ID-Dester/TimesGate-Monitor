using System.Security.Principal;
using LibreHardwareMonitor.PawnIo;

namespace TimesGateMonitor.Hardware.LibreHardwareMonitor;

public sealed record PawnIoRuntimeStatus(
    bool IsInstalled,
    Version? Version,
    bool IsAdministrator)
{
    public static PawnIoRuntimeStatus Capture()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        return new PawnIoRuntimeStatus(
            PawnIo.IsInstalled,
            PawnIo.Version,
            principal.IsInRole(WindowsBuiltInRole.Administrator));
    }
}

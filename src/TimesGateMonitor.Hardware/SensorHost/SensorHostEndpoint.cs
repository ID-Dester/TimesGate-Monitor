using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace TimesGateMonitor.Hardware.SensorHost;

public static class SensorHostEndpoint
{
    public const string ScheduledTaskName = "TGm Sensor Host";

    public static string PipeName { get; } = CreatePipeName();

    private static string CreatePipeName()
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"TGm.SensorHost.{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }
}

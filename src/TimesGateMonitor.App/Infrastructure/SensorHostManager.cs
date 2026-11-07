using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using TimesGateMonitor.Hardware.SensorHost;

namespace TimesGateMonitor.App.Infrastructure;

internal sealed record SensorHostOperationResult(bool IsSuccess, bool WasCancelled, string Message);

internal static class SensorHostManager
{
    private const int UacCancelledError = 1223;

    public const int CurrentSetupVersion = 6;

    public static string ExecutablePath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "TGm.SensorHost.exe");

    public static bool IsAvailable => File.Exists(ExecutablePath);

    public static bool IsInstalled() => RunTaskScheduler("/Query", waitForExit: true) == 0;

    public static bool TryStart() => RunTaskScheduler("/Run", waitForExit: true) == 0;

    public static Task<SensorHostOperationResult> InstallAsync() => RunElevatedAsync("--install", includeTarget: true);

    public static Task<SensorHostOperationResult> UninstallAsync() => RunElevatedAsync("--uninstall", includeTarget: true);

    private static int RunTaskScheduler(string operation, bool waitForExit)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList =
                {
                    operation,
                    "/TN",
                    SensorHostEndpoint.ScheduledTaskName
                }
            });
            if (process is null)
            {
                return -1;
            }

            if (waitForExit)
            {
                process.WaitForExit(2000);
            }

            return process.HasExited ? process.ExitCode : -1;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return -1;
        }
    }

    private static async Task<SensorHostOperationResult> RunElevatedAsync(string operation, bool includeTarget)
    {
        if (!IsAvailable)
        {
            return new SensorHostOperationResult(
                false,
                false,
                "TGm.SensorHost.exe is missing from the application folder.");
        }

        try
        {
            var arguments = includeTarget
                ? BuildInstallArguments(operation)
                : operation;
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            if (process is null)
            {
                return new SensorHostOperationResult(false, false, "Could not start CPU sensor setup.");
            }

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                return new SensorHostOperationResult(
                    false,
                    false,
                    $"CPU sensor setup failed. Details: {Path.Combine(AppDataPaths.Logs, "sensor-host-setup.log")}");
            }

            _ = TryStart();
            var client = new SensorHostClient();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (client.TryCapture(out _))
                {
                    return new SensorHostOperationResult(true, false, "CPU sensor access is ready.");
                }

                await Task.Delay(250);
            }

            return new SensorHostOperationResult(
                false,
                false,
                "The elevated sensor reader was registered but did not answer. Use the gear menu → CPU SENSOR ACCESS to retry.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == UacCancelledError)
        {
            return new SensorHostOperationResult(
                false,
                true,
                "Administrator approval was cancelled. TGm will continue without low-level CPU sensors.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new SensorHostOperationResult(false, false, exception.Message);
        }
    }

    private static string BuildInstallArguments(string operation)
    {
        var identity = WindowsIdentity.GetCurrent();
        var userName = identity.Name;
        var userSid = identity.User?.Value;
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(userSid))
        {
            throw new InvalidOperationException("The current Windows user could not be identified.");
        }

        return string.Join(
            ' ',
            operation,
            "--target-user",
            WindowsQuote(userName),
            "--target-sid",
            WindowsQuote(userSid),
            "--pipe-name",
            WindowsQuote(SensorHostEndpoint.PipeName),
            "--log-path",
            WindowsQuote(Path.Combine(AppDataPaths.Logs, "sensor-host-setup.log")));
    }

    private static string WindowsQuote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

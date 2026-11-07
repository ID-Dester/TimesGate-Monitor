using System.IO.Pipes;
using System.Diagnostics;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TimesGateMonitor.Hardware.LibreHardwareMonitor;
using TimesGateMonitor.Hardware.SensorHost;

namespace TimesGateMonitor.SensorHost;

internal static class Program
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    public static int Main(string[] args)
    {
        try
        {
            var options = CommandLineOptions.Parse(args);
            SetupLog.Configure(options.LogPath);

            if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
            {
                return ScheduledTaskInstaller.InstallAndStart(options);
            }

            if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
            {
                return ScheduledTaskInstaller.Uninstall(options);
            }

            return args.Contains("--run", StringComparer.OrdinalIgnoreCase)
                ? RunServerAsync(options).GetAwaiter().GetResult()
                : 2;
        }
        catch (Exception exception)
        {
            SetupLog.Write("Unhandled sensor host error.", exception);
            return 1;
        }
    }

    private static async Task<int> RunServerAsync(CommandLineOptions options)
    {
        options.ValidateForTargetUser();
        SetupLog.Write($"Sensor reader starting for SID {options.TargetSid}; pipe {options.PipeName}.");
        using var monitor = new LibreHardwareMonitorService(
            NullLogger<LibreHardwareMonitorService>.Instance);
        monitor.Initialize();
        SetupLog.Write("Hardware monitor initialized.");

        while (true)
        {
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(options.TargetSid),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            await using var pipe = NamedPipeServerStreamAcl.Create(
                options.PipeName,
                PipeDirection.Out,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                pipeSecurity);
            using var idleSource = new CancellationTokenSource(IdleTimeout);

            try
            {
                await pipe.WaitForConnectionAsync(idleSource.Token);
            }
            catch (OperationCanceledException)
            {
                SetupLog.Write("Sensor reader stopped after two minutes without a client.");
                return 0;
            }

            var captureTask = Task.Run(monitor.CaptureSnapshot);
            if (await Task.WhenAny(captureTask, Task.Delay(CaptureTimeout)) != captureTask)
            {
                SetupLog.Write(
                    $"Hardware capture exceeded {CaptureTimeout.TotalSeconds:0} seconds. " +
                    "Stopping the reader so Task Scheduler can restart it.");
                Environment.Exit(3);
            }

            var snapshot = await captureTask;
            try
            {
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot));
            }
            catch (IOException)
            {
                // TGm may abandon a slow request and retry. This must not stop the reader.
            }
        }
    }

    private static class ScheduledTaskInstaller
    {
        public static int InstallAndStart(CommandLineOptions options)
        {
            options.ValidateForTargetUser();
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                SetupLog.Write("Installation failed because the executable path is unavailable.");
                return 1;
            }

            var workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            var runArguments = CommandLineOptions.BuildRunArguments(options);
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $taskName = '{{PowerShellLiteral(SensorHostEndpoint.ScheduledTaskName)}}'
                $executable = '{{PowerShellLiteral(executablePath)}}'
                $workingDirectory = '{{PowerShellLiteral(workingDirectory)}}'
                $userName = '{{PowerShellLiteral(options.TargetUser)}}'
                $userSid = '{{PowerShellLiteral(options.TargetSid)}}'
                $runArguments = '{{PowerShellLiteral(runArguments)}}'
                $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
                if ($null -ne $existing) {
                    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
                }
                Get-CimInstance Win32_Process -Filter "Name = 'TGm.SensorHost.exe'" -ErrorAction SilentlyContinue |
                    Where-Object { $_.CommandLine -match '(?i)--run' -and $_.CommandLine -like ('*' + $userSid + '*') } |
                    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                Start-Sleep -Milliseconds 300
                $action = New-ScheduledTaskAction -Execute $executable -Argument $runArguments -WorkingDirectory $workingDirectory
                $trigger = New-ScheduledTaskTrigger -AtLogOn -User $userName
                $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
                $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1)
                Register-ScheduledTask -TaskName $taskName -Description 'Optional elevated CPU sensor reader for TGm. Runs only while TGm requests data.' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
                $registered = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
                if ($registered.Actions.Execute -ne $executable) { throw 'The registered task points to an unexpected executable.' }
                $scheduler = New-Object -ComObject 'Schedule.Service'
                $scheduler.Connect()
                $task = $scheduler.GetFolder('\').GetTask('\' + $taskName)
                $task.SetSecurityDescriptor(('D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;' + $userSid + ')'), 0)
                Start-ScheduledTask -TaskName $taskName
                """;

            SetupLog.Write($"Installing SYSTEM task for {options.TargetUser} ({options.TargetSid}). Host: {executablePath}");
            var result = RunPowerShell(script);
            SetupLog.Write(
                $"Install finished with exit code {result.ExitCode}." +
                $"{Environment.NewLine}stdout: {result.StandardOutput.Trim()}" +
                $"{Environment.NewLine}stderr: {result.StandardError.Trim()}");
            return result.ExitCode;
        }

        public static int Uninstall(CommandLineOptions options)
        {
            options.ValidateForTargetUser();
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $taskName = '{{PowerShellLiteral(SensorHostEndpoint.ScheduledTaskName)}}'
                $userSid = '{{PowerShellLiteral(options.TargetSid)}}'
                $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
                if ($null -ne $task) {
                    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
                }
                Get-CimInstance Win32_Process -Filter "Name = 'TGm.SensorHost.exe'" -ErrorAction SilentlyContinue |
                    Where-Object { $_.CommandLine -match '(?i)--run' -and $_.CommandLine -like ('*' + $userSid + '*') } |
                    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                if ($null -ne $task) {
                    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
                }
                """;
            var result = RunPowerShell(script);
            SetupLog.Write(
                $"Uninstall finished with exit code {result.ExitCode}." +
                $"{Environment.NewLine}stdout: {result.StandardOutput.Trim()}" +
                $"{Environment.NewLine}stderr: {result.StandardError.Trim()}");
            return result.ExitCode;
        }

        private static ProcessResult RunPowerShell(string script)
        {
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-EncodedCommand",
                    encodedCommand
                }
            }) ?? throw new InvalidOperationException("Could not start Windows PowerShell.");

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, standardOutput, standardError);
        }

        private static string PowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    }

    private sealed record CommandLineOptions(
        string TargetUser,
        string TargetSid,
        string PipeName,
        string LogPath)
    {
        public static CommandLineOptions Parse(string[] args) => new(
            Read(args, "--target-user"),
            Read(args, "--target-sid"),
            Read(args, "--pipe-name"),
            Read(args, "--log-path"));

        public void ValidateForTargetUser()
        {
            if (string.IsNullOrWhiteSpace(TargetUser) ||
                string.IsNullOrWhiteSpace(TargetSid) ||
                string.IsNullOrWhiteSpace(PipeName))
            {
                throw new ArgumentException("Target user, SID and pipe name are required.");
            }

            _ = new SecurityIdentifier(TargetSid);
            if (!PipeName.StartsWith("TGm.SensorHost.", StringComparison.Ordinal) ||
                PipeName.IndexOfAny(['\\', '/', ':']) >= 0)
            {
                throw new ArgumentException("The sensor pipe name is invalid.");
            }
        }

        public static string BuildRunArguments(CommandLineOptions options) => string.Join(
            ' ',
            "--run",
            "--target-user",
            WindowsQuote(options.TargetUser),
            "--target-sid",
            WindowsQuote(options.TargetSid),
            "--pipe-name",
            WindowsQuote(options.PipeName),
            "--log-path",
            WindowsQuote(options.LogPath));

        private static string Read(string[] args, string name)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return string.Empty;
        }

        private static string WindowsQuote(string value) =>
            $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static class SetupLog
    {
        private static string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TGm",
            "Logs",
            "sensor-host-setup.log");

        public static void Configure(string logPath)
        {
            if (!string.IsNullOrWhiteSpace(logPath) && Path.IsPathFullyQualified(logPath))
            {
                _logPath = logPath;
            }
        }

        public static void Write(string message, Exception? exception = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                var details = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
                File.AppendAllText(
                    _logPath,
                    $"[{DateTimeOffset.Now:O}] {message}{details}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never prevent the sensor host from exiting cleanly.
            }
        }
    }
}

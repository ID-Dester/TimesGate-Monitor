using TimesGateMonitor.App.Infrastructure;
using System.IO;
using System.IO.Compression;

namespace TimesGateMonitor.Tests.App;

internal static class ApplicationLifecycleTests
{
    public static void Run()
    {
        var preferences = new ApplicationPreferences
        {
            MinimizeToTray = true,
            StartMinimized = true,
            StartWithWindows = false,
            AutoReconnect = true,
            SensorRefreshSeconds = 20,
            CpuSensorPromptCompleted = true,
            CpuSensorHostEnabled = true
        };
        var clone = preferences.Clone();
        clone.AutoReconnect = false;
        TestAssert.True(preferences.AutoReconnect, "Cloning preferences must isolate later edits.");
        TestAssert.True(clone.CpuSensorHostEnabled, "CPU sensor preference must be cloned.");
        TestAssert.Equal(20, clone.SensorRefreshSeconds, "Sensor refresh preference must be cloned.");

        TestAssert.True(
            !ConnectionSettingsValidator.TryParseLocalToken(string.Empty, out _, out var emptyError) &&
            emptyError.Contains("required", StringComparison.OrdinalIgnoreCase),
            "An empty LocalToken must produce an explicit required-field error.");
        TestAssert.True(
            ConnectionSettingsValidator.TryParseLocalToken("123456", out var token, out _) && token == 123456,
            "A numeric LocalToken must be accepted.");

        var encodedToken = LocalTokenCredentialStore.Encode("123456");
        TestAssert.Equal(
            "123456",
            LocalTokenCredentialStore.Decode(encodedToken),
            "Credential Manager token encoding must round-trip.");

        var sanitized = DiagnosticBundleExporter.SanitizeText(
            @"POST 192.0.2.181 ""LocalToken"": ""123456"" C:\Users\Alice\AppData\Local\TGm");
        TestAssert.True(
            sanitized.Contains("192.0.2.x", StringComparison.Ordinal) &&
            sanitized.Contains("LocalToken", StringComparison.Ordinal) &&
            sanitized.Contains("<redacted>", StringComparison.Ordinal) &&
            sanitized.Contains(@"%USERPROFILE%\AppData", StringComparison.Ordinal) &&
            !sanitized.Contains("123456", StringComparison.Ordinal) &&
            !sanitized.Contains("Alice", StringComparison.Ordinal),
            "Diagnostic export must redact tokens, IP suffixes and user-profile names.");

        var diagnosticPath = Path.Combine(
            Path.GetTempPath(),
            $"tgm-diagnostics-test-{Guid.NewGuid():N}.zip");
        try
        {
            DiagnosticBundleExporter.Export(diagnosticPath, preferences);
            using var archive = ZipFile.OpenRead(diagnosticPath);
            TestAssert.True(
                archive.GetEntry("report.txt") is not null &&
                archive.GetEntry("preferences.json") is not null,
                "Diagnostic export must contain its report and sanitized preferences.");
            var archiveText = string.Join(
                Environment.NewLine,
                archive.Entries.Select(ReadTextEntry));
            TestAssert.True(
                !archiveText.Contains("123456", StringComparison.Ordinal),
                "Diagnostic archives must not contain known test tokens.");
        }
        finally
        {
            File.Delete(diagnosticPath);
        }

        using var tray = new TrayIconService();
        tray.SetStatus(new string('X', 100));
    }

    private static string ReadTextEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}

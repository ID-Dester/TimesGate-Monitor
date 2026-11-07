using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TimesGateMonitor.App.Infrastructure;

internal static partial class DiagnosticBundleExporter
{
    private const int MaximumLogBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    public static void Export(string destinationPath, ApplicationPreferences preferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(preferences);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var temporaryPath = destinationPath + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "report.txt", BuildReport(preferences));
                WriteEntry(archive, "preferences.json", BuildPreferences(preferences));
                TryAddSanitizedDashboard(archive);
                TryAddSanitizedLogs(archive);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static string SanitizeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sanitized = UserProfileRegex().Replace(value, "%USERPROFILE%");
        sanitized = TokenPropertyRegex().Replace(sanitized, "$1<redacted>");
        return Ipv4Regex().Replace(
            sanitized,
            match => RedactIpv4(match.Value));
    }

    internal static string RedactIpv4(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}.x" : "<redacted-ip>";
    }

    private static string BuildReport(ApplicationPreferences preferences)
    {
        var assembly = typeof(DiagnosticBundleExporter).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var lines = new[]
        {
            "TGm sanitized diagnostic report",
            $"Created: {DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)}",
            $"TGm version: {version}",
            $"OS: {Environment.OSVersion.VersionString}",
            $"Process architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $".NET runtime: {Environment.Version}",
            $"Sensor host available: {SensorHostManager.IsAvailable}",
            $"Sensor host installed: {SensorHostManager.IsInstalled()}",
            $"Automatic reconnect: {preferences.AutoReconnect}",
            $"Minimum sensor refresh: {preferences.SensorRefreshSeconds} seconds",
            $"Start with Windows: {preferences.StartWithWindows}",
            string.Empty,
            "LocalToken is intentionally excluded. IP addresses, user-profile paths and imported-media paths are redacted."
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildPreferences(ApplicationPreferences preferences) =>
        JsonSerializer.Serialize(
            new
            {
                preferences.Version,
                preferences.MinimizeToTray,
                preferences.StartMinimized,
                preferences.StartWithWindows,
                preferences.AutoReconnect,
                preferences.SensorRefreshSeconds,
                preferences.CpuSensorPromptCompleted,
                preferences.CpuSensorHostEnabled,
                preferences.CpuSensorSetupVersion
            },
            IndentedJsonOptions);

    private static void TryAddSanitizedDashboard(ZipArchive archive)
    {
        try
        {
            if (!File.Exists(AppDataPaths.Dashboard))
            {
                return;
            }

            var root = JsonNode.Parse(File.ReadAllText(AppDataPaths.Dashboard));
            SanitizeJson(root);
            WriteEntry(
                archive,
                "dashboard.sanitized.json",
                root?.ToJsonString(IndentedJsonOptions) ?? "{}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            WriteEntry(archive, "dashboard-error.txt", exception.GetType().Name);
        }
    }

    private static void SanitizeJson(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (property.Key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Contains("secret", StringComparison.OrdinalIgnoreCase))
                {
                    jsonObject[property.Key] = "<redacted>";
                    continue;
                }

                if (property.Key.EndsWith("Ip", StringComparison.OrdinalIgnoreCase) &&
                    property.Value is JsonValue ipValue &&
                    ipValue.TryGetValue<string>(out var ip))
                {
                    jsonObject[property.Key] = RedactIpv4(ip);
                    continue;
                }

                if (property.Key.Contains("MediaPath", StringComparison.OrdinalIgnoreCase) &&
                    property.Value is not null)
                {
                    jsonObject[property.Key] = "<configured-media>";
                    continue;
                }

                SanitizeJson(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                SanitizeJson(child);
            }
        }
    }

    private static void TryAddSanitizedLogs(ZipArchive archive)
    {
        try
        {
            if (!Directory.Exists(AppDataPaths.Logs))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(AppDataPaths.Logs, "*.log*")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(5))
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var start = Math.Max(0, stream.Length - MaximumLogBytes);
                stream.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: false);
                var text = reader.ReadToEnd();
                WriteEntry(archive, $"logs/{Path.GetFileName(path)}", SanitizeText(text));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteEntry(archive, "logs/error.txt", exception.GetType().Name);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    [GeneratedRegex("""(?i)(["']?(?:LocalToken|token|password|secret)["']?\s*[:=]\s*)["']?[^\s,;}]+""")]
    private static partial Regex TokenPropertyRegex();

    [GeneratedRegex(@"(?<!\d)(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}(?!\d)")]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"(?i)[A-Z]:\\Users\\[^\\\s]+")]
    private static partial Regex UserProfileRegex();
}

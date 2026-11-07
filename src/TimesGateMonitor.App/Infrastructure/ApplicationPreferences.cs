using System.IO;
using System.Text.Json;

namespace TimesGateMonitor.App.Infrastructure;

internal sealed class ApplicationPreferences
{
    public int Version { get; set; } = 1;

    public bool MinimizeToTray { get; set; } = true;

    public bool StartMinimized { get; set; }

    public bool StartWithWindows { get; set; }

    public bool AutoReconnect { get; set; } = true;

    public int SensorRefreshSeconds { get; set; } = 5;

    public bool CpuSensorPromptCompleted { get; set; }

    public bool CpuSensorHostEnabled { get; set; }

    public int CpuSensorSetupVersion { get; set; }

    public ApplicationPreferences Clone() => new()
    {
        Version = Version,
        MinimizeToTray = MinimizeToTray,
        StartMinimized = StartMinimized,
        StartWithWindows = StartWithWindows,
        AutoReconnect = AutoReconnect,
        SensorRefreshSeconds = SensorRefreshSeconds,
        CpuSensorPromptCompleted = CpuSensorPromptCompleted,
        CpuSensorHostEnabled = CpuSensorHostEnabled,
        CpuSensorSetupVersion = CpuSensorSetupVersion
    };
}

internal static class ApplicationPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ApplicationPreferences Load()
    {
        try
        {
            if (!File.Exists(AppDataPaths.Preferences))
            {
                return new ApplicationPreferences { StartWithWindows = AutostartService.IsEnabled() };
            }

            var preferences = JsonSerializer.Deserialize<ApplicationPreferences>(
                File.ReadAllText(AppDataPaths.Preferences),
                Options);
            if (preferences?.Version != 1)
            {
                return new ApplicationPreferences { StartWithWindows = AutostartService.IsEnabled() };
            }

            preferences.StartWithWindows = AutostartService.IsEnabled();
            preferences.SensorRefreshSeconds = Math.Clamp(preferences.SensorRefreshSeconds, 5, 30);
            return preferences;
        }
        catch (IOException)
        {
            return new ApplicationPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new ApplicationPreferences();
        }
        catch (JsonException)
        {
            return new ApplicationPreferences();
        }
    }

    public static void Save(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(AppDataPaths.Root);
        var temporaryPath = $"{AppDataPaths.Preferences}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, Options));
        File.Move(temporaryPath, AppDataPaths.Preferences, overwrite: true);
    }
}

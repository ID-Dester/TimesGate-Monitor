using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimesGateMonitor.App.Infrastructure;

namespace TimesGateMonitor.App.Dashboard;

internal sealed class DashboardUserConfiguration
{
    [JsonRequired]
    public int Version { get; set; } = 2;

    public string DeviceIp { get; set; } = string.Empty;

    public string ServeIp { get; set; } = LocalIpv4AddressDetector.TryGetPreferredAddress() ?? string.Empty;

    public int Brightness { get; set; } = 70;

    public DashboardScreenConfiguration[] Screens { get; set; } = DashboardModuleCatalog.CreateEmptyScreens();
}

internal static class DashboardConfigurationStore
{
    private static readonly HashSet<string> SupportedMediaExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigurationPath { get; } = AppDataPaths.Dashboard;

    public static DashboardUserConfiguration? TryLoad()
    {
        try
        {
            if (!File.Exists(ConfigurationPath))
            {
                return null;
            }

            var json = File.ReadAllText(ConfigurationPath);
            var configuration = JsonSerializer.Deserialize<DashboardUserConfiguration>(json, SerializerOptions);
            NormalizeLegacyTitles(configuration);
            return IsValid(configuration) ? configuration : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public static void Save(DashboardUserConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var directory = Path.GetDirectoryName(ConfigurationPath)
            ?? throw new InvalidOperationException("Configuration directory is unavailable.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            ConfigurationPath,
            JsonSerializer.Serialize(configuration, SerializerOptions));
    }

    public static string ImportMedia(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected media file does not exist.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedMediaExtensions.Contains(extension))
        {
            throw new InvalidDataException("Supported media formats: PNG, JPG, JPEG, BMP and GIF.");
        }

        var mediaDirectory = Path.Combine(AppDataPaths.DashboardRoot, "Media");
        Directory.CreateDirectory(mediaDirectory);
        var destination = Path.Combine(mediaDirectory, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }

    private static bool IsValid(DashboardUserConfiguration? configuration)
    {
        if (configuration?.Version != 2 ||
            configuration.Screens.Length != 5 ||
            configuration.Brightness is < 0 or > 100)
        {
            return false;
        }

        foreach (var screen in configuration.Screens)
        {
            if (screen.Slots.Length != 3 ||
                string.IsNullOrWhiteSpace(screen.Title) ||
                !IsHexColor(screen.FrameColorHex) ||
                !IsHexColor(screen.TextColorHex) ||
                !DashboardHeaderIconCatalog.All.Any(icon => icon.Key == screen.HeaderIconKey))
            {
                return false;
            }

            foreach (var assignment in screen.Slots.Where(assignment => assignment is not null))
            {
                _ = DashboardWidgetCatalog.GetRequired(assignment!.WidgetKey);
            }
        }

        return true;
    }

    private static bool IsHexColor(string? value) =>
        value is not null &&
        value.Length == 7 &&
        value[0] == '#' &&
        int.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _);

    private static void NormalizeLegacyTitles(DashboardUserConfiguration? configuration)
    {
        if (configuration is null)
        {
            return;
        }

        foreach (var screen in configuration.Screens)
        {
            if (string.Equals(screen.HeaderIconKey, "helldiver", StringComparison.Ordinal))
            {
                screen.HeaderIconKey = "terminal";
            }

            screen.Title = (screen.WidgetKey, screen.Title) switch
            {
                ("gpu", "GPU_MON") => "GPU",
                ("cpu", "CPU_MON") => "CPU",
                ("ram", "RAM_MON") => "RAM",
                ("clock_large", "BIG_CLOCK") => "SMALL_CLOCK",
                _ => screen.Title
            };
        }
    }
}

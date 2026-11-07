using System.Globalization;
using TimesGateMonitor.Core.Formatting;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.App.Dashboard;

internal static class DashboardWidgetValueFormatter
{
    public static string Format(
        string widgetKey,
        DashboardWidgetDisplayMode displayMode,
        HardwareSnapshot snapshot) => widgetKey switch
    {
        "gpu_load" => displayMode == DashboardWidgetDisplayMode.Bar
            ? CompactBar("GPU", snapshot.GpuLoadPercent)
            : $"GPU: {NetworkTextValueFormatter.Percent(snapshot.GpuLoadPercent)}",
        "gpu_temp" => $"TEMP: {Temperature(snapshot.GpuTemperatureCelsius)}C",
        "gpu_power" => $"TDP: {Power(snapshot.GpuPowerWatts)}",
        "cpu_load" => displayMode == DashboardWidgetDisplayMode.Bar
            ? CompactBar("CPU", snapshot.CpuLoadPercent)
            : $"CPU: {NetworkTextValueFormatter.Percent(snapshot.CpuLoadPercent)}",
        "cpu_temp" => $"TEMP: {Temperature(snapshot.CpuTemperatureCelsius)}C",
        "cpu_power" => $"TDP: {Power(snapshot.CpuPowerWatts)}",
        "cpu_summary" => LoadAndTemperature("CPU", snapshot.CpuLoadPercent, snapshot.CpuTemperatureCelsius),
        "gpu_summary" => LoadAndTemperature("GPU", snapshot.GpuLoadPercent, snapshot.GpuTemperatureCelsius),
        "ram_load" => displayMode == DashboardWidgetDisplayMode.Bar
            ? CompactBar("RAM", snapshot.RamLoadPercent)
            : $"RAM: {NetworkTextValueFormatter.Percent(snapshot.RamLoadPercent)}",
        "ram_used" => $"RAM: {Gigabytes(snapshot.RamUsedGigabytes)}/{Gigabytes(snapshot.RamTotalGigabytes)}",
        "ram_free" => $"FREE {Gigabytes(snapshot.RamAvailableGigabytes)}G",
        "clock" => $"TIME: {DateTime.Now:HH:mm}",
        "date" => $"DATE: {DateTime.Now:dd.MM.yyyy}",
        "uptime" => $"UP {Uptime(snapshot.Uptime)}",
        "temperatures" =>
            $"TEMP C{Temperature(snapshot.CpuTemperatureCelsius)} G{Temperature(snapshot.GpuTemperatureCelsius)}",
        _ => "--"
    };

    public static string LargeClock(bool showSeconds = false) => DateTime.Now.ToString(
        showSeconds ? "HH:mm:ss" : "HH:mm",
        CultureInfo.InvariantCulture);

    private static string LoadAndTemperature(string label, double? load, double? temperature) =>
        $"{label}: {NetworkTextValueFormatter.Percent(load)} {Temperature(temperature)}C";

    private static string CompactBar(string label, double? value)
    {
        const int segments = 6;
        if (!value.HasValue)
        {
            return $"{label} [??????]";
        }

        var clamped = Math.Clamp(value.Value, 0, 100);
        var filled = (int)Math.Round(clamped * segments / 100d, MidpointRounding.AwayFromZero);
        return $"{label} [{new string('■', filled)}{new string('□', segments - filled)}]";
    }

    private static string Temperature(double? value) => value.HasValue
        ? Math.Round(value.Value).ToString("0", CultureInfo.InvariantCulture)
        : "--";

    private static string Power(double? watts) => watts.HasValue
        ? $"{watts.Value.ToString("0", CultureInfo.InvariantCulture)} W"
        : "-- W";

    private static string Gigabytes(double? value) => value.HasValue
        ? value.Value.ToString("0.0", CultureInfo.InvariantCulture)
        : "--";

    private static string Uptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays}D{uptime.Hours:00}H"
        : $"{uptime.Hours:00}:{uptime.Minutes:00}";
}

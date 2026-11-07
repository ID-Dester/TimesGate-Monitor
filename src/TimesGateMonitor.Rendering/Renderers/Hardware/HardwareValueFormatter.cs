using System.Globalization;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

internal static class HardwareValueFormatter
{
    public static string Percent(double? value) => value.HasValue
        ? $"{Math.Round(Math.Clamp(value.Value, 0, 100)):0}%"
        : "--";

    public static string Temperature(double? value) => value.HasValue
        ? $"{Math.Round(value.Value):0}°C"
        : "--";

    public static string CompactTemperature(double? value) => value.HasValue
        ? $"{Math.Round(value.Value):0}°"
        : "--";

    public static string Clock(double? megahertz) => megahertz.HasValue
        ? megahertz.Value >= 1000
            ? $"{(megahertz.Value / 1000d).ToString("0.0", CultureInfo.InvariantCulture)} GHz"
            : $"{megahertz.Value.ToString("0", CultureInfo.InvariantCulture)} MHz"
        : "--";

    public static string Power(double? watts) => watts.HasValue
        ? $"{watts.Value.ToString("0", CultureInfo.InvariantCulture)} W"
        : "--";

    public static string Gigabytes(double? value) => value.HasValue
        ? $"{value.Value.ToString("0.0", CultureInfo.InvariantCulture)} GB"
        : "--";

    public static string RatioGigabytes(double? used, double? total) =>
        used.HasValue && total.HasValue
            ? $"{used.Value.ToString("0.0", CultureInfo.InvariantCulture)} / {total.Value.ToString("0", CultureInfo.InvariantCulture)} GB"
            : "-- / -- GB";

    public static string Uptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays}d {uptime.Hours:00}h"
        : $"{uptime.Hours:00}:{uptime.Minutes:00}";

}

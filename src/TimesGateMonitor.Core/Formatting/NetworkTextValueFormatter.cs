using System.Globalization;

namespace TimesGateMonitor.Core.Formatting;

public static class NetworkTextValueFormatter
{
    private const int BarSegments = 10;

    public static string Percent(double? value) => value.HasValue
        ? $"{Math.Round(Math.Clamp(value.Value, 0, 100)).ToString("0", CultureInfo.InvariantCulture)}%"
        : "--";

    public static string Temperature(double? value) => value.HasValue
        ? $"{Math.Round(value.Value).ToString("0", CultureInfo.InvariantCulture)}C"
        : "--";

    public static string SquareBar(double? value)
    {
        if (!value.HasValue)
        {
            return "[??????????]";
        }

        var filled = (int)Math.Round(
            Math.Clamp(value.Value, 0, 100) * BarSegments / 100d,
            MidpointRounding.AwayFromZero);

        return $"[{new string('■', filled)}{new string('□', BarSegments - filled)}]";
    }
}

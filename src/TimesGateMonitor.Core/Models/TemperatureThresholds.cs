namespace TimesGateMonitor.Core.Models;

public sealed record TemperatureThresholds
{
    public static TemperatureThresholds CpuDefault { get; } = new(70, 85);

    public static TemperatureThresholds GpuDefault { get; } = new(70, 80);

    public TemperatureThresholds(double warmAtCelsius, double hotAtCelsius)
    {
        if (!double.IsFinite(warmAtCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(warmAtCelsius));
        }

        if (!double.IsFinite(hotAtCelsius) || hotAtCelsius <= warmAtCelsius)
        {
            throw new ArgumentOutOfRangeException(nameof(hotAtCelsius));
        }

        WarmAtCelsius = warmAtCelsius;
        HotAtCelsius = hotAtCelsius;
    }

    public double WarmAtCelsius { get; }

    public double HotAtCelsius { get; }

    public TemperatureLevel Classify(double? temperatureCelsius)
    {
        if (!temperatureCelsius.HasValue || !double.IsFinite(temperatureCelsius.Value))
        {
            return TemperatureLevel.Unavailable;
        }

        if (temperatureCelsius.Value >= HotAtCelsius)
        {
            return TemperatureLevel.Hot;
        }

        return temperatureCelsius.Value >= WarmAtCelsius
            ? TemperatureLevel.Warm
            : TemperatureLevel.Normal;
    }
}

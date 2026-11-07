using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Tests.Monitoring;

internal static class TemperatureThresholdTests
{
    public static void Run()
    {
        TestAssert.Equal(
            TemperatureLevel.Unavailable,
            TemperatureThresholds.CpuDefault.Classify(null),
            "Missing temperature must be unavailable.");
        TestAssert.Equal(
            TemperatureLevel.Normal,
            TemperatureThresholds.CpuDefault.Classify(69.9),
            "CPU temperature below 70 C must be normal.");
        TestAssert.Equal(
            TemperatureLevel.Warm,
            TemperatureThresholds.CpuDefault.Classify(70),
            "CPU temperature at 70 C must be warm.");
        TestAssert.Equal(
            TemperatureLevel.Hot,
            TemperatureThresholds.CpuDefault.Classify(85),
            "CPU temperature at 85 C must be hot.");
        TestAssert.Equal(
            TemperatureLevel.Hot,
            TemperatureThresholds.GpuDefault.Classify(80),
            "GPU temperature at 80 C must be hot.");
    }
}

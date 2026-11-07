using TimesGateMonitor.Hardware.Windows;

namespace TimesGateMonitor.Tests.Hardware;

internal static class WindowsCpuLoadSamplerTests
{
    public static void Run()
    {
        var previous = new WindowsCpuLoadSampler.SystemTimesSample(
            Idle: 100,
            Kernel: 500,
            User: 300);
        var current = new WindowsCpuLoadSampler.SystemTimesSample(
            Idle: 120,
            Kernel: 600,
            User: 350);

        var usage = WindowsCpuLoadSampler.CalculateUsage(previous, current);

        TestAssert.True(usage.HasValue, "A valid sample delta must produce CPU usage.");
        TestAssert.True(
            Math.Abs(usage.GetValueOrDefault() - 86.6666667d) < 0.0001d,
            "CPU usage calculation is incorrect.");

        var unchanged = WindowsCpuLoadSampler.CalculateUsage(current, current);
        TestAssert.True(unchanged is null, "An empty time interval must not produce CPU usage.");
    }
}

using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Core.Monitoring;

namespace TimesGateMonitor.Tests.Monitoring;

internal static class ExponentialSmootherTests
{
    public static void Run()
    {
        var smoother = new ExponentialSmoother(0.3);

        TestAssert.Equal(70d, smoother.Update(70)!.Value, "First sample must pass through.");
        TestAssert.True(
            Math.Abs(smoother.Update(100)!.Value - 79d) < 0.0001,
            "Smoother must use the configured new-value weight.");
        TestAssert.True(smoother.Update(null) is null, "Unavailable input must remain unavailable.");

        var snapshotSmoother = new HardwareSnapshotSmoother(1);
        var result = snapshotSmoother.Apply(new HardwareSnapshot
        {
            CpuLoadPercent = 130,
            GpuLoadPercent = -10,
            RamLoadPercent = 55
        });

        TestAssert.Equal(100d, result.CpuLoadPercent!.Value, "CPU load must be clamped.");
        TestAssert.Equal(0d, result.GpuLoadPercent!.Value, "GPU load must be clamped.");
        TestAssert.Equal(55d, result.RamLoadPercent!.Value, "Valid RAM load must be preserved.");
    }
}

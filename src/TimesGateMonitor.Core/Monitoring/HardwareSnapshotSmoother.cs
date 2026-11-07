using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Core.Monitoring;

public sealed class HardwareSnapshotSmoother
{
    private readonly ExponentialSmoother _cpuLoad;
    private readonly ExponentialSmoother _gpuLoad;
    private readonly ExponentialSmoother _ramLoad;

    public HardwareSnapshotSmoother(double newValueWeight = 0.3)
    {
        _cpuLoad = new ExponentialSmoother(newValueWeight);
        _gpuLoad = new ExponentialSmoother(newValueWeight);
        _ramLoad = new ExponentialSmoother(newValueWeight);
    }

    public HardwareSnapshot Apply(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            CpuLoadPercent = ClampPercent(_cpuLoad.Update(snapshot.CpuLoadPercent)),
            GpuLoadPercent = ClampPercent(_gpuLoad.Update(snapshot.GpuLoadPercent)),
            RamLoadPercent = ClampPercent(_ramLoad.Update(snapshot.RamLoadPercent))
        };
    }

    public void Reset()
    {
        _cpuLoad.Reset();
        _gpuLoad.Reset();
        _ramLoad.Reset();
    }

    private static double? ClampPercent(double? value) =>
        value.HasValue ? Math.Clamp(value.Value, 0, 100) : null;
}

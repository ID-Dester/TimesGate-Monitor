using TimesGateMonitor.Tests.App;
using TimesGateMonitor.Tests.Formatting;
using TimesGateMonitor.Tests.Hardware;
using TimesGateMonitor.Tests.Monitoring;
using TimesGateMonitor.Tests.Rendering;
using TimesGateMonitor.Tests.TimesGate;

namespace TimesGateMonitor.Tests;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            (nameof(DashboardModuleCatalogTests), () => RunSync(DashboardModuleCatalogTests.Run)),
            (nameof(ApplicationLifecycleTests), () => RunSync(ApplicationLifecycleTests.Run)),
            (nameof(LocalIpv4AddressDetectorTests), () => RunSync(LocalIpv4AddressDetectorTests.Run)),
            (nameof(ReconnectBackoffTests), () => RunSync(ReconnectBackoffTests.Run)),
            (nameof(ScreenApplyPolicyTests), () => RunSync(ScreenApplyPolicyTests.Run)),
            (nameof(ScreenHeartbeatPolicyTests), () => RunSync(ScreenHeartbeatPolicyTests.Run)),
            (nameof(DiagnosticFrameRendererTests), () => RunSync(DiagnosticFrameRendererTests.Run)),
            (nameof(HardwareDashboardRendererTests), () => RunSync(HardwareDashboardRendererTests.Run)),
            (nameof(ExponentialSmootherTests), () => RunSync(ExponentialSmootherTests.Run)),
            (nameof(TemperatureThresholdTests), () => RunSync(TemperatureThresholdTests.Run)),
            (nameof(NetworkTextValueFormatterTests), () => RunSync(NetworkTextValueFormatterTests.Run)),
            (nameof(HardwareSnapshotMapperTests), () => RunSync(HardwareSnapshotMapperTests.Run)),
            (nameof(WindowsCpuLoadSamplerTests), () => RunSync(WindowsCpuLoadSamplerTests.Run)),
            (nameof(SensorHostClientTests), () => RunSync(SensorHostClientTests.Run)),
            (nameof(SensorHostRecoveryPolicyTests), () => RunSync(SensorHostRecoveryPolicyTests.Run)),
            (nameof(TimesGateConnectionTests), () => RunSync(TimesGateConnectionTests.Run)),
            (nameof(TimesGateServiceTests), TimesGateServiceTests.RunAsync),
            (nameof(DashboardRecoveryTests.FirstPartialApplyCanRecoverAsync), () => Task.Run(DashboardRecoveryTests.FirstPartialApplyCanRecoverAsync)),
            (nameof(DashboardRecoveryTests.FailedReapplyKeepsNewTargetAsync), () => Task.Run(DashboardRecoveryTests.FailedReapplyKeepsNewTargetAsync)),
            (nameof(DashboardRecoveryTests.FailedHealthCheckDoesNotStartRecoveryAsync), () => Task.Run(DashboardRecoveryTests.FailedHealthCheckDoesNotStartRecoveryAsync)),
            (nameof(DashboardRecoveryTests.CancelledFirstApplyDoesNotStartRecoveryAsync), () => Task.Run(DashboardRecoveryTests.CancelledFirstApplyDoesNotStartRecoveryAsync)),
            (nameof(DashboardRecoveryTests.UnconfirmedScreensRemainMonitoredAsync), () => Task.Run(DashboardRecoveryTests.UnconfirmedScreensRemainMonitoredAsync))
        };

        var failures = 0;

        foreach (var test in tests)
        {
            try
            {
                await test.Run().ConfigureAwait(false);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} unit test groups passed.");
        return failures == 0 ? 0 : 1;
    }

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

using TimesGateMonitor.Hardware.SensorHost;

namespace TimesGateMonitor.Tests.Hardware;

internal static class SensorHostRecoveryPolicyTests
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero);
        var policy = new SensorHostRecoveryPolicy();

        TestAssert.False(
            policy.RecordFailureAndShouldRequestStart(now),
            "One transient IPC miss must not restart the sensor host.");
        TestAssert.False(
            policy.RecordFailureAndShouldRequestStart(now.AddSeconds(1)),
            "Two transient IPC misses must not restart the sensor host.");
        TestAssert.True(
            policy.RecordFailureAndShouldRequestStart(now.AddSeconds(2)),
            "Three consecutive IPC misses must request a sensor-host restart.");
        TestAssert.False(
            policy.RecordFailureAndShouldRequestStart(now.AddSeconds(10)),
            "Restart requests must be rate limited while Task Scheduler reacts.");
        TestAssert.True(
            policy.RecordFailureAndShouldRequestStart(
                now.AddSeconds(2) + SensorHostRecoveryPolicy.RestartCooldown),
            "A dead sensor host must be requested again after the cooldown.");

        policy.RecordSuccess();
        TestAssert.Equal(0, policy.ConsecutiveFailures, "A recovered host must reset its failure streak.");
    }
}

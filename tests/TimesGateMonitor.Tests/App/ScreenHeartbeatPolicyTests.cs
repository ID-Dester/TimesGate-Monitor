using TimesGateMonitor.App.Dashboard;

namespace TimesGateMonitor.Tests.App;

internal static class ScreenHeartbeatPolicyTests
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        TestAssert.False(
            ScreenHeartbeatPolicy.IsSilent(now - TimeSpan.FromSeconds(89), now),
            "A screen must not be restored before three heartbeat periods have elapsed.");
        TestAssert.True(
            ScreenHeartbeatPolicy.IsSilent(now - TimeSpan.FromSeconds(90), now),
            "A screen must be considered silent after three heartbeat periods.");
        TestAssert.True(
            ScreenHeartbeatPolicy.IsSilent(null, now),
            "A configured screen with no requests must be considered silent.");

        TestAssert.True(
            ScreenHeartbeatPolicy.IsFullDashboardSilent(
            [
                now - TimeSpan.FromSeconds(84),
                now - TimeSpan.FromSeconds(89),
                now - TimeSpan.FromSeconds(82),
                now - TimeSpan.FromSeconds(86),
                now - TimeSpan.FromSeconds(90)
            ],
            now),
            "A tightly grouped five-LCD outage must be treated as one device reset before serial recovery starts.");
        TestAssert.False(
            ScreenHeartbeatPolicy.IsFullDashboardSilent(
            [
                now - TimeSpan.FromSeconds(84),
                now - TimeSpan.FromSeconds(20),
                now - TimeSpan.FromSeconds(82)
            ],
            now),
            "One active LCD must prevent full-dashboard reset detection.");

        TestAssert.False(
            ScreenHeartbeatPolicy.CanRestore(now - TimeSpan.FromMinutes(4), now),
            "The restore cooldown must prevent a configuration loop.");
        TestAssert.True(
            ScreenHeartbeatPolicy.CanRestore(now - TimeSpan.FromMinutes(5), now),
            "A screen must become eligible after the restore cooldown.");

        TestAssert.True(
            ScreenHeartbeatPolicy.ShouldInspectSingleSilentScreen(
                restoreFullDashboard: false,
                staleScreenIndex: 0),
            "A genuinely stale LCD must still use selective recovery diagnostics.");

        var estimatedSilenceStart = ScreenHeartbeatPolicy.EstimateSilenceStartedUtc(
        [
            now - TimeSpan.FromMinutes(3),
            null,
            now - TimeSpan.FromMinutes(2),
            now - TimeSpan.FromMinutes(4)
        ]);
        TestAssert.True(
            estimatedSilenceStart.HasValue,
            "An outage with known requests should have an estimated start time.");
        TestAssert.Equal(
            now - TimeSpan.FromMinutes(2),
            estimatedSilenceStart!.Value,
            "The latest known LCD request should estimate when the full-device interruption began.");
        TestAssert.True(
            ScreenHeartbeatPolicy.EstimateSilenceStartedUtc([null, null]) is null,
            "An outage without known requests should have no estimated start time.");
    }
}

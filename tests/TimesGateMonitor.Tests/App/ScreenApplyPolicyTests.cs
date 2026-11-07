using TimesGateMonitor.App.Dashboard;

namespace TimesGateMonitor.Tests.App;

internal static class ScreenApplyPolicyTests
{
    public static void Run()
    {
        TestAssert.False(
            ScreenApplyPolicy.ShouldApply(forceApply: false, configurationMatches: true),
            "An unchanged screen must not be resent during a normal apply.");
        TestAssert.True(
            ScreenApplyPolicy.ShouldApply(forceApply: false, configurationMatches: false),
            "A changed screen must be sent during a normal apply.");
        TestAssert.True(
            ScreenApplyPolicy.ShouldApply(forceApply: true, configurationMatches: true),
            "A forced recovery must resend a screen without invalidating its last known configuration first.");
    }
}

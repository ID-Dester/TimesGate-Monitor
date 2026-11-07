namespace TimesGateMonitor.App.Dashboard;

internal static class ScreenApplyPolicy
{
    public static bool ShouldApply(bool forceApply, bool configurationMatches) =>
        forceApply || !configurationMatches;
}

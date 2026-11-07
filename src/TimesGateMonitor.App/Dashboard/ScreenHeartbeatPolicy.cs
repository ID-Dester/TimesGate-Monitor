namespace TimesGateMonitor.App.Dashboard;

internal static class ScreenHeartbeatPolicy
{
    public const int HeartbeatUpdateSeconds = 30;

    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan JournalInterval = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(90);

    public static readonly TimeSpan RestoreCooldown = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan AddressChangeGracePeriod = TimeSpan.FromSeconds(30);

    public static bool IsSilent(DateTimeOffset? lastRequestUtc, DateTimeOffset nowUtc) =>
        lastRequestUtc is null || nowUtc - lastRequestUtc.Value >= SilenceTimeout;

    public static bool IsFullDashboardSilent(
        IReadOnlyCollection<DateTimeOffset?> lastRequestTimesUtc,
        DateTimeOffset nowUtc) =>
        lastRequestTimesUtc.Count > 0 &&
        lastRequestTimesUtc.All(lastRequestUtc =>
            lastRequestUtc is null ||
            nowUtc - lastRequestUtc.Value >= SilenceTimeout - CheckInterval);

    public static bool CanRestore(DateTimeOffset? lastRestoreUtc, DateTimeOffset nowUtc) =>
        lastRestoreUtc is null || nowUtc - lastRestoreUtc.Value >= RestoreCooldown;

    public static bool ShouldInspectSingleSilentScreen(
        bool restoreFullDashboard,
        int staleScreenIndex) =>
        !restoreFullDashboard &&
        staleScreenIndex >= 0;

    public static DateTimeOffset? EstimateSilenceStartedUtc(
        IEnumerable<DateTimeOffset?> lastRequestTimesUtc)
    {
        var knownRequestTimes = lastRequestTimesUtc
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return knownRequestTimes.Length == 0 ? null : knownRequestTimes.Max();
    }
}

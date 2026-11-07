using TimesGateMonitor.App.Dashboard;

namespace TimesGateMonitor.Tests.App;

internal static class ReconnectBackoffTests
{
    public static void Run()
    {
        var backoff = new ReconnectBackoff();
        TestAssert.Equal(TimeSpan.FromSeconds(5), backoff.NextDelay(), "First reconnect delay is incorrect.");
        TestAssert.Equal(TimeSpan.FromSeconds(10), backoff.NextDelay(), "Second reconnect delay is incorrect.");
        TestAssert.Equal(TimeSpan.FromSeconds(20), backoff.NextDelay(), "Third reconnect delay is incorrect.");
        TestAssert.Equal(TimeSpan.FromSeconds(30), backoff.NextDelay(), "Reconnect delay must be capped at 30 seconds.");
        TestAssert.Equal(TimeSpan.FromSeconds(30), backoff.NextDelay(), "Reconnect delay cap must remain stable.");

        backoff.Reset();
        TestAssert.Equal(TimeSpan.FromSeconds(5), backoff.NextDelay(), "Successful reconnect must reset backoff.");
    }
}

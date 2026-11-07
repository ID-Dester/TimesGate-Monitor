namespace TimesGateMonitor.App.Dashboard;

internal sealed class ReconnectBackoff
{
    private int _attempt;

    public TimeSpan NextDelay()
    {
        var seconds = Math.Min(30, 5 * (1 << Math.Min(_attempt, 3)));
        _attempt++;
        return TimeSpan.FromSeconds(seconds);
    }

    public void Reset() => _attempt = 0;
}

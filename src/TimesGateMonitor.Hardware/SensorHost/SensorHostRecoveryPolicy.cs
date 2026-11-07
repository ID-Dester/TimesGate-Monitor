namespace TimesGateMonitor.Hardware.SensorHost;

internal sealed class SensorHostRecoveryPolicy
{
    internal const int FailureThreshold = 3;

    internal static readonly TimeSpan RestartCooldown = TimeSpan.FromSeconds(15);

    private DateTimeOffset? _lastRestartRequestUtc;

    public int ConsecutiveFailures { get; private set; }

    public void RecordSuccess()
    {
        ConsecutiveFailures = 0;
        _lastRestartRequestUtc = null;
    }

    public bool RecordFailureAndShouldRequestStart(DateTimeOffset nowUtc)
    {
        ConsecutiveFailures++;
        if (ConsecutiveFailures < FailureThreshold)
        {
            return false;
        }

        if (_lastRestartRequestUtc is { } lastRequestUtc &&
            nowUtc - lastRequestUtc < RestartCooldown)
        {
            return false;
        }

        _lastRestartRequestUtc = nowUtc;
        return true;
    }
}

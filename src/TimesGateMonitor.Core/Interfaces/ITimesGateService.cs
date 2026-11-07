using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Core.Interfaces;

public interface ITimesGateService
{
    Task<TimesGateCommandResult> HealthCheckAsync(
        TimesGateConnection connection,
        CancellationToken cancellationToken);

    Task<TimesGateCommandResult> InitializeFrameSequenceAsync(
        TimesGateConnection connection,
        CancellationToken cancellationToken);

    Task<TimesGateCommandResult> SetBrightnessAsync(
        TimesGateConnection connection,
        int brightness,
        CancellationToken cancellationToken);

    Task<TimesGateCommandResult> SendFrameAsync(
        TimesGateConnection connection,
        DisplayFrame frame,
        TimeSpan displayDuration,
        CancellationToken cancellationToken);

    Task<TimesGateCommandResult> ConfigureNetworkTextScreenAsync(
        TimesGateConnection connection,
        int screenIndex,
        Uri backgroundImageUrl,
        IReadOnlyCollection<NetworkTextItem> items,
        CancellationToken cancellationToken);
}

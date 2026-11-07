namespace TimesGateMonitor.Core.Models;

public sealed record TimesGateCommandResult(
    bool IsSuccess,
    bool WasSkipped = false,
    int? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static TimesGateCommandResult Success() => new(true);

    public static TimesGateCommandResult Skipped() => new(true, WasSkipped: true);

    public static TimesGateCommandResult Failure(string message, int? errorCode = null) =>
        new(false, ErrorCode: errorCode, ErrorMessage: message);
}

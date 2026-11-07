using System.Globalization;

namespace TimesGateMonitor.App.Infrastructure;

internal static class ConnectionSettingsValidator
{
    public static bool TryParseLocalToken(string? text, out int token, out string error)
    {
        token = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "LocalToken is required. Find it in the Times Gate device settings.";
            return false;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out token))
        {
            error = "LocalToken must contain digits only.";
            return false;
        }

        return true;
    }
}

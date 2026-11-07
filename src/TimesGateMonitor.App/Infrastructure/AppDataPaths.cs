using System.IO;

namespace TimesGateMonitor.App.Infrastructure;

internal static class AppDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TGm");

    public static string Logs { get; } = Path.Combine(Root, "Logs");

    public static string Preferences { get; } = Path.Combine(Root, "preferences.json");

    public static string DashboardRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimesGateMonitor");

    public static string Dashboard { get; } = Path.Combine(DashboardRoot, "dashboard.json");
}

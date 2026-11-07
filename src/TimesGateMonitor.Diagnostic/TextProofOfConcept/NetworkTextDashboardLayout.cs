using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Diagnostic.TextProofOfConcept;

internal sealed record NetworkTextScreenLayout(
    int ScreenIndex,
    Uri BackgroundUrl,
    IReadOnlyCollection<NetworkTextItem> Items);

internal static class NetworkTextDashboardLayout
{
    private static readonly string[][] ValuePaths =
    [
        ["value/gpu/load", "value/gpu/temp", "value/gpu/tdp"],
        ["value/cpu/load", "value/cpu/temp", "value/cpu/tdp"],
        ["value/core/cpu", "value/core/gpu", "value/core/ram"],
        ["value/ram/load", "value/ram/used", "value/ram/free"],
        ["value/system/time", "value/system/up", "value/system/temp"]
    ];

    private static readonly int[] RowY = [29, 53, 77];

    public static IReadOnlyList<NetworkTextScreenLayout> Create(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        var layouts = new List<NetworkTextScreenLayout>(DisplayFrame.ScreenCount);
        var textId = 1;

        for (var screenIndex = 1; screenIndex <= DisplayFrame.ScreenCount; screenIndex++)
        {
            var updateSeconds = screenIndex <= 3 ? 3 : 5;
            var items = ValuePaths[screenIndex - 1]
                .Select((path, row) => new NetworkTextItem(
                    textId++,
                    X: 18,
                    Y: RowY[row],
                    Font: 4,
                    TextWidth: 104,
                    TextHeight: 16,
                    ValueUrl: new Uri(baseUri, path),
                    UpdateSeconds: updateSeconds))
                .ToArray();

            layouts.Add(new NetworkTextScreenLayout(
                screenIndex,
                new Uri(baseUri, $"background/screen-{screenIndex}.gif"),
                items));
        }

        return layouts;
    }
}

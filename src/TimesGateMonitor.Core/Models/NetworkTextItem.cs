namespace TimesGateMonitor.Core.Models;

/// <summary>
/// Describes one Times Gate field whose value is periodically fetched from a URL.
/// </summary>
public sealed record NetworkTextItem(
    int TextId,
    int X,
    int Y,
    int Font,
    int TextWidth,
    int TextHeight,
    Uri ValueUrl,
    string Color = "#55FF88",
    int UpdateSeconds = 1,
    int Alignment = 1,
    int ItemType = 23,
    string? TextString = null)
{
    public bool RequiresNetworkValue => ItemType == 23;
}

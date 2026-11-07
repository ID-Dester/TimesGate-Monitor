namespace TimesGateMonitor.Core.Models;

/// <summary>
/// An encoded image intended for one physical Times Gate LCD.
/// </summary>
public sealed class DisplayFrame
{
    public const int ScreenCount = 5;
    public const int PixelSize = 128;

    public DisplayFrame(int screenIndex, byte[] jpegData)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(screenIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(screenIndex, ScreenCount);
        ArgumentNullException.ThrowIfNull(jpegData);

        if (jpegData.Length < 4 || jpegData[0] != 0xFF || jpegData[1] != 0xD8)
        {
            throw new ArgumentException("Times Gate frames must contain JPEG data.", nameof(jpegData));
        }

        ScreenIndex = screenIndex;
        JpegData = jpegData;
    }

    public int ScreenIndex { get; }

    public ReadOnlyMemory<byte> JpegData { get; }
}

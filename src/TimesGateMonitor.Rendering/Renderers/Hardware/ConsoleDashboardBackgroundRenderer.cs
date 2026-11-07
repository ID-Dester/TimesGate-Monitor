using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public static class ConsoleDashboardBackgroundRenderer
{
    private static readonly string[] ScreenTitles = ["GPU", "CPU", "CORE", "RAM", "SYSTEM"];
    private static readonly Typeface ConsoleTypeface = new(
        new FontFamily("Consolas"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);
    public static IReadOnlyDictionary<int, byte[]> RenderAll() =>
        Enumerable.Range(1, ScreenTitles.Length).ToDictionary(index => index, Render);

    public static byte[] Render(int screenIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(screenIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(screenIndex, ScreenTitles.Length);

        return RenderConsole(
            ScreenTitles[screenIndex - 1],
            screenIndex <= 3 ? 3 : 5);
    }

    public static byte[] RenderConsole(string title, int updateSeconds)
        => RenderConsole(
            title,
            updateSeconds,
            Color.FromRgb(0x55, 0xFF, 0x88),
            Color.FromRgb(0x55, 0xFF, 0x88),
            "$",
            Color.FromRgb(0x55, 0xFF, 0x88));

    public static byte[] RenderConsole(
        string title,
        int updateSeconds,
        Color frameColor,
        Color textColor,
        string headerSymbol,
        Color headerSymbolColor,
        string? headerIconKey = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(updateSeconds, 1);
        title = string.IsNullOrWhiteSpace(title) ? "SCREEN" : title.Trim();

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var background = FrozenBrush(0xFF050806);
            var frame = FrozenBrush(frameColor);
            var text = FrozenBrush(textColor);
            var symbol = FrozenBrush(headerSymbolColor);
            var muted = FrozenBrush(Muted(frameColor));

            drawing.DrawRectangle(background, null, new Rect(0, 0, 128, 128));
            drawing.DrawRectangle(null, new Pen(frame, 1), new Rect(3.5, 3.5, 121, 121));
            DrawHeader(drawing, title, headerSymbol, text, symbol, headerIconKey);
            drawing.DrawLine(new Pen(muted, 1), new Point(7, 23.5), new Point(121, 23.5));
            DrawText(drawing, ">", 8, 30, 10, muted);
            DrawText(drawing, ">", 8, 54, 10, muted);
            DrawText(drawing, ">", 8, 78, 10, muted);
            drawing.DrawLine(new Pen(muted, 1), new Point(7, 106.5), new Point(121, 106.5));
            DrawText(drawing, "NET://LIVE", 8, 110, 9, muted);
            DrawText(drawing, $"{updateSeconds}S", 107, 110, 9, text);
        }

        return EncodeGif(visual);
    }

    public static byte[] RenderLargeClock(
        string title,
        Color frameColor,
        Color textColor,
        string headerSymbol,
        Color headerSymbolColor,
        string? headerIconKey = null)
    {
        title = string.IsNullOrWhiteSpace(title) ? "CLOCK" : title.Trim();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var background = FrozenBrush(0xFF050806);
            var frame = FrozenBrush(frameColor);
            var text = FrozenBrush(textColor);
            var symbol = FrozenBrush(headerSymbolColor);
            var muted = FrozenBrush(Muted(frameColor));
            drawing.DrawRectangle(background, null, new Rect(0, 0, 128, 128));
            drawing.DrawRectangle(null, new Pen(frame, 1), new Rect(3.5, 3.5, 121, 121));
            DrawHeader(drawing, title, headerSymbol, text, symbol, headerIconKey);
            drawing.DrawLine(new Pen(muted, 1), new Point(7, 23.5), new Point(121, 23.5));
            DrawText(drawing, "LOCAL TIME", 37, 32, 9, muted);
            drawing.DrawLine(new Pen(muted, 1), new Point(7, 91.5), new Point(121, 91.5));
            DrawText(drawing, "RTC://LOCAL", 8, 108, 9, muted);
            DrawText(drawing, "NATIVE", 91, 108, 9, text);
        }

        return EncodeGif(visual);
    }

    public static byte[] RenderSolid(Color color)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            drawing.DrawRectangle(brush, null, new Rect(0, 0, 128, 128));
        }

        return EncodeGif(visual);
    }

    private static byte[] EncodeGif(DrawingVisual visual)
    {
        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new GifBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(capacity: 8 * 1024);
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void DrawText(
        DrawingContext drawing,
        string text,
        double x,
        double y,
        double size,
        Brush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            ConsoleTypeface,
            size,
            brush,
            pixelsPerDip: 1);
        drawing.DrawText(formatted, new Point(x, y));
    }

    private static void DrawHeader(
        DrawingContext drawing,
        string title,
        string headerSymbol,
        Brush textBrush,
        Brush symbolBrush,
        string? headerIconKey)
    {
        var symbolSize = headerSymbol.Length > 1 ? 10d : 13d;
        var formattedSymbol = new FormattedText(
            headerSymbol,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            ConsoleTypeface,
            symbolSize,
            symbolBrush,
            pixelsPerDip: 1);
        drawing.DrawText(formattedSymbol, new Point(8, 6));
        DrawText(
            drawing,
            title.ToUpperInvariant(),
            Math.Min(32, 10 + formattedSymbol.WidthIncludingTrailingWhitespace),
            6,
            13,
            textBrush);
    }

    private static Color Muted(Color color) => Color.FromRgb(
        (byte)Math.Max(24, color.R * 0.43),
        (byte)Math.Max(32, color.G * 0.43),
        (byte)Math.Max(28, color.B * 0.43));

    private static SolidColorBrush FrozenBrush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

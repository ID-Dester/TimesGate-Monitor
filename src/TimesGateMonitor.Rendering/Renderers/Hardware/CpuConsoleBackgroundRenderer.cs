using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

/// <summary>
/// Creates the static background used by the network-text proof of concept.
/// Dynamic values are drawn by the Times Gate firmware on top of this image.
/// </summary>
public sealed class CpuConsoleBackgroundRenderer
{
    private static readonly Typeface ConsoleTypeface = new(
        new FontFamily("Consolas"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    public static byte[] Render()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var background = FrozenBrush(0xFF050806);
            var green = FrozenBrush(0xFF55FF88);
            var muted = FrozenBrush(0xFF3A7950);

            drawing.DrawRectangle(background, null, new Rect(0, 0, 128, 128));
            drawing.DrawRectangle(null, new Pen(muted, 1), new Rect(3.5, 3.5, 121, 121));
            DrawText(drawing, "$ CPU", 8, 6, 13, green);
            drawing.DrawLine(new Pen(muted, 1), new Point(7, 23.5), new Point(121, 23.5));
            DrawText(drawing, "> LOAD", 8, 29, 10, muted);
            DrawText(drawing, "> TEMP", 8, 78, 10, muted);
            drawing.DrawLine(new Pen(muted, 1), new Point(7, 106.5), new Point(121, 106.5));
            DrawText(drawing, "NET://LIVE", 8, 110, 9, muted);
            DrawText(drawing, "1S", 107, 110, 9, green);
        }

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
}

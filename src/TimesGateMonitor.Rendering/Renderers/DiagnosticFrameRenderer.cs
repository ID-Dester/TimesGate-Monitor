using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers;

public sealed class DiagnosticFrameRenderer : IDiagnosticFrameRenderer
{
    private static readonly int[] TestValues = [20, 40, 60, 80, 100];
    private static readonly Typeface BoldTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    public IReadOnlyList<DisplayFrame> Render(DiagnosticFrameOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var frames = new List<DisplayFrame>(DisplayFrame.ScreenCount);

        for (var screenIndex = 1; screenIndex <= DisplayFrame.ScreenCount; screenIndex++)
        {
            frames.Add(RenderScreen(screenIndex, TestValues[screenIndex - 1], options));
        }

        return frames;
    }

    private static DisplayFrame RenderScreen(
        int screenIndex,
        int testValue,
        DiagnosticFrameOptions options)
    {
        var background = CreateBrush(options.BackgroundColor);
        var accent = CreateBrush(options.AccentColor);
        var white = CreateBrush(0xFFFFFFFF);
        var caption = CreateBrush(0xFF9AA5B1);
        var track = CreateBrush(0xFF252A31);
        var visual = new DrawingVisual();

        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(
                background,
                pen: null,
                new Rect(0, 0, DisplayFrame.PixelSize, DisplayFrame.PixelSize));

            DrawCenteredText(drawing, $"SCREEN {screenIndex}", 15, 14, accent);
            DrawCenteredText(
                drawing,
                testValue.ToString(CultureInfo.InvariantCulture),
                39,
                29,
                white);
            DrawCenteredText(drawing, "DIAGNOSTIC", 79, 10, caption);

            drawing.DrawRoundedRectangle(track, pen: null, new Rect(10, 105, 108, 11), 5, 5);
            drawing.DrawRoundedRectangle(
                accent,
                pen: null,
                new Rect(10, 105, 108d * testValue / 100d, 11),
                5,
                5);
        }

        var bitmap = new RenderTargetBitmap(
            DisplayFrame.PixelSize,
            DisplayFrame.PixelSize,
            dpiX: 96,
            dpiY: 96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream(capacity: 16 * 1024);
        encoder.Save(stream);
        return new DisplayFrame(screenIndex, stream.ToArray());
    }

    private static SolidColorBrush CreateBrush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
        brush.Freeze();
        return brush;
    }

    private static void DrawCenteredText(
        DrawingContext drawing,
        string text,
        double y,
        double size,
        Brush brush)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BoldTypeface,
            size,
            brush,
            pixelsPerDip: 1d);
        var x = (DisplayFrame.PixelSize - formattedText.WidthIncludingTrailingWhitespace) / 2d;
        drawing.DrawText(formattedText, new Point(x, y));
    }
}

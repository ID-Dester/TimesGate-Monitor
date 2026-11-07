using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public sealed class DashboardCanvas
{
    private static readonly Typeface RegularTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    private readonly DrawingContext _drawing;

    public DashboardCanvas(DrawingContext drawing, DashboardRenderOptions options)
    {
        _drawing = drawing;
        Accent = CreateBrush(options.AccentColor);
        Background = CreateBrush(options.BackgroundColor);
        Foreground = CreateBrush(options.ForegroundColor);
        Muted = CreateBrush(options.MutedColor);
        Track = CreateBrush(options.TrackColor);
        Warm = CreateBrush(options.WarmColor);
        Hot = CreateBrush(options.HotColor);

        _drawing.DrawRectangle(
            Background,
            pen: null,
            new Rect(0, 0, DisplayFrame.PixelSize, DisplayFrame.PixelSize));
    }

    public Brush Accent { get; }

    public Brush Background { get; }

    public Brush Foreground { get; }

    public Brush Muted { get; }

    public Brush Track { get; }

    public Brush Warm { get; }

    public Brush Hot { get; }

    public void Title(string text) => DrawCentered(text, y: 5, size: 15, Accent, bold: true);

    public void DrawText(
        string text,
        double x,
        double y,
        double size,
        Brush brush,
        bool bold = false) =>
        _drawing.DrawText(CreateText(text, size, brush, bold), new Point(x, y));

    public void DrawCentered(
        string text,
        double y,
        double size,
        Brush brush,
        bool bold = false)
    {
        var formatted = CreateText(text, size, brush, bold);
        var x = (DisplayFrame.PixelSize - formatted.WidthIncludingTrailingWhitespace) / 2d;
        _drawing.DrawText(formatted, new Point(x, y));
    }

    public void DrawRight(
        string text,
        double right,
        double y,
        double size,
        Brush brush,
        bool bold = false)
    {
        var formatted = CreateText(text, size, brush, bold);
        _drawing.DrawText(
            formatted,
            new Point(right - formatted.WidthIncludingTrailingWhitespace, y));
    }

    public void ProgressBar(double? percent, double y, double height = 9)
    {
        const double left = 9;
        const double width = 110;
        var radius = height / 2d;
        _drawing.DrawRoundedRectangle(Track, pen: null, new Rect(left, y, width, height), radius, radius);

        if (!percent.HasValue)
        {
            return;
        }

        var fillWidth = width * Math.Clamp(percent.Value, 0, 100) / 100d;
        if (fillWidth > 0)
        {
            _drawing.DrawRoundedRectangle(
                Accent,
                pen: null,
                new Rect(left, y, fillWidth, height),
                radius,
                radius);
        }
    }

    public void MetricRow(string label, string value, double y, Brush? valueBrush = null, double size = 10.5)
    {
        DrawText(label, 9, y, size, Muted, bold: true);
        DrawRight(value, 119, y, size, valueBrush ?? Foreground, bold: true);
    }

    public Brush TemperatureBrush(TemperatureLevel level) => level switch
    {
        TemperatureLevel.Warm => Warm,
        TemperatureLevel.Hot => Hot,
        TemperatureLevel.Unavailable => Muted,
        _ => Foreground
    };

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

    private static FormattedText CreateText(
        string text,
        double size,
        Brush brush,
        bool bold) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            bold ? BoldTypeface : RegularTypeface,
            size,
            brush,
            pixelsPerDip: 1d);
}

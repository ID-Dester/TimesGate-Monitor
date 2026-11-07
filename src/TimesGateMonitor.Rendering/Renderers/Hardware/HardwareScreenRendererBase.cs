using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public abstract class HardwareScreenRendererBase : IHardwareScreenRenderer
{
    public abstract int ScreenIndex { get; }

    public DisplayFrame Render(HardwareSnapshot snapshot, DashboardRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var canvas = new DashboardCanvas(drawing, options);
            Draw(canvas, snapshot, options);
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
        return new DisplayFrame(ScreenIndex, stream.ToArray());
    }

    protected abstract void Draw(
        DashboardCanvas canvas,
        HardwareSnapshot snapshot,
        DashboardRenderOptions options);
}

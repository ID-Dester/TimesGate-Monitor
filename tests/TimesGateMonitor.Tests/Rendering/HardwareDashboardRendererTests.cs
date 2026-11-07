using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TimesGateMonitor.Core.Interfaces;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Rendering.Renderers.Hardware;

namespace TimesGateMonitor.Tests.Rendering;

internal static class HardwareDashboardRendererTests
{
    public static void Run()
    {
        IHardwareScreenRenderer[] screens =
        [
            new CpuScreenRenderer(),
            new GpuScreenRenderer(),
            new CenterScreenRenderer(),
            new RamScreenRenderer(),
            new SystemScreenRenderer()
        ];
        var renderer = new HardwareDashboardRenderer(screens);
        var frames = renderer.Render(
            new HardwareSnapshot
            {
                CpuLoadPercent = 42,
                CpuTemperatureCelsius = 73,
                CpuClockMegahertz = 4850,
                CpuPowerWatts = 87,
                GpuLoadPercent = 76,
                GpuTemperatureCelsius = 81,
                GpuClockMegahertz = 2655,
                GpuPowerWatts = 198,
                VramUsedGigabytes = 6.4,
                VramTotalGigabytes = 12,
                RamLoadPercent = 53,
                RamUsedGigabytes = 17,
                RamTotalGigabytes = 32,
                RamAvailableGigabytes = 15,
                Uptime = TimeSpan.FromHours(27)
            },
            new DashboardRenderOptions());

        TestAssert.Equal(DisplayFrame.ScreenCount, frames.Count, "Dashboard must render five frames.");
        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var frame in frames)
        {
            using var stream = new MemoryStream(frame.JpegData.ToArray());
            var decoder = new JpegBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            TestAssert.Equal(DisplayFrame.PixelSize, decoder.Frames[0].PixelWidth, "JPEG width is incorrect.");
            TestAssert.Equal(DisplayFrame.PixelSize, decoder.Frames[0].PixelHeight, "JPEG height is incorrect.");
            TestAssert.True(
                hashes.Add(Convert.ToHexString(SHA256.HashData(frame.JpegData.Span))),
                "Every hardware screen must be visually distinct.");
        }

        var backgrounds = ConsoleDashboardBackgroundRenderer.RenderAll();
        TestAssert.Equal(
            DisplayFrame.ScreenCount,
            backgrounds.Count,
            "Console dashboard must render five backgrounds.");

        hashes.Clear();
        foreach (var (screenIndex, background) in backgrounds)
        {
            using var backgroundStream = new MemoryStream(background);
            var backgroundDecoder = new GifBitmapDecoder(
                backgroundStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            TestAssert.Equal(
                DisplayFrame.PixelSize,
                backgroundDecoder.Frames[0].PixelWidth,
                $"Console background {screenIndex} width is incorrect.");
            TestAssert.Equal(
                DisplayFrame.PixelSize,
                backgroundDecoder.Frames[0].PixelHeight,
                $"Console background {screenIndex} height is incorrect.");
            TestAssert.True(
                hashes.Add(Convert.ToHexString(SHA256.HashData(background))),
                "Every console background must be visually distinct.");
        }

        var terminalBackground = ConsoleDashboardBackgroundRenderer.RenderConsole(
            "GPU",
            5,
            Color.FromRgb(0x55, 0xFF, 0x88),
            Color.FromRgb(0x55, 0xFF, 0x88),
            ">_",
            Color.FromRgb(0x55, 0xFF, 0x88),
            "terminal");
        using (var terminalStream = new MemoryStream(terminalBackground))
        {
            var terminalDecoder = new GifBitmapDecoder(
                terminalStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            TestAssert.Equal(DisplayFrame.PixelSize, terminalDecoder.Frames[0].PixelWidth, "Terminal background width is incorrect.");
            TestAssert.Equal(DisplayFrame.PixelSize, terminalDecoder.Frames[0].PixelHeight, "Terminal background height is incorrect.");
        }

        var mediaPath = Path.Combine(Path.GetTempPath(), $"timesgate-media-{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(mediaPath, frames[0].JpegData.ToArray());
            var mediaBackground = CustomMediaBackgroundRenderer.Render(mediaPath);
            using var mediaStream = new MemoryStream(mediaBackground);
            var mediaDecoder = new GifBitmapDecoder(
                mediaStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            TestAssert.Equal(DisplayFrame.PixelSize, mediaDecoder.Frames[0].PixelWidth, "Custom-media width is incorrect.");
            TestAssert.Equal(DisplayFrame.PixelSize, mediaDecoder.Frames[0].PixelHeight, "Custom-media height is incorrect.");
        }
        finally
        {
            File.Delete(mediaPath);
        }

        var animationPath = Path.Combine(Path.GetTempPath(), $"timesgate-animation-{Guid.NewGuid():N}.gif");
        try
        {
            WriteTestAnimation(animationPath);
            var animation = CustomMediaBackgroundRenderer.Render(animationPath);
            using var animationStream = new MemoryStream(animation);
            var animationDecoder = new GifBitmapDecoder(
                animationStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            TestAssert.Equal(2, animationDecoder.Frames.Count, "Custom GIF lost its animation frames.");
            foreach (var frame in animationDecoder.Frames)
            {
                TestAssert.Equal(DisplayFrame.PixelSize, frame.PixelWidth, "Animated GIF width is incorrect.");
                TestAssert.Equal(DisplayFrame.PixelSize, frame.PixelHeight, "Animated GIF height is incorrect.");
                TestAssert.True(
                    frame.Metadata is BitmapMetadata metadata &&
                    metadata.GetQuery("/grctlext/Delay") is ushort delay &&
                    delay > 0,
                    "Animated GIF frame delay is missing.");
            }
        }
        finally
        {
            File.Delete(animationPath);
        }
    }

    private static void WriteTestAnimation(string path)
    {
        var encoder = new GifBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateSolidFrame(64, 96, 0xE8, 0x3D, 0x55)));
        encoder.Frames.Add(BitmapFrame.Create(CreateSolidFrame(64, 96, 0x58, 0x65, 0xF2)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static BitmapSource CreateSolidFrame(int width, int height, byte red, byte green, byte blue)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = 0xFF;
        }

        return BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
    }
}

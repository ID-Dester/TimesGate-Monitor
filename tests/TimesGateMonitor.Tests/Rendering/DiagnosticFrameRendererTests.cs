using System.Security.Cryptography;
using System.IO;
using System.Windows.Media.Imaging;
using TimesGateMonitor.Core.Models;
using TimesGateMonitor.Rendering.Renderers;

namespace TimesGateMonitor.Tests.Rendering;

internal static class DiagnosticFrameRendererTests
{
    private static readonly int[] ExpectedScreenIndexes = [1, 2, 3, 4, 5];

    public static void Run()
    {
        var renderer = new DiagnosticFrameRenderer();
        var frames = renderer.Render(new DiagnosticFrameOptions());

        TestAssert.Equal(DisplayFrame.ScreenCount, frames.Count, "Renderer must create five frames.");
        TestAssert.SequenceEqual(
            ExpectedScreenIndexes,
            frames.Select(frame => frame.ScreenIndex),
            "Renderer returned incorrect screen indexes.");

        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var frame in frames)
        {
            using var stream = new MemoryStream(frame.JpegData.ToArray());
            var decoder = new JpegBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];

            TestAssert.Equal(DisplayFrame.PixelSize, bitmap.PixelWidth, "JPEG width is incorrect.");
            TestAssert.Equal(DisplayFrame.PixelSize, bitmap.PixelHeight, "JPEG height is incorrect.");
            TestAssert.True(
                hashes.Add(Convert.ToHexString(SHA256.HashData(frame.JpegData.Span))),
                "Every diagnostic frame must be visually distinct.");
        }
    }
}

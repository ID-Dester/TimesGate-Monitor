using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TimesGateMonitor.Rendering.Renderers.Hardware;

public static class CustomMediaBackgroundRenderer
{
    private const long MaximumAnimatedGifBytes = 2 * 1024 * 1024;
    private const int MaximumAnimatedGifFrames = 300;

    public static byte[] Render(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The imported widget image could not be found.", path);
        }

        var extension = Path.GetExtension(path);
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("The imported media does not contain an image frame.");
        }

        var firstFrame = decoder.Frames[0];
        if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) &&
            decoder.Frames.Count > 1)
        {
            if (decoder.Frames.All(frame => frame.PixelWidth == 128 && frame.PixelHeight == 128) &&
                stream.Length <= MaximumAnimatedGifBytes)
            {
                return File.ReadAllBytes(path);
            }

            return RenderAnimatedFrames(decoder.Frames);
        }

        return RenderStaticFrame(firstFrame);
    }

    private static byte[] RenderStaticFrame(BitmapSource frame)
    {
        var encoder = new GifBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(RenderFrame(frame)));
        using var output = new MemoryStream(capacity: 16 * 1024);
        encoder.Save(output);
        return output.ToArray();
    }

    private static byte[] RenderAnimatedFrames(IReadOnlyList<BitmapFrame> frames)
    {
        if (frames.Count > MaximumAnimatedGifFrames)
        {
            throw new InvalidDataException(
                $"Animated GIFs may contain no more than {MaximumAnimatedGifFrames} frames.");
        }

        var delays = new ushort[frames.Count];
        var encoder = new GifBitmapEncoder();
        for (var index = 0; index < frames.Count; index++)
        {
            delays[index] = GetFrameDelay(frames[index]);
            encoder.Frames.Add(BitmapFrame.Create(RenderFrame(frames[index])));
        }

        using var encoded = new MemoryStream(capacity: 256 * 1024);
        encoder.Save(encoded);
        var animatedGif = AddAnimationMetadata(encoded.ToArray(), delays);
        if (animatedGif.Length > MaximumAnimatedGifBytes)
        {
            throw new InvalidDataException(
                "The resized animated GIF is larger than 2 MB. Reduce its frame count or duration.");
        }

        return animatedGif;
    }

    private static RenderTargetBitmap RenderFrame(BitmapSource frame)
    {
        var scale = Math.Max(128d / frame.PixelWidth, 128d / frame.PixelHeight);
        var width = frame.PixelWidth * scale;
        var height = frame.PixelHeight * scale;
        var destination = new Rect((128 - width) / 2, (128 - height) / 2, width, height);

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 128, 128));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, 128, 128)));
            drawing.DrawImage(frame, destination);
            drawing.Pop();
        }

        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static ushort GetFrameDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata &&
                metadata.GetQuery("/grctlext/Delay") is ushort delay)
            {
                return delay < 2 ? (ushort)10 : delay;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // Some GIF encoders omit the graphic-control extension entirely.
        }

        return 10;
    }

    private static byte[] AddAnimationMetadata(byte[] encodedGif, IReadOnlyList<ushort> delays)
    {
        if (encodedGif.Length < 14 ||
            encodedGif[0] != (byte)'G' ||
            encodedGif[1] != (byte)'I' ||
            encodedGif[2] != (byte)'F')
        {
            throw new InvalidDataException("The resized animation is not a valid GIF stream.");
        }

        var cursor = 13;
        var packedFields = encodedGif[10];
        if ((packedFields & 0x80) != 0)
        {
            cursor += 3 * (1 << ((packedFields & 0x07) + 1));
        }

        EnsureAvailable(encodedGif, cursor, 1);
        using var output = new MemoryStream(encodedGif.Length + (delays.Count * 8) + 19);
        output.Write(encodedGif, 0, cursor);
        WriteLoopExtension(output);

        var frameIndex = 0;
        while (cursor < encodedGif.Length)
        {
            switch (encodedGif[cursor])
            {
                case 0x21 when cursor + 1 < encodedGif.Length && encodedGif[cursor + 1] == 0xF9:
                    cursor += 2;
                    SkipSubBlocks(encodedGif, ref cursor);
                    break;

                case 0x21:
                    CopyExtension(encodedGif, ref cursor, output);
                    break;

                case 0x2C:
                    var delay = frameIndex < delays.Count ? delays[frameIndex] : (ushort)10;
                    WriteGraphicControlExtension(output, delay);
                    CopyImage(encodedGif, ref cursor, output);
                    frameIndex++;
                    break;

                case 0x3B:
                    output.WriteByte(0x3B);
                    if (frameIndex != delays.Count)
                    {
                        throw new InvalidDataException("The resized GIF lost one or more animation frames.");
                    }

                    return output.ToArray();

                default:
                    throw new InvalidDataException("The resized GIF contains an unsupported data block.");
            }
        }

        throw new InvalidDataException("The resized GIF is missing its trailer.");
    }

    private static void CopyExtension(byte[] data, ref int cursor, Stream output)
    {
        EnsureAvailable(data, cursor, 2);
        output.Write(data, cursor, 2);
        cursor += 2;
        CopySubBlocks(data, ref cursor, output);
    }

    private static void CopyImage(byte[] data, ref int cursor, Stream output)
    {
        EnsureAvailable(data, cursor, 10);
        var packedFields = data[cursor + 9];
        var descriptorLength = 10;
        if ((packedFields & 0x80) != 0)
        {
            descriptorLength += 3 * (1 << ((packedFields & 0x07) + 1));
        }

        EnsureAvailable(data, cursor, descriptorLength + 1);
        output.Write(data, cursor, descriptorLength + 1);
        cursor += descriptorLength + 1;
        CopySubBlocks(data, ref cursor, output);
    }

    private static void CopySubBlocks(byte[] data, ref int cursor, Stream output)
    {
        while (true)
        {
            EnsureAvailable(data, cursor, 1);
            var length = data[cursor++];
            output.WriteByte(length);
            if (length == 0)
            {
                return;
            }

            EnsureAvailable(data, cursor, length);
            output.Write(data, cursor, length);
            cursor += length;
        }
    }

    private static void SkipSubBlocks(byte[] data, ref int cursor)
    {
        while (true)
        {
            EnsureAvailable(data, cursor, 1);
            var length = data[cursor++];
            if (length == 0)
            {
                return;
            }

            EnsureAvailable(data, cursor, length);
            cursor += length;
        }
    }

    private static void WriteLoopExtension(Stream output)
    {
        ReadOnlySpan<byte> extension =
        [
            0x21, 0xFF, 0x0B,
            (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
            (byte)'2', (byte)'.', (byte)'0',
            0x03, 0x01, 0x00, 0x00, 0x00
        ];
        output.Write(extension);
    }

    private static void WriteGraphicControlExtension(Stream output, ushort delay)
    {
        delay = delay < 2 ? (ushort)10 : delay;
        Span<byte> extension =
        [
            0x21, 0xF9, 0x04, 0x04,
            (byte)(delay & 0xFF), (byte)(delay >> 8),
            0x00, 0x00
        ];
        output.Write(extension);
    }

    private static void EnsureAvailable(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new InvalidDataException("The resized GIF stream is truncated.");
        }
    }
}

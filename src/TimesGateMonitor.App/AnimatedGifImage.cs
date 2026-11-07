using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TimesGateMonitor.App;

internal sealed class AnimatedGifImage : System.Windows.Controls.Image
{
    private readonly IReadOnlyList<BitmapFrame> _frames;
    private readonly IReadOnlyList<TimeSpan> _delays;
    private readonly DispatcherTimer _timer;
    private int _frameIndex;

    private AnimatedGifImage(IReadOnlyList<BitmapFrame> frames, IReadOnlyList<TimeSpan> delays)
    {
        _frames = frames;
        _delays = delays;
        Source = frames[0];
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = delays[0]
        };
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public static AnimatedGifImage? TryCreate(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count < 2)
            {
                return null;
            }

            var delays = decoder.Frames.Select(ReadDelay).ToArray();
            return new AnimatedGifImage(decoder.Frames, delays);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private void Start()
    {
        if (_frames.Count < 2)
        {
            return;
        }

        _timer.Interval = _delays[_frameIndex];
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        Source = _frames[_frameIndex];
        _timer.Interval = _delays[_frameIndex];
    }

    private static TimeSpan ReadDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata &&
                metadata.GetQuery("/grctlext/Delay") is ushort delay)
            {
                return TimeSpan.FromMilliseconds(Math.Max(20, delay * 10));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // Use a visible default delay when the source omits frame timing.
        }

        return TimeSpan.FromMilliseconds(100);
    }
}

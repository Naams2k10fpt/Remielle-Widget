using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Remielle.Overlay;

internal sealed class AnimatedGif : Image, IDisposable
{
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<BitmapSource> _frames = [];
    private IReadOnlyList<TimeSpan> _delays = [];
    private int _frameIndex;

    public AnimatedGif()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher);
        _timer.Tick += OnTick;
    }

    public void Load(string path)
    {
        _timer.Stop();

        (_frames, _delays) = Decode(path);
        _frameIndex = 0;
        Source = _frames[0];

        if (_frames.Count > 1)
        {
            _timer.Interval = _delays[0];
            _timer.Start();
        }
    }

    internal static (BitmapSource[] Frames, TimeSpan[] Delays) Decode(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("GIF contains no frames.");
        }

        var fallbackWidth = decoder.Frames.Max(
            frame => GetMetadataInt(frame.Metadata, "/imgdesc/Left") + frame.PixelWidth);
        var fallbackHeight = decoder.Frames.Max(
            frame => GetMetadataInt(frame.Metadata, "/imgdesc/Top") + frame.PixelHeight);
        var width = Math.Max(1, GetMetadataInt(decoder.Metadata, "/logscrdesc/Width", fallbackWidth));
        var height = Math.Max(1, GetMetadataInt(decoder.Metadata, "/logscrdesc/Height", fallbackHeight));
        var stride = width * 4;
        var canvas = new byte[stride * height];
        byte[]? restoreCanvas = null;
        var frames = new BitmapSource[decoder.Frames.Count];
        var delays = new TimeSpan[decoder.Frames.Count];
        var previousDisposal = 0;
        var previousLeft = 0;
        var previousTop = 0;
        var previousWidth = 0;
        var previousHeight = 0;

        for (var i = 0; i < decoder.Frames.Count; i++)
        {
            if (previousDisposal == 2)
            {
                Clear(canvas, stride, width, height, previousLeft, previousTop, previousWidth, previousHeight);
            }
            else if (previousDisposal == 3 && restoreCanvas is not null)
            {
                canvas = restoreCanvas;
            }

            var rawFrame = decoder.Frames[i];
            var left = GetMetadataInt(rawFrame.Metadata, "/imgdesc/Left");
            var top = GetMetadataInt(rawFrame.Metadata, "/imgdesc/Top");
            var disposal = GetMetadataInt(rawFrame.Metadata, "/grctlext/Disposal");
            restoreCanvas = disposal == 3 ? (byte[])canvas.Clone() : null;

            Draw(rawFrame, canvas, stride, width, height, left, top);
            frames[i] = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                canvas,
                stride);
            frames[i].Freeze();
            delays[i] = GetDelay(rawFrame);

            previousDisposal = disposal;
            previousLeft = left;
            previousTop = top;
            previousWidth = rawFrame.PixelWidth;
            previousHeight = rawFrame.PixelHeight;
        }

        return (frames, delays);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        Source = null;
        _frames = [];
        _delays = [];
    }

    private void OnTick(object? sender, EventArgs args)
    {
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        Source = _frames[_frameIndex];
        _timer.Interval = _delays[_frameIndex];
    }

    private static TimeSpan GetDelay(BitmapFrame frame)
    {
        var hundredths = GetMetadataInt(frame.Metadata, "/grctlext/Delay", 10);
        return TimeSpan.FromMilliseconds(Math.Max(20, hundredths * 10));
    }

    private static int GetMetadataInt(ImageMetadata? metadata, string query, int fallback = 0)
    {
        if (metadata is BitmapMetadata bitmapMetadata)
        {
            try
            {
                if (bitmapMetadata.GetQuery(query) is object value)
                {
                    return Convert.ToInt32(value);
                }
            }
            catch (Exception exception) when (
                exception is NotSupportedException or FormatException or InvalidCastException or OverflowException)
            {
            }
        }

        return fallback;
    }

    private static void Draw(
        BitmapFrame frame,
        byte[] canvas,
        int canvasStride,
        int canvasWidth,
        int canvasHeight,
        int left,
        int top)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var frameStride = converted.PixelWidth * 4;
        var pixels = new byte[frameStride * converted.PixelHeight];
        converted.CopyPixels(pixels, frameStride, 0);

        var startX = Math.Max(0, left);
        var endX = Math.Min(canvasWidth, left + converted.PixelWidth);
        var startY = Math.Max(0, top);
        var endY = Math.Min(canvasHeight, top + converted.PixelHeight);
        for (var y = startY; y < endY; y++)
        {
            for (var x = startX; x < endX; x++)
            {
                var source = (y - top) * frameStride + (x - left) * 4;
                if (pixels[source + 3] == 0)
                {
                    continue;
                }

                var destination = y * canvasStride + x * 4;
                canvas[destination] = pixels[source];
                canvas[destination + 1] = pixels[source + 1];
                canvas[destination + 2] = pixels[source + 2];
                canvas[destination + 3] = pixels[source + 3];
            }
        }
    }

    private static void Clear(
        byte[] canvas,
        int stride,
        int canvasWidth,
        int canvasHeight,
        int left,
        int top,
        int frameWidth,
        int frameHeight)
    {
        var startX = Math.Max(0, left);
        var endX = Math.Min(canvasWidth, left + frameWidth);
        var startY = Math.Max(0, top);
        var endY = Math.Min(canvasHeight, top + frameHeight);
        for (var y = startY; y < endY; y++)
        {
            Array.Clear(canvas, y * stride + startX * 4, Math.Max(0, endX - startX) * 4);
        }
    }
}

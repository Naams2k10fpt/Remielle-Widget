using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Remielle.Overlay;

internal sealed class AnimatedGif : Image, IDisposable
{
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<BitmapFrame> _frames = [];
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

        using var stream = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        _frames = decoder.Frames.ToArray();
        if (_frames.Count == 0)
        {
            throw new InvalidDataException("GIF contains no frames.");
        }

        _delays = _frames.Select(GetDelay).ToArray();
        _frameIndex = 0;
        Source = _frames[0];

        if (_frames.Count > 1)
        {
            _timer.Interval = _delays[0];
            _timer.Start();
        }
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
        var hundredths = 10;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try
            {
                if (metadata.GetQuery("/grctlext/Delay") is object value)
                {
                    hundredths = Convert.ToInt32(value);
                }
            }
            catch (NotSupportedException)
            {
            }
        }

        return TimeSpan.FromMilliseconds(Math.Max(20, hundredths * 10));
    }
}

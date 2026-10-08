using System.Collections.Concurrent;

namespace ReplayKit.Services;

internal sealed class HistoryEncoder : IDisposable
{
    private readonly BlockingCollection<(BitmapSource Image, string Monitor, DateTimeOffset At, int Generation, int Quality)> _queue = new(1);
    private readonly Task _worker;
    public HistoryEncoder(Action<CaptureFrame, int> deliver, Action<Exception> fail)
    {
        _worker = Task.Factory.StartNew(() =>
        {
            BitmapSource? previous = null; byte[]? jpeg = null; var previousQuality = 0;
            foreach (var packet in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (!ReferenceEquals(previous, packet.Image) || previousQuality != packet.Quality)
                    {
                        jpeg = Images.EncodeJpeg(packet.Image, packet.Quality); previous = packet.Image; previousQuality = packet.Quality;
                    }
                    deliver(new CaptureFrame(packet.At, packet.Monitor, packet.Image.PixelWidth, packet.Image.PixelHeight, jpeg!), packet.Generation);
                }
                catch (Exception e) { fail(e); }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public bool Submit(BitmapSource image, string monitor, DateTimeOffset at, int generation, int quality)
    {
        var packet = (image, monitor, at, generation, quality);
        if (_queue.TryAdd(packet)) return false;
        var dropped = _queue.TryTake(out _);
        _queue.TryAdd(packet);
        return dropped;
    }
    public void Dispose() { _queue.CompleteAdding(); _worker.GetAwaiter().GetResult(); _queue.Dispose(); }
}

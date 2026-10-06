using System.Collections.Concurrent;

namespace ReplayKit.Services;

internal sealed class HistoryEncoder : IDisposable
{
    private readonly BlockingCollection<(BitmapSource Image, string Monitor, DateTimeOffset At, int Generation)> _queue = new(1);
    private readonly Task _worker;
    public HistoryEncoder(Action<CaptureFrame, int> deliver, Action<Exception> fail)
    {
        _worker = Task.Factory.StartNew(() =>
        {
            BitmapSource? previous = null; byte[]? jpeg = null;
            foreach (var packet in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (!ReferenceEquals(previous, packet.Image)) { jpeg = Images.EncodeJpeg(packet.Image); previous = packet.Image; }
                    deliver(new CaptureFrame(packet.At, packet.Monitor, packet.Image.PixelWidth, packet.Image.PixelHeight, jpeg!), packet.Generation);
                }
                catch (Exception e) { fail(e); }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Submit(BitmapSource image, string monitor, DateTimeOffset at, int generation)
    {
        if (!_queue.TryAdd((image, monitor, at, generation))) { _queue.TryTake(out _); _queue.TryAdd((image, monitor, at, generation)); }
    }
    public void Dispose() { _queue.CompleteAdding(); _worker.GetAwaiter().GetResult(); _queue.Dispose(); }
}

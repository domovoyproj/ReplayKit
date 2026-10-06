using System.Collections.Concurrent;

namespace ReplayKit.Services;

/// <summary>Encoder owner thread with a three-frame queue: a slow encoder never blocks DXGI.</summary>
internal sealed class VideoSession : IDisposable
{
    private readonly BlockingCollection<(BitmapSource Image, long Ticks)> _frames = new(3);
    private readonly object _gate = new();
    private AudioMixer? _audio;
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<string?> Finished { get; }
    public string? Error { get; private set; }
    public long DroppedFrames { get; private set; }
    public VideoSession(string path, VideoOptions options)
    {
        Finished = Task.Factory.StartNew(() => Run(path, options), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Submit(BitmapSource image, long ticks)
    {
        lock (_gate)
        {
            if (_frames.IsAddingCompleted || Finished.IsCompleted) return;
            if (!_frames.TryAdd((image, ticks))) { _frames.TryTake(out _); _frames.TryAdd((image, ticks)); DroppedFrames++; }
        }
    }
    public void Pause() { lock (_gate) _audio?.Clear(); }
    public void Stop() { lock (_gate) { if (!_frames.IsAddingCompleted) _frames.CompleteAdding(); } }
    public void Dispose() { Stop(); Finished.GetAwaiter().GetResult(); _frames.Dispose(); }
    private string? Run(string path, VideoOptions options)
    {
        Mp4Writer? writer = null; long audioFrames = 0;
        try
        {
            foreach (var packet in _frames.GetConsumingEnumerable())
            {
                var image = options.Resize(packet.Image);
                if (writer == null)
                {
                    lock (_gate) { if (options.Audio) _audio = new AudioMixer(options); }
                    writer = new Mp4Writer(path, image.PixelWidth, image.PixelHeight, options.Fps, options.Audio, options.Quality);
                }
                writer.Write(image, packet.Ticks);
                if (_audio != null)
                {
                    var target = (packet.Ticks + TimeSpan.TicksPerSecond / options.Fps) * 48000 / TimeSpan.TicksPerSecond;
                    while (audioFrames < target)
                    {
                        var count = (int)Math.Min(4800, target - audioFrames);
                        byte[] pcm; lock (_gate) pcm = _audio.Read(count);
                        writer.WriteAudio(pcm, audioFrames * TimeSpan.TicksPerSecond / 48000); audioFrames += count;
                    }
                }
                Started.TrySetResult(true);
            }
        }
        catch (Exception e) { Error = e.Message; Started.TrySetException(e); }
        finally
        {
            lock (_gate) { _audio?.Dispose(); _audio = null; }
            if (!Started.Task.IsCompleted) Started.TrySetCanceled();
        }
        try { return writer?.FrameCount > 0 ? writer.Complete() : null; }
        catch (Exception e) { Error = e.Message; return writer?.RecoveryPath; }
        finally { writer?.Dispose(); }
    }
}

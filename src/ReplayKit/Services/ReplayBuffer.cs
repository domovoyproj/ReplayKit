using System.Collections.Concurrent;
using System.Diagnostics;

namespace ReplayKit.Services;

public sealed record ReplayFrame(long Ticks, CaptureFrame Frame, byte[]? Audio, long AudioTicks = 0);

/// <summary>Bounded in-memory MJPEG replay. Only Export writes to disk.</summary>
public sealed class ReplayBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<ReplayFrame> _frames = new();
    private readonly BlockingCollection<(BitmapSource Image, long Ticks, int Generation)> _pending = new(1);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Task _worker;
    private long _bytes, _next, _lastAudio;
    private int _generation;
    private bool _enabled;
    private AudioMixer? _audio;
    private VideoOptions _audioOptions = new();
    public bool Enabled { get { lock (_gate) return _enabled; } }
    public int Fps { get; private set; } = 15;
    public int Seconds { get; private set; } = 30;
    public int MemoryMb { get; private set; } = 256;
    public bool Economy { get; private set; } = true;
    public string? Error { get; private set; }
    public long Bytes { get { lock (_gate) return _bytes; } }
    public long EvictedFrames { get; private set; }
    public ReplayBuffer() => _worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    public void Configure(Settings settings)
    {
        lock (_gate)
        {
            Clear(); _enabled = settings.ReplayEnabled; Seconds = settings.ReplaySeconds is 15 or 30 or 60 ? settings.ReplaySeconds : 30;
            MemoryMb = Math.Clamp(settings.ReplayMemoryMb, 64, 1024); Economy = settings.ReplayEconomy;
            Fps = Economy ? 15 : 30; _audioOptions = settings.Video.Normalize();
        }
    }
    public void Submit(BitmapSource image)
    {
        lock (_gate)
        {
            var now = _clock.Elapsed.Ticks;
            if (!_enabled || now < _next) return;
            _next = now + TimeSpan.TicksPerSecond / Fps;
            _pending.TryAdd((image, now, _generation));
        }
    }
    private void Run()
    {
        foreach (var packet in _pending.GetConsumingEnumerable())
        {
            try
            {
                int width; lock (_gate) { if (!_enabled || packet.Generation != _generation) continue; width = Economy ? 960 : 1920; }
                var image = new VideoOptions(MaxWidth: width).Resize(packet.Image);
                var jpeg = Images.EncodeJpeg(image);
                lock (_gate)
                {
                    if (!_enabled || packet.Generation != _generation) continue;
                    if (_frames.TryPeek(out var previous) && (previous.Frame.Width != image.PixelWidth || previous.Frame.Height != image.PixelHeight))
                    { _frames.Clear(); _bytes = 0; _lastAudio = 0; _audio?.Clear(); }
                    if (_audio == null && _audioOptions.Audio) _audio = new AudioMixer(_audioOptions);
                    var audioStart = _lastAudio == 0 ? packet.Ticks : _lastAudio;
                    var count = (int)Math.Clamp((packet.Ticks + TimeSpan.TicksPerSecond / Fps - audioStart) * 48000 / TimeSpan.TicksPerSecond, 0, 96000);
                    var audio = _audio?.Read(count);
                    _lastAudio = audioStart + count * TimeSpan.TicksPerSecond / 48000;
                    var frame = new ReplayFrame(packet.Ticks, new CaptureFrame(DateTimeOffset.Now, "replay", image.PixelWidth, image.PixelHeight, jpeg), audio, audioStart);
                    _frames.Enqueue(frame); _bytes += Size(frame);
                    while (_frames.Count > 0 && (_bytes > MemoryMb * 1024L * 1024 || packet.Ticks - _frames.Peek().Ticks > Seconds * TimeSpan.TicksPerSecond)) { _bytes -= Size(_frames.Dequeue()); EvictedFrames++; }
                    Error = null;
                }
            }
            catch (Exception e) { lock (_gate) { Error = e.Message; _enabled = false; _audio?.Dispose(); _audio = null; } }
        }
    }
    private static long Size(ReplayFrame frame) => frame.Frame.EncodedImage.LongLength + (frame.Audio?.LongLength ?? 0);
    public ReplayFrame[] Snapshot() { lock (_gate) return _frames.Where(f => _clock.Elapsed.Ticks - f.Ticks <= Seconds * TimeSpan.TicksPerSecond).ToArray(); }
    public void Clear() { lock (_gate) { _generation++; _frames.Clear(); _bytes = 0; _next = 0; _lastAudio = 0; _audio?.Dispose(); _audio = null; } }
    public static string Export(string destination, ReplayFrame[] frames, int fps, bool audio)
    {
        if (frames.Length == 0) throw new InvalidOperationException("Видеобуфер пока пуст.");
        using var writer = new Mp4Writer(destination, frames[0].Frame.Width, frames[0].Frame.Height, fps, audio && frames.Any(f => f.Audio != null));
        var origin = frames[0].Ticks;
        foreach (var frame in frames)
        {
            writer.Write(Images.Decode(frame.Frame), frame.Ticks - origin);
            if (audio && frame.Audio != null) {
                var start = frame.AudioTicks - origin;
                var skip = start < 0 ? (int)Math.Min(frame.Audio.Length, (-start * 48000 / TimeSpan.TicksPerSecond) * 4) : 0;
                writer.WriteAudio(skip == 0 ? frame.Audio : frame.Audio.Skip(skip).ToArray(), Math.Max(0, start));
            }
        }
        return writer.Complete();
    }
    public void Dispose() { _pending.CompleteAdding(); _worker.GetAwaiter().GetResult(); Clear(); _pending.Dispose(); }
}

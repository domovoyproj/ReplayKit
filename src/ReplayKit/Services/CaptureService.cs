using System.Diagnostics;

namespace ReplayKit.Services;

public sealed class CaptureService : IDisposable
{
    private sealed class VideoRequest(string path)
    {
        public string Path { get; } = path;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string?> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stopwatch Waiting { get; } = Stopwatch.StartNew();
    }
    public FrameBuffer Buffer { get; } = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Task _worker;
    private readonly Func<string, uint, BitmapSource?>? _captureFrame;
    private string _monitor;
    private bool _recording = true, _locked, _stopVideo, _videoStarted;
    private int _generation;
    private long _videoTicks;
    private VideoRequest? _video;
    public event Action? StateChanged;
    public event Action<string>? VideoSaved;
    public string? Error { get; private set; }
    public string? VideoError { get; private set; }
    public bool Recording { get { lock (_gate) return _recording && !_locked; } }
    public bool VideoRecording { get { lock (_gate) return _videoStarted; } }
    public bool VideoPending { get { lock (_gate) return _video != null; } }
    public TimeSpan VideoDuration => TimeSpan.FromTicks(Interlocked.Read(ref _videoTicks));
    public string MonitorId { get { lock (_gate) return _monitor; } }
    public CaptureService(string monitor) : this(monitor, null) { }
    internal CaptureService(string monitor, Func<string, uint, BitmapSource?>? captureFrame)
    {
        _monitor = monitor;
        _captureFrame = captureFrame;
        _locked = SessionState.IsLocked;
        // DXGI and Media Foundation share one owner thread and one desktop duplication.
        _worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public Task StartVideoAsync(string path)
    {
        lock (_gate)
        {
            if (_locked) throw new InvalidOperationException("Сеанс Windows заблокирован.");
            if (_video != null) throw new InvalidOperationException("Видеозапись уже запущена.");
            _video = new VideoRequest(path); _stopVideo = false; VideoError = null;
            Interlocked.Exchange(ref _videoTicks, 0);
            _wake.Set(); StateChanged?.Invoke(); return _video.Started.Task;
        }
    }
    public Task<string?> StopVideoAsync()
    {
        lock (_gate)
        {
            if (_video == null) return Task.FromResult<string?>(null);
            _stopVideo = true; _wake.Set(); return _video.Finished.Task;
        }
    }
    public void SelectMonitor(string monitor)
    {
        lock (_gate) { _monitor = monitor; _generation++; Buffer.Clear(); Error = null; _stopVideo = true; }
        _wake.Set(); StateChanged?.Invoke();
    }
    public void Toggle()
    {
        lock (_gate) { _recording = !_recording; _generation++; }
        _wake.Set(); StateChanged?.Invoke();
    }
    public void SetLocked(bool locked)
    {
        lock (_gate) { _locked = locked; _generation++; if (locked) { Buffer.Clear(); _stopVideo = true; } }
        _wake.Set(); StateChanged?.Invoke();
    }
    private void Run()
    {
        using var capture = new DxgiCapture();
        Mp4Writer? writer = null; VideoRequest? current = null;
        BitmapSource? encodedBitmap = null; CaptureFrame? encoded = null;
        var clock = Stopwatch.StartNew(); var videoClock = new Stopwatch(); long nextHistory = 0, nextNotify = 0;
        void FinishVideo(Exception? error = null)
        {
            if (current == null) return;
            string? saved = null;
            try { if (writer != null && writer.FrameCount > 0) saved = writer.Complete(); }
            catch (Exception e) { error ??= e; }
            finally { try { writer?.Dispose(); } catch (Exception e) { error ??= e; } writer = null; }
            var completed = current; current = null;
            lock (_gate) { if (_video == completed) _video = null; _videoStarted = false; _stopVideo = false; VideoError = error?.Message; }
            if (!completed.Started.Task.IsCompleted)
            {
                if (error != null) completed.Started.TrySetException(error); else completed.Started.TrySetCanceled();
            }
            completed.Finished.TrySetResult(saved);
            if (saved != null) VideoSaved?.Invoke(saved);
            StateChanged?.Invoke();
        }
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var loopStart = clock.ElapsedMilliseconds;
                string monitor; int generation; bool historyActive, locked, stopVideo; VideoRequest? requested;
                lock (_gate) { monitor = _monitor; generation = _generation; locked = _locked; historyActive = _recording && !locked; requested = _video; stopVideo = _stopVideo; }
                current ??= requested;
                if (current != null && (stopVideo || locked)) FinishVideo();
                var videoActive = current != null && !locked;
                if (!historyActive && !videoActive)
                {
                    capture.Dispose(); encodedBitmap = null; encoded = null;
                    _wake.WaitOne(1000); continue;
                }
                try
                {
                    var timeout = videoActive ? 15u : 100u;
                    var bitmap = _captureFrame != null ? _captureFrame(monitor, timeout) : capture.CaptureBitmap(monitor, timeout);
                    lock (_gate) { if (_generation != generation) continue; }
                    if (bitmap != null)
                    {
                        if (videoActive)
                        {
                            if (writer == null) { writer = new Mp4Writer(current!.Path, bitmap.PixelWidth, bitmap.PixelHeight); videoClock.Restart(); }
                            writer.Write(bitmap, videoClock.Elapsed.Ticks);
                            Interlocked.Exchange(ref _videoTicks, videoClock.Elapsed.Ticks);
                            lock (_gate) _videoStarted = true;
                            current!.Started.TrySetResult(true);
                        }
                        if (historyActive && clock.ElapsedMilliseconds >= nextHistory)
                        {
                            if (!ReferenceEquals(encodedBitmap, bitmap))
                            {
                                encoded = new CaptureFrame(DateTimeOffset.Now, monitor, bitmap.PixelWidth, bitmap.PixelHeight, Images.EncodeJpeg(bitmap)); encodedBitmap = bitmap;
                            }
                            lock (_gate) { if (_generation == generation) Buffer.Add(encoded! with { CapturedAt = DateTimeOffset.Now }); }
                            // Keep the one-second cadence anchored to the clock. Adding a
                            // second after JPEG encoding can accidentally skip the next tick.
                            nextHistory = (clock.ElapsedMilliseconds / 1000 + 1) * 1000;
                        }
                        lock (_gate) Error = null;
                    }
                    else if (videoActive && current!.Waiting.Elapsed > TimeSpan.FromSeconds(10))
                        FinishVideo(new InvalidOperationException("Не получен полноценный кадр экрана. Проверьте выбранный монитор и сеанс Windows."));
                }
                catch (Exception e)
                {
                    if (current != null) FinishVideo(e);
                    capture.Dispose(); encodedBitmap = null; encoded = null;
                    lock (_gate) Error = $"Захват недоступен ({e.HResult:X8}). Проверьте монитор или сеанс Windows.";
                }
                if (clock.ElapsedMilliseconds >= nextNotify) { StateChanged?.Invoke(); nextNotify = clock.ElapsedMilliseconds + 500; }
                var period = current == null ? 1000 : 1000 / Mp4Writer.FrameRate;
                _wake.WaitOne((int)Math.Max(0, period - (clock.ElapsedMilliseconds - loopStart)));
            }
        }
        finally { current ??= _video; FinishVideo(); }
    }
    public void Dispose()
    {
        _stop.Cancel(); _wake.Set(); _worker.GetAwaiter().GetResult(); _wake.Dispose(); _stop.Dispose(); Buffer.Clear();
    }
}

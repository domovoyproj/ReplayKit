using System.Diagnostics;

namespace ReplayKit.Services;

public sealed class CaptureService : IDisposable
{
    private sealed class VideoRequest(string path, VideoOptions options)
    {
        public string Path { get; } = path;
        public VideoOptions Options { get; } = options;
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
    private bool _videoPaused;
    public VideoOptions VideoOptions { get; set; } = new();
    public ReplayBuffer Replay { get; } = new();
    private Int32Rect? _region;
    private Windows.Graphics.Capture.GraphicsCaptureItem? _item;
    public bool IsMonitorSource { get { lock (_gate) return _item == null && _region == null; } }
    public Int32Rect? Region { get { lock (_gate) return _region; } }
    public void SelectSource(Windows.Graphics.Capture.GraphicsCaptureItem? item, Int32Rect? region = null)
    {
        lock (_gate) { _item = item; _region = region; _generation++; Buffer.Clear(); Replay.Clear(); _stopVideo = true; }
        _wake.Set(); StateChanged?.Invoke();
    }
    public bool VideoPaused { get { lock (_gate) return _videoPaused; } }
    public bool Locked { get { lock (_gate) return _locked; } }
    public string Status => Locked ? "Сеанс Windows заблокирован" : Error ?? (Recording ? "Ожидание кадра экрана" : "Буфер на паузе");
    public void ToggleVideoPause() { lock (_gate) { if (_videoStarted) _videoPaused = !_videoPaused; } _wake.Set(); StateChanged?.Invoke(); }
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
        // Capture owns DXGI/WGC; history, replay compression and MP4 each have bounded worker queues.
        _worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public Task StartVideoAsync(string path)
    {
        lock (_gate)
        {
            if (_locked) throw new InvalidOperationException("Сеанс Windows заблокирован.");
            if (_video != null) throw new InvalidOperationException("Видеозапись уже запущена.");
            _video = new VideoRequest(path, VideoOptions.Normalize()); _stopVideo = false; _videoPaused = false; VideoError = null;
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
        lock (_gate) { _monitor = monitor; _item = null; _region = null; _generation++; Buffer.Clear(); Replay.Clear(); Error = null; _stopVideo = true; }
        _wake.Set(); StateChanged?.Invoke();
    }
    public void Toggle()
    {
        lock (_gate) { _recording = !_recording; _generation++; }
        _wake.Set(); StateChanged?.Invoke();
    }
    public void SetLocked(bool locked)
    {
        lock (_gate) { _locked = locked; _generation++; if (locked) { Buffer.Clear(); Replay.Clear(); _stopVideo = true; } }
        _wake.Set(); StateChanged?.Invoke();
    }
    private void Run()
    {
        using var capture = new DxgiCapture();
        using var historyEncoder = new HistoryEncoder((frame, generation) => { lock (_gate) { if (_generation == generation && !_locked && _recording) Buffer.Add(frame); } },
            e => { lock (_gate) Error = $"Не удалось подготовить кадр ({e.HResult:X8})."; });
        WindowCapture? windowCapture = null; Windows.Graphics.Capture.GraphicsCaptureItem? activeItem = null;
        VideoSession? writer = null; VideoRequest? current = null;

        var clock = Stopwatch.StartNew(); var videoClock = new Stopwatch(); long nextHistory = 0, nextNotify = 0;
        void FinishVideo(Exception? error = null)
        {
            if (current == null) return;
            string? saved = null;
            try { if (writer != null) { writer.Stop(); saved = writer.Finished.GetAwaiter().GetResult(); if (writer.Error != null) error ??= new IOException(writer.Error); } }
            catch (Exception e) { error ??= e; }
            finally { writer = null; }
            var completed = current; current = null;
            lock (_gate) { if (_video == completed) _video = null; _videoStarted = false; _videoPaused = false; _stopVideo = false; VideoError = error?.Message; }
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
                string monitor; int generation; Windows.Graphics.Capture.GraphicsCaptureItem? item; Int32Rect? region; bool historyActive, locked, stopVideo; VideoRequest? requested;
                lock (_gate) { monitor = _monitor; item = _item; region = _region; generation = _generation; locked = _locked; historyActive = _recording && !locked; requested = _video; stopVideo = _stopVideo; }
                current ??= requested;
                if (current != null && (stopVideo || locked)) FinishVideo();
                var videoActive = current != null && !locked;
                if (writer?.Finished.IsCompleted == true) { FinishVideo(); videoActive = false; }
                if (!historyActive && !videoActive && (!Replay.Enabled || locked))
                {
                    capture.Dispose(); windowCapture?.Dispose(); windowCapture = null; activeItem = null;
                    _wake.WaitOne(1000); continue;
                }
                try
                {
                    var timeout = videoActive || Replay.Enabled ? 8u : 100u;
                    if (activeItem != item) { windowCapture?.Dispose(); windowCapture = null; activeItem = item; capture.Dispose(); }
                    if (item != null) windowCapture ??= new WindowCapture(item);
                    var bitmap = _captureFrame != null ? _captureFrame(monitor, timeout) : item != null ? windowCapture!.Capture() : capture.CaptureBitmap(monitor, timeout);
                    lock (_gate) { if (_generation != generation) continue; }
                    if (bitmap != null)
                    {
                        if (region is { } crop) bitmap = Images.Crop(bitmap, crop);
                        lock (_gate)
                        {
                            if (_generation != generation) continue;
                            if (!_locked && Replay.Enabled) Replay.Submit(bitmap);
                        }
                        if (videoActive)
                        {
                            if (writer == null) { writer = new VideoSession(current!.Path, current.Options); videoClock.Restart(); }
                            if (VideoPaused) { videoClock.Stop(); writer.Pause(); }
                            else { if (!videoClock.IsRunning) { writer.Pause(); videoClock.Start(); } writer.Submit(bitmap, videoClock.Elapsed.Ticks); }
                            Interlocked.Exchange(ref _videoTicks, videoClock.Elapsed.Ticks);
                            if (writer.Started.Task.IsCompletedSuccessfully) { lock (_gate) _videoStarted = true; current!.Started.TrySetResult(true); }
                        }
                        if (historyActive && clock.ElapsedMilliseconds >= nextHistory)
                        {
                            historyEncoder.Submit(bitmap, monitor, DateTimeOffset.Now, generation);
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
                    capture.Dispose();
                    lock (_gate) Error = e is InvalidOperationException ? e.Message : $"Захват недоступен ({e.HResult:X8}). Проверьте источник и сеанс Windows.";
                }
                if (clock.ElapsedMilliseconds >= nextNotify) { StateChanged?.Invoke(); nextNotify = clock.ElapsedMilliseconds + 500; }
                var fps = Math.Max(current?.Options.Fps ?? 1, Replay.Enabled && !locked ? Replay.Fps : 1);
                var period = (int)Math.Ceiling(1000d / fps);
                var wait = Math.Max(0, period - (clock.ElapsedMilliseconds - loopStart));
                if (current == null && historyActive)
                    wait = Math.Max(1, Math.Min(wait, nextHistory - clock.ElapsedMilliseconds));
                _wake.WaitOne((int)wait);
            }
        }
        finally { current ??= _video; FinishVideo(); windowCapture?.Dispose(); }
    }
    public void Dispose()
    {
        _stop.Cancel(); _wake.Set(); _worker.GetAwaiter().GetResult(); _wake.Dispose(); _stop.Dispose(); Buffer.Clear(); Replay.Dispose();
    }
}

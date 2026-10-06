namespace ReplayKit.Services;

public sealed class CaptureService : IDisposable
{
    public FrameBuffer Buffer { get; } = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private string _monitor;
    private bool _recording = true;
    private bool _locked;
    private int _generation;
    public event Action? StateChanged;
    public string? Error { get; private set; }
    public bool Recording { get { lock (_gate) return _recording && !_locked; } }
    public string MonitorId { get { lock (_gate) return _monitor; } }
    public CaptureService(string monitor) { _monitor = monitor; _worker = Task.Run(RunAsync); }
    public void SelectMonitor(string monitor)
    {
        lock (_gate) { _monitor = monitor; _generation++; Buffer.Clear(); Error = null; }
        StateChanged?.Invoke();
    }
    public void Toggle()
    {
        lock (_gate) { _recording = !_recording; _generation++; }
        StateChanged?.Invoke();
    }
    public void SetLocked(bool locked)
    {
        lock (_gate) { _locked = locked; _generation++; if (locked) Buffer.Clear(); }
        StateChanged?.Invoke();
    }
    private async Task RunAsync()
    {
        using var capture = new DxgiCapture();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                string monitor; int generation; bool active;
                lock (_gate) { monitor = _monitor; generation = _generation; active = _recording && !_locked; }
                if (!active) { capture.Dispose(); continue; }
                try
                {
                    var frame = capture.Capture(monitor, DateTimeOffset.Now);
                    lock (_gate)
                    {
                        if (_generation != generation) continue;
                        if (frame != null) Buffer.Add(frame);
                        Error = null;
                    }
                }
                catch (Exception e)
                {
                    capture.Dispose();
                    lock (_gate) Error = $"Захват недоступен ({e.HResult:X8}). Проверьте монитор или сеанс Windows.";
                }
                StateChanged?.Invoke();
            } while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) { }
    }
    public void Dispose()
    {
        _stop.Cancel(); _worker.GetAwaiter().GetResult(); _stop.Dispose(); Buffer.Clear();
    }
}

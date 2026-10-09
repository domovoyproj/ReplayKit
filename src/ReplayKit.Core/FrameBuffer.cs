namespace ReplayKit.Core;

public sealed class FrameBuffer
{
    private CaptureFrame?[] _frames;
    private readonly object _gate = new();
    private int _next;
    private int _count;
    public FrameBuffer(int capacity = 60)
    {
        _frames = new CaptureFrame[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    }
    public int Capacity { get { lock (_gate) return _frames.Length; } }
    public int Count { get { lock (_gate) return _count; } }
    public void Add(CaptureFrame frame)
    {
        lock (_gate)
        {
            _frames[_next] = frame;
            _next = (_next + 1) % _frames.Length;
            _count = Math.Min(_count + 1, _frames.Length);
        }
    }
    public CaptureFrame[] Snapshot(DateTimeOffset now)
    {
        lock (_gate)
        {
            var result = new List<CaptureFrame>(_count);
            for (var i = 0; i < _count; i++)
            {
                var frame = _frames[(_next - _count + i + _frames.Length) % _frames.Length]!;
                if (now - frame.CapturedAt < TimeSpan.FromSeconds(_frames.Length) && frame.CapturedAt <= now) result.Add(frame);
            }
            return result.ToArray();
        }
    }
    public void Resize(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_gate)
        {
            if (capacity == _frames.Length) return;
            var keep = Math.Min(capacity, _count);
            var replacement = new CaptureFrame?[capacity];
            for (var i = 0; i < keep; i++)
                replacement[i] = _frames[(_next - keep + i + _frames.Length) % _frames.Length];
            _frames = replacement;
            _count = keep;
            _next = keep % capacity;
        }
    }
    public void Clear()
    {
        lock (_gate) { Array.Clear(_frames); _next = _count = 0; }
    }
}

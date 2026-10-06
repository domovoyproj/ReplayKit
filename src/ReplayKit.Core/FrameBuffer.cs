namespace ReplayKit.Core;

public sealed class FrameBuffer(int capacity = 60)
{
    private readonly CaptureFrame?[] _frames = new CaptureFrame[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private readonly object _gate = new();
    private int _next;
    private int _count;
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
                if (now - frame.CapturedAt < TimeSpan.FromSeconds(60) && frame.CapturedAt <= now) result.Add(frame);
            }
            return result.ToArray();
        }
    }
    public void Clear()
    {
        lock (_gate) { Array.Clear(_frames); _next = _count = 0; }
    }
}

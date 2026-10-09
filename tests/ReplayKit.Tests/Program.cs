using ReplayKit.Core;

var passed = 0;
void Test(string name, Action check) { check(); passed++; Console.WriteLine($"PASS {name}"); }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
var now = DateTimeOffset.UtcNow;
CaptureFrame Frame(int offset) => new(now.AddSeconds(offset), "monitor-1", 1920, 1080, new byte[] { 1, 2, 3 });
Test("Ring retains 60 chronological frames after multiple wraps", () => {
    var buffer = new FrameBuffer(); for (var i = -200; i <= 0; i++) buffer.Add(Frame(i));
    var frames = buffer.Snapshot(now); Assert(frames.Length == 60); Assert(frames[0].CapturedAt == now.AddSeconds(-59)); Assert(frames[^1].CapturedAt == now);
});
Test("Snapshot stays fixed while capture continues", () => {
    var buffer = new FrameBuffer(3); for (var i = -2; i <= 0; i++) buffer.Add(Frame(i));
    var snapshot = buffer.Snapshot(now); buffer.Add(Frame(1)); buffer.Clear(); Assert(snapshot.Length == 3); Assert(snapshot[^1].CapturedAt == now); Assert(buffer.Snapshot(now).Length == 0);
});
Test("Expired and future frames are filtered", () => {
    var buffer = new FrameBuffer(); buffer.Add(Frame(-60)); buffer.Add(Frame(-59)); buffer.Add(Frame(1)); Assert(buffer.Snapshot(now).Length == 1);
});
Test("Invalid capacity fails explicitly", () => { try { _ = new FrameBuffer(0); throw new Exception("Expected error"); } catch (ArgumentOutOfRangeException) { } });
Test("Resize preserves the newest frames and updates retention", () => {
    var buffer = new FrameBuffer(5); for (var i = -4; i <= 0; i++) buffer.Add(Frame(i));
    buffer.Resize(3); var smaller = buffer.Snapshot(now); Assert(buffer.Capacity == 3 && buffer.Count == 3); Assert(smaller.Select(f => f.CapturedAt).SequenceEqual(new[] { now.AddSeconds(-2), now.AddSeconds(-1), now }));
    buffer.Resize(6); buffer.Add(Frame(1)); Assert(buffer.Capacity == 6 && buffer.Snapshot(now.AddSeconds(1)).Length == 4);
});
Test("Concurrent readers and writer preserve order", () => {
    var buffer = new FrameBuffer();
    Parallel.Invoke(() => { for (var i = 0; i < 10000; i++) buffer.Add(new(now.AddTicks(i), "m", 10, 10, Array.Empty<byte>())); }, () => {
        for (var i = 0; i < 10000; i++) { var frames = buffer.Snapshot(now.AddSeconds(1)); Assert(frames.Length <= 60); Assert(frames.Zip(frames.Skip(1)).All(pair => pair.First.CapturedAt <= pair.Second.CapturedAt)); }
    });
});
Test("Hotkey validation and rendering", () => {
    Assert(HotkeySpec.History.ToString() == "Ctrl + Shift + R"); Assert(HotkeySpec.History.IsValid);
    Assert(!new HotkeySpec(0, 0x52).IsValid); Assert(!new HotkeySpec(6, 0x10).IsValid); Assert(new HotkeySpec(1, 0x70).ToString() == "Alt + F1");
});
Console.WriteLine($"{passed} tests passed.");

using NAudio.Wave;

namespace ReplayKit.Services;

internal sealed class ReplayPlayback : IDisposable
{
    private readonly WaveOutEvent _output = new();
    private readonly RawSourceWaveStream _stream;
    public ReplayPlayback(ReplayFrame[] frames, int fps)
    {
        var origin = frames[0].Ticks;
        var length = (int)((frames[^1].Ticks - origin + TimeSpan.TicksPerSecond / fps) * 48000 / TimeSpan.TicksPerSecond) * 4;
        var pcm = new byte[length];
        foreach (var frame in frames)
        {
            if (frame.Audio == null) continue;
            var offset = (int)((frame.AudioTicks - origin) * 48000 / TimeSpan.TicksPerSecond) * 4;
            var skip = Math.Max(0, -offset); var target = Math.Max(0, offset);
            var count = Math.Min(frame.Audio.Length - skip, pcm.Length - target);
            if (count > 0) Array.Copy(frame.Audio, skip, pcm, target, count);
        }
        _stream = new RawSourceWaveStream(new MemoryStream(pcm, false), new WaveFormat(48000, 16, 2));
        try { _output.Init(_stream); _output.Play(); }
        catch { _output.Dispose(); _stream.Dispose(); throw; }
    }
    public void Dispose() { _output.Stop(); _output.Dispose(); _stream.Dispose(); }
}

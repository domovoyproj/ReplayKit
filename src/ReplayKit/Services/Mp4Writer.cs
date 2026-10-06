using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace ReplayKit.Services;

/// <summary>Windows' built-in H.264 encoder; no external executable or network dependency.</summary>
public sealed class Mp4Writer : IDisposable
{
    public const int FrameRate = 30;
    private static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    private readonly string _destination;
    private readonly string _temporary;
    private IMFSinkWriter? _writer;
    private int _stream;
    private bool _mfStarted;
    private bool _finished;
    private long _lastTimestamp = -1;
    private readonly int _sourceWidth, _sourceHeight;
    public int Width { get; }
    public int Height { get; }
    public int FrameCount { get; private set; }

    public Mp4Writer(string destination, int width, int height)
    {
        _destination = Path.GetFullPath(destination);
        _temporary = _destination + ".replaykit-" + Guid.NewGuid().ToString("N") + ".mp4";
        _sourceWidth = width; _sourceHeight = height;
        Width = width & ~1; Height = height & ~1;
        if (Width < 2 || Height < 2) throw new ArgumentOutOfRangeException(nameof(width));
        try
        {
            MFStartup().CheckError(); _mfStarted = true;
            using var options = MFCreateAttributes(1);
            options.Set(SinkWriterAttributeKeys.DisableThrottling, 0u).CheckError();
            _writer = MFCreateSinkWriterFromURL(_temporary, null, options);
            using var output = MFCreateMediaType();
            Configure(output, H264);
            output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)Math.Clamp((long)Width * Height * 5, 4_000_000, 24_000_000)).CheckError();
            _stream = _writer.AddStream(output);
            using var input = MFCreateMediaType(); Configure(input, Rgb32);
            input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(Width * 4)).CheckError();
            _writer.SetInputMediaType(_stream, input, null);
            _writer.BeginWriting();
        }
        catch { Dispose(); throw; }
    }
    private void Configure(IMFMediaType type, Guid subtype)
    {
        type.Set(MediaTypeAttributeKeys.MajorType, Video).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
        type.Set(MediaTypeAttributeKeys.InterlaceMode, 2u).CheckError(); // progressive
        MFSetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, (uint)Width, (uint)Height).CheckError();
        MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, FrameRate, 1).CheckError();
        MFSetAttributeRatio(type, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
    }
    public void Write(BitmapSource image, long timestampTicks)
    {
        if (_finished || _writer == null) throw new ObjectDisposedException(nameof(Mp4Writer));
        if (image.PixelWidth != _sourceWidth || image.PixelHeight != _sourceHeight) throw new InvalidOperationException("Разрешение монитора изменилось. Запись завершена.");
        BitmapSource source = image;
        if (image.Format != PixelFormats.Bgr32 && image.Format != PixelFormats.Bgra32)
            source = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        var length = Width * Height * 4;
        using var buffer = MFCreateMemoryBuffer(length);
        buffer.Lock(out var pointer, out _, out _);
        try { source.CopyPixels(new Int32Rect(0, 0, Width, Height), pointer, length, Width * 4); }
        finally { buffer.Unlock(); }
        buffer.CurrentLength = length;
        using var sample = MFCreateSample(); sample.AddBuffer(buffer);
        sample.SampleTime = Math.Max(timestampTicks, _lastTimestamp + 1);
        sample.SampleDuration = TimeSpan.TicksPerSecond / FrameRate;
        _writer.WriteSample(_stream, sample);
        _lastTimestamp = sample.SampleTime; FrameCount++;
    }
    public string Complete()
    {
        if (_finished) return _destination;
        if (_writer == null || FrameCount == 0) throw new InvalidOperationException("Нет кадров для сохранения видео.");
        _writer.Finalize(); _finished = true;
        _writer.Dispose(); _writer = null;
        File.Move(_temporary, _destination, true);
        return _destination;
    }
    public void Dispose()
    {
        _writer?.Dispose(); _writer = null;
        if (_mfStarted) { MFShutdown(); _mfStarted = false; }
        // A failed finalization must preserve an existing destination, without orphaned files.
        if (File.Exists(_temporary)) File.Delete(_temporary);
    }
}

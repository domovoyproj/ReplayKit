using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace ReplayKit.Services;

/// <summary>Bounded WASAPI inputs, mixed as 48 kHz stereo PCM16. No audio files.</summary>
internal sealed class AudioMixer : IDisposable
{
    private readonly List<(WasapiCapture Capture, BufferedWaveProvider Buffer, double Volume)> _inputs = new();
    private Exception? _error;
    public AudioMixer(VideoOptions options)
    {
        try
        {
            if (options.SystemAudio) Add(new WasapiLoopbackCapture(), options.SystemVolume);
            if (options.Microphone) Add(new WasapiCapture(), options.MicrophoneVolume);
        }
        catch { Dispose(); throw; }
    }
    private void Add(WasapiCapture capture, double volume)
    {
        capture.WaveFormat = new WaveFormat(48000, 16, 2);
        var buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = true, ReadFully = true };
        _inputs.Add((capture, buffer, volume));
        capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        capture.RecordingStopped += (_, e) => { if (e.Exception != null) _error = e.Exception; };
        capture.StartRecording();
    }
    public byte[] Read(int frames)
    {
        if (_error != null) throw new IOException("Аудиоустройство отключено. Запись завершена.", _error);
        var sums = new int[frames * 2]; var data = new byte[frames * 4];
        foreach (var input in _inputs)
        {
            input.Buffer.Read(data, 0, data.Length);
            for (var i = 0; i < sums.Length; i++) sums[i] += (int)(BitConverter.ToInt16(data, i * 2) * input.Volume);
        }
        for (var i = 0; i < sums.Length; i++)
        {
            var value = (short)Math.Clamp(sums[i], short.MinValue, short.MaxValue);
            data[i * 2] = (byte)value; data[i * 2 + 1] = (byte)(value >> 8);
        }
        return data;
    }
    public void Clear() { foreach (var input in _inputs) input.Buffer.ClearBuffer(); }
    public void Dispose()
    {
        foreach (var input in _inputs) { input.Capture.StopRecording(); input.Capture.Dispose(); }
        _inputs.Clear();
    }
}

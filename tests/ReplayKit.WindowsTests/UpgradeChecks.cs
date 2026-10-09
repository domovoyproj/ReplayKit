using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using ReplayKit.Services;
using ReplayKit.Views;
using System.Windows;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

internal static class UpgradeChecks
{
    public static async Task Run(BitmapSource image, Action<string, bool> check, Action<Window, string> render)
    {
        await Task.Run(() =>
        {
            var destination = Path.GetFullPath("artifacts/qa/recover.mp4");
            File.WriteAllText(destination, "original"); string? recovery;
            using (var locked = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using var writer = new Mp4Writer(destination, image.PixelWidth, image.PixelHeight);
                writer.Write(image, 0);
                try { writer.Complete(); throw new Exception("Destination should be locked"); } catch (IOException) { }
                recovery = writer.RecoveryPath;
                writer.Dispose();
                check("Finalized MP4 survives destination failure and disposal", recovery != null && File.Exists(recovery) && VideoChecks.Decode(recovery).Frames == 1);
            }
            check("Failed save preserves the original destination", File.ReadAllText(destination) == "original");
            File.Delete(recovery!);
            foreach (var fps in new[] { 15, 60 })
            {
                var path = Path.GetFullPath($"artifacts/qa/video-{fps}.mp4");
                using (var writer = new Mp4Writer(path, image.PixelWidth, image.PixelHeight, fps))
                { for (var i = 0; i < fps; i++) writer.Write(image, i * TimeSpan.TicksPerSecond / fps); writer.Complete(); }
                var movie = VideoChecks.Decode(path);
                check($"{fps} FPS MP4 preserves sample count and timing", movie.Frames == fps && Math.Abs(movie.LastTime - (fps - 1) * TimeSpan.TicksPerSecond / fps) < 10000);
            }
            var audioPath = Path.GetFullPath("artifacts/qa/audio-video.mp4");
            using (var writer = new Mp4Writer(audioPath, image.PixelWidth, image.PixelHeight, audio: true))
            {
                for (var f = 0; f < 30; f++)
                {
                    writer.Write(image, f * TimeSpan.TicksPerSecond / 30);
                    var pcm = new byte[1600 * 4];
                    for (var i = 0; i < 1600; i++)
                    {
                        var sample = (short)(Math.Sin((f * 1600 + i) * 2 * Math.PI * 440 / 48000) * 12000);
                        pcm[i * 4] = pcm[i * 4 + 2] = (byte)sample; pcm[i * 4 + 1] = pcm[i * 4 + 3] = (byte)(sample >> 8);
                    }
                    writer.WriteAudio(pcm, f * TimeSpan.TicksPerSecond / 30);
                }
                writer.Complete();
            }
            var audio = DecodeAudio(audioPath);
            check("MP4 includes decodable non-silent AAC stereo audio", audio.Bytes > 150000 && audio.Peak > 5000);
            var archivePath = Path.GetFullPath("artifacts/qa/history.zip");
            var at = DateTimeOffset.Now;
            HistoryArchive.Export(archivePath, new[] {
                new ReplayKit.Core.CaptureFrame(at.AddSeconds(-1), "display", image.PixelWidth, image.PixelHeight, Images.EncodeJpeg(image)),
                new ReplayKit.Core.CaptureFrame(at, "display", image.PixelWidth, image.PixelHeight, Images.EncodeJpeg(image)) });
            using var archive = ZipFile.OpenRead(archivePath);
            check("History export creates PNG frames and timestamp manifest", archive.Entries.Count == 3 && archive.GetEntry("history.json") != null && archive.Entries.Count(e => e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) == 2);
        });
        using (var capture = new CaptureService("test", (_, _) => image))
        {
            capture.SetLocked(false);
            var destination = Path.GetFullPath("artifacts/qa/paused-video.mp4");
            await capture.StartVideoAsync(destination).WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(450); capture.ToggleVideoPause(); await Task.Delay(120);
            var before = capture.VideoDuration; await Task.Delay(800);
            check("Video pause freezes elapsed recording time", capture.VideoPaused && Math.Abs((capture.VideoDuration - before).TotalMilliseconds) < 60);
            capture.ToggleVideoPause(); await Task.Delay(450);
            var activeDuration = capture.VideoDuration;
            await capture.StopVideoAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var movie = await Task.Run(() => VideoChecks.Decode(destination));
            var durationError = Math.Abs(movie.LastTime - activeDuration.Ticks);
            check("Resumed MP4 excludes paused time", movie.LastTime > before.Ticks && durationError < TimeSpan.FromMilliseconds(250).Ticks);
            capture.Replay.Configure(new Settings { ReplayEnabled = true, ReplaySeconds = 15, ReplayMemoryMb = 64 });
            for (var i = 0; i < 200 && capture.Replay.Snapshot().Length < 8 && capture.Replay.Error == null; i++) await Task.Delay(50);
            var snapshot = capture.Replay.Snapshot();
            Console.WriteLine($"Replay startup: frames={snapshot.Length}, bytes={capture.Replay.Bytes}, error={capture.Replay.Error ?? "none"}");
            check("Replay collects compressed frames within RAM budget", snapshot.Length > 5 && capture.Replay.Bytes <= 64L * 1024 * 1024 && snapshot[0].Frame.Width <= 960);
            var replayUi = new ReplayWindow(snapshot, 15); replayUi.Show(); await Task.Delay(150); render(replayUi, "replay-dark");
            check("Replay viewer opens a frozen timeline", replayUi.IsVisible); replayUi.Close();
            var first = snapshot[0]; await Task.Delay(200); capture.Replay.Clear();
            check("Frozen replay survives live buffer clear", snapshot[0] == first && capture.Replay.Snapshot().Length == 0);
            var selected = snapshot.Skip(2).Take(5).ToArray();
            var exported = Path.GetFullPath("artifacts/qa/replay-trim.mp4");
            await Task.Run(() => ReplayBuffer.Export(exported, selected, 15, false));
            var trim = await Task.Run(() => VideoChecks.Decode(exported));
            check("Replay trim exports only selected frames with relative time", trim.Frames >= 5 && Math.Abs(trim.LastTime - (selected[^1].Ticks - selected[0].Ticks)) <= TimeSpan.TicksPerSecond / 15);
            capture.SetLocked(true); await Task.Delay(150);
            check("Lock clears replay and prevents refilling", capture.Replay.Snapshot().Length == 0);
        }
        var pin = new ImageToolsWindow(image); pin.Show(); await Task.Delay(80); render(pin, "pin-dark"); check("Pinned frame stays above other windows", pin.Topmost); pin.Close();
        var compare = new ImageToolsWindow(image, VideoChecks.Solid(1024, 640)); compare.Show(); await Task.Delay(80); render(compare, "compare-dark"); compare.Close();
        var text = await TextRecognition.ReadAsync(image);
        check("Local Windows OCR recognizes synthetic screenshot", text.Contains("ReplayKit", StringComparison.OrdinalIgnoreCase));
        using (var pressure = new ReplayBuffer())
        {
            var pixels = new byte[960 * 540 * 4]; new Random(711).NextBytes(pixels);
            var noise = BitmapSource.Create(960, 540, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, pixels, 960 * 4); noise.Freeze();
            pressure.Configure(new Settings { ReplayEnabled = true, ReplaySeconds = 60, ReplayMemoryMb = 64, ReplayEconomy = false });
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(30) && pressure.EvictedFrames == 0) { pressure.Submit(noise); await Task.Delay(35); }
            check("High-entropy replay evicts frames at the configured RAM limit", pressure.EvictedFrames > 0 && pressure.Bytes <= 64L * 1024 * 1024);
            var smaller = VideoChecks.Solid(400, 300);
            for (var i = 0; i < 4; i++) { pressure.Submit(smaller); await Task.Delay(100); }
            check("Source resize resets incompatible replay frames", pressure.Snapshot().Length > 0 && pressure.Snapshot().All(f => f.Frame.Width == 400));
        }
    }
    private static (long Bytes, int Peak) DecodeAudio(string path)
    {
        MFStartup().CheckError();
        try
        {
            using var reader = MFCreateSourceReaderFromURL(path, null);
            using var type = MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.MajorType, new Guid("73647561-0000-0010-8000-00aa00389b71")).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, new Guid("00000001-0000-0010-8000-00aa00389b71")).CheckError();
            reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, type);
            long bytes = 0; int peak = 0;
            while (true)
            {
                using var sample = reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (sample != null)
                {
                    using var buffer = sample.ConvertToContiguousBuffer(); buffer.Lock(out var pointer, out _, out var length);
                    try
                    {
                        var data = new byte[length]; Marshal.Copy(pointer, data, 0, length); bytes += length;
                        for (var i = 0; i + 1 < data.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(data, i)));
                    }
                    finally { buffer.Unlock(); }
                }
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            }
            return (bytes, peak);
        }
        finally { MFShutdown(); }
    }
}

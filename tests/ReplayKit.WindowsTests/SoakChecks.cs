using System.Diagnostics;
using System.IO;
using ReplayKit.Services;

internal static class SoakChecks
{
    public static async Task Run()
    {
        Directory.CreateDirectory("artifacts/qa");
        var image = VideoChecks.Solid(1024, 640);
        using var capture = new CaptureService("synthetic soak", (_, _) => image);
        capture.SetLocked(false);
        capture.Replay.Configure(new Settings { ReplayEnabled = true, ReplaySeconds = 60, ReplayMemoryMb = 64 });
        var path = Path.GetFullPath("artifacts/qa/soak-30min.mp4");
        await capture.StartVideoAsync(path).WaitAsync(TimeSpan.FromSeconds(15));
        long baseline = 0, last = 0;
        for (var minute = 1; minute <= 30; minute++)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            last = Process.GetCurrentProcess().PrivateMemorySize64;
            if (minute == 2) baseline = last;
            var line = $"minute={minute} privateMB={last / 1024 / 1024} replayMB={capture.Replay.Bytes / 1024 / 1024} duration={capture.VideoDuration.TotalSeconds:F1} history={capture.Buffer.Snapshot(DateTimeOffset.Now).Length}";
            Console.WriteLine(line); File.AppendAllText("artifacts/qa/soak.log", line + Environment.NewLine);
            if (!capture.VideoRecording || capture.Replay.Bytes > 64L * 1024 * 1024) throw new Exception("Recording stopped or replay exceeded memory limit");
        }
        await capture.StopVideoAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var movie = await Task.Run(() => VideoChecks.Decode(path));
        if (movie.LastTime < TimeSpan.FromMinutes(29.9).Ticks || last > baseline + 100L * 1024 * 1024) throw new Exception("Soak duration or memory plateau failed");
        Console.WriteLine($"PASS 30-minute real-time synthetic capture + MP4 + RAM replay; frames={movie.Frames}, private growthMB={(last - baseline) / 1024 / 1024}");
    }
}

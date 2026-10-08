using System.IO.Compression;
using System.Text.Json;

namespace ReplayKit.Services;

public static class HistoryArchive
{
    private sealed record ArchiveEntry(string File, DateTimeOffset CapturedAt, string MonitorId, int Width, int Height);

    public static void Export(string path, IReadOnlyList<CaptureFrame> frames, IProgress<int>? progress = null)
    {
        if (frames.Count == 0) throw new InvalidOperationException("История пока пуста.");
        var temporary = path + ".replaykit-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var manifest = new List<ArchiveEntry>(frames.Count);
                for (var i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    var name = $"frames/{i + 1:000}-{frame.CapturedAt:HH-mm-ss}.png";
                    var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                    using (var output = entry.Open()) output.Write(Images.EncodePng(Images.Decode(frame)));
                    manifest.Add(new(name, frame.CapturedAt, frame.MonitorId, frame.Width, frame.Height));
                    progress?.Report((i + 1) * 100 / frames.Count);
                }
                var metadata = archive.CreateEntry("history.json", CompressionLevel.Fastest);
                using var stream = metadata.Open();
                JsonSerializer.Serialize(stream, new { Format = 1, ExportedAt = DateTimeOffset.Now, Frames = manifest }, new JsonSerializerOptions { WriteIndented = true });
            }
            File.Move(temporary, path, true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReplayKit;
using ReplayKit.Core;
using ReplayKit.Services;
using ReplayKit.Views;

internal static class Program
{
    private static int _exitCode;
    private static int _passed;
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ReplayKit;component/Themes/Controls.xaml", UriKind.Relative) });
        ThemeService.Apply("Dark");
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await RunAsync(args); Console.WriteLine($"{_passed} Windows integration checks passed."); }
            catch (Exception e) { Console.Error.WriteLine(e); _exitCode = 1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run(); return _exitCode;
    }
    private static void Check(string name, bool condition)
    {
        if (!condition) throw new Exception(name); _passed++; Console.WriteLine($"PASS {name}");
    }
    private static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    private static BitmapSource Sample()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(30, 38, 65), Color.FromRgb(53, 84, 113), 35), null, new Rect(0, 0, 1024, 640));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(236, 239, 247)), null, new Rect(110, 90, 804, 460), 18, 18);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(92, 110, 211)), null, new Rect(140, 130, 744, 100), 12, 12);
            dc.DrawText(new FormattedText("ReplayKit", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 34, Brushes.White, 1), new Point(170, 155));
            dc.DrawText(new FormattedText("A moment worth keeping.", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 24, Brushes.DimGray, 1), new Point(140, 260));
            for (var i = 0; i < 3; i++) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(209, 219, 237)), null, new Rect(140 + i * 255, 325, 234, 165), 12, 12);
        }
        var result = new RenderTargetBitmap(1024, 640, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    private static void RenderUi(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/qa"); using var file = File.Create($"artifacts/qa/{name}.png"); encoder.Save(file);
    }
    private static async Task RunAsync(string[] args)
    {
        var image = Sample(); var encoded = Images.EncodeJpeg(image);
        var now = DateTimeOffset.Now;
        var frame = new CaptureFrame(now, "synthetic", image.PixelWidth, image.PixelHeight, encoded);
        var decoded = Images.Decode(frame);
        Check("JPEG stays in memory and retains native dimensions", decoded.PixelWidth == 1024 && decoded.PixelHeight == 640);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(decoded));
        using var stream = new MemoryStream(); png.Save(stream); stream.Position = 0;
        var pngDecoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var originalPixels = new byte[1024 * 640 * 4]; var savedPixels = new byte[originalPixels.Length];
        var original32 = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        var saved32 = new FormatConvertedBitmap(pngDecoded, PixelFormats.Bgra32, null, 0);
        original32.CopyPixels(originalPixels, 1024 * 4, 0); saved32.CopyPixels(savedPixels, 1024 * 4, 0);
        Check("PNG export round trip preserves decoded pixels", originalPixels.SequenceEqual(savedPixels));
        using (var first = new HotkeyService())
        using (var second = new HotkeyService())
        {
            var a = new HotkeySpec(7, 0x86); var b = new HotkeySpec(7, 0x87);
            Check("Global hotkeys register", first.Apply(a, b));
            Check("Global conflict is detected", !second.Apply(a, b));
            Check("Same settings can be reapplied", first.Apply(a, b));
            Check("Duplicate action hotkeys are rejected", !first.Apply(a, a));
            Check("Two registered action hotkeys can be exchanged", first.Apply(b, a));
        }
        using var capture = new CaptureService("synthetic"); capture.SetLocked(true);
        var lockedVideoRejected = false;
        try { _ = capture.StartVideoAsync(Path.GetFullPath("artifacts/qa/locked-start.mp4")); } catch (InvalidOperationException) { lockedVideoRejected = true; }
        Check("Locked session rejects video before file creation", lockedVideoRejected && !capture.VideoPending && !File.Exists("artifacts/qa/locked-start.mp4"));
        now = DateTimeOffset.Now;
        // Leave expiry margin for the first WPF window's JIT/layout on slow CI machines.
        // Exact one-second/60-second retention is covered independently by core tests.
        for (var i = -59; i <= 0; i++) capture.Buffer.Add(frame with { CapturedAt = now.AddSeconds(i * 0.5) });
        var history = new HistoryWindow(capture, new Settings(), _ => { }, () => { }); history.Show();
        Check("Custom window chrome is active", history.WindowStyle == WindowStyle.None && history.AllowsTransparency);
        Check("Dark theme foreground inherits into live window", ((SolidColorBrush)history.Foreground).Color == ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color);
        for (var i = 0; i < 300 && Field<BitmapSource[]>(history, "_previews").Length == 0; i++) await Task.Delay(50);
        Check("History preloads all 60 frozen preview frames", Field<BitmapSource[]>(history, "_previews").Length == 60);
        capture.Buffer.Add(frame with { CapturedAt = now.AddSeconds(1) });
        Check("Live capture cannot shift open timeline", Field<CaptureFrame[]>(history, "_frames")[^1].CapturedAt == now);
        Call(history, "SelectOffset", -59); Check("Oldest second selects first frame", Field<int>(history, "_selected") == 0);
        Call(history, "SelectOffset", 0); Check("Latest second selects last frame", Field<int>(history, "_selected") == 59);
        var samples = new List<double>();
        for (var i = -59; i <= 0; i++)
        {
            var watch = Stopwatch.StartNew(); Call(history, "SelectOffset", i); history.UpdateLayout(); samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        Check($"Frame selection below 100ms (maximum {samples.Max():F2}ms)", samples.Max() < 100);
        history.WindowState = WindowState.Maximized; await Task.Delay(100);
        var dpi = VisualTreeHelper.GetDpi(history);
        var work = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Check("Borderless maximization respects taskbar working area", history.ActualWidth * dpi.DpiScaleX <= work.Width + 2 && history.ActualHeight * dpi.DpiScaleY <= work.Height + 2);
        history.WindowState = WindowState.Normal;
        await Task.Delay(150); RenderUi(history, "history-dark");
        ThemeService.Apply("Light"); await Task.Delay(100);
        Check("Live theme changes update window foreground", ((SolidColorBrush)history.Foreground).Color == ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color);
        RenderUi(history, "history-light"); ThemeService.Apply("Dark");
        history.Close();
        var editor = new EditorWindow(decoded); editor.Show(); await Task.Delay(100);
        Check("Editor uses custom transparent chrome", editor.WindowStyle == WindowStyle.None && editor.AllowsTransparency);
        var withMark = (BitmapSource)Call(editor, "Render", (Action<DrawingContext>)(dc => dc.DrawRectangle(Brushes.Black, null, new Rect(10, 10, 40, 40))))!;
        Call(editor, "Push", withMark);
        var samplePixel = new byte[4]; withMark.CopyPixels(new Int32Rect(20, 20, 1, 1), samplePixel, 4, 0);
        Check("Redaction is flattened into opaque pixels", samplePixel[0] == 0 && samplePixel[1] == 0 && samplePixel[2] == 0 && samplePixel[3] == 255);
        Call(editor, "Undo"); Check("Undo restores original image", ReferenceEquals(Field<BitmapSource>(editor, "_image"), decoded));
        Call(editor, "Redo"); Check("Redo restores edit", ReferenceEquals(Field<BitmapSource>(editor, "_image"), withMark));
        var crop = Images.Crop(withMark, new Int32Rect(100, 100, 400, 300)); Call(editor, "Push", crop);
        Check("Crop uses original pixel coordinates", Field<BitmapSource>(editor, "_image").PixelWidth == 400);
        Call(editor, "Undo"); RenderUi(editor, "editor-dark"); editor.Close();
        var settings = new SettingsWindow(new Settings(), _ => null); settings.Show();
        Check("Settings use custom transparent chrome", settings.WindowStyle == WindowStyle.None && settings.AllowsTransparency);
        await Task.Delay(100); RenderUi(settings, "settings-dark"); settings.Close();
        Directory.CreateDirectory("artifacts/qa");
        var videoPath = Path.GetFullPath("artifacts/qa/video-smoke.mp4");
        await Task.Run(() =>
        {
            using var writer = new Mp4Writer(videoPath, image.PixelWidth, image.PixelHeight);
            var second = VideoChecks.Solid(1024, 640);
            for (var i = 0; i < 60; i++) writer.Write(i < 30 ? image : second, i * TimeSpan.TicksPerSecond / Mp4Writer.FrameRate);
            writer.Complete();
            Check("Native H.264 MP4 contains all submitted frames", writer.FrameCount == 60);
        });
        var videoBytes = File.ReadAllBytes(videoPath);
        var videoAtoms = System.Text.Encoding.Latin1.GetString(videoBytes);
        Check("MP4 is finalized with movie metadata and H.264 payload", videoBytes.Length > 1000 && videoAtoms.Contains("ftyp") && videoAtoms.Contains("moov") && videoAtoms.Contains("avc1"));
        var decodedVideo = await Task.Run(() => VideoChecks.Decode(videoPath));
        Check("MP4 decodes 60 frames with correct dimensions and timestamps", decodedVideo.Frames == 60 && decodedVideo.Width == 1024 && decodedVideo.Height == 640 && Math.Abs(decodedVideo.LastTime / (double)TimeSpan.TicksPerSecond - 59d / 30) < 0.02);
        Check("Video retains image colors and orientation", VideoChecks.Near(decodedVideo.FirstPixel, 92, 110, 211) && VideoChecks.Near(decodedVideo.LastPixel, 218, 52, 96));
        await Task.Run(() =>
        {
            var existing = Path.GetFullPath("artifacts/qa/existing.mp4"); File.WriteAllText(existing, "existing video");
            using (var empty = new Mp4Writer(existing, 1024, 640)) { }
            Check("Cancelled empty video preserves existing destination", File.ReadAllText(existing) == "existing video" && !Directory.EnumerateFiles("artifacts/qa", "existing.mp4.replaykit-*").Any());
            using var changed = new Mp4Writer(existing, 1024, 640);
            var rejected = false;
            try { changed.Write(VideoChecks.Solid(1026, 640), 0); } catch (InvalidOperationException) { rejected = true; }
            Check("Resolution change is rejected before encoding", rejected && changed.FrameCount == 0 && File.ReadAllText(existing) == "existing video");
        });
        using (var recording = new CaptureService("test monitor", (_, _) => image))
        {
            recording.SetLocked(false);
            // Cold JIT/encoder startup can exceed 300 ms on a shared CI runner.
            // Wait for actual samples, then measure their cadence instead of startup latency.
            for (var t = 0; t < 120 && recording.Buffer.Snapshot(DateTimeOffset.Now).Length < 3; t++) await Task.Delay(50);
            var initialHistory = recording.Buffer.Snapshot(DateTimeOffset.Now);
            Console.WriteLine($"Worker history: {initialHistory.Length} frames; intervals: {string.Join(", ", initialHistory.Zip(initialHistory.Skip(1), (a, b) => (b.CapturedAt - a.CapturedAt).TotalMilliseconds.ToString("F0")))} ms");
            Check("History does not skip every other tick after JPEG encoding", initialHistory.Length >= 3 && (initialHistory[1].CapturedAt - initialHistory[0].CapturedAt).TotalSeconds < 1.5);
            var workerVideo = Path.GetFullPath("artifacts/qa/worker-video.mp4");
            await recording.StartVideoAsync(workerVideo).WaitAsync(TimeSpan.FromSeconds(10));
            var duplicateRejected = false;
            try { _ = recording.StartVideoAsync(workerVideo); } catch (InvalidOperationException) { duplicateRejected = true; }
            Check("Worker allows one active video", duplicateRejected && recording.VideoRecording);
            var count = recording.Buffer.Snapshot(DateTimeOffset.Now).Length;
            await Task.Delay(1200);
            Check("Replay continues at one-second intervals during MP4", recording.Buffer.Snapshot(DateTimeOffset.Now).Length > count);
            recording.Toggle();
            await Task.Delay(200);
            Check("Pausing screenshot buffer keeps video recording", !recording.Recording && recording.VideoRecording);
            var saved = await recording.StopVideoAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check("Worker stop finalizes selected MP4 and resets state", saved == workerVideo && !recording.VideoPending && !recording.VideoRecording);
            var workerMovie = await Task.Run(() => VideoChecks.Decode(workerVideo));
            Check("Worker MP4 decodes captured images and elapsed time", workerMovie.Frames > 2 && workerMovie.LastTime > TimeSpan.TicksPerSecond && VideoChecks.Near(workerMovie.FirstPixel, 92, 110, 211));
            await recording.StartVideoAsync(Path.GetFullPath("artifacts/qa/worker-lock.mp4")).WaitAsync(TimeSpan.FromSeconds(10));
            recording.SetLocked(true);
            for (var t = 0; t < 100 && recording.VideoPending; t++) await Task.Delay(50);
            Check("Lock finalizes active video and clears history", !recording.VideoPending && recording.Buffer.Snapshot(DateTimeOffset.Now).Length == 0);
            recording.SetLocked(false);
            await recording.StartVideoAsync(Path.GetFullPath("artifacts/qa/worker-monitor.mp4")).WaitAsync(TimeSpan.FromSeconds(10));
            recording.SelectMonitor("new test monitor");
            for (var t = 0; t < 100 && recording.VideoPending; t++) await Task.Delay(50);
            Check("Monitor change finalizes active video", !recording.VideoPending && recording.MonitorId == "new test monitor");
            await recording.StartVideoAsync(Path.GetFullPath("artifacts/qa/worker-exit.mp4")).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Check("Graceful exit leaves playable MP4", (await Task.Run(() => VideoChecks.Decode(Path.GetFullPath("artifacts/qa/worker-exit.mp4")))).Frames > 0);
        if (args.Contains("--capture"))
        {
            // A valid JPEG header and dimensions can still describe a completely black image.
            // Keep a known, non-excluded window on screen and verify its pixels through DXGI.
            var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            using var witness = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                StartPosition = System.Windows.Forms.FormStartPosition.Manual, Bounds = screen.Bounds, TopMost = true,
                ShowInTaskbar = false, BackColor = System.Drawing.Color.FromArgb(218, 52, 96), Text = "ReplayKit capture test" };
            witness.Show(); witness.Activate(); witness.Refresh();
            await Task.Delay(600);
            var checkPoint = witness.PointToScreen(new System.Drawing.Point(24, 24)); var dc = GetDC(IntPtr.Zero);
            var displayed = GetPixel(dc, checkPoint.X, checkPoint.Y); ReleaseDC(IntPtr.Zero, dc);
            Check("Known test window is visible (requires unlocked Windows)", (displayed & 255) == 218 && ((displayed >> 8) & 255) == 52 && ((displayed >> 16) & 255) == 96);
            using var dxgi = new DxgiCapture();
            CaptureFrame? actual = null;
            for (var i = 0; i < 20 && actual == null; i++) { actual = dxgi.Capture(screen.DeviceName, DateTimeOffset.Now); await Task.Delay(100); }
            Check("Real DXGI capture at native monitor dimensions", actual != null && actual.Width == screen.Bounds.Width && actual.Height == screen.Bounds.Height && actual.EncodedImage.Length > 1000);
            Console.WriteLine($"DXGI: {actual!.Width}×{actual.Height}, {actual.EncodedImage.Length / 1024} KiB JPEG in RAM.");
            var firstImage = new FormatConvertedBitmap(Images.Decode(actual), PixelFormats.Bgra32, null, 0);
            var firstPixels = new byte[firstImage.PixelWidth * firstImage.PixelHeight * 4]; firstImage.CopyPixels(firstPixels, firstImage.PixelWidth * 4, 0);
            Check("First captured desktop is not an uninitialized black surface", Enumerable.Range(0, firstPixels.Length / 4).Any(p => firstPixels[p * 4] > 10 || firstPixels[p * 4 + 1] > 10 || firstPixels[p * 4 + 2] > 10));
            for (var i = 0; i < 8; i++)
            {
                var captured = dxgi.Capture(screen.DeviceName, DateTimeOffset.Now) ?? actual;
                var point = witness.PointToScreen(new System.Drawing.Point(24, 24));
                var pixel = new byte[4];
                new FormatConvertedBitmap(Images.Decode(captured), PixelFormats.Bgra32, null, 0)
                    .CopyPixels(new Int32Rect((int)point.X - screen.Bounds.Left, (int)point.Y - screen.Bounds.Top, 1, 1), pixel, 4, 0);
                Console.WriteLine($"Capture {i}: witness RGB={pixel[2]},{pixel[1]},{pixel[0]}");
                Check("Real desktop pixels match visible witness", Math.Abs(pixel[2] - 218) < 15 && Math.Abs(pixel[1] - 52) < 15 && Math.Abs(pixel[0] - 96) < 15);
                await Task.Delay(150);
            }
            dxgi.Dispose();
            using (var background = new CaptureService(screen.DeviceName))
            {
                await Task.Delay(2200);
                var backgroundFrames = background.Buffer.Snapshot(DateTimeOffset.Now);
                Check("Background capture service produces real frames", backgroundFrames.Length >= 2);
                foreach (var captured in backgroundFrames)
                {
                    var point = witness.PointToScreen(new System.Drawing.Point(24, 24)); var pixel = new byte[4];
                    new FormatConvertedBitmap(Images.Decode(captured), PixelFormats.Bgra32, null, 0)
                        .CopyPixels(new Int32Rect((int)point.X - screen.Bounds.Left, (int)point.Y - screen.Bounds.Top, 1, 1), pixel, 4, 0);
                    Console.WriteLine($"Worker witness RGB={pixel[2]},{pixel[1]},{pixel[0]}");
                    Check("Worker capture preserves visible desktop pixels", Math.Abs(pixel[2] - 218) < 15 && Math.Abs(pixel[1] - 52) < 15 && Math.Abs(pixel[0] - 96) < 15);
                }
                var screenVideo = Path.GetFullPath("artifacts/qa/screen-video.mp4");
                await background.StartVideoAsync(screenVideo).WaitAsync(TimeSpan.FromSeconds(15));
                Check("Screen video starts while replay buffer continues", background.VideoRecording);
                await Task.Delay(2200);
                Check("Replay buffer keeps one-second history during video", background.Buffer.Snapshot(DateTimeOffset.Now).Length >= backgroundFrames.Length + 1);
                var saved = await background.StopVideoAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Check("Video stop finalizes chosen MP4 and resets state", saved == screenVideo && File.Exists(screenVideo) && !background.VideoPending && !background.VideoRecording);
                var recorded = await Task.Run(() => VideoChecks.Decode(screenVideo));
                Check("Recorded screen MP4 decodes actual captured colors", recorded.Frames > 1 && recorded.Width == screen.Bounds.Width && recorded.Height == screen.Bounds.Height && VideoChecks.Near(recorded.FirstPixel, 218, 52, 96) && VideoChecks.Near(recorded.LastPixel, 218, 52, 96));
                await background.StartVideoAsync(Path.GetFullPath("artifacts/qa/lock-video.mp4")).WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(200); background.SetLocked(true);
                for (var t = 0; t < 50 && background.VideoPending; t++) await Task.Delay(100);
                Check("Session lock finalizes video and clears replay", !background.VideoPending && background.Buffer.Snapshot(DateTimeOffset.Now).Length == 0);
            }
            witness.Close();
        }
    }
}

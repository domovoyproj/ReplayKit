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
        now = DateTimeOffset.Now;
        // Leave expiry margin for the first WPF window's JIT/layout on slow CI machines.
        // Exact one-second/60-second retention is covered independently by core tests.
        for (var i = -59; i <= 0; i++) capture.Buffer.Add(frame with { CapturedAt = now.AddSeconds(i * 0.5) });
        var history = new HistoryWindow(capture, new Settings(), _ => { }, () => { }); history.Show();
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
        await Task.Delay(150); RenderUi(history, "history-dark");
        ThemeService.Apply("Light"); await Task.Delay(100); RenderUi(history, "history-light"); ThemeService.Apply("Dark");
        history.Close();
        var editor = new EditorWindow(decoded); editor.Show(); await Task.Delay(100);
        var withMark = (BitmapSource)Call(editor, "Render", (Action<DrawingContext>)(dc => dc.DrawRectangle(Brushes.Black, null, new Rect(10, 10, 40, 40))))!;
        Call(editor, "Push", withMark);
        var samplePixel = new byte[4]; withMark.CopyPixels(new Int32Rect(20, 20, 1, 1), samplePixel, 4, 0);
        Check("Redaction is flattened into opaque pixels", samplePixel[0] == 0 && samplePixel[1] == 0 && samplePixel[2] == 0 && samplePixel[3] == 255);
        Call(editor, "Undo"); Check("Undo restores original image", ReferenceEquals(Field<BitmapSource>(editor, "_image"), decoded));
        Call(editor, "Redo"); Check("Redo restores edit", ReferenceEquals(Field<BitmapSource>(editor, "_image"), withMark));
        var crop = Images.Crop(withMark, new Int32Rect(100, 100, 400, 300)); Call(editor, "Push", crop);
        Check("Crop uses original pixel coordinates", Field<BitmapSource>(editor, "_image").PixelWidth == 400);
        Call(editor, "Undo"); RenderUi(editor, "editor-dark"); editor.Close();
        if (args.Contains("--capture"))
        {
            using var dxgi = new DxgiCapture();
            var screen = System.Windows.Forms.Screen.PrimaryScreen!;
            CaptureFrame? actual = null;
            for (var i = 0; i < 20 && actual == null; i++) { actual = dxgi.Capture(screen.DeviceName, DateTimeOffset.Now); await Task.Delay(100); }
            Check("Real DXGI capture at native monitor dimensions", actual != null && actual.Width == screen.Bounds.Width && actual.Height == screen.Bounds.Height && actual.EncodedImage.Length > 1000);
            Console.WriteLine($"DXGI: {actual!.Width}×{actual.Height}, {actual.EncodedImage.Length / 1024} KiB JPEG in RAM.");
        }
    }
}

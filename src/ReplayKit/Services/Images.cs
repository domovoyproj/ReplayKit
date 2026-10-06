namespace ReplayKit.Services;

public static class Images
{
    public static BitmapSource Decode(CaptureFrame frame, int maxWidth = 0)
    {
        using var stream = new MemoryStream(frame.EncodedImage, false);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
        if (maxWidth > 0 && frame.Width > maxWidth) image.DecodePixelWidth = maxWidth;
        image.EndInit(); image.Freeze(); return image;
    }
    public static byte[] EncodeJpeg(BitmapSource image)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    public static void SavePng(BitmapSource image, Window owner)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG изображение|*.png", DefaultExt = ".png", FileName = $"ReplayKit-{DateTime.Now:yyyy-MM-dd-HHmmss}.png" };
        if (dialog.ShowDialog(owner) != true) return;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(dialog.FileName); encoder.Save(file);
    }
    public static async Task CopyAsync(BitmapSource image)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Clipboard.SetImage(image); return; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 4) { await Task.Delay(70); }
        }
    }
}

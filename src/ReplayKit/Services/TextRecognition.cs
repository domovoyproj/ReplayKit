using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ReplayKit.Services;

public static class TextRecognition
{
    public static async Task<string> ReadAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("Установите язык распознавания текста в параметрах языка Windows.");
        var max = (int)OcrEngine.MaxImageDimension;
        if (image.PixelWidth > max || image.PixelHeight > max)
        {
            var scale = Math.Min((double)max / image.PixelWidth, (double)max / image.PixelHeight);
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }
        image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, image.PixelWidth, image.PixelHeight, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }
}

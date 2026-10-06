using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

internal static class VideoChecks
{
    internal sealed record Decoded(int Frames, int Width, int Height, long LastTime, byte[] FirstPixel, byte[] LastPixel);
    internal static BitmapSource Solid(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 96; pixels[i + 1] = 52; pixels[i + 2] = 218; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        bitmap.Freeze(); return bitmap;
    }
    internal static bool Near(byte[] pixel, int red, int green, int blue) => Math.Abs(pixel[2] - red) < 20 && Math.Abs(pixel[1] - green) < 20 && Math.Abs(pixel[0] - blue) < 20;
    internal static Decoded Decode(string path)
    {
        MFStartup().CheckError();
        try
        {
            using var options = MFCreateAttributes(1);
            options.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u).CheckError();
            using var reader = MFCreateSourceReaderFromURL(path, options);
            using var desired = MFCreateMediaType();
            desired.Set(MediaTypeAttributeKeys.MajorType, new Guid("73646976-0000-0010-8000-00aa00389b71")).CheckError();
            desired.Set(MediaTypeAttributeKeys.Subtype, new Guid("00000016-0000-0010-8000-00aa00389b71")).CheckError();
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, desired);
            using var format = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            var size = format.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            var width = (int)(size >> 32); var height = (int)(size & uint.MaxValue);
            var stride = unchecked((int)format.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
            int count = 0; long lastTime = 0; byte[] first = [], last = [];
            while (true)
            {
                using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out var time);
                if (sample != null)
                {
                    using var buffer = sample.ConvertToContiguousBuffer(); buffer.Lock(out var pointer, out _, out _);
                    try
                    {
                        var pixel = new byte[4];
                        var y = stride < 0 ? height - 1 - 150 : 150;
                        Marshal.Copy(pointer + y * Math.Abs(stride) + 170 * 4, pixel, 0, 4);
                        if (count == 0) first = pixel; last = pixel;
                    }
                    finally { buffer.Unlock(); }
                    count++; lastTime = time;
                }
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            }
            return new Decoded(count, width, height, lastTime, first, last);
        }
        finally { MFShutdown(); }
    }
}

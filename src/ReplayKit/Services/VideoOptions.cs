namespace ReplayKit.Services;

public sealed record VideoOptions(int Fps = 30, int MaxWidth = 0, int Quality = 5,
    bool SystemAudio = false, bool Microphone = false, double SystemVolume = 1, double MicrophoneVolume = 1)
{
    public bool Audio => SystemAudio || Microphone;
    public VideoOptions Normalize() => this with { Fps = Fps is 15 or 30 or 60 ? Fps : 30,
        MaxWidth = MaxWidth is 0 or 1280 or 1920 ? MaxWidth : 1920, Quality = Math.Clamp(Quality, 2, 10),
        SystemVolume = Math.Clamp(SystemVolume, 0, 2), MicrophoneVolume = Math.Clamp(MicrophoneVolume, 0, 2) };
    public BitmapSource Resize(BitmapSource image)
    {
        if (MaxWidth == 0 || image.PixelWidth <= MaxWidth) return image;
        var scale = (double)MaxWidth / image.PixelWidth;
        var bitmap = new TransformedBitmap(image, new ScaleTransform(scale, scale)); bitmap.Freeze(); return bitmap;
    }
}

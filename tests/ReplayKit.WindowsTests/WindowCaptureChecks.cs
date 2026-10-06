using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ReplayKit.Services;
using Windows.Graphics.Capture;

internal static class WindowCaptureChecks
{
    // Win32 ABI from Microsoft's GraphicsCaptureItem interop sample; MIT, see THIRD_PARTY_NOTICES.
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
    }
    public static async Task Run(IntPtr window, Action<string, bool> check)
    {
        var interop = GraphicsCaptureItem.As<ItemInterop>();
        var pointer = interop.CreateForWindow(window, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"));
        GraphicsCaptureItem item;
        try { item = GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        await Task.Run(async () =>
        {
            using var capture = new WindowCapture(item); BitmapSource? frame = null;
            for (var i = 0; i < 50 && frame == null; i++) { frame = capture.Capture(); await Task.Delay(50); }
            check("WGC captures an occluded window", frame != null);
            var pixel = new byte[4]; frame!.CopyPixels(new Int32Rect(24, 24, 1, 1), pixel, 4, 0);
            check("WGC preserves target window pixels behind another window", VideoChecks.Near(pixel, 218, 52, 96));
        });
    }
}

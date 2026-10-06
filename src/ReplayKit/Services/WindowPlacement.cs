using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ReplayKit.Services;

public static class WindowPlacement
{
    public static void CenterAtCursor(Window window)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var area = screen.WorkingArea;
        var handle = new WindowInteropHelper(window).Handle;
        // Move into the target monitor first so WPF receives WM_DPICHANGED before sizing.
        SetWindowPos(handle, IntPtr.Zero, area.Left + 14, area.Top + 14, 0, 0, 0x0015);
        var scale = GetDpiForWindow(handle) / 96d;
        var width = Math.Min(window.Width * scale, area.Width - 28);
        var height = Math.Min(window.Height * scale, area.Height - 28);
        window.MinWidth = Math.Min(window.MinWidth, width / scale);
        window.MinHeight = Math.Min(window.MinHeight, height / scale);
        window.Width = width / scale; window.Height = height / scale;
        SetWindowPos(handle, IntPtr.Zero, area.Left + (area.Width - (int)width) / 2, area.Top + (area.Height - (int)height) / 2, (int)width, (int)height, 0x0014);
    }
    public static void ExcludeFromCapture(Window window)
    {
        // Windows 10 2004+: prevents ReplayKit's own windows entering the replay buffer.
        SetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, 0x11);
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}

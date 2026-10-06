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
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowDisplayAffinity(handle, 0x11);
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessages);
    }
    private static IntPtr WindowMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x24) return IntPtr.Zero; // WM_GETMINMAXINFO
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
        var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        limits.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        limits.MaxSize.X = info.Work.Right - info.Work.Left;
        limits.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        Marshal.StructureToPtr(limits, lParam, false); handled = true;
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

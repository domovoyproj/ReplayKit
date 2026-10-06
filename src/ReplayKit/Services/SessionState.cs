using System.Runtime.InteropServices;

namespace ReplayKit.Services;

internal static class SessionState
{
    public static bool IsLocked
    {
        get
        {
            // WTSSessionInfoEx / WTSINFOEXW, x64 layout: Level at 0, level-1 data at 8.
            // Check the current state as well as listening for later SessionSwitch events.
            if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 25, out var info, out var size)) return false;
            try { return size >= 20 && Marshal.ReadInt32(info) == 1 && Marshal.ReadInt32(info, 16) == 0; }
            finally { WTSFreeMemory(info); }
        }
    }
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr info, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
}

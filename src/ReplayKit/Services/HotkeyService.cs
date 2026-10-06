using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ReplayKit.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source = new(new HwndSourceParameters("ReplayKit hotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
    private HotkeySpec? _history;
    private HotkeySpec? _recording;
    public event Action? OpenHistory;
    public event Action? ToggleRecording;
    public HotkeyService() { _source.AddHook(WndProc); }
    public bool Apply(HotkeySpec history, HotkeySpec recording)
    {
        if (!history.IsValid || !recording.IsValid || history == recording) return false;
        // Probe both bindings with spare IDs; leave the existing set live until both are known to work.
        var historyUnchanged = history == _history;
        var recordingUnchanged = recording == _recording;
        var probeHistory = !historyUnchanged && history != _recording;
        var probeRecording = !recordingUnchanged && recording != _history;
        if (probeHistory && !RegisterHotKey(_source.Handle, 101, history.Modifiers | 0x4000, history.VirtualKey)) return false;
        if (probeRecording && !RegisterHotKey(_source.Handle, 102, recording.Modifiers | 0x4000, recording.VirtualKey))
        { UnregisterHotKey(_source.Handle, 101); return false; }
        if (!historyUnchanged) UnregisterHotKey(_source.Handle, 1);
        if (!recordingUnchanged) UnregisterHotKey(_source.Handle, 2);
        UnregisterHotKey(_source.Handle, 101); UnregisterHotKey(_source.Handle, 102);
        var ok1 = historyUnchanged || RegisterHotKey(_source.Handle, 1, history.Modifiers | 0x4000, history.VirtualKey);
        var ok2 = recordingUnchanged || RegisterHotKey(_source.Handle, 2, recording.Modifiers | 0x4000, recording.VirtualKey);
        if (!ok1 || !ok2)
        {
            if (!historyUnchanged) UnregisterHotKey(_source.Handle, 1);
            if (!recordingUnchanged) UnregisterHotKey(_source.Handle, 2);
            if (!historyUnchanged && _history != null) RegisterHotKey(_source.Handle, 1, _history.Modifiers | 0x4000, _history.VirtualKey);
            if (!recordingUnchanged && _recording != null) RegisterHotKey(_source.Handle, 2, _recording.Modifiers | 0x4000, _recording.VirtualKey);
            return false;
        }
        _history = history; _recording = recording; return true;
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x312) { if (wParam.ToInt32() == 1) OpenHistory?.Invoke(); if (wParam.ToInt32() == 2) ToggleRecording?.Invoke(); handled = true; }
        return IntPtr.Zero;
    }
    public void Dispose() { UnregisterHotKey(_source.Handle, 1); UnregisterHotKey(_source.Handle, 2); _source.Dispose(); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

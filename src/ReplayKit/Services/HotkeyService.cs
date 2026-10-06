using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ReplayKit.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source = new(new HwndSourceParameters("ReplayKit hotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
    private HotkeySpec[] _bindings = [];
    public event Action? OpenHistory;
    public event Action? ToggleRecording;
    public HotkeyService() { _source.AddHook(WndProc); }
    public event Action? ToggleVideo;
    public event Action? OpenReplay;
    public bool Apply(HotkeySpec history, HotkeySpec recording) => Apply(history, recording, new(6, 0x56), new(6, 0x42));
    public bool Apply(params HotkeySpec[] bindings)
    {
        if (bindings.Length != 4 || bindings.Any(b => !b.IsValid) || bindings.Distinct().Count() != 4) return false;
        var probes = new List<int>();
        for (var i = 0; i < bindings.Length; i++)
        {
            if (_bindings.Contains(bindings[i])) continue;
            if (!RegisterHotKey(_source.Handle, 101 + i, bindings[i].Modifiers | 0x4000, bindings[i].VirtualKey))
            { foreach (var id in probes) UnregisterHotKey(_source.Handle, id); return false; }
            probes.Add(101 + i);
        }
        foreach (var id in probes) UnregisterHotKey(_source.Handle, id);
        for (var i = 0; i < _bindings.Length; i++) UnregisterHotKey(_source.Handle, i + 1);
        var ok = true;
        for (var i = 0; i < bindings.Length; i++) ok &= RegisterHotKey(_source.Handle, i + 1, bindings[i].Modifiers | 0x4000, bindings[i].VirtualKey);
        if (!ok)
        {
            for (var i = 0; i < bindings.Length; i++) UnregisterHotKey(_source.Handle, i + 1);
            for (var i = 0; i < _bindings.Length; i++) RegisterHotKey(_source.Handle, i + 1, _bindings[i].Modifiers | 0x4000, _bindings[i].VirtualKey);
            return false;
        }
        _bindings = bindings.ToArray(); return true;
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x312) { if (wParam.ToInt32() == 1) OpenHistory?.Invoke(); if (wParam.ToInt32() == 2) ToggleRecording?.Invoke(); if (wParam.ToInt32() == 3) ToggleVideo?.Invoke(); if (wParam.ToInt32() == 4) OpenReplay?.Invoke(); handled = true; }
        return IntPtr.Zero;
    }
    public void Dispose() { for (var i = 1; i <= 4; i++) UnregisterHotKey(_source.Handle, i); _source.Dispose(); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

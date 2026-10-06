using Microsoft.Win32;
using ReplayKit.Services;
using ReplayKit.Views;
using Color = System.Drawing.Color;
using System.Windows.Threading;

namespace ReplayKit;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _activationWait;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _recordingIcon, _pausedIcon;
    private HotkeyService? _hotkeys;
    private CaptureService? _capture;
    private HistoryWindow? _history;
    private SettingsWindow? _settingsWindow;
    public Settings Settings { get; private set; } = new();
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\ReplayKit.SingleInstance", out var first);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ReplayKit.Activate");
        if (!first) { _activation.Set(); Shutdown(); return; }
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => Dispatcher.BeginInvoke(OpenHistory), null, Timeout.Infinite, false);
        DispatcherUnhandledException += OnUnhandled;
        Settings = SettingsStore.Load(); ThemeService.Apply(Settings.Theme);
        _capture = new CaptureService(ResolveMonitor());
        _capture.StateChanged += OnCaptureState;
        _hotkeys = new HotkeyService();
        _hotkeys.OpenHistory += OpenHistory; _hotkeys.ToggleRecording += _capture.Toggle;
        var bindingsOk = _hotkeys.Apply(Settings.HistoryHotkey, Settings.RecordingHotkey);
        CreateTray();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        SystemEvents.UserPreferenceChanged += OnPreferencesChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        try { SettingsStore.SetAutoStart(Settings.AutoStart); } catch (Exception ex) { ShowNotification("Автозапуск", ex.Message); }
        if (!bindingsOk) { ShowNotification("Сочетание клавиш занято", "Откройте настройки в трее и выберите свободные сочетания."); OpenSettings(); }
        else if (!e.Args.Contains("--background")) OpenHistory();
    }
    private string ResolveMonitor() => System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Settings.MonitorId)?.DeviceName
        ?? System.Windows.Forms.Screen.PrimaryScreen!.DeviceName;
    private void CreateTray()
    {
        _recordingIcon = CreateIcon(Color.FromArgb(113, 139, 255)); _pausedIcon = CreateIcon(Color.FromArgb(144, 148, 158));
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть историю", null, (_, _) => OpenHistory());
        menu.Items.Add("Запись / пауза", null, (_, _) => _capture!.Toggle());
        menu.Items.Add("Настройки", null, (_, _) => OpenSettings());
        menu.Items.Add("Очистить буфер", null, (_, _) => _capture!.Buffer.Clear());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => Shutdown());
        _tray = new System.Windows.Forms.NotifyIcon { Visible = true, Icon = _recordingIcon, Text = "ReplayKit · запись", ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => OpenHistory();
    }
    private static System.Drawing.Icon CreateIcon(Color color)
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new System.Drawing.SolidBrush(color);
        graphics.FillEllipse(brush, 2, 2, 28, 28);
        using var pen = new System.Drawing.Pen(Color.White, 2.8f);
        graphics.DrawArc(pen, 9, 9, 14, 14, 35, 285); graphics.DrawLine(pen, 9, 9, 9, 15);
        var handle = bitmap.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    public void OpenHistory()
    {
        if (_exiting || _capture == null) return;
        if (_history == null)
        {
            _history = new HistoryWindow(_capture, Settings, SelectMonitor, OpenSettings);
            _history.Closed += (_, _) => _history = null;
        }
        if (_history.IsVisible) { _history.Activate(); return; }
        _history.Show(); _history.Activate();
    }
    private void SelectMonitor(string monitor)
    {
        Settings.MonitorId = monitor; _capture!.SelectMonitor(monitor); SettingsStore.Save(Settings);
    }
    public void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(Settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    private string? ApplySettings(Settings proposed)
    {
        if (!_hotkeys!.Apply(proposed.HistoryHotkey, proposed.RecordingHotkey)) return "Сочетание занято или недопустимо. Старые сочетания продолжают работать.";
        try { SettingsStore.SetAutoStart(proposed.AutoStart); SettingsStore.Save(proposed); }
        catch (Exception ex) { _hotkeys.Apply(Settings.HistoryHotkey, Settings.RecordingHotkey); return ex.Message; }
        Settings.HistoryHotkey = proposed.HistoryHotkey; Settings.RecordingHotkey = proposed.RecordingHotkey;
        Settings.AutoStart = proposed.AutoStart; Settings.Theme = proposed.Theme;
        ThemeService.Apply(Settings.Theme); return null;
    }
    private void OnCaptureState() => Dispatcher.BeginInvoke(() =>
    {
        if (_tray == null || _capture == null || _exiting) return;
        _tray.Icon = _capture.Recording && _capture.Error == null ? _recordingIcon : _pausedIcon;
        _tray.Text = _capture.Error != null ? "ReplayKit · захват недоступен" : _capture.Recording ? "ReplayKit · запись" : "ReplayKit · пауза";
    });
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) _capture?.SetLocked(true); if (e.Reason == SessionSwitchReason.SessionUnlock) _capture?.SetLocked(false); }
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) _capture?.SetLocked(true); if (e.Mode == PowerModes.Resume) _capture?.SetLocked(false); }
    private void OnDisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { _capture?.SelectMonitor(ResolveMonitor()); _history?.Close(); });
    private void OnPreferencesChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => ThemeService.Apply(Settings.Theme));
    private void ShowNotification(string title, string text) => _tray?.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.Info);
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; MessageBox.Show(e.Exception.Message, "ReplayKit", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        SystemEvents.UserPreferenceChanged -= OnPreferencesChanged; SystemEvents.PowerModeChanged -= OnPowerChanged;
        _activationWait?.Unregister(null); _capture?.Dispose(); _hotkeys?.Dispose();
        _tray?.Dispose(); _recordingIcon?.Dispose(); _pausedIcon?.Dispose(); _activation?.Dispose(); _mutex?.Dispose();
        base.OnExit(e);
    }
}

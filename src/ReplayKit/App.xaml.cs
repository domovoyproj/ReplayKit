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
    private EventWaitHandle? _quit;
    private RegisteredWaitHandle? _activationWait;
    private RegisteredWaitHandle? _quitWait;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _recordingIcon, _pausedIcon, _videoIcon;
    private HotkeyService? _hotkeys;
    private CaptureService? _capture;
    private HistoryWindow? _history;
    private SettingsWindow? _settingsWindow;
    public Settings Settings { get; private set; } = new();
    private bool _exiting;
    private bool _videoBusy;
    private RecordingOverlay? _overlay;
    private ReplayWindow? _replayWindow;
    private string? _lastVideoError, _lastReplayError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, @"Local\ReplayKit.SingleInstance", out var first);
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ReplayKit.Activate");
        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ReplayKit.Quit");
        if (e.Args.Contains("--quit"))
        {
            if (!first)
            {
                _quit.Set();
                try { if (_mutex.WaitOne(30000)) _mutex.ReleaseMutex(); }
                catch (AbandonedMutexException) { _mutex.ReleaseMutex(); }
            }
            Shutdown(); return;
        }
        if (!first) { _activation.Set(); Shutdown(); return; }
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => Dispatcher.BeginInvoke(OpenHistory), null, Timeout.Infinite, false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(_quit, (_, _) => Dispatcher.BeginInvoke(() => Shutdown()), null, Timeout.Infinite, false);
        DispatcherUnhandledException += OnUnhandled;
        Settings = SettingsStore.Load(); ThemeService.Apply(Settings.Theme);
        _capture = new CaptureService(ResolveMonitor()) { VideoOptions = Settings.Video };
        _capture.Replay.Configure(Settings);
        _capture.StateChanged += OnCaptureState;
        _capture.VideoSaved += OnVideoSaved;
        _hotkeys = new HotkeyService();
        _hotkeys.OpenHistory += OpenHistory; _hotkeys.ToggleRecording += _capture.Toggle;
        _hotkeys.ToggleVideo += ToggleVideo; _hotkeys.OpenReplay += OpenReplay;
        var bindingsOk = _hotkeys.Apply(Settings.HistoryHotkey, Settings.RecordingHotkey, Settings.VideoHotkey, Settings.ReplayHotkey);
        CreateTray();
        OnCaptureState();
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
        _recordingIcon = CreateIcon(Color.FromArgb(113, 139, 255)); _pausedIcon = CreateIcon(Color.FromArgb(144, 148, 158)); _videoIcon = CreateIcon(Color.FromArgb(255, 92, 103));
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть историю", null, (_, _) => OpenHistory());
        menu.Items.Add("Буфер скриншотов / пауза", null, (_, _) => _capture!.Toggle());
        menu.Items.Add("Начать / остановить видео", null, (_, _) => ToggleVideo());
        menu.Items.Add("Пауза / продолжить видео", null, (_, _) => _capture!.ToggleVideoPause());
        menu.Items.Add("Видеобуфер", null, (_, _) => OpenReplay());
        menu.Items.Add("Диагностика", null, (_, _) => ShowDiagnostics());
        menu.Items.Add("Настройки", null, (_, _) => OpenSettings());
        menu.Items.Add("Очистить буфер", null, (_, _) => { _history?.Close(); _capture!.Buffer.Clear(); _capture.Replay.Clear(); });
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
            _history = new HistoryWindow(_capture, Settings, SelectMonitor, OpenSettings, ToggleVideo);
            _history.Closed += (_, _) => _history = null;
        }
        if (_history.IsVisible) { if (_history.WindowState == WindowState.Minimized) _history.WindowState = WindowState.Normal; _history.Activate(); return; }
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
        if (_capture!.VideoPending) return "Остановите запись перед изменением настроек.";
        if (!_hotkeys!.Apply(proposed.HistoryHotkey, proposed.RecordingHotkey, proposed.VideoHotkey, proposed.ReplayHotkey)) return "Сочетание занято или недопустимо. Старые сочетания продолжают работать.";
        try { SettingsStore.SetAutoStart(proposed.AutoStart); SettingsStore.Save(proposed); }
        catch (Exception ex) { _hotkeys.Apply(Settings.HistoryHotkey, Settings.RecordingHotkey, Settings.VideoHotkey, Settings.ReplayHotkey); return ex.Message; }
        Settings.HistoryHotkey = proposed.HistoryHotkey; Settings.RecordingHotkey = proposed.RecordingHotkey;
        Settings.AutoStart = proposed.AutoStart; Settings.Theme = proposed.Theme;
        Settings.VideoHotkey = proposed.VideoHotkey; Settings.ReplayHotkey = proposed.ReplayHotkey;
        Settings.Video = proposed.Video; Settings.ReplayEnabled = proposed.ReplayEnabled; Settings.ReplaySeconds = proposed.ReplaySeconds;
        Settings.ReplayMemoryMb = proposed.ReplayMemoryMb; Settings.ReplayEconomy = proposed.ReplayEconomy;
        _capture!.VideoOptions = Settings.Video; _capture.Replay.Configure(Settings);
        ThemeService.Apply(Settings.Theme); return null;
    }
    private void OnCaptureState() => Dispatcher.BeginInvoke(() =>
    {
        if (_tray == null || _capture == null || _exiting) return;
        var replayActive = _capture.Replay.Enabled && !_capture.Locked;
        var icon = _capture.VideoPaused ? _pausedIcon : _capture.VideoRecording ? _videoIcon : (_capture.Recording || replayActive) && _capture.Error == null ? _recordingIcon : _pausedIcon;
        if (_tray.Icon != icon) _tray.Icon = icon;
        var text = _capture.VideoPaused ? "ReplayKit · видео на паузе" : _capture.VideoRecording ? "ReplayKit · запись видео" : _capture.Error != null ? "ReplayKit · захват недоступен" : _capture.Recording ? "ReplayKit · буфер" : replayActive ? "ReplayKit · видеобуфер" : "ReplayKit · пауза";
        if (_tray.Text != text) _tray.Text = text;
        if (_capture.VideoError != null && _lastVideoError != _capture.VideoError) ShowNotification("Запись остановлена", _capture.VideoError);
        if (_capture.Replay.Error != null && _lastReplayError != _capture.Replay.Error) ShowNotification("Видеобуфер остановлен", _capture.Replay.Error);
        _lastVideoError = _capture.VideoError; _lastReplayError = _capture.Replay.Error;
    });
    private async void ToggleVideo()
    {
        if (_capture == null || _videoBusy) return;
        _videoBusy = true;
        try
        {
            if (_capture.VideoPending)
            {
                var saved = await _capture.StopVideoAsync();
                if (_capture.VideoError != null)
                {
                    MessageBox.Show(_capture.VideoError, "Видеозапись", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (saved != null && saved.Contains(".replaykit-", StringComparison.Ordinal))
                    {
                        var recovery = new SaveFileDialog { Title = "Сохранить готовое видео в другое место", Filter = "Видео MP4|*.mp4", DefaultExt = ".mp4", FileName = $"ReplayKit-recovered-{DateTime.Now:yyyyMMdd-HHmmss}.mp4" };
                        if (recovery.ShowDialog() == true) { File.Move(saved, recovery.FileName, true); ShowNotification("Видео сохранено", Path.GetFileName(recovery.FileName)); }
                    }
                }
            }
            else
            {
                var dialog = new SaveFileDialog { Filter = "Видео MP4|*.mp4", DefaultExt = ".mp4", FileName = $"ReplayKit-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4" };
                if (dialog.ShowDialog() == true) { await _capture.StartVideoAsync(dialog.FileName); _overlay = new RecordingOverlay(_capture, ToggleVideo); _overlay.Closed += (_, _) => _overlay = null; _overlay.Show(); }
            }
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Видеозапись", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { _videoBusy = false; OnCaptureState(); }
    }
    public void OpenReplay()
    {
        if (_capture == null) return;
        if (_replayWindow != null) { _replayWindow.Activate(); return; }
        _replayWindow = new ReplayWindow(_capture.Replay.Snapshot(), _capture.Replay.Fps);
        _replayWindow.Closed += (_, _) => _replayWindow = null; _replayWindow.Show();
    }
    private void ShowDiagnostics()
    {
        if (_capture == null) return;
        MessageBox.Show($"ReplayKit {typeof(App).Assembly.GetName().Version}\nWindows: {Environment.OSVersion.Version}\nСеанс заблокирован: {_capture.Locked}\nКадров в истории: {_capture.Buffer.Snapshot(DateTimeOffset.Now).Length}\nВидеобуфер: {_capture.Replay.Bytes / 1024 / 1024} МБ\nЗахват: {_capture.Status}\nВидео: {_capture.VideoError ?? "ошибок нет"}\nВидеобуфер: {_capture.Replay.Error ?? "ошибок нет"}", "Диагностика · без изображений");
    }
    private void OnVideoSaved(string path) => Dispatcher.BeginInvoke(() => { if (!_exiting) ShowNotification("Видео сохранено", Path.GetFileName(path)); });
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) _capture?.SetLocked(true); if (e.Reason == SessionSwitchReason.SessionUnlock) _capture?.SetLocked(false); }
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) _capture?.SetLocked(true); if (e.Mode == PowerModes.Resume) _capture?.SetLocked(SessionState.IsLocked); }
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
        _activationWait?.Unregister(null); _quitWait?.Unregister(null); _capture?.Dispose(); _hotkeys?.Dispose();
        _tray?.Dispose(); _recordingIcon?.Dispose(); _pausedIcon?.Dispose(); _videoIcon?.Dispose(); _activation?.Dispose(); _quit?.Dispose(); _mutex?.Dispose();
        base.OnExit(e);
    }
}

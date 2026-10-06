using ReplayKit.Services;

namespace ReplayKit.Views;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Func<Settings, string?> _apply;
    private HotkeySpec _history, _recording;
    public SettingsWindow(Settings settings, Func<Settings, string?> apply)
    {
        _settings = settings; _apply = apply; _history = settings.HistoryHotkey; _recording = settings.RecordingHotkey;
        InitializeComponent(); HistoryKey.Text = _history.ToString(); RecordingKey.Text = _recording.ToString();
        AutoStart.IsChecked = settings.AutoStart;
        Theme.SelectedItem = Theme.Items.Cast<ComboBoxItem>().First(x => (string)x.Tag == settings.Theme);
    }
    private void OnLoaded(object sender, RoutedEventArgs e) { WindowPlacement.CenterAtCursor(this); WindowPlacement.ExcludeFromCapture(this); }
    private void OnHotkey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        uint mods = 0; var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= 2; if (m.HasFlag(ModifierKeys.Shift)) mods |= 4;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= 1; if (m.HasFlag(ModifierKeys.Windows)) mods |= 8;
        var spec = new HotkeySpec(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!spec.IsValid) return;
        if (sender == HistoryKey) { _history = spec; HistoryKey.Text = spec.ToString(); }
        else { _recording = spec; RecordingKey.Text = spec.ToString(); }
        Status.Text = "Проверим доступность при сохранении";
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        var error = _apply(new Settings { MonitorId = _settings.MonitorId, Theme = (string)((ComboBoxItem)Theme.SelectedItem).Tag, AutoStart = AutoStart.IsChecked == true, HistoryHotkey = _history, RecordingHotkey = _recording });
        if (error != null) Status.Text = error; else Close();
    }
    private void OnKey(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

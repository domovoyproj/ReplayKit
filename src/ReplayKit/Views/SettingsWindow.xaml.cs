using ReplayKit.Services;

namespace ReplayKit.Views;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Func<Settings, string?> _apply;
    private HotkeySpec _history, _recording, _video, _replay;
    public SettingsWindow(Settings settings, Func<Settings, string?> apply)
    {
        _settings = settings; _apply = apply; _history = settings.HistoryHotkey; _recording = settings.RecordingHotkey; _video = settings.VideoHotkey; _replay = settings.ReplayHotkey;
        InitializeComponent(); HistoryKey.Text = _history.ToString(); RecordingKey.Text = _recording.ToString();
        AutoStart.IsChecked = settings.AutoStart;
        Select(HistoryLength, settings.HistorySeconds); Select(HistoryQuality, settings.HistoryQuality);
        VideoKey.Text = _video.ToString(); ReplayKey.Text = _replay.ToString();
        Select(Fps, settings.Video.Fps); Select(VideoSize, settings.Video.MaxWidth); Select(Quality, settings.Video.Quality);
        SystemAudio.IsChecked = settings.Video.SystemAudio; Microphone.IsChecked = settings.Video.Microphone;
        SystemVolume.Value = settings.Video.SystemVolume; MicrophoneVolume.Value = settings.Video.MicrophoneVolume;
        ReplayEnabled.IsChecked = settings.ReplayEnabled; Select(ReplayLength, settings.ReplaySeconds); Select(ReplayMemory, settings.ReplayMemoryMb); Economy.IsChecked = settings.ReplayEconomy;
        Theme.SelectedItem = Theme.Items.Cast<ComboBoxItem>().First(x => (string)x.Tag == settings.Theme);
    }
    private static void Select(ComboBox box, int value) => box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value.ToString()) ?? box.Items[0];
    private static int Value(ComboBox box) => int.Parse((string)((ComboBoxItem)box.SelectedItem).Tag);
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
        else if (sender == VideoKey) { _video = spec; VideoKey.Text = spec.ToString(); }
        else if (sender == ReplayKey) { _replay = spec; ReplayKey.Text = spec.ToString(); }
        else { _recording = spec; RecordingKey.Text = spec.ToString(); }
        Status.Text = "Проверим доступность при сохранении";
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        var error = _apply(new Settings { MonitorId = _settings.MonitorId, Theme = (string)((ComboBoxItem)Theme.SelectedItem).Tag, AutoStart = AutoStart.IsChecked == true, HistorySeconds = Value(HistoryLength), HistoryQuality = Value(HistoryQuality), HistoryHotkey = _history, RecordingHotkey = _recording, VideoHotkey = _video, ReplayHotkey = _replay,
            Video = new VideoOptions(Value(Fps), Value(VideoSize), Value(Quality), SystemAudio.IsChecked == true, Microphone.IsChecked == true, SystemVolume.Value, MicrophoneVolume.Value),
            ReplayEnabled = ReplayEnabled.IsChecked == true, ReplaySeconds = Value(ReplayLength), ReplayMemoryMb = Value(ReplayMemory), ReplayEconomy = Economy.IsChecked == true });
        if (error != null) Status.Text = error; else Close();
    }
    private void OnKey(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

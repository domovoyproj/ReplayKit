using ReplayKit.Services;

namespace ReplayKit.Views;

public sealed class ReplayWindow : Window
{
    private readonly ReplayFrame[] _frames;
    private readonly int _fps;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new(12) };
    private readonly Slider _position = new() { IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly Slider _start = new() { IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly Slider _end = new() { IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly TextBlock _status = new() { Margin = new(4) };
    private readonly CheckBox _audio = new() { Content = "Со звуком", Margin = new(8) };
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private int _generation;
    private readonly System.Diagnostics.Stopwatch _playClock = new();
    private long _playOrigin;
    private ReplayPlayback? _playback;
    public ReplayWindow(ReplayFrame[] frames, int fps)
    {
        _frames = frames; _fps = fps; Title = "ReplayKit — видеобуфер"; Width = 960; Height = 740; MinWidth = 600; MinHeight = 500;
        Style = (Style)FindResource(typeof(Window));
        var root = new DockPanel { Margin = new(24) };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(Button("Закрыть", () => Close())); top.Children.Add(new TextBlock { Text = "Последние секунды", FontSize = 20, Margin = new(12) });
        top.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock) DragMove(); };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(_status); bottom.Children.Add(_position);
        bottom.Children.Add(new TextBlock { Text = "Начало фрагмента" }); bottom.Children.Add(_start);
        bottom.Children.Add(new TextBlock { Text = "Конец фрагмента" }); bottom.Children.Add(_end);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _audio.IsChecked = frames.Any(f => f.Audio != null); _audio.IsEnabled = _audio.IsChecked == true; actions.Children.Add(_audio);
        actions.Children.Add(Button("▶ / Ⅱ", TogglePlayback));
        var save = Button("Сохранить MP4", () => { }); save.Click += Save; actions.Children.Add(save); bottom.Children.Add(actions);
        root.Children.Add(_image);
        var surface = new Border { CornerRadius = new(24), Background = (Brush)FindResource("WindowBrush"), Child = root, Margin = new(12) };
        surface.SetResourceReference(Border.BackgroundProperty, "WindowBrush"); Content = surface;
        foreach (var slider in new[] { _position, _start, _end }) { slider.Maximum = Math.Max(0, frames.Length - 1); slider.IsEnabled = frames.Length > 0; }
        _end.Value = _end.Maximum;
        _position.ValueChanged += async (_, _) => await ShowFrame();
        _start.ValueChanged += (_, _) => { if (_start.Value > _end.Value) _end.Value = _start.Value; _position.Value = _start.Value; };
        _end.ValueChanged += (_, _) => { if (_end.Value < _start.Value) _start.Value = _end.Value; _position.Value = _end.Value; };
        _timer = new() { Interval = TimeSpan.FromMilliseconds(1000d / fps) };
        _timer.Tick += (_, _) =>
        {
            var target = _playOrigin + _playClock.Elapsed.Ticks;
            var index = (int)_position.Value;
            while (index < (int)_end.Value && _frames[index + 1].Ticks <= target) index++;
            _position.Value = index;
            if (target >= _frames[(int)_end.Value].Ticks + TimeSpan.TicksPerSecond / _fps) StopPlayback();
        };
        foreach (var slider in new[] { _position, _start, _end }) slider.PreviewMouseDown += (_, _) => StopPlayback();
        Loaded += async (_, _) => { WindowPlacement.CenterAtCursor(this); WindowPlacement.ExcludeFromCapture(this); await ShowFrame(); };
        Closed += (_, _) => { _generation++; StopPlayback(); _image.Source = null; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        save.IsEnabled = frames.Length > 0;
    }
    private void StopPlayback() { _timer?.Stop(); _playClock.Stop(); _playback?.Dispose(); _playback = null; }
    private void TogglePlayback()
    {
        if (_timer.IsEnabled) { StopPlayback(); return; }
        if (_frames.Length == 0) return;
        if (_position.Value >= _end.Value || _position.Value < _start.Value) _position.Value = _start.Value;
        try
        {
            if (_audio.IsChecked == true) _playback = new ReplayPlayback(_frames.Skip((int)_position.Value).Take((int)(_end.Value - _position.Value) + 1).ToArray(), _fps);
            _playOrigin = _frames[(int)_position.Value].Ticks; _playClock.Restart(); _timer.Start();
        }
        catch (Exception e) { StopPlayback(); _status.Text = e.Message; }
    }
    private static Button Button(string title, Action action) { var b = new Button { Content = title }; b.Click += (_, _) => action(); return b; }
    private async Task ShowFrame()
    {
        if (_frames.Length == 0) { _status.Text = "Видеобуфер пуст. Включите его в настройках и подождите несколько секунд."; return; }
        var generation = ++_generation; var index = (int)_position.Value;
        var image = await Task.Run(() => Images.Decode(_frames[index].Frame, 1280));
        if (generation != _generation) return;
        _image.Source = image;
        _status.Text = $"{(_frames[index].Ticks - _frames[0].Ticks) / (double)TimeSpan.TicksPerSecond:F1} с · выбран фрагмент {(_frames[(int)_end.Value].Ticks - _frames[(int)_start.Value].Ticks) / (double)TimeSpan.TicksPerSecond:F1} с";
    }
    private async void Save(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Видео MP4|*.mp4", DefaultExt = ".mp4", FileName = $"ReplayKit-Replay-{DateTime.Now:yyyyMMdd-HHmmss}.mp4" };
        if (dialog.ShowDialog(this) != true) return;
        var frames = _frames.Skip((int)_start.Value).Take((int)(_end.Value - _start.Value) + 1).ToArray(); var audio = _audio.IsChecked == true;
        ((Button)sender).IsEnabled = false;
        try { _status.Text = "Сохраняем…"; await Task.Run(() => ReplayBuffer.Export(dialog.FileName, frames, _fps, audio)); _status.Text = "Видео сохранено"; }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { ((Button)sender).IsEnabled = true; }
    }
}

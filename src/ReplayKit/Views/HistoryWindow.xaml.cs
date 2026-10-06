using ReplayKit.Services;
using System.Windows.Media.Animation;

namespace ReplayKit.Views;

public partial class HistoryWindow : Window
{
    private readonly CaptureService _capture;
    private readonly Action<string> _selectMonitor;
    private readonly Action _openSettings;
    private CaptureFrame[] _frames = Array.Empty<CaptureFrame>();
    private BitmapSource[] _previews = Array.Empty<BitmapSource>();
    private int _selected = -1;
    private int _loadGeneration;
    private bool _initializing = true;
    private bool _loading;
    private readonly System.Windows.Threading.DispatcherTimer _emptyTimer;
    public HistoryWindow(CaptureService capture, Settings settings, Action<string> selectMonitor, Action openSettings)
    {
        _capture = capture; _selectMonitor = selectMonitor; _openSettings = openSettings;
        InitializeComponent();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            Monitors.Items.Add(new ComboBoxItem { Content = $"Монитор {Monitors.Items.Count + 1} · {screen.Bounds.Width}×{screen.Bounds.Height}", Tag = screen.DeviceName });
        Monitors.SelectedItem = Monitors.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == capture.MonitorId);
        _initializing = false;
        _emptyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _emptyTimer.Tick += async (_, _) => { if (_frames.Length == 0 && !_loading) await LoadSnapshotAsync(); };
        _capture.StateChanged += OnCaptureState;
        Closed += (_, _) => { _loadGeneration++; _emptyTimer.Stop(); _capture.StateChanged -= OnCaptureState; _previews = Array.Empty<BitmapSource>(); _frames = Array.Empty<CaptureFrame>(); Preview.Source = null; Filmstrip.Children.Clear(); };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowPlacement.CenterAtCursor(this); WindowPlacement.ExcludeFromCapture(this);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        await LoadSnapshotAsync(); _emptyTimer.Start();
    }
    private void OnCaptureState() => Dispatcher.BeginInvoke(() =>
    {
        CaptureState.Text = _capture.Error != null ? "Захват недоступен" : _capture.Recording ? "● Запись" : "Ⅱ Пауза";
        if (_frames.Length == 0 && _capture.Error != null) EmptyHint.Text = _capture.Error;
    });
    private async Task LoadSnapshotAsync()
    {
        var generation = ++_loadGeneration;
        var frames = _capture.Buffer.Snapshot(DateTimeOffset.Now);
        SetActions(false);
        if (frames.Length == 0)
        {
            _frames = frames; _previews = Array.Empty<BitmapSource>(); _selected = -1;
            Preview.Source = null; EmptyState.Visibility = Visibility.Visible; Filmstrip.Children.Clear();
            EmptyTitle.Text = _capture.Recording ? "Ваша последняя минута" : "Запись на паузе";
            EmptyHint.Text = _capture.Error ?? (_capture.Recording ? "Первый кадр появится через секунду" : "Включите запись через меню в трее");
            FrameTime.Text = ""; FrameDetails.Text = ""; return;
        }
        EmptyTitle.Text = "Подготавливаем историю…";
        _loading = true;
        BitmapSource[] decoded;
        try { decoded = await Task.Run(() => frames.Select(f => Images.Decode(f, 1280)).ToArray()); }
        finally { _loading = false; }
        if (generation != _loadGeneration) return;
        _frames = frames; _previews = decoded; EmptyState.Visibility = Visibility.Collapsed;
        Timeline.Value = 0; SelectOffset(0); SetActions(true); BuildFilmstrip();
    }
    private void SetActions(bool enabled) { CopyButton.IsEnabled = SaveButton.IsEnabled = EditButton.IsEnabled = enabled; Timeline.IsEnabled = enabled; }
    private CaptureFrame? Selected => _selected >= 0 && _selected < _frames.Length ? _frames[_selected] : null;
    private void SelectOffset(int offset)
    {
        if (_frames.Length == 0 || _previews.Length != _frames.Length) return;
        var target = _frames[^1].CapturedAt.AddSeconds(offset);
        _selected = Enumerable.Range(0, _frames.Length).MinBy(i => Math.Abs((_frames[i].CapturedAt - target).TotalMilliseconds));
        Preview.Source = _previews[_selected];
        var frame = _frames[_selected];
        var age = (int)Math.Round((frame.CapturedAt - _frames[^1].CapturedAt).TotalSeconds);
        RelativeTime.Text = age == 0 ? "Последний кадр" : $"{age} секунд";
        FrameTime.Text = frame.CapturedAt.ToString("HH:mm:ss.fff");
        FrameDetails.Text = $"{frame.Width} × {frame.Height} · {frame.CapturedAt:dd.MM.yyyy}";
        foreach (Border border in Filmstrip.Children) border.BorderBrush = (Brush)FindResource((int)border.Tag == _selected ? "AccentBrush" : "LineBrush");
    }
    private void BuildFilmstrip()
    {
        Filmstrip.Children.Clear();
        var count = Math.Min(12, _frames.Length);
        for (var t = 0; t < count; t++)
        {
            var index = count == 1 ? 0 : (int)Math.Round(t * (_frames.Length - 1d) / (count - 1));
            var border = new Border { Width = 74, Height = 43, Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(2), BorderBrush = (Brush)FindResource("LineBrush"), Tag = index, Cursor = Cursors.Hand, ToolTip = _frames[index].CapturedAt.ToString("HH:mm:ss") };
            border.Child = new Image { Source = _previews[index], Stretch = Stretch.UniformToFill };
            border.MouseLeftButtonDown += (_, _) => Timeline.Value = Math.Round((_frames[index].CapturedAt - _frames[^1].CapturedAt).TotalSeconds);
            Filmstrip.Children.Add(border);
        }
        SelectOffset((int)Timeline.Value);
    }
    private void OnTimeline(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_initializing) SelectOffset((int)Math.Round(e.NewValue)); }
    private void Step(int delta) { Timeline.Value = Math.Clamp(Timeline.Value + delta, -59, 0); }
    private void OnWheel(object sender, MouseWheelEventArgs e) { Step(e.Delta > 0 ? 1 : -1); e.Handled = true; }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadSnapshotAsync();
    private async void OnMonitor(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || Monitors.SelectedItem is not ComboBoxItem item) return;
        _selectMonitor((string)item.Tag); await LoadSnapshotAsync();
    }
    private async Task CopyAsync()
    {
        if (Selected is not { } frame) return;
        await Images.CopyAsync(await Task.Run(() => Images.Decode(frame)));
        CopyButton.Content = "Скопировано ✓"; await Task.Delay(1400); CopyButton.Content = "Копировать";
    }
    private void Save() { if (Selected is { } frame) Images.SavePng(Images.Decode(frame), this); }
    private void Edit() { if (Selected is { } frame) new EditorWindow(Images.Decode(frame)) { Owner = this }.ShowDialog(); }
    private async void OnCopy(object sender, RoutedEventArgs e) => await CopyAsync();
    private void OnSave(object sender, RoutedEventArgs e) => Save();
    private void OnEdit(object sender, RoutedEventArgs e) => Edit();
    private async void OnKey(object sender, KeyEventArgs e)
    {
        if (Monitors.IsDropDownOpen) return;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift); var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Left: Step(shift ? -5 : -1); break;
            case Key.Right: Step(shift ? 5 : 1); break;
            case Key.Home: Timeline.Value = _frames.Length == 0 ? -59 : Math.Max(-59, Math.Round((_frames[0].CapturedAt - _frames[^1].CapturedAt).TotalSeconds)); break;
            case Key.End: Timeline.Value = 0; break;
            case Key.C when control: await CopyAsync(); break;
            case Key.S when control: Save(); break;
            case Key.E when !control: Edit(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is not Button && e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnSettings(object sender, RoutedEventArgs e) => _openSettings();
    private void OnWindowSize(object sender, SizeChangedEventArgs e)
    {
        if (Actions == null) return;
        var narrow = ActualWidth < 890;
        Grid.SetRow(Actions, narrow ? 1 : 0); Grid.SetColumn(Actions, narrow ? 0 : 1);
        Grid.SetColumnSpan(Actions, narrow ? 2 : 1); Actions.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }
}

using ReplayKit.Services;
using System.Windows.Media.Animation;

namespace ReplayKit.Views;

public partial class HistoryWindow : Window
{
    private readonly CaptureService _capture;
    private readonly Action<string> _selectMonitor;
    private readonly Action _openSettings;
    private readonly Action _toggleVideo;
    private CaptureFrame[] _frames = Array.Empty<CaptureFrame>();
    private BitmapSource?[] _previews = Array.Empty<BitmapSource?>();
    private readonly Dictionary<int, BitmapSource> _previewCache = new();
    private readonly LinkedList<int> _previewOrder = new();
    private readonly Dictionary<int, Image> _filmstripImages = new();
    private readonly Dictionary<int, Border> _filmstripBorders = new();
    private int _highlightedFilmstrip = -1;
    private CaptureFrame? _comparison;
    private int _selected = -1;
    private int _loadGeneration;
    private int _previewRequest;
    private bool _initializing = true;
    private bool _loading;
    private readonly System.Windows.Threading.DispatcherTimer _emptyTimer;
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer;
    public HistoryWindow(CaptureService capture, Settings settings, Action<string> selectMonitor, Action openSettings, Action? toggleVideo = null)
    {
        _capture = capture; _selectMonitor = selectMonitor; _openSettings = openSettings;
        _toggleVideo = toggleVideo ?? (() => { });
        InitializeComponent();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            Monitors.Items.Add(new ComboBoxItem { Content = $"Монитор {Monitors.Items.Count + 1} · {screen.Bounds.Width}×{screen.Bounds.Height}", Tag = screen.DeviceName });
        Monitors.SelectedItem = Monitors.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == capture.MonitorId);
        _initializing = false;
        _emptyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _emptyTimer.Tick += async (_, _) => { if (_frames.Length == 0 && !_loading) await LoadSnapshotAsync(); };
        _previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); if (_selected >= 0) await LoadSelectedPreviewAsync(_selected, _loadGeneration, _previewRequest); };
        _capture.StateChanged += OnCaptureState;
        OnCaptureState();
        Closed += (_, _) => { _loadGeneration++; _previewRequest++; _emptyTimer.Stop(); _previewTimer.Stop(); _capture.StateChanged -= OnCaptureState; _previews = Array.Empty<BitmapSource?>(); _previewCache.Clear(); _previewOrder.Clear(); _filmstripImages.Clear(); _filmstripBorders.Clear(); _frames = Array.Empty<CaptureFrame>(); Preview.Source = null; Filmstrip.Children.Clear(); };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowPlacement.CenterAtCursor(this); WindowPlacement.ExcludeFromCapture(this);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        await LoadSnapshotAsync(); _emptyTimer.Start();
    }
    private void OnCaptureState() => Dispatcher.BeginInvoke(() =>
    {
        var duration = _capture.VideoDuration;
        CaptureState.Text = _capture.VideoRecording ? $"{(_capture.VideoPaused ? "Ⅱ" : "●")} {(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}" : _capture.Error != null ? "Захват недоступен" : _capture.Recording ? "Буфер активен" : _capture.Replay.Enabled && !_capture.Locked ? "Видеобуфер активен" : "Пауза";
        VideoButton.Content = _capture.VideoRecording ? "■ Стоп" : _capture.VideoPending ? "Подготовка…" : "● Видео";
        Monitors.IsEnabled = !_capture.VideoPending;
        VideoButton.IsEnabled = !_capture.VideoPending || _capture.VideoRecording;
        if (_frames.Length == 0) EmptyHint.Text = _capture.Status;
    });
    private async Task LoadSnapshotAsync()
    {
        var generation = ++_loadGeneration;
        var frames = _capture.Buffer.Snapshot(DateTimeOffset.Now);
        _previewRequest++;
        SetActions(false);
        if (frames.Length == 0)
        {
            _frames = frames; _previews = Array.Empty<BitmapSource?>(); _selected = -1;
            _previewCache.Clear(); _previewOrder.Clear(); _filmstripImages.Clear(); _filmstripBorders.Clear();
            Preview.Source = null; EmptyState.Visibility = Visibility.Visible; Filmstrip.Children.Clear();
            EmptyTitle.Text = _capture.Recording ? "История скоро появится" : "Запись на паузе";
            EmptyHint.Text = _capture.Error ?? (_capture.Recording ? "Первый кадр появится через секунду" : "Включите запись через меню в трее");
            FrameTime.Text = ""; FrameDetails.Text = ""; FrameCounter.Text = "0 кадров"; LoadingBadge.Visibility = Visibility.Collapsed; return;
        }
        _loading = true;
        await Task.Yield();
        _loading = false;
        if (generation != _loadGeneration) return;
        _frames = frames; _previews = new BitmapSource?[frames.Length];
        _previewCache.Clear(); _previewOrder.Clear(); _filmstripImages.Clear(); _filmstripBorders.Clear(); _highlightedFilmstrip = -1;
        EmptyState.Visibility = Visibility.Collapsed;
        var seconds = _capture.HistorySeconds;
        Timeline.Minimum = -(seconds - 1); TimelineCaption.Text = $"ИСТОРИЯ · {seconds} СЕКУНД";
        OldestLabel.Text = $"−{seconds - 1} с"; MiddleLabel.Text = $"−{seconds / 2} с"; FrameCounter.Text = $"{frames.Length} из {seconds}";
        Timeline.Value = 0; BuildFilmstrip(); SelectOffset(0); SetActions(true);
    }
    private void SetActions(bool enabled) { CopyButton.IsEnabled = SaveButton.IsEnabled = EditButton.IsEnabled = enabled; Timeline.IsEnabled = enabled; }
    private CaptureFrame? Selected => _selected >= 0 && _selected < _frames.Length ? _frames[_selected] : null;
    private void SelectOffset(int offset)
    {
        if (_frames.Length == 0 || _previews.Length != _frames.Length) return;
        var target = _frames[^1].CapturedAt.AddSeconds(offset);
        _selected = NearestFrame(target);
        _previewRequest++; _previewTimer.Stop();
        if (_previewCache.TryGetValue(_selected, out var cached)) { Preview.Source = cached; TouchPreview(_selected); LoadingBadge.Visibility = Visibility.Collapsed; }
        else { if (_previews[_selected] != null) Preview.Source = _previews[_selected]; LoadingBadge.Visibility = Visibility.Visible; _previewTimer.Start(); }
        var frame = _frames[_selected];
        var age = (int)Math.Round((frame.CapturedAt - _frames[^1].CapturedAt).TotalSeconds);
        RelativeTime.Text = age == 0 ? "Последний кадр" : $"{age} секунд";
        FrameTime.Text = frame.CapturedAt.ToString("HH:mm:ss.fff");
        FrameDetails.Text = $"{frame.Width} × {frame.Height} · {frame.CapturedAt:dd.MM.yyyy}";
        if (_highlightedFilmstrip != _selected)
        {
            if (_filmstripBorders.TryGetValue(_highlightedFilmstrip, out var previous)) previous.BorderBrush = (Brush)FindResource("LineBrush");
            if (_filmstripBorders.TryGetValue(_selected, out var current)) current.BorderBrush = (Brush)FindResource("AccentBrush");
            _highlightedFilmstrip = _selected;
        }
    }
    private int NearestFrame(DateTimeOffset target)
    {
        var low = 0; var high = _frames.Length - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_frames[middle].CapturedAt < target) low = middle + 1; else high = middle;
        }
        if (low == 0) return 0;
        return Math.Abs((_frames[low].CapturedAt - target).Ticks) < Math.Abs((_frames[low - 1].CapturedAt - target).Ticks) ? low : low - 1;
    }
    private async Task LoadSelectedPreviewAsync(int index, int generation, int request)
    {
        if (generation != _loadGeneration || request != _previewRequest || index >= _frames.Length) return;
        var frame = _frames[index];
        if (_previews[index] == null)
        {
            var quick = await Task.Run(() => Images.Decode(frame, 360));
            if (generation != _loadGeneration || request != _previewRequest) return;
            _previews[index] = quick; Preview.Source = quick;
        }
        var decoded = await Task.Run(() => Images.Decode(frame, 1600));
        if (generation != _loadGeneration) return;
        CachePreview(index, decoded);
        if (request == _previewRequest && index == _selected) { Preview.Source = decoded; LoadingBadge.Visibility = Visibility.Collapsed; }
    }
    private void CachePreview(int index, BitmapSource image)
    {
        _previewCache[index] = image; TouchPreview(index);
        while (_previewOrder.Count > 5)
        {
            var remove = _previewOrder.First!.Value; _previewOrder.RemoveFirst(); _previewCache.Remove(remove);
        }
    }
    private void TouchPreview(int index)
    {
        _previewOrder.Remove(index); _previewOrder.AddLast(index);
    }
    private void BuildFilmstrip()
    {
        Filmstrip.Children.Clear(); _filmstripImages.Clear(); _filmstripBorders.Clear(); _highlightedFilmstrip = -1;
        var count = Math.Min(12, _frames.Length);
        for (var t = 0; t < count; t++)
        {
            var index = count == 1 ? 0 : (int)Math.Round(t * (_frames.Length - 1d) / (count - 1));
            var border = new Border { Width = 74, Height = 43, Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(2), BorderBrush = (Brush)FindResource("LineBrush"), Tag = index, Cursor = Cursors.Hand, ToolTip = _frames[index].CapturedAt.ToString("HH:mm:ss") };
            var image = new Image { Stretch = Stretch.UniformToFill }; border.Child = image; _filmstripImages[index] = image;
            _filmstripBorders[index] = border;
            border.MouseLeftButtonDown += (_, _) => Timeline.Value = Math.Round((_frames[index].CapturedAt - _frames[^1].CapturedAt).TotalSeconds);
            Filmstrip.Children.Add(border);
        }
        _ = LoadFilmstripAsync(_loadGeneration);
    }
    private async Task LoadFilmstripAsync(int generation)
    {
        var frames = _frames;
        var decoded = await Task.Run(() => Enumerable.Range(0, frames.Length).Select(i => (Index: i, Image: Images.Decode(frames[i], 120))).ToArray());
        if (generation != _loadGeneration) return;
        foreach (var pair in decoded)
        {
            _previews[pair.Index] = pair.Image;
            if (_filmstripImages.TryGetValue(pair.Index, out var image)) image.Source = pair.Image;
            if (pair.Index == _selected && !_previewCache.ContainsKey(pair.Index)) Preview.Source = pair.Image;
        }
    }
    private void OnTimeline(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_initializing) SelectOffset((int)Math.Round(e.NewValue)); }
    private void Step(int delta) { Timeline.Value = Math.Clamp(Timeline.Value + delta, Timeline.Minimum, 0); }
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
            case Key.Home: Timeline.Value = _frames.Length == 0 ? Timeline.Minimum : Math.Max(Timeline.Minimum, Math.Round((_frames[0].CapturedAt - _frames[^1].CapturedAt).TotalSeconds)); break;
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
    private void OnVideo(object sender, RoutedEventArgs e) => _toggleVideo();
    private void OnMore(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Add(string title, Action action, bool enabled = true) { var item = new MenuItem { Header = title, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Закрепить кадр", () => new ImageToolsWindow(Images.Decode(Selected!)).Show(), Selected != null);
        Add(_comparison == null ? "Выбрать кадр для сравнения" : "Сравнить с выбранным кадром", () => { if (_comparison == null) { _comparison = Selected; RelativeTime.Text = "Выберите второй кадр → Ещё → Сравнить"; } else { new ImageToolsWindow(Images.Decode(_comparison), Images.Decode(Selected!)).Show(); _comparison = null; } }, Selected != null);
        Add("Распознать текст", Recognize, Selected != null);
        Add("Экспортировать историю в ZIP", ExportHistory, _frames.Length > 0);
        Add("Выбрать окно / экран", PickSource, !_capture.VideoPending);
        Add("Выделить область монитора", PickRegion, Selected != null && !_capture.VideoPending && _capture.IsMonitorSource);
        Add("Захватывать весь монитор", () => { _capture.SelectSource(null); _ = LoadSnapshotAsync(); }, !_capture.VideoPending);
        Add("Видеобуфер", () => (Application.Current as App)?.OpenReplay());
        menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
    }
    private async void ExportHistory()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Архив ReplayKit|*.zip", DefaultExt = ".zip", FileName = $"ReplayKit-history-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            RelativeTime.Text = "Экспортируем историю…";
            var progress = new Progress<int>(value => RelativeTime.Text = $"Экспорт истории · {value}%");
            await Task.Run(() => HistoryArchive.Export(dialog.FileName, _frames, progress));
            RelativeTime.Text = "История сохранена ✓";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Экспорт истории"); }
    }
    private async void Recognize()
    {
        if (Selected is not { } frame) return;
        try
        {
            RelativeTime.Text = "Распознаём…";
            var text = await TextRecognition.ReadAsync(Images.Decode(frame));
            if (string.IsNullOrWhiteSpace(text)) { RelativeTime.Text = "Текст не найден"; return; }
            Clipboard.SetText(text); RelativeTime.Text = "Текст скопирован ✓";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Распознавание текста"); }
    }
    private async void PickSource()
    {
        try
        {
            if (!Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported()) { MessageBox.Show(this, "Windows Graphics Capture недоступен."); return; }
            var picker = new Windows.Graphics.Capture.GraphicsCapturePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var item = await picker.PickSingleItemAsync();
            if (item != null) { _capture.SelectSource(item); await LoadSnapshotAsync(); }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Источник захвата"); }
    }
    private async void PickRegion()
    {
        if (Selected is not { } frame) return;
        var dialog = new ImageToolsWindow(Images.Decode(frame), selectRegion: true) { Owner = this };
        if (dialog.ShowDialog() == true) { _capture.SelectSource(null, dialog.SelectedRegion); await LoadSnapshotAsync(); }
    }
    private void OnWindowSize(object sender, SizeChangedEventArgs e)
    {
        if (Actions == null) return;
        AppTitle.Visibility = ActualWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
        var narrow = ActualWidth < 890;
        Grid.SetRow(Actions, narrow ? 1 : 0); Grid.SetColumn(Actions, narrow ? 0 : 1);
        Grid.SetColumnSpan(Actions, narrow ? 2 : 1); Actions.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }
}

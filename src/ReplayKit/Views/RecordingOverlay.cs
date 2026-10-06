using ReplayKit.Services;

namespace ReplayKit.Views;

public sealed class RecordingOverlay : Window
{
    private readonly CaptureService _capture;
    private readonly TextBlock _time = new() { Margin = new(12), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _pause = new();
    public RecordingOverlay(CaptureService capture, Action stop)
    {
        _capture = capture; Width = 340; Height = 86; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        Style = (Style)FindResource(typeof(Window));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8) };
        panel.Children.Add(_time); _pause.Content = "Ⅱ"; _pause.Click += (_, _) => capture.ToggleVideoPause(); panel.Children.Add(_pause);
        var button = new Button { Content = "■ Стоп" }; button.Click += (_, _) => stop(); panel.Children.Add(button);
        var surface = new Border { CornerRadius = new(18), Background = (Brush)FindResource("WindowBrush"), Child = panel };
        surface.SetResourceReference(Border.BackgroundProperty, "WindowBrush"); Content = surface;
        _time.MouseLeftButtonDown += (_, _) => DragMove();
        Loaded += (_, _) => { WindowPlacement.CenterAtCursor(this); WindowPlacement.ExcludeFromCapture(this); };
        capture.StateChanged += Update; Closed += (_, _) => capture.StateChanged -= Update; Update();
    }
    private void Update() => Dispatcher.BeginInvoke(() =>
    {
        var time = _capture.VideoDuration;
        _time.Text = $"● {(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        _pause.Content = _capture.VideoPaused ? "▶" : "Ⅱ";
        if (!_capture.VideoPending) Close();
    });
}

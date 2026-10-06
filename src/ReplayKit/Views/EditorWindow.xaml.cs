using ReplayKit.Services;
using System.Globalization;
using System.Windows.Shapes;

namespace ReplayKit.Views;

public partial class EditorWindow : Window
{
    private BitmapSource _image;
    private readonly List<BitmapSource> _undo = new();
    private readonly List<BitmapSource> _redo = new();
    private string _tool = "Pen";
    private Point? _start;
    private readonly List<Point> _points = new();
    private Point _end;
    private TextBox? _textBox;
    public EditorWindow(BitmapSource image) { _image = image; InitializeComponent(); UpdateImage(); MarkTool(); }
    private void OnLoaded(object sender, RoutedEventArgs e) { WindowPlacement.CenterAtCursor(this); WindowPlacement.ExcludeFromCapture(this); }
    private Brush Ink => new SolidColorBrush((Color)ColorConverter.ConvertFromString((string)((ComboBoxItem)ColorPicker.SelectedItem).Tag));
    private double LineWidth => ThicknessSlider.Value * Math.Max(1, _image.PixelWidth / 1500d);
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, _image.PixelWidth - 1), Math.Clamp(p.Y, 0, _image.PixelHeight - 1));
    private void UpdateImage()
    {
        Surface.Width = Overlay.Width = BaseImage.Width = _image.PixelWidth;
        Surface.Height = Overlay.Height = BaseImage.Height = _image.PixelHeight;
        BaseImage.Source = _image; Dimensions.Text = $"{_image.PixelWidth} × {_image.PixelHeight}";
        UndoButton.IsEnabled = _undo.Count > 0; RedoButton.IsEnabled = _redo.Count > 0;
    }
    private void MarkTool()
    {
        foreach (var button in Tools.Children.OfType<Button>())
            button.Background = (Brush)FindResource((string)button.Tag == _tool ? "HoverBrush" : "PanelBrush");
        Surface.Cursor = _tool == "Text" ? Cursors.IBeam : Cursors.Cross;
    }
    private void OnTool(object sender, RoutedEventArgs e) { CommitText(); _tool = (string)((Button)sender).Tag; MarkTool(); }
    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (_textBox != null) { CommitText(); return; }
        var point = Clamp(e.GetPosition(Surface));
        if (_tool == "Text") { BeginText(point); e.Handled = true; return; }
        _start = _end = point; _points.Clear(); _points.Add(point); Surface.CaptureMouse(); e.Handled = true;
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_start == null) return;
        _end = Clamp(e.GetPosition(Surface));
        if (_tool == "Pen" && (_end - _points[^1]).Length > 1) _points.Add(_end);
        DrawPreview();
    }
    private Rect Selection => new(_start ?? _end, _end);
    private void DrawPreview()
    {
        Overlay.Children.Clear();
        var rect = Selection;
        if (_tool == "Pen")
            Overlay.Children.Add(new Polyline { Points = new PointCollection(_points), Stroke = Ink, StrokeThickness = LineWidth, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        else if (_tool == "Arrow")
        {
            Overlay.Children.Add(new Line { X1 = _start!.Value.X, Y1 = _start.Value.Y, X2 = _end.X, Y2 = _end.Y, Stroke = Ink, StrokeThickness = LineWidth });
            Overlay.Children.Add(new Polygon { Points = ArrowHead(_start.Value, _end), Fill = Ink });
        }
        else
        {
            var shape = new Rectangle { Width = rect.Width, Height = rect.Height, StrokeThickness = _tool == "Crop" ? 3 : LineWidth,
                Stroke = _tool is "Crop" or "Rectangle" ? Ink : null,
                Fill = _tool == "Redact" ? Brushes.Black : _tool == "Highlight" ? Ink : Brushes.Transparent,
                Opacity = _tool == "Highlight" ? 0.35 : 1 };
            if (_tool == "Crop") shape.StrokeDashArray = new DoubleCollection { 6, 4 };
            Canvas.SetLeft(shape, rect.Left); Canvas.SetTop(shape, rect.Top); Overlay.Children.Add(shape);
        }
    }
    private PointCollection ArrowHead(Point start, Point end)
    {
        var direction = end - start; if (direction.Length < 1) return new PointCollection();
        direction.Normalize(); var perpendicular = new Vector(-direction.Y, direction.X);
        var size = LineWidth * 4;
        return new PointCollection { end, end - direction * size + perpendicular * size * 0.48, end - direction * size - perpendicular * size * 0.48 };
    }
    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_start == null) return;
        _end = Clamp(e.GetPosition(Surface)); DrawPreview();
        var rect = Selection;
        if (_tool == "Crop" && rect.Width >= 3 && rect.Height >= 3)
        {
            Push(Images.Crop(_image, new Int32Rect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height)));
        }
        else if (_tool != "Crop" && (_tool == "Pen" || (_end - _start.Value).Length > 2))
        {
            var start = _start.Value; var end = _end; var points = _points.ToArray(); var tool = _tool; var ink = Ink; var width = LineWidth;
            var head = ArrowHead(start, end);
            Push(Render(dc =>
            {
                var pen = new Pen(ink, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                if (tool == "Pen")
                {
                    if (points.Length == 1) dc.DrawEllipse(ink, null, points[0], width / 2, width / 2);
                    else for (var i = 1; i < points.Length; i++) dc.DrawLine(pen, points[i - 1], points[i]);
                }
                else if (tool == "Arrow")
                {
                    dc.DrawLine(pen, start, end);
                    if (head.Count > 0)
                    {
                        var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(head[0], true, true); context.PolyLineTo(head.Skip(1).ToArray(), true, false); }
                        dc.DrawGeometry(ink, null, geometry);
                    }
                }
                else if (tool == "Rectangle") dc.DrawRectangle(null, pen, rect);
                else if (tool == "Redact") dc.DrawRectangle(Brushes.Black, null, rect);
                else if (tool == "Highlight") { dc.PushOpacity(0.35); dc.DrawRectangle(ink, null, rect); dc.Pop(); }
            }));
        }
        _start = null; Surface.ReleaseMouseCapture(); Overlay.Children.Clear(); e.Handled = true;
    }
    private BitmapSource Render(Action<DrawingContext> annotation)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) { dc.DrawImage(_image, new Rect(0, 0, _image.PixelWidth, _image.PixelHeight)); annotation(dc); }
        var result = new RenderTargetBitmap(_image.PixelWidth, _image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    private void Push(BitmapSource image)
    {
        _undo.Add(_image); _redo.Clear(); _image = image;
        // Bound raster undo memory independently of the replay buffer.
        while (_undo.Count > 1 && _undo.Sum(x => (long)x.PixelWidth * x.PixelHeight * 4) > 160L * 1024 * 1024) _undo.RemoveAt(0);
        UpdateImage();
    }
    private void BeginText(Point point)
    {
        _textBox = new TextBox { MinWidth = 180, FontSize = Math.Max(24, _image.PixelWidth / 55d), Foreground = Ink };
        Canvas.SetLeft(_textBox, point.X); Canvas.SetTop(_textBox, point.Y); Surface.Children.Add(_textBox);
        _textBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitText(); e.Handled = true; } if (e.Key == Key.Escape) { CancelText(); e.Handled = true; } };
        _textBox.Focus();
    }
    private void CommitText()
    {
        if (_textBox == null) return;
        var box = _textBox; var text = box.Text; var point = new Point(Canvas.GetLeft(box), Canvas.GetTop(box));
        CancelText();
        if (string.IsNullOrWhiteSpace(text)) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), box.FontSize, box.Foreground, 1);
        Push(Render(dc => dc.DrawText(formatted, point)));
    }
    private void CancelText() { if (_textBox != null) Surface.Children.Remove(_textBox); _textBox = null; }
    private void Undo() { CommitText(); if (_undo.Count == 0) return; _redo.Add(_image); _image = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); UpdateImage(); }
    private void Redo() { CommitText(); if (_redo.Count == 0) return; _undo.Add(_image); _image = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); UpdateImage(); }
    private void OnUndo(object sender, RoutedEventArgs e) => Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => Redo();
    private void OnLostCapture(object sender, MouseEventArgs e) { _start = null; Overlay.Children.Clear(); }
    private void OnSave(object sender, RoutedEventArgs e) { CommitText(); Images.SavePng(_image, this); }
    private async void OnCopy(object sender, RoutedEventArgs e) { CommitText(); await Images.CopyAsync(_image); CopyButton.Content = "Скопировано ✓"; await Task.Delay(1400); CopyButton.Content = "Копировать"; }
    private async void OnKey(object sender, KeyEventArgs e)
    {
        if (_textBox != null) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Z) Undo(); else if (e.Key == Key.Y) Redo();
            else if (e.Key == Key.C) await Images.CopyAsync(_image); else if (e.Key == Key.S) Images.SavePng(_image, this); else return;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void OnClose(object sender, RoutedEventArgs e) { CommitText(); Close(); }
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}

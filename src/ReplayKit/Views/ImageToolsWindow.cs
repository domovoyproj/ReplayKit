using ReplayKit.Services;

namespace ReplayKit.Views;

public sealed class ImageToolsWindow : Window
{
    public Int32Rect? SelectedRegion { get; private set; }
    public ImageToolsWindow(BitmapSource image, BitmapSource? compare = null, bool selectRegion = false)
    {
        Title = selectRegion ? "Выделите область" : compare == null ? "Закреплённый кадр" : "Сравнение кадров";
        Width = compare == null ? 800 : 1180; Height = 620; MinWidth = 320; MinHeight = 240;
        Style = (Style)FindResource(typeof(Window)); Topmost = !selectRegion && compare == null;
        var root = new DockPanel { Margin = new(16) };
        var header = WindowChrome.Create(this, Title); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var grid = new Grid(); root.Children.Add(grid);
        if (selectRegion)
        {
            var canvas = new Canvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = Brushes.Transparent };
            canvas.Children.Add(new Image { Source = image, Width = image.PixelWidth, Height = image.PixelHeight, IsHitTestVisible = false });
            var rectangle = new System.Windows.Shapes.Rectangle { Stroke = Brushes.DeepSkyBlue, StrokeThickness = Math.Max(2, image.PixelWidth / 500d), Fill = new SolidColorBrush(Color.FromArgb(40, 100, 140, 255)), IsHitTestVisible = false };
            canvas.Children.Add(rectangle); Point? start = null;
            canvas.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(canvas); canvas.CaptureMouse(); };
            canvas.MouseMove += (_, e) =>
            {
                if (start == null) return; var r = new Rect(start.Value, e.GetPosition(canvas));
                Canvas.SetLeft(rectangle, r.X); Canvas.SetTop(rectangle, r.Y); rectangle.Width = r.Width; rectangle.Height = r.Height;
            };
            canvas.MouseLeftButtonUp += (_, e) =>
            {
                if (start == null) return;
                var r = Rect.Intersect(new Rect(start.Value, e.GetPosition(canvas)), new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                start = null; canvas.ReleaseMouseCapture();
                if (!r.IsEmpty && r.Width >= 4 && r.Height >= 4) { SelectedRegion = new((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height); DialogResult = true; }
            };
            grid.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
        }
        else
        {
            grid.ColumnDefinitions.Add(new()); if (compare != null) grid.ColumnDefinitions.Add(new());
            UIElement Panel(BitmapSource source, string label)
            {
                var panel = new DockPanel { Margin = new(8) };
                if (compare != null) { var caption = new TextBlock { Text = label, Margin = new(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Center }; DockPanel.SetDock(caption, Dock.Top); panel.Children.Add(caption); }
                panel.Children.Add(new Image { Source = source, Stretch = Stretch.Uniform }); return panel;
            }
            grid.Children.Add(Panel(image, "Выбранный ранее"));
            if (compare != null) { var second = Panel(compare, "Текущий кадр"); Grid.SetColumn(second, 1); grid.Children.Add(second); }
        }
        var surface = new Border { CornerRadius = new(24), Background = (Brush)FindResource("WindowBrush"), Child = root, Margin = new(12) };
        surface.SetResourceReference(Border.BackgroundProperty, "WindowBrush"); Content = surface;
        Loaded += (_, _) => { WindowPlacement.CenterAtCursor(this); WindowPlacement.ExcludeFromCapture(this); };
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Escape) Close(); if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) await Images.CopyAsync(image); if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Images.SavePng(image, this); };
    }
}

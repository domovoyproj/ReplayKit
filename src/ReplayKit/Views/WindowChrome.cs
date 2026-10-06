namespace ReplayKit.Views;

internal static class WindowChrome
{
    public static Grid Create(Window window, string title)
    {
        var grid = new Grid { Height = 42, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Add(string color, string tooltip, Action action)
        {
            var button = new Button { Style = (Style)window.FindResource("TrafficButton"), Background = (Brush)new BrushConverter().ConvertFromString(color)!, ToolTip = tooltip };
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            button.Click += (_, _) => action(); buttons.Children.Add(button);
        }
        Add("#FF645B", "Закрыть", () => window.Close());
        Add("#FFBE43", "Свернуть", () => window.WindowState = WindowState.Minimized);
        void Maximize() => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        Add("#30C94C", "Развернуть", Maximize);
        grid.Children.Add(buttons);
        var caption = new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(caption, 1); grid.Children.Add(caption);
        grid.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Grid or TextBlock) { if (e.ClickCount == 2) Maximize(); else window.DragMove(); } };
        return grid;
    }
}

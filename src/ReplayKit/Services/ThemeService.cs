using Microsoft.Win32;

namespace ReplayKit.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool dark = theme == "Dark" || (theme == "System" && Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0);
        var palette = new Dictionary<string, string> {
            ["WindowBrush"] = dark ? "#ED171B26" : "#F4F4F6FA",
            ["PanelBrush"] = dark ? "#202635" : "#FFFFFF",
            ["TextBrush"] = dark ? "#EDF0FA" : "#202638",
            ["MutedBrush"] = dark ? "#98A4BF" : "#66728B",
            ["LineBrush"] = dark ? "#384155" : "#DCE2EE",
            ["HoverBrush"] = dark ? "#35415A" : "#E9EEFA",
            ["AccentBrush"] = dark ? "#A7B6FF" : "#5669DE",
            ["AccentTextBrush"] = dark ? "#171B26" : "#FFFFFF"
        };
        foreach (var (name, value) in palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze();
            Application.Current.Resources[name] = brush;
        }
    }
}

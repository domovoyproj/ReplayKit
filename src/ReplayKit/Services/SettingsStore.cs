using System.Text.Json;
using Microsoft.Win32;

namespace ReplayKit.Services;

public sealed class Settings
{
    public string? MonitorId { get; set; }
    public string Theme { get; set; } = "System";
    public bool AutoStart { get; set; } = true;
    public HotkeySpec HistoryHotkey { get; set; } = HotkeySpec.History;
    public HotkeySpec RecordingHotkey { get; set; } = HotkeySpec.Recording;
    public HotkeySpec VideoHotkey { get; set; } = new(6, 0x56);
    public HotkeySpec ReplayHotkey { get; set; } = new(6, 0x42);
    public VideoOptions Video { get; set; } = new();
    public bool ReplayEnabled { get; set; }
    public int ReplaySeconds { get; set; } = 30;
    public int ReplayMemoryMb { get; set; } = 256;
    public bool ReplayEconomy { get; set; } = true;
}

public static class SettingsStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplayKit");
    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new();
            if (settings.HistoryHotkey?.IsValid != true) settings.HistoryHotkey = HotkeySpec.History;
            if (settings.RecordingHotkey?.IsValid != true || settings.RecordingHotkey == settings.HistoryHotkey) settings.RecordingHotkey = HotkeySpec.Recording;
            if (settings.Theme is not ("Light" or "Dark" or "System")) settings.Theme = "System";
            settings.Video = (settings.Video ?? new()).Normalize();
            if (settings.VideoHotkey?.IsValid != true) settings.VideoHotkey = new(6, 0x56);
            if (settings.ReplayHotkey?.IsValid != true) settings.ReplayHotkey = new(6, 0x42);
            settings.ReplaySeconds = settings.ReplaySeconds is 15 or 30 or 60 ? settings.ReplaySeconds : 30;
            settings.ReplayMemoryMb = Math.Clamp(settings.ReplayMemoryMb, 64, 1024);
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            using var installed = Registry.CurrentUser.OpenSubKey(@"Software\ReplayKit");
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return new Settings { AutoStart = installed?.GetValue("Installed") == null || run?.GetValue("ReplayKit") != null };
        }
    }
    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, "settings.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("ReplayKit", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("ReplayKit", false);
    }
}

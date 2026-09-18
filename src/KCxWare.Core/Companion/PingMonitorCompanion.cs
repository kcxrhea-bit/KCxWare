using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KCxWare.Core.Companion;

public sealed class PingMonitorCompanion
{
    private const string ProcessName = "KCxPingMonitor";
    private readonly string _preferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCxWare", "settings", "companion.json");
    public string? ExecutablePath { get; private set; }
    public bool IsAvailable => (ExecutablePath = ResolveExecutable()) is not null;
    public bool IsRunning => Process.GetProcessesByName(ProcessName).Length > 0;
    public bool StartWithGamingMode { get; private set; }
    public bool StopWhenGamingModeEnds { get; private set; }
    public string Status => !IsAvailable ? "NOT FOUND" : IsRunning ? "RUNNING" : "STOPPED";

    public PingMonitorCompanion() => LoadPreferences();
    public void SetPreferences(bool startWithGamingMode, bool stopWhenGamingMode)
    {
        StartWithGamingMode = startWithGamingMode; StopWhenGamingModeEnds = stopWhenGamingMode;
        Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
        File.WriteAllText(_preferencesPath, JsonSerializer.Serialize(new Preferences(startWithGamingMode, stopWhenGamingMode)));
    }
    public bool Launch()
    {
        if (IsRunning) { BringForward(); return true; }
        var path = ResolveExecutable(); if (path is null) return false;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) }); return true;
    }
    public void OpenLogs()
    {
        var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCxPingMonitor", "logs");
        Directory.CreateDirectory(logs); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logs}\"") { UseShellExecute = true });
    }
    public bool StopIfOwnedByGamingMode() => StopWhenGamingModeEnds && Stop();
    public bool Stop()
    {
        var processes = Process.GetProcessesByName(ProcessName); if (processes.Length == 0) return false;
        foreach (var process in processes) { try { process.CloseMainWindow(); } catch { } }
        return true;
    }
    private string? ResolveExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tools", "KCxPingMonitor", "KCxPingMonitor.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCxWare", "Tools", "KCxPingMonitor", "KCxPingMonitor.exe"),
            Path.Combine(@"D:\KCxProjects\KCxPingMonitor\publish\win-x64", "KCxPingMonitor.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
    private void LoadPreferences()
    {
        try { if (File.Exists(_preferencesPath)) { var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_preferencesPath)); StartWithGamingMode = p?.StartWithGamingMode == true; StopWhenGamingModeEnds = p?.StopWhenGamingMode == true; } } catch { StartWithGamingMode = false; StopWhenGamingModeEnds = false; }
    }
    private void BringForward()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
            if (process.MainWindowHandle != IntPtr.Zero) { ShowWindow(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle); break; }
    }
    private sealed record Preferences(bool StartWithGamingMode, bool StopWhenGamingMode);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}

using System.IO;

namespace Grimly.Hosting;

/// <summary>
/// Tiny append-only logger that writes to the app's settings folder. Used to
/// trace the startup path when the ready notification doesn't appear — so we
/// can see whether the toast call was ever reached, and if so, what the
/// upstream Foundry checks returned.
///
/// Also writes to <see cref="System.Diagnostics.Debug"/> so DebugView picks
/// it up for live monitoring.
/// </summary>
internal static class StartupLog
{
    private static readonly object _lock = new();
    private static string? _logPath;

    public static void Initialize(string settingsFolderName)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                settingsFolderName);
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "startup.log");

            // Truncate on each launch so we always have a fresh trace.
            File.WriteAllText(_logPath,
                $"=== Startup {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch
        {
            _logPath = null;
        }
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        System.Diagnostics.Debug.WriteLine(line);

        if (_logPath == null) return;
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch { /* best-effort */ }
    }

    public static string? LogPath => _logPath;
}

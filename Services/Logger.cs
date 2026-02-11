using System.IO;

namespace MiniCalendar.Services;

public static class Logger
{
    private static readonly string LogPath;

    static Logger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "MiniCalendar");
        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }
        LogPath = Path.Combine(appFolder, "debug.log");
    }

    public static void Log(string message)
    {
        try
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, logEntry);
        }
        catch
        {
            // Ignore logging errors
        }
    }

    public static string GetLogPath() => LogPath;
}

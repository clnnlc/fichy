using System.IO;

namespace VolumeMixer.Services;

/// <summary>
/// Minimal file logger (only active when the VOLUMEMIXER_LOG environment
/// variable is set), used for diagnosing hotkey/audio issues in the field.
/// </summary>
public static class Logger
{
    private static readonly bool Enabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FICHY_LOG"));

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "fichy", "log.txt");

    private static readonly object Gate = new();

    public static void Log(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

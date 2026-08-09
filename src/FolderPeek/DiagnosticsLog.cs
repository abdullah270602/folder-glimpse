using System.Diagnostics;
using System.IO;

namespace FolderPeek;

internal static class DiagnosticsLog
{
    private static readonly object Gate = new();
    private static string? _path;

    internal static bool Enabled => _path is not null;
    internal static string? Path => _path;

    internal static void Initialize()
    {
        var configured = Environment.GetEnvironmentVariable("FOLDERPEEK_DIAGNOSTICS_PATH");
        _path = string.IsNullOrWhiteSpace(configured)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderPeek", "diagnostics.log")
            : System.IO.Path.GetFullPath(configured);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, $"FolderPeek diagnostics started {DateTimeOffset.Now:O}{Environment.NewLine}");
        }
        catch { _path = null; }
    }

    [Conditional("DEBUG")]
    internal static void Debug(string message) => Write(message);

    internal static void Write(string message)
    {
        var path = _path;
        if (path is null) return;
        try
        {
            lock (Gate) File.AppendAllText(path, $"{DateTimeOffset.Now:O} [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}

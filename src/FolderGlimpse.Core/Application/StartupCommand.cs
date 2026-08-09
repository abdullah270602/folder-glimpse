namespace FolderGlimpse.Core.Application;

public static class StartupCommand
{
    public static string Build(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\" --startup";

    public static bool IsCanonicalFor(string? command, string executablePath)
    {
        if (!TryParse(command, out var registeredPath, out var startup)) return false;
        return startup && PathsEqual(registeredPath, executablePath);
    }

    public static bool IsPathOnlyFor(string? command, string executablePath)
    {
        if (!TryParse(command, out var registeredPath, out var startup)) return false;
        return !startup && PathsEqual(registeredPath, executablePath);
    }

    public static bool TryParse(string? command, out string executablePath, out bool startup)
    {
        executablePath = string.Empty;
        startup = false;
        if (string.IsNullOrWhiteSpace(command)) return false;
        var value = command.Trim();
        string remainder;
        if (value[0] == '"')
        {
            var end = value.IndexOf('"', 1);
            if (end <= 1) return false;
            executablePath = value[1..end];
            remainder = value[(end + 1)..].Trim();
        }
        else
        {
            var split = value.IndexOf(' ');
            executablePath = split < 0 ? value : value[..split];
            remainder = split < 0 ? string.Empty : value[(split + 1)..].Trim();
        }
        startup = string.Equals(remainder, "--startup", StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(executablePath) && (remainder.Length == 0 || startup);
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

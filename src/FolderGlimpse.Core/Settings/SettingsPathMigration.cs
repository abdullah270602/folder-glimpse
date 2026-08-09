namespace FolderGlimpse.Core.Settings;

public static class SettingsPathMigration
{
    public static bool TryMigrate(string legacyPath, string currentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);

        var legacy = Path.GetFullPath(legacyPath);
        var current = Path.GetFullPath(currentPath);
        if (File.Exists(current) || !File.Exists(legacy)) return false;

        string? temporary = null;
        try
        {
            var directory = Path.GetDirectoryName(current)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".{Path.GetFileName(current)}.{Guid.NewGuid():N}.migration.tmp");
            File.Copy(legacy, temporary, overwrite: false);
            File.Move(temporary, current, overwrite: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch { } }
        }
    }
}

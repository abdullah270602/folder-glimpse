namespace FolderGlimpse.Core.Settings;

public interface IStartupValueStore
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}

public static class StartupRegistrationMigration
{
    public const string CurrentValueName = "FolderGlimpse";

    // Legacy FolderPeek value used only for one-time startup-registration migration.
    public const string LegacyValueName = "FolderPeek";

    public static bool TryMigrate(IStartupValueStore store, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        try
        {
            var legacyValue = store.Read(LegacyValueName);
            if (store.Read(CurrentValueName) is not null)
            {
                if (legacyValue is not null) store.Delete(LegacyValueName);
                return false;
            }

            if (legacyValue is null) return false;
            store.Write(CurrentValueName, $"\"{Path.GetFullPath(executablePath)}\"");
            store.Delete(LegacyValueName);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}

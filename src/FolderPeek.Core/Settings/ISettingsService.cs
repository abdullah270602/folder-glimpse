namespace FolderPeek.Core.Settings;

public sealed class SettingsChangedEventArgs(FolderPeekSettings previous, FolderPeekSettings current) : EventArgs
{
    public FolderPeekSettings Previous { get; } = previous;
    public FolderPeekSettings Current { get; } = current;
}

public interface ISettingsService
{
    FolderPeekSettings Current { get; }
    string? LastError { get; }
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    void Load();
    bool TryUpdate(Func<FolderPeekSettings, FolderPeekSettings> update, out string? error);
    bool TryResetDefaults(out string? error);
}

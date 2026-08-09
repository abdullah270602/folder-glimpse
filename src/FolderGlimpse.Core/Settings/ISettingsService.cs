namespace FolderGlimpse.Core.Settings;

public sealed class SettingsChangedEventArgs(FolderGlimpseSettings previous, FolderGlimpseSettings current) : EventArgs
{
    public FolderGlimpseSettings Previous { get; } = previous;
    public FolderGlimpseSettings Current { get; } = current;
}

public interface ISettingsService
{
    FolderGlimpseSettings Current { get; }
    string? LastError { get; }
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    void Load();
    bool TryUpdate(Func<FolderGlimpseSettings, FolderGlimpseSettings> update, out string? error);
    bool TryResetDefaults(out string? error);
}

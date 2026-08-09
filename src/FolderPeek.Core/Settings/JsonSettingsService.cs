using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderPeek.Core.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly string _path;
    private FolderPeekSettings _current = FolderPeekSettings.Default;

    public JsonSettingsService(string path) => _path = Path.GetFullPath(path);

    public FolderPeekSettings Current { get { lock (_gate) return _current; } }
    public string? LastError { get; private set; }
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public void Load()
    {
        FolderPeekSettings loaded;
        try
        {
            loaded = LoadDocument();
            LastError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            loaded = FolderPeekSettings.Default;
            LastError = "Settings were reset because the settings file could not be read.";
        }

        lock (_gate) _current = loaded.Normalize();
        // Missing, empty, malformed, and partial files are all healed with a complete file.
        TryWrite(Current, out _);
    }

    public bool TryUpdate(Func<FolderPeekSettings, FolderPeekSettings> update, out string? error)
    {
        ArgumentNullException.ThrowIfNull(update);
        FolderPeekSettings previous;
        FolderPeekSettings next;
        lock (_gate)
        {
            previous = _current;
            next = update(previous).Normalize();
            if (next == previous) { error = null; return true; }
            if (!TryWrite(next, out error)) return false;
            _current = next;
            LastError = null;
        }
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, next));
        return true;
    }

    public bool TryResetDefaults(out string? error) => TryUpdate(_ => FolderPeekSettings.Default, out error);

    private FolderPeekSettings LoadDocument()
    {
        if (!File.Exists(_path)) return FolderPeekSettings.Default;
        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json)) return FolderPeekSettings.Default;
        var document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions) ?? new SettingsDocument();
        var defaults = FolderPeekSettings.Default;
        return new FolderPeekSettings
        {
            Theme = document.Theme ?? defaults.Theme,
            PopupWidth = document.PopupWidth ?? defaults.PopupWidth,
            MaxPopupHeight = document.MaxPopupHeight ?? defaults.MaxPopupHeight,
            ShowFullPath = document.ShowFullPath ?? defaults.ShowFullPath,
            ShowFileSize = document.ShowFileSize ?? defaults.ShowFileSize,
            ShowModifiedDate = document.ShowModifiedDate ?? defaults.ShowModifiedDate,
            ShowHiddenFiles = document.ShowHiddenFiles ?? defaults.ShowHiddenFiles,
            SortMode = document.SortMode ?? defaults.SortMode,
            FoldersFirst = document.FoldersFirst ?? defaults.FoldersFirst,
            InitialItemLimit = document.InitialItemLimit ?? defaults.InitialItemLimit,
            Density = document.Density ?? defaults.Density,
            Hotkey = document.Hotkey ?? defaults.Hotkey,
            HoldThresholdMs = document.HoldThresholdMs ?? defaults.HoldThresholdMs,
            TapBehavior = document.TapBehavior ?? defaults.TapBehavior
        };
    }

    private bool TryWrite(FolderPeekSettings settings, out string? error)
    {
        string? temp = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temp, _path, true);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = error = "Unable to save FolderPeek settings.";
            return false;
        }
        finally
        {
            if (temp is not null) { try { File.Delete(temp); } catch { } }
        }
    }

    private sealed class SettingsDocument
    {
        public ThemePreference? Theme { get; set; }
        public double? PopupWidth { get; set; }
        public double? MaxPopupHeight { get; set; }
        public bool? ShowFullPath { get; set; }
        public bool? ShowFileSize { get; set; }
        public bool? ShowModifiedDate { get; set; }
        public bool? ShowHiddenFiles { get; set; }
        public SortMode? SortMode { get; set; }
        public bool? FoldersFirst { get; set; }
        public int? InitialItemLimit { get; set; }
        public DisplayDensity? Density { get; set; }
        public TriggerHotkey? Hotkey { get; set; }
        public int? HoldThresholdMs { get; set; }
        public TapBehavior? TapBehavior { get; set; }
    }
}

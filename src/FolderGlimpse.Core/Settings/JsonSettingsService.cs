using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderGlimpse.Core.Settings;

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
    private FolderGlimpseSettings _current = FolderGlimpseSettings.Default;

    public JsonSettingsService(string path) => _path = Path.GetFullPath(path);

    public FolderGlimpseSettings Current { get { lock (_gate) return _current; } }
    public string? LastError { get; private set; }
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public void Load()
    {
        FolderGlimpseSettings loaded;
        try
        {
            loaded = LoadDocument();
            LastError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            loaded = FolderGlimpseSettings.Default;
            LastError = "Settings were reset because the settings file could not be read.";
        }

        lock (_gate) _current = loaded.Normalize();
        // Missing, empty, malformed, and partial files are all healed with a complete file.
        TryWrite(Current, out _);
    }

    public bool TryUpdate(Func<FolderGlimpseSettings, FolderGlimpseSettings> update, out string? error)
    {
        ArgumentNullException.ThrowIfNull(update);
        FolderGlimpseSettings previous;
        FolderGlimpseSettings next;
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

    public bool TryResetDefaults(out string? error) => TryUpdate(_ => FolderGlimpseSettings.Default, out error);

    private FolderGlimpseSettings LoadDocument()
    {
        if (!File.Exists(_path)) return FolderGlimpseSettings.Default;
        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json)) return FolderGlimpseSettings.Default;
        var document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions) ?? new SettingsDocument();
        var defaults = FolderGlimpseSettings.Default;
        return new FolderGlimpseSettings
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
            TapBehavior = document.TapBehavior ?? defaults.TapBehavior,
            HoverMode = document.HoverMode ?? defaults.HoverMode,
            HoverOpenDelayMs = document.HoverOpenDelayMs ?? defaults.HoverOpenDelayMs,
            HoverCloseDelayMs = document.HoverCloseDelayMs ?? defaults.HoverCloseDelayMs,
            HoverMovementTolerancePx = document.HoverMovementTolerancePx ?? defaults.HoverMovementTolerancePx,
            HoverModifier = document.HoverModifier ?? defaults.HoverModifier,
            MouseTriggers = document.MouseTriggers ?? defaults.MouseTriggers,
            InteractiveItems = document.InteractiveItems ?? defaults.InteractiveItems,
            DoubleClickFilesToOpen = document.DoubleClickFilesToOpen ?? defaults.DoubleClickFilesToOpen,
            DoubleClickFoldersToOpen = document.DoubleClickFoldersToOpen ?? defaults.DoubleClickFoldersToOpen,
            RightClickActions = document.RightClickActions ?? defaults.RightClickActions,
            MultiSelection = document.MultiSelection ?? defaults.MultiSelection,
            ShowSelectionCheckboxes = document.ShowSelectionCheckboxes ?? defaults.ShowSelectionCheckboxes,
            AllowOpeningMultipleItems = document.AllowOpeningMultipleItems ?? defaults.AllowOpeningMultipleItems,
            ConfirmBeforeOpeningMoreThan = document.ConfirmBeforeOpeningMoreThan ?? defaults.ConfirmBeforeOpeningMoreThan,
            ClosePreviewAfterOpening = document.ClosePreviewAfterOpening ?? defaults.ClosePreviewAfterOpening
        };
    }

    private bool TryWrite(FolderGlimpseSettings settings, out string? error)
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
            LastError = error = "Unable to save FolderGlimpse settings.";
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
        public HoverPreviewMode? HoverMode { get; set; }
        public int? HoverOpenDelayMs { get; set; }
        public int? HoverCloseDelayMs { get; set; }
        public int? HoverMovementTolerancePx { get; set; }
        public HoverModifier? HoverModifier { get; set; }
        public MouseTriggerOptions? MouseTriggers { get; set; }
        public bool? InteractiveItems { get; set; }
        public bool? DoubleClickFilesToOpen { get; set; }
        public bool? DoubleClickFoldersToOpen { get; set; }
        public bool? RightClickActions { get; set; }
        public bool? MultiSelection { get; set; }
        public bool? ShowSelectionCheckboxes { get; set; }
        public bool? AllowOpeningMultipleItems { get; set; }
        public int? ConfirmBeforeOpeningMoreThan { get; set; }
        public bool? ClosePreviewAfterOpening { get; set; }
    }
}

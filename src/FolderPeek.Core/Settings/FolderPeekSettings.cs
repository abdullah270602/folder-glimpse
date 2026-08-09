using System.Text.Json.Serialization;

namespace FolderPeek.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemePreference { System, Light, Dark }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortMode { Name, ModifiedDate, Type }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayDensity { Compact, Comfortable }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerHotkey { Space, ControlSpace }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TapBehavior { TogglePreview, MomentaryOnly }

public sealed record FolderPeekSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public double PopupWidth { get; init; } = 430;
    public double MaxPopupHeight { get; init; } = 620;
    public bool ShowFullPath { get; init; } = true;
    public bool ShowFileSize { get; init; } = true;
    public bool ShowModifiedDate { get; init; }
    public bool ShowHiddenFiles { get; init; }
    public SortMode SortMode { get; init; } = SortMode.Name;
    public bool FoldersFirst { get; init; } = true;

    // 0 means All. Other accepted values are the choices exposed in Settings.
    public int InitialItemLimit { get; init; } = 50;
    public DisplayDensity Density { get; init; } = DisplayDensity.Comfortable;
    public TriggerHotkey Hotkey { get; init; } = TriggerHotkey.Space;
    public int HoldThresholdMs { get; init; } = 200;
    public TapBehavior TapBehavior { get; init; } = TapBehavior.TogglePreview;
    public bool InteractiveItems { get; init; } = true;
    public bool DoubleClickFilesToOpen { get; init; } = true;
    public bool DoubleClickFoldersToOpen { get; init; } = true;
    public bool RightClickActions { get; init; } = true;
    public bool MultiSelection { get; init; } = true;
    public bool ShowSelectionCheckboxes { get; init; }
    public bool AllowOpeningMultipleItems { get; init; } = true;
    public int ConfirmBeforeOpeningMoreThan { get; init; } = 5;
    public bool ClosePreviewAfterOpening { get; init; } = true;

    [JsonIgnore] public TimeSpan HoldThreshold => TimeSpan.FromMilliseconds(HoldThresholdMs);
    [JsonIgnore] public TimeSpan SnapshotMaxAge => TimeSpan.FromMilliseconds(350);
    [JsonIgnore] public int PreviewVisibleRows => 10;
    [JsonIgnore] public double PreviewRowHeightDip => Density == DisplayDensity.Compact ? 27 : 32;

    public static FolderPeekSettings Default => new();

    public FolderPeekSettings Normalize()
    {
        var validLimits = InitialItemLimit is 0 or 20 or 50 or 100 or 200;
        return this with
        {
            Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
            PopupWidth = Math.Clamp(double.IsFinite(PopupWidth) ? PopupWidth : 430, 300, 700),
            MaxPopupHeight = Math.Clamp(double.IsFinite(MaxPopupHeight) ? MaxPopupHeight : 620, 250, 900),
            SortMode = Enum.IsDefined(SortMode) ? SortMode : SortMode.Name,
            InitialItemLimit = validLimits ? InitialItemLimit : 50,
            Density = Enum.IsDefined(Density) ? Density : DisplayDensity.Comfortable,
            Hotkey = Enum.IsDefined(Hotkey) ? Hotkey : TriggerHotkey.Space,
            HoldThresholdMs = Math.Clamp(HoldThresholdMs, 100, 600),
            TapBehavior = Enum.IsDefined(TapBehavior) ? TapBehavior : TapBehavior.TogglePreview,
            ConfirmBeforeOpeningMoreThan = Math.Clamp(ConfirmBeforeOpeningMoreThan, 2, 50)
        };
    }
}

using System.Text.Json.Serialization;

namespace FolderGlimpse.Core.Settings;

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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HoverPreviewMode { Off, SelectedFolder, AnyFolder }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HoverModifier { None, Control, Shift }

public sealed record FolderGlimpseSettings
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
    public HoverPreviewMode HoverMode { get; init; } = HoverPreviewMode.AnyFolder;
    public int HoverOpenDelayMs { get; init; } = 650;
    public int HoverCloseDelayMs { get; init; } = 250;
    public int HoverMovementTolerancePx { get; init; } = 6;
    public HoverModifier HoverModifier { get; init; } = global::FolderGlimpse.Core.Settings.HoverModifier.None;
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
    // WinEvents invalidate selection/focus snapshots immediately; a slow fallback refresh
    // covers providers that omit events without forcing continuous cross-process polling.
    [JsonIgnore] public TimeSpan SnapshotMaxAge => TimeSpan.FromMilliseconds(3500);
    [JsonIgnore] public TimeSpan HoverOpenDelay => TimeSpan.FromMilliseconds(HoverOpenDelayMs);
    [JsonIgnore] public TimeSpan HoverCloseDelay => TimeSpan.FromMilliseconds(HoverCloseDelayMs);
    [JsonIgnore] public int PreviewVisibleRows => 10;
    [JsonIgnore] public double PreviewRowHeightDip => Density == DisplayDensity.Compact ? 27 : 32;

    public static FolderGlimpseSettings Default => new();

    public FolderGlimpseSettings Normalize()
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
            HoverMode = Enum.IsDefined(HoverMode) ? HoverMode : HoverPreviewMode.AnyFolder,
            HoverOpenDelayMs = Math.Clamp(HoverOpenDelayMs, 150, 2000),
            HoverCloseDelayMs = Math.Clamp(HoverCloseDelayMs, 100, 1000),
            HoverMovementTolerancePx = Math.Clamp(HoverMovementTolerancePx, 2, 16),
            HoverModifier = Enum.IsDefined(HoverModifier) ? HoverModifier : global::FolderGlimpse.Core.Settings.HoverModifier.None,
            ConfirmBeforeOpeningMoreThan = Math.Clamp(ConfirmBeforeOpeningMoreThan, 2, 50)
        };
    }
}

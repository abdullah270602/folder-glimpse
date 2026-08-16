using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Core.Application;

public static class DiagnosticReport
{
    public static string Create(FolderGlimpseSettings settings, string version, string operatingSystem,
        string processArchitecture, string framework)
    {
        var mouse = settings.MouseTriggers == MouseTriggerOptions.None ? "Off" : settings.MouseTriggers.ToString();
        return string.Join(Environment.NewLine,
            "FolderGlimpse diagnostics",
            $"Version: {version}",
            $"Windows: {operatingSystem}",
            $"Architecture: {processArchitecture}",
            $"Runtime: {framework}",
            $"Theme: {settings.Theme}",
            $"Hover: {settings.HoverMode}; modifier={settings.HoverModifier}; open={settings.HoverOpenDelayMs}ms; close={settings.HoverCloseDelayMs}ms",
            $"Keyboard: {settings.Hotkey}; tap={settings.TapBehavior}; hold={settings.HoldThresholdMs}ms",
            $"Mouse shortcuts: {mouse}",
            $"Preview: {settings.PopupWidth:0}x{settings.MaxPopupHeight:0}; density={settings.Density}; limit={(settings.InitialItemLimit == 0 ? "All" : settings.InitialItemLimit)}");
    }
}

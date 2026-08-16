using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Core.Input;

public static class HoverSamplingPolicy
{
    public static bool ShouldSample(bool enabled, HoverPreviewMode mode, nint foregroundWindow, nint explorerWindow) =>
        ShouldSample(enabled, mode, MouseTriggerOptions.None, foregroundWindow, explorerWindow);

    public static bool ShouldSample(bool enabled, HoverPreviewMode mode, MouseTriggerOptions mouseTriggers,
        nint foregroundWindow, nint explorerWindow) =>
        enabled && (mode != HoverPreviewMode.Off || mouseTriggers != MouseTriggerOptions.None) &&
        explorerWindow != 0 && foregroundWindow == explorerWindow;
}

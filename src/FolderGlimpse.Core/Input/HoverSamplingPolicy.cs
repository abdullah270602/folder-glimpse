using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Core.Input;

public static class HoverSamplingPolicy
{
    public static bool ShouldSample(bool enabled, HoverPreviewMode mode, nint foregroundWindow, nint explorerWindow) =>
        enabled && mode != HoverPreviewMode.Off && explorerWindow != 0 && foregroundWindow == explorerWindow;
}

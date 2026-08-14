namespace FolderGlimpse.Core.Settings;

public static class SettingsScrollPolicy
{
    public const double PixelsPerNotch = 56;

    public static double OffsetDelta(int wheelDelta) => -(wheelDelta / 120d) * PixelsPerNotch;
}

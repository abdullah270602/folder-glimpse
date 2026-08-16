namespace FolderGlimpse.Core.Settings;

public static class PopupCustomization
{
    public static bool ShouldLoadEntryIcons(FolderGlimpseSettings settings) => settings.ShowEntryIcons;

    public static FolderGlimpseSettings ApplyPreset(FolderGlimpseSettings settings, PopupLayoutPreset preset) => preset switch
    {
        PopupLayoutPreset.Minimal => settings with
        {
            HeaderStyle = PopupHeaderStyle.Hidden,
            FooterStyle = PopupFooterStyle.Hidden,
            ShowEntryIcons = false,
            Density = DisplayDensity.Compact,
            PreviewVisibleRows = 8,
            ShowFullPath = false,
            ShowFileSize = false,
            ShowModifiedDate = false
        },
        PopupLayoutPreset.Balanced => settings with
        {
            HeaderStyle = PopupHeaderStyle.Compact,
            FooterStyle = PopupFooterStyle.Smart,
            ShowEntryIcons = true,
            Density = DisplayDensity.Comfortable,
            PreviewVisibleRows = 10,
            ShowFullPath = false,
            ShowFileSize = true,
            ShowModifiedDate = false
        },
        PopupLayoutPreset.Detailed => settings with
        {
            HeaderStyle = PopupHeaderStyle.Full,
            FooterStyle = PopupFooterStyle.Always,
            ShowEntryIcons = true,
            Density = DisplayDensity.Comfortable,
            PreviewVisibleRows = 10,
            ShowFullPath = true,
            ShowFileSize = true,
            ShowModifiedDate = true
        },
        _ => settings
    };

    public static PopupLayoutPreset DetectPreset(FolderGlimpseSettings settings)
    {
        foreach (var preset in new[] { PopupLayoutPreset.Minimal, PopupLayoutPreset.Balanced, PopupLayoutPreset.Detailed })
        {
            var applied = ApplyPreset(settings, preset);
            if (SamePresetFields(settings, applied)) return preset;
        }
        return PopupLayoutPreset.Custom;
    }

    private static bool SamePresetFields(FolderGlimpseSettings left, FolderGlimpseSettings right) =>
        left.HeaderStyle == right.HeaderStyle && left.FooterStyle == right.FooterStyle &&
        left.ShowEntryIcons == right.ShowEntryIcons && left.Density == right.Density &&
        left.PreviewVisibleRows == right.PreviewVisibleRows && left.ShowFullPath == right.ShowFullPath &&
        left.ShowFileSize == right.ShowFileSize && left.ShowModifiedDate == right.ShowModifiedDate;
}

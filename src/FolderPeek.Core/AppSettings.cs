namespace FolderPeek.Core;

public sealed record AppSettings(
    TimeSpan HoldThreshold,
    TimeSpan SnapshotMaxAge,
    int MaxInitialItems,
    double PreviewWidthDip,
    double PreviewMaxHeightDip,
    int PreviewVisibleRows,
    double PreviewRowHeightDip,
    bool ShowFileSizes)
{
    public static AppSettings Default { get; } = new(
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(350),
        200,
        430,
        620,
        10,
        32,
        true);
}

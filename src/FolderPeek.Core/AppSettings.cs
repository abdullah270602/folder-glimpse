namespace FolderPeek.Core;

public sealed record AppSettings(
    TimeSpan HoldThreshold,
    TimeSpan SnapshotMaxAge,
    int MaxInitialItems,
    double PreviewWidthDip,
    double PreviewMaxHeightDip,
    bool ShowFileSizes)
{
    public static AppSettings Default { get; } = new(
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(350),
        200,
        430,
        620,
        true);
}

namespace FolderPeek.Core.FolderInspection;

public sealed record FolderEntry(string Name, string FullPath, bool IsDirectory, long? Size, DateTimeOffset ModifiedAt);

public sealed record FolderContents(
    IReadOnlyList<FolderEntry> Entries,
    bool HasMore,
    string? Error,
    TimeSpan Duration);

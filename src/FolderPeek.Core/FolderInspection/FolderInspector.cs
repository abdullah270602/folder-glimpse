using System.Diagnostics;
using FolderPeek.Core.Settings;

namespace FolderPeek.Core.FolderInspection;

public sealed record FolderInspectionOptions(
    bool ShowHiddenFiles = false,
    SortMode SortMode = SortMode.Name,
    bool FoldersFirst = true,
    int? ItemLimit = 50);

public interface IFolderInspector
{
    Task<FolderContents> InspectAsync(string path, FolderInspectionOptions options, CancellationToken cancellationToken);
}

public sealed class FolderInspector : IFolderInspector
{
    public Task<FolderContents> InspectAsync(string path, int limit, CancellationToken cancellationToken) =>
        InspectAsync(path, new FolderInspectionOptions(ItemLimit: limit), cancellationToken);

    public Task<FolderContents> InspectAsync(string path, FolderInspectionOptions options, CancellationToken cancellationToken) =>
        Task.Run(() => Inspect(path, options, cancellationToken), cancellationToken);

    private static FolderContents Inspect(string path, FolderInspectionOptions options, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var entries = new List<FolderEntry>();
        try
        {
            foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = item.Attributes;
                    if (!options.ShowHiddenFiles && (attributes & FileAttributes.Hidden) != 0) continue;
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    entries.Add(new FolderEntry(item.Name, item.FullName, isDirectory,
                        isDirectory ? null : ((FileInfo)item).Length, item.LastWriteTimeUtc));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // An unreadable entry should never prevent the rest of the folder from previewing.
                }
            }

            entries.Sort(CreateComparer(options));
            var limit = options.ItemLimit is > 0 ? options.ItemLimit.Value : int.MaxValue;
            var hasMore = entries.Count > limit;
            if (hasMore) entries.RemoveRange(limit, entries.Count - limit);
            return new FolderContents(entries, hasMore, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return new FolderContents([], false, "Unable to read this folder", stopwatch.Elapsed);
        }
    }

    private static IComparer<FolderEntry> CreateComparer(FolderInspectionOptions options) =>
        Comparer<FolderEntry>.Create((left, right) =>
        {
            if (options.FoldersFirst)
            {
                var folder = right.IsDirectory.CompareTo(left.IsDirectory);
                if (folder != 0) return folder;
            }
            var primary = options.SortMode switch
            {
                SortMode.ModifiedDate => right.ModifiedAt.CompareTo(left.ModifiedAt),
                SortMode.Type => StringComparer.OrdinalIgnoreCase.Compare(TypeKey(left), TypeKey(right)),
                _ => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name)
            };
            if (primary != 0) return primary;
            var name = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return name != 0 ? name : StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
        });

    private static string TypeKey(FolderEntry entry) => entry.IsDirectory ? "Folder" : Path.GetExtension(entry.Name);
}

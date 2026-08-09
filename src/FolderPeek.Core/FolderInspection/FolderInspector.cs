using System.Diagnostics;

namespace FolderPeek.Core.FolderInspection;

public interface IFolderInspector
{
    Task<FolderContents> InspectAsync(string path, int limit, CancellationToken cancellationToken);
}

public sealed class FolderInspector : IFolderInspector
{
    public Task<FolderContents> InspectAsync(string path, int limit, CancellationToken cancellationToken) =>
        Task.Run(() => Inspect(path, limit, cancellationToken), cancellationToken);

    private static FolderContents Inspect(string path, int limit, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var entries = new List<FolderEntry>(Math.Min(limit, 256));
        try
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }

            var directory = new DirectoryInfo(path);
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count == limit)
                {
                    return Complete(entries, true, null, stopwatch.Elapsed);
                }

                try
                {
                    var isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
                    entries.Add(new FolderEntry(
                        item.Name,
                        item.FullName,
                        isDirectory,
                        isDirectory ? null : ((FileInfo)item).Length,
                        item.LastWriteTimeUtc));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    entries.Add(new FolderEntry(item.Name, item.FullName, item is DirectoryInfo, null, item.LastWriteTimeUtc));
                }
            }

            return Complete(entries, false, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return Complete(entries, false, exception.Message, stopwatch.Elapsed);
        }
    }

    private static FolderContents Complete(List<FolderEntry> entries, bool hasMore, string? error, TimeSpan duration)
    {
        entries.Sort(static (left, right) =>
        {
            var directoryOrder = right.IsDirectory.CompareTo(left.IsDirectory);
            return directoryOrder != 0 ? directoryOrder : StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        });
        return new FolderContents(entries, hasMore, error, duration);
    }
}

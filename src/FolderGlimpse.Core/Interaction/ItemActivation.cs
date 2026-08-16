using FolderGlimpse.Core.FolderInspection;
using FolderGlimpse.Core.Settings;

namespace FolderGlimpse.Core.Interaction;

public interface IShellLauncher
{
    Task OpenFileAsync(string path, CancellationToken cancellationToken = default);
    Task OpenFolderAsync(string path, CancellationToken cancellationToken = default);
    Task OpenFileLocationAsync(string path, CancellationToken cancellationToken = default);
    Task ShowPropertiesAsync(string path, CancellationToken cancellationToken = default);
}

public interface IOpenManyConfirmation
{
    Task<bool> ConfirmAsync(int itemCount, CancellationToken cancellationToken = default);
}

public sealed record ActivationOptions(bool InteractiveItems = true, bool AllowOpeningMultipleItems = true,
    int ConfirmBeforeOpeningMoreThan = 5);

public sealed record ActivationResult(int RequestedCount, bool ConfirmationRequested, bool Cancelled, string? Error = null);

public sealed class ItemActivationService(IShellLauncher launcher, IOpenManyConfirmation confirmation)
{
    public async Task<ActivationResult> OpenAsync(IReadOnlyList<FolderEntry> entries, ActivationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.InteractiveItems || entries.Count == 0) return new(0, false, false);
        if (entries.Count > 1 && !options.AllowOpeningMultipleItems)
            return new(0, false, false, "Opening multiple items is disabled in Settings.");

        var threshold = Math.Clamp(options.ConfirmBeforeOpeningMoreThan, 2, 50);
        var shouldConfirm = entries.Count > threshold;
        if (shouldConfirm && !await confirmation.ConfirmAsync(entries.Count, cancellationToken).ConfigureAwait(false))
            return new(0, true, true);

        var requested = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (entry.IsDirectory) await launcher.OpenFolderAsync(entry.FullPath, cancellationToken).ConfigureAwait(false);
                else await launcher.OpenFileAsync(entry.FullPath, cancellationToken).ConfigureAwait(false);
                requested++;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { }
        }
        return new(requested, shouldConfirm, false);
    }
}

public enum ItemAction { Open, OpenFileLocation, CopyPath, CopyPaths, Properties }

public enum PreviewInteractionMode
{
    ViewOnly,
    HoverPointer,
    Sticky
}

public static class ItemActionPolicy
{
    public static bool CanHitTestEntries(PreviewInteractionMode mode, FolderGlimpseSettings settings) =>
        settings.InteractiveItems && mode is PreviewInteractionMode.HoverPointer or PreviewInteractionMode.Sticky;

    public static bool CanSelect(PreviewInteractionMode mode, FolderGlimpseSettings settings) =>
        settings.InteractiveItems && mode == PreviewInteractionMode.Sticky;

    public static bool CanPromoteHover(PreviewInteractionMode mode, FolderGlimpseSettings settings) =>
        settings.InteractiveItems && mode == PreviewInteractionMode.HoverPointer;

    public static bool CanDoubleClick(PreviewInteractionMode mode, FolderEntry entry, FolderGlimpseSettings settings) =>
        CanHitTestEntries(mode, settings) &&
        (entry.IsDirectory ? settings.DoubleClickFoldersToOpen : settings.DoubleClickFilesToOpen);

    public static IReadOnlyList<FolderEntry> ActivationTargetsForDoubleClick(
        PreviewInteractionMode mode, FolderEntry clickedEntry, FolderGlimpseSettings settings) =>
        CanDoubleClick(mode, clickedEntry, settings) ? [clickedEntry] : [];

    public static bool CanUseContextActions(PreviewInteractionMode mode, FolderGlimpseSettings settings) =>
        CanSelect(mode, settings) && settings.RightClickActions;

    public static IReadOnlyList<ItemAction> Available(IReadOnlyList<FolderEntry> selected, bool enabled)
    {
        if (!enabled || selected.Count == 0) return [];
        if (selected.Count > 1) return [ItemAction.Open, ItemAction.CopyPaths];
        return selected[0].IsDirectory
            ? [ItemAction.Open, ItemAction.CopyPath, ItemAction.Properties]
            : [ItemAction.Open, ItemAction.OpenFileLocation, ItemAction.CopyPath, ItemAction.Properties];
    }

    public static string PathsForClipboard(IEnumerable<FolderEntry> entries) =>
        string.Join(Environment.NewLine, entries.Select(entry => entry.FullPath));
}

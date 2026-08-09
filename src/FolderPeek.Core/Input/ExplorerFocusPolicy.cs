namespace FolderPeek.Core.Input;

public readonly record struct ExplorerFocusNode(
    int ProcessId,
    nint NativeWindow,
    bool IsEdit,
    bool IsItemsView,
    bool IsSelectedItem);

public readonly record struct ExplorerFocusAssessment(bool IsEligible, string Reason);

public static class ExplorerFocusPolicy
{
    public static ExplorerFocusAssessment Assess(
        IEnumerable<ExplorerFocusNode> ancestry,
        int explorerProcessId,
        nint explorerWindow)
    {
        var inItemsView = false;
        var hasSelectedItem = false;
        foreach (var node in ancestry)
        {
            if (node.ProcessId != explorerProcessId)
                return new(false, "Focus left Explorer before reaching its window");
            if (node.IsEdit)
                return new(false, "Text edit is focused");

            inItemsView |= node.IsItemsView;
            hasSelectedItem |= node.IsSelectedItem;
            if (node.NativeWindow == explorerWindow)
            {
                return inItemsView && hasSelectedItem
                    ? new(true, "Eligible")
                    : new(false, "Explorer file list is not focused");
            }
        }

        return new(false, "Focused element does not belong to the foreground Explorer window");
    }
}

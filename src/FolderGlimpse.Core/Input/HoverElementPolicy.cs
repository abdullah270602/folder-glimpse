namespace FolderGlimpse.Core.Input;

public readonly record struct HoverElementNode(
    int ProcessId,
    nint NativeWindow,
    bool IsRejectedSurface,
    bool IsItemsView,
    bool IsFolderItemCandidate);

public readonly record struct HoverElementAssessment(bool IsEligible, string Reason);

public static class HoverElementPolicy
{
    public static HoverElementAssessment Assess(IEnumerable<HoverElementNode> ancestry, int explorerProcessId,
        nint explorerWindow)
    {
        var inItemsView = false;
        var hasCandidate = false;
        foreach (var node in ancestry)
        {
            if (node.ProcessId != explorerProcessId)
                return new(false, "Pointer ancestry left Explorer");
            if (node.IsRejectedSurface)
                return new(false, "Pointer is over an excluded Explorer surface");
            inItemsView |= node.IsItemsView;
            hasCandidate |= node.IsFolderItemCandidate;
            if (node.NativeWindow == explorerWindow)
                return inItemsView && hasCandidate
                    ? new(true, "Eligible")
                    : new(false, "Pointer is not over a file-list item");
        }
        return new(false, "Pointer element does not belong to the foreground Explorer window");
    }
}

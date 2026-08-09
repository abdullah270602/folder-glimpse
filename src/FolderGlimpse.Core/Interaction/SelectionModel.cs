namespace FolderGlimpse.Core.Interaction;

public sealed class SelectionModel
{
    private readonly HashSet<int> _selected = [];

    public IReadOnlyCollection<int> SelectedIndices => _selected.Order().ToArray();
    public int SelectedCount => _selected.Count;
    public int? AnchorIndex { get; private set; }
    public int? FocusedIndex { get; private set; }
    public bool IsSelected(int index) => _selected.Contains(index);

    public void Clear()
    {
        _selected.Clear();
        AnchorIndex = null;
        FocusedIndex = null;
    }

    public void Select(int index, int itemCount, bool control = false, bool shift = false, bool multiSelection = true)
    {
        if (!IsValid(index, itemCount)) return;
        if (!multiSelection) { SelectOnly(index); return; }
        if (shift && AnchorIndex is int anchor)
        {
            _selected.Clear();
            var first = Math.Min(anchor, index);
            var last = Math.Max(anchor, index);
            for (var current = first; current <= last; current++) _selected.Add(current);
            FocusedIndex = index;
            return;
        }
        if (control)
        {
            if (!_selected.Add(index)) _selected.Remove(index);
            AnchorIndex = index;
            FocusedIndex = index;
            return;
        }
        SelectOnly(index);
    }

    public void Toggle(int index, int itemCount, bool multiSelection)
    {
        if (!IsValid(index, itemCount)) return;
        if (!multiSelection) { SelectOnly(index); return; }
        if (!_selected.Add(index)) _selected.Remove(index);
        AnchorIndex = index;
        FocusedIndex = index;
    }

    public void SelectAll(int itemCount, bool multiSelection)
    {
        if (itemCount <= 0) { Clear(); return; }
        if (!multiSelection) { SelectOnly(FocusedIndex is int focused && focused < itemCount ? focused : 0); return; }
        _selected.Clear();
        for (var index = 0; index < itemCount; index++) _selected.Add(index);
        AnchorIndex ??= 0;
        FocusedIndex ??= 0;
    }

    public int? Move(int delta, int itemCount)
    {
        if (itemCount <= 0) { Clear(); return null; }
        var current = FocusedIndex ?? (_selected.Count > 0 ? _selected.Min() : (delta >= 0 ? -1 : itemCount));
        SelectOnly(Math.Clamp(current + delta, 0, itemCount - 1));
        return FocusedIndex;
    }

    private void SelectOnly(int index)
    {
        _selected.Clear();
        _selected.Add(index);
        AnchorIndex = index;
        FocusedIndex = index;
    }

    private static bool IsValid(int index, int itemCount) => index >= 0 && index < itemCount;
}

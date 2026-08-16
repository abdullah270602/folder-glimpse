using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderGlimpse.Core;
using FolderGlimpse.Core.FolderInspection;
using FolderGlimpse.Core.Interaction;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Theming;

namespace FolderGlimpse.Preview;

public partial class PreviewWindow : Window
{
    private readonly SelectionModel _selection = new();
    private readonly IShellLauncher _launcher;
    private FolderGlimpseSettings _settings = FolderGlimpseSettings.Default;
    private nint _handle;
    private PreviewInteractionMode _interactionMode;
    private ContextMenu? _entryMenu;
    private PixelRect _lastAnchor;
    private bool _detached;

    internal PreviewViewModel ViewModel { get; } = new();
    internal event Func<IReadOnlyList<FolderEntry>, Task>? OpenRequested;
    internal event Action? CloseRequested;
    internal event Action? PromoteRequested;

    internal PreviewWindow(IShellLauncher launcher)
    {
        _launcher = launcher;
        InitializeComponent();
        DataContext = ViewModel;
        SourceInitialized += OnSourceInitialized;
        Closing += PreventClose;
    }

    internal bool OwnsForeground => IsActive || _entryMenu?.IsOpen == true;
    internal nint Handle => _handle;

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _handle = new WindowInteropHelper(this).Handle;
        SetInteractiveStyle(false);
        var preference = NativeMethods.DwmwcpRoundSmall;
        NativeMethods.DwmSetWindowAttribute(_handle, NativeMethods.DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    private void SetInteractiveStyle(bool interactive)
    {
        if (_handle == 0) return;
        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64() | NativeMethods.WsExToolWindow;
        style = interactive ? style & ~NativeMethods.WsExNoActivate : style | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new nint(style));
    }

    private void PreventClose(object? sender, CancelEventArgs args)
    {
        if (System.Windows.Application.Current?.Dispatcher.HasShutdownStarted == false) { args.Cancel = true; Hide(); }
    }

    internal void ApplyTheme(ThemeManager theme) { theme.Apply(this); Background = (System.Windows.Media.Brush)Resources["PanelBrush"]; }

    internal void ResetSelection()
    {
        if (_selection.SelectedCount == 0) { ViewModel.SelectedCount = 0; return; }
        _selection.Clear();
        RefreshSelection();
    }

    internal void ReplaceEntry(int index, PreviewEntryViewModel entry)
    {
        if (index < 0 || index >= ViewModel.Entries.Count) return;
        entry.IsSelected = _selection.IsSelected(index);
        ViewModel.Entries[index] = entry;
    }

    internal void SelectFirstForCapture(int count)
    {
        _selection.Clear();
        for (var index = 0; index < Math.Min(count, ViewModel.Entries.Count); index++)
            _selection.Select(index, ViewModel.Entries.Count, control: index > 0, multiSelection: true);
        RefreshSelection(0);
        Reposition();
        UpdateLayout();
    }

    internal void ConfigureInteraction(PreviewInteractionMode mode, FolderGlimpseSettings settings)
    {
        _settings = settings;
        ApplyPresentation(settings);
        _interactionMode = settings.InteractiveItems ? mode : PreviewInteractionMode.ViewOnly;
        var selectable = ItemActionPolicy.CanSelect(_interactionMode, settings);
        ViewModel.ShowCheckboxes = selectable && settings.MultiSelection && settings.ShowSelectionCheckboxes;
        EntryList.IsHitTestVisible = ItemActionPolicy.CanHitTestEntries(_interactionMode, settings);
        EntryList.Focusable = selectable;
        EntryList.Cursor = _interactionMode == PreviewInteractionMode.HoverPointer
            ? System.Windows.Input.Cursors.Hand
            : System.Windows.Input.Cursors.Arrow;
        EntryList.ToolTip = _interactionMode == PreviewInteractionMode.HoverPointer
            ? "Click an item to pin this glimpse. Double-click to open it."
            : null;
        var needsSelectionRefresh = _selection.SelectedCount > 0;
        if (!selectable) _selection.Clear();
        else if (!settings.MultiSelection && _selection.SelectedIndices.Count > 1)
            _selection.Select(_selection.FocusedIndex ?? _selection.SelectedIndices.Min(), ViewModel.Entries.Count, multiSelection: false);
        if (needsSelectionRefresh) RefreshSelection();
        else ViewModel.SelectedCount = 0;
        SetInteractiveStyle(selectable);
    }

    private void ApplyPresentation(FolderGlimpseSettings settings)
    {
        var showHeader = settings.HeaderStyle != PopupHeaderStyle.Hidden;
        HeaderPanel.Visibility = HeaderDivider.Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;
        HeaderPanel.Margin = settings.HeaderStyle == PopupHeaderStyle.Compact
            ? new Thickness(18, 11, 18, 10)
            : new Thickness(18, 15, 18, 13);
        ViewModel.ShowPath = settings.HeaderStyle == PopupHeaderStyle.Full && settings.ShowFullPath;
        FolderPathText.Visibility = ViewModel.PathVisibility;
        ViewModel.FooterStyle = settings.FooterStyle;
        ViewModel.ShowEntryIcons = settings.ShowEntryIcons;
    }

    internal bool TryPromoteEntry(int index)
    {
        if (index < 0 || index >= ViewModel.Entries.Count ||
            !ItemActionPolicy.CanPromoteHover(_interactionMode, _settings)) return false;

        PromoteRequested?.Invoke();
        if (ItemActionPolicy.CanSelect(_interactionMode, _settings))
        {
            _selection.Select(index, ViewModel.Entries.Count, multiSelection: _settings.MultiSelection);
            RefreshSelection(index);
        }
        return true;
    }

    internal void SetDetached(bool detached)
    {
        _detached = detached;
        if (_handle != 0 && IsVisible)
            NativeMethods.SetWindowPos(_handle, detached ? NativeMethods.HwndNotTopmost : NativeMethods.HwndTopmost,
                0, 0, 0, 0, NativeMethods.SwpNoActivate | NativeMethods.SwpNoSize | NativeMethods.SwpNoMove);
    }

    internal void CaptureTo(string path)
    {
        UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(stream);
    }

    internal void ShowBeside(PixelRect anchor, FolderGlimpseSettings settings,
        PreviewInteractionMode interactionMode = PreviewInteractionMode.ViewOnly)
    {
        _lastAnchor = anchor;
        SetDetached(false);
        ConfigureInteraction(interactionMode, settings);
        Width = settings.PopupWidth;
        MaxHeight = settings.MaxPopupHeight;
        EntryList.Tag = settings.PreviewRowHeightDip;
        EntryList.MaxHeight = settings.PreviewVisibleRows == 0
            ? double.PositiveInfinity
            : (settings.PreviewVisibleRows * settings.PreviewRowHeightDip) + EntryList.Padding.Top + EntryList.Padding.Bottom;
        SizeToContent = SizeToContent.Manual;
        Show();
        Reposition();
    }

    private void Reposition()
    {
        var desiredHeight = DesiredWindowHeight();
        var needsScroll = (_settings.PreviewVisibleRows > 0 && ViewModel.Entries.Count > _settings.PreviewVisibleRows) || desiredHeight > MaxHeight;
        ScrollViewer.SetVerticalScrollBarVisibility(EntryList, needsScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden);
        Height = Math.Min(Math.Max(desiredHeight, MinHeight), MaxHeight);
        UpdateLayout();
        var nativeAnchor = new NativeMethods.Rect { Left = _lastAnchor.Left, Top = _lastAnchor.Top, Right = _lastAnchor.Right, Bottom = _lastAnchor.Bottom };
        var monitor = NativeMethods.MonitorFromRect(ref nativeAnchor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var dpi = NativeMethods.GetDpiForWindow(_handle);
        if (dpi == 0) dpi = 96;
        var popupSize = new PixelSize((int)Math.Ceiling(Width * dpi / 96d), (int)Math.Ceiling(Height * dpi / 96d));
        var work = new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        var placed = PopupPositioner.Place(_lastAnchor, work, popupSize, _settings.PlacementPreference);
        var stickyInteractive = ItemActionPolicy.CanSelect(_interactionMode, _settings);
        var flags = NativeMethods.SwpShowWindow | (stickyInteractive ? 0u : NativeMethods.SwpNoActivate);
        NativeMethods.SetWindowPos(_handle, _detached ? NativeMethods.HwndNotTopmost : NativeMethods.HwndTopmost, placed.Left, placed.Top, placed.Width, placed.Height, flags);
        if (stickyInteractive)
        {
            ActivateForInteraction();
            EntryList.Focus();
        }
    }

    private void ActivateForInteraction()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(_handle);
            NativeMethods.SetFocus(_handle);
            Activate();
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private double DesiredWindowHeight()
    {
        var rowLimit = _settings.PreviewVisibleRows == 0 ? int.MaxValue : _settings.PreviewVisibleRows;
        var visibleRows = Math.Max(1, Math.Min(ViewModel.Entries.Count, rowLimit));
        var list = (visibleRows * _settings.PreviewRowHeightDip) + EntryList.Padding.Top + EntryList.Padding.Bottom;
        var actionBar = ViewModel.SelectedCount > 1 ? 48d : 0d;
        var header = _settings.HeaderStyle switch
        {
            PopupHeaderStyle.Hidden => 0d,
            PopupHeaderStyle.Compact => 47d,
            _ => 64d
        };
        var footer = ViewModel.FooterVisibility == Visibility.Visible ? 39d : 0d;
        return header + list + footer + actionBar + (actionBar > 0 ? 3d : 2d);
    }

    private void EntryListMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ItemActionPolicy.CanHitTestEntries(_interactionMode, _settings)) return;
        if (FindRow(e.OriginalSource as DependencyObject) is not { } row)
        {
            if (ItemActionPolicy.CanSelect(_interactionMode, _settings))
            {
                _selection.Clear();
                RefreshSelection();
            }
            return;
        }
        var index = EntryList.ItemContainerGenerator.IndexFromContainer(row);
        if (index < 0 || index >= ViewModel.Entries.Count) return;
        var entry = ViewModel.Entries[index].Entry;
        if (DiagnosticsLog.Enabled)
            DiagnosticsLog.Write($"preview pointer mode={_interactionMode} clicks={e.ClickCount} entry={entry.FullPath}");
        if (_interactionMode == PreviewInteractionMode.HoverPointer)
        {
            if (e.ClickCount == 1 && TryPromoteEntry(index))
            {
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 2 &&
                ItemActionPolicy.ActivationTargetsForDoubleClick(_interactionMode, entry, _settings) is { Count: > 0 } targets)
                _ = RequestOpenAsync(targets);
            e.Handled = true;
            return;
        }
        if (!ItemActionPolicy.CanSelect(_interactionMode, _settings) ||
            FindAncestor<System.Windows.Controls.CheckBox>(e.OriginalSource as DependencyObject) is not null) return;
        var modifiers = Keyboard.Modifiers;
        _selection.Select(index, ViewModel.Entries.Count, modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift), _settings.MultiSelection);
        RefreshSelection(index);
        if (e.ClickCount == 2 &&
            ItemActionPolicy.ActivationTargetsForDoubleClick(_interactionMode, entry, _settings).Count > 0)
            _ = RequestOpenAsync();
        e.Handled = true;
    }

    private void EntryListMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ItemActionPolicy.CanUseContextActions(_interactionMode, _settings) ||
            FindRow(e.OriginalSource as DependencyObject) is not { } row) return;
        var index = EntryList.ItemContainerGenerator.IndexFromContainer(row);
        if (!_selection.IsSelected(index)) _selection.Select(index, ViewModel.Entries.Count, multiSelection: _settings.MultiSelection);
        RefreshSelection(index);
        OpenContextMenu(row);
        e.Handled = true;
    }

    private void SelectionCheckboxClicked(object sender, RoutedEventArgs e)
    {
        if (!ItemActionPolicy.CanSelect(_interactionMode, _settings) || sender is not System.Windows.Controls.CheckBox check ||
            check.DataContext is not PreviewEntryViewModel item) return;
        var index = ViewModel.Entries.IndexOf(item);
        _selection.Toggle(index, ViewModel.Entries.Count, _settings.MultiSelection);
        RefreshSelection(index);
        e.Handled = true;
    }

    private void WindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!ItemActionPolicy.CanSelect(_interactionMode, _settings)) return;
        if (e.Key is Key.Up or Key.Down)
        {
            var index = _selection.Move(e.Key == Key.Down ? 1 : -1, ViewModel.Entries.Count);
            RefreshSelection(index);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { _ = RequestOpenAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _selection.SelectAll(ViewModel.Entries.Count, _settings.MultiSelection);
            RefreshSelection();
            e.Handled = true;
        }
    }

    private async void OpenSelectedClicked(object sender, RoutedEventArgs e) => await RequestOpenAsync();

    private Task RequestOpenAsync(IReadOnlyList<FolderEntry>? entries = null)
    {
        var selected = entries ?? SelectedEntries();
        return selected.Count == 0 || OpenRequested is null ? Task.CompletedTask : OpenRequested.Invoke(selected);
    }

    private void OpenContextMenu(ListBoxItem placementTarget)
    {
        var selected = SelectedEntries();
        var actions = ItemActionPolicy.Available(selected, _settings.RightClickActions);
        if (actions.Count == 0) return;
        _entryMenu = new ContextMenu { PlacementTarget = placementTarget, Style = (Style)FindResource("EntryContextMenuStyle") };
        _entryMenu.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            _entryMenu.IsOpen = false;
            CloseRequested?.Invoke();
            args.Handled = true;
        };
        foreach (var action in actions)
        {
            var menuItem = new MenuItem { Header = ActionLabel(action, selected.Count), Style = (Style)FindResource("EntryMenuItemStyle") };
            menuItem.Click += async (_, _) => await ExecuteActionAsync(action, selected);
            _entryMenu.Items.Add(menuItem);
        }
        _entryMenu.Closed += (_, _) => _entryMenu = null;
        _entryMenu.IsOpen = true;
    }

    private async Task ExecuteActionAsync(ItemAction action, IReadOnlyList<FolderEntry> selected)
    {
        ViewModel.ErrorMessage = string.Empty;
        try
        {
            switch (action)
            {
                case ItemAction.Open: await RequestOpenAsync(); break;
                case ItemAction.OpenFileLocation: await _launcher.OpenFileLocationAsync(selected[0].FullPath); break;
                case ItemAction.CopyPath:
                case ItemAction.CopyPaths: System.Windows.Clipboard.SetText(ItemActionPolicy.PathsForClipboard(selected)); break;
                case ItemAction.Properties: await _launcher.ShowPropertiesAsync(selected[0].FullPath); break;
            }
        }
        catch (Exception exception) { ViewModel.ErrorMessage = SafeError(exception); }
    }

    private IReadOnlyList<FolderEntry> SelectedEntries() => _selection.SelectedIndices
        .Where(index => index >= 0 && index < ViewModel.Entries.Count)
        .Select(index => ViewModel.Entries[index].Entry).ToArray();

    private void RefreshSelection(int? focusIndex = null)
    {
        for (var index = 0; index < ViewModel.Entries.Count; index++) ViewModel.Entries[index].IsSelected = _selection.IsSelected(index);
        ViewModel.SelectedCount = _selection.SelectedCount;
        if (IsVisible) Dispatcher.BeginInvoke(Reposition, System.Windows.Threading.DispatcherPriority.Loaded);
        if (focusIndex is int focus && EntryList.ItemContainerGenerator.ContainerFromIndex(focus) is ListBoxItem row)
        {
            row.Focus();
            row.BringIntoView();
        }
    }

    private ListBoxItem? FindRow(DependencyObject? source) => FindAncestor<ListBoxItem>(source);
    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static string ActionLabel(ItemAction action, int count) => action switch
    {
        ItemAction.Open => count > 1 ? "Open selected" : "Open",
        ItemAction.OpenFileLocation => "Open file location",
        ItemAction.CopyPath => "Copy path",
        ItemAction.CopyPaths => "Copy paths",
        ItemAction.Properties => "Properties",
        _ => action.ToString()
    };

    private static string SafeError(Exception exception) => exception is FileNotFoundException or DirectoryNotFoundException
        ? "This item is no longer available."
        : "Windows could not complete that action.";
}

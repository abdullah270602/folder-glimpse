using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using FolderGlimpse.Core;
using FolderGlimpse.Core.Input;

namespace FolderGlimpse.ExplorerIntegration;

internal sealed class ExplorerSnapshotMonitor : IDisposable
{
    private readonly Thread _thread;
    private readonly CancellationTokenSource _stop = new();
    private readonly NativeMethods.WinEventProc _winEventCallback;
    private readonly System.Threading.Timer _eventDebounce;
    private ExplorerSnapshot _current = ExplorerSnapshot.Ineligible("Starting", DateTimeOffset.UtcNow);
    private Task<FocusResult>? _focusTask;
    private long _generation;
    private nint _currentExplorerWindow;
    private int _currentExplorerProcessId;
    private string? _lastDiagnostic;
    private uint _threadId;
    private int _refreshPosted;
    private int _invalidated = 1;

    internal ExplorerSnapshot Current => Volatile.Read(ref _current);
    internal bool IsInvalidated => Volatile.Read(ref _invalidated) == 1;
    internal nint CurrentExplorerWindow => Volatile.Read(ref _currentExplorerWindow);
    internal int CurrentExplorerProcessId => Volatile.Read(ref _currentExplorerProcessId);

    internal ExplorerSnapshotMonitor()
    {
        _winEventCallback = OnWinEvent;
        _eventDebounce = new System.Threading.Timer(_ => PostRefresh(), null, Timeout.Infinite, Timeout.Infinite);
        _thread = new Thread(Run) { IsBackground = true, Name = "FolderGlimpse Explorer monitor" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    internal void Start() => _thread.Start();

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        var foregroundHook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground, 0, _winEventCallback, 0, 0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        var focusSelectionHook = NativeMethods.SetWinEventHook(NativeMethods.EventObjectFocus,
            NativeMethods.EventObjectSelectionWithin, 0, _winEventCallback, 0, 0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        var locationHook = NativeMethods.SetWinEventHook(NativeMethods.EventObjectLocationChange,
            NativeMethods.EventObjectLocationChange, 0, _winEventCallback, 0, 0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        using var fallback = new System.Threading.Timer(_ => RequestRefresh(false), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        try
        {
            PublishCapture();
            while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message != NativeMethods.WmAppRefreshExplorer) continue;
                Volatile.Write(ref _refreshPosted, 0);
                PublishCapture();
            }
        }
        finally
        {
            if (locationHook != 0) NativeMethods.UnhookWinEvent(locationHook);
            if (focusSelectionHook != 0) NativeMethods.UnhookWinEvent(focusSelectionHook);
            if (foregroundHook != 0) NativeMethods.UnhookWinEvent(foregroundHook);
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (eventType != NativeMethods.EventSystemForeground)
        {
            if (window == 0 || CurrentExplorerProcessId == 0) return;
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId != (uint)CurrentExplorerProcessId) return;
        }
        RequestRefresh(true);
    }

    private void RequestRefresh(bool debounce)
    {
        Volatile.Write(ref _invalidated, 1);
        if (debounce)
        {
            // Explorer emits focus, selection, and location notifications as a burst. Waiting
            // briefly avoids querying UIA while its provider is still rebuilding the row.
            _eventDebounce.Change(75, Timeout.Infinite);
            return;
        }
        PostRefresh();
    }

    private void PostRefresh()
    {
        var threadId = Volatile.Read(ref _threadId);
        if (threadId != 0 && Interlocked.Exchange(ref _refreshPosted, 1) == 0)
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WmAppRefreshExplorer, 0, 0);
    }

    private void PublishCapture()
    {
        ExplorerSnapshot snapshot;
        try { snapshot = Capture(); }
        catch (Exception exception)
        {
            Debug.WriteLine($"FolderGlimpse monitor recovered: {exception}");
            snapshot = ExplorerSnapshot.Ineligible("Integration worker error", DateTimeOffset.UtcNow,
                Interlocked.Increment(ref _generation));
        }
        Volatile.Write(ref _current, snapshot);
        Volatile.Write(ref _invalidated, 0);
        if (!DiagnosticsLog.Enabled) return;
        var diagnostic = $"snapshot eligible={snapshot.IsEligible} reason={snapshot.Reason} hwnd=0x{snapshot.ForegroundWindow:X} focus=0x{snapshot.FocusWindow:X} path={snapshot.FolderPath ?? "<none>"}";
        if (string.Equals(diagnostic, _lastDiagnostic, StringComparison.Ordinal)) return;
        _lastDiagnostic = diagnostic;
        DiagnosticsLog.Write(diagnostic);
    }

    private ExplorerSnapshot Capture()
    {
        var now = DateTimeOffset.UtcNow;
        var generation = Interlocked.Increment(ref _generation);
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0)
        {
            return ExplorerSnapshot.Ineligible("No foreground window", now, generation);
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _currentExplorerWindow, 0);
                Volatile.Write(ref _currentExplorerProcessId, 0);
                return ExplorerSnapshot.Ineligible("Foreground is not Explorer", now, generation);
            }
            Volatile.Write(ref _currentExplorerWindow, foreground);
            Volatile.Write(ref _currentExplorerProcessId, (int)pid);

            if (_focusTask is { IsCompleted: false })
                return ExplorerSnapshot.Ineligible("UI Automation worker is still busy", now, generation);
            var focusTask = Task.Run(() => InspectFocus(foreground, (int)pid));
            _focusTask = focusTask;
            if (!focusTask.Wait(65))
            {
                return ExplorerSnapshot.Ineligible("UI Automation timed out", now, generation);
            }
            var focus = focusTask.GetAwaiter().GetResult();
            _focusTask = null;
            if (!focus.Eligible) return ExplorerSnapshot.Ineligible(focus.Reason, now, generation);

            var selection = ReadShellSelection(foreground, focus.SelectedName!);
            if (selection is null)
            {
                return ExplorerSnapshot.Ineligible("Explorer does not have one matching local folder selected", now, generation);
            }

            if (NativeMethods.GetForegroundWindow() != foreground)
            {
                return ExplorerSnapshot.Ineligible("Foreground changed during capture", now, generation);
            }

            var gui = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
            if (!NativeMethods.GetGUIThreadInfo(0, ref gui) || gui.Focus == 0)
                return ExplorerSnapshot.Ineligible("Unable to capture focus window", now, generation);
            return new ExplorerSnapshot(true, "Eligible", foreground, gui.Focus, (int)pid, selection.Value.Path,
                selection.Value.DisplayName, focus.Bounds, DateTimeOffset.UtcNow, generation);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"FolderGlimpse snapshot: {exception.Message}");
            return ExplorerSnapshot.Ineligible(exception.GetType().Name, now, generation);
        }
    }

    private static (string Path, string DisplayName)? ReadShellSelection(nint foreground, string expectedName)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null || (shell = Activator.CreateInstance(type)) is null)
            {
                return null;
            }

            windows = shell.GetType().InvokeMember("Windows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            if (windows is null) return null;
            var count = Convert.ToInt32(windows.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, windows, null));
            (string Path, string DisplayName)? match = null;
            for (var index = 0; index < count; index++)
            {
                object? browser = null;
                object? document = null;
                object? selectedItems = null;
                object? item = null;
                try
                {
                    browser = windows.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, windows, new object[] { index });
                    if (browser is null) continue;
                    var hwnd = Convert.ToInt64(browser.GetType().InvokeMember("HWND", System.Reflection.BindingFlags.GetProperty, null, browser, null));
                    if (new nint(hwnd) != foreground) continue;
                    document = browser.GetType().InvokeMember("Document", System.Reflection.BindingFlags.GetProperty, null, browser, null);
                    if (document is null) continue;
                    selectedItems = document.GetType().InvokeMember("SelectedItems", System.Reflection.BindingFlags.InvokeMethod, null, document, null);
                    if (selectedItems is null) continue;
                    var selectedCount = Convert.ToInt32(selectedItems.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, selectedItems, null));
                    if (selectedCount != 1) continue;
                    item = selectedItems.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, selectedItems, new object[] { 0 });
                    if (item is null) continue;
                    var isFolder = Convert.ToBoolean(item.GetType().InvokeMember("IsFolder", System.Reflection.BindingFlags.GetProperty, null, item, null));
                    var path = Convert.ToString(item.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, item, null));
                    if (!isFolder || string.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || !Path.IsPathRooted(path) || !Directory.Exists(path)) continue;
                    var candidate = (path!, Path.GetFileName(path!.TrimEnd(Path.DirectorySeparatorChar)));
                    if (!string.Equals(candidate.Item2, expectedName, StringComparison.CurrentCultureIgnoreCase)) continue;
                    if (match is not null) return null;
                    match = candidate;
                }
                finally
                {
                    Release(item); Release(selectedItems); Release(document); Release(browser);
                }
            }
            return match;
        }
        finally
        {
            Release(windows); Release(shell);
        }
    }

    private static FocusResult InspectFocus(nint foreground, int pid)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return new(false, "No focused automation element", null, null);
            AutomationElement? selectedItem = null;
            var ancestry = new List<ExplorerFocusNode>();
            var walker = TreeWalker.ControlViewWalker;
            for (var element = focused; element is not null; element = walker.GetParent(element))
            {
                var current = element.Current;
                var isSelectedItem = element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) &&
                    pattern is SelectionItemPattern selection && selection.Current.IsSelected;
                if (selectedItem is null && isSelectedItem) selectedItem = element;
                var nativeWindow = new nint(current.NativeWindowHandle);
                ancestry.Add(new ExplorerFocusNode(
                    current.ProcessId,
                    nativeWindow,
                    current.ControlType == ControlType.Edit,
                    string.Equals(current.AutomationId, "ItemsView", StringComparison.OrdinalIgnoreCase) ||
                    current.ClassName.Contains("UIItemsView", StringComparison.OrdinalIgnoreCase),
                    isSelectedItem));

                // The parent of CabinetWClass is the desktop automation root, which is
                // hosted by a different Explorer process. It is outside this focus proof.
                if (nativeWindow == foreground) break;
            }

            var assessment = ExplorerFocusPolicy.Assess(ancestry, pid, foreground);
            if (!assessment.IsEligible || selectedItem is null) return new(false, assessment.Reason, null, null);
            var item = selectedItem.Current;
            var rect = item.BoundingRectangle;
            PixelRect? bounds = !item.IsOffscreen && rect.Width > 0 && rect.Height > 0 &&
                double.IsFinite(rect.X) && double.IsFinite(rect.Y)
                ? new PixelRect((int)Math.Round(rect.Left), (int)Math.Round(rect.Top), (int)Math.Round(rect.Right), (int)Math.Round(rect.Bottom))
                : null;
            return new(true, "Eligible", item.Name, bounds);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return new(false, exception.GetType().Name, null, null);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _eventDebounce.Dispose();
        if (_threadId != 0) NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmQuit, 0, 0);
        if (_thread.IsAlive) _thread.Join(1000);
        _stop.Dispose();
    }

    private readonly record struct FocusResult(bool Eligible, string Reason, string? SelectedName, PixelRect? Bounds);
}

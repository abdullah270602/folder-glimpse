using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using FolderGlimpse.Core;
using FolderGlimpse.Core.Input;

namespace FolderGlimpse.ExplorerIntegration;

internal sealed class HoverTargetResolver : IDisposable
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _ready = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private Request? _pending;
    private Task<HoverItem?>? _pointTask;

    internal HoverTargetResolver()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "FolderGlimpse hover resolver" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    internal Task<ExplorerSnapshot?> ResolveAsync(nint foreground, int explorerPid, HoverPoint point, long generation)
    {
        var completion = new TaskCompletionSource<ExplorerSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending?.Completion.TrySetResult(null);
            _pending = new(foreground, explorerPid, point, generation, completion);
        }
        _ready.Set();
        return completion.Task;
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            _ready.WaitOne();
            if (_stop.IsCancellationRequested) break;
            Request? request;
            lock (_gate) { request = _pending; _pending = null; }
            if (request is null) continue;
            try { request.Completion.TrySetResult(Resolve(request)); }
            catch (Exception exception)
            {
                Debug.WriteLine($"FolderGlimpse hover resolver recovered: {exception.Message}");
                request.Completion.TrySetResult(null);
            }
        }
    }

    private ExplorerSnapshot? Resolve(Request request)
    {
        if (NativeMethods.GetForegroundWindow() != request.Foreground) return null;
        if (_pointTask is { IsCompleted: false }) return null;
        _pointTask = Task.Run(() => InspectPoint(request));
        if (!_pointTask.Wait(90)) return null;
        var item = _pointTask.GetAwaiter().GetResult();
        _pointTask = null;
        if (item is null || NativeMethods.GetForegroundWindow() != request.Foreground) return null;
        var shell = ResolveShellFolder(request.Foreground, item.Value.Name);
        if (shell is null || NativeMethods.GetForegroundWindow() != request.Foreground) return null;

        var gui = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        if (!NativeMethods.GetGUIThreadInfo(0, ref gui) || gui.Focus == 0) return null;
        return new ExplorerSnapshot(true, "Hover eligible", request.Foreground, gui.Focus, request.ExplorerPid,
            shell.Value.Path, shell.Value.DisplayName, item.Value.Bounds, DateTimeOffset.UtcNow, request.Generation);
    }

    private static HoverItem? InspectPoint(Request request)
    {
        try
        {
            // A normal Details/Large Icons name cell advertises a writable ValuePattern even
            // before rename mode starts.  Looking at that cell alone therefore cannot tell us
            // whether Explorer is actually editing text.  Check the focused element instead:
            // search, address-bar and active rename editors are writable *and focused*.
            if (HasActiveEditorFocus(request)) return null;

            var element = AutomationElement.FromPoint(new System.Windows.Point(request.Point.X, request.Point.Y));
            if (element is null) return null;
            AutomationElement? item = null;
            var ancestry = new List<HoverElementNode>();
            var walker = TreeWalker.ControlViewWalker;
            for (var current = element; current is not null; current = walker.GetParent(current))
            {
                var properties = current.Current;
                var rejected = properties.ControlType is { } type &&
                    (type == ControlType.Menu || type == ControlType.MenuItem || type == ControlType.Tree);
                var candidate = current.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _);
                if (item is null && candidate) item = current;
                var itemsView = string.Equals(properties.AutomationId, "ItemsView", StringComparison.OrdinalIgnoreCase) ||
                    properties.ClassName.Contains("UIItemsView", StringComparison.OrdinalIgnoreCase);
                ancestry.Add(new(properties.ProcessId, new nint(properties.NativeWindowHandle), rejected, itemsView, candidate));
                if (new nint(properties.NativeWindowHandle) == request.Foreground) break;
            }
            if (!HoverElementPolicy.Assess(ancestry, request.ExplorerPid, request.Foreground).IsEligible || item is null) return null;
            var itemProperties = item.Current;
            if (itemProperties.IsOffscreen || string.IsNullOrWhiteSpace(itemProperties.Name)) return null;
            var rect = itemProperties.BoundingRectangle;
            if (rect.Width <= 0 || rect.Height <= 0 || !double.IsFinite(rect.X) || !double.IsFinite(rect.Y)) return null;
            var bounds = new PixelRect((int)Math.Round(rect.Left), (int)Math.Round(rect.Top),
                (int)Math.Round(rect.Right), (int)Math.Round(rect.Bottom));
            if (request.Point.X < bounds.Left || request.Point.X >= bounds.Right ||
                request.Point.Y < bounds.Top || request.Point.Y >= bounds.Bottom) return null;
            return new(itemProperties.Name, bounds);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static bool HasActiveEditorFocus(Request request)
    {
        var focused = AutomationElement.FocusedElement;
        if (focused is null) return true;

        var walker = TreeWalker.ControlViewWalker;
        var reachedExplorerWindow = false;
        for (var current = focused; current is not null; current = walker.GetParent(current))
        {
            var properties = current.Current;
            if (properties.ProcessId != request.ExplorerPid) return true;

            if (properties.ControlType == ControlType.Edit && properties.HasKeyboardFocus &&
                current.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern) &&
                valuePattern is ValuePattern value && !value.Current.IsReadOnly)
                return true;

            if (properties.ControlType == ControlType.Menu || properties.ControlType == ControlType.MenuItem)
                return true;

            if (new nint(properties.NativeWindowHandle) == request.Foreground)
            {
                reachedExplorerWindow = true;
                break;
            }
        }

        return !reachedExplorerWindow;
    }

    private static (string Path, string DisplayName)? ResolveShellFolder(nint foreground, string displayName)
    {
        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null || (shell = Activator.CreateInstance(shellType)) is null) return null;
            windows = shell.GetType().InvokeMember("Windows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            if (windows is null) return null;
            var count = Convert.ToInt32(windows.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, windows, null));
            (string Path, string DisplayName)? match = null;
            for (var index = 0; index < count; index++)
            {
                object? browser = null; object? document = null; object? folder = null; object? item = null;
                try
                {
                    browser = windows.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, windows, [index]);
                    if (browser is null) continue;
                    var hwnd = Convert.ToInt64(browser.GetType().InvokeMember("HWND", System.Reflection.BindingFlags.GetProperty, null, browser, null));
                    if (new nint(hwnd) != foreground) continue;
                    document = browser.GetType().InvokeMember("Document", System.Reflection.BindingFlags.GetProperty, null, browser, null);
                    folder = document?.GetType().InvokeMember("Folder", System.Reflection.BindingFlags.GetProperty, null, document, null);
                    item = folder?.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod, null, folder, [displayName]);
                    if (item is null) continue;
                    var isFolder = Convert.ToBoolean(item.GetType().InvokeMember("IsFolder", System.Reflection.BindingFlags.GetProperty, null, item, null));
                    var path = Convert.ToString(item.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, item, null));
                    var name = Convert.ToString(item.GetType().InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, item, null));
                    if (!isFolder || string.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
                        !Path.IsPathRooted(path) || !Directory.Exists(path)) continue;
                    var candidate = (path!, string.IsNullOrWhiteSpace(name) ? displayName : name!);
                    if (match is not null && !string.Equals(match.Value.Path, candidate.Item1, StringComparison.OrdinalIgnoreCase)) return null;
                    match = candidate;
                }
                catch (Exception exception) when (exception is COMException or InvalidOperationException) { }
                finally { Release(item); Release(folder); Release(document); Release(browser); }
            }
            return match;
        }
        finally { Release(windows); Release(shell); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate) { _pending?.Completion.TrySetResult(null); _pending = null; }
        _ready.Set();
        if (_thread.IsAlive) _thread.Join(500);
        _ready.Dispose(); _stop.Dispose();
    }

    private sealed record Request(nint Foreground, int ExplorerPid, HoverPoint Point, long Generation,
        TaskCompletionSource<ExplorerSnapshot?> Completion);
    private readonly record struct HoverItem(string Name, PixelRect Bounds);
}

using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FolderPeek.Core;
using FolderPeek.Core.FolderInspection;
using FolderPeek.Core.Input;
using FolderPeek.ExplorerIntegration;
using FolderPeek.Input;
using FolderPeek.Preview;
using Forms = System.Windows.Forms;

namespace FolderPeek;

public partial class App : System.Windows.Application
{
    private readonly AppSettings _settings = AppSettings.Default;
    private readonly PeekStateMachine _state = new();
    private readonly IFolderInspector _inspector = new FolderInspector();
    private ExplorerSnapshotMonitor? _monitor;
    private KeyboardHook? _hook;
    private PreviewWindow? _preview;
    private Forms.NotifyIcon? _tray;
    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _contextTimer;
    private CancellationTokenSource? _previewLoad;
    private ExplorerSnapshot? _gestureSnapshot;
    private Mutex? _singleInstance;
    private volatile bool _enabled = true;
    private long _previewGeneration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var diagnostics = e.Args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase);
        var allowInjectedInput = e.Args.Contains("--allow-injected-input", StringComparer.OrdinalIgnoreCase);
        if (diagnostics) DiagnosticsLog.Initialize();
        _singleInstance = new Mutex(true, "Local\\FolderPeek.SingleInstance", out var created);
        if (!created) { Shutdown(); return; }

        _preview = new PreviewWindow();
        _monitor = new ExplorerSnapshotMonitor();
        _monitor.Start();
        _hook = new KeyboardHook(CanOwnSpace, allowInjectedInput);
        _hook.Gesture += OnHookGesture;
        _hook.Start();

        _holdTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = _settings.HoldThreshold };
        _holdTimer.Tick += (_, _) => { _holdTimer.Stop(); Apply(_state.HoldThresholdElapsed()); };
        _contextTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _contextTimer.Tick += (_, _) => ValidateOpenContext();
        _contextTimer.Start();
        CreateTrayIcon();
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"startup diagnostics={diagnostics} allowInjectedInput={allowInjectedInput}");
    }

    private bool CanOwnSpace()
    {
        var modifiers = IsDown(NativeMethods.VkShift) || IsDown(NativeMethods.VkControl) || IsDown(NativeMethods.VkMenu) ||
                        IsDown(NativeMethods.VkLWin) || IsDown(NativeMethods.VkRWin);
        var snapshot = _monitor?.Current;
        var gui = new NativeMethods.GuiThreadInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        var focus = NativeMethods.GetGUIThreadInfo(0, ref gui) ? gui.Focus : 0;
        var canOwn = EligibilityPolicy.CanOwnSpace(
            new InputContext(_enabled, false, modifiers, NativeMethods.GetForegroundWindow(), focus, DateTimeOffset.UtcNow),
            snapshot, _settings.SnapshotMaxAge);
        if (DiagnosticsLog.Enabled)
            DiagnosticsLog.Write($"eligibility decision={canOwn} enabled={_enabled} modifiers={modifiers} foreground=0x{NativeMethods.GetForegroundWindow():X} focus=0x{focus:X} snapshotReason={snapshot?.Reason ?? "<null>"} ageMs={(snapshot is null ? -1 : (DateTimeOffset.UtcNow - snapshot.CapturedAt).TotalMilliseconds):F0}");
        return canOwn;
    }

    private static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    private void OnHookGesture(HookGesture gesture) => Dispatcher.BeginInvoke(() => HandleGesture(gesture), DispatcherPriority.Input);

    private void HandleGesture(HookGesture gesture)
    {
        switch (gesture)
        {
            case HookGesture.SpaceDown:
                _gestureSnapshot = _monitor?.Current;
                var down = _state.SpaceDown(_gestureSnapshot?.IsEligible == true);
                if (down.State == PeekState.Pending) _holdTimer?.Start();
                Apply(down);
                break;
            case HookGesture.SpaceUp:
                _holdTimer?.Stop();
                Apply(_state.SpaceUp());
                break;
            case HookGesture.Escape:
                Apply(_state.Escape(CanOwnSpace()));
                break;
            case HookGesture.LostRelease:
                _holdTimer?.Stop();
                Apply(_state.Reset());
                break;
        }
    }

    private void Apply(StateTransition transition)
    {
        if (_hook is not null) _hook.CanConsumeEscape = transition.State == PeekState.StickyOpen;
        Debug.WriteLine($"FolderPeek state={transition.State}, action={transition.Action}");
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"state={transition.State} action={transition.Action}");
        switch (transition.Action)
        {
            case PeekAction.OpenSticky:
            case PeekAction.OpenMomentary:
                if (_gestureSnapshot is { IsEligible: true } snapshot) _ = OpenPreviewAsync(snapshot);
                break;
            case PeekAction.Close:
                ClosePreview();
                break;
        }
    }

    private async Task OpenPreviewAsync(ExplorerSnapshot snapshot)
    {
        if (_preview is null || snapshot.FolderPath is null) return;
        var generation = Interlocked.Increment(ref _previewGeneration);
        _previewLoad?.Cancel();
        _previewLoad?.Dispose();
        _previewLoad = new CancellationTokenSource();
        var token = _previewLoad.Token;
        var viewModel = _preview.ViewModel;
        viewModel.FolderName = snapshot.DisplayName ?? Path.GetFileName(snapshot.FolderPath);
        viewModel.FolderPath = snapshot.FolderPath;
        viewModel.Entries.Clear();
        viewModel.EntriesChanged();
        viewModel.Loading = true;
        viewModel.Status = "Loading folder…";
        _preview.ShowBeside(snapshot.ItemBounds ?? CursorAnchor(), _settings.PreviewWidthDip, _settings.PreviewMaxHeightDip,
            _settings.PreviewVisibleRows, _settings.PreviewRowHeightDip);

        FolderContents contents;
        try { contents = await _inspector.InspectAsync(snapshot.FolderPath, _settings.MaxInitialItems, token); }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested || generation != Volatile.Read(ref _previewGeneration) || !_preview.IsVisible) return;

        foreach (var entry in contents.Entries)
        {
            var detail = entry.IsDirectory || !_settings.ShowFileSizes ? string.Empty : FormatSize(entry.Size);
            viewModel.Entries.Add(new PreviewEntryViewModel(entry.Name, detail, null));
        }
        viewModel.Loading = false;
        viewModel.EntriesChanged();
        viewModel.Status = contents.Error is not null ? contents.Error :
            contents.HasMore ? $"{contents.Entries.Count}+ items · showing first {_settings.MaxInitialItems}" :
            $"{contents.Entries.Count} {(contents.Entries.Count == 1 ? "item" : "items")}";
        _preview.ShowBeside(snapshot.ItemBounds ?? CursorAnchor(), _settings.PreviewWidthDip, _settings.PreviewMaxHeightDip,
            _settings.PreviewVisibleRows, _settings.PreviewRowHeightDip);
        _ = LoadIconsAsync(contents, generation, token);
    }

    private async Task LoadIconsAsync(FolderContents contents, long generation, CancellationToken token)
    {
        if (_preview is null) return;
        try
        {
            for (var index = 0; index < contents.Entries.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var entry = contents.Entries[index];
                var icon = await Task.Run(() => ShellIconProvider.GetSmallIcon(entry.FullPath), token);
                if (token.IsCancellationRequested || generation != Volatile.Read(ref _previewGeneration) || !_preview.IsVisible) return;
                var detail = entry.IsDirectory || !_settings.ShowFileSizes ? string.Empty : FormatSize(entry.Size);
                _preview.ViewModel.Entries[index] = new PreviewEntryViewModel(entry.Name, detail, icon);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine($"FolderPeek icon load: {exception.Message}"); }
    }

    private static string FormatSize(long? value)
    {
        if (value is null) return string.Empty;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value.Value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }

    private static PixelRect CursorAnchor()
    {
        NativeMethods.GetCursorPos(out var point);
        return new PixelRect(point.X - 1, point.Y - 1, point.X + 1, point.Y + 1);
    }

    private void ValidateOpenContext()
    {
        if (_state.State == PeekState.Idle || _gestureSnapshot is null) return;
        var current = _monitor?.Current;
        if (current?.IsEligible == true && current.ForegroundWindow == _gestureSnapshot.ForegroundWindow &&
            string.Equals(current.FolderPath, _gestureSnapshot.FolderPath, StringComparison.OrdinalIgnoreCase)) return;
        _holdTimer?.Stop();
        Apply(_state.ContextInvalidated());
    }

    private void ClosePreview()
    {
        Interlocked.Increment(ref _previewGeneration);
        _previewLoad?.Cancel();
        _preview?.Hide();
    }

    private void CreateTrayIcon()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = "FolderPeek",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        var enabled = new Forms.ToolStripMenuItem("Enabled") { Checked = true, CheckOnClick = true };
        enabled.CheckedChanged += (_, _) =>
        {
            _enabled = enabled.Checked;
            if (!_enabled) { _holdTimer?.Stop(); Apply(_state.Reset()); }
        };
        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Shutdown();
        _tray.ContextMenuStrip.Items.Add(enabled);
        _tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add(exit);
        _tray.DoubleClick += (_, _) => { enabled.Checked = !enabled.Checked; };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _previewLoad?.Cancel();
        _contextTimer?.Stop();
        _holdTimer?.Stop();
        if (_hook is not null) { _hook.Gesture -= OnHookGesture; _hook.Dispose(); }
        _monitor?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _preview?.Close();
        _previewLoad?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

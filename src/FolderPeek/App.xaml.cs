using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FolderPeek.Core;
using FolderPeek.Core.FolderInspection;
using FolderPeek.Core.Input;
using FolderPeek.Core.Settings;
using FolderPeek.ExplorerIntegration;
using FolderPeek.Input;
using FolderPeek.Preview;
using FolderPeek.Startup;
using FolderPeek.Theming;
using Forms = System.Windows.Forms;

namespace FolderPeek;

public partial class App : System.Windows.Application
{
    private readonly PeekStateMachine _state = new();
    private readonly IFolderInspector _inspector = new FolderInspector();
    private JsonSettingsService? _settings;
    private IStartupRegistration? _startup;
    private ThemeManager? _theme;
    private ExplorerSnapshotMonitor? _monitor;
    private KeyboardHook? _hook;
    private PreviewWindow? _preview;
    private Settings.SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _enabledMenu;
    private Forms.ToolStripMenuItem? _startupMenu;
    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _contextTimer;
    private CancellationTokenSource? _previewLoad;
    private ExplorerSnapshot? _gestureSnapshot;
    private FolderPeekSettings? _gestureSettings;
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

        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderPeek", "settings.json");
        _settings = new JsonSettingsService(settingsPath);
        _settings.Load();
        _settings.SettingsChanged += OnSettingsChanged;
        _startup = new RegistryStartupRegistration();
        _startup.Changed += OnStartupRegistrationChanged;
        _theme = new ThemeManager(_settings.Current.Theme);
        _theme.Changed += OnThemeChanged;
        _preview = new PreviewWindow();
        ApplyTheme();

        _monitor = new ExplorerSnapshotMonitor();
        _monitor.Start();
        _hook = new KeyboardHook(CanBeginTrigger, IsSameEligibleExplorerContext, allowInjectedInput);
        _hook.Gesture += OnHookGesture;
        try { _hook.Start(); }
        catch (Exception exception)
        {
            _enabled = false;
            Forms.MessageBox.Show($"FolderPeek started, but its keyboard shortcut is unavailable.\n\n{exception.Message}", "FolderPeek", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
        }

        _holdTimer = new DispatcherTimer(DispatcherPriority.Input);
        _holdTimer.Tick += (_, _) => { _holdTimer.Stop(); Apply(_state.HoldThresholdElapsed()); };
        _contextTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _contextTimer.Tick += (_, _) => ValidateOpenContext();
        _contextTimer.Start();
        CreateTrayIcon();
        var captureThemeText = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-theme=", StringComparison.OrdinalIgnoreCase))?["--capture-theme=".Length..];
        if (Enum.TryParse<ThemePreference>(captureThemeText, true, out var captureTheme)) _theme?.SetPreference(captureTheme);
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings();
        var settingsCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-settings=", StringComparison.OrdinalIgnoreCase))?["--capture-settings=".Length..];
        if (!string.IsNullOrWhiteSpace(settingsCapture))
        {
            var captureBottom = e.Args.Contains("--capture-bottom", StringComparer.OrdinalIgnoreCase);
            var exitAfterCapture = e.Args.Contains("--exit-after-capture", StringComparer.OrdinalIgnoreCase);
            OpenSettings();
            var captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            captureTimer.Tick += (_, _) => { captureTimer.Stop(); _settingsWindow?.CaptureTo(settingsCapture, captureBottom); if (exitAfterCapture) Shutdown(); };
            captureTimer.Start();
        }
        var previewCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-preview=", StringComparison.OrdinalIgnoreCase))?["--capture-preview=".Length..];
        var previewFolder = e.Args.FirstOrDefault(argument => argument.StartsWith("--preview-folder=", StringComparison.OrdinalIgnoreCase))?["--preview-folder=".Length..];
        if (!string.IsNullOrWhiteSpace(previewCapture) && !string.IsNullOrWhiteSpace(previewFolder) && Directory.Exists(previewFolder))
            _ = CapturePreviewAsync(previewFolder, previewCapture);
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"startup diagnostics={diagnostics} allowInjectedInput={allowInjectedInput}");
    }

    private bool CanBeginTrigger()
    {
        var settings = _settings!.Current;
        var ctrl = IsDown(NativeMethods.VkControl);
        var forbidden = IsDown(NativeMethods.VkShift) || IsDown(NativeMethods.VkMenu) || IsDown(NativeMethods.VkLWin) || IsDown(NativeMethods.VkRWin);
        var hotkeyMatches = !forbidden && (settings.Hotkey == TriggerHotkey.ControlSpace ? ctrl : !ctrl);
        return hotkeyMatches && IsEligible(settings.SnapshotMaxAge);
    }

    private bool IsSameEligibleExplorerContext() => IsEligible(_settings!.Current.SnapshotMaxAge);

    private bool IsEligible(TimeSpan maxAge)
    {
        var snapshot = _monitor?.Current;
        var gui = new NativeMethods.GuiThreadInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        var focus = NativeMethods.GetGUIThreadInfo(0, ref gui) ? gui.Focus : 0;
        return EligibilityPolicy.CanOwnSpace(new InputContext(_enabled, false, false, NativeMethods.GetForegroundWindow(), focus, DateTimeOffset.UtcNow), snapshot, maxAge);
    }

    private static bool IsDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    private void OnHookGesture(HookGesture gesture) => Dispatcher.BeginInvoke(() => HandleGesture(gesture), DispatcherPriority.Input);

    private void HandleGesture(HookGesture gesture)
    {
        switch (gesture)
        {
            case HookGesture.SpaceDown:
                _gestureSnapshot = _monitor?.Current;
                _gestureSettings = _settings!.Current;
                var down = _state.SpaceDown(_gestureSnapshot?.IsEligible == true, _gestureSettings.TapBehavior);
                if (down.State == PeekState.Pending && _holdTimer is not null)
                {
                    _holdTimer.Interval = _gestureSettings.HoldThreshold;
                    _holdTimer.Start();
                }
                Apply(down);
                break;
            case HookGesture.SpaceUp:
                _holdTimer?.Stop(); Apply(_state.SpaceUp()); break;
            case HookGesture.Escape:
                Apply(_state.Escape(IsSameEligibleExplorerContext())); break;
            case HookGesture.LostRelease:
                _holdTimer?.Stop(); Apply(_state.Reset()); break;
        }
    }

    private void Apply(StateTransition transition)
    {
        if (_hook is not null) _hook.CanConsumeEscape = transition.State == PeekState.StickyOpen;
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"state={transition.State} action={transition.Action}");
        switch (transition.Action)
        {
            case PeekAction.OpenSticky:
            case PeekAction.OpenMomentary:
                if (_gestureSnapshot is { IsEligible: true } snapshot) _ = OpenPreviewAsync(snapshot, _gestureSettings ?? _settings!.Current);
                break;
            case PeekAction.Close: ClosePreview(); break;
        }
    }

    private async Task OpenPreviewAsync(ExplorerSnapshot snapshot, FolderPeekSettings settings)
    {
        if (_preview is null || snapshot.FolderPath is null) return;
        var generation = Interlocked.Increment(ref _previewGeneration);
        _previewLoad?.Cancel(); _previewLoad?.Dispose(); _previewLoad = new CancellationTokenSource();
        var token = _previewLoad.Token;
        var vm = _preview.ViewModel;
        vm.FolderName = snapshot.DisplayName ?? Path.GetFileName(snapshot.FolderPath);
        vm.FolderPath = snapshot.FolderPath; vm.ShowPath = settings.ShowFullPath;
        vm.Entries.Clear(); vm.EntriesChanged(); vm.Loading = true; vm.Status = "Loading folder…";
        _preview.ApplyTheme(_theme!); _preview.ShowBeside(snapshot.ItemBounds ?? CursorAnchor(), settings);

        FolderContents contents;
        try
        {
            var options = new FolderInspectionOptions(settings.ShowHiddenFiles, settings.SortMode, settings.FoldersFirst,
                settings.InitialItemLimit == 0 ? null : settings.InitialItemLimit);
            contents = await _inspector.InspectAsync(snapshot.FolderPath, options, token);
        }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested || generation != Volatile.Read(ref _previewGeneration) || !_preview.IsVisible) return;

        foreach (var entry in contents.Entries)
            vm.Entries.Add(CreateEntry(entry, settings, null));
        vm.Loading = false; vm.EntriesChanged();
        vm.Status = contents.Error ?? (contents.HasMore
            ? $"{settings.InitialItemLimit}+ items · showing first {settings.InitialItemLimit}"
            : $"{contents.Entries.Count} {(contents.Entries.Count == 1 ? "item" : "items")}");
        _preview.ShowBeside(snapshot.ItemBounds ?? CursorAnchor(), settings);
        _ = LoadIconsAsync(contents, settings, generation, token);
    }

    private async Task CapturePreviewAsync(string folder, string output)
    {
        var snapshot = new ExplorerSnapshot(true, "Capture", NativeMethods.GetForegroundWindow(), 0, 0, folder,
            Path.GetFileName(folder), CursorAnchor(), DateTimeOffset.UtcNow, 1);
        await OpenPreviewAsync(snapshot, _settings!.Current);
        await Task.Delay(900);
        _preview?.CaptureTo(output);
        Shutdown();
    }

    private async Task LoadIconsAsync(FolderContents contents, FolderPeekSettings settings, long generation, CancellationToken token)
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
                _preview.ViewModel.Entries[index] = CreateEntry(entry, settings, icon);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine($"FolderPeek icon load: {exception.Message}"); }
    }

    private static PreviewEntryViewModel CreateEntry(FolderEntry entry, FolderPeekSettings settings, System.Windows.Media.Imaging.BitmapSource? icon) =>
        new(entry.Name, entry.IsDirectory ? string.Empty : FormatSize(entry.Size), entry.ModifiedAt.LocalDateTime.ToString("g"),
            settings.ShowFileSize, settings.ShowModifiedDate, icon);

    private static string FormatSize(long? value)
    {
        if (value is null) return string.Empty;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value.Value; var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }

    private static PixelRect CursorAnchor() { NativeMethods.GetCursorPos(out var p); return new PixelRect(p.X - 1, p.Y - 1, p.X + 1, p.Y + 1); }

    private void ValidateOpenContext()
    {
        if (_state.State == PeekState.Idle || _gestureSnapshot is null) return;
        var current = _monitor?.Current;
        if (current?.IsEligible == true && current.ForegroundWindow == _gestureSnapshot.ForegroundWindow &&
            string.Equals(current.FolderPath, _gestureSnapshot.FolderPath, StringComparison.OrdinalIgnoreCase)) return;
        _holdTimer?.Stop(); Apply(_state.ContextInvalidated());
    }

    private void ClosePreview() { Interlocked.Increment(ref _previewGeneration); _previewLoad?.Cancel(); _preview?.Hide(); }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        _theme?.SetPreference(e.Current.Theme); ApplyTheme();
        if (e.Current.TapBehavior == TapBehavior.MomentaryOnly && _state.State == PeekState.StickyOpen) Apply(_state.Reset());
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();
    private void OnStartupRegistrationChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (_startupMenu is not null) _startupMenu.Checked = _startup?.IsEnabled == true; });
    private void ApplyTheme() { if (_preview is not null && _theme is not null) _preview.ApplyTheme(_theme); _settingsWindow?.RefreshTheme(); }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new Settings.SettingsWindow(_settings!, _startup!, _theme!);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show(); _settingsWindow.WindowState = WindowState.Normal; _settingsWindow.Activate();
    }

    private void CreateTrayIcon()
    {
        _tray = new Forms.NotifyIcon { Text = "FolderPeek", Icon = System.Drawing.SystemIcons.Application, Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
        _tray.ContextMenuStrip.Opening += (_, _) => { if (_startupMenu is not null) _startupMenu.Checked = _startup?.IsEnabled == true; };
        var title = new Forms.ToolStripMenuItem("FolderPeek") { Enabled = false, Font = new System.Drawing.Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold) };
        _enabledMenu = new Forms.ToolStripMenuItem("Enabled") { Checked = true };
        _enabledMenu.Click += (_, _) => { _enabled = !_enabled; _enabledMenu.Checked = _enabled; if (!_enabled) { _holdTimer?.Stop(); Apply(_state.ContextInvalidated()); } };
        var settings = new Forms.ToolStripMenuItem("Settings…"); settings.Click += (_, _) => Dispatcher.BeginInvoke(OpenSettings);
        _startupMenu = new Forms.ToolStripMenuItem("Launch at startup") { Checked = _startup!.IsEnabled };
        _startupMenu.Click += (_, _) => { _startup.TrySetEnabled(!_startup.IsEnabled, out var error); _startupMenu.Checked = _startup.IsEnabled; if (error is not null) Forms.MessageBox.Show(error, "FolderPeek"); };
        var about = new Forms.ToolStripMenuItem("About"); about.Click += (_, _) => Forms.MessageBox.Show($"FolderPeek {Assembly.GetExecutingAssembly().GetName().Version}\n\nPress Space on a selected folder to peek inside.", "About FolderPeek");
        var exit = new Forms.ToolStripMenuItem("Exit"); exit.Click += (_, _) => Shutdown();
        _tray.ContextMenuStrip.Items.AddRange([title, new Forms.ToolStripSeparator(), _enabledMenu, settings, _startupMenu, new Forms.ToolStripSeparator(), about, exit]);
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(OpenSettings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _previewLoad?.Cancel(); _contextTimer?.Stop(); _holdTimer?.Stop();
        if (_settings is not null) _settings.SettingsChanged -= OnSettingsChanged;
        if (_startup is not null) _startup.Changed -= OnStartupRegistrationChanged;
        if (_theme is not null) { _theme.Changed -= OnThemeChanged; _theme.Dispose(); }
        if (_hook is not null) { _hook.Gesture -= OnHookGesture; _hook.Dispose(); }
        _monitor?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _settingsWindow?.Close(); _preview?.Close(); _previewLoad?.Dispose(); _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

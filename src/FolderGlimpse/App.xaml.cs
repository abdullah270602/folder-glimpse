using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FolderGlimpse.Core;
using FolderGlimpse.Core.Application;
using FolderGlimpse.Core.FolderInspection;
using FolderGlimpse.Core.Input;
using FolderGlimpse.Core.Interaction;
using FolderGlimpse.Core.Settings;
using FolderGlimpse.Application;
using FolderGlimpse.ExplorerIntegration;
using FolderGlimpse.Input;
using FolderGlimpse.Interaction;
using FolderGlimpse.Preview;
using FolderGlimpse.Shell;
using FolderGlimpse.Startup;
using FolderGlimpse.Theming;
using FolderGlimpse.Updates;
using FolderGlimpse.Tray;
using Forms = System.Windows.Forms;

namespace FolderGlimpse;

public partial class App : System.Windows.Application
{
    private readonly PeekStateMachine _state = new();
    private readonly HoverPreviewStateMachine _hoverState = new();
    private readonly PointerTargetCacheStateMachine _pointerTargetState = new();
    private readonly IFolderInspector _inspector = new FolderInspector();
    private readonly IShellLauncher _shellLauncher = new WindowsShellLauncher();
    private JsonSettingsService? _settings;
    private IStartupRegistration? _startup;
    private ThemeManager? _theme;
    private ExplorerSnapshotMonitor? _monitor;
    private HoverTargetResolver? _hoverResolver;
    private KeyboardHook? _hook;
    private MouseTriggerHook? _mouseHook;
    private PreviewWindow? _preview;
    private ItemActivationService? _activation;
    private IApplicationStateService? _appState;
    private MainWindow? _mainWindow;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _enabledMenu;
    private Forms.ToolStripMenuItem? _startupMenu;
    private System.Drawing.Font? _trayMenuFont;
    private System.Drawing.Font? _trayTitleFont;
    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _contextTimer;
    private DispatcherTimer? _hoverTimer;
    private CancellationTokenSource? _previewLoad;
    private ExplorerSnapshot? _gestureSnapshot;
    private FolderGlimpseSettings? _gestureSettings;
    private SingleInstanceCoordinator? _singleInstance;
    private LaunchIntent _launchIntent;
    private volatile bool _enabled = true;
    private int _stickyInputActive;
    private volatile bool _activationInProgress;
    private bool _detachedSticky;
    private long _previewGeneration;
    private ExplorerSnapshot? _hoverTarget;
    private bool _hoverPreviewActive;
    private ExplorerSnapshot? _pointerTarget;
    private Task<ExplorerSnapshot?>? _pointerResolution;
    private int _mouseTriggerOptions;
    private int _mouseGestureAllowed = 1;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _launchIntent = LaunchIntent.Parse(e.Args);
        var diagnostics = e.Args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase);
        var allowInjectedInput = e.Args.Contains("--allow-injected-input", StringComparer.OrdinalIgnoreCase);
        var captureMode = _launchIntent.Kind == LaunchIntentKind.Capture;
        if (diagnostics) DiagnosticsLog.Initialize();
        _singleInstance = SingleInstanceCoordinator.Create(captureMode);
        if (!_singleInstance.IsPrimary)
        {
            var request = _launchIntent.ActivationRequest;
            if (request is not null && !_singleInstance.TrySignal(request.Value, TimeSpan.FromSeconds(2)))
                Forms.MessageBox.Show("FolderGlimpse is already running, but its window could not be opened. Try the tray icon.", "FolderGlimpse",
                    Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
            Shutdown();
            return;
        }
        _singleInstance.ActivationRequested += OnActivationRequested;
        _singleInstance.StartListening();

        // Visual capture runs use an isolated, disposable profile so QA never mutates or
        // inherits the installed app's settings while rendering screenshots.
        var localAppData = captureMode
            ? Path.Combine(Path.GetTempPath(), "FolderGlimpseCapture", Environment.ProcessId.ToString())
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsPath = Path.Combine(localAppData, "FolderGlimpse", "settings.json");
        var statePath = Path.Combine(localAppData, "FolderGlimpse", "state.json");
        // Legacy FolderPeek location used only for one-time settings migration.
        var legacySettingsPath = Path.Combine(localAppData, "FolderPeek", "settings.json");
        SettingsPathMigration.TryMigrate(legacySettingsPath, settingsPath);
        _settings = new JsonSettingsService(settingsPath);
        _settings.Load();
        Volatile.Write(ref _mouseTriggerOptions, (int)_settings.Current.MouseTriggers);
        _appState = new JsonApplicationStateService(statePath);
        _appState.Load();
        _settings.SettingsChanged += OnSettingsChanged;
        _startup = new RegistryStartupRegistration();
        _startup.Changed += OnStartupRegistrationChanged;
        _theme = new ThemeManager(_settings.Current.Theme);
        _theme.Changed += OnThemeChanged;
        _preview = new PreviewWindow(_shellLauncher);
        _preview.OpenRequested += OpenSelectedAsync;
        _preview.CloseRequested += CloseStickyPreview;
        _preview.PromoteRequested += PromoteHoverPreview;
        _activation = new ItemActivationService(_shellLauncher, new WpfOpenManyConfirmation(_preview, _theme));
        ApplyTheme();

        if (!captureMode)
        {
            _monitor = new ExplorerSnapshotMonitor();
            _monitor.ExplorerContextChanged += OnExplorerContextChanged;
            _monitor.Start();
            _hoverResolver = new HoverTargetResolver();
            _mouseHook = new MouseTriggerHook(TryCaptureMouseTrigger);
            _mouseHook.Gesture += OnMouseTriggerGesture;
            _mouseHook.HookFailed += OnMouseHookFailed;
            try { _mouseHook.Start(_settings.Current.MouseTriggers != MouseTriggerOptions.None); }
            catch (Exception exception)
            {
                _settings.TryUpdate(settings => settings with { MouseTriggers = MouseTriggerOptions.None }, out _);
                Forms.MessageBox.Show($"FolderGlimpse started, but its optional mouse shortcuts are unavailable.\n\n{exception.Message}",
                    "FolderGlimpse", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
            }
            _hook = new KeyboardHook(CanBeginTrigger, IsSameEligibleExplorerContext, allowInjectedInput);
            _hook.Gesture += OnHookGesture;
            try { _hook.Start(); }
            catch (Exception exception)
            {
                _enabled = false;
                Forms.MessageBox.Show($"FolderGlimpse started, but its keyboard shortcut is unavailable.\n\n{exception.Message}", "FolderGlimpse", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
            }
        }

        _holdTimer = new DispatcherTimer(DispatcherPriority.Input);
        _holdTimer.Tick += (_, _) => { _holdTimer.Stop(); Apply(_state.HoldThresholdElapsed()); };
        _contextTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _contextTimer.Tick += (_, _) => ValidateOpenContext();
        if (!captureMode) _contextTimer.Start();
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _hoverTimer.Tick += (_, _) => SampleHover();
        ConfigureHoverTimer();
        CreateTrayIcon();
        var captureThemeText = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-theme=", StringComparison.OrdinalIgnoreCase))?["--capture-theme=".Length..];
        if (Enum.TryParse<ThemePreference>(captureThemeText, true, out var captureTheme)) _theme?.SetPreference(captureTheme);
        var settingsCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-settings=", StringComparison.OrdinalIgnoreCase))?["--capture-settings=".Length..];
        if (!string.IsNullOrWhiteSpace(settingsCapture))
        {
            var captureBottom = e.Args.Contains("--capture-bottom", StringComparer.OrdinalIgnoreCase);
            var captureInteraction = e.Args.Contains("--capture-interaction", StringComparer.OrdinalIgnoreCase);
            var exitAfterCapture = e.Args.Contains("--exit-after-capture", StringComparer.OrdinalIgnoreCase);
            ShowMain(InitialSurface.Settings, false);
            var captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            captureTimer.Tick += (_, _) => { captureTimer.Stop(); _mainWindow?.CaptureSettingsTo(settingsCapture, captureBottom, captureInteraction); if (exitAfterCapture) RequestExit(); };
            captureTimer.Start();
        }
        var aboutCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-about=", StringComparison.OrdinalIgnoreCase))?["--capture-about=".Length..];
        if (!string.IsNullOrWhiteSpace(aboutCapture))
        {
            ShowMain(InitialSurface.About, false);
            var aboutCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            aboutCaptureTimer.Tick += (_, _) => { aboutCaptureTimer.Stop(); _mainWindow?.CaptureTo(aboutCapture); RequestExit(); };
            aboutCaptureTimer.Start();
        }
        var previewCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-preview=", StringComparison.OrdinalIgnoreCase))?["--capture-preview=".Length..];
        var previewFolder = e.Args.FirstOrDefault(argument => argument.StartsWith("--preview-folder=", StringComparison.OrdinalIgnoreCase))?["--preview-folder=".Length..];
        var captureInteractive = e.Args.Contains("--capture-interactive", StringComparer.OrdinalIgnoreCase);
        var captureSelection = !e.Args.Contains("--no-capture-selection", StringComparer.OrdinalIgnoreCase);
        var captureDelayText = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-delay-ms=", StringComparison.OrdinalIgnoreCase))?["--capture-delay-ms=".Length..];
        var captureDelay = int.TryParse(captureDelayText, out var requestedDelay) ? Math.Clamp(requestedDelay, 250, 5000) : 900;
        var capturePresetText = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-preset=", StringComparison.OrdinalIgnoreCase))?["--capture-preset=".Length..];
        var capturePreset = Enum.TryParse<PopupLayoutPreset>(capturePresetText, true, out var parsedPreset) ? parsedPreset : PopupLayoutPreset.Custom;
        if (!string.IsNullOrWhiteSpace(previewCapture) && !string.IsNullOrWhiteSpace(previewFolder) && Directory.Exists(previewFolder))
            _ = CapturePreviewAsync(previewFolder, previewCapture, captureInteractive, captureSelection, captureDelay, capturePreset);
        var trayCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-tray=", StringComparison.OrdinalIgnoreCase))?["--capture-tray=".Length..];
        if (!string.IsNullOrWhiteSpace(trayCapture))
        {
            var trayCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            trayCaptureTimer.Tick += (_, _) => { trayCaptureTimer.Stop(); CaptureTrayMenu(trayCapture); Shutdown(); };
            trayCaptureTimer.Start();
        }
        var mainCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-main=", StringComparison.OrdinalIgnoreCase))?["--capture-main=".Length..];
        if (!string.IsNullOrWhiteSpace(mainCapture))
        {
            ShowMain(InitialSurface.Home, false);
            var sectionText = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-section=", StringComparison.OrdinalIgnoreCase))?["--capture-section=".Length..];
            if (Enum.TryParse<ShellSection>(sectionText, true, out var section)) _mainWindow?.Navigate(section);
            var mainCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            mainCaptureTimer.Tick += (_, _) => { mainCaptureTimer.Stop(); _mainWindow?.CaptureTo(mainCapture); RequestExit(); };
            mainCaptureTimer.Start();
        }
        var welcomeCapture = e.Args.FirstOrDefault(argument => argument.StartsWith("--capture-welcome=", StringComparison.OrdinalIgnoreCase))?["--capture-welcome=".Length..];
        if (!string.IsNullOrWhiteSpace(welcomeCapture))
        {
            ShowMain(InitialSurface.Welcome, false);
            var welcomeCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            welcomeCaptureTimer.Tick += (_, _) => { welcomeCaptureTimer.Stop(); _mainWindow?.CaptureTo(welcomeCapture); RequestExit(); };
            welcomeCaptureTimer.Start();
        }
        if (!captureMode)
            ShowMain(InitialSurfacePolicy.Decide(_launchIntent, _appState.Current.HasCompletedOnboarding));
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"startup diagnostics={diagnostics} allowInjectedInput={allowInjectedInput}");
    }

    private bool CanBeginTrigger()
    {
        var settings = _settings!.Current;
        var ctrl = IsDown(NativeMethods.VkControl);
        var forbidden = IsDown(NativeMethods.VkShift) || IsDown(NativeMethods.VkMenu) || IsDown(NativeMethods.VkLWin) || IsDown(NativeMethods.VkRWin);
        var hotkeyMatches = !forbidden && (settings.Hotkey == TriggerHotkey.ControlSpace ? ctrl : !ctrl);
        if (!hotkeyMatches || !_enabled) return false;
        if (Volatile.Read(ref _stickyInputActive) == 1 && _preview is { Handle: not 0 } preview &&
            NativeMethods.GetForegroundWindow() == preview.Handle) return true;
        return IsEligible(settings.SnapshotMaxAge);
    }

    private bool IsSameEligibleExplorerContext() => IsEligible(_settings!.Current.SnapshotMaxAge);

    private bool IsEligible(TimeSpan maxAge)
    {
        if (_monitor?.IsInvalidated != false) return false;
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
                CancelHoverPreview();
                var closingFocusedSticky = _state.State == PeekState.StickyOpen && _preview is { Handle: not 0 } preview &&
                    NativeMethods.GetForegroundWindow() == preview.Handle;
                if (!closingFocusedSticky) _gestureSnapshot = _monitor?.Current;
                _gestureSettings = _settings!.Current;
                var down = _state.SpaceDown(closingFocusedSticky || _gestureSnapshot?.IsEligible == true, _gestureSettings.TapBehavior);
                if (down.State == PeekState.Pending && _holdTimer is not null)
                {
                    _holdTimer.Interval = _gestureSettings.HoldThreshold;
                    _holdTimer.Start();
                }
                Apply(down);
                var explorerWindow = _gestureSnapshot?.ForegroundWindow ?? 0;
                if (closingFocusedSticky && down.Action == PeekAction.Close && explorerWindow != 0)
                    NativeMethods.SetForegroundWindow(explorerWindow);
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
        Volatile.Write(ref _stickyInputActive, transition.State == PeekState.StickyOpen ? 1 : 0);
        Volatile.Write(ref _mouseGestureAllowed, transition.State == PeekState.Idle ? 1 : 0);
        if (_hook is not null) _hook.CanConsumeEscape = transition.State == PeekState.StickyOpen;
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"state={transition.State} action={transition.Action}");
        switch (transition.Action)
        {
            case PeekAction.OpenSticky:
                if (_gestureSnapshot is { IsEligible: true } stickySnapshot) _ = OpenPreviewAsync(stickySnapshot,
                    _gestureSettings ?? _settings!.Current, PreviewInteractionMode.Sticky);
                break;
            case PeekAction.PromoteSticky:
                if (_gestureSnapshot is { IsEligible: true } promotedSnapshot && _preview is not null)
                    _preview.ShowBeside(promotedSnapshot.ItemBounds ?? CursorAnchor(),
                        _gestureSettings ?? _settings!.Current, PreviewInteractionMode.Sticky);
                break;
            case PeekAction.OpenMomentary:
                if (_gestureSnapshot is { IsEligible: true } snapshot) _ = OpenPreviewAsync(snapshot,
                    _gestureSettings ?? _settings!.Current, PreviewInteractionMode.ViewOnly);
                break;
            case PeekAction.Close: ClosePreview(); break;
        }
    }

    private async Task OpenPreviewAsync(ExplorerSnapshot snapshot, FolderGlimpseSettings settings,
        PreviewInteractionMode interactionMode)
    {
        if (_preview is null || snapshot.FolderPath is null) return;
        var generation = Interlocked.Increment(ref _previewGeneration);
        _previewLoad?.Cancel(); _previewLoad?.Dispose(); _previewLoad = new CancellationTokenSource();
        var token = _previewLoad.Token;
        var vm = _preview.ViewModel;
        vm.FolderName = snapshot.DisplayName ?? Path.GetFileName(snapshot.FolderPath);
        vm.FolderPath = snapshot.FolderPath;
        _preview.ResetSelection(); vm.Entries.Clear(); vm.EntriesChanged();
        vm.ErrorMessage = string.Empty; vm.IsTruncated = false; vm.Loading = true; vm.Status = string.Empty;
        _preview.ApplyTheme(_theme!); _preview.ShowBeside(snapshot.ItemBounds ?? CursorAnchor(), settings, interactionMode);

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
        vm.ErrorMessage = contents.Error ?? string.Empty;
        vm.IsTruncated = contents.HasMore;
        vm.Status = contents.HasMore
            ? $"{settings.InitialItemLimit}+ items · showing first {settings.InitialItemLimit}"
            : $"{contents.Entries.Count} {(contents.Entries.Count == 1 ? "item" : "items")}";
        _preview.ShowBeside(snapshot.ItemBounds ?? CursorAnchor(), settings, interactionMode);
        if (PopupCustomization.ShouldLoadEntryIcons(settings)) _ = LoadIconsAsync(contents, settings, generation, token);
    }

    private async Task CapturePreviewAsync(string folder, string output, bool interactive, bool captureSelection, int captureDelayMs,
        PopupLayoutPreset capturePreset = PopupLayoutPreset.Custom)
    {
        var snapshot = new ExplorerSnapshot(true, "Capture", NativeMethods.GetForegroundWindow(), 0, 0, folder,
            Path.GetFileName(folder), CursorAnchor(), DateTimeOffset.UtcNow, 1);
        var captureSettings = interactive
            ? _settings!.Current with { InteractiveItems = true, MultiSelection = true, ShowSelectionCheckboxes = true }
            : _settings!.Current;
        captureSettings = PopupCustomization.ApplyPreset(captureSettings, capturePreset);
        if (interactive)
        {
            _gestureSnapshot = snapshot;
            _gestureSettings = captureSettings;
            _state.SpaceDown(true, captureSettings.TapBehavior);
            _state.SpaceUp();
            Volatile.Write(ref _stickyInputActive, 1);
        }
        await OpenPreviewAsync(snapshot, captureSettings,
            interactive ? PreviewInteractionMode.Sticky : PreviewInteractionMode.ViewOnly);
        await Task.Delay(captureDelayMs);
        if (interactive && captureSelection) _preview?.SelectFirstForCapture(3);
        _preview?.CaptureTo(output);
        Shutdown();
    }

    private async Task LoadIconsAsync(FolderContents contents, FolderGlimpseSettings settings, long generation, CancellationToken token)
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
                _preview.ReplaceEntry(index, CreateEntry(entry, settings, icon));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.WriteLine($"FolderGlimpse icon load: {exception.Message}"); }
    }

    private static PreviewEntryViewModel CreateEntry(FolderEntry entry, FolderGlimpseSettings settings, System.Windows.Media.Imaging.BitmapSource? icon) =>
        new(entry, entry.IsDirectory ? string.Empty : FormatSize(entry.Size), entry.ModifiedAt.LocalDateTime.ToString("g"),
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

    private void ConfigureHoverTimer()
    {
        if (_hoverTimer is null || _settings is null) return;
        var shouldSample = _monitor is not null && HoverSamplingPolicy.ShouldSample(
            _enabled, _settings.Current.HoverMode, _settings.Current.MouseTriggers,
            NativeMethods.GetForegroundWindow(), _monitor.CurrentExplorerWindow);
        if (shouldSample) _hoverTimer.Start();
        else { _hoverTimer.Stop(); CancelHoverPreview(); CancelPointerTarget(); }
    }

    private void OnExplorerContextChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(ConfigureHoverTimer, DispatcherPriority.Background);
    }

    private void SampleHover()
    {
        if (_settings is null || _monitor is null || _preview is null) { CancelHoverPreview(); return; }
        var settings = _settings.Current;
        var inputIdle = _state.State == PeekState.Idle && !_activationInProgress;
        var hoverCanSample = HoverEligibilityPolicy.CanSample(_enabled, settings.HoverMode, inputIdle, _activationInProgress);
        var mouseCanSample = _enabled && settings.MouseTriggers != MouseTriggerOptions.None && inputIdle;
        if (!hoverCanSample && !mouseCanSample) { CancelHoverPreview(); CancelPointerTarget(); return; }
        if (!NativeMethods.GetCursorPos(out var nativePoint)) { CancelHoverPreview(); return; }
        var point = new HoverPoint(nativePoint.X, nativePoint.Y);
        var now = DateTimeOffset.UtcNow;

        if (_hoverPreviewActive)
        {
            var target = _hoverTarget;
            var sameExplorer = target is not null && NativeMethods.GetForegroundWindow() == target.ForegroundWindow;
            var overSource = target?.ItemBounds is { } bounds && Contains(bounds, point);
            var overPreview = _preview.Handle != 0 && NativeMethods.GetWindowRect(_preview.Handle, out var window) &&
                point.X >= window.Left && point.X < window.Right && point.Y >= window.Top && point.Y < window.Bottom;
            if (!sameExplorer) { CancelHoverPreview(); return; }
            var openTransition = _hoverState.ObserveOpen(overSource || overPreview, now, settings.HoverCloseDelay);
            if (openTransition.Action == HoverAction.Close) CloseHoverPreview();
            return;
        }

        var buttonsDown = IsDown(NativeMethods.VkLButton) || IsDown(NativeMethods.VkRButton) || IsDown(NativeMethods.VkMButton);
        if (buttonsDown) return;
        var foreground = NativeMethods.GetForegroundWindow();
        var explorerUnderPointer = foreground != 0 && foreground == _monitor.CurrentExplorerWindow &&
            _monitor.CurrentExplorerProcessId != 0;
        if (mouseCanSample && explorerUnderPointer)
            SamplePointerTarget(foreground, _monitor.CurrentExplorerProcessId, point, now, settings);
        else CancelPointerTarget();

        if (!hoverCanSample) { CancelHoverPreview(); return; }
        var modifiersMatch = HoverEligibilityPolicy.IsModifierMatch(settings.HoverModifier,
            IsDown(NativeMethods.VkControl), IsDown(NativeMethods.VkShift), IsDown(NativeMethods.VkMenu),
            IsDown(NativeMethods.VkLWin) || IsDown(NativeMethods.VkRWin));
        if (!modifiersMatch || foreground == 0) { CancelHoverPreview(); return; }

        ExplorerSnapshot? selected = null;
        var candidate = settings.HoverMode switch
        {
            HoverPreviewMode.SelectedFolder => !_monitor.IsInvalidated && HoverEligibilityPolicy.CanUseSelectedSnapshot(
                selected = _monitor.Current, foreground, point, now, settings.SnapshotMaxAge),
            HoverPreviewMode.AnyFolder => explorerUnderPointer,
            _ => false
        };
        if (!candidate) { CancelHoverPreview(); return; }

        var transition = _hoverState.ObserveCandidate(point, now, settings.HoverMovementTolerancePx, settings.HoverOpenDelay);
        if (transition.Action != HoverAction.Resolve) return;
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"hover resolve mode={settings.HoverMode} hwnd=0x{foreground:X} point={point.X},{point.Y} generation={transition.Generation}");
        if (settings.HoverMode == HoverPreviewMode.SelectedFolder)
        {
            CompleteHoverResolution(transition.Generation, selected, point, settings);
            return;
        }
        if (settings.MouseTriggers != MouseTriggerOptions.None)
        {
            var cached = Volatile.Read(ref _pointerTarget);
            if (MouseTriggerPolicy.IsFreshTargetAtPoint(cached, point, foreground, now,
                TimeSpan.FromMilliseconds(1500)))
            {
                CompleteHoverResolution(transition.Generation, cached, point, settings);
                return;
            }
            if (_pointerResolution is { } pending)
            {
                _ = CompleteHoverFromPointerAsync(pending, transition.Generation, point, settings);
                return;
            }
        }
        _ = ResolveAnyHoverAsync(foreground, _monitor.CurrentExplorerProcessId, point, transition.Generation, settings);
    }

    private void SamplePointerTarget(nint foreground, int explorerPid, HoverPoint point, DateTimeOffset now,
        FolderGlimpseSettings settings)
    {
        var transition = _pointerTargetState.Observe(point, now, settings.HoverMovementTolerancePx,
            TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(1000));
        if (transition.Action == PointerTargetAction.Clear) Volatile.Write(ref _pointerTarget, null);
        if (transition.Action != PointerTargetAction.Resolve || _hoverResolver is null) return;
        var task = _hoverResolver.ResolveAsync(foreground, explorerPid, point, transition.Generation);
        _pointerResolution = task;
        _ = CompletePointerTargetAsync(task, transition.Generation, point, foreground, settings.HoverMovementTolerancePx);
    }

    private async Task CompletePointerTargetAsync(Task<ExplorerSnapshot?> task, long generation, HoverPoint originalPoint,
        nint foreground, int tolerance)
    {
        var snapshot = await task;
        await Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(_pointerResolution, task)) _pointerResolution = null;
            var eligible = NativeMethods.GetForegroundWindow() == foreground && NativeMethods.GetCursorPos(out var cursor) &&
                new HoverPoint(cursor.X, cursor.Y).DistanceSquared(originalPoint) <= (long)tolerance * tolerance &&
                snapshot?.ItemBounds is { } bounds && Contains(bounds, new(cursor.X, cursor.Y));
            var transition = _pointerTargetState.Resolved(generation, eligible, DateTimeOffset.UtcNow);
            if (transition.Generation == generation && transition.Phase is PointerTargetPhase.Ready or PointerTargetPhase.Rejected)
                Volatile.Write(ref _pointerTarget, transition.Phase == PointerTargetPhase.Ready ? snapshot : null);
        }, DispatcherPriority.Background);
    }

    private async Task CompleteHoverFromPointerAsync(Task<ExplorerSnapshot?> task, long hoverGeneration,
        HoverPoint point, FolderGlimpseSettings settings)
    {
        var snapshot = await task;
        await Dispatcher.InvokeAsync(() => CompleteHoverResolution(hoverGeneration, snapshot, point, settings),
            DispatcherPriority.Background);
    }

    private async Task ResolveAnyHoverAsync(nint foreground, int explorerPid, HoverPoint point, long generation,
        FolderGlimpseSettings capturedSettings)
    {
        if (_hoverResolver is null) return;
        var snapshot = await _hoverResolver.ResolveAsync(foreground, explorerPid, point, generation);
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"hover resolved eligible={snapshot?.IsEligible == true} path={snapshot?.FolderPath ?? "<none>"} generation={generation}");
        await Dispatcher.InvokeAsync(() => CompleteHoverResolution(generation, snapshot, point, capturedSettings), DispatcherPriority.Background);
    }

    private void CompleteHoverResolution(long generation, ExplorerSnapshot? snapshot, HoverPoint originalPoint,
        FolderGlimpseSettings capturedSettings)
    {
        if (_settings is null || capturedSettings.HoverMode != _settings.Current.HoverMode ||
            !NativeMethods.GetCursorPos(out var currentPoint) || snapshot?.ItemBounds is not { } bounds ||
            !Contains(bounds, new HoverPoint(currentPoint.X, currentPoint.Y)) ||
            new HoverPoint(currentPoint.X, currentPoint.Y).DistanceSquared(originalPoint) >
                (long)capturedSettings.HoverMovementTolerancePx * capturedSettings.HoverMovementTolerancePx)
        {
            _hoverState.Resolved(generation, false);
            return;
        }
        var transition = _hoverState.Resolved(generation, true);
        if (transition.Action != HoverAction.Open || snapshot is null) return;
        _hoverTarget = snapshot;
        _hoverPreviewActive = true;
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"hover open path={snapshot.FolderPath} generation={generation}");
        _ = OpenPreviewAsync(snapshot, capturedSettings, PreviewInteractionMode.HoverPointer);
    }

    private static bool Contains(PixelRect bounds, HoverPoint point) =>
        point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    private void CancelHoverPreview()
    {
        var transition = _hoverState.Cancel();
        if (transition.Action == HoverAction.Close) CloseHoverPreview();
    }

    private void CancelPointerTarget()
    {
        _pointerTargetState.Cancel();
        _pointerResolution = null;
        Volatile.Write(ref _pointerTarget, null);
    }

    private ExplorerSnapshot? TryCaptureMouseTrigger(MouseTriggerInput input)
    {
        if (!_enabled || Volatile.Read(ref _mouseGestureAllowed) == 0) return null;
        var configured = (MouseTriggerOptions)Volatile.Read(ref _mouseTriggerOptions);
        var target = Volatile.Read(ref _pointerTarget);
        return MouseTriggerPolicy.CanCapture(configured, input, target, TimeSpan.FromMilliseconds(1500)) ? target : null;
    }

    private void OnMouseTriggerGesture(MouseTriggerGesture gesture) =>
        Dispatcher.BeginInvoke(() => HandleMouseTrigger(gesture), DispatcherPriority.Input);

    private void HandleMouseTrigger(MouseTriggerGesture gesture)
    {
        if (_settings is null || !_enabled || _state.State != PeekState.Idle ||
            !_settings.Current.MouseTriggers.HasFlag(gesture.Trigger) ||
            NativeMethods.GetForegroundWindow() != gesture.Target.ForegroundWindow ||
            gesture.Target.ItemBounds is not { } bounds || !Contains(bounds, gesture.ReleasePoint)) return;
        CancelHoverPreview();
        CancelPointerTarget();
        _gestureSnapshot = gesture.Target;
        _gestureSettings = _settings.Current;
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"mouse trigger={gesture.Trigger} path={gesture.Target.FolderPath}");
        Apply(_state.OpenPointerSticky());
    }

    private void OnMouseHookFailed(Exception exception) => Dispatcher.BeginInvoke(() =>
    {
        if (_settings is null) return;
        _settings.TryUpdate(settings => settings with { MouseTriggers = MouseTriggerOptions.None }, out _);
        Forms.MessageBox.Show($"Mouse shortcuts were turned off because their Windows hook could not be installed.\n\n{exception.Message}",
            "FolderGlimpse", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
    });

    private void CloseHoverPreview()
    {
        if (!_hoverPreviewActive) return;
        _hoverPreviewActive = false;
        _hoverTarget = null;
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write("hover close");
        ClosePreview();
    }

    private void PromoteHoverPreview()
    {
        if (!_hoverPreviewActive || _hoverTarget is not { IsEligible: true } target || _settings is null) return;
        var hoverTransition = _hoverState.Promote();
        if (hoverTransition.Action != HoverAction.Promote) return;
        _hoverPreviewActive = false;
        _hoverTarget = null;
        _gestureSnapshot = target;
        _gestureSettings = _settings.Current;
        if (DiagnosticsLog.Enabled) DiagnosticsLog.Write($"hover promote path={target.FolderPath}");
        Apply(_state.PromoteToSticky());
    }

    private void ValidateOpenContext()
    {
        if (_state.State == PeekState.Idle || _gestureSnapshot is null) return;
        if (_state.State == PeekState.StickyOpen && (_preview?.OwnsForeground == true || _activationInProgress || _detachedSticky)) return;
        var current = _monitor?.Current;
        if (current?.IsEligible == true && current.ForegroundWindow == _gestureSnapshot.ForegroundWindow &&
            string.Equals(current.FolderPath, _gestureSnapshot.FolderPath, StringComparison.OrdinalIgnoreCase)) return;
        _holdTimer?.Stop(); Apply(_state.ContextInvalidated());
    }

    private void ClosePreview()
    {
        Interlocked.Increment(ref _previewGeneration);
        _previewLoad?.Cancel();
        _detachedSticky = false;
        _preview?.Hide();
        _preview?.SetDetached(false);
    }

    private void CloseStickyPreview()
    {
        var explorerWindow = _gestureSnapshot?.ForegroundWindow ?? 0;
        Apply(_state.Reset());
        if (explorerWindow != 0) NativeMethods.SetForegroundWindow(explorerWindow);
    }

    private async Task OpenSelectedAsync(IReadOnlyList<FolderEntry> entries)
    {
        if (_activation is null || _settings is null || _preview is null) return;
        var settings = _settings.Current;
        var options = new ActivationOptions(settings.InteractiveItems, settings.AllowOpeningMultipleItems, settings.ConfirmBeforeOpeningMoreThan);
        _activationInProgress = true;
        _preview.ViewModel.ErrorMessage = string.Empty;
        try
        {
            if (DiagnosticsLog.Enabled)
                DiagnosticsLog.Write($"activation requested count={entries.Count} paths={string.Join('|', entries.Select(entry => entry.FullPath))}");
            var result = await _activation.OpenAsync(entries, options);
            if (DiagnosticsLog.Enabled)
                DiagnosticsLog.Write($"activation completed requested={result.RequestedCount} cancelled={result.Cancelled} error={result.Error ?? "<none>"}");
            if (result.Error is not null) { _preview.ViewModel.ErrorMessage = result.Error; return; }
            if (result.RequestedCount > 0)
            {
                if (_hoverPreviewActive) CloseHoverPreview();
                else if (settings.ClosePreviewAfterOpening) Apply(_state.Reset());
                else { _detachedSticky = true; _preview.SetDetached(true); }
            }
        }
        catch (Exception exception)
        {
            _preview.ViewModel.ErrorMessage = exception is FileNotFoundException or DirectoryNotFoundException
                ? "This item is no longer available."
                : "Windows could not open the selected item.";
        }
        finally { _activationInProgress = false; }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Previous.HoverMode != e.Current.HoverMode || e.Previous.HoverModifier != e.Current.HoverModifier ||
            e.Previous.HoverOpenDelayMs != e.Current.HoverOpenDelayMs || e.Previous.HoverCloseDelayMs != e.Current.HoverCloseDelayMs ||
            e.Previous.HoverMovementTolerancePx != e.Current.HoverMovementTolerancePx) CancelHoverPreview();
        if (e.Previous.MouseTriggers != e.Current.MouseTriggers)
        {
            Volatile.Write(ref _mouseTriggerOptions, (int)e.Current.MouseTriggers);
            _mouseHook?.SetEnabled(e.Current.MouseTriggers != MouseTriggerOptions.None);
            CancelPointerTarget();
        }
        _theme?.SetPreference(e.Current.Theme); ApplyTheme();
        if (e.Current.TapBehavior == TapBehavior.MomentaryOnly && _state.State == PeekState.StickyOpen) Apply(_state.Reset());
        if (_preview?.IsVisible == true) _preview.ConfigureInteraction(
            _hoverPreviewActive ? PreviewInteractionMode.HoverPointer :
            _state.State == PeekState.StickyOpen ? PreviewInteractionMode.Sticky : PreviewInteractionMode.ViewOnly,
            e.Current);
        _mainWindow?.RefreshState();
        ConfigureHoverTimer();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();
    private void OnStartupRegistrationChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_startupMenu is not null) _startupMenu.Checked = _startup?.IsEnabled == true;
        _mainWindow?.RefreshState();
    });
    private void ApplyTheme()
    {
        if (_preview is not null && _theme is not null) _preview.ApplyTheme(_theme);
        _mainWindow?.RefreshTheme();
        ApplyTrayTheme();
    }

    private void ShowMain(InitialSurface surface, bool activate = true)
    {
        if (surface == InitialSurface.None || _settings is null || _startup is null || _appState is null || _theme is null) return;
        CancelHoverPreview();
        if (_state.State != PeekState.Idle) { _holdTimer?.Stop(); Apply(_state.Reset()); }
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(_settings, _startup, _appState, _theme, new GitHubUpdateChecker(),
                () => _enabled, SetEnabled);
        }
        _mainWindow.ShowSurface(surface, activate);
    }

    private void OnActivationRequested(ActivationRequest request) => Dispatcher.BeginInvoke(() =>
    {
        if (_mainWindow is null)
            ShowMain(request switch { ActivationRequest.Settings => InitialSurface.Settings, ActivationRequest.About => InitialSurface.About,
                _ => InitialSurfacePolicy.Decide(new(LaunchIntentKind.Normal), _appState?.Current.HasCompletedOnboarding == true) });
        else _mainWindow.HandleActivation(request);
    });

    private void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (_enabledMenu is not null) _enabledMenu.Checked = enabled;
        if (!enabled) { _holdTimer?.Stop(); Apply(_state.ContextInvalidated()); CancelHoverPreview(); CancelPointerTarget(); }
        ConfigureHoverTimer();
        _mainWindow?.RefreshState();
    }

    private void RequestExit()
    {
        Shutdown();
    }

    private void CreateTrayIcon()
    {
        _trayMenuFont = new System.Drawing.Font("Segoe UI Variable Text", 10f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        _trayTitleFont = new System.Drawing.Font("Segoe UI Variable Text", 10f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
        _trayMenu = new Forms.ContextMenuStrip
        {
            AutoSize = true,
            ShowCheckMargin = true,
            ShowImageMargin = false,
            DropShadowEnabled = true,
            Padding = new Forms.Padding(6, 12, 6, 6),
            Font = _trayMenuFont
        };
        _trayIcon = LoadTrayIcon();
        _tray = new Forms.NotifyIcon { Text = "FolderGlimpse", Icon = _trayIcon, Visible = true, ContextMenuStrip = _trayMenu };
        _trayMenu.Opening += (_, _) =>
        {
            if (_startupMenu is not null) _startupMenu.Checked = _startup?.IsEnabled == true;
            ApplyTrayTheme();
        };
        _trayMenu.Opened += (_, _) => ApplyNativeTrayTheme();

        var title = TrayItem("FolderGlimpse");
        title.Enabled = false;
        title.Tag = ModernTrayMenuRenderer.TitleItemTag;
        title.Font = _trayTitleFont;
        var open = TrayItem("Open FolderGlimpse");
        open.Click += (_, _) => Dispatcher.BeginInvoke(() => ShowMain(InitialSurface.Home));
        _enabledMenu = TrayItem("Enabled");
        _enabledMenu.Checked = true;
        _enabledMenu.Click += (_, _) => SetEnabled(!_enabled);
        var settings = TrayItem("Settings…"); settings.Click += (_, _) => Dispatcher.BeginInvoke(() => ShowMain(InitialSurface.Settings));
        _startupMenu = TrayItem("Launch at startup");
        _startupMenu.Checked = _startup!.IsEnabled;
        _startupMenu.Click += (_, _) => { _startup.TrySetEnabled(!_startup.IsEnabled, out var error); _startupMenu.Checked = _startup.IsEnabled; if (error is not null) Forms.MessageBox.Show(error, "FolderGlimpse"); };
        var exit = TrayItem("Exit"); exit.Click += (_, _) => RequestExit();
        _trayMenu.Items.AddRange([title, TraySeparator(), open, _enabledMenu, settings, _startupMenu, TraySeparator(), exit]);
        ApplyTrayTheme();
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => ShowMain(InitialSurface.Home));
    }

    private static Forms.ToolStripMenuItem TrayItem(string text) => new(text)
    {
        AutoSize = false,
        Size = new System.Drawing.Size(224, 36),
        Padding = new Forms.Padding(0, 0, 12, 0),
        Margin = Forms.Padding.Empty
    };

    private static Forms.ToolStripSeparator TraySeparator() => new()
    {
        AutoSize = false,
        Size = new System.Drawing.Size(224, 9),
        Margin = Forms.Padding.Empty
    };

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("Assets/Branding/FolderGlimpse-Tray.ico", UriKind.Relative))
            ?? throw new InvalidOperationException("FolderGlimpse tray icon resource is missing.");
        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }

    private void ApplyTrayTheme()
    {
        if (_trayMenu is null || _theme is null) return;
        var renderer = new ModernTrayMenuRenderer(_theme.IsDark);
        _trayMenu.Renderer = renderer;
        _trayMenu.BackColor = renderer.BackgroundColor;
        _trayMenu.ForeColor = renderer.ForegroundColor;
        _trayMenu.Invalidate(true);
    }

    private void ApplyNativeTrayTheme()
    {
        if (_trayMenu is null || _theme is null || !_trayMenu.IsHandleCreated) return;
        var dark = _theme.IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(_trayMenu.Handle, NativeMethods.DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var corner = NativeMethods.DwmwcpRoundSmall;
        NativeMethods.DwmSetWindowAttribute(_trayMenu.Handle, NativeMethods.DwmwaWindowCornerPreference, ref corner, sizeof(int));
    }

    private void CaptureTrayMenu(string path)
    {
        if (_trayMenu is null) return;
        _trayMenu.Show(new System.Drawing.Point(32, 32));
        _trayMenu.PerformLayout();
        _trayMenu.Items.OfType<Forms.ToolStripMenuItem>().FirstOrDefault(item => item.Text == "Settings…")?.Select();
        using var bitmap = new System.Drawing.Bitmap(_trayMenu.Width, _trayMenu.Height);
        _trayMenu.DrawToBitmap(bitmap, _trayMenu.ClientRectangle);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        _trayMenu.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _previewLoad?.Cancel(); _contextTimer?.Stop(); _holdTimer?.Stop(); _hoverTimer?.Stop();
        if (_settings is not null) _settings.SettingsChanged -= OnSettingsChanged;
        if (_startup is not null) _startup.Changed -= OnStartupRegistrationChanged;
        if (_theme is not null) { _theme.Changed -= OnThemeChanged; _theme.Dispose(); }
        if (_hook is not null) { _hook.Gesture -= OnHookGesture; _hook.Dispose(); }
        if (_mouseHook is not null)
        {
            _mouseHook.Gesture -= OnMouseTriggerGesture;
            _mouseHook.HookFailed -= OnMouseHookFailed;
            _mouseHook.Dispose();
        }
        if (_monitor is not null) _monitor.ExplorerContextChanged -= OnExplorerContextChanged;
        _monitor?.Dispose();
        _hoverResolver?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _trayIcon?.Dispose();
        _trayMenu?.Dispose();
        _trayMenuFont?.Dispose(); _trayTitleFont?.Dispose();
        if (_preview is not null)
        {
            _preview.OpenRequested -= OpenSelectedAsync;
            _preview.CloseRequested -= CloseStickyPreview;
            _preview.PromoteRequested -= PromoteHoverPreview;
        }
        if (_singleInstance is not null) _singleInstance.ActivationRequested -= OnActivationRequested;
        _singleInstance?.Dispose();
        _mainWindow?.PrepareForExit(); _mainWindow?.Dispose();
        _preview?.Close(); _previewLoad?.Dispose();
        base.OnExit(e);
    }
}

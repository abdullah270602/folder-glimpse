using FolderGlimpse.Core;
using FolderGlimpse.Core.Application;
using FolderGlimpse.Core.FolderInspection;
using FolderGlimpse.Core.Input;
using FolderGlimpse.Core.Interaction;
using FolderGlimpse.Core.Settings;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Tap/hold state machine", TestStateMachine),
    ("Input eligibility policy", TestEligibility),
    ("Hover preview state and eligibility", TestHoverPreview),
    ("Hover sampling policy", TestHoverSamplingPolicy),
    ("Mouse trigger safety and pointer cache", TestMouseTriggers),
    ("Hover UI Automation ancestry policy", TestHoverElementPolicy),
    ("Explorer focus ancestry policy", TestExplorerFocusPolicy),
    ("Folder enumeration", TestEnumeration),
    ("Settings persistence and recovery", TestSettings),
    ("Settings scroll policy", TestSettingsScroll),
    ("Settings location migration", TestSettingsMigration),
    ("Startup registration migration", TestStartupMigration),
    ("Launch, onboarding, and activation policy", TestApplicationLifecycle),
    ("Semantic update version ordering", TestSemanticVersions),
    ("Privacy-safe diagnostic report", TestDiagnosticReport),
    ("Product identity metadata", TestProductIdentity),
    ("Interactive selection model", TestSelection),
    ("Safe item activation", TestActivation),
    ("Preview interaction routing", TestPreviewInteractionRouting),
    ("Context action policy", TestContextActions),
    ("Popup positioning", TestPositioning)
};

static Task TestSemanticVersions()
{
    True(SemanticVersion.TryParse("v0.1.0-beta.2+build", out var beta2), "tag and build metadata parse");
    True(SemanticVersion.TryParse("0.1.0-beta.10", out var beta10), "multi-digit prerelease parses");
    True(SemanticVersion.TryParse("0.1.0", out var stable), "stable version parses");
    True(beta10.CompareTo(beta2) > 0, "numeric prerelease identifiers use numeric ordering");
    True(stable.CompareTo(beta10) > 0, "stable release sorts after its prereleases");
    True(SemanticVersion.TryParse("1.0.0-alpha.1", out var alpha1), "dotted prerelease parses");
    True(SemanticVersion.TryParse("1.0.0-alpha.beta", out var alphaBeta), "text prerelease parses");
    True(alphaBeta.CompareTo(alpha1) > 0, "numeric prerelease identifiers sort before text identifiers");
    False(SemanticVersion.TryParse("1.0", out _), "incomplete versions fail closed");
    False(SemanticVersion.TryParse("1.0.0-", out _), "empty prerelease fails closed");
    return Task.CompletedTask;
}

static Task TestDiagnosticReport()
{
    var report = DiagnosticReport.Create(FolderGlimpseSettings.Default with
    {
        MouseTriggers = MouseTriggerOptions.MiddleClick | MouseTriggerOptions.ControlRightClick
    }, "0.1.0-beta.2", "Windows test", "X64", ".NET test");
    True(report.Contains("0.1.0-beta.2", StringComparison.Ordinal), "report includes product version");
    True(report.Contains("MiddleClick", StringComparison.Ordinal), "report includes relevant trigger configuration");
    False(report.Contains("C:\\", StringComparison.Ordinal), "report contains no local path");
    False(report.Contains("user", StringComparison.OrdinalIgnoreCase), "report contains no user identity field");
    return Task.CompletedTask;
}

var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL  {test.Name}\n      {exception.Message}"); }
}
Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} suites passed");
return failed;

static Task TestStateMachine()
{
    var machine = new PeekStateMachine();
    Equal(PeekState.Pending, machine.SpaceDown(true).State, "eligible down -> pending");
    var tap = machine.SpaceUp();
    Equal(PeekState.StickyOpen, tap.State, "quick up -> sticky");
    Equal(PeekAction.OpenSticky, tap.Action, "quick up opens sticky");
    var closeDown = machine.SpaceDown(true);
    Equal(PeekAction.Close, closeDown.Action, "second tap closes immediately");
    True(closeDown.Suppress, "second down is suppressed");
    Equal(PeekState.Idle, machine.SpaceUp().State, "second up completes owned gesture");

    machine = new PeekStateMachine();
    machine.SpaceDown(true);
    Equal(PeekAction.None, machine.SpaceDown(true).Action, "repeat down is inert");
    var held = machine.HoldThresholdElapsed();
    Equal(PeekState.MomentaryOpen, held.State, "threshold -> momentary");
    Equal(PeekAction.OpenMomentary, held.Action, "threshold opens momentary");
    var released = machine.SpaceUp();
    Equal(PeekState.Idle, released.State, "held release -> idle");
    Equal(PeekAction.Close, released.Action, "held release closes");

    machine.SpaceDown(true); machine.SpaceUp();
    var escape = machine.Escape(true);
    Equal(PeekState.Idle, escape.State, "Escape closes sticky");
    True(escape.Suppress, "same-context Escape is suppressed");

    var passed = new PeekStateMachine().SpaceDown(false);
    Equal(PeekState.Idle, passed.State, "ineligible down stays idle");
    False(passed.Suppress, "ineligible down passes");

    machine = new PeekStateMachine();
    var promoted = machine.PromoteToSticky();
    Equal(PeekState.StickyOpen, promoted.State, "pointer promotion opens sticky state");
    Equal(PeekAction.PromoteSticky, promoted.Action, "pointer promotion reuses the visible preview");
    False(promoted.Suppress, "pointer promotion does not claim keyboard input");
    Equal(PeekAction.Close, machine.Escape(true).Action, "Escape closes a pointer-promoted sticky preview");

    machine = new PeekStateMachine();
    machine.SpaceDown(true);
    Equal(PeekAction.None, machine.PromoteToSticky().Action, "pointer promotion cannot interrupt an owned Space gesture");

    machine = new PeekStateMachine();
    var mouseOpen = machine.OpenPointerSticky();
    Equal(PeekState.StickyOpen, mouseOpen.State, "mouse shortcut opens sticky state");
    Equal(PeekAction.OpenSticky, mouseOpen.Action, "mouse shortcut loads a new sticky preview");
    False(mouseOpen.Suppress, "mouse shortcut state does not claim keyboard input");

    machine = new PeekStateMachine();
    machine.SpaceDown(true); machine.HoldThresholdElapsed();
    var invalidated = machine.ContextInvalidated();
    Equal(PeekState.ClosingUntilSpaceUp, invalidated.State, "invalidated owned hold waits for up");
    Equal(PeekAction.Close, invalidated.Action, "invalidated hold closes");
    True(machine.SpaceUp().Suppress, "matching up remains suppressed");

    machine = new PeekStateMachine();
    machine.SpaceDown(true, TapBehavior.MomentaryOnly);
    var momentaryTap = machine.SpaceUp();
    Equal(PeekState.Idle, momentaryTap.State, "momentary-only tap stays closed");
    True(momentaryTap.Suppress, "momentary-only tap remains an owned gesture");
    return Task.CompletedTask;
}

static Task TestEligibility()
{
    var now = DateTimeOffset.UtcNow;
    var eligible = new ExplorerSnapshot(true, "Eligible", 42, 84, 7, @"C:\Folder", "Folder", null, now, 1);
    True(EligibilityPolicy.CanOwnSpace(new(true, false, false, 42, 84, now), eligible, TimeSpan.FromMilliseconds(350)), "valid snapshot accepted");
    False(EligibilityPolicy.CanOwnSpace(new(true, false, false, 99, 84, now), eligible, TimeSpan.FromMilliseconds(350)), "other foreground rejected");
    False(EligibilityPolicy.CanOwnSpace(new(true, false, false, 42, 85, now), eligible, TimeSpan.FromMilliseconds(350)), "changed focus rejected");
    False(EligibilityPolicy.CanOwnSpace(new(true, true, false, 42, 84, now), eligible, TimeSpan.FromMilliseconds(350)), "injected input rejected");
    False(EligibilityPolicy.CanOwnSpace(new(true, false, true, 42, 84, now), eligible, TimeSpan.FromMilliseconds(350)), "modifiers rejected");
    False(EligibilityPolicy.CanOwnSpace(new(false, false, false, 42, 84, now), eligible, TimeSpan.FromMilliseconds(350)), "disabled rejected");
    False(EligibilityPolicy.CanOwnSpace(new(true, false, false, 42, 84, now.AddSeconds(1)), eligible, TimeSpan.FromMilliseconds(350)), "stale snapshot rejected");
    False(EligibilityPolicy.CanOwnSpace(new(true, false, false, 42, 84, now), eligible with { IsEligible = false }, TimeSpan.FromMilliseconds(350)), "focus/search rejection respected");
    False(EligibilityPolicy.CanOwnSpace(new(true, false, false, 42, 84, now), null, TimeSpan.FromMilliseconds(350)), "missing snapshot rejected");
    return Task.CompletedTask;
}

static Task TestHoverPreview()
{
    var machine = new HoverPreviewStateMachine();
    var start = DateTimeOffset.UtcNow;
    var point = new HoverPoint(120, 240);
    var dwell = TimeSpan.FromMilliseconds(650);
    Equal(HoverPhase.Dwelling, machine.ObserveCandidate(point, start, 6, dwell).Phase, "first candidate begins dwell");
    Equal(HoverAction.None, machine.ObserveCandidate(new(124, 242), start.AddMilliseconds(649), 6, dwell).Action,
        "small movement below dwell threshold does not resolve");
    var resolve = machine.ObserveCandidate(new(124, 242), start.AddMilliseconds(650), 6, dwell);
    Equal(HoverAction.Resolve, resolve.Action, "stable dwell resolves at configured threshold");
    Equal(HoverAction.None, machine.Resolved(resolve.Generation + 1, true).Action, "stale resolver result is ignored");
    Equal(HoverAction.Open, machine.Resolved(resolve.Generation, true).Action, "current eligible result opens");
    Equal(HoverPhase.ClosingGrace, machine.ObserveOpen(false, start.AddMilliseconds(700), TimeSpan.FromMilliseconds(250)).Phase,
        "leaving source begins close grace");
    Equal(HoverPhase.Open, machine.ObserveOpen(true, start.AddMilliseconds(800), TimeSpan.FromMilliseconds(250)).Phase,
        "moving into preview cancels close grace");
    machine.ObserveOpen(false, start.AddMilliseconds(900), TimeSpan.FromMilliseconds(250));
    Equal(HoverAction.Close, machine.ObserveOpen(false, start.AddMilliseconds(1150), TimeSpan.FromMilliseconds(250)).Action,
        "preview closes after configured grace");

    machine = new HoverPreviewStateMachine();
    var first = machine.ObserveCandidate(point, start, 6, dwell);
    var moved = machine.ObserveCandidate(new(127, 240), start.AddMilliseconds(640), 6, dwell);
    True(moved.Generation > first.Generation, "movement beyond tolerance restarts dwell generation");
    Equal(HoverAction.None, machine.ObserveCandidate(new(127, 240), start.AddMilliseconds(650), 6, dwell).Action,
        "movement restart prevents premature resolve");

    machine = new HoverPreviewStateMachine();
    machine.ObserveCandidate(point, start, 6, dwell);
    var rejectedResolve = machine.ObserveCandidate(point, start.Add(dwell), 6, dwell);
    Equal(HoverPhase.Rejected, machine.Resolved(rejectedResolve.Generation, false).Phase, "failed resolution is negatively cached");
    Equal(HoverAction.None, machine.ObserveCandidate(point, start.AddSeconds(5), 6, dwell).Action,
        "stationary rejected target is not resolved repeatedly");
    Equal(HoverPhase.Dwelling, machine.ObserveCandidate(new(127, 240), start.AddSeconds(5), 6, dwell).Phase,
        "moving beyond tolerance clears the negative cache");
    var idleGeneration = new HoverPreviewStateMachine().Generation;
    var idleCancel = new HoverPreviewStateMachine().Cancel();
    Equal(idleGeneration, idleCancel.Generation, "idle cancellation is allocation-free state churn");

    True(HoverEligibilityPolicy.IsModifierMatch(HoverModifier.None, false, false, false, false), "no-modifier mode accepts clean hover");
    False(HoverEligibilityPolicy.IsModifierMatch(HoverModifier.None, true, false, false, false), "no-modifier mode rejects Ctrl");
    True(HoverEligibilityPolicy.IsModifierMatch(HoverModifier.Control, true, false, false, false), "Ctrl mode accepts exact Ctrl");
    False(HoverEligibilityPolicy.IsModifierMatch(HoverModifier.Control, true, true, false, false), "Ctrl mode rejects extra Shift");
    True(HoverEligibilityPolicy.IsModifierMatch(HoverModifier.Shift, false, true, false, false), "Shift mode accepts exact Shift");
    False(HoverEligibilityPolicy.IsModifierMatch(HoverModifier.Shift, false, true, true, false), "all modes reject Alt");
    True(HoverEligibilityPolicy.CanSample(true, HoverPreviewMode.AnyFolder, true, false),
        "visible control center does not independently disable Explorer hover sampling");
    False(HoverEligibilityPolicy.CanSample(true, HoverPreviewMode.Off, true, false), "off mode has zero sampling");
    False(HoverEligibilityPolicy.CanSample(false, HoverPreviewMode.AnyFolder, true, false), "disabled app does not sample");
    False(HoverEligibilityPolicy.CanSample(true, HoverPreviewMode.AnyFolder, false, false), "keyboard ownership preempts hover");
    False(HoverEligibilityPolicy.CanSample(true, HoverPreviewMode.AnyFolder, true, true), "item activation preempts hover");

    var bounds = new PixelRect(100, 200, 300, 280);
    var snapshot = new ExplorerSnapshot(true, "Eligible", 42, 84, 7, @"C:\Folder", "Folder", bounds, start, 1);
    True(HoverEligibilityPolicy.CanUseSelectedSnapshot(snapshot, 42, point, start.AddMilliseconds(100), TimeSpan.FromMilliseconds(350)),
        "fresh selected folder under pointer is accepted");
    False(HoverEligibilityPolicy.CanUseSelectedSnapshot(snapshot, 99, point, start, TimeSpan.FromMilliseconds(350)),
        "other foreground window is rejected");
    False(HoverEligibilityPolicy.CanUseSelectedSnapshot(snapshot, 42, new(301, 240), start, TimeSpan.FromMilliseconds(350)),
        "pointer outside selected row is rejected");
    False(HoverEligibilityPolicy.CanUseSelectedSnapshot(snapshot, 42, point, start.AddSeconds(1), TimeSpan.FromMilliseconds(350)),
        "stale selection is rejected");
    False(HoverEligibilityPolicy.CanUseSelectedSnapshot(snapshot with { ItemBounds = null }, 42, point, start, TimeSpan.FromMilliseconds(350)),
        "missing item bounds fail closed");

    var steady = new HoverPreviewStateMachine();
    steady.ObserveCandidate(point, start, 6, dwell);
    var steadyResolve = steady.ObserveCandidate(point, start.Add(dwell), 6, dwell);
    steady.Resolved(steadyResolve.Generation, false);
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var index = 0; index < 100_000; index++)
        steady.ObserveCandidate(point, start.AddSeconds(2), 6, dwell);
    var steadyAllocations = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    True(steadyAllocations <= 1024, "steady rejected-target sampling remains effectively allocation-free");

    var promotable = new HoverPreviewStateMachine();
    promotable.ObserveCandidate(point, start, 6, TimeSpan.Zero);
    var promotableResolve = promotable.ObserveCandidate(point, start, 6, TimeSpan.Zero);
    promotable.Resolved(promotableResolve.Generation, true);
    var promotion = promotable.Promote();
    Equal(HoverAction.Promote, promotion.Action, "open hover can be deliberately promoted");
    Equal(HoverPhase.Idle, promotion.Phase, "promotion releases hover ownership without closing the popup");
    Equal(HoverAction.None, promotable.Promote().Action, "idle hover cannot be promoted twice");
    return Task.CompletedTask;
}

static Task TestHoverSamplingPolicy()
{
    var explorer = new nint(42);
    True(HoverSamplingPolicy.ShouldSample(true, HoverPreviewMode.AnyFolder, explorer, explorer),
        "enabled hover samples while Explorer is foreground");
    True(HoverSamplingPolicy.ShouldSample(true, HoverPreviewMode.SelectedFolder, explorer, explorer),
        "selected-folder hover samples while Explorer is foreground");
    False(HoverSamplingPolicy.ShouldSample(false, HoverPreviewMode.AnyFolder, explorer, explorer),
        "disabled app does not sample hover");
    False(HoverSamplingPolicy.ShouldSample(true, HoverPreviewMode.Off, explorer, explorer),
        "off mode does not sample hover");
    False(HoverSamplingPolicy.ShouldSample(true, HoverPreviewMode.AnyFolder, new nint(7), explorer),
        "a background Explorer window does not keep the sampler awake");
    False(HoverSamplingPolicy.ShouldSample(true, HoverPreviewMode.AnyFolder, explorer, 0),
        "an unknown Explorer context does not sample");
    True(HoverSamplingPolicy.ShouldSample(true, HoverPreviewMode.Off, MouseTriggerOptions.MiddleClick, explorer, explorer),
        "an enabled mouse shortcut keeps pointer targeting active when hover is off");
    return Task.CompletedTask;
}

static Task TestMouseTriggers()
{
    var now = DateTimeOffset.UtcNow;
    var point = new HoverPoint(120, 220);
    var target = new ExplorerSnapshot(true, "Pointer target", 42, 84, 7, @"C:\Folder", "Folder",
        new PixelRect(100, 200, 300, 280), now, 1);
    MouseTriggerInput Input(MouseTriggerButton button, bool control = false, bool shift = false, bool injected = false,
        HoverPoint? at = null, nint? foreground = null, DateTimeOffset? time = null) =>
        new(button, at ?? point, control, shift, false, false, injected, foreground ?? 42, time ?? now);

    Equal(MouseTriggerOptions.MiddleClick, MouseTriggerPolicy.Match(Input(MouseTriggerButton.Middle)),
        "clean middle click maps to its shortcut");
    Equal(MouseTriggerOptions.ControlLeftClick, MouseTriggerPolicy.Match(Input(MouseTriggerButton.Left, control: true)),
        "exact Ctrl-left maps to its shortcut");
    Equal(MouseTriggerOptions.ControlRightClick, MouseTriggerPolicy.Match(Input(MouseTriggerButton.Right, control: true)),
        "exact Ctrl-right maps to its shortcut");
    Equal(MouseTriggerOptions.None, MouseTriggerPolicy.Match(Input(MouseTriggerButton.Left)), "ordinary left click always passes");
    Equal(MouseTriggerOptions.None, MouseTriggerPolicy.Match(Input(MouseTriggerButton.Right, control: true, shift: true)),
        "extra modifiers fail open");
    Equal(MouseTriggerOptions.None, MouseTriggerPolicy.Match(Input(MouseTriggerButton.Middle, injected: true)),
        "injected mouse input fails open");
    True(MouseTriggerPolicy.CanCapture(MouseTriggerOptions.MiddleClick, Input(MouseTriggerButton.Middle), target,
        TimeSpan.FromMilliseconds(1500)), "fresh configured folder target is captured");
    True(MouseTriggerPolicy.CanCapture(MouseTriggerOptions.MiddleClick | MouseTriggerOptions.ControlLeftClick,
        Input(MouseTriggerButton.Left, control: true), target, TimeSpan.FromMilliseconds(1500)),
        "combined shortcut settings preserve each exact gesture");
    True(MouseTriggerPolicy.IsFreshTargetAtPoint(target, point, 42, now, TimeSpan.FromMilliseconds(1500)),
        "fresh pointer targets can be safely reused by hover resolution");
    False(MouseTriggerPolicy.CanCapture(MouseTriggerOptions.ControlLeftClick, Input(MouseTriggerButton.Middle), target,
        TimeSpan.FromMilliseconds(1500)), "unconfigured gesture passes");
    False(MouseTriggerPolicy.CanCapture(MouseTriggerOptions.MiddleClick, Input(MouseTriggerButton.Middle, at: new(301, 220)), target,
        TimeSpan.FromMilliseconds(1500)), "point outside cached item passes");
    False(MouseTriggerPolicy.CanCapture(MouseTriggerOptions.MiddleClick,
        Input(MouseTriggerButton.Middle, time: now.AddSeconds(2)), target, TimeSpan.FromMilliseconds(1500)),
        "stale target passes");

    var cache = new PointerTargetCacheStateMachine();
    Equal(PointerTargetAction.Clear, cache.Observe(point, now, 6, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(1)).Action,
        "new pointer candidate clears stale cache");
    Equal(PointerTargetAction.None, cache.Observe(point, now.AddMilliseconds(149), 6,
        TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(1)).Action, "prefetch waits for stability");
    var resolve = cache.Observe(point, now.AddMilliseconds(150), 6, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(1));
    Equal(PointerTargetAction.Resolve, resolve.Action, "stable pointer requests target resolution");
    Equal(PointerTargetPhase.Resolving, cache.Resolved(resolve.Generation - 1, true, now.AddMilliseconds(170)).Phase,
        "a stale resolver generation cannot publish over the current target");
    Equal(PointerTargetPhase.Ready, cache.Resolved(resolve.Generation, true, now.AddMilliseconds(180)).Phase,
        "eligible target becomes ready");
    Equal(PointerTargetAction.Resolve, cache.Observe(point, now.AddMilliseconds(1180), 6,
        TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(1)).Action, "stationary target refreshes before it goes stale");
    Equal(PointerTargetAction.Clear, cache.Observe(new(130, 220), now.AddMilliseconds(1200), 6,
        TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(1)).Action, "meaningful movement invalidates target immediately");
    return Task.CompletedTask;
}

static Task TestSettingsScroll()
{
    Equal(-56d, SettingsScrollPolicy.OffsetDelta(120), "one wheel notch scrolls up one controlled step");
    Equal(56d, SettingsScrollPolicy.OffsetDelta(-120), "one wheel notch scrolls down one controlled step");
    Equal(-28d, SettingsScrollPolicy.OffsetDelta(60), "high-resolution half-notch input stays proportional");
    Equal(0d, SettingsScrollPolicy.OffsetDelta(0), "zero wheel delta does not move content");
    return Task.CompletedTask;
}

static Task TestHoverElementPolicy()
{
    const int explorerPid = 1200;
    var explorerWindow = new nint(2400);
    HoverElementNode[] itemTree =
    [
        new(explorerPid, 0, false, false, true),
        new(explorerPid, 0, false, true, false),
        new(explorerPid, explorerWindow, false, false, false)
    ];
    True(HoverElementPolicy.Assess(itemTree, explorerPid, explorerWindow).IsEligible,
        "file-list item inside foreground Explorer is accepted");
    False(HoverElementPolicy.Assess([
        new(explorerPid, 0, true, false, true),
        new(explorerPid, explorerWindow, false, true, false)
    ], explorerPid, explorerWindow).IsEligible, "menu and tree surfaces fail closed");
    True(HoverElementPolicy.Assess([
        new(explorerPid, 0, false, false, false),
        new(explorerPid, 0, false, false, true),
        new(explorerPid, 0, false, true, false),
        new(explorerPid, explorerWindow, false, false, false)
    ], explorerPid, explorerWindow).IsEligible, "Windows 11 read-only details cells inherit valid file-item ancestry");
    False(HoverElementPolicy.Assess([
        new(999, 0, false, false, true),
        new(explorerPid, explorerWindow, false, true, false)
    ], explorerPid, explorerWindow).IsEligible, "foreign-process ancestry fails closed");
    False(HoverElementPolicy.Assess([
        new(explorerPid, 0, false, false, true),
        new(explorerPid, explorerWindow, false, false, false)
    ], explorerPid, explorerWindow).IsEligible, "navigation/details items without ItemsView fail closed");
    False(HoverElementPolicy.Assess([
        new(explorerPid, 0, false, true, false),
        new(explorerPid, explorerWindow, false, false, false)
    ], explorerPid, explorerWindow).IsEligible, "blank file-list space fails closed");
    False(HoverElementPolicy.Assess(itemTree[..2], explorerPid, explorerWindow).IsEligible,
        "ancestry that never reaches foreground frame fails closed");
    return Task.CompletedTask;
}

static Task TestExplorerFocusPolicy()
{
    const int explorerPid = 10256;
    var explorerWindow = new nint(2099170);
    ExplorerFocusNode[] windows11Tree =
    [
        new(explorerPid, 0, false, false, true),                 // UIItem
        new(explorerPid, new nint(2163156), false, true, false), // UIItemsView
        new(explorerPid, new nint(458798), false, false, false), // Shell Folder View
        new(explorerPid, explorerWindow, false, false, false),
        new(3752, new nint(65548), false, false, false)           // desktop root: must be ignored
    ];
    True(ExplorerFocusPolicy.Assess(windows11Tree, explorerPid, explorerWindow).IsEligible,
        "desktop root after Explorer window does not invalidate focus");

    var searchTree = new[]
    {
        new ExplorerFocusNode(explorerPid, 0, true, false, false),
        new ExplorerFocusNode(explorerPid, explorerWindow, false, false, false)
    };
    False(ExplorerFocusPolicy.Assess(searchTree, explorerPid, explorerWindow).IsEligible,
        "search/edit focus is rejected");

    var foreignBeforeWindow = new[]
    {
        new ExplorerFocusNode(9000, 0, false, true, true),
        new ExplorerFocusNode(explorerPid, explorerWindow, false, false, false)
    };
    False(ExplorerFocusPolicy.Assess(foreignBeforeWindow, explorerPid, explorerWindow).IsEligible,
        "foreign process before Explorer window is rejected");
    return Task.CompletedTask;
}

static async Task TestEnumeration()
{
    var root = Path.Combine(Path.GetTempPath(), "FolderGlimpse.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var inspector = new FolderInspector();
        var empty = await inspector.InspectAsync(root, 10, CancellationToken.None);
        Equal(0, empty.Entries.Count, "empty directory");
        Directory.CreateDirectory(Path.Combine(root, "FolderB"));
        Directory.CreateDirectory(Path.Combine(root, "FolderA"));
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "hello");
        var ordinary = await inspector.InspectAsync(root, 10, CancellationToken.None);
        Equal(3, ordinary.Entries.Count, "ordinary item count");
        True(ordinary.Entries[0].IsDirectory && ordinary.Entries[1].IsDirectory, "directories first");
        Equal("FolderA", ordinary.Entries[0].Name, "directories sorted");
        Equal(5L, ordinary.Entries[2].Size, "file size read");

        for (var index = 0; index < 30; index++) await File.WriteAllTextAsync(Path.Combine(root, $"file-{index:00}.txt"), "x");
        var capped = await inspector.InspectAsync(root, 7, CancellationToken.None);
        Equal(7, capped.Entries.Count, "large directory capped");
        True(capped.HasMore, "large directory reports more");

        var globallySorted = await inspector.InspectAsync(root, new FolderInspectionOptions(ItemLimit: 20), CancellationToken.None);
        Equal("FolderA", globallySorted.Entries[0].Name, "limit is applied after global sort");
        var newestPath = Path.Combine(root, "newest.zzz");
        await File.WriteAllTextAsync(newestPath, "new");
        File.SetLastWriteTimeUtc(newestPath, DateTime.UtcNow.AddMinutes(2));
        var modified = await inspector.InspectAsync(root, new FolderInspectionOptions(SortMode: SortMode.ModifiedDate, FoldersFirst: false, ItemLimit: 20), CancellationToken.None);
        Equal("newest.zzz", modified.Entries[0].Name, "modified sort is newest first");

        var hiddenPath = Path.Combine(root, "hidden.txt");
        await File.WriteAllTextAsync(hiddenPath, "secret");
        File.SetAttributes(hiddenPath, File.GetAttributes(hiddenPath) | FileAttributes.Hidden);
        var hiddenOff = await inspector.InspectAsync(root, new FolderInspectionOptions(ItemLimit: null), CancellationToken.None);
        False(hiddenOff.Entries.Any(x => x.Name == "hidden.txt"), "hidden entries excluded by default");
        var hiddenOn = await inspector.InspectAsync(root, new FolderInspectionOptions(ShowHiddenFiles: true, ItemLimit: null), CancellationToken.None);
        True(hiddenOn.Entries.Any(x => x.Name == "hidden.txt"), "hidden entries included when enabled");
        File.SetAttributes(hiddenPath, FileAttributes.Normal);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => inspector.InspectAsync(root, 10, canceled.Token), "pre-cancelled inspection");
        var missing = await inspector.InspectAsync(Path.Combine(root, "deleted"), 10, CancellationToken.None);
        True(missing.Error is not null, "deleted directory is an error result");
    }
    finally { Directory.Delete(root, true); }
}

static Task TestSettings()
{
    var root = Path.Combine(Path.GetTempPath(), "FolderGlimpse.Settings.Tests", Guid.NewGuid().ToString("N"));
    var path = Path.Combine(root, "settings.json");
    try
    {
        var service = new JsonSettingsService(path);
        service.Load();
        True(File.Exists(path), "missing settings file is created");
        Equal(430d, service.Current.PopupWidth, "defaults loaded");
        True(service.Current.InteractiveItems, "interaction defaults on");
        True(service.Current.DoubleClickFilesToOpen && service.Current.DoubleClickFoldersToOpen, "double-click defaults on");
        True(service.Current.RightClickActions && service.Current.MultiSelection, "selection actions default on");
        False(service.Current.ShowSelectionCheckboxes, "selection checkboxes default off");
        True(service.Current.AllowOpeningMultipleItems && service.Current.ClosePreviewAfterOpening, "safe opening defaults on");
        Equal(5, service.Current.ConfirmBeforeOpeningMoreThan, "confirmation default is five");
        Equal(HoverPreviewMode.AnyFolder, service.Current.HoverMode, "any-folder hover is enabled by default");
        Equal(TriggerHotkey.Space, service.Current.Hotkey, "Space remains the default keyboard trigger");
        Equal(650, service.Current.HoverOpenDelayMs, "hover delay has a conservative default");
        Equal(250, service.Current.HoverCloseDelayMs, "hover close grace has a usable default");
        Equal(6, service.Current.HoverMovementTolerancePx, "hover movement tolerance defaults to six pixels");
        Equal(HoverModifier.None, service.Current.HoverModifier, "hover requires no modifier by default");
        Equal(MouseTriggerOptions.None, service.Current.MouseTriggers, "mouse shortcuts are opt-in by default");
        True(service.TryUpdate(s => s with { Theme = ThemePreference.Dark, PopupWidth = 612, InitialItemLimit = 100,
            HoverMode = HoverPreviewMode.AnyFolder, HoverModifier = HoverModifier.Control,
            MouseTriggers = MouseTriggerOptions.MiddleClick | MouseTriggerOptions.ControlRightClick,
            HoverOpenDelayMs = 900, HoverCloseDelayMs = 400, HoverMovementTolerancePx = 10,
            InteractiveItems = false, DoubleClickFilesToOpen = false, DoubleClickFoldersToOpen = false,
            RightClickActions = false, MultiSelection = false, ShowSelectionCheckboxes = true,
            AllowOpeningMultipleItems = false, ConfirmBeforeOpeningMoreThan = 12, ClosePreviewAfterOpening = false }, out _), "settings update persists");
        var reloaded = new JsonSettingsService(path); reloaded.Load();
        Equal(ThemePreference.Dark, reloaded.Current.Theme, "enum round trips");
        Equal(612d, reloaded.Current.PopupWidth, "number round trips");
        True(reloaded.Current.ShowSelectionCheckboxes, "interaction boolean round trips");
        False(reloaded.Current.AllowOpeningMultipleItems, "multi-open setting round trips");
        Equal(12, reloaded.Current.ConfirmBeforeOpeningMoreThan, "confirmation threshold round trips");
        False(reloaded.Current.InteractiveItems, "interactive-items setting round trips");
        False(reloaded.Current.DoubleClickFilesToOpen, "file activation setting round trips");
        False(reloaded.Current.DoubleClickFoldersToOpen, "folder activation setting round trips");
        False(reloaded.Current.RightClickActions, "right-click setting round trips");
        False(reloaded.Current.MultiSelection, "multi-selection setting round trips");
        False(reloaded.Current.ClosePreviewAfterOpening, "close-after-open setting round trips");
        Equal(HoverPreviewMode.AnyFolder, reloaded.Current.HoverMode, "hover mode round trips");
        Equal(HoverModifier.Control, reloaded.Current.HoverModifier, "hover modifier round trips");
        Equal(900, reloaded.Current.HoverOpenDelayMs, "hover open delay round trips");
        Equal(400, reloaded.Current.HoverCloseDelayMs, "hover close delay round trips");
        Equal(10, reloaded.Current.HoverMovementTolerancePx, "hover movement tolerance round trips");
        Equal(MouseTriggerOptions.MiddleClick | MouseTriggerOptions.ControlRightClick, reloaded.Current.MouseTriggers,
            "combined mouse shortcuts round trip");

        File.WriteAllText(path, "{ \"showFileSize\": false, \"unknownFutureField\": 123 }");
        reloaded.Load();
        False(reloaded.Current.ShowFileSize, "partial boolean is retained");
        Equal(430d, reloaded.Current.PopupWidth, "missing partial values use defaults");

        File.WriteAllText(path, "{ not-json");
        reloaded.Load();
        Equal(FolderGlimpseSettings.Default, reloaded.Current, "malformed settings recover to defaults");
        True(reloaded.TryUpdate(s => s with { PopupWidth = 9999, HoldThresholdMs = 1, InitialItemLimit = 17,
            HoverOpenDelayMs = 1, HoverCloseDelayMs = 9000, HoverMovementTolerancePx = 100,
            HoverMode = (HoverPreviewMode)999, HoverModifier = (HoverModifier)999,
            MouseTriggers = (MouseTriggerOptions)128,
            ConfirmBeforeOpeningMoreThan = 500 }, out _), "invalid values normalize");
        Equal(700d, reloaded.Current.PopupWidth, "width clamps high");
        Equal(100, reloaded.Current.HoldThresholdMs, "hold delay clamps low");
        Equal(50, reloaded.Current.InitialItemLimit, "unsupported item limit resets");
        Equal(50, reloaded.Current.ConfirmBeforeOpeningMoreThan, "confirmation threshold clamps high");
        Equal(150, reloaded.Current.HoverOpenDelayMs, "hover open delay clamps low");
        Equal(1000, reloaded.Current.HoverCloseDelayMs, "hover close delay clamps high");
        Equal(16, reloaded.Current.HoverMovementTolerancePx, "hover tolerance clamps high");
        Equal(HoverPreviewMode.AnyFolder, reloaded.Current.HoverMode, "invalid hover mode falls back to the product default");
        Equal(HoverModifier.None, reloaded.Current.HoverModifier, "invalid hover modifier fails to none");
        Equal(MouseTriggerOptions.None, reloaded.Current.MouseTriggers, "unknown mouse trigger flags fail open to none");
        True(reloaded.TryUpdate(s => s with { ConfirmBeforeOpeningMoreThan = 1 }, out _), "low confirmation threshold normalizes");
        Equal(2, reloaded.Current.ConfirmBeforeOpeningMoreThan, "confirmation threshold clamps low");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task TestSettingsMigration()
{
    var root = Path.Combine(Path.GetTempPath(), "FolderGlimpse.Migration.Tests", Guid.NewGuid().ToString("N"));
    var legacyPath = Path.Combine(root, "legacy", "settings.json");
    var currentPath = Path.Combine(root, "current", "settings.json");
    try
    {
        var defaults = new JsonSettingsService(currentPath);
        False(SettingsPathMigration.TryMigrate(legacyPath, currentPath), "missing legacy settings do not migrate");
        defaults.Load();
        Equal(FolderGlimpseSettings.Default, defaults.Current, "no old or new settings uses defaults");

        Directory.Delete(root, true);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "{ \"theme\": \"Dark\", \"popupWidth\": 588 }");
        True(SettingsPathMigration.TryMigrate(legacyPath, currentPath), "legacy settings migrate when current settings are absent");
        True(File.Exists(legacyPath), "legacy settings remain after successful copy");
        var migrated = new JsonSettingsService(currentPath); migrated.Load();
        Equal(ThemePreference.Dark, migrated.Current.Theme, "migrated theme is preserved");
        Equal(588d, migrated.Current.PopupWidth, "migrated dimensions are preserved");

        File.WriteAllText(legacyPath, "{ \"theme\": \"Light\" }");
        File.WriteAllText(currentPath, "{ \"theme\": \"Dark\" }");
        False(SettingsPathMigration.TryMigrate(legacyPath, currentPath), "current settings are never overwritten");
        var currentWins = new JsonSettingsService(currentPath); currentWins.Load();
        Equal(ThemePreference.Dark, currentWins.Current.Theme, "current FolderGlimpse settings win");

        File.Delete(currentPath);
        File.WriteAllText(legacyPath, "{ malformed");
        True(SettingsPathMigration.TryMigrate(legacyPath, currentPath), "malformed legacy file can still be copied safely");
        var recovered = new JsonSettingsService(currentPath); recovered.Load();
        Equal(FolderGlimpseSettings.Default, recovered.Current, "malformed legacy settings recover to defaults");
        True(File.Exists(currentPath), "recovered settings are healed at the current location");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task TestStartupMigration()
{
    var store = new FakeStartupValueStore();
    var executable = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FolderGlimpse.exe"));
    // Legacy FolderPeek executable values appear only to exercise backward compatibility.
    store.Values[StartupRegistrationMigration.LegacyValueName] = "\"C:\\Old\\FolderPeek.exe\"";
    True(StartupRegistrationMigration.TryMigrate(store, executable), "legacy startup registration migrates");
    Equal($"\"{executable}\"", store.Values[StartupRegistrationMigration.CurrentValueName], "new registration targets current executable");
    False(store.Values.ContainsKey(StartupRegistrationMigration.LegacyValueName), "legacy registration is removed after replacement");

    store.Values[StartupRegistrationMigration.CurrentValueName] = "\"C:\\Newer\\FolderGlimpse.exe\"";
    store.Values[StartupRegistrationMigration.LegacyValueName] = "\"C:\\Old\\FolderPeek.exe\"";
    False(StartupRegistrationMigration.TryMigrate(store, executable), "existing current registration is not overwritten");
    Equal("\"C:\\Newer\\FolderGlimpse.exe\"", store.Values[StartupRegistrationMigration.CurrentValueName], "existing current registration wins");
    False(store.Values.ContainsKey(StartupRegistrationMigration.LegacyValueName), "duplicate legacy registration is removed");
    return Task.CompletedTask;
}

static Task TestApplicationLifecycle()
{
    Equal(LaunchIntentKind.Normal, LaunchIntent.Parse([]).Kind, "no arguments is a normal launch");
    Equal(LaunchIntentKind.Startup, LaunchIntent.Parse(["--STARTUP"]).Kind, "startup parsing is case-insensitive");
    Equal(LaunchIntentKind.Startup, LaunchIntent.Parse(["--settings", "--startup"]).Kind, "startup stays silent even with a deep link");
    Equal(LaunchIntentKind.Settings, LaunchIntent.Parse(["--settings"]).Kind, "settings argument deep-links");
    Equal(LaunchIntentKind.About, LaunchIntent.Parse(["--about"]).Kind, "about argument deep-links");
    Equal(LaunchIntentKind.Capture, LaunchIntent.Parse(["--capture-main=screen.png", "--startup"]).Kind, "capture has isolated precedence");
    Equal(LaunchIntentKind.Capture, LaunchIntent.Parse(["--capture-welcome=screen.png"]).Kind, "welcome capture is isolated");

    Equal(InitialSurface.Welcome, InitialSurfacePolicy.Decide(new(LaunchIntentKind.Normal), false), "first normal launch welcomes");
    Equal(InitialSurface.Home, InitialSurfacePolicy.Decide(new(LaunchIntentKind.Normal), true), "returning normal launch opens Home");
    Equal(InitialSurface.None, InitialSurfacePolicy.Decide(new(LaunchIntentKind.Startup), false), "startup with incomplete onboarding stays silent");
    Equal(InitialSurface.None, InitialSurfacePolicy.Decide(new(LaunchIntentKind.Startup), true), "startup after onboarding stays silent");
    Equal(InitialSurface.Settings, InitialSurfacePolicy.Decide(new(LaunchIntentKind.Settings), false), "explicit settings opens settings");
    Equal(InitialSurface.About, InitialSurfacePolicy.Decide(new(LaunchIntentKind.About), false), "explicit about opens about");
    Equal(ActivationRequest.OpenDefault, LaunchIntent.Parse([]).ActivationRequest, "normal second launch requests default surface");
    Equal(null, LaunchIntent.Parse(["--startup"]).ActivationRequest, "startup second launch never activates UI");
    True(ActivationRequestCodec.TryDecode(ActivationRequestCodec.Encode(ActivationRequest.Settings), out var decoded) && decoded == ActivationRequest.Settings,
        "activation request round trips");
    False(ActivationRequestCodec.TryDecode(255, out _), "unknown activation request is rejected");
    var navigation = new ShellNavigationModel();
    navigation.Navigate(ShellSection.Settings); navigation.Navigate(ShellSection.HowToUse); navigation.Navigate(ShellSection.About); navigation.Navigate(ShellSection.Home);
    Equal(ShellSection.Home, navigation.Current, "navigation supports Home -> Settings -> How to Use -> About -> Home");

    var executable = Path.Combine(Path.GetTempPath(), "Folder Glimpse", "FolderGlimpse.exe");
    var command = StartupCommand.Build(executable);
    Equal($"\"{Path.GetFullPath(executable)}\" --startup", command, "startup command is quoted and explicit");
    True(StartupCommand.IsCanonicalFor(command, executable), "canonical startup command matches executable");
    True(StartupCommand.IsPathOnlyFor($"\"{Path.GetFullPath(executable)}\"", executable), "old path-only command is recognized for upgrade");
    False(StartupCommand.IsCanonicalFor($"\"{Path.GetFullPath(executable)}\"", executable), "path-only command is not silently treated as canonical");
    False(StartupCommand.IsCanonicalFor("\"C:\\Other\\FolderGlimpse.exe\" --startup", executable), "another installation is not a match");

    var root = Path.Combine(Path.GetTempPath(), "FolderGlimpse.AppState.Tests", Guid.NewGuid().ToString("N"));
    var path = Path.Combine(root, "state.json");
    try
    {
        var state = new JsonApplicationStateService(path);
        state.Load();
        False(state.Current.HasCompletedOnboarding, "missing state is first run");
        True(state.TryUpdate(value => value with { HasCompletedOnboarding = true }, out _), "Get Started persists onboarding");
        var reloaded = new JsonApplicationStateService(path); reloaded.Load();
        True(reloaded.Current.HasCompletedOnboarding, "subsequent launch retains onboarding completion");

        File.WriteAllText(path, "{ malformed");
        reloaded.Load();
        False(reloaded.Current.HasCompletedOnboarding, "malformed state safely returns to Welcome");

        True(reloaded.TryUpdate(value => value with { HasCompletedOnboarding = true }, out _), "state recovers after malformed data");
        var preferences = new JsonSettingsService(Path.Combine(root, "settings.json"));
        preferences.Load();
        preferences.TryResetDefaults(out _);
        var stillComplete = new JsonApplicationStateService(path); stillComplete.Load();
        True(stillComplete.Current.HasCompletedOnboarding, "preference reset does not reset onboarding");

        var blocker = Path.Combine(root, "not-a-directory");
        File.WriteAllText(blocker, "block writes");
        var failingState = new JsonApplicationStateService(Path.Combine(blocker, "state.json"));
        failingState.Load();
        False(failingState.TryUpdate(value => value with { HasCompletedOnboarding = true }, out var saveError), "failed state persistence is reported");
        False(failingState.Current.HasCompletedOnboarding, "failed Get Started persistence does not claim completion");
        True(saveError is not null, "failed state persistence explains the error");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task TestProductIdentity()
{
    var assembly = typeof(FolderGlimpseSettings).Assembly;
    Equal("FolderGlimpse.Core", assembly.GetName().Name, "core assembly uses canonical product identity");
    Equal("FolderGlimpseSettings", nameof(FolderGlimpseSettings), "settings type uses canonical product identity");
    Equal("FolderGlimpse", assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyProductAttribute), false)
        .Cast<System.Reflection.AssemblyProductAttribute>().Single().Product, "assembly product uses canonical identity");
    Equal("Glance inside folders without opening them, and so much more.",
        assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyDescriptionAttribute), false)
            .Cast<System.Reflection.AssemblyDescriptionAttribute>().Single().Description, "assembly description uses canonical tagline");
    return Task.CompletedTask;
}

static Task TestSelection()
{
    var model = new SelectionModel();
    model.Select(0, 5);
    Sequence([0], model.SelectedIndices, "single click A");
    model.Select(1, 5);
    Sequence([1], model.SelectedIndices, "single click B replaces A");
    model.Select(0, 5, control: true);
    model.Select(1, 5, control: true);
    Sequence([0], model.SelectedIndices, "Ctrl-click toggles B off while retaining A");
    model.Select(1, 5, control: true);
    Sequence([0, 1], model.SelectedIndices, "Ctrl-click adds B");
    model.Select(0, 5, control: true);
    Sequence([1], model.SelectedIndices, "Ctrl-click A again removes it");

    model.Select(0, 5);
    model.Select(3, 5, shift: true);
    Sequence([0, 1, 2, 3], model.SelectedIndices, "Shift selects contiguous anchor range");
    model.SelectAll(5, true);
    Sequence([0, 1, 2, 3, 4], model.SelectedIndices, "Ctrl+A selects all displayed items");
    model.Toggle(2, 5, true);
    Sequence([0, 1, 3, 4], model.SelectedIndices, "checkbox uses the same selection model");
    model.Select(2, 5, control: true, multiSelection: false);
    Sequence([2], model.SelectedIndices, "multi-selection disabled forces one item");
    model.Toggle(4, 5, false);
    Sequence([4], model.SelectedIndices, "checkbox cannot create multiple selection when disabled");
    Equal(4, model.Move(1, 5), "Down clamps at the last item");
    Sequence([4], model.SelectedIndices, "keyboard movement remains single selection");
    return Task.CompletedTask;
}

static async Task TestActivation()
{
    var root = Path.Combine(Path.GetTempPath(), "FolderGlimpse.Activation.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var fileA = Path.Combine(root, "file A.txt");
        var fileB = Path.Combine(root, "文档.txt");
        var folder = Path.Combine(root, "Folder");
        await File.WriteAllTextAsync(fileA, "a");
        await File.WriteAllTextAsync(fileB, "b");
        Directory.CreateDirectory(folder);
        var entries = new[] { Entry(fileA, false), Entry(fileB, false), Entry(folder, true) };
        var launcher = new FakeShellLauncher();
        var confirmation = new FakeConfirmation(true);
        var service = new ItemActivationService(launcher, confirmation);

        var immediate = await service.OpenAsync(entries, new(true, true, 5));
        Equal(3, immediate.RequestedCount, "three items open below threshold");
        False(immediate.ConfirmationRequested, "below threshold does not confirm");
        Sequence(["file:" + fileA, "file:" + fileB, "folder:" + folder], launcher.Requests, "mixed items use correct shell operations");

        launcher.Requests.Clear();
        var singleFolder = await service.OpenAsync([entries[2]], new(true, false, 5));
        Equal(1, singleFolder.RequestedCount, "single folder still opens when multi-open is disabled");
        Sequence(["folder:" + folder], launcher.Requests, "single folder uses folder shell activation");

        launcher.Requests.Clear();
        var exactThreshold = await service.OpenAsync(Enumerable.Repeat(entries[1], 5).ToArray(), new(true, true, 5));
        False(exactThreshold.ConfirmationRequested, "exact confirmation threshold opens without prompting");
        Equal(5, exactThreshold.RequestedCount, "exact threshold opens every requested item");

        launcher.Requests.Clear();
        var six = Enumerable.Repeat(entries[1], 6).ToArray();
        var confirmed = await service.OpenAsync(six, new(true, true, 5));
        True(confirmed.ConfirmationRequested && !confirmed.Cancelled, "six items over threshold five confirm first");
        Equal(6, confirmed.RequestedCount, "confirmation Open All requests every item");
        Equal(6, launcher.Requests.Count, "all launches happen after confirmation");

        launcher.Requests.Clear();
        var cancelled = await new ItemActivationService(launcher, new FakeConfirmation(false)).OpenAsync(entries, new(true, true, 2));
        True(cancelled.ConfirmationRequested && cancelled.Cancelled, "over threshold cancellation is reported");
        Equal(0, launcher.Requests.Count, "confirmation occurs before any launch");

        var blocked = await service.OpenAsync(entries, new(true, false, 5));
        Equal(0, blocked.RequestedCount, "multi-open disabled launches nothing");
        True(blocked.Error is not null, "multi-open disabled explains why");

        launcher.Requests.Clear();
        File.Delete(fileA);
        launcher.MissingPaths.Add(fileA);
        var missing = await service.OpenAsync([entries[0]], new());
        Equal(0, missing.RequestedCount, "missing file is skipped gracefully");
        Equal(0, launcher.Requests.Count, "missing file never reaches launcher");

        launcher.Requests.Clear();
        Directory.Delete(folder);
        launcher.MissingPaths.Add(folder);
        var missingFolder = await service.OpenAsync([entries[2]], new());
        Equal(0, missingFolder.RequestedCount, "missing folder is skipped gracefully");
        Equal(0, launcher.Requests.Count, "missing folder never reaches successful requests");

        launcher.Requests.Clear();
        var disabled = await service.OpenAsync([entries[1]], new(false, true, 5));
        Equal(0, disabled.RequestedCount, "interaction disabled prevents activation");
        Equal(0, launcher.Requests.Count, "interaction disabled never reaches launcher");

        launcher.FailingPaths.Add(fileB);
        await ThrowsAsync<InvalidOperationException>(() => service.OpenAsync([entries[1]], new()),
            "unexpected shell launch failures reach the UI error boundary");

        using var cancelledSource = new CancellationTokenSource();
        cancelledSource.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => service.OpenAsync([entries[1]], new(), cancelledSource.Token),
            "pre-cancelled activation does not invoke the shell");
    }
    finally { Directory.Delete(root, true); }
}

static Task TestPreviewInteractionRouting()
{
    var file = new FolderEntry("file.txt", @"C:\Root\file.txt", false, 1, DateTimeOffset.UtcNow);
    var folder = new FolderEntry("Folder", @"C:\Root\Folder", true, null, DateTimeOffset.UtcNow);
    var defaults = FolderGlimpseSettings.Default;

    False(ItemActionPolicy.CanHitTestEntries(PreviewInteractionMode.ViewOnly, defaults), "momentary/view-only previews ignore pointer input");
    True(ItemActionPolicy.CanHitTestEntries(PreviewInteractionMode.HoverPointer, defaults), "hover previews accept pointer input");
    True(ItemActionPolicy.CanHitTestEntries(PreviewInteractionMode.Sticky, defaults), "sticky previews accept pointer input");
    False(ItemActionPolicy.CanSelect(PreviewInteractionMode.HoverPointer, defaults), "hover click does not steal selection or keyboard focus");
    True(ItemActionPolicy.CanSelect(PreviewInteractionMode.Sticky, defaults), "sticky preview supports selection");
    True(ItemActionPolicy.CanPromoteHover(PreviewInteractionMode.HoverPointer, defaults), "hover click may deliberately promote to sticky");
    False(ItemActionPolicy.CanPromoteHover(PreviewInteractionMode.ViewOnly, defaults), "momentary preview cannot be promoted");

    True(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.HoverPointer, folder, defaults), "hover double-click opens folders");
    True(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.HoverPointer, file, defaults), "hover double-click opens files");
    True(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.Sticky, folder, defaults), "sticky double-click opens folders");
    False(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.ViewOnly, folder, defaults), "view-only preview never activates folders");
    False(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.HoverPointer, folder,
        defaults with { DoubleClickFoldersToOpen = false }), "folder double-click setting applies to hover");
    False(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.HoverPointer, file,
        defaults with { DoubleClickFilesToOpen = false }), "file double-click setting applies to hover");

    var hoverFolderTargets = ItemActionPolicy.ActivationTargetsForDoubleClick(
        PreviewInteractionMode.HoverPointer, folder, defaults);
    Equal(1, hoverFolderTargets.Count, "hover double-click routes one exact entry");
    Equal(folder.FullPath, hoverFolderTargets[0].FullPath, "hover double-click routes the clicked folder, not a stale selection");
    Equal(0, ItemActionPolicy.ActivationTargetsForDoubleClick(
        PreviewInteractionMode.ViewOnly, folder, defaults).Count, "view-only double-click routes no target");
    Equal(0, ItemActionPolicy.ActivationTargetsForDoubleClick(
        PreviewInteractionMode.HoverPointer, file,
        defaults with { DoubleClickFilesToOpen = false }).Count, "disabled file activation routes no target");

    False(ItemActionPolicy.CanUseContextActions(PreviewInteractionMode.HoverPointer, defaults), "hover remains non-activating and has no context menu");
    True(ItemActionPolicy.CanUseContextActions(PreviewInteractionMode.Sticky, defaults), "sticky context actions remain available");
    False(ItemActionPolicy.CanUseContextActions(PreviewInteractionMode.Sticky,
        defaults with { RightClickActions = false }), "context-action setting is respected");

    var disabled = defaults with { InteractiveItems = false };
    foreach (var mode in Enum.GetValues<PreviewInteractionMode>())
    {
        False(ItemActionPolicy.CanHitTestEntries(mode, disabled), $"interaction master switch blocks hit testing in {mode}");
        False(ItemActionPolicy.CanSelect(mode, disabled), $"interaction master switch blocks selection in {mode}");
        False(ItemActionPolicy.CanPromoteHover(mode, disabled), $"interaction master switch blocks promotion in {mode}");
        False(ItemActionPolicy.CanDoubleClick(mode, folder, disabled), $"interaction master switch blocks folder activation in {mode}");
        False(ItemActionPolicy.CanUseContextActions(mode, disabled), $"interaction master switch blocks context actions in {mode}");
    }
    return Task.CompletedTask;
}

static Task TestContextActions()
{
    var file = new FolderEntry("file.txt", @"C:\Root\file.txt", false, 1, DateTimeOffset.UtcNow);
    var folder = new FolderEntry("Folder", @"C:\Root\Folder", true, null, DateTimeOffset.UtcNow);
    Sequence([ItemAction.Open, ItemAction.OpenFileLocation, ItemAction.CopyPath, ItemAction.Properties], ItemActionPolicy.Available([file], true), "file actions");
    Sequence([ItemAction.Open, ItemAction.CopyPath, ItemAction.Properties], ItemActionPolicy.Available([folder], true), "folder actions");
    Sequence([ItemAction.Open, ItemAction.CopyPaths], ItemActionPolicy.Available([file, folder], true), "multiple actions");
    Equal(@"C:\Root\file.txt" + Environment.NewLine + @"C:\Root\Folder", ItemActionPolicy.PathsForClipboard([file, folder]), "copy paths uses newlines");
    Equal(0, ItemActionPolicy.Available([file], false).Count, "disabled right-click has no actions");
    True(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.Sticky, file, FolderGlimpseSettings.Default), "file double-click enabled by default");
    True(ItemActionPolicy.CanDoubleClick(PreviewInteractionMode.Sticky, folder, FolderGlimpseSettings.Default), "folder double-click enabled by default");
    return Task.CompletedTask;
}

static Task TestPositioning()
{
    Equal(10, FolderGlimpseSettings.Default.PreviewVisibleRows, "default preview shows ten rows");
    True(FolderGlimpseSettings.Default.PreviewRowHeightDip > 0, "preview row height is positive");
    var work = new PixelRect(0, 0, 1920, 1040);
    var right = PopupPositioner.Place(new PixelRect(100, 100, 300, 140), work, new PixelSize(430, 500));
    Equal(310, right.Left, "uses right side when available");
    var left = PopupPositioner.Place(new PixelRect(1700, 100, 1850, 140), work, new PixelSize(430, 500));
    Equal(1260, left.Left, "flips left at right edge");
    var bottom = PopupPositioner.Place(new PixelRect(500, 1010, 650, 1030), work, new PixelSize(430, 500));
    Equal(540, bottom.Top, "clamps bottom");
    var negative = PopupPositioner.Place(new PixelRect(-1800, -400, -1700, -360), new PixelRect(-1920, -1080, 0, 0), new PixelSize(500, 600));
    True(negative.Left >= -1920 && negative.Right <= 0 && negative.Top >= -1080 && negative.Bottom <= 0, "negative monitor coordinates stay on-screen");
    var tiny = PopupPositioner.Place(new PixelRect(20, 20, 30, 30), new PixelRect(0, 0, 300, 200), new PixelSize(500, 600));
    Equal(300, tiny.Width, "oversized popup shrinks to work width");
    Equal(200, tiny.Height, "oversized popup shrinks to work height");
    return Task.CompletedTask;
}

static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void False(bool value, string message) => True(!value, message);
static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}
static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
{
    if (!expected.SequenceEqual(actual)) throw new InvalidOperationException($"{message}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
}
static FolderEntry Entry(string path, bool directory) => new(Path.GetFileName(path), path, directory, directory ? null : 1, DateTimeOffset.UtcNow);
static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"{message}: expected {typeof(T).Name}");
}

sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> Requests { get; } = [];
    public HashSet<string> MissingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FailingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task OpenFileAsync(string path, CancellationToken cancellationToken = default) { if (MissingPaths.Contains(path)) throw new FileNotFoundException(); if (FailingPaths.Contains(path)) throw new InvalidOperationException(); Requests.Add("file:" + path); return Task.CompletedTask; }
    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) { if (MissingPaths.Contains(path)) throw new DirectoryNotFoundException(); if (FailingPaths.Contains(path)) throw new InvalidOperationException(); Requests.Add("folder:" + path); return Task.CompletedTask; }
    public Task OpenFileLocationAsync(string path, CancellationToken cancellationToken = default) { Requests.Add("location:" + path); return Task.CompletedTask; }
    public Task ShowPropertiesAsync(string path, CancellationToken cancellationToken = default) { Requests.Add("properties:" + path); return Task.CompletedTask; }
}

sealed class FakeConfirmation(bool result) : IOpenManyConfirmation
{
    public int Calls { get; private set; }
    public Task<bool> ConfirmAsync(int itemCount, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(result); }
}

sealed class FakeStartupValueStore : IStartupValueStore
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public string? Read(string name) => Values.GetValueOrDefault(name);
    public void Write(string name, string value) => Values[name] = value;
    public void Delete(string name) => Values.Remove(name);
}

using FolderPeek.Core;
using FolderPeek.Core.FolderInspection;
using FolderPeek.Core.Input;
using FolderPeek.Core.Settings;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Tap/hold state machine", TestStateMachine),
    ("Input eligibility policy", TestEligibility),
    ("Explorer focus ancestry policy", TestExplorerFocusPolicy),
    ("Folder enumeration", TestEnumeration),
    ("Settings persistence and recovery", TestSettings),
    ("Popup positioning", TestPositioning)
};

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
    var root = Path.Combine(Path.GetTempPath(), "FolderPeek.Tests", Guid.NewGuid().ToString("N"));
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
    var root = Path.Combine(Path.GetTempPath(), "FolderPeek.Settings.Tests", Guid.NewGuid().ToString("N"));
    var path = Path.Combine(root, "settings.json");
    try
    {
        var service = new JsonSettingsService(path);
        service.Load();
        True(File.Exists(path), "missing settings file is created");
        Equal(430d, service.Current.PopupWidth, "defaults loaded");
        True(service.TryUpdate(s => s with { Theme = ThemePreference.Dark, PopupWidth = 612, InitialItemLimit = 100 }, out _), "settings update persists");
        var reloaded = new JsonSettingsService(path); reloaded.Load();
        Equal(ThemePreference.Dark, reloaded.Current.Theme, "enum round trips");
        Equal(612d, reloaded.Current.PopupWidth, "number round trips");

        File.WriteAllText(path, "{ \"showFileSize\": false, \"unknownFutureField\": 123 }");
        reloaded.Load();
        False(reloaded.Current.ShowFileSize, "partial boolean is retained");
        Equal(430d, reloaded.Current.PopupWidth, "missing partial values use defaults");

        File.WriteAllText(path, "{ not-json");
        reloaded.Load();
        Equal(FolderPeekSettings.Default, reloaded.Current, "malformed settings recover to defaults");
        True(reloaded.TryUpdate(s => s with { PopupWidth = 9999, HoldThresholdMs = 1, InitialItemLimit = 17 }, out _), "invalid values normalize");
        Equal(700d, reloaded.Current.PopupWidth, "width clamps high");
        Equal(100, reloaded.Current.HoldThresholdMs, "hold delay clamps low");
        Equal(50, reloaded.Current.InitialItemLimit, "unsupported item limit resets");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    return Task.CompletedTask;
}

static Task TestPositioning()
{
    Equal(10, FolderPeekSettings.Default.PreviewVisibleRows, "default preview shows ten rows");
    True(FolderPeekSettings.Default.PreviewRowHeightDip > 0, "preview row height is positive");
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
static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"{message}: expected {typeof(T).Name}");
}

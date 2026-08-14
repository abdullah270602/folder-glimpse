# Hover preview specification

Hover is FolderGlimpse's pointer-first way to glimpse a folder without opening it or changing
Explorer selection. It works alongside the configurable keyboard trigger rather than replacing
it. Hover ships disabled until the user explicitly chooses one of two targeting modes:

- **Selected folder** — preview only when the pointer rests on the single selected folder. This is
  the conservative V1 mode and reuses the existing selection/focus proof.
- **Any folder** — preview an unselected filesystem folder under the pointer. This V2 mode uses UI
  Automation only after dwell, then resolves the UIA display name through the active Explorer
  Shell view. UIA metadata is never treated as an authoritative filesystem path.

## User-visible behavior

1. Explorer must be the foreground application and the pointer must be over its file view.
2. The pointer must remain within the configured movement tolerance for the configured dwell time.
3. FolderGlimpse appears beside the item without activating or changing Explorer selection.
4. The preview stays open while the pointer is over the source item or the preview.
5. Double-clicking a child item opens it through the normal Windows shell without activating the
   glimpse for keyboard input. Selection, keyboard navigation, and context actions remain sticky-only.
6. It closes after the configured exit delay when the pointer leaves both regions.
7. Moving to another folder starts a new dwell; the old preview cannot publish over the new target.

Settings are independent and persistent:

- mode: Off / Selected folder / Any folder;
- open delay: 150–2000 ms;
- close delay: 100–1000 ms;
- movement tolerance: 2–16 physical pixels;
- modifier: None / Ctrl / Shift.

Keyboard access remains available in every hover mode. Users can configure Space or Ctrl+Space,
then choose tap-to-toggle or hold-only behavior. These are alternative trigger styles; the app
does not register arbitrary simultaneous keyboard shortcuts. Changing hover settings cancels an
active hover glimpse and applies to the next dwell.

## Safety policy

Hover fails closed: uncertainty means no preview, but it never blocks, injects, or changes mouse
input. The following always cancel or reject a hover:

- Explorer is not foreground, or its frame/process changed;
- a mouse button is held, a drag/capture is active, or an unsupported modifier is down;
- search, address bar, rename editor, navigation tree, menus, dialogs, preview/details panes, or an
  element outside the active Explorer file view is under the pointer;
- the target is virtual, remote/UNC, missing, not a folder, offscreen, or has invalid bounds;
- UIA/Shell calls time out, throw, return ambiguous data, or complete for an obsolete generation;
- the main FolderGlimpse window opens, the app is disabled, Explorer exits, the session changes, or
  a keyboard preview gesture begins.

V1 additionally requires the pointer inside the current selected-item bounds. V2 validates that
the UIA item belongs to the active Explorer frame and file-list ancestry, then uses the matched
Shell window's Folder.ParseName result to obtain and validate the filesystem path.

## Performance architecture

The pipeline has two stages:

### Fast sampler

- one 50 ms background timer (20 Hz), started only when hover is enabled;
- Win32-only checks: cursor, foreground HWND, buttons/modifiers, rectangle containment;
- no allocation-heavy logging, filesystem access, Shell COM, or UIA calls;
- unchanged points do not enqueue work.

### Deferred resolver

- one bounded MTA task at a time for UI Automation;
- Shell COM resolution is isolated from the UI thread;
- at most one request in flight; newer generations replace older pending work;
- work starts only after dwell, never on ordinary mouse movement;
- successful targets are cached while the pointer remains inside their bounds;
- a generation/cancellation check occurs before every UI publish.

Performance budgets for Release builds:

- fast-sampler p99 under 0.25 ms on supported hardware;
- zero UIA/Shell calls before dwell;
- no more than one target-resolution request per stationary candidate;
- a rejected stationary target is negatively cached until meaningful pointer movement;
- resolution rate is bounded by the configured dwell delay even during adversarial movement;
- no folder enumeration and no thumbnail extraction in the hover detector;
- existing cancellable background folder inspection and icon cache behavior remain unchanged;
- idle hover-disabled cost is zero (timer stopped and worker asleep).

## Pointer interaction

Hover and sticky previews use separate interaction modes. Hover enables hit-testing only for row
double-click activation and retains `WS_EX_NOACTIVATE`; it never selects rows, accepts keyboard
input, shows selection checkboxes, or opens context menus. A double-click passes the exact row entry
to the shared activation service, closes the hover glimpse after a successful request, and preserves
the file/folder activation settings. Momentary previews remain fully view-only. Sticky previews keep
the complete selection, keyboard, Open button, and context-action behavior.

## State model

`Idle -> Dwelling -> Resolving -> Open -> ClosingGrace -> Idle`

A failed resolution enters `Rejected`; it returns to `Dwelling` only after movement beyond the
tolerance or to `Idle` after a context cancellation. This prevents repeated UIA/Shell work while
the pointer rests on a file, blank area, or unsupported Explorer surface.

- movement outside tolerance restarts `Dwelling`;
- an obsolete resolver completion is ignored by generation;
- returning to source/preview during `ClosingGrace` restores `Open` without reloading;
- any unsafe context transition goes directly to `Idle` and closes;
- keyboard preview ownership always preempts hover ownership.

## Required tests

- settings defaults, normalization, partial JSON, round trip, reset, and invalid enum recovery;
- dwell at threshold −1/0/+1 ms and movement tolerance boundaries;
- selected-mode bounds and snapshot freshness checks;
- any-folder resolution success plus all fail-closed ancestry/Shell cases;
- stale completion, rapid A→B→C movement, same-target cache, and duplicate-open suppression;
- leave/return close grace, pointer over preview, foreground loss, drag/buttons, modifiers, disable,
  settings change, keyboard preemption, and Explorer restart;
- interaction-mode truth table covering view-only, hover-pointer, and sticky behavior; hover
  double-click routing for files/folders; disabled activation settings; missing targets and failures;
- sampler benchmark with fake Win32 input proving no deferred calls before dwell;
- real Windows matrix across Explorer layouts, tabs, search/rename/navigation tree, mixed DPI,
  100/125/150/200%, negative monitor origins, scrolling, and large folders.

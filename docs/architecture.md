# FolderGlimpse architecture decision

Status: accepted for V1 (2026-08-09)

## Product identity and upgrades

FolderGlimpse uses a `Local\FolderGlimpse.SingleInstance` mutex. During an in-place
upgrade, close any still-running legacy version before starting FolderGlimpse; the old
and new executables use different mutex names and could otherwise both install keyboard
hooks for that one session. Settings and launch-at-sign-in registrations are migrated
automatically from the legacy identity, without overwriting newer FolderGlimpse settings.

## Decision

FolderGlimpse is a separate, non-elevated .NET 8 WPF process. It does not inject into
Explorer. Four isolated areas are joined by immutable records and small interfaces:

- `ExplorerIntegration` produces a conservative `ExplorerSnapshot`. Win32 identifies
  the foreground HWND, Shell automation supplies the authoritative selected filesystem
  path, and UI Automation proves that focus is in Explorer's file view and supplies the
  selected row's physical screen rectangle.
- `Input` owns a `WH_KEYBOARD_LL` hook and an explicit tap/hold state machine. The hook
  performs no COM, UIA, filesystem, WPF, or blocking work. It may consume Space only
  from a fresh eligible snapshot whose HWND still equals `GetForegroundWindow()`.
- Optional hover input uses a separate 20 Hz sampler that runs only while Explorer is the
  foreground application and is stopped everywhere else, including while hover is off.
  Before the dwell threshold it performs only cursor, modifier, mouse-button, and foreground
  HWND checks. Any-folder resolution is latest-request-wins on isolated UIA MTA and Shell STA
  workers; enumeration and icon extraction remain in the cancellable preview pipeline.
- Explorer eligibility refresh is event-driven. Out-of-context WinEvent hooks invalidate on
  foreground, focus, selection, and item-location changes; a 75 ms debounce avoids querying UIA
  while Explorer is rebuilding a row, and a slow three-second fallback covers providers that
  omit events. This replaces continuous cross-process UIA/Shell polling.
- `FolderInspection` enumerates only immediate children, asynchronously, with
  cancellation and a bounded initial result set. Directories sort before files.
- `Preview` is one reusable WPF tool window. Momentary mode remains non-activating and
  read-only; hover mode remains non-activating until a deliberate row click promotes the existing
  popup to sticky mode, which explicitly takes focus for standard mouse and keyboard interaction.
  Placement is computed in physical pixels against the selected monitor work area and
  applied with `SetWindowPos`.
- `Interaction` owns a UI-independent selection model plus Shell-launch and confirmation
  abstractions. Paths are passed as data to normal Shell execution; no command strings are
  constructed and tests use fakes rather than launching applications.
- `Settings` is an immutable snapshot published by a central JSON service. Writes use a
  same-directory temporary file and atomic replacement; invalid, missing, or partial files
  recover to normalized defaults. Startup registration remains a separate Windows source
  of truth under the current user's Run key.

The application is per-monitor-V2 DPI aware and x64. A tray icon controls enable/disable,
settings, launch-at-sign-in, About, and exit. Release logging is off; debug logging records
integration and state decisions.

## Input ownership and safety

Suppression is synchronous: Windows requires the hook to pass or consume the first key
down before tap versus hold is known. FolderGlimpse therefore decides ownership once, on
the first physical unmodified Space down, and keeps that decision through the matching
up. A consumed down implies repeats and up are consumed. A passed down implies repeats
and up are passed.

Eligibility requires all of the following:

1. FolderGlimpse is enabled.
2. The event is physical and matches the configured Space or exact Ctrl+Space chord.
3. The foreground HWND is a normal Explorer frame and has not changed.
4. The snapshot is fresh.
5. UI Automation proves focus is in the file-items selection container, not an Edit,
   search/address field, rename control, navigation tree, menu, or dialog.
6. Shell and UIA agree there is exactly one selected normal filesystem directory.

Any missing evidence, exception, race, unsupported namespace, stale snapshot, or
integrity boundary causes the key to pass through. A sticky preview may own foreground
focus while it is interactive; it closes when focus moves anywhere other than its own
context menu. Escape and the configured second Space gesture are handled locally by the
focused sticky window without broadening the global hook. Otherwise Explorer receives them.

The state machine is `Idle`, `Pending`, `MomentaryOpen`, `StickyOpen`, plus
`ClosingUntilSpaceUp` so a second sticky-mode Space never leaks an orphan release.
The hold threshold and tap policy are snapshotted at first key-down (defaults: 200 ms and
toggle preview). Repeated downs never transition, and changing settings mid-gesture cannot
leak the matching key-up.

## Explorer integration

The ideal Shell path is `IShellWindows` -> matching `IWebBrowser2.HWND` ->
`IShellBrowser.QueryActiveShellView` -> `IFolderView2.GetSelection` -> `IShellItem`.
This provides `SFGAO_FOLDER | SFGAO_FILESYSTEM` and `SIGDN_FILESYSPATH` validation.
The initial implementation uses the documented Shell automation fallback
(`Shell.Application.Windows`, matched by HWND, `Document.SelectedItems`) because it is
substantially smaller and can be deployed without an interop package. `Directory.Exists`
and rooted/local-path checks narrow its output. The interface permits replacing this
reader with the lower-level implementation without touching input or UI.

UI Automation runs off the WPF thread. It rejects Edit ancestry and requires a selected
UIA item under an Explorer file-list container. Its bounding rectangle is treated as
physical pixels. A missing rectangle is not fatal: positioning falls back to the cursor,
but missing focus proof makes the entire snapshot ineligible.

Windows 11 tabs are a known Shell-API ambiguity: frame HWND alone is insufficient.
Matching the foreground HWND, active automation tree, selection, and rechecking the
foreground mitigates it. If multiple candidates cannot be distinguished, FolderGlimpse
passes Space.

## UI and DPI

WPF was chosen over WinUI 3. FolderGlimpse's difficult work is HWND/COM/UIA interop, for
which WinUI provides no simplification, while Windows App SDK would add deployment and
bootstrap complexity. The popup always uses `WS_EX_TOOLWINDOW`. Momentary/read-only mode
additionally uses `WS_EX_NOACTIVATE`, `ShowActivated=false`, and
`SetWindowPos(... SWP_NOACTIVATE)`. Hover mode keeps that style while enabling row hit-testing
only for pointer double-click activation; it does not accept keyboard focus or selection. Sticky
interactive mode removes the no-activate style,
takes foreground focus, and restores the captured Explorer frame when the user dismisses it.
Because Windows may reject an immediate cross-thread foreground request, the handoff briefly
attaches only the WPF and current foreground input queues, sets focus, and detaches in `finally`.

`AllowsTransparency` is avoided so DWM can provide reliable shadowing and rounded
corners. Placement converts the WPF desired DIP size once using the target DPI, then
does side selection and clamping entirely in physical pixels. Negative monitor origins
and taskbars on any edge are supported through `MonitorFromRect` and `GetMonitorInfo`.

Settings and Preview consume one application-level WPF resource dictionary. It defines
the typography, cards, buttons, toggle switches, combo boxes, sliders, focus states, and
one vertical scrollbar template, while `ThemeManager` supplies light/dark palette brushes
at runtime. Settings follows the Windows single-column settings-card pattern: every change
applies immediately, descriptions clarify consequences, controls align consistently, and
the action footer stays visible while content scrolls. The native title bar follows dark
mode through DWM so it does not clash with the content surface.

The notification-area menu is a WinForms `ContextMenuStrip`, so it cannot consume WPF
templates. A dedicated `ToolStripProfessionalRenderer` maps the same resolved palette to
its background, text, separators, rounded selection state, and accent checkmarks. The
renderer is swapped whenever `ThemeManager` changes, and DWM receives matching dark-mode
and rounded-corner attributes when the menu opens. High-contrast mode maps to system colors.

## Alternatives considered

- **WinUI 3:** better built-in Fluent styling, but materially more packaging/interoperability
  complexity for no V1 integration benefit.
- **RegisterHotKey:** has no reliable release stream for momentary mode and bare Space is
  globally intrusive.
- **Raw Input:** observes background input but cannot conditionally suppress delivery.
- **Synchronous COM/UIA in the hook:** rejected; hook timeouts can silently remove the hook
  and cross-process calls can hang.
- **Explorer extension or injection:** rejected for safety, deployment, and crash-isolation.
- **Ctrl+Space default:** retained as a future configurable fallback, but it also conflicts
  with IME behavior and does not meet the requested interaction.

## Known limitations and validation gaps

- Plain Space overrides Explorer's own select/deselect behavior in the narrowly eligible
  case. This is intentional and must be field-tested.
- Shell automation and UIA are best-effort across Explorer builds and integrity levels.
  Elevated Explorer, virtual folders, ZIPs, Libraries, UNC paths, and unusual namespace
  extensions are rejected.
- A conservative cache may miss a very fast press after a selection change; it passes the
  key instead of risking text-input interference.
- UIA rectangles may be stale, off-screen, or full-row width. Cursor fallback is used.
- Open-file-location currently opens the containing folder without selecting the child.
- Full native Explorer shell-extension context menus and internal folder navigation remain
  deliberately out of scope; FolderGlimpse exposes only its small non-destructive action set.
- The hosted development environment's Shell automation probe returned Access Denied, so
  Explorer selection/tab behavior requires the manual checklist in `docs/manual-testing.md`.

## Primary references

- [LowLevelKeyboardProc](https://learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc)
- [SetWindowsHookEx](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowshookexw)
- [UI Automation threading](https://learn.microsoft.com/windows/win32/winauto/uiauto-threading)
- [IShellWindows](https://learn.microsoft.com/windows/win32/api/exdisp/nn-exdisp-ishellwindows)
- [IFolderView2::GetSelection](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-getselection)
- [UI Automation bounding rectangles](https://learn.microsoft.com/dotnet/api/system.windows.automation.automationelement.boundingrectangleproperty)
- [Per-monitor-V2 manifests](https://learn.microsoft.com/windows/win32/sbscs/application-manifests)
- [SetWindowPos](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [WPF control virtualization](https://learn.microsoft.com/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- [Windows settings design guidance](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings)
- [Windows content layout and spacing](https://learn.microsoft.com/windows/apps/design/basics/content-basics)
- [WPF styles and templates](https://learn.microsoft.com/dotnet/desktop/wpf/controls/styles-templates-overview)
- [ToolStrip custom renderers](https://learn.microsoft.com/dotnet/api/system.windows.forms.toolstripprofessionalrenderer)

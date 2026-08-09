# FolderPeek architecture decision

Status: accepted for V1 (2026-08-09)

## Decision

FolderPeek is a separate, non-elevated .NET 8 WPF process. It does not inject into
Explorer. Four isolated areas are joined by immutable records and small interfaces:

- `ExplorerIntegration` produces a conservative `ExplorerSnapshot`. Win32 identifies
  the foreground HWND, Shell automation supplies the authoritative selected filesystem
  path, and UI Automation proves that focus is in Explorer's file view and supplies the
  selected row's physical screen rectangle.
- `Input` owns a `WH_KEYBOARD_LL` hook and an explicit tap/hold state machine. The hook
  performs no COM, UIA, filesystem, WPF, or blocking work. It may consume Space only
  from a fresh eligible snapshot whose HWND still equals `GetForegroundWindow()`.
- `FolderInspection` enumerates only immediate children, asynchronously, with
  cancellation and a bounded initial result set. Directories sort before files.
- `Preview` is one reusable, non-activating WPF window. Placement is computed in physical
  pixels against the selected monitor work area and applied with `SetWindowPos`.

The application is per-monitor-V2 DPI aware and x64. A tray icon controls enable/disable
and exit. Release logging is off; debug logging records integration and state decisions.

## Input ownership and safety

Suppression is synchronous: Windows requires the hook to pass or consume the first key
down before tap versus hold is known. FolderPeek therefore decides ownership once, on
the first physical unmodified Space down, and keeps that decision through the matching
up. A consumed down implies repeats and up are consumed. A passed down implies repeats
and up are passed.

Eligibility requires all of the following:

1. FolderPeek is enabled.
2. The event is physical, plain Space (no Ctrl, Alt, Shift, or Windows key).
3. The foreground HWND is a normal Explorer frame and has not changed.
4. The snapshot is fresh.
5. UI Automation proves focus is in the file-items selection container, not an Edit,
   search/address field, rename control, navigation tree, menu, or dialog.
6. Shell and UIA agree there is exactly one selected normal filesystem directory.

Any missing evidence, exception, race, unsupported namespace, stale snapshot, or
integrity boundary causes the key to pass through. Sticky preview closes when its
Explorer context becomes invalid. Escape is consumed only while a same-context sticky
preview is visible; otherwise Explorer receives it.

The state machine is `Idle`, `Pending`, `MomentaryOpen`, `StickyOpen`, plus
`ClosingUntilSpaceUp` so a second sticky-mode Space never leaks an orphan release.
The hold threshold is one setting (default 200 ms). Repeated downs never transition.

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
foreground mitigates it. If multiple candidates cannot be distinguished, FolderPeek
passes Space.

## UI and DPI

WPF was chosen over WinUI 3. FolderPeek's difficult work is HWND/COM/UIA interop, for
which WinUI provides no simplification, while Windows App SDK would add deployment and
bootstrap complexity. The popup uses `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`,
`ShowActivated=false`, and `SetWindowPos(... SWP_NOACTIVATE)`. It never takes focus.

`AllowsTransparency` is avoided so DWM can provide reliable shadowing and rounded
corners. Placement converts the WPF desired DIP size once using the target DPI, then
does side selection and clamping entirely in physical pixels. Negative monitor origins
and taskbars on any edge are supported through `MonitorFromRect` and `GetMonitorInfo`.

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
- A non-activating preview is view-only. Keyboard navigation is deferred to a future
  explicit interactive mode.
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

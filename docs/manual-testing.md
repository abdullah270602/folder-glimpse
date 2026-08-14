# Windows integration test checklist

## Hover runtime verification — 2026-08-14

The Release build was exercised against Windows 11 File Explorer in Details view, with
diagnostics written to `artifacts/hover-runtime.log`:

| Check | Result |
|---|---|
| Selected-folder mode after returning from visible Settings | Pass — App Certification Kit opened beside its selected row |
| Any-folder mode on a selected row | Pass — Catalogs resolved through UIA + Shell and opened |
| Any-folder mode on an unselected row | Pass — Remote remained selected while DesignTime was hovered and previewed |
| Windows 11 read-only Details cells reported as UIA Edit | Pass after differentiating read-only cells from writable search/rename edits |
| Exit grace | Pass — leaving source and preview closed after the configured 200 ms delay |
| Blank Explorer area | Pass — one failed resolution, no popup, no repeated stationary work |
| Settings wheel movement | Pass — controlled proportional scrolling rather than multi-card jumps |
| Explorer selection refresh | Pass — WinEvent invalidation refreshed Catalogs → Remote without polling delay |
| Explorer-foreground idle resource sample | Pass — 78.125 ms CPU over 12 seconds (0.651% of one core), 141.7 MB working set |

Ctrl/Shift matching, dwell/tolerance boundaries, stale generations, persistence, and allocation
behavior are covered by the automated suite. Physical modifier-hold behavior remains in the
manual matrix below and should not be described as end-to-end verified until that check is run.

## Result from this development session

Date: 2026-08-09  
Environment: Windows 11 x64 (`10.0.26200`), .NET SDK 8.0.423

| Check | Result |
|---|---|
| Debug solution build | Pass, 0 warnings / 0 errors |
| Automated test executable | Pass, 13/13 suites |
| App process startup smoke test | Pass |
| Windows 11 Explorer selection/focus snapshot | Pass against `UIItem` → `UIItemsView` → `CabinetWClass` ancestry |
| Sticky tap / second-tap close | Pass in live Explorer |
| Hold-open / release-close | Pass in live Explorer at the 200 ms threshold |
| Selected file safety | Pass; Space was not owned and no popup appeared |
| Explorer search safety | Pass; Space was inserted into search and no popup appeared |
| Rendered names/icons/sizes | Pass; captured in `artifacts/live-preview-final.png` during development |
| Mixed-DPI/light-dark visual inspection | Not run |
| Settings missing/partial/malformed recovery | Pass (automated) |
| Hidden, modified-date, global-limit sorting | Pass (automated) |
| Modern Settings render in explicit Light and Dark | Pass (deterministic render inspection) |
| Shared Settings/Preview scrollbar render | Pass (deterministic render inspection) |
| Combo/toggle/slider/button accessibility patterns | Pass (6/6 combos, 14/14 toggles, 4/4 sliders, both footer actions) |
| Tray menu Light/Dark, checkmark, separator, and hover renders | Pass (deterministic render inspection) |
| Interactive preview selection/checkmark/action-bar render | Pass in explicit Light and Dark (deterministic render inspection) |
| Interaction Settings section render | Pass in explicit Dark (deterministic render inspection) |
| Sticky native focus/style probe | Pass; foreground moved to popup, `TOOLWINDOW` present, `NOACTIVATE` absent |
| Momentary native focus/style probe | Pass; foreground unchanged, `TOOLWINDOW | NOACTIVATE` present |
| Sticky Ctrl+A keyboard input | Pass; all four displayed fixture rows selected through focused-window input |
| Sticky Down-arrow keyboard input | Pass; second displayed row became the sole focused selection |
| Sticky Escape / second-Space close | Pass; popup hid and prior foreground HWND was restored in both probes |

The desktop-control service could not enumerate Windows (`EnumWindows` returned
`0x80070003`), so validation used direct Windows UI Automation, Shell automation, and an
opt-in diagnostic build that accepts injected input. A real physical Space press was also
observed by the hook as non-injected and owned for the eligible folder. Mixed-DPI,
rename-in-progress, and session-transition cases still require the checklist below before
broad distribution.

The table records development evidence, not a substitute for the required release gate. Every
signed production candidate must repeat the automated checks and applicable hands-on cases.

## Prepare fixtures

From the repository root:

```powershell
./scripts/Create-TestFolders.ps1
```

This creates `%TEMP%\FolderGlimpseTest` with empty, small, many-item, and deep-but-nonrecursive
folders and opens nothing automatically.

## Required release gate

- [ ] Launch the self-contained `FolderGlimpse.exe`; verify one tray icon and no taskbar entry.
- [ ] Open `%TEMP%\FolderGlimpseTest` in normal, non-elevated File Explorer.
- [ ] Select `Small`, tap Space, and verify a sticky popup appears beside the selected row.
- [ ] Single-click a file; verify it highlights without opening. Double-click it and verify
      its configured Windows application opens.
- [ ] Double-click a child folder; verify File Explorer opens it without changing FolderGlimpse
      into an internal navigation view.
- [ ] Use Up/Down, Enter, Escape, Ctrl+A, Ctrl-click, and Shift-click in sticky mode; verify
      standard selection and activation behavior.
- [ ] Right-click a file, folder, and multi-selection; verify only the documented safe actions.
- [ ] Copy one path and several paths; verify full paths and newline separation on Clipboard.
- [ ] Enable selection checkboxes; verify checkbox state and row selection remain identical.
- [ ] Select above the configured threshold; verify Cancel/Open All appears before any item
      launches. Disable multi-open and verify no group is launched.
- [ ] Turn off close-after-opening and verify a successful launch leaves the sticky preview open.
- [ ] Disable Interactive items; verify sticky preview is read-only and held/momentary behavior is unchanged.
- [ ] Verify releasing Space does not close sticky mode.
- [ ] Tap Space again; verify the popup closes and no subsequent key-up affects Explorer.
- [ ] Select `Small`, hold Space; verify the popup opens at roughly 200 ms and closes on release.
- [ ] Press/hold Space through Windows key repeat; verify only one popup transition occurs.
- [ ] Select `README.md`; press Space; verify FolderGlimpse does nothing and Explorer receives Space.
- [ ] Select multiple items; verify Space passes through.
- [ ] Click Explorer search and type several words with spaces; verify zero interception.
- [ ] Rename a folder and type a name containing spaces; verify zero interception.
- [ ] Focus the address bar, navigation tree, command bar, preview pane, and a context menu;
      verify Space is never consumed.
- [ ] Open Notepad and type/hold Space; verify FolderGlimpse does nothing.
- [ ] Hold Space over an eligible folder, Alt+Tab before release, then release; verify the
      popup closes and the destination app does not receive repeats or an orphan release.
- [ ] Preview `Empty`; verify a clear empty state.
- [ ] Preview `ManyItems`; verify immediate loading UI, responsiveness, scrolling, and a
      `200+ items` bounded-result message.
- [ ] Rapidly alternate two folders; verify late enumeration/icons never replace the newer view.
- [ ] Delete or rename the selected folder while preview is loading; verify no crash or stale popup.
- [ ] Switch Explorer tabs immediately before Space. Repeat with two tabs selecting folders
      that have the same leaf name; ambiguous input must pass through.
- [ ] Restart Explorer while FolderGlimpse runs; verify no crash and recovery after a new safe snapshot.
- [ ] Test Windows light and dark app mode; verify readable text/borders.
- [ ] Test 100%, 125%, 150%, and, if available, mixed-DPI monitors. Include a monitor left of
      the primary and taskbars on different edges. Verify the popup stays in the work area.
- [ ] Verify the popup does not activate Explorer, enter Alt+Tab, cover unrelated foreground
      apps after focus changes, or remain topmost after session lock/unlock.
- [ ] Toggle **Enabled** off in the tray; verify Space always passes. Toggle on and retest.
- [ ] Open **Settings…** and exercise every control; restart and verify values persisted.
- [ ] Change System/Light/Dark while the popup and Settings are open; verify immediate readable updates.
- [ ] Test Space and exact Ctrl+Space modes, 100/600 ms hold limits, Toggle and Momentary Only.
- [ ] Leave hover **Off** and verify ordinary pointer movement never opens a preview.
- [ ] Select **Selected folder**, hover the selected row, and verify the preview opens only after
      the configured delay. Hover an unselected folder and verify it stays closed.
- [ ] Select **Any folder** and hover several unselected folders without clicking. Verify the
      correct folder opens, fast pointer sweeps never flash stale previews, and A → B → C cannot
      publish an old A/B result over C.
- [ ] Exercise minimum/maximum open delay, exit delay, and movement tolerance. Move from the
      Explorer row into the preview during exit grace and verify it remains open.
- [ ] Test None, exact Ctrl, and exact Shift hover modifiers. Verify extra modifiers, mouse
      buttons, drag/drop, Explorer menus, search, rename, navigation tree, desktop, and other
      applications never open a hover preview.
- [ ] While hover is open, press the configured keyboard trigger and verify keyboard ownership
      cleanly replaces hover ownership. Open the FolderGlimpse shell and verify hover closes.
- [ ] Compare Task Manager CPU with hover Off, Selected folder, and Any folder while idle and
      during rapid movement. Disabled cost should match the existing build; enabled idle use
      should remain negligible.
- [ ] Verify 20/50/100/200/All limits, hidden-file filtering, all sort modes, folders-first,
      compact/comfortable density, path/size/date visibility, width, and height.
- [ ] Toggle **Launch at startup** from both Settings and tray; verify synchronized state and
      the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry, then disable it.
- [ ] Replace settings.json with empty, partial, malformed, and out-of-range content; verify
      safe recovery without a crash and a healed complete file.
- [ ] Choose **Exit**; verify the process and hook terminate cleanly.
- [ ] Download the final GitHub Release assets on a clean Windows 11 account; verify
      `SHA256SUMS.txt`, `Get-AuthenticodeSignature`, the RFC 3161 timestamp, and Windows SDK
      `signtool verify /pa /all /v` before launching.
- [ ] Extract `FolderGlimpse-win-x64.zip`; verify its EXE hash equals the standalone asset and
      complete first-run, preview, startup, update-replacement, and uninstall checks.

Record Windows build, Explorer version, display layout/scales, and any failed eligibility
reason from a Debug build when reporting results.

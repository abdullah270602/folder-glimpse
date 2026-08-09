# Windows integration test checklist

## Result from this development session

Date: 2026-08-09  
Environment: Windows 11 x64 (`10.0.26200`), .NET SDK 8.0.423

| Check | Result |
|---|---|
| Debug solution build | Pass, 0 warnings / 0 errors |
| Automated test executable | Pass, 6/6 suites |
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

The desktop-control service could not enumerate Windows (`EnumWindows` returned
`0x80070003`), so validation used direct Windows UI Automation, Shell automation, and an
opt-in diagnostic build that accepts injected input. A real physical Space press was also
observed by the hook as non-injected and owned for the eligible folder. Mixed-DPI,
rename-in-progress, and session-transition cases still require the checklist below before
broad distribution.

## Prepare fixtures

From the repository root:

```powershell
./scripts/Create-TestFolders.ps1
```

This creates `%TEMP%\FolderPeekTest` with empty, small, many-item, and deep-but-nonrecursive
folders and opens nothing automatically.

## Required release gate

- [ ] Launch the self-contained `FolderPeek.exe`; verify one tray icon and no taskbar entry.
- [ ] Open `%TEMP%\FolderPeekTest` in normal, non-elevated File Explorer.
- [ ] Select `Small`, tap Space, and verify a sticky popup appears beside the selected row.
- [ ] Verify releasing Space does not close sticky mode.
- [ ] Tap Space again; verify the popup closes and no subsequent key-up affects Explorer.
- [ ] Select `Small`, hold Space; verify the popup opens at roughly 200 ms and closes on release.
- [ ] Press/hold Space through Windows key repeat; verify only one popup transition occurs.
- [ ] Select `README.md`; press Space; verify FolderPeek does nothing and Explorer receives Space.
- [ ] Select multiple items; verify Space passes through.
- [ ] Click Explorer search and type several words with spaces; verify zero interception.
- [ ] Rename a folder and type a name containing spaces; verify zero interception.
- [ ] Focus the address bar, navigation tree, command bar, preview pane, and a context menu;
      verify Space is never consumed.
- [ ] Open Notepad and type/hold Space; verify FolderPeek does nothing.
- [ ] Hold Space over an eligible folder, Alt+Tab before release, then release; verify the
      popup closes and the destination app does not receive repeats or an orphan release.
- [ ] Preview `Empty`; verify a clear empty state.
- [ ] Preview `ManyItems`; verify immediate loading UI, responsiveness, scrolling, and a
      `200+ items` bounded-result message.
- [ ] Rapidly alternate two folders; verify late enumeration/icons never replace the newer view.
- [ ] Delete or rename the selected folder while preview is loading; verify no crash or stale popup.
- [ ] Switch Explorer tabs immediately before Space. Repeat with two tabs selecting folders
      that have the same leaf name; ambiguous input must pass through.
- [ ] Restart Explorer while FolderPeek runs; verify no crash and recovery after a new safe snapshot.
- [ ] Test Windows light and dark app mode; verify readable text/borders.
- [ ] Test 100%, 125%, 150%, and, if available, mixed-DPI monitors. Include a monitor left of
      the primary and taskbars on different edges. Verify the popup stays in the work area.
- [ ] Verify the popup does not activate Explorer, enter Alt+Tab, cover unrelated foreground
      apps after focus changes, or remain topmost after session lock/unlock.
- [ ] Toggle **Enabled** off in the tray; verify Space always passes. Toggle on and retest.
- [ ] Open **Settings…** and exercise every control; restart and verify values persisted.
- [ ] Change System/Light/Dark while the popup and Settings are open; verify immediate readable updates.
- [ ] Test Space and exact Ctrl+Space modes, 100/600 ms hold limits, Toggle and Momentary Only.
- [ ] Verify 20/50/100/200/All limits, hidden-file filtering, all sort modes, folders-first,
      compact/comfortable density, path/size/date visibility, width, and height.
- [ ] Toggle **Launch at startup** from both Settings and tray; verify synchronized state and
      the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry, then disable it.
- [ ] Replace settings.json with empty, partial, malformed, and out-of-range content; verify
      safe recovery without a crash and a healed complete file.
- [ ] Choose **Exit**; verify the process and hook terminate cleanly.

Record Windows build, Explorer version, display layout/scales, and any failed eligibility
reason from a Debug build when reporting results.

# FolderPeek

FolderPeek is a lightweight Windows 11 tray utility that previews the immediate contents
of the selected File Explorer folder without navigating into it.

![FolderPeek screenshot placeholder](docs/screenshot-placeholder.svg)

## Use it

1. Launch `FolderPeek.exe`. It stays in the notification area.
2. In Windows 11 File Explorer, select exactly one normal local folder in the file list.
3. Tap **Space** to open a sticky preview. Tap **Space** again or press **Escape** to close.
4. Hold **Space** for about 200 ms to open a momentary preview; release it to close.
5. Right-click the tray icon to disable FolderPeek temporarily or exit cleanly.

FolderPeek deliberately passes Space through when Explorer is not foreground, selection
is ambiguous, a file or multiple items are selected, a text/search/rename field is
focused, a modifier is held, or its Explorer snapshot is stale. Plain Space does have an
Explorer selection meaning, so FolderPeek only overrides it in the narrow eligible case.

## Build

Requirements: Windows 11 x64 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
No third-party NuGet packages are used.

```powershell
dotnet restore FolderPeek.sln
dotnet build FolderPeek.sln -c Release
dotnet run --project tests/FolderPeek.Tests/FolderPeek.Tests.csproj -c Release
```

Create a framework-dependent build:

```powershell
dotnet publish src/FolderPeek/FolderPeek.csproj -c Release -r win-x64 --self-contained false -o artifacts/FolderPeek-win-x64
```

Create the preferred self-contained single-file build:

```powershell
dotnet publish src/FolderPeek/FolderPeek.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/FolderPeek-win-x64-self-contained
```

## Design

- .NET 8/WPF window with no activation, taskbar entry, or Explorer injection
- conservative Shell automation + UI Automation Explorer snapshot worker
- dedicated `WH_KEYBOARD_LL` thread; no filesystem, COM, UIA, WPF, or blocking work in
  the hook callback
- explicit, unit-tested tap/hold state machine
- cancellable off-UI-thread folder inspection capped at 200 initial items
- compact ten-row viewport with a rounded theme-aware scrollbar for larger folders
- native shell icons loaded after names appear, so icon extraction does not delay content
- per-monitor-V2 physical-pixel positioning and work-area clamping
- system light/dark color selection and a tray enable/exit menu

See [the architecture decision](docs/architecture.md) and
[manual integration checklist/results](docs/manual-testing.md).

## Current limitations

- V1 supports ordinary local filesystem folders only. UNC/network paths, ZIPs, Libraries,
  This PC, Recycle Bin, and other virtual Shell namespaces are rejected.
- Explorer integration is intentionally fail-closed. An elevated Explorer window or a
  Windows build whose UI Automation tree cannot be proven safe will receive normal Space
  behavior and show no preview.
- Windows 11 tab disambiguation uses the focused UIA item name plus the matched Explorer
  frame. Two tabs under the same frame selecting same-named folders are treated as
  ambiguous and are rejected.
- The popup is view-only in V1. Keyboard navigation and opening child items are deferred.
- Click-away is detected through foreground/focus/selection polling; clicking the same
  already-selected row may leave a sticky preview open. Space or Escape always closes it.
- High contrast is not specially styled yet. The app follows light/dark app mode.

FolderPeek works offline, performs no telemetry, does not modify previewed folders, and
does not require administrator privileges.

### Diagnostics

If Explorer integration needs troubleshooting, start FolderPeek from PowerShell with
`--diagnostics`. It writes `%LOCALAPPDATA%\FolderPeek\diagnostics.log`. The additional
`--allow-injected-input` switch exists only for automated integration testing; normal
launches continue rejecting injected keyboard events.

<div align="center">
  <img src="src/FolderGlimpse/Assets/Branding/FolderGlimpse-App-128.png" width="96" alt="FolderGlimpse icon">
  <h1>FolderGlimpse</h1>
  <p><strong>Glance inside folders without opening them.</strong></p>
  <p>A fast, lightweight folder preview utility built for Windows 11.</p>

  [![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11&logoColor=white)](https://www.microsoft.com/windows/windows-11)
  [![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
  [![Latest release](https://img.shields.io/github/v/release/abdullah270602/folder-glimpse?display_name=tag&sort=semver)](https://github.com/abdullah270602/folder-glimpse/releases/latest)

  <br>

  **[Download FolderGlimpse for Windows](https://github.com/abdullah270602/folder-glimpse/releases/latest/download/FolderGlimpse.exe)**
  · [View releases](https://github.com/abdullah270602/folder-glimpse/releases)
  · [Report an issue](https://github.com/abdullah270602/folder-glimpse/issues)
</div>

---

> [!IMPORTANT]
> No official FolderGlimpse binary has been published yet. The download links above will become
> active after the first reviewed, Authenticode-signed GitHub Release. Until then, build from
> source for evaluation and do not redistribute local validation artifacts.

FolderGlimpse lets you inspect a folder directly from File Explorer without navigating
into it. Select a folder and press <kbd>Space</kbd>: tap to keep the preview open, or hold
to view it only while the key is pressed.

![FolderGlimpse folder preview](docs/preview.svg)

## Highlights

- **Instant folder previews** from Windows 11 File Explorer
- **Tap or hold** the configured shortcut for sticky or momentary previews
- **Open files and folders** directly from an interactive sticky preview
- **Multi-select and context actions** with familiar Windows interactions
- **System, light, and dark themes** with mixed-DPI monitor support
- **Compact control center** for settings, help, startup behavior, and app status
- **Consistent Windows 11 styling** with purpose-built application and tray icons
- **Quiet tray operation** with centered actions, enable/disable, settings, startup, and exit
- **Local and private**: no account, cloud service, analytics, or telemetry

## Download and install

FolderGlimpse will initially be distributed as a portable, self-contained Windows x64 app.
Official release builds will not require a separate .NET installation.

1. Download **[FolderGlimpse.exe](https://github.com/abdullah270602/folder-glimpse/releases/latest/download/FolderGlimpse.exe)**.
2. Move it to a permanent folder such as `%LOCALAPPDATA%\Programs\FolderGlimpse`.
3. Run `FolderGlimpse.exe` and complete the short first-run introduction.
4. Optionally enable **Launch at startup** in Settings or from the tray menu.

> [!NOTE]
> FolderGlimpse is not code-signed yet. Windows SmartScreen may show an
> “unrecognized app” message on first launch. Download only from this repository's
> official [Releases](https://github.com/abdullah270602/folder-glimpse/releases) page.

For private testing before the first official release, the entire self-contained build folder
may be zipped and shared. Recipients should extract it before running the EXE and understand that
the build is unsigned. Do not present a locally shared build as an official GitHub release.

### Verify a download

Each production release will include `SHA256SUMS.txt`. From the folder containing the
download, compare the published hash with the locally calculated value:

```powershell
Get-FileHash .\FolderGlimpse.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

Once production signing is enabled, verify both the publisher signature and timestamp:

```powershell
$signature = Get-AuthenticodeSignature .\FolderGlimpse.exe
$signature | Format-List Status, StatusMessage
$signature.SignerCertificate | Format-List Subject, Thumbprint, NotAfter
$signature.TimeStamperCertificate | Format-List Subject, NotAfter
```

Only a `Valid` status from an official release is acceptable. Signing proves publisher
identity and file integrity; it does not guarantee that Microsoft SmartScreen will never
warn, because reputation is evaluated separately.

### Updating

Download the newest EXE from [Releases](https://github.com/abdullah270602/folder-glimpse/releases)
and replace the previous file after exiting FolderGlimpse from the tray.

### Uninstalling

1. Right-click the tray icon and choose **Exit**.
2. Disable **Launch at startup** first if it is enabled.
3. Delete `FolderGlimpse.exe`.
4. Optional: delete `%LOCALAPPDATA%\FolderGlimpse` to remove saved preferences.

## How to use

1. Open Windows 11 File Explorer.
2. Select exactly one normal local folder in the file list.
3. Tap <kbd>Space</kbd> to keep a preview open, or hold it for a momentary preview.
4. Tap <kbd>Space</kbd> again or press <kbd>Esc</kbd> to close a sticky preview.

### Optional hover preview

Hover preview is off by default. In **Settings → Hover preview**, choose **Selected
folder** to hover only the current selection, or **Any folder** to preview a folder without
selecting it first. The open delay, exit delay, movement tolerance, and optional Ctrl/Shift
safety modifier are adjustable. The sampler is stopped entirely while hover is off; folder
resolution starts only after the pointer remains stable for the configured delay.

In an interactive sticky preview:

- Click to select an item.
- Double-click a file to open it in its normal Windows app.
- Double-click a folder to open it in File Explorer.
- Use <kbd>Ctrl</kbd> or <kbd>Shift</kbd> for multiple selection.
- Press <kbd>Enter</kbd> to open selected items.
- Right-click for safe actions such as Open, Copy path, Open file location, and Properties.

Closing the control-center window does not exit FolderGlimpse; it continues running in
the notification area. The tray menu provides **Open FolderGlimpse**, **Enabled**, **Settings…**,
**Launch at startup**, and **Exit**. To stop the app completely, choose **Exit**.

## Safety and privacy

FolderGlimpse works offline, requires no administrator privileges, performs no telemetry,
and does not modify previewed folders. It uses conservative Explorer and UI Automation
checks and passes the shortcut through whenever the current context cannot be proven safe—for
example, while typing in search, the address bar, or a rename field.

Settings and application state are stored locally under `%LOCALAPPDATA%\FolderGlimpse`.

## Compatibility and current limitations

- Windows 11 x64 is the supported target.
- V1 previews ordinary local filesystem folders only.
- Network/UNC folders, ZIPs, Libraries, This PC, Recycle Bin, and other virtual Shell
  locations are intentionally not supported yet.
- Elevated Explorer windows may not be accessible to a normally launched FolderGlimpse;
  the shortcut passes through safely in that case.
- Momentary previews are view-only. Sticky previews support selection and activation.
- High contrast does not yet have a dedicated visual theme.

## Build from source

Requirements:

- Windows 11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

```powershell
git clone https://github.com/abdullah270602/folder-glimpse.git
cd folder-glimpse
dotnet restore FolderGlimpse.sln --configfile NuGet.config
dotnet build FolderGlimpse.sln -c Release
dotnet run --project tests/FolderGlimpse.Tests/FolderGlimpse.Tests.csproj -c Release
```

Create the same self-contained single-file build used for releases:

```powershell
dotnet publish src/FolderGlimpse/FolderGlimpse.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o artifacts/FolderGlimpse-win-x64
```

The output is `artifacts/FolderGlimpse-win-x64/FolderGlimpse.exe`.

To share a private test build, zip the complete `artifacts/FolderGlimpse-win-x64` folder. With the
single-file options above, it normally contains only the portable EXE; sharing the whole folder
keeps the process unambiguous if release files are added later.

## Contributing

Bug reports, documentation corrections, and carefully scoped feature proposals are welcome.
Please read [CONTRIBUTING.md](CONTRIBUTING.md) before preparing a change. By submitting a
contribution, you agree that it will be licensed under the project's MIT License. For security issues, follow
[SECURITY.md](SECURITY.md) instead of creating a public issue.

## Project direction

The current focus is reliability, safe Explorer integration, accessibility, and a polished
Windows experience. A small official website may be added later as a clearer home for
downloads and documentation; GitHub Releases remains the source of truth for binaries.
If FolderGlimpse is useful to you, starring the repository is a welcome way to support it.

Technical details are available in [docs/architecture.md](docs/architecture.md), with the
Windows QA checklist in [docs/manual-testing.md](docs/manual-testing.md). Maintainers can
also consult the [release runbook](docs/releasing.md), [signing guide](docs/signing.md), and
[code-signing policy](docs/code-signing-policy.md), plus the [distribution roadmap](docs/distribution.md).
The manual GitHub security controls are listed in
[repository settings](docs/repository-settings.md).

## License

FolderGlimpse is open-source software licensed under the [MIT License](LICENSE).
See [licensing notes](docs/licensing.md) for the project's contribution terms.

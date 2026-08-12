# Privacy

FolderGlimpse is designed to operate locally on the Windows computer where it is installed.

## Data the application uses

- The currently selected local folder path and its immediate entries, only to render a preview.
- Windows Explorer focus and selection metadata, to decide whether the configured shortcut is safe.
- Local preferences and onboarding state under `%LOCALAPPDATA%\FolderGlimpse`.
- An optional per-user Windows startup registration when the user enables launch at sign-in.
- An optional local `diagnostics.log` when the app is explicitly started with `--diagnostics`.
  This log may contain selected folder paths, Explorer window identifiers, and error messages.

## Data the application does not collect

FolderGlimpse does not include analytics, advertising, telemetry, user accounts, or a cloud
service. It does not transmit folder names, file names, paths, settings, or usage information
to the project maintainer.

Diagnostics are disabled during normal launches. When enabled, the log remains under
`%LOCALAPPDATA%\FolderGlimpse` (unless the user explicitly configures another local path) and is
never uploaded automatically. Review and redact private paths before sharing it in a support report.

Opening a file delegates to its normal Windows application. That application may have its own
network and privacy behavior, which FolderGlimpse does not control.

## Removing local data

Exit FolderGlimpse, disable launch at startup, delete the executable, and optionally delete
`%LOCALAPPDATA%\FolderGlimpse` to remove saved preferences and application state.
This also removes the default diagnostic log.

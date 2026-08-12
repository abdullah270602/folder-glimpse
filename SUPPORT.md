# Support

## Before asking for help

1. Confirm you are using Windows 11 x64 and the newest official GitHub Release.
2. Exit FolderGlimpse from the tray, start it again, and reproduce the issue.
3. Check the [README](README.md) limitations and [manual-testing guide](docs/manual-testing.md).
4. Search existing [issues](https://github.com/abdullah270602/folder-glimpse/issues).

For usage questions or reproducible bugs, open a GitHub issue and include the FolderGlimpse
version, Windows version, Explorer view, exact steps, expected behavior, and observed behavior.
Screenshots are useful when they do not expose private filenames or paths.

For a difficult Explorer-integration problem, start the app once with `--diagnostics`, reproduce
the problem, exit, and inspect `%LOCALAPPDATA%\FolderGlimpse\diagnostics.log`. The log can contain
selected folder paths, so redact it before attaching any excerpt. Diagnostics remain local and are
never uploaded by FolderGlimpse.

Do not post vulnerability details publicly. Follow [SECURITY.md](SECURITY.md) for private
security reporting. Community support is best-effort; no service-level agreement is offered.

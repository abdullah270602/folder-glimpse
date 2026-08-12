# Contributing to FolderGlimpse

Thank you for helping improve FolderGlimpse. The project favors focused, reliable changes
that preserve safe keyboard handling and the native Windows experience.

## Before you start

- Search existing issues before opening a new one.
- For a larger feature or behavioral change, open an issue first so the approach can be
  discussed before significant implementation work.
- Do not report security vulnerabilities in public issues; follow [SECURITY.md](SECURITY.md).

## Development setup

FolderGlimpse requires Windows 11 x64 and the .NET 8 SDK.

```powershell
git clone git@github.com:abdullah270602/folder-glimpse.git
cd folder-glimpse
dotnet restore FolderGlimpse.sln
dotnet build FolderGlimpse.sln -c Debug
dotnet run --project tests/FolderGlimpse.Tests/FolderGlimpse.Tests.csproj -c Debug
```

## Pull requests

1. Create a focused branch from the current default branch.
2. Keep unrelated formatting or refactors out of the change.
3. Add or update tests when behavior changes.
4. Run the full Release build and test executable.
5. Complete the relevant items in [docs/manual-testing.md](docs/manual-testing.md) for
   Windows UI, input, Explorer, tray, startup, or DPI changes.
6. Explain the user-facing outcome, risks, and actual verification in the pull request.

## Safety requirements

Changes must preserve these invariants:

- If Explorer eligibility is stale or uncertain, keyboard input passes through.
- The low-level keyboard hook must not perform filesystem, COM, UI Automation, WPF, or
  blocking work.
- A captured key-down owns the complete key gesture through its matching key-up.
- Folder enumeration remains nonrecursive, cancellable, and off the UI thread.
- The preview must not steal focus in momentary mode.
- A second app launch must not create another hook or tray icon.

See [docs/architecture.md](docs/architecture.md) for the design rationale.

## Style

- Follow the existing C# and XAML conventions.
- Prefer small changes and existing services over new frameworks or dependencies.
- Keep user-facing text concise and use the shared theme resources for UI colors.
- Do not add telemetry, network services, accounts, or cloud behavior without prior discussion.

## Licensing

FolderGlimpse is licensed under the [MIT License](LICENSE). By submitting a contribution,
you agree to license it under the same terms and confirm that you have the right to do so.

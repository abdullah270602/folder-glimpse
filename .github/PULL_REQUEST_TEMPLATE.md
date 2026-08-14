## Summary

Describe the user-visible outcome and why the change is needed.

## Validation

- [ ] `dotnet build FolderGlimpse.sln -c Debug`
- [ ] `dotnet build FolderGlimpse.sln -c Release`
- [ ] `dotnet run --project tests/FolderGlimpse.Tests/FolderGlimpse.Tests.csproj -c Release`
- [ ] `dotnet run --project tests/FolderGlimpse.UiTests/FolderGlimpse.UiTests.csproj -c Release`
- [ ] Relevant Windows 11 manual checks completed
- [ ] No secrets, certificates, generated binaries, or personal paths were added

## Safety review

- [ ] Explorer search, address, rename, menu, and dialog contexts still pass shortcuts through
- [ ] An owned key-down still owns its matching repeats and key-up
- [ ] The preview never steals focus or remains stranded above another application
- [ ] Documentation and screenshots were updated when behavior changed

## Screenshots

Include before/after images for visual changes, with private paths and filenames removed.

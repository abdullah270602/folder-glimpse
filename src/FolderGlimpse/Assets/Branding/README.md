# FolderGlimpse branding assets

The three PNG files in `Source` are the approved app, in-app mark, and tray references. Generated
production files are created by `scripts/generate-brand-assets.py`:

- `FolderGlimpse-App.ico` contains 256, 128, 64, 48, 32, 24, 20, and 16 px frames for the
  executable, Explorer, taskbar, and window chrome.
- `FolderGlimpse-Tray.ico` contains dedicated, sharpened 32, 24, 20, and 16 px frames derived from
  the approved compact folder-and-document tray artwork.
- `FolderGlimpse-Mark-512.png` and `FolderGlimpse-Mark-256.png` are transparent in-app marks for
  Home, Welcome, Settings, About, and shell branding.

Regenerate from the repository root with Pillow installed:

```powershell
python scripts/generate-brand-assets.py `
  --app-master src/FolderGlimpse/Assets/Branding/Source/FolderGlimpse-App-Master.png `
  --mark-master src/FolderGlimpse/Assets/Branding/Source/FolderGlimpse-Mark-Master.png `
  --tray-master src/FolderGlimpse/Assets/Branding/Source/FolderGlimpse-Tray-Master.png `
  --output src/FolderGlimpse/Assets/Branding
```

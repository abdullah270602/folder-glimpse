# Changelog

All notable FolderGlimpse changes will be documented here. The project follows
[Semantic Versioning](https://semver.org/) and uses annotated tags such as `v1.0.0`.

## Unreleased

### Added

- Configurable hover-to-glimpse modes for the selected folder or any folder under the pointer,
  with adjustable delay, exit grace, movement tolerance, and optional Ctrl/Shift modifiers.
- Windows 11 Explorer folder previews with tap and hold gestures.
- Interactive sticky previews, multi-selection, and safe context actions.
- Configurable appearance, shortcut, startup, sorting, and item-display preferences.
- Control center, tray integration, onboarding, and local settings persistence.
- Reproducible CI, release packaging, checksum, SBOM, provenance, and signing gates.
- Production application, taskbar, tray, and in-app branding assets derived from the approved
  FolderGlimpse folder-and-document mark.

### Changed

- Positioned hover as the primary pointer-first workflow while keeping Space and Ctrl+Space as
  configurable keyboard triggers.
- Replaced continuous Explorer eligibility polling with debounced Windows accessibility events
  and a slow fallback refresh to reduce idle CPU use.
- Fixed hover targeting across the Details Name column and larger icon layouts.
- Standardized wordmarks, primary buttons, switches, sliders, selection borders, links, and tray
  accents on the approved FolderGlimpse blue palette.
- Standardized in-app brand-mark sizing across Home, Settings, Welcome, and About surfaces.
- Simplified the tray menu, centered action labels within their hover surfaces, and removed the
  redundant About action while retaining About in the control center.
- Limited the preview viewport to ten visible rows with a cleaner themed scrollbar for longer
  folder listings.

The owner will choose the first public release version after production signing and release QA
are available. Prereleases use identifiers such as `v1.0.0-beta.1`.

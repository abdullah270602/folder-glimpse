# Changelog

All notable FolderGlimpse changes will be documented here. The project follows
[Semantic Versioning](https://semver.org/) and uses annotated tags such as `v0.1.0-beta.1`.

## Unreleased

### Changed

- Enabled Any-folder hover by default on fresh installs and resets while retaining Space as the
  default keyboard trigger.
- Reworked Home and How to Use around the hover-first workflow, with keyboard tap/hold presented
  as a configurable alternative.
- Added deliberate click-to-pin promotion from a hover glimpse to the existing sticky interactive
  popup without closing or reloading it.
- Stopped high-frequency hover pointer sampling whenever Explorer is not foreground.
- Clarified the recommended portable ZIP download and distinguished user downloads from checksum
  and SBOM metadata.

## [0.1.0-beta.1] - 2026-08-15

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

### Fixed

- Hover glimpses now accept file and folder double-clicks without taking keyboard focus; momentary
  previews remain view-only and sticky previews retain full selection and context actions.

This is the first public test build. It is intentionally unsigned while trusted open-source
signing is pending; release checksums, an SPDX SBOM, and GitHub provenance attestations accompany
the downloadable artifacts.

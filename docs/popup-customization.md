# Popup customization specification

## Goals

Popup customization should let users remove visual chrome and choose a predictable placement
without weakening FolderGlimpse's responsiveness, accessibility, error reporting, or mixed-DPI
placement guarantees. Existing settings files must retain the current appearance.

## Persistent settings and defaults

| Setting | Values | Default | Behavior |
| --- | --- | --- | --- |
| Header style | Full / Compact / Hidden | Full | Full shows icon, folder name, and the optional full path. Compact shows icon and name. Hidden collapses the header and its divider. |
| Footer style | Smart / Always / Hidden | Always | Always shows the item count. Smart shows only truncated-result notices. Hidden removes count chrome. Errors remain visible in the body in every mode. |
| Entry icons | On / Off | On | Off collapses the icon column; it also skips shell icon extraction entirely. |
| Visible rows | Auto / 5 / 8 / 10 / 15 | 10 | A finite value caps the list before scrolling. Auto uses the configured maximum height and monitor work area. |
| Preferred side | Auto / Right / Left / Below / Above | Auto | The preference is tried first, then safe fallbacks. The final popup remains inside the target monitor work area. |

`ShowFullPath` remains persisted for backward compatibility and only affects the Full header.
Width, maximum height, density, size, and modified-date settings remain independent.

## Presets

Presets update the underlying settings rather than becoming a second source of truth. The Settings
UI reports Custom whenever individual values no longer match a preset.

| Preset | Header | Footer | Icons | Density | Rows | Path | Size | Date |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Minimal | Hidden | Hidden | Off | Compact | 8 | Off | Off | Off |
| Balanced | Compact | Smart | On | Comfortable | 10 | Off | On | Off |
| Detailed | Full | Always | On | Comfortable | 10 | On | On | On |

Placement, width, maximum height, theme, sorting, filtering, and interaction preferences are not
changed by presets.

## Layout and status invariants

- Collapsed header/footer rows and their dividers consume zero height.
- Empty folders retain the centered empty message.
- Read failures show a centered, user-safe error message even when the footer is Hidden.
- Smart footer shows truncated-result notices, but ordinary counts are omitted.
- Action controls for multi-selection remain independent of footer visibility.
- Hiding icons prevents icon extraction work; it is not merely a visual collapse.
- Loading, error, and late icon results remain generation/cancellation safe.
- Auto rows never exceed maximum popup height or the monitor work area.

## Placement rules

All placement calculations use signed physical pixels. A side fully fits only when the popup plus
the gap fits between the anchor and that work-area edge. Auto tries Right, Left, Below, then Above.
An explicit preference tries that side first, then its opposite, then the two orthogonal sides. If
no side fully fits, the preferred candidate is clamped inside the work area. Negative monitor
coordinates, small work areas, taskbars on any edge, and popup sizes larger than the work area are
supported.

## Deferred options

Text scale and folders-only/files-only filters remain candidates for a later release. Arbitrary
fonts, opacity, custom colors, padding sliders, and thumbnails are intentionally excluded because
they add visual inconsistency, accessibility risk, or background work disproportionate to their
value.

## Verification

- Persistence tests for defaults, partial legacy JSON, invalid enum/row values, round trips, and
  reset.
- Preset mapping and Custom detection tests.
- Geometry truth-table and randomized signed-coordinate tests for every placement preference.
- WPF checks for header/path/footer/divider/icon visibility and error visibility.
- Rendered screenshots for Full, Compact, Hidden/Minimal, Smart footer, Detailed, and light/dark.
- Enumeration verification that icon extraction is skipped when icons are hidden.
- Release build, complete automated suites, release tooling, clean publish, CI, CodeQL, and final
  GitHub release artifact verification.

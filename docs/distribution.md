# Distribution roadmap

## V1: portable beta, then trusted stable release

Retain a self-contained Windows 11 x64 portable EXE as the smallest reliable first distribution.
The initial beta is explicitly unsigned; stable releases require trusted Authenticode signing:

- `FolderGlimpse.exe`
- `FolderGlimpse-win-x64.zip`
- `SHA256SUMS.txt`
- `FolderGlimpse.spdx.json`
- GitHub provenance/SBOM attestations

It requires no administrator access, preserves the existing per-user startup registration, and is
easy to remove. Users select a permanent location and replace the EXE to update.

| Format | Benefits | Costs and current fit |
|---|---|---|
| Portable signed EXE | No installer, no admin, simplest release and rollback, current behavior unchanged | Manual placement/update; no Start Menu entry or registered uninstall record |
| Signed MSI | Familiar enterprise install, explicit upgrade/uninstall identity, Start Menu and Add/Remove Programs support | Requires installer authoring, stable component/product upgrade rules, separate signing, and careful per-user/machine choice |
| Signed MSIX | Clean identity/uninstall, package isolation, Store-ready path, possible update integration | Requires package identity/certificate alignment and testing of startup registration, Shell/UI Automation, executable activation, and update behavior |

An installer should be added only after real user demand justifies its maintenance. If added, prefer
per-user installation under `%LOCALAPPDATA%`, avoid administrator privileges, preserve the exact
publisher identity, register startup through a package-compatible mechanism, and test upgrades and
uninstall without leaving settings or startup entries unexpectedly.

## Website

A future official site should remain a presentation layer, not a second binary origin:

- Primary **Download for Windows** links to the exact latest GitHub Release asset.
- Releases, signature subject, version, SHA-256, privacy, security, and support are visible.
- A **Star on GitHub** action is secondary and never required for downloading.
- The site may resolve the newest asset through the GitHub Releases API, with a releases-page
  fallback when an API call fails.
- It must never mirror or serve a different unverified executable.
- GitHub Releases remains canonical until an equally controlled distribution system is approved.

Do not publish a site or invent a domain before the owner separately authorizes hosting and DNS.

## WinGet preparation

Submit only after stable, publicly accessible, Authenticode-signed releases are proven reliable.

Proposed identity (subject to availability and publisher agreement):

- Package identifier: `AbdullahNaseem.FolderGlimpse`
- Publisher: must exactly match the approved public product/publisher identity where applicable
- Installer type: portable initially
- Architecture: x64
- Scope: user
- Release URL: immutable GitHub Release asset

For each version, create manifests with the immutable release URL and the SHA-256 of the published
EXE. Test `winget validate`, silent installation behavior, startup state, upgrade replacement, and
uninstall expectations in Windows Sandbox before submitting to `microsoft/winget-pkgs`. A portable
package does not provide a full MSI-style uninstall, so the manifest and documentation must be
truthful about cleanup.

A future automation may open a WinGet manifest-update pull request only after the GitHub Release is
signed, immutable, and clean-machine verified. It must read the released asset hash rather than a
local pre-signing artifact. No WinGet submission is part of the current repository preparation.

## Microsoft Store

Evaluate MSIX/Store distribution later if automatic updates, package identity, and improved user
acquisition outweigh the packaging and policy cost. It should complement—not silently diverge
from—the canonical source and release history.

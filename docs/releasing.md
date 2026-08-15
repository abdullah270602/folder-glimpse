# Maintainer release runbook

GitHub Releases is the canonical FolderGlimpse binary source. Every public release includes the
portable EXE, ZIP, SHA-256 checksum list, SPDX SBOM, and GitHub provenance attestations.

Until trusted signing is approved, only explicitly labeled `beta.N` or `rc.N` prereleases may be
published unsigned. Stable releases remain blocked until a real production signing service is
configured as described in [signing.md](signing.md). The repository is licensed under the MIT License.

## Version policy

- Stable: `vMAJOR.MINOR.PATCH`, for example `v1.0.0`.
- Prerelease: `vMAJOR.MINOR.PATCH-beta.N`, for example `v0.1.0-beta.1`.
- Release tags are annotated and maintainer-signed.
- The workflow derives assembly, file, informational, and asset versions from the tag.
- The About page continues to read assembly metadata; it must not hardcode a release number.
- `CHANGELOG.md` is updated before tagging. GitHub-generated notes supplement rather than replace
  important compatibility, security, and migration notes.

Because FolderGlimpse has not yet had a public, signed release, `v0.1.0-beta.1` is the selected
first public test version. It is an unsigned beta with checksums, SBOM, provenance, malware scan,
and launch validation—not a trusted production release.

## 1. Prepare

1. Confirm `git status --short` contains only intended changes.
2. Confirm the repository-local identity is `Abdullah Naseem <abdullahnaseem27@gmail.com>`.
3. Move the relevant `CHANGELOG.md` entries out of Unreleased.
4. Confirm the README accurately identifies the current channel as unsigned beta or trusted stable.
   Never claim a verified publisher until the shipped artifact has been independently checked.
5. Review privacy, security, compatibility, and uninstall documentation.
6. Review dependency and CodeQL alerts.
7. Complete every applicable item in [manual-testing.md](manual-testing.md), especially physical
   keyboard, Explorer focus exclusions, session changes, and mixed-DPI cases.

## 2. Build and test locally

```powershell
dotnet restore FolderGlimpse.sln --configfile NuGet.config
dotnet build FolderGlimpse.sln -c Debug --no-restore
dotnet build FolderGlimpse.sln -c Release --no-restore
dotnet run --project tests/FolderGlimpse.Tests/FolderGlimpse.Tests.csproj -c Release --no-build
dotnet run --project tests/FolderGlimpse.UiTests/FolderGlimpse.UiTests.csproj -c Release --no-build
```

Run a safe unsigned packaging rehearsal through GitHub Actions using **Release → Run workflow**.
Supply a prerelease-shaped version such as `0.1.0-dryrun.1`. This path builds artifacts but cannot
access signing credentials or publish a GitHub Release.

## 3. Create the release tag

After committing the reviewed release preparation, create a signed annotated tag locally:

```powershell
git tag -s v0.1.0-beta.1 -m "FolderGlimpse v0.1.0-beta.1"
git tag -v v0.1.0-beta.1
```

Pushing is an explicit owner action:

```powershell
git push origin main
git push origin v0.1.0-beta.1
```

The workflow rejects malformed and lightweight tags and ensures the tag resolves to its workflow
commit. Protect the tag namespace so only maintainers can create or delete release tags.

## 4. Observe the selected release channel

For an unsigned beta:

1. Confirm the build/test candidate and unsigned-beta policy gate are green.
2. Confirm Microsoft Defender/basic scanning completed or review any explicit runner warning.
3. Confirm the ZIP extraction and launch smoke test passed.
4. Confirm checksums, SBOM, and provenance attestations target the final published bytes.
5. Confirm the GitHub Release is marked **Prerelease** and its title and notes say **unsigned beta**.

For a stable release, trusted signing is mandatory:

1. Confirm the build/test candidate job is green.
2. Review the exact commit, tag, unsigned candidate hash, and workflow initiator.
3. A required reviewer approves the GitHub `production-signing` environment.
4. Approve the corresponding provider signing request only if its origin and artifact match.
5. Confirm `Get-AuthenticodeSignature` reports `Valid`, a signer certificate, and timestamp.
6. Confirm `signtool verify /pa /all /v` succeeds.
7. Confirm Microsoft Defender/basic scanning completed or review any explicit runner warning.
8. Confirm checksums were calculated only after signing and ZIP creation.
9. Confirm SBOM and provenance attestations target the released hashes.

The workflow fails closed when the selected channel violates policy: an unsigned tag that is not a
numbered beta/RC cannot publish, and a stable tag cannot publish without trusted signing,
timestamping, packaging, and validation.

## 5. Verify the published release

Download all assets into a new directory on a clean Windows 11 x64 account or machine:

```powershell
Get-FileHash .\FolderGlimpse.exe -Algorithm SHA256
Get-FileHash .\FolderGlimpse-win-x64.zip -Algorithm SHA256
Get-FileHash .\FolderGlimpse.spdx.json -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
gh attestation verify .\FolderGlimpse.exe --repo abdullah270602/folder-glimpse
gh attestation verify .\FolderGlimpse-win-x64.zip --repo abdullah270602/folder-glimpse
```

For a trusted stable release, additionally run:

```powershell
Get-AuthenticodeSignature .\FolderGlimpse.exe | Format-List *
signtool verify /pa /all /v .\FolderGlimpse.exe
```

For an unsigned beta, confirm `Get-AuthenticodeSignature` reports `NotSigned` and that the release
notes disclose this. A beta signature must never be implied by checksums or GitHub attestations.

Extract the ZIP, confirm its EXE hash equals the standalone EXE, launch it, complete onboarding,
preview the test folders, enable/disable startup, exit, relaunch, and uninstall using the README
instructions. Record actual SmartScreen behavior without promising future warning-free downloads.

Use GitHub's attestation verification instructions to verify the release against
`abdullah270602/folder-glimpse`. Confirm release notes, version metadata, asset names, and direct
README links all resolve to the same release.

## 6. Immutability and rollback

Do not replace binaries inside an existing release. If a release is defective:

1. Mark it clearly as affected and stop recommending it.
2. Disable or remove the release only when necessary to protect users; retain incident evidence.
3. Fix forward with a new semantic version, rebuilt through the complete channel-specific pipeline.
4. Revoke a signing certificate only for actual key/identity compromise, following the CA or
   signing-provider process.
5. Publish a security advisory when confidentiality allows.

Never reuse an old version number or silently mutate a checksum. Update WinGet only after the new
release has passed clean-machine validation.

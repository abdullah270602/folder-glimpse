# Maintainer release runbook

GitHub Releases is the canonical FolderGlimpse binary source. Production release assets are the
signed portable EXE, ZIP, SHA-256 checksum list, SPDX SBOM, and GitHub provenance attestations.

No production release may proceed until a real production signing service is configured as
described in [signing.md](signing.md). The repository is licensed under the MIT License.

## Version policy

- Stable: `vMAJOR.MINOR.PATCH`, for example `v1.0.0`.
- Prerelease: `vMAJOR.MINOR.PATCH-beta.N`, for example `v1.0.0-beta.1`.
- Release tags are annotated and maintainer-signed.
- The workflow derives assembly, file, informational, and asset versions from the tag.
- The About page continues to read assembly metadata; it must not hardcode a release number.
- `CHANGELOG.md` is updated before tagging. GitHub-generated notes supplement rather than replace
  important compatibility, security, and migration notes.

Because FolderGlimpse has not yet had a public, signed release, `v0.1.0` or
`v1.0.0-beta.1` is the recommended first public test version. The owner must choose; this repository
preparation does not create or assign a tag.

## 1. Prepare

1. Confirm `git status --short` contains only intended changes.
2. Confirm the repository-local identity is `Abdullah Naseem <abdullahnaseem27@gmail.com>`.
3. Move the relevant `CHANGELOG.md` entries out of Unreleased.
4. Before the first release, replace the README's “no official binary” and “not code-signed yet”
   notices with the verified publisher/signature state. Never update those claims speculatively.
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
```

Run a safe unsigned packaging rehearsal through GitHub Actions using **Release → Run workflow**.
Supply a prerelease-shaped version such as `1.0.0-dryrun.1`. This path builds artifacts but cannot
access signing credentials or publish a GitHub Release.

## 3. Create the release tag

After committing the reviewed release preparation, create a signed annotated tag locally:

```powershell
git tag -s v1.0.0 -m "FolderGlimpse v1.0.0"
git tag -v v1.0.0
```

Pushing is an explicit owner action:

```powershell
git push origin main
git push origin v1.0.0
```

The workflow rejects malformed and lightweight tags and ensures the tag resolves to its workflow
commit. Protect the tag namespace so only maintainers can create or delete release tags.

## 4. Approve signing and observe the pipeline

1. Confirm the build/test candidate job is green.
2. Review the exact commit, tag, unsigned candidate hash, and workflow initiator.
3. A required reviewer approves the GitHub `production-signing` environment.
4. Approve the corresponding provider signing request only if its origin and artifact match.
5. Confirm `Get-AuthenticodeSignature` reports `Valid`, a signer certificate, and timestamp.
6. Confirm `signtool verify /pa /all /v` succeeds.
7. Confirm Microsoft Defender/basic scanning completed or review any explicit runner warning.
8. Confirm checksums were calculated only after signing and ZIP creation.
9. Confirm SBOM and provenance attestations target the released hashes.

The workflow fails closed and creates no release when licensing, signing, signature verification,
timestamping, packaging, or validation fails.

## 5. Verify the published release

Download all assets into a new directory on a clean Windows 11 x64 account or machine:

```powershell
Get-FileHash .\FolderGlimpse.exe -Algorithm SHA256
Get-FileHash .\FolderGlimpse-win-x64.zip -Algorithm SHA256
Get-FileHash .\FolderGlimpse.spdx.json -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
Get-AuthenticodeSignature .\FolderGlimpse.exe | Format-List *
signtool verify /pa /all /v .\FolderGlimpse.exe
```

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
3. Fix forward with a new semantic version, rebuilt and signed through the complete pipeline.
4. Revoke a signing certificate only for actual key/identity compromise, following the CA or
   signing-provider process.
5. Publish a security advisory when confidentiality allows.

Never reuse an old version number or silently mutate a checksum. Update WinGet only after the new
release has passed clean-machine validation.

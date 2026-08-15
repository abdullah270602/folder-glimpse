# FolderGlimpse code-signing policy

## Purpose and scope

Code signing identifies trusted FolderGlimpse Windows release artifacts and protects their
integrity. This policy covers stable executables and packages distributed through the canonical
GitHub Releases page. Pull-request, branch, local, and dry-run builds remain unsigned. Until trusted
signing is approved, explicitly labeled `beta.N` or `rc.N` prereleases may also be published
unsigned under the separate controls below.

## Temporary unsigned prerelease channel

An unsigned prerelease is an official test build, but it is not a trusted production build. It may
be published only from an annotated semantic-version tag already contained in `main`, after build,
test, metadata, malware-scan, packaging, extraction, and launch checks pass. The workflow publishes
SHA-256 checksums, an SPDX SBOM, and GitHub provenance attestations for the final EXE and ZIP.

The README and release notes must say that the beta is unsigned and may trigger SmartScreen. They
must direct users to canonical GitHub Releases and must never ask users to disable SmartScreen,
ignore security warnings, or install a self-signed root certificate. Stable tags remain fail-closed
until the trusted signing configuration and approvals below are available.

## Authorized releases

Only a protected semantic-version tag whose commit is already contained in `main` may request
production signing. The tag must be annotated and maintainer-signed. The complete CI build and test
job must succeed before a signing request is eligible.

A production signing request requires two independent approvals:

1. A required reviewer approves the GitHub `production-signing` Environment after checking the
   tag, commit, workflow origin, test results, and unsigned artifact.
2. An authorized release approver accepts the matching signing request in the signing provider.

The person reviewing should not approve an unexpected request, rerun, artifact, identity, or source
revision. Emergency bypasses are not a normal release path.

## Key custody and identity

The private Authenticode key is generated and retained by an approved HSM-backed signing service.
It is never exported to a maintainer computer, GitHub secret, repository, build artifact, backup,
or password manager. A narrowly scoped provider service token may submit requests but cannot bypass
provider policy and human approval.

The production workflow compares the resulting certificate subject with the owner-approved exact
`SIGNING_CERTIFICATE_SUBJECT`. It rejects another identity even when Windows considers that
certificate valid. Certificate issuance, renewal, and identity changes require owner review and a
documented update to the protected environment.

## Signature requirements

Every production executable must:

- use Authenticode with a SHA-256 file digest;
- include an RFC 3161 timestamp issued while the signer certificate is valid;
- pass `Get-AuthenticodeSignature` with `Valid` status;
- pass Windows SDK `signtool verify /pa /all /v`;
- match the exact post-signing SHA-256 recorded in the SPDX SBOM and `SHA256SUMS.txt`;
- pass release-bundle, extraction, launch, and malware checks before publication.

Signing proves publisher identity and file integrity. The project does not promise immediate or
permanent Microsoft SmartScreen reputation.

## Audit and publication

GitHub Actions and the provider retain the build origin, request, approver, certificate, timestamp,
and artifact evidence. The release attaches the signed EXE, ZIP containing that exact EXE, checksum
file, SPDX SBOM, and GitHub attestations. Releases and tags are treated as immutable; a corrected
artifact receives a new version rather than replacing an existing download.

Release evidence may disclose certificate subjects, thumbprints, expiration dates, timestamps,
commit IDs, workflow IDs, and artifact hashes. It must never disclose tokens, authentication
material, private keys, or secret values.

## Renewal and compromise

Maintainers review certificate and provider-token expiry at least quarterly and start certificate
renewal at least 60 days before expiration. A renewed certificate must complete a controlled
non-public verification before its exact subject is approved for production.

On suspected compromise:

1. Disable the `production-signing` Environment and provider policy immediately.
2. Revoke or rotate affected submission credentials and remove unauthorized approvers.
3. Preserve GitHub, provider, and release evidence.
4. Identify every artifact signed during the possible exposure window.
5. Coordinate certificate revocation with the provider or certificate authority when warranted.
6. Notify users through a security advisory and release notes.
7. Fix forward through the complete reviewed pipeline using a new version and trusted identity.

Published assets are never silently replaced. See [the release runbook](releasing.md) for the
operational procedure and [the signing guide](signing.md) for provider setup.

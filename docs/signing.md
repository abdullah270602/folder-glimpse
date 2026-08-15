# Production code signing

FolderGlimpse stable production releases must be Authenticode-signed and RFC 3161 timestamped. A
temporary unsigned beta/RC channel is governed separately by the
[code-signing policy](code-signing-policy.md); it never claims a trusted publisher identity. The
private signing key must remain in an HSM or managed signing service and must never be committed,
exported into the repository, or printed by CI.

The normative project controls are defined in the public
[FolderGlimpse code-signing policy](code-signing-policy.md).

## Practical options

| Option | Eligibility and cost | Key custody and CI | Recommendation |
|---|---|---|---|
| [SignPath Foundation](https://signpath.org/) | Free for qualifying established open-source projects. Requires an OSI-approved license, public project activity, documented security/signing policy, MFA, and SignPath approval. | SignPath keeps the key in an HSM and verifies GitHub build origin. Its GitHub action supports an approval-controlled signing request. | Preferred first application after FolderGlimpse establishes public project history. Approval is not guaranteed. |
| [Microsoft Artifact Signing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) (formerly Trusted Signing) | Managed monthly service; Microsoft lists a Basic tier with a signature allowance. Public Trust identity validation is limited to supported countries and organization/individual types. Confirm current regional eligibility before purchase. | Microsoft-managed HSM. GitHub can authenticate to Azure with OIDC instead of a client secret. | Strong option for an eligible organization. An individual located outside a supported region may not qualify. |
| Traditional OV certificate | Commercial pricing varies by certificate authority, commonly a few hundred US dollars per year plus identity validation. Current industry rules require protected hardware or cloud key storage. | Use the CA token/HSM or a reputable cloud-signing integration. Avoid an exportable PFX in GitHub Secrets. | Most realistic paid fallback when managed-service eligibility is unavailable. |
| EV certificate | More expensive and stricter organization validation than OV. | Hardware or cloud HSM is required. | Not justified solely for SmartScreen: Microsoft no longer promises immediate reputation bypass. |

Microsoft Store/MSIX is a separate future distribution path. Store submission causes Microsoft
to sign the package and may improve the download experience, but packaging, identity, and Store
policy add maintenance beyond the portable V1 release.

Authenticode proves publisher identity and integrity. It does **not** guarantee that SmartScreen
will never warn; reputation is evaluated separately and may need to accumulate. See Microsoft's
[SmartScreen reputation guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

## Recommended FolderGlimpse path

1. Publish the MIT-licensed repository and establish public project history.
2. Enable MFA, private vulnerability reporting, secret scanning, push protection, branch rules,
   protected tags, and a reviewer-protected `production-signing` environment.
3. Establish public release notes, security policy, uninstall instructions, and a documented
   signing policy.
4. Apply to SignPath Foundation. If the project is not accepted, obtain an OV certificate backed
   by the provider's HSM/cloud-signing service. Consider Artifact Signing only if the owner has an
   eligible organization in a supported region.
5. Configure the existing production workflow only after the provider project and identity have
   been approved.

The checked-in release workflow currently models the SignPath route and fails closed if its
production environment, license, or settings are missing. Switching providers should replace only
the signing step; build, signature verification, checksum, SBOM, attestation, and release gates
must remain unchanged.

## External SignPath setup

These operations are intentionally not automated from the repository:

1. Create the SignPath organization and complete MFA/identity requirements.
2. Apply for the Foundation plan and provide the public repository, license, privacy policy,
   security policy, release history, and maintainer information requested by SignPath.
3. Connect the GitHub repository as a trusted build system.
4. Create a project, artifact configuration for the single `FolderGlimpse.exe`, and a production
   signing policy that requires manual approval.
5. Configure an RFC 3161 timestamp server in the provider policy.
6. In GitHub, create the `production-signing` Environment with required reviewers and restrict it
   to protected `v*` tags.
7. Add environment variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
   `SIGNPATH_SIGNING_POLICY_SLUG`, and `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`.
8. Add `SIGNING_CERTIFICATE_SUBJECT` with the exact expected certificate subject distinguished
   name shown by the approved provider certificate. Review and update it deliberately on renewal.
9. Add the provider service token as the environment secret `SIGNPATH_API_TOKEN`. Never print or
   copy its value into an issue, log, commit, or local documentation.
10. Rotate/revoke the token according to the provider policy; production signing approval remains a
   separate control even when the token is valid.

If Artifact Signing is selected instead, use [GitHub OIDC for Azure](https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-azure),
assign the minimum certificate-profile signer role to a dedicated federated identity, restrict its
subject to the production environment, and do not create a client secret.

## Verification

The production workflow must pass both PowerShell and Windows SDK verification:

```powershell
$signature = Get-AuthenticodeSignature .\FolderGlimpse.exe
$signature | Format-List Status, StatusMessage
$signature.SignerCertificate | Format-List Subject, Thumbprint, NotAfter
$signature.TimeStamperCertificate | Format-List Subject, Thumbprint, NotAfter

signtool verify /pa /all /v .\FolderGlimpse.exe
```

Accept the release only if the status is `Valid`, the expected publisher identity is shown, an
RFC 3161 timestamp certificate is present, and SignTool exits successfully. Record the subject,
thumbprint, certificate expiry, timestamp, release tag, commit, and artifact SHA-256 in the release
evidence without recording any private credentials.

## Expiry and incident response

Timestamped signatures remain verifiable after the signing certificate expires, provided it was
valid at signing time and the timestamp chain remains valid. Start renewal at least 60 days before
expiry and test the renewed identity in a non-production provider policy.

If a token or signing identity may be compromised: disable the production environment, revoke the
provider credential, stop releases, preserve logs, identify every affected signature, notify users,
and follow the provider/CA revocation process. Do not silently replace published assets; publish a
clear incident notice and a new version after remediation.

# Required GitHub repository settings

These settings cannot be safely created by repository files alone. The owner should enable them
after the initial reviewed branch is pushed and before accepting contributions or publishing a
production release.

## General and Actions

- Set `main` as the default branch.
- Disable force pushes and branch deletion for `main`.
- In Actions settings, allow only actions and reusable workflows explicitly needed by the project;
  require actions to be pinned to full commit SHAs when the account plan supports that policy.
- Keep the default `GITHUB_TOKEN` permission read-only and do not allow Actions to approve pull
  requests. Individual jobs elevate only their documented minimum permissions.
- Require approval for workflows first run by outside contributors.

## Main-branch ruleset

Require pull requests for `main` with:

- at least one approval;
- dismissal of stale approvals after new commits;
- code-owner review for signing, workflow, input, and Explorer-integration paths;
- approval of the most recent push by someone other than its author;
- resolved review conversations;
- required status checks: the CI build/test/smoke job and CodeQL;
- branch up to date before merge;
- blocked force pushes and deletions.

Use a small, documented emergency bypass group only if operationally necessary. Do not make normal
maintenance dependent on bypassing checks.

## Release tags and environment

- Add a ruleset for `v*` tags restricting creation, update, and deletion to release maintainers.
- Require signed annotated tags as a maintainer practice.
- Treat numbered beta/RC tags as the only unsigned public-release namespace; stable tags always use
  the trusted signing environment.
- Create an Environment named exactly `production-signing`.
- Add a required reviewer who checks commit, tag, test results, artifact origin, and provider request.
- Restrict deployment branches/tags to the protected semantic-version tag pattern.
- Store all SignPath variables and the `SIGNPATH_API_TOKEN` secret in this Environment—not as broad
  repository secrets—so pull requests and unsigned dry runs cannot access them.
- Set `SIGNING_CERTIFICATE_SUBJECT` to the exact approved Authenticode publisher subject. The
  workflow rejects a valid certificate belonging to a different identity.
- Keep provider-side manual signing approval enabled as a second authorization boundary.

## Security features

Enable:

- private vulnerability reporting;
- Dependabot alerts and security updates;
- dependency graph;
- secret scanning and push protection;
- CodeQL/default code-scanning visibility;
- automatic deletion prevention or immutable releases where the plan/account provides it.

Review alerts rather than automatically suppressing them. The repository workflows already run
dependency review on pull requests and weekly CodeQL analysis.

## Publishing policy

Treat releases as immutable. Do not replace an asset under an existing version or reuse a tag. Keep
GitHub Releases as the initial canonical host, verify the uploaded hash and attestation—and the
Authenticode signature for stable releases—on a clean Windows system before updating external
download pages or WinGet manifests.

# Update implementation plan

This file is intentionally persistent while the update feature is incomplete.
Remove it only after every phase below is implemented, documented, and verified.

## Phase 1 — authoritative build version

- [x] Validate release tags as `vMAJOR.MINOR.PATCH` and require their commits to
  be contained in `origin/main` in both GitHub release workflows.
- [x] Build desktop and server assemblies from the tag with matching assembly,
  file, and informational versions.
- [x] Derive local/development informational versions from the latest semantic
  tag contained in `origin/main`, never from the current development branch.
- [x] Show the embedded desktop version in About and return the embedded server
  version plus platform/update capability from `/api/info`.

## Phase 2 — desktop update

- [x] Generate a release manifest containing versioned assets and SHA-256 hashes.
- [x] Sign the manifest in GitHub Actions and verify its signature in Orynivo.
- [x] Add localized update status/check/download/install UI to About.
- [x] Download only the matching official Windows installer, verify it, launch
  it after explicit confirmation, and close Orynivo.

## Phase 3 — server update and offline relay

- [x] Add authenticated server update status/upload/apply/progress endpoints.
- [x] Let Orynivo select and relay the matching DEB/RPM asset when the server has
  no direct internet access.
- [x] Add a narrowly scoped privileged Linux updater that independently verifies
  the signed manifest/package before invoking the package manager and restarting.
- [x] Keep remote updates disabled by default and report unsupported portable,
  development, Windows, and macOS server installations clearly.

## Required external release secret

- [x] Configure the GitHub Actions signing private-key secret and the Base64 DER
  public-key repository variable documented in README. Never commit, log, or
  package the private key.

## Completion

- [x] Update README, CHANGELOG, and applicable AGENTS.md files.
- [ ] Build/test every affected project and validate workflow/package scripts.
- [ ] Remove this plan after all items above are complete.

## Current checkpoint (2026-07-15)

All three implementation phases are present in the repository. The GitHub
signing secret and public-key repository variable have now been configured.
Core, desktop,
and server rebuild with zero warnings/errors; development builds resolve to
`0.25.0-dev+c676e862`, a simulated tagged build resolves exactly to `0.25.0`,
`v0.25.0` was verified as contained in `origin/main`, the server info/update
status endpoints passed a local smoke test, and all packaged shell scripts pass
`bash -n`. Remaining work requires repository administration: configure the two
GitHub signing values described above, run the three release workflows against a
new test tag, install the produced DEB and RPM in disposable Linux systems, and
exercise a real desktop-to-server update before removing this plan.

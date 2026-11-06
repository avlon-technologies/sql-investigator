# Draft: SQL Investigator on macOS

**Status: draft for the owner's review.** Nothing here is proposed, approved or applied. The authoritative model, bindings and scenarios in `continuum/` are unchanged.

## What changes

| Dimension | File here | Change |
|---|---|---|
| Decision | `ADR-007-draft.md` | Support macOS. Credentials are protected by the OS per-user secret store (DPAPI on Windows, login Keychain on macOS). It supersedes ADR-004's credential clause only |
| Model (semantic) | `macos-support.adl-change` | 4 `modify description` operations (LocalMCPServer, LocalDevelopment, CredentialVault, ConnectionProvisioningSuite) so they stop naming Windows and DPAPI. No element, edge, owner or constraint changes |
| Bindings | `local-development.adl-binding.json`, `avlon.bindings.localdotnet.vocabulary-claims.json` | New technology `os-user-secret-store` for CredentialVault (`windows: DPAPI-CurrentUser`, `macos: Keychain-login-generic-password`, `acl: current-user-only`). `os: windows-x64 osx-arm64 osx-x64` for LocalMCPServer. ConnectionRegistry `protection: os-user-secret-store`. Binding pinned to the new attestation |
| Acceptance | `b01-connection-provisioning.scenario.json` | Step `credential.dpapi-current-user` becomes `credential.user-protected`, with wording that names no OS mechanism |

Still one environment (`LocalDevelopment`) and one package. A development package names exactly one environment, so a second environment would mean a second package and a second acceptance record.

## Proof, labelled for what it is

- **Model change (ADL apply).** `continuum apply` over base `def71c2d…6a3a`: *applied, 4 modify, 0 errors, 0 warnings*, state B `sha256:ff4a0287e6407cf45d4b81383093fd7bbeb122aa1b7c0eff88acdea2320b0031`. `--verify-authored` against a hand-edited `architecture.adl` gave **EQUIVALENT**, the same state judged by the Host on both sides.
- **Package with the draft bindings and scenarios** (built in a scratch copy, not in `continuum/`):
  - `bind refresh` re-pinned the binding to attestation `sha256:28a88f436b9ce5349783975e4a495199b25cebcd317d764ba6ce299d808c4005`.
  - `package build` showed binding LocalDevelopment complete, 12 scenarios resolved and 0 underspecified-causality findings.
  - `package check` reported **intact**.
- **Not proven:**
  - Anything about code: no implementation exists.
  - That the scenarios pass on macOS: no Mac has run them.
  - The scratch build used the current B01. The renamed step was checked for shape only.
- These are mechanical facts. Whether to do this is the owner's decision.

## What it does not settle (gaps to decide with it)

1. **Keychain access control.** Should the item be limited to the SqlInvestigator executable (needs a signed binary and a stable code identity), or allowed for any process of the user, like DPAPI? Unsigned builds can only do the second.
2. **Signing and notarization.** An unsigned macOS binary is quarantined by Gatekeeper. Do we sign and notarize, or document `xattr -d com.apple.quarantine`?
3. **Where macOS CI runs.** A self-hosted Mac (free), or GitHub macOS runners (10× minutes).
4. **VISION.md and GOAL.md** say "Windows". They are intent documents and would be updated after acceptance, not by this transition.

## What would be implemented after acceptance

- `CredentialVault` and `VaultProvisioner` split behind an OS check: the DPAPI code unchanged, plus a Keychain implementation through Security.framework P/Invoke (`SecItemAdd`, `SecItemCopyMatching`, `SecItemDelete`). It does not shell out to `security`, which would put the secret on a command line.
- `SecureFiles`: NTFS DACLs on Windows, `chmod 0700/0600` and an owner check on macOS.
- Target `net10.0` with RIDs `win-x64`, `osx-arm64` and `osx-x64`. Windows-only code is guarded by `OperatingSystem.IsWindows()`, so the platform analyzer stays clean with warnings as errors.
- Tests: the vault and ACL tests per OS. The adapter's `credential.user-protected` step checks a DPAPI blob on Windows and a Keychain item plus file modes on macOS.
- Release: a self-contained zip per RID.

## Next steps (owner)

1. Review and adjust these drafts. Decide the gaps above.
2. Propose ADR-007: `continuum decision propose …` from the draft, with root-anchored expectations.
3. Create the transition: `continuum icp init transitions/0001-macos-support --base continuum --reason "<why>" --semantic docs/proposals/macos/macos-support.adl-change --bindings … --acceptance …`, then follow `continuum icp ready`.
4. After approval, implement, run all 12 scenarios on Windows and on a Mac, and accept the transition.

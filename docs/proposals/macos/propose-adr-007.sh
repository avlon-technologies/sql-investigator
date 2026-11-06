#!/usr/bin/env bash
# Proposes ADR-007 as agent:claude-code. Needs a delegation the owner minted:
#   continuum decision delegate --workload agent:claude-code --action propose --action check \
#     --action amend --action evidence --subject '*' --ttl 3600 --out <token file>
# Usage: CONTINUUM_DECISION_TOKEN="$(cat <token file>)" bash docs/proposals/macos/propose-adr-007.sh
set -euo pipefail
: "${CONTINUUM_DECISION_TOKEN:?set CONTINUUM_DECISION_TOKEN from the owner-minted delegation}"
Q=::SQLInvestigator.Investigation.QueryExecution
continuum decision propose \
  --request adr-007-macos-propose-1 \
  --title "Support macOS; protect credentials with the operating system's per-user secret store" \
  --decision "Support win-x64, osx-arm64 and osx-x64 (macOS 15 or later) as one product. Keep System.Text.Json files with atomic replacement and one writing service per store, and no SQLite. Protect each alias's credential with the OS per-user secret store: DPAPI CurrentUser on Windows, unchanged; one application-scoped generic-password item per alias in the login Keychain on macOS, created and deleted only by the local console. Owner-only data folders on both (NTFS DACLs; 0700/0600). Fail closed with credential.unavailable, never prompt or downgrade, when the Keychain refuses. Supersedes ADR-004 (its store decision is restated unchanged)." \
  --rationale "macOS developers cannot use a Windows-only tool. Everything but credential protection, owner-only permissions and the data path is already cross-platform. Each OS's own per-user store keeps secrets out of plaintext without a passphrase the unattended server would have to hold." \
  --scope "$Q.CredentialVault" --scope ::SQLInvestigator.LocalMCPServer --scope ::SQLInvestigator.LocalDevelopment \
  --scope "$Q.ConnectionRegistry" --scope "$Q.InvestigationHistory" --scope "$Q.KnowledgeStore" --scope "$Q.ConnectionCatalog" \
  --model continuum/model/architecture.adl-manifest.json \
  --option macos=docs/proposals/macos/macos-support.adl-change \
  --alternative "Data-protection keychain with an access group: needs Developer ID, a provisioning profile and an app bundle; the upgrade once production signing exists" \
  --alternative "Passphrase-derived encrypted file: the unattended server would hold the passphrase in plaintext" \
  --alternative "A cross-platform secret library: a dependency for two well-understood OS APIs; Linux is out of scope" \
  --alternative "Keep Windows only" \
  --assumption "The login Keychain is unlocked in the developer's session; a locked or refusing Keychain fails closed" \
  --consequence "CredentialVault stays a secret-store no service writes; secrets enter only at the local console" \
  --consequence "Bindings: os-user-secret-store replaces windows-dpapi-currentuser; LocalMCPServer bound for windows-x64 osx-arm64 osx-x64; acceptance step credential.dpapi-current-user becomes credential.user-protected" \
  --consequence "Production macOS distribution requires Developer ID signing and notarization; until then macOS builds are development builds" \
  --risk "Unsigned or ad-hoc builds are identified by code hash, so every update re-prompts for Keychain access or needs re-provisioning" \
  --risk "GitHub macOS runners cannot host SQL Server; macOS SQL connectivity needs a reachable test server" \
  --expect "not $Q.QueryGateway writes $Q.CredentialVault" \
  --expect "not $Q.KnowledgeService writes $Q.CredentialVault" \
  --expect "not $Q.ConnectionRegistry writes $Q.CredentialVault" \
  --expect "$Q.ConnectionRegistry reads $Q.CredentialVault" \
  --expect "$Q.QueryGateway writes $Q.InvestigationHistory" \
  --expect "$Q.KnowledgeService writes $Q.KnowledgeStore" \
  --expect "$Q.ConnectionRegistry writes $Q.ConnectionCatalog"

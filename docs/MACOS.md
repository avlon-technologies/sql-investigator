# SQL Investigator on macOS

SQL Investigator runs on macOS as well as Windows (ADR-007). It is the same program with the same 11 MCP tools, the same SELECT-only safety rules and the same console commands. Only the credential store, the data folder and file permissions differ. Read the [user guide](USER-GUIDE.md) for everything else. This page covers what is specific to macOS.

> **Status: development builds.** The macOS builds are **ad-hoc signed**. They are not signed with an Apple Developer ID and not notarized, so they are not a production macOS release. See [Unsigned builds](#unsigned-builds-what-that-means) and [What a production release needs](#what-a-production-release-needs).

## Supported systems

| | |
|---|---|
| Architectures | Apple silicon (`osx-arm64`) and Intel (`osx-x64`). Use the build that matches your Mac: `uname -m` prints `arm64` or `x86_64` |
| Minimum macOS | **macOS 15 Sequoia**, the oldest macOS that .NET 10 supports |
| Verified on | macOS 15.8.2 (arm64) and 15.7.9 (x64), on GitHub-hosted runners, 2026-11-06: unit, integration and all 12 acceptance scenarios against a real SQL Server ([evidence](../evaluation/2026-11-06-macos/RUNTIME-EVIDENCE.md)) |
| Not yet verified | macOS 26; a developer's own login keychain prompts (CI uses an unattended keychain) |
| .NET | Not needed. The build is self-contained, in a single executable file |

## 1. Install

Each build is a `SqlInvestigator-<version>-osx-arm64.tar.gz` or `…-osx-x64.tar.gz`, with a `.sha256` file beside it. It contains the `SqlInvestigator` executable, `README.md` (the user guide), `MACOS.md` (this page) and `VERSION.txt`. Builds come from the `macOS` workflow (`.github/workflows/macos.yml`), which you start by hand. Download the artifact with `gh run download`, then check it:

```bash
shasum -a 256 -c SqlInvestigator-1.0.0-osx-arm64.tar.gz.sha256   # prints "...: OK"
```

```bash
mkdir -p ~/Applications/SqlInvestigator
tar -xzf SqlInvestigator-1.0.0-osx-arm64.tar.gz -C ~/Applications/SqlInvestigator
~/Applications/SqlInvestigator/SqlInvestigator --version   # prints e.g. 1.0.0+<commit>
```

The folder is not on your `PATH`, so this page uses the full path. To type just `SqlInvestigator`, add `export PATH="$HOME/Applications/SqlInvestigator:$PATH"` to `~/.zshrc`. The Claude Code registration below uses the full path either way.

If macOS says the app **"cannot be opened"** or that **Apple could not verify** it, the file carries the quarantine mark a browser adds on download. Files fetched with `curl` or `gh` do not get it. For a development build you trust, remove the mark:

```bash
xattr -d com.apple.quarantine ~/Applications/SqlInvestigator/SqlInvestigator
```

Alternatively, try to run it once, then choose **Open Anyway** in System Settings → Privacy & Security. Since macOS 15 the Control-click shortcut no longer bypasses this.

### Where it keeps data

`~/Library/Application Support/SqlInvestigator/data`. The folder is mode `0700` and every store file `0600`, so only your user can read them. `SQLINVESTIGATOR_HOME` moves the folder, exactly as on Windows. Use the same value at the console and in the Claude Code registration.

Unlike on Windows, there is no `vault` folder. Passwords are kept in your **login keychain**, not in files.

## 2. Connect a database (at your terminal, not in Claude Code)

```bash
~/Applications/SqlInvestigator/SqlInvestigator provision --alias commerce \
  --server sqldev01,1433 --database Commerce --user commerce_ro \
  --environment development --scope dbo.Orders --scope dbo.Customers
# Password for commerce_ro on sqldev01,1433/Commerce:   (typed, not shown)
```

The options are the same as on Windows; see the user guide. Use a SQL login (`--user`). Add `--trust-server-certificate` for a local, container or test SQL Server: they use a self-signed certificate, and without the flag `provision` fails with `server.unreachable` ("the encrypted (TLS) connection could not be set up"). `--windows-auth` (integrated authentication) depends on a Kerberos setup on the Mac and has not been tested on macOS.

To pipe the password from a password manager instead of typing it, send it on standard input, for example `security find-generic-password -s my-sql -w | SqlInvestigator provision ...`. Never put it on the command line; `provision` refuses a password argument.

## 3. Register it with Claude Code

Use the absolute path:

```bash
claude mcp add sql-investigator --scope user -- ~/Applications/SqlInvestigator/SqlInvestigator
```

## How the keychain is used

- **One item per alias.** `provision` stores each alias's password as a generic-password item in your default (login) keychain. You can see it in Keychain Access:
  - name **SQL Investigator: \<alias\>**;
  - kind *SQL Investigator credential*;
  - account = the alias;
  - service `SqlInvestigator/1 <16 hex digits>`. The digits identify the data folder, so a second `SQLINVESTIGATOR_HOME` gets its own items.
- **Only the SqlInvestigator executable that stored an item can read it.** The item's access list trusts only that program. Any other application, including `security find-generic-password -w` and Keychain Access's *Show password*, needs your approval in a keychain dialog. This is stricter than DPAPI on Windows, where any program running as you can decrypt your blobs.
- **The MCP server never asks.** Claude Code starts the server in the background, where nobody would see a dialog. The server forbids keychain prompts and gives each read 15 seconds. If the keychain would need your approval, is locked, or does not answer, the query fails with `credential.unavailable` and tells you what to do. It never falls back to a weaker store.
- **Only the console writes.** `provision`, `provision --replace` and `remove` are the only commands that create, change or delete items. They run in your terminal, where a keychain dialog can be answered.
- **Removing an alias.** `connection_remove`, called by Claude, removes an alias from the catalog but leaves its keychain item. `SqlInvestigator remove --alias <name>` at your terminal deletes both, and still deletes the keychain item after `connection_remove` has already removed the alias from the catalog.

### After you update or rebuild SqlInvestigator

An ad-hoc-signed program is identified by the exact hash of its code, so a new build is a **different application** to the keychain. After an update, the server cannot read the old items: queries fail with `credential.unavailable`. Fix it once per alias, either way:

- **Re-provision with the new build:** `SqlInvestigator provision --alias <name> ... --replace`. If macOS asks whether SqlInvestigator may change the existing item, choose **Always Allow**.
- **Or** open Keychain Access, find **SQL Investigator: \<alias\>**, open Access Control and add the new `SqlInvestigator` executable.

A production build signed with a Developer ID keeps the same identity across updates and would not need this; see below.

## Unsigned builds: what that means

| | Development build (today) | Production build (needs the steps below) |
|---|---|---|
| Signature | Ad-hoc (made by the .NET SDK) | Developer ID Application, hardened runtime |
| Gatekeeper on a downloaded file | Blocked until you remove the quarantine mark or choose Open Anyway | Opens normally (notarized) |
| Keychain identity across updates | Changes with every build; re-provision or re-allow after updating | Stable (same Team ID and signing identity) |
| Status | Development-ready and runtime-verified on macOS 15 | **Distribution-ready only once signed and notarized** |

## What a production release needs

None of this is required for development or for the automated tests. All of it is required before calling a macOS build a production release:

1. **Membership and certificate:** Apple Developer Program membership, and a **Developer ID Application** certificate in the build machine's keychain.
2. **Signing:** sign the published executable with the **hardened runtime** and a secure timestamp:
   ```bash
   codesign --force --options runtime --timestamp --entitlements sqlinvestigator.entitlements --sign "Developer ID Application: <Team> (<TEAMID>)" SqlInvestigator
   ```
   The entitlements file must at least allow JIT (`com.apple.security.cs.allow-jit`), which .NET needs under the hardened runtime.
3. **Notarization:** `xcrun notarytool submit SqlInvestigator.zip --keychain-profile <profile> --wait`. A bare executable cannot have its notarization ticket stapled to it. To ship an offline-verifiable artifact, wrap it in a signed `.pkg` and staple that (`xcrun stapler staple`).
4. **Optional upgrade:** with a Team ID, the vault could move to the data-protection keychain with an access group (ADR-007, alternatives). That would be a new architecture decision.

## Troubleshooting

| What you see | What it means and what to do |
|---|---|
| "SqlInvestigator cannot be opened" / "Apple could not verify…" | The download is quarantined. Run `xattr -d com.apple.quarantine <path>` for a development build you trust, or choose Open Anyway in Privacy & Security |
| `credential.unavailable` … "did not release the credential" | The keychain needs your approval for this build (typically after an update), or it is locked. See [After you update](#after-you-update-or-rebuild-sqlinvestigator). Over SSH the login keychain is usually locked: run `security unlock-keychain ~/Library/Keychains/login.keychain-db` first |
| `credential.unavailable` … "No credential is stored" | The alias has no keychain item for this data folder. Was it provisioned with a different `SQLINVESTIGATOR_HOME`? Re-provision |
| `credential.store-failed` at `provision` or `remove` | The keychain refused the write, often because it is locked. Unlock it and retry. If an old item is in the way, delete **SQL Investigator: \<alias\>** in Keychain Access |
| `server.unreachable` … "the encrypted (TLS) connection could not be set up" | The server answered, but its certificate is not trusted by this Mac. For a local, container or test server, provision again with `--trust-server-certificate` (add `--replace` if the alias exists). For a server with a real certificate, add its CA in Keychain Access |
| `store.inaccessible` | The data folder could not be created or set to `0700`, usually because it belongs to another user. Use a folder you own, or the default |
| A keychain dialog appears while you run `provision --replace` | Expected after an update: the item belongs to the previous build. Choose Always Allow (or Allow) |
| `platform.unsupported` | SQL Investigator runs only on Windows x64 and macOS 15 or later |
| Claude Code shows the server as failed | Run the registered command by hand and read its stderr. Check that the path is absolute, and that `SQLINVESTIGATOR_HOME` matches what you used at the console |

## Uninstall

1. Remove each alias and its keychain item: `SqlInvestigator list`, then `SqlInvestigator remove --alias <name>` for each.
2. `claude mcp remove sql-investigator --scope user`.
3. Delete `~/Applications/SqlInvestigator`.
4. Delete `~/Library/Application Support/SqlInvestigator` (or your `SQLINVESTIGATOR_HOME`). This also deletes your saved investigations and relationship knowledge.

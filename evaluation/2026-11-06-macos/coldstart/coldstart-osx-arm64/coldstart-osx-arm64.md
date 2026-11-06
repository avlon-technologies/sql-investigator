# SQL Investigator - macOS cold-start evaluation

- Started: 2026-11-06T14:49:57Z, finished: 2026-11-06T14:50:01Z
- Runner: macOS 15.8.2, arm64, user runner
- Tarball: `SqlInvestigator-1.0.0-osx-arm64.tar.gz`
- Followed only docs/MACOS.md and docs/USER-GUIDE.md; MCP emulated by eng/coldstart/mcp-client.mjs
- Result: **16 PASS, 2 FAIL, 6 BLOCKED, 4 INFO**

## Summary

| Status | Stage | Check |
|---|---|---|
| INFO | 0 Prerequisites | runner and inputs |
| PASS | 1 Install | tarball matches this Mac's architecture |
| INFO | 1 Install | no .sha256 next to the tarball; MACOS.md gives no integrity check (finding) |
| INFO | 1 Install | tarball contents (MACOS.md: SqlInvestigator, README.md, VERSION.txt) |
| PASS | 1 Install | extracted to ~/Applications/SqlInvestigator; SqlInvestigator is executable |
| PASS | 1 Install | release contains SqlInvestigator, README.md and VERSION.txt |
| PASS | 2 First launch | no quarantine mark on the executable, as MACOS.md predicts for curl/gh downloads |
| PASS | 2 First launch | executable is ad-hoc signed, as MACOS.md states |
| PASS | 2 First launch | --version prints a version |
| PASS | 2 First launch | --help lists provision, list and remove |
| PASS | 2 First launch | provision --help documents --alias and --scope |
| PASS | 3 Provision | provision refuses a password argument (exit 1) |
| PASS | 3 Provision | --environment production is refused |
| FAIL | 3 Provision | provision failed (exit 1) |
| PASS | 3 Provision | provision printed the password prompt with stdin redirected (USER-GUIDE.md) |
| PASS | 3 Provision | provision output does not contain the password |
| FAIL | 3 Provision | list does not show coldstart-main as documented (exit 0) |
| BLOCKED | 4 Credential storage | keychain item, folder permissions, plaintext scan |
| BLOCKED | 5 MCP investigation | claude mcp add / claude mcp list / /mcp (Claude Code not installed; emulated by mcp-client.mjs) |
| BLOCKED | 5 MCP investigation | MCP session |
| BLOCKED | 6 Restart | restart session |
| BLOCKED | 7 Remove credentials | remove --alias, keychain check, server response |
| PASS | 8 Uninstall | step 1: list, then remove --alias for each remaining alias (none left) |
| BLOCKED | 8 Uninstall | step 2: claude mcp remove sql-investigator --scope user (Claude Code not installed) |
| PASS | 8 Uninstall | step 3: program folder deleted |
| PASS | 8 Uninstall | step 4: data folder deleted |
| PASS | 8 Uninstall | no SqlInvestigator keychain items remain |
| INFO | 8 Uninstall | files named like SqlInvestigator remain after uninstall (check whether they are the product's) |

## Evidence

#### INFO - 0 Prerequisites: runner and inputs

~~~~text
date (UTC): 2026-11-06T14:49:57Z
user: runner (uid 501)
uname -m: arm64
ProductName:		macOS
ProductVersion:		15.8.2
BuildVersion:		24H232
node: v22.23.4
bash: 3.2.57(1)-release
default keychain:     "/Users/runner/work/_temp/sqlinv-ci.keychain-db"
keychain search list:
    "/Users/runner/work/_temp/sqlinv-ci.keychain-db"
    "/Library/Keychains/System.keychain"
SQLINV_TARBALL=/Users/runner/work/_temp/download/SqlInvestigator-1.0.0-osx-arm64.tar.gz
SQLINV_SQL_SERVER=sqlinv-macos-a36558d1.canadacentral.azurecontainer.io,1433
SQLINV_SQL_DATABASE=SqlInvFixture_coldstart_osx_arm64_29ccb61c
SQLINV_SQL_LOGIN=sqlinv_fixture_coldstart_osx_arm64_29ccb61c
SQLINV_SQL_SCOPE=dbo.Orders
SQLINV_REPORT=/Users/runner/work/_temp/report/coldstart-osx-arm64.md
SQLINV_SQL_PASSWORD=<set, masked>
scopes parsed:
  dbo.Orders
pre-existing install folder: no
pre-existing data folder: no
~~~~

#### PASS - 1 Install: tarball matches this Mac's architecture

~~~~text
uname -m = arm64; tarball = SqlInvestigator-1.0.0-osx-arm64.tar.gz
~~~~

#### INFO - 1 Install: no .sha256 next to the tarball; MACOS.md gives no integrity check (finding)

~~~~text
ffbeb55a833dc060461bf1f262d296acba092b3a1e86b169a710c18c56b41222  /Users/runner/work/_temp/download/SqlInvestigator-1.0.0-osx-arm64.tar.gz
~~~~

#### INFO - 1 Install: tarball contents (MACOS.md: SqlInvestigator, README.md, VERSION.txt)

~~~~text
$ tar -tzvf /Users/runner/work/_temp/download/SqlInvestigator-1.0.0-osx-arm64.tar.gz
drwxr-xr-x  0 runner staff       0 Nov  6 14:49 ./
-rwxr-xr-x  0 runner staff 96779344 Nov  6 14:49 ./SqlInvestigator
-rw-r--r--  0 runner staff    14734 Nov  6 14:49 ./README.md
-rw-r--r--  0 runner staff      211 Nov  6 14:49 ./VERSION.txt
[exit code 0]

~~~~

#### PASS - 1 Install: extracted to ~/Applications/SqlInvestigator; SqlInvestigator is executable

~~~~text
$ mkdir -p /Users/runner/Applications/SqlInvestigator
[exit code 0]

$ tar -xzf /Users/runner/work/_temp/download/SqlInvestigator-1.0.0-osx-arm64.tar.gz -C /Users/runner/Applications/SqlInvestigator
[exit code 0]

$ ls -la /Users/runner/Applications/SqlInvestigator
total 189064
drwxr-xr-x  5 runner  staff       160 Nov  6 14:49 .
drwxr-xr-x  3 runner  staff        96 Nov  6 14:49 ..
-rw-r--r--  1 runner  staff     14734 Nov  6 14:49 README.md
-rwxr-xr-x  1 runner  staff  96779344 Nov  6 14:49 SqlInvestigator
-rw-r--r--  1 runner  staff       211 Nov  6 14:49 VERSION.txt
[exit code 0]

~~~~

#### PASS - 1 Install: release contains SqlInvestigator, README.md and VERSION.txt

~~~~text
present: SqlInvestigator
present: README.md
present: VERSION.txt
--- VERSION.txt
SQL Investigator 1.0.0
commit   410992fc66edf561838ef5a3554bc810f89f0a55
platform macOS osx-arm64, self-contained .NET 10, single file
signing  ad-hoc (development build: not Developer ID signed, not notarized)
~~~~

#### PASS - 2 First launch: no quarantine mark on the executable, as MACOS.md predicts for curl/gh downloads

~~~~text
$ xattr -l /Users/runner/work/_temp/download/SqlInvestigator-1.0.0-osx-arm64.tar.gz
[exit code 0]

$ xattr -l /Users/runner/Applications/SqlInvestigator/SqlInvestigator
[exit code 0]

~~~~

#### PASS - 2 First launch: executable is ad-hoc signed, as MACOS.md states

~~~~text
$ codesign -dv /Users/runner/Applications/SqlInvestigator/SqlInvestigator
Executable=/Users/runner/Applications/SqlInvestigator/SqlInvestigator
Identifier=SqlInvestigator
Format=Mach-O thin (arm64)
CodeDirectory v=20400 size=750408 flags=0x2(adhoc) hashes=23445+2 location=embedded
Signature=adhoc
Info.plist=not bound
TeamIdentifier=not set
Sealed Resources=none
Internal requirements count=0 size=12
[exit code 0]

$ spctl --assess --type execute -vv /Users/runner/Applications/SqlInvestigator/SqlInvestigator
/Users/runner/Applications/SqlInvestigator/SqlInvestigator: rejected
[exit code 3]

~~~~

#### PASS - 2 First launch: --version prints a version

~~~~text
$ /Users/runner/Applications/SqlInvestigator/SqlInvestigator --version
1.0.0+410992fc66edf561838ef5a3554bc810f89f0a55
[exit code 0]

~~~~

#### PASS - 2 First launch: --help lists provision, list and remove

~~~~text
$ /Users/runner/Applications/SqlInvestigator/SqlInvestigator --help
SqlInvestigator — local MCP stdio server for bounded, read-only SQL Server investigation.

Run without arguments, it speaks MCP over stdio (Claude Code starts it this way).

Local console commands (run by the developer, never through MCP):
  provision --alias <name> --server <host[,port]> --database <db> --environment fixture|development
            --scope <schema.object[(col,...)]> [--scope ...]
            (--user <login> | --windows-auth)
            [--trust-server-certificate] [--no-encrypt] [--replace] [--no-verify]
      The password is prompted for at the console (not echoed), or read as one line from standard
      input when input is redirected. It is never accepted as an argument.
      The login is checked to be read-only before it is stored (skip with --no-verify).
  remove --alias <name>       remove the alias and delete its protected secret (DPAPI file or keychain item)
  list                        list aliases (no secrets): alias, server/database, authentication,
                              environment, scope, provisioned at; tab-separated
  erase-investigation --id <investigationId>   delete an investigation and its query provenance
  erase-relationship --id <relationshipId>     delete a relationship with its evidence and review
  erase-snapshots --alias <name>               delete the stored schema snapshots of an alias
  --version                   print the version
  --help                      print this text (also after any command, e.g. provision --help)
[exit code 0]

~~~~

#### PASS - 2 First launch: provision --help documents --alias and --scope

~~~~text
$ /Users/runner/Applications/SqlInvestigator/SqlInvestigator provision --help
SqlInvestigator — local MCP stdio server for bounded, read-only SQL Server investigation.

Run without arguments, it speaks MCP over stdio (Claude Code starts it this way).

Local console commands (run by the developer, never through MCP):
  provision --alias <name> --server <host[,port]> --database <db> --environment fixture|development
            --scope <schema.object[(col,...)]> [--scope ...]
            (--user <login> | --windows-auth)
            [--trust-server-certificate] [--no-encrypt] [--replace] [--no-verify]
      The password is prompted for at the console (not echoed), or read as one line from standard
      input when input is redirected. It is never accepted as an argument.
      The login is checked to be read-only before it is stored (skip with --no-verify).
  remove --alias <name>       remove the alias and delete its protected secret (DPAPI file or keychain item)
  list                        list aliases (no secrets): alias, server/database, authentication,
                              environment, scope, provisioned at; tab-separated
  erase-investigation --id <investigationId>   delete an investigation and its query provenance
  erase-relationship --id <relationshipId>     delete a relationship with its evidence and review
  erase-snapshots --alias <name>               delete the stored schema snapshots of an alias
  --version                   print the version
  --help                      print this text (also after any command, e.g. provision --help)
[exit code 0]

~~~~

#### PASS - 3 Provision: provision refuses a password argument (exit 1)

~~~~text
$ /Users/runner/Applications/SqlInvestigator/SqlInvestigator provision --alias coldstart-neg ... --password not-the-real-password
input.invalid: A secret is never accepted as an argument.
  Omit it: the console prompts for the password.
[exit code 1]

~~~~

#### PASS - 3 Provision: --environment production is refused

~~~~text
$ /Users/runner/Applications/SqlInvestigator/SqlInvestigator provision --alias coldstart-prod ... --environment production --no-verify (stdin: /dev/null)
environment.unapproved: "production" is not an approved environment. Only fixture and development databases may be provisioned; production data needs the data owner's approval first.
[exit code 1]

~~~~

#### FAIL - 3 Provision: provision failed (exit 1)

~~~~text
$ printf <password> | /Users/runner/Applications/SqlInvestigator/SqlInvestigator provision --alias coldstart-main --server sqlinv-macos-a36558d1.canadacentral.azurecontainer.io,1433 --database SqlInvFixture_coldstart_osx_arm64_29ccb61c --user sqlinv_fixture_coldstart_osx_arm64_29ccb61c --environment fixture --scope dbo.Orders 
Password for sqlinv_fixture_coldstart_osx_arm64_29ccb61c on sqlinv-macos-a36558d1.canadacentral.azurecontainer.io,1433/SqlInvFixture_coldstart_osx_arm64_29ccb61c: 
Checking that sqlinv_fixture_coldstart_osx_arm64_29ccb61c can connect and is read-only...
sql.error: SQL Server returned error -2146893019 for the query on alias "coldstart-main": A connection was successfully established with the server, but then an error occurred during the pre-login handshake. (provider: TCP Provider, error: 35 - An internal exception was caught) Nothing was provisioned.
  Correct the query and run it again; nothing is returned for a failed query.
[exit code 1]

~~~~

#### PASS - 3 Provision: provision printed the password prompt with stdin redirected (USER-GUIDE.md)

~~~~text
Password for sqlinv_fixture_coldstart_osx_arm64_29ccb61c on sqlinv-macos-a36558d1.canadacentral.azurecontainer.io,1433/SqlInvFixture_coldstart_osx_arm64_29ccb61c: 
~~~~

#### PASS - 3 Provision: provision output does not contain the password

#### FAIL - 3 Provision: list does not show coldstart-main as documented (exit 0)

~~~~text
$ /Users/runner/Applications/SqlInvestigator/SqlInvestigator list
No aliases are provisioned.
[exit code 0]

~~~~

#### BLOCKED - 4 Credential storage: keychain item, folder permissions, plaintext scan

Provision did not succeed, so there is nothing stored to check.

#### BLOCKED - 5 MCP investigation: claude mcp add / claude mcp list / /mcp (Claude Code not installed; emulated by mcp-client.mjs)

~~~~text
Documented registration (MACOS.md "3. Register it with Claude Code"):
  claude mcp add sql-investigator --scope user -- ~/Applications/SqlInvestigator/SqlInvestigator
claude on PATH: no
Claude Code is not available on this runner. Equivalent used here: an MCP stdio client
(mcp-client.mjs) starts the same absolute path with no arguments and no extra environment.
~~~~

#### BLOCKED - 5 MCP investigation: MCP session

Needs an installed executable and a provisioned alias.

#### BLOCKED - 6 Restart: restart session

Needs an installed executable and a provisioned alias.

#### BLOCKED - 7 Remove credentials: remove --alias, keychain check, server response

Needs an installed executable and a provisioned alias.

#### PASS - 8 Uninstall: step 1: list, then remove --alias for each remaining alias (none left)

~~~~text
$ /Users/runner/Applications/SqlInvestigator/SqlInvestigator list
No aliases are provisioned.
[exit code 0]

~~~~

#### BLOCKED - 8 Uninstall: step 2: claude mcp remove sql-investigator --scope user (Claude Code not installed)

#### PASS - 8 Uninstall: step 3: program folder deleted

~~~~text
$ rm -rf /Users/runner/Applications/SqlInvestigator
[exit code 0]

~~~~

#### PASS - 8 Uninstall: step 4: data folder deleted

~~~~text
$ rm -rf "/Users/runner/Library/Application Support/SqlInvestigator"
[exit code 0]

~~~~

#### PASS - 8 Uninstall: no SqlInvestigator keychain items remain

~~~~text
SqlInvestigator items left in the default keychain: 0
~~~~

#### INFO - 8 Uninstall: files named like SqlInvestigator remain after uninstall (check whether they are the product's)

~~~~text
/Users/runner/work/_temp/publish/app/SqlInvestigator
/Users/runner/work/_temp/publish/fixture/SqlInvestigator.FixtureTool
/Users/runner/work/_temp/download/SqlInvestigator-1.0.0-osx-arm64.tar.gz
/Users/runner/work/sql-investigator-macos/sql-investigator-macos/tests/SqlInvestigator.IntegrationTests
/Users/runner/work/sql-investigator-macos/sql-investigator-macos/tests/SqlInvestigator.FixtureTool
/Users/runner/work/sql-investigator-macos/sql-investigator-macos/tests/SqlInvestigator.Testing
/Users/runner/work/sql-investigator-macos/sql-investigator-macos/tests/SqlInvestigator.Tests
/Users/runner/work/sql-investigator-macos/sql-investigator-macos/SqlInvestigator.slnx
/Users/runner/work/sql-investigator-macos/sql-investigator-macos/src/SqlInvestigator.McpServer
~~~~


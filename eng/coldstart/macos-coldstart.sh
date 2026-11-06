#!/bin/bash
# =============================================================================
# SQL Investigator - cold-start installation evaluation on macOS.
#
# Follows ONLY the published documentation:
#   docs/MACOS.md        ("SQL Investigator on macOS")
#   docs/USER-GUIDE.md   ("SQL Investigator - user guide")
# plus the public MCP specification (for mcp-client.mjs, next to this file).
#
# Runs unattended on a fresh macOS 15 runner, as a normal user. bash 3.2.
# Inputs (environment): SQLINV_TARBALL SQLINV_SQL_SERVER SQLINV_SQL_DATABASE
#   SQLINV_SQL_LOGIN SQLINV_SQL_PASSWORD SQLINV_SQL_SCOPE SQLINV_REPORT
# Output: a Markdown report at $SQLINV_REPORT. Every stage runs; the script
# exits non-zero at the end if any check FAILED.
#
# The password is copied into a NON-exported shell variable and removed from
# the environment at once. It reaches the product only on standard input (via
# the printf builtin, so never on any command line) and is masked in every
# line written to the report.
# =============================================================================

HERE=$(cd "$(dirname "$0")" && pwd)
CLIENT="$HERE/mcp-client.mjs"

_PW="${SQLINV_SQL_PASSWORD:-}"
unset SQLINV_SQL_PASSWORD
# MACOS.md "Where it keeps data": default folder unless SQLINVESTIGATOR_HOME is set; we use the default.
unset SQLINVESTIGATOR_HOME

# MACOS.md "1. Install" and "Where it keeps data"
INSTALL_DIR="$HOME/Applications/SqlInvestigator"
EXE="$INSTALL_DIR/SqlInvestigator"
APP_SUPPORT="$HOME/Library/Application Support/SqlInvestigator"
DATA_DIR="$APP_SUPPORT/data"
ALIAS="coldstart-main"     # USER-GUIDE.md: a letter, then up to 63 letters, digits, - or _
ALIAS2="coldstart-tool"
LABEL="SQL Investigator: $ALIAS"
LABEL2="SQL Investigator: $ALIAS2"

WORK=$(mktemp -d "${TMPDIR:-/tmp}/sqlinv-coldstart.XXXXXX")
chmod 700 "$WORK"
SUMMARY="$WORK/summary.tsv"
DETAILS="$WORK/details.md"
: >"$SUMMARY"; : >"$DETAILS"
REPORT="${SQLINV_REPORT:-$PWD/sqlinv-coldstart-report.md}"
STARTED=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
NFAIL=0; NPASS=0; NBLOCK=0; NINFO=0
TAB=$(printf '\t')

# ----------------------------------------------------------------- helpers
# Replace every occurrence of the password with ********. The password reaches
# awk only through awk's own environment, never argv.
mask() {
  SQLINV_MASK="$_PW" awk 'BEGIN { p = ENVIRON["SQLINV_MASK"]; n = length(p) }
    { s = $0; if (n > 0) { o = ""; while ((i = index(s, p)) > 0) { o = o substr(s, 1, i - 1) "********"; s = substr(s, i + n) } s = o s } print s }'
}
_B64=""
[ -n "$_PW" ] && _B64=$(printf '%s' "$_PW" | base64 | tr -d '\n')
# contains_secret FILE... : true if any file contains the password (plain or base64).
contains_secret() {
  [ -n "$_PW" ] || return 1
  SQLINV_MASK="$_PW" SQLINV_MASK64="$_B64" awk 'BEGIN { p = ENVIRON["SQLINV_MASK"]; q = ENVIRON["SQLINV_MASK64"]; f = 0 }
    index($0, p) > 0 || (q != "" && index($0, q) > 0) { f = 1; exit }
    END { exit f ? 0 : 1 }' "$@" 2>/dev/null
}

# record STATUS STAGE TITLE [EVIDENCE-FILE] [NOTE]
record() {
  st="$1"; stage="$2"; title="$3"; evf="$4"; note="$5"
  case "$st" in
    FAIL) NFAIL=$((NFAIL + 1)) ;;
    PASS) NPASS=$((NPASS + 1)) ;;
    BLOCKED) NBLOCK=$((NBLOCK + 1)) ;;
    *) NINFO=$((NINFO + 1)) ;;
  esac
  printf '%s\t%s\t%s\n' "$st" "$stage" "$title" | mask >>"$SUMMARY"
  {
    printf '#### %s - %s: %s\n\n' "$st" "$stage" "$title"
    [ -n "$note" ] && printf '%s\n\n' "$note"
    if [ -n "$evf" ] && [ -s "$evf" ]; then
      printf '~~~~text\n'
      head -n 200 "$evf" | cut -c1-2000
      lines=$(wc -l <"$evf" | tr -d ' ')
      [ "$lines" -gt 200 ] && printf '... (%s lines in total, truncated)\n' "$lines"
      printf '~~~~\n\n'
    fi
  } | mask >>"$DETAILS"
  printf '[%s] %s: %s\n' "$st" "$stage" "$title" | mask >&2
}

evfile() { f="$WORK/ev-$1.txt"; : >"$f"; echo "$f"; }

# run_tb SECS OUTFILE CMD... : run CMD with a hard time box; returns 124 on timeout.
# If FEED_SECRET=1 the password (one line) is written to CMD's stdin; otherwise stdin is /dev/null.
FEED_SECRET=0
run_tb() {
  _secs="$1"; _out="$2"; shift 2
  _flag="$WORK/.timeout.$$.$RANDOM"
  rm -f "$_flag"
  if [ "$FEED_SECRET" = 1 ]; then
    ( printf '%s\n' "$_PW" | "$@" ) >>"$_out" 2>&1 &
  else
    ( "$@" </dev/null ) >>"$_out" 2>&1 &
  fi
  _pid=$!
  ( sleep "$_secs"; touch "$_flag"; pkill -TERM -P "$_pid" 2>/dev/null; kill -TERM "$_pid" 2>/dev/null
    sleep 3; pkill -KILL -P "$_pid" 2>/dev/null; kill -KILL "$_pid" 2>/dev/null ) >/dev/null 2>&1 &
  _wd=$!
  wait "$_pid"; _rc=$?
  kill "$_wd" 2>/dev/null; wait "$_wd" 2>/dev/null
  if [ -f "$_flag" ]; then rm -f "$_flag"; return 124; fi
  return $_rc
}

# run_step SECS EVFILE "display command" CMD... : evidence = command line, output, exit code.
run_step() {
  _s="$1"; _e="$2"; _d="$3"; shift 3
  printf '$ %s\n' "$_d" >>"$_e"
  run_tb "$_s" "$_e" "$@"; _r=$?
  [ $_r -eq 124 ] && printf '[timed out after %ss; process killed]\n' "$_s" >>"$_e"
  printf '[exit code %s]\n\n' "$_r" >>"$_e"
  return $_r
}

have_exe() { [ -x "$EXE" ]; }
blocked_no_exe() { record BLOCKED "$1" "$2" "" "The executable $EXE is not installed (stage 1 failed), so this could not run."; }

# Scope list: SQLINV_SQL_SCOPE may hold several objects separated by whitespace, ';'
# or commas outside parentheses. Each becomes one --scope (USER-GUIDE.md option table).
SCOPES_FILE="$WORK/scopes.txt"
printf '%s\n' "${SQLINV_SQL_SCOPE:-}" | awk '{
  d = 0; cur = ""
  for (i = 1; i <= length($0); i++) {
    ch = substr($0, i, 1)
    if (ch == "(") d++
    if (ch == ")") d--
    if (d == 0 && (ch == "," || ch == ";" || ch == " " || ch == "\t")) { if (cur != "") print cur; cur = "" }
    else cur = cur ch
  }
  if (cur != "") print cur
}' >"$SCOPES_FILE"
SCOPE_ARGS=()
while IFS= read -r s; do [ -n "$s" ] && SCOPE_ARGS+=(--scope "$s"); done <"$SCOPES_FILE"
FIRST_SCOPE=$(head -n 1 "$SCOPES_FILE")
FIRST_OBJECT=$(printf '%s' "$FIRST_SCOPE" | sed 's/(.*//')

provision_display() { # alias extra-flags
  echo "printf <password> | $EXE provision --alias $1 --server ${SQLINV_SQL_SERVER:-} --database ${SQLINV_SQL_DATABASE:-} --user ${SQLINV_SQL_LOGIN:-} --environment fixture ${SCOPE_ARGS[*]} $2"
}

# =============================================================================
# Stage 0 - prerequisites (not a product check; records the environment)
# =============================================================================
E=$(evfile 0-env)
{
  echo "date (UTC): $STARTED"
  echo "user: $(id -un) (uid $(id -u))"
  echo "uname -m: $(uname -m)"
  sw_vers 2>&1
  echo "node: $(node --version 2>&1)"
  echo "bash: $BASH_VERSION"
  echo "default keychain: $(security default-keychain 2>&1)"
  echo "keychain search list:"; security list-keychains 2>&1
  for v in SQLINV_TARBALL SQLINV_SQL_SERVER SQLINV_SQL_DATABASE SQLINV_SQL_LOGIN SQLINV_SQL_SCOPE SQLINV_REPORT; do
    eval "val=\${$v:-}"; echo "$v=${val:-<unset>}"
  done
  if [ -n "$_PW" ]; then echo "SQLINV_SQL_PASSWORD=<set, masked>"; else echo "SQLINV_SQL_PASSWORD=<unset>"; fi
  echo "scopes parsed:"; sed 's/^/  /' "$SCOPES_FILE"
  echo "pre-existing install folder: $([ -e "$INSTALL_DIR" ] && echo yes || echo no)"
  echo "pre-existing data folder: $([ -e "$APP_SUPPORT" ] && echo yes || echo no)"
} >"$E" 2>&1
MISSING=""
for v in SQLINV_TARBALL SQLINV_SQL_SERVER SQLINV_SQL_DATABASE SQLINV_SQL_LOGIN SQLINV_SQL_SCOPE; do
  eval "val=\${$v:-}"; [ -z "$val" ] && MISSING="$MISSING $v"
done
[ -z "$_PW" ] && MISSING="$MISSING SQLINV_SQL_PASSWORD"
command -v node >/dev/null 2>&1 || MISSING="$MISSING node"
if [ -n "$MISSING" ]; then
  record BLOCKED "0 Prerequisites" "inputs missing:$MISSING" "$E"
else
  record INFO "0 Prerequisites" "runner and inputs" "$E"
fi

# =============================================================================
# Stage 1 - Installation (MACOS.md "Supported systems", "1. Install")
# =============================================================================
ST="1 Install"
# MACOS.md "Supported systems": use the build matching `uname -m` (arm64 / x86_64).
E=$(evfile 1-arch)
ARCH=$(uname -m)
TB=$(basename "${SQLINV_TARBALL:-none}")
echo "uname -m = $ARCH; tarball = $TB" >"$E"
case "$ARCH:$TB" in
  arm64:*osx-arm64*|x86_64:*osx-x64*) record PASS "$ST" "tarball matches this Mac's architecture" "$E" ;;
  *) record FAIL "$ST" "tarball does not match this Mac's architecture" "$E" ;;
esac

# The macOS doc publishes no checksum; the Windows section of the user guide has a .sha256.
E=$(evfile 1-sha)
if [ -f "${SQLINV_TARBALL:-/nonexistent}.sha256" ]; then
  want=$(awk '{print tolower($1); exit}' "$SQLINV_TARBALL.sha256")
  got=$(shasum -a 256 "$SQLINV_TARBALL" | awk '{print tolower($1)}')
  printf 'expected %s\nactual   %s\n' "$want" "$got" >"$E"
  if [ "$want" = "$got" ]; then record PASS "$ST" "SHA-256 matches the .sha256 file" "$E"
  else record FAIL "$ST" "SHA-256 does not match the .sha256 file" "$E"; fi
else
  shasum -a 256 "${SQLINV_TARBALL:-/nonexistent}" >"$E" 2>&1
  record INFO "$ST" "no .sha256 next to the tarball; MACOS.md gives no integrity check (finding)" "$E"
fi

E=$(evfile 1-list)
run_step 60 "$E" "tar -tzvf $SQLINV_TARBALL" tar -tzvf "${SQLINV_TARBALL:-/nonexistent}"
record INFO "$ST" "tarball contents (MACOS.md: SqlInvestigator, README.md, VERSION.txt)" "$E"

# MACOS.md "1. Install": mkdir -p ~/Applications/SqlInvestigator; tar -xzf ... -C ~/Applications/SqlInvestigator
E=$(evfile 1-install)
run_step 30 "$E" "mkdir -p $INSTALL_DIR" mkdir -p "$INSTALL_DIR"
run_step 120 "$E" "tar -xzf $SQLINV_TARBALL -C $INSTALL_DIR" tar -xzf "${SQLINV_TARBALL:-/nonexistent}" -C "$INSTALL_DIR"
run_step 10 "$E" "ls -la $INSTALL_DIR" ls -la "$INSTALL_DIR"
if have_exe; then
  record PASS "$ST" "extracted to ~/Applications/SqlInvestigator; SqlInvestigator is executable" "$E"
else
  record FAIL "$ST" "no executable at $EXE after extraction (top-level folder in the tarball?)" "$E"
fi
E=$(evfile 1-files)
MISS=""
for f in SqlInvestigator README.md VERSION.txt; do
  [ -e "$INSTALL_DIR/$f" ] && echo "present: $f" >>"$E" || { echo "MISSING: $f" >>"$E"; MISS="$MISS $f"; }
done
[ -f "$INSTALL_DIR/VERSION.txt" ] && { echo "--- VERSION.txt"; cat "$INSTALL_DIR/VERSION.txt"; } >>"$E" 2>&1
if [ -z "$MISS" ]; then record PASS "$ST" "release contains SqlInvestigator, README.md and VERSION.txt" "$E"
else record FAIL "$ST" "release is missing:$MISS" "$E"; fi

# =============================================================================
# Stage 2 - First launch and Gatekeeper (MACOS.md "1. Install", "Unsigned builds")
# =============================================================================
ST="2 First launch"
if have_exe; then
  # MACOS.md: curl/gh downloads carry no quarantine mark; if present, `xattr -d com.apple.quarantine`.
  E=$(evfile 2-xattr)
  run_step 10 "$E" "xattr -l $SQLINV_TARBALL" xattr -l "$SQLINV_TARBALL"
  run_step 10 "$E" "xattr -l $EXE" xattr -l "$EXE"
  if xattr -p com.apple.quarantine "$EXE" >/dev/null 2>&1; then
    run_step 10 "$E" "xattr -d com.apple.quarantine $EXE" xattr -d com.apple.quarantine "$EXE"
    if xattr -p com.apple.quarantine "$EXE" >/dev/null 2>&1; then
      record FAIL "$ST" "quarantine mark present and could not be removed as documented" "$E"
    else
      record PASS "$ST" "quarantine mark was present (unexpected for a gh/curl download) and was removed with xattr -d" "$E"
    fi
  else
    record PASS "$ST" "no quarantine mark on the executable, as MACOS.md predicts for curl/gh downloads" "$E"
  fi

  # MACOS.md "Unsigned builds": the development build is ad-hoc signed, not notarized.
  E=$(evfile 2-sign)
  run_step 20 "$E" "codesign -dv $EXE" codesign -dv "$EXE"
  run_step 20 "$E" "spctl --assess --type execute -vv $EXE" spctl --assess --type execute -vv "$EXE"
  if grep -q 'Signature=adhoc' "$E"; then
    record PASS "$ST" "executable is ad-hoc signed, as MACOS.md states" "$E"
  else
    record INFO "$ST" "signature is not reported as ad-hoc (MACOS.md says development builds are ad-hoc)" "$E"
  fi

  # MACOS.md: `SqlInvestigator --version` prints e.g. 1.0.0+<commit>.
  E=$(evfile 2-version)
  run_step 60 "$E" "$EXE --version" env -u SQLINVESTIGATOR_HOME "$EXE" --version; rc=$?
  if [ $rc -eq 0 ] && grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+' "$E"; then
    record PASS "$ST" "--version prints a version" "$E"
  else
    record FAIL "$ST" "--version did not exit 0 with a version (exit $rc)" "$E"
  fi

  # USER-GUIDE.md "1. Install": --help lists the console commands; --help after any command.
  E=$(evfile 2-help)
  run_step 60 "$E" "$EXE --help" "$EXE" --help; rc=$?
  if [ $rc -eq 0 ] && grep -q provision "$E" && grep -q list "$E" && grep -q remove "$E"; then
    record PASS "$ST" "--help lists provision, list and remove" "$E"
  else
    record FAIL "$ST" "--help did not exit 0 listing provision/list/remove (exit $rc)" "$E"
  fi
  E=$(evfile 2-provhelp)
  run_step 60 "$E" "$EXE provision --help" "$EXE" provision --help; rc=$?
  if [ $rc -eq 0 ] && grep -q -- '--alias' "$E" && grep -q -- '--scope' "$E"; then
    record PASS "$ST" "provision --help documents --alias and --scope" "$E"
  else
    record FAIL "$ST" "provision --help did not exit 0 with its options (exit $rc)" "$E"
  fi
else
  blocked_no_exe "$ST" "--version / --help / Gatekeeper"
fi

# =============================================================================
# Stage 3 - Connect a database (MACOS.md "2. Connect a database";
#           USER-GUIDE.md "2. Connect a database", option table, piping on stdin)
# =============================================================================
ST="3 Provision"
TRUST_FLAG=""
PROVISIONED=0
if have_exe; then
  # MACOS.md: "Never put it on the command line; `provision` refuses a password argument."
  # The docs do not name such an option; we try --password with a DUMMY value (not the secret).
  E=$(evfile 3-neg-pw)
  run_step 60 "$E" "$EXE provision --alias coldstart-neg ... --password not-the-real-password" \
    "$EXE" provision --alias coldstart-neg --server "$SQLINV_SQL_SERVER" --database "$SQLINV_SQL_DATABASE" \
    --user "$SQLINV_SQL_LOGIN" --environment fixture "${SCOPE_ARGS[@]}" --password not-the-real-password; rc=$?
  if [ $rc -ne 0 ] && [ $rc -ne 124 ]; then record PASS "$ST" "provision refuses a password argument (exit $rc)" "$E"
  else record FAIL "$ST" "provision did not refuse a --password argument (exit $rc)" "$E"; fi

  # USER-GUIDE.md: "--environment fixture|development ... production is refused".
  E=$(evfile 3-neg-prod)
  run_step 60 "$E" "$EXE provision --alias coldstart-prod ... --environment production --no-verify (stdin: /dev/null)" \
    "$EXE" provision --alias coldstart-prod --server "$SQLINV_SQL_SERVER" --database "$SQLINV_SQL_DATABASE" \
    --user "$SQLINV_SQL_LOGIN" --environment production "${SCOPE_ARGS[@]}" --no-verify; rc=$?
  if [ $rc -ne 0 ] && grep -Eiq 'environment\.unapproved|production' "$E"; then
    record PASS "$ST" "--environment production is refused" "$E"
  elif [ $rc -ne 0 ]; then
    record INFO "$ST" "production provision failed, but not visibly because of the environment (exit $rc)" "$E"
  else
    record FAIL "$ST" "--environment production was accepted" "$E"
  fi

  # The real provision: password piped on stdin (MACOS.md / USER-GUIDE.md), environment
  # 'fixture' (synthetic test data). USER-GUIDE.md: add --trust-server-certificate on a
  # certificate error, so retry with it if the first attempt reports one.
  E=$(evfile 3-provision)
  for attempt in "" "--trust-server-certificate"; do
    FEED_SECRET=1
    run_step 180 "$E" "$(provision_display "$ALIAS" "$attempt")" \
      "$EXE" provision --alias "$ALIAS" --server "$SQLINV_SQL_SERVER" --database "$SQLINV_SQL_DATABASE" \
      --user "$SQLINV_SQL_LOGIN" --environment fixture "${SCOPE_ARGS[@]}" $attempt; rc=$?
    FEED_SECRET=0
    if [ $rc -eq 0 ]; then PROVISIONED=1; TRUST_FLAG="$attempt"; break; fi
    if [ -z "$attempt" ] && ! grep -Eiq 'certificate|server\.unreachable' "$E"; then break; fi
    echo "(certificate / unreachable error: retrying with --trust-server-certificate, per USER-GUIDE.md)" >>"$E"
  done
  if [ $PROVISIONED = 1 ]; then
    record PASS "$ST" "provision with the password on stdin succeeded${TRUST_FLAG:+ (needed $TRUST_FLAG)}" "$E"
  else
    record FAIL "$ST" "provision failed (exit $rc)" "$E"
  fi
  E2=$(evfile 3-prompt)
  { grep -i 'password for' "$E" || echo "(no 'Password for ...' prompt seen in the output)"; } >"$E2"
  if grep -qi 'password for' "$E"; then record PASS "$ST" "provision printed the password prompt with stdin redirected (USER-GUIDE.md)" "$E2"
  else record INFO "$ST" "no password prompt seen with stdin redirected (USER-GUIDE.md says it is still printed)" "$E2"; fi
  if contains_secret "$E"; then record FAIL "$ST" "provision ECHOED THE PASSWORD to its output (masked in this report)" ""
  else record PASS "$ST" "provision output does not contain the password" ""; fi

  # USER-GUIDE.md: `list` is tab-separated: alias, server/database, auth, environment, scope, provisioned at.
  E=$(evfile 3-list)
  run_step 60 "$E" "$EXE list" "$EXE" list; rc=$?
  if [ $rc -eq 0 ] && grep -q "^$ALIAS$TAB" "$E" && grep "^$ALIAS$TAB" "$E" | grep -q fixture; then
    record PASS "$ST" "list shows the alias as a tab-separated line with environment fixture" "$E"
  else
    record FAIL "$ST" "list does not show $ALIAS as documented (exit $rc)" "$E"
  fi
  if grep -Eq '^coldstart-(neg|prod)' "$E"; then
    record FAIL "$ST" "a refused provision attempt left an alias in the catalog" "$E"
  fi
else
  blocked_no_exe "$ST" "provision / list"
fi

# =============================================================================
# Stage 4 - Secure credential storage (MACOS.md "Where it keeps data",
#           "How the keychain is used")
# =============================================================================
ST="4 Credential storage"
if [ $PROVISIONED = 1 ]; then
  # One generic-password item per alias in the default (login) keychain:
  # name "SQL Investigator: <alias>", kind "SQL Investigator credential",
  # account = alias, service "SqlInvestigator/1 <16 hex digits>".
  E=$(evfile 4-item)
  run_step 30 "$E" "security find-generic-password -l \"$LABEL\"" security find-generic-password -l "$LABEL"; rc=$?
  run_step 30 "$E" "security find-generic-password -a $ALIAS -D \"SQL Investigator credential\"" \
    security find-generic-password -a "$ALIAS" -D "SQL Investigator credential"
  if [ $rc -eq 0 ]; then record PASS "$ST" "keychain item \"$LABEL\" exists" "$E"
  else record FAIL "$ST" "no keychain item labelled \"$LABEL\" (exit $rc)" "$E"; fi
  grep -q "\"acct\"<blob>=\"$ALIAS\"" "$E" && record PASS "$ST" "item account = the alias" || record FAIL "$ST" "item account is not \"$ALIAS\"" "$E"
  grep -q '"desc"<blob>="SQL Investigator credential"' "$E" && record PASS "$ST" "item kind = SQL Investigator credential" || record FAIL "$ST" "item kind is not \"SQL Investigator credential\"" "$E"
  grep -Eq '"svce"<blob>="SqlInvestigator/1 [0-9A-Fa-f]{16}"' "$E" && record PASS "$ST" "item service = SqlInvestigator/1 <16 hex digits>" || record FAIL "$ST" "item service does not match SqlInvestigator/1 <16 hex>" "$E"
  grep -q '"genp"' "$E" && record PASS "$ST" "item class is generic password" || record FAIL "$ST" "item is not a generic password" "$E"
  DEFKC=$(security default-keychain 2>/dev/null | tr -d ' "')
  ITEMKC=$(grep -m1 '^keychain:' "$E" | sed 's/^keychain: *//; s/"//g')
  echo "default keychain: $DEFKC / item keychain: $ITEMKC" >>"$E"
  [ -n "$ITEMKC" ] && [ "$ITEMKC" = "$DEFKC" ] && record PASS "$ST" "item is in the default keychain ($ITEMKC)" || record FAIL "$ST" "item keychain ($ITEMKC) is not the default keychain ($DEFKC)" "$E"

  # MACOS.md: other programs, including `security find-generic-password -w`, need approval in a
  # dialog. Unattended, that means a refusal or a hang. Time-boxed at 15 s; the raw output is
  # checked for the password and then deleted, never copied into the report unmasked.
  E=$(evfile 4-readpw)
  RAW="$WORK/raw-readpw.txt"; : >"$RAW"
  printf '$ security find-generic-password -l "%s" -w   (time-boxed 15 s)\n' "$LABEL" >>"$E"
  run_tb 15 "$RAW" security find-generic-password -l "$LABEL" -w; rc=$?
  if contains_secret "$RAW"; then
    echo "[the command printed the password; output withheld]" >>"$E"
    printf '[exit code %s]\n' "$rc" >>"$E"
    record FAIL "$ST" "security -w returned the password without any approval (MACOS.md says only SqlInvestigator may read it)" "$E"
  else
    mask <"$RAW" >>"$E"
    [ $rc -eq 124 ] && echo "[timed out after 15 s: presumably waiting on a keychain dialog nobody can answer]" >>"$E"
    printf '[exit code %s]\n' "$rc" >>"$E"
    record PASS "$ST" "another program cannot read the password unattended (exit $rc$([ $rc -eq 124 ] && echo ', timed out'))" "$E"
  fi
  rm -f "$RAW"

  # MACOS.md "Where it keeps data": folder 0700, every store file 0600, no vault folder.
  E=$(evfile 4-folder)
  run_step 10 "$E" "ls -la \"$DATA_DIR\"" ls -la "$DATA_DIR"
  run_step 10 "$E" "stat -f '%Lp %Su %N' \"$APP_SUPPORT\" \"$DATA_DIR\"" stat -f '%Lp %Su %N' "$APP_SUPPORT" "$DATA_DIR"
  find "$DATA_DIR" -exec stat -f '%Lp %Su %HT %N' {} \; >>"$E" 2>&1
  if [ -d "$DATA_DIR" ]; then
    m=$(stat -f '%Lp' "$DATA_DIR")
    [ "$m" = "700" ] && record PASS "$ST" "data folder exists at the documented path with mode 0700" "$E" || record FAIL "$ST" "data folder mode is $m, not 0700" "$E"
    owner=$(stat -f '%Su' "$DATA_DIR")
    [ "$owner" = "$(id -un)" ] && record PASS "$ST" "data folder is owned by the current user" || record FAIL "$ST" "data folder owner is $owner" "$E"
    BAD=$(find "$DATA_DIR" -type f ! -perm 0600 2>/dev/null)
    NF=$(find "$DATA_DIR" -type f | wc -l | tr -d ' ')
    if [ -z "$BAD" ] && [ "$NF" -gt 0 ]; then record PASS "$ST" "all $NF store files are mode 0600" "$E"
    elif [ "$NF" -eq 0 ]; then record FAIL "$ST" "no store files in the data folder after provision (USER-GUIDE.md: catalog.json is created at provision)" "$E"
    else record FAIL "$ST" "store files not 0600: $BAD" "$E"; fi
    [ -f "$DATA_DIR/catalog.json" ] && record PASS "$ST" "catalog.json exists after provision" || record FAIL "$ST" "no catalog.json after provision (USER-GUIDE.md: created when you provision)" "$E"
    [ -e "$DATA_DIR/vault" ] && record FAIL "$ST" "a vault folder exists (MACOS.md: there is no vault folder on macOS)" "$E" || record PASS "$ST" "no vault folder, as MACOS.md states"
  else
    record FAIL "$ST" "data folder $DATA_DIR does not exist after provision" "$E"
  fi
  E=$(evfile 4-plain)
  echo "scanning every file under $APP_SUPPORT for the password (plain text and base64)" >"$E"
  LEAK=""
  while IFS= read -r f; do contains_secret "$f" && LEAK="$LEAK $f"; done <<EOF
$(find "$APP_SUPPORT" -type f 2>/dev/null)
EOF
  if [ -z "$LEAK" ]; then record PASS "$ST" "no plaintext (or base64) password anywhere in the data folder" "$E"
  else echo "found in:$LEAK" >>"$E"; record FAIL "$ST" "the password is stored in plain text in the data folder" "$E"; fi
else
  record BLOCKED "$ST" "keychain item, folder permissions, plaintext scan" "" "Provision did not succeed, so there is nothing stored to check."
fi

# =============================================================================
# Stage 5 - Run as an MCP stdio server (MACOS.md "3. Register it with Claude Code";
#           USER-GUIDE.md "3. Register", "4. Investigate")
# =============================================================================
ST="5 MCP investigation"
E=$(evfile 5-register)
{
  echo "Documented registration (MACOS.md \"3. Register it with Claude Code\"):"
  echo "  claude mcp add sql-investigator --scope user -- ~/Applications/SqlInvestigator/SqlInvestigator"
  echo "claude on PATH: $(command -v claude 2>/dev/null || echo 'no')"
  echo "Claude Code is not available on this runner. Equivalent used here: an MCP stdio client"
  echo "(mcp-client.mjs) starts the same absolute path with no arguments and no extra environment."
} >"$E"
record BLOCKED "$ST" "claude mcp add / claude mcp list / /mcp (Claude Code not installed; emulated by mcp-client.mjs)" "$E"

# run_session MODE STAGE ALIAS : run one MCP session and import its steps into the report.
run_session() {
  _mode="$1"; _stage="$2"; _alias="$3"
  _dir="$WORK/mcp-$_mode"; mkdir -p "$_dir"
  _e=$(evfile "mcp-$_mode")
  run_step 600 "$_e" "node mcp-client.mjs $_mode --exe $EXE --alias $_alias --scope $FIRST_SCOPE" \
    env -u SQLINVESTIGATOR_HOME node "$CLIENT" "$_mode" --exe "$EXE" --out "$_dir" --state "$WORK/state.json" \
    --alias "$_alias" --scope "$FIRST_SCOPE"; _rc=$?
  if [ -f "$_dir/steps.tsv" ]; then
    while IFS="$TAB" read -r s_st s_mode s_title s_ev; do
      record "$s_st" "$_stage" "$s_title" "$s_ev"
    done <"$_dir/steps.tsv"
  fi
  if [ $_rc -ne 0 ] && [ $_rc -ne 1 ]; then
    record FAIL "$_stage" "MCP client for '$_mode' did not finish normally (exit $_rc)" "$_e"
  fi
  # USER-GUIDE.md: "Passwords are redacted from every result and log."
  _leak=""
  while IFS= read -r _f; do contains_secret "$_f" && _leak="$_leak $(basename "$_f")"; done <<EOF
$(find "$_dir" -type f; echo "$_e")
EOF
  if [ -n "$_leak" ]; then
    record FAIL "$_stage" "the password appears in MCP output or server stderr:$_leak (masked here)" ""
  else
    record PASS "$_stage" "no MCP result and no server stderr line contains the password" ""
  fi
}

if have_exe && [ $PROVISIONED = 1 ]; then
  run_session first "$ST" "$ALIAS"
else
  record BLOCKED "$ST" "MCP session" "" "Needs an installed executable and a provisioned alias."
fi

# =============================================================================
# Stage 6 - Restart and reconnection (the first session closed stdin and the
#           server exited; USER-GUIDE.md "The server answers only while its input is open")
# =============================================================================
ST="6 Restart"
if have_exe && [ $PROVISIONED = 1 ]; then
  E=$(evfile 6-procs)
  pgrep -fl "$EXE" >"$E" 2>&1 || echo "no SqlInvestigator process is running between sessions" >>"$E"
  if pgrep -f "$EXE" >/dev/null 2>&1; then record FAIL "$ST" "a server process is still running after the first session ended" "$E"
  else record PASS "$ST" "no server process left running before the restart" "$E"; fi
  run_session restart "$ST" "$ALIAS"
else
  record BLOCKED "$ST" "restart session" "" "Needs an installed executable and a provisioned alias."
fi

# =============================================================================
# Stage 7 - Removing stored credentials (MACOS.md "How the keychain is used" -> "Removing an alias";
#           USER-GUIDE.md "2. Connect a database": `remove --alias`)
# =============================================================================
ST="7 Remove credentials"
if have_exe && [ $PROVISIONED = 1 ]; then
  # 7a. `SqlInvestigator remove --alias <name>` at the terminal deletes catalog entry AND keychain item.
  E=$(evfile 7-remove)
  run_step 60 "$E" "$EXE remove --alias $ALIAS" "$EXE" remove --alias "$ALIAS"; rc=$?
  [ $rc -eq 0 ] && record PASS "$ST" "remove --alias $ALIAS exits 0" "$E" || record FAIL "$ST" "remove --alias $ALIAS failed (exit $rc)" "$E"
  E=$(evfile 7-kc)
  run_step 30 "$E" "security find-generic-password -l \"$LABEL\"" security find-generic-password -l "$LABEL"; rc=$?
  if [ $rc -ne 0 ] && [ $rc -ne 124 ]; then record PASS "$ST" "keychain item \"$LABEL\" is gone (security exit $rc)" "$E"
  else record FAIL "$ST" "keychain item \"$LABEL\" still exists after remove" "$E"; fi
  E=$(evfile 7-list)
  run_step 60 "$E" "$EXE list" "$EXE" list
  grep -q "^$ALIAS$TAB" "$E" && record FAIL "$ST" "list still shows $ALIAS" "$E" || record PASS "$ST" "list no longer shows $ALIAS" "$E"
  # What the server says when asked to query the removed alias.
  run_session removed "$ST" "$ALIAS"

  # 7b. MACOS.md: "`connection_remove`, called by Claude, removes an alias from the catalog but
  # leaves its keychain item." Check that promise with a second alias, then try to clean up the
  # orphaned item the documented way (console `remove --alias`).
  E=$(evfile 7b-provision)
  FEED_SECRET=1
  run_step 180 "$E" "$(provision_display "$ALIAS2" "$TRUST_FLAG")" \
    "$EXE" provision --alias "$ALIAS2" --server "$SQLINV_SQL_SERVER" --database "$SQLINV_SQL_DATABASE" \
    --user "$SQLINV_SQL_LOGIN" --environment fixture "${SCOPE_ARGS[@]}" $TRUST_FLAG; rc=$?
  FEED_SECRET=0
  if [ $rc -eq 0 ]; then
    record PASS "$ST" "provisioned a second alias $ALIAS2 for the connection_remove check" "$E"
    run_session conn-remove "$ST" "$ALIAS2"
    E=$(evfile 7b-kc)
    run_step 30 "$E" "security find-generic-password -l \"$LABEL2\"" security find-generic-password -l "$LABEL2"; rc=$?
    [ $rc -eq 0 ] && record PASS "$ST" "after connection_remove the keychain item is still there, as MACOS.md says" "$E" \
                  || record FAIL "$ST" "after connection_remove the keychain item is gone (MACOS.md says it stays)" "$E"
    E=$(evfile 7b-remove)
    run_step 60 "$E" "$EXE remove --alias $ALIAS2   (alias already gone from the catalog)" "$EXE" remove --alias "$ALIAS2"; rc=$?
    run_step 30 "$E" "security find-generic-password -l \"$LABEL2\"" security find-generic-password -l "$LABEL2"; kc=$?
    if [ $kc -ne 0 ] && [ $kc -ne 124 ]; then
      record PASS "$ST" "console remove --alias deletes the orphaned keychain item left by connection_remove (remove exit $rc)" "$E"
    else
      record FAIL "$ST" "console remove --alias does NOT delete the keychain item orphaned by connection_remove (remove exit $rc); the docs give only a Keychain Access GUI route" "$E"
      E=$(evfile 7b-cleanup)
      run_step 30 "$E" "security delete-generic-password -l \"$LABEL2\"   (cleanup, not a documented step)" \
        security delete-generic-password -l "$LABEL2"
      record INFO "$ST" "cleaned up the orphaned item with security delete-generic-password" "$E"
    fi
  else
    record BLOCKED "$ST" "connection_remove keeps the keychain item" "$E" "Could not provision the second alias."
  fi
else
  record BLOCKED "$ST" "remove --alias, keychain check, server response" "" "Needs an installed executable and a provisioned alias."
fi

# =============================================================================
# Stage 8 - Uninstall (MACOS.md "Uninstall"; USER-GUIDE.md "Uninstall")
# =============================================================================
ST="8 Uninstall"
# Step 1: `SqlInvestigator list`, then `remove --alias <name>` for each.
if have_exe; then
  E=$(evfile 8-list)
  run_step 60 "$E" "$EXE list" "$EXE" list; rc=$?
  REMAINING=$(awk -F"$TAB" 'NR > 0 && $1 ~ /^[A-Za-z][A-Za-z0-9_-]*$/ && $1 != "alias" && NF > 1 { print $1 }' "$E")
  for a in $REMAINING; do
    run_step 60 "$E" "$EXE remove --alias $a" "$EXE" remove --alias "$a"
  done
  record PASS "$ST" "step 1: list, then remove --alias for each remaining alias (${REMAINING:-none left})" "$E"
else
  blocked_no_exe "$ST" "step 1: list / remove"
fi
# Step 2: `claude mcp remove sql-investigator --scope user` - Claude Code is not on this runner.
record BLOCKED "$ST" "step 2: claude mcp remove sql-investigator --scope user (Claude Code not installed)" ""
# Step 3: delete ~/Applications/SqlInvestigator.
E=$(evfile 8-rmapp)
run_step 30 "$E" "rm -rf $INSTALL_DIR" rm -rf "$INSTALL_DIR"
[ ! -e "$INSTALL_DIR" ] && record PASS "$ST" "step 3: program folder deleted" "$E" || record FAIL "$ST" "step 3: program folder still exists" "$E"
# Step 4: delete ~/Library/Application Support/SqlInvestigator.
E=$(evfile 8-rmdata)
run_step 30 "$E" "rm -rf \"$APP_SUPPORT\"" rm -rf "$APP_SUPPORT"
[ ! -e "$APP_SUPPORT" ] && record PASS "$ST" "step 4: data folder deleted" "$E" || record FAIL "$ST" "step 4: data folder still exists" "$E"
# Afterwards: no SqlInvestigator keychain items remain (attributes only; dump-keychain without -d reads no secrets).
E=$(evfile 8-kc)
run_tb 60 "$WORK/dump.txt" security dump-keychain
n=$(grep -c '"svce"<blob>="SqlInvestigator/1' "$WORK/dump.txt" 2>/dev/null)
grep -B6 -A6 'SqlInvestigator/1' "$WORK/dump.txt" >"$E" 2>/dev/null
echo "SqlInvestigator items left in the default keychain: ${n:-0}" >>"$E"
rm -f "$WORK/dump.txt"
[ "${n:-0}" -eq 0 ] && record PASS "$ST" "no SqlInvestigator keychain items remain" "$E" || record FAIL "$ST" "$n SqlInvestigator keychain item(s) remain after uninstall" "$E"
# USER-GUIDE.md: "SQL Investigator writes nothing ... outside these folders." Look for leftovers.
E=$(evfile 8-leftovers)
find "$HOME" -maxdepth 5 -iname '*sqlinvestigator*' -not -path "$WORK/*" -not -path "$HERE/*" 2>/dev/null >"$E"
find "${TMPDIR:-/tmp}" -maxdepth 4 -iname '*sqlinvestigator*' -not -path "$WORK*" 2>/dev/null >>"$E"
if [ -s "$E" ]; then record INFO "$ST" "files named like SqlInvestigator remain after uninstall (check whether they are the product's)" "$E"
else record PASS "$ST" "nothing named SqlInvestigator remains under \$HOME or \$TMPDIR" "$E"; fi

# =============================================================================
# Report
# =============================================================================
mkdir -p "$(dirname "$REPORT")"
{
  echo "# SQL Investigator - macOS cold-start evaluation"
  echo
  echo "- Started: $STARTED, finished: $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
  echo "- Runner: $(sw_vers -productName 2>/dev/null) $(sw_vers -productVersion 2>/dev/null), $(uname -m), user $(id -un)"
  echo "- Tarball: \`$(basename "${SQLINV_TARBALL:-none}")\`"
  echo "- Followed only docs/MACOS.md and docs/USER-GUIDE.md; MCP emulated by eng/coldstart/mcp-client.mjs"
  echo "- Result: **$NPASS PASS, $NFAIL FAIL, $NBLOCK BLOCKED, $NINFO INFO**"
  echo
  echo "## Summary"
  echo
  echo "| Status | Stage | Check |"
  echo "|---|---|---|"
  while IFS="$TAB" read -r a b c; do
    printf '| %s | %s | %s |\n' "$a" "$b" "$(printf '%s' "$c" | sed 's/|/\\|/g')"
  done <"$SUMMARY"
  echo
  echo "## Evidence"
  echo
  cat "$DETAILS"
} | mask >"$REPORT"
chmod 600 "$REPORT" 2>/dev/null
if contains_secret "$REPORT"; then
  # Cannot happen (everything passes through mask), but never leave a secret behind.
  : >"$REPORT"; echo "REPORT WITHHELD: it contained the password" >"$REPORT"; NFAIL=$((NFAIL + 1))
fi
echo "Report: $REPORT ($NPASS PASS, $NFAIL FAIL, $NBLOCK BLOCKED, $NINFO INFO)" >&2

[ -n "${SQLINV_KEEP_WORK:-}" ] || rm -rf "$WORK"
[ $NFAIL -eq 0 ]

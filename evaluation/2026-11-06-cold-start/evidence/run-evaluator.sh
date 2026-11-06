#!/usr/bin/env bash
# Launches the independent cold-start evaluator: a separate Claude Code process with a fresh context, no MCP servers
# except the one it registers itself (phase 2), no user settings, and read access denied outside its workspace.
# usage: run-evaluator.sh phase1 | phase2 <session-id>
set -euo pipefail
W='C:\sqlinv-eval\coldstart-1'
LOGS="$(dirname "$0")/evaluator-logs"; mkdir -p "$LOGS"
cd /c/sqlinv-eval/coldstart-1
# not a child of this session: drop the parent session's markers
unset CLAUDECODE CLAUDE_CODE_ENTRYPOINT CLAUDE_CODE_SESSION_ID CLAUDE_CODE_CHILD_SESSION CLAUDE_PID CLAUDE_CODE_MESSAGING_SOCKET CLAUDE_CODE_MESSAGING_TOKEN CLAUDE_CODE_SESSION_ATTENDED CLAUDE_CODE_EXECPATH CONTINUUM_API_KEY || true
DENY='["Read(//c/projects/**)","Read(//c/Users/kangm/AppData/Local/Temp/claude/8304409b6c461889/**)","Read(//c/Users/kangm/AppData/Local/SqlInvestigator/**)","Read(//c/Users/kangm/.claude/projects/C--projects-avlon-sql-investigator/**)","Read(//c/actions-runner/**)","Read(//c/sqlinv-eval/smoke/**)","Edit(//c/projects/**)","Write(//c/projects/**)","mcp__sql-investigator__knowledge_review","mcp__sql-investigator__connection_remove"]'
SETTINGS="{\"permissions\":{\"deny\":$DENY}}"
BASE_TOOLS=(Bash PowerShell Read Write Edit Glob Grep)
MCP_TOOLS=(mcp__sql-investigator__connection_list mcp__sql-investigator__schema_inspect mcp__sql-investigator__sql_validate mcp__sql-investigator__sql_execute mcp__sql-investigator__investigation_start mcp__sql-investigator__investigation_record mcp__sql-investigator__investigation_get mcp__sql-investigator__knowledge_search mcp__sql-investigator__knowledge_propose)
case "$1" in
  phase1)
    claude -p "You are starting fresh. Read C:\\sqlinv-eval\\coldstart-1\\TASK.md and carry out Phase 1 exactly as it describes." \
      --output-format stream-json --verbose --setting-sources project,local --strict-mcp-config \
      --settings "$SETTINGS" --allowedTools "${BASE_TOOLS[@]}" \
      > "$LOGS/phase1.jsonl" 2> "$LOGS/phase1.stderr" ;;
  phase2)
    claude -p "Claude Code has been restarted with the MCP server you registered in Phase 1 loaded from C:\\sqlinv-eval\\coldstart-1\\.mcp.json. Continue with Phase 2 of C:\\sqlinv-eval\\coldstart-1\\TASK.md." \
      --resume "$2" --output-format stream-json --verbose --setting-sources project,local --strict-mcp-config \
      --mcp-config "$W\\.mcp.json" --settings "$SETTINGS" --allowedTools "${BASE_TOOLS[@]}" "${MCP_TOOLS[@]}" \
      > "$LOGS/phase2.jsonl" 2> "$LOGS/phase2.stderr" ;;
esac
echo "exit $?"

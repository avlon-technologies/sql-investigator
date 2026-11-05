// acceptance/adapter.mjs — scaffolded by `continuum package adapter init` from the scenarios of sql-investigator.
//
// One stub per step key. Replace each `unimplemented(…)` with a binding to YOUR implementation;
// `continuum package run` reports the first unbound step of every scenario, by name, until none is left.
//
// THE CONTRACT (acceptance/run.mjs): createAdapter({ createFakeProvider, env }) → { steps, beforeScenario?, afterScenario? }
//   a step: async (ctx, args, step) => { … throw to fail … }
//   ctx = { scenario, provider, state, assert, env, log } — ctx.state is yours, fresh for every scenario
//
// TIME MOVES. A scenario sets the clock ("it is 08:40 site time") and later moves it. Anything that is
// stamped or expires — sessions, tokens, "now" — must read `clock.now(ctx)`, never Date.now(): an
// implementation that keeps real time loses every scenario whose session outlives a clock jump.
export const clock = {
  now: (ctx) => ctx.state.now ?? new Date(),
  set: (ctx, when) => { ctx.state.now = new Date(when); return ctx.state.now; },
};

const unimplemented = (key, args) => {
  throw new Error(`unimplemented step "${key}" (args: ${Object.keys(args ?? {}).join(', ') || 'none'}) — bind it in acceptance/adapter.mjs`);
};

export function createAdapter({ createFakeProvider, env }) {
  return {
    beforeScenario(ctx) { ctx.state = {}; },
    afterScenario(ctx) {},
    steps: {
      // audit.has-budget-events  phases: then  args: none  used by 1 scenario
      //   "an audit event is written for every budget enforcement"
      'audit.has-budget-events': async (ctx, args, step) => unimplemented('audit.has-budget-events', args),
      // connection.exercise-error-paths  phases: when  args: none  used by 1 scenario
      //   "connection errors are provoked (wrong password, unreachable server)"
      'connection.exercise-error-paths': async (ctx, args, step) => unimplemented('connection.exercise-error-paths', args),
      // connection.fixture  phases: given  args: alias, scope  used by 6 scenarios
      //   "alias "alpha" points at a disposable SQL Server fixture, scoped to dbo.Orders, through a read-only principal"
      //   "alias "beta" points at a disposable SQL Server fixture, scoped to dbo.Shipments, through a read-only principal"
      'connection.fixture': async (ctx, args, step) => unimplemented('connection.fixture', args),
      // connection.list  phases: when  args: none  used by 1 scenario
      //   "Claude Code calls connection_list"
      'connection.list': async (ctx, args, step) => unimplemented('connection.list', args),
      // connection.metadata-has-no-secret  phases: then  args: alias  used by 1 scenario
      //   "the listed metadata for "alpha" contains no secret"
      'connection.metadata-has-no-secret': async (ctx, args, step) => unimplemented('connection.metadata-has-no-secret', args),
      // connection.provision  phases: given  args: alias, credential  used by 2 scenarios
      //   "the developer provisions alias "alpha" at the local console, typing the secret there"
      'connection.provision': async (ctx, args, step) => unimplemented('connection.provision', args),
      // connection.verify-local-only-provisioning  phases: then  args: none  used by 1 scenario
      //   "no MCP tool accepts or returns a credential, and provisioning is not an MCP tool"
      'connection.verify-local-only-provisioning': async (ctx, args, step) => unimplemented('connection.verify-local-only-provisioning', args),
      // credential.dpapi-current-user  phases: then  args: alias  used by 1 scenario
      //   "the credential for "alpha" is stored DPAPI CurrentUser protected in a current-user-only file"
      'credential.dpapi-current-user': async (ctx, args, step) => unimplemented('credential.dpapi-current-user', args),
      // investigation.correlate-separate  phases: when  args: aliases, key  used by 1 scenario
      //   "Claude Code queries alpha and beta separately and correlates on OrderId"
      'investigation.correlate-separate': async (ctx, args, step) => unimplemented('investigation.correlate-separate', args),
      // investigation.history-complete  phases: then  args: none  used by 1 scenario
      //   "the question, hypothesis, query id, exact SQL, alias, finding and uncertainty are all present, and no raw result rows"
      'investigation.history-complete': async (ctx, args, step) => unimplemented('investigation.history-complete', args),
      // investigation.labels-unmatched-and-ambiguous  phases: then  args: none  used by 1 scenario
      //   "unmatched and ambiguous records are labelled as such"
      'investigation.labels-unmatched-and-ambiguous': async (ctx, args, step) => unimplemented('investigation.labels-unmatched-and-ambiguous', args),
      // investigation.no-cross-database-sql  phases: then  args: none  used by 1 scenario
      //   "no executed statement references more than one database"
      'investigation.no-cross-database-sql': async (ctx, args, step) => unimplemented('investigation.no-cross-database-sql', args),
      // investigation.record  phases: when  args: alias, finding, hypothesis, queryId, sql, uncertainty  used by 1 scenario
      //   "Claude Code records a hypothesis, the query evidence, a finding and its uncertainty"
      'investigation.record': async (ctx, args, step) => unimplemented('investigation.record', args),
      // investigation.reopen  phases: when  args: none  used by 1 scenario
      //   "the investigation is reopened with investigation_get"
      'investigation.reopen': async (ctx, args, step) => unimplemented('investigation.reopen', args),
      // investigation.start  phases: given  args: question  used by 1 scenario
      //   "Claude Code starts an investigation: "Why is order 42 delayed?""
      'investigation.start': async (ctx, args, step) => unimplemented('investigation.start', args),
      // knowledge.empty  phases: given  args: none  used by 1 scenario
      //   "the knowledge store holds no relationships"
      'knowledge.empty': async (ctx, args, step) => unimplemented('knowledge.empty', args),
      // knowledge.negative-knowledge-retained  phases: then  args: none  used by 1 scenario
      //   "the rejection and its evidence are kept"
      'knowledge.negative-knowledge-retained': async (ctx, args, step) => unimplemented('knowledge.negative-knowledge-retained', args),
      // knowledge.propose  phases: when  args: evidence, source, target  used by 1 scenario
      //   "Claude Code proposes that alpha.Orders.OrderId relates to beta.Shipments.OrderId, citing fixture-q1"
      'knowledge.propose': async (ctx, args, step) => unimplemented('knowledge.propose', args),
      // knowledge.reject  phases: when  args: none  used by 1 scenario
      //   "the developer rejects the proposal through knowledge_review"
      'knowledge.reject': async (ctx, args, step) => unimplemented('knowledge.reject', args),
      // knowledge.rejected-not-promoted  phases: then  args: none  used by 1 scenario
      //   "the relationship is rejected and never reported as verified"
      'knowledge.rejected-not-promoted': async (ctx, args, step) => unimplemented('knowledge.rejected-not-promoted', args),
      // logs.exclude-secret  phases: then  args: value  used by 1 scenario
      //   "no stderr diagnostic contains "fixture-secret""
      'logs.exclude-secret': async (ctx, args, step) => unimplemented('logs.exclude-secret', args),
      // mcp.exercise-routine-tools  phases: when  args: none  used by 1 scenario
      //   "Claude Code calls each routine tool"
      'mcp.exercise-routine-tools': async (ctx, args, step) => unimplemented('mcp.exercise-routine-tools', args),
      // mcp.start-stdio  phases: given  args: none  used by 1 scenario
      //   "Claude Code starts LocalMCPServer as an MCP stdio child process"
      'mcp.start-stdio': async (ctx, args, step) => unimplemented('mcp.start-stdio', args),
      // mcp.stdout-protocol-only  phases: then  args: none  used by 1 scenario
      //   "everything written to stdout is a well-formed MCP protocol message"
      'mcp.stdout-protocol-only': async (ctx, args, step) => unimplemented('mcp.stdout-protocol-only', args),
      // mcp.tool-surface-exact  phases: then  args: none  used by 1 scenario
      //   "the advertised tools are exactly connection_list, connection_remove, schema_inspect, sql_validate, sql_execute, investigation_start, investigation_record, investigation_get, knowledge_search, knowledge_propose and knowledge_review"
      'mcp.tool-surface-exact': async (ctx, args, step) => unimplemented('mcp.tool-surface-exact', args),
      // output.excludes-secret  phases: then  args: value  used by 3 scenarios
      //   "no MCP output contains the secret "fixture-secret""
      'output.excludes-secret': async (ctx, args, step) => unimplemented('output.excludes-secret', args),
      // schema.changed-column-visible  phases: then  args: none  used by 1 scenario
      //   "the changed column is visible"
      'schema.changed-column-visible': async (ctx, args, step) => unimplemented('schema.changed-column-visible', args),
      // schema.fixture-change  phases: when  args: alias, change  used by 1 scenario
      //   "the fixture schema of "alpha" changes (rename-column)"
      'schema.fixture-change': async (ctx, args, step) => unimplemented('schema.fixture-change', args),
      // schema.has-source-and-time  phases: then  args: none  used by 1 scenario
      //   "the snapshot states its source and capture time"
      'schema.has-source-and-time': async (ctx, args, step) => unimplemented('schema.has-source-and-time', args),
      // schema.no-fabricated-results  phases: then  args: none  used by 1 scenario
      //   "no stale or invented object is reported"
      'schema.no-fabricated-results': async (ctx, args, step) => unimplemented('schema.no-fabricated-results', args),
      // schema.refresh-after-drift  phases: when  args: alias  used by 1 scenario
      //   "Claude Code inspects "alpha" again and the snapshot refreshes"
      'schema.refresh-after-drift': async (ctx, args, step) => unimplemented('schema.refresh-after-drift', args),
      // schema.snapshot  phases: given  args: alias  used by 1 scenario
      //   "a schema snapshot of "alpha" exists"
      'schema.snapshot': async (ctx, args, step) => unimplemented('schema.snapshot', args),
      // sql.actionable-sanitized-errors  phases: then  args: none  used by 1 scenario
      //   "each failure returns an actionable, sanitized error and no result rows"
      'sql.actionable-sanitized-errors': async (ctx, args, step) => unimplemented('sql.actionable-sanitized-errors', args),
      // sql.all-rejected-before-execution  phases: then  args: none  used by 2 scenarios
      //   "every statement is rejected before it reaches SQL Server, with an audited reason"
      'sql.all-rejected-before-execution': async (ctx, args, step) => unimplemented('sql.all-rejected-before-execution', args),
      // sql.budgets-enforced  phases: then  args: none  used by 1 scenario
      //   "each over-budget query is cancelled, refused or truncated with an indicator"
      'sql.budgets-enforced': async (ctx, args, step) => unimplemented('sql.budgets-enforced', args),
      // sql.exact-statement  phases: then  args: none  used by 1 scenario
      //   "the result carries the exact SQL that ran"
      'sql.exact-statement': async (ctx, args, step) => unimplemented('sql.exact-statement', args),
      // sql.execute  phases: when  args: alias, sql  used by 1 scenario
      //   "Claude Code calls sql_execute on "alpha" with: SELECT OrderId FROM dbo.Orders"
      'sql.execute': async (ctx, args, step) => unimplemented('sql.execute', args),
      // sql.exercise-budgets  phases: when  args: kinds  used by 1 scenario
      //   "queries exceed each budget: timeout, row, byte, concurrency, investigation"
      'sql.exercise-budgets': async (ctx, args, step) => unimplemented('sql.exercise-budgets', args),
      // sql.exercise-failures  phases: when  args: kinds  used by 1 scenario
      //   "queries fail in each way: unavailable-server, cancelled, invalid-credentials, malformed-sql"
      'sql.exercise-failures': async (ctx, args, step) => unimplemented('sql.exercise-failures', args),
      // sql.has-query-id  phases: then  args: none  used by 1 scenario
      //   "the result carries a query id"
      'sql.has-query-id': async (ctx, args, step) => unimplemented('sql.has-query-id', args),
      // sql.has-truncation-indicator  phases: then  args: none  used by 1 scenario
      //   "the result says whether it was truncated"
      'sql.has-truncation-indicator': async (ctx, args, step) => unimplemented('sql.has-truncation-indicator', args),
      // sql.principal-denies-writes  phases: then  args: alias, statements  used by 1 scenario
      //   "independently of the allowlist, the alias's SQL principal, connected directly to the fixture, is denied every write and DDL statement by SQL Server"
      'sql.principal-denies-writes': async (ctx, args, step) => unimplemented('sql.principal-denies-writes', args),
      // sql.reject-corpus  phases: when  args: statements  used by 1 scenario
      //   "Claude Code submits each statement of the write and bypass corpus"
      'sql.reject-corpus': async (ctx, args, step) => unimplemented('sql.reject-corpus', args),
      // sql.reject-policy-corpus  phases: when  args: cases  used by 1 scenario
      //   "Claude Code submits each fail-closed policy case"
      'sql.reject-policy-corpus': async (ctx, args, step) => unimplemented('sql.reject-policy-corpus', args),
      // sql.within-result-budget  phases: then  args: none  used by 1 scenario
      //   "the result stays within the row and byte budget"
      'sql.within-result-budget': async (ctx, args, step) => unimplemented('sql.within-result-budget', args),
      // storage.excludes-plaintext-secret  phases: then  args: value  used by 1 scenario
      //   "no file on disk contains "fixture-secret" in plaintext"
      'storage.excludes-plaintext-secret': async (ctx, args, step) => unimplemented('storage.excludes-plaintext-secret', args),
    },
  };
}

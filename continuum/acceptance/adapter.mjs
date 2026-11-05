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
      //   "audit.has-budget-events"
      'audit.has-budget-events': async (ctx, args, step) => unimplemented('audit.has-budget-events', args),
      // connection.exercise-error-paths  phases: when  args: none  used by 1 scenario
      //   "connection.exercise-error-paths"
      'connection.exercise-error-paths': async (ctx, args, step) => unimplemented('connection.exercise-error-paths', args),
      // connection.fixture  phases: given  args: alias, scope  used by 6 scenarios
      //   "connection.fixture"
      'connection.fixture': async (ctx, args, step) => unimplemented('connection.fixture', args),
      // connection.list  phases: when  args: none  used by 1 scenario
      //   "connection.list"
      'connection.list': async (ctx, args, step) => unimplemented('connection.list', args),
      // connection.metadata-has-no-secret  phases: then  args: alias  used by 1 scenario
      //   "connection.metadata-has-no-secret"
      'connection.metadata-has-no-secret': async (ctx, args, step) => unimplemented('connection.metadata-has-no-secret', args),
      // connection.provision  phases: given  args: alias, credential  used by 2 scenarios
      //   "connection.provision"
      'connection.provision': async (ctx, args, step) => unimplemented('connection.provision', args),
      // connection.verify-local-only-provisioning  phases: then  args: none  used by 1 scenario
      //   "connection.verify-local-only-provisioning"
      'connection.verify-local-only-provisioning': async (ctx, args, step) => unimplemented('connection.verify-local-only-provisioning', args),
      // credential.dpapi-current-user  phases: then  args: alias  used by 1 scenario
      //   "credential.dpapi-current-user"
      'credential.dpapi-current-user': async (ctx, args, step) => unimplemented('credential.dpapi-current-user', args),
      // investigation.correlate-separate  phases: when  args: aliases, key  used by 1 scenario
      //   "investigation.correlate-separate"
      'investigation.correlate-separate': async (ctx, args, step) => unimplemented('investigation.correlate-separate', args),
      // investigation.history-complete  phases: then  args: none  used by 1 scenario
      //   "investigation.history-complete"
      'investigation.history-complete': async (ctx, args, step) => unimplemented('investigation.history-complete', args),
      // investigation.labels-unmatched-and-ambiguous  phases: then  args: none  used by 1 scenario
      //   "investigation.labels-unmatched-and-ambiguous"
      'investigation.labels-unmatched-and-ambiguous': async (ctx, args, step) => unimplemented('investigation.labels-unmatched-and-ambiguous', args),
      // investigation.no-cross-database-sql  phases: then  args: none  used by 1 scenario
      //   "investigation.no-cross-database-sql"
      'investigation.no-cross-database-sql': async (ctx, args, step) => unimplemented('investigation.no-cross-database-sql', args),
      // investigation.record  phases: when  args: alias, finding, hypothesis, queryId, sql, uncertainty  used by 1 scenario
      //   "investigation.record"
      'investigation.record': async (ctx, args, step) => unimplemented('investigation.record', args),
      // investigation.reopen  phases: when  args: none  used by 1 scenario
      //   "investigation.reopen"
      'investigation.reopen': async (ctx, args, step) => unimplemented('investigation.reopen', args),
      // investigation.start  phases: given  args: question  used by 1 scenario
      //   "investigation.start"
      'investigation.start': async (ctx, args, step) => unimplemented('investigation.start', args),
      // knowledge.empty  phases: given  args: none  used by 1 scenario
      //   "knowledge.empty"
      'knowledge.empty': async (ctx, args, step) => unimplemented('knowledge.empty', args),
      // knowledge.negative-knowledge-retained  phases: then  args: none  used by 1 scenario
      //   "knowledge.negative-knowledge-retained"
      'knowledge.negative-knowledge-retained': async (ctx, args, step) => unimplemented('knowledge.negative-knowledge-retained', args),
      // knowledge.propose  phases: when  args: evidence, source, target  used by 1 scenario
      //   "knowledge.propose"
      'knowledge.propose': async (ctx, args, step) => unimplemented('knowledge.propose', args),
      // knowledge.reject  phases: when  args: none  used by 1 scenario
      //   "knowledge.reject"
      'knowledge.reject': async (ctx, args, step) => unimplemented('knowledge.reject', args),
      // knowledge.rejected-not-promoted  phases: then  args: none  used by 1 scenario
      //   "knowledge.rejected-not-promoted"
      'knowledge.rejected-not-promoted': async (ctx, args, step) => unimplemented('knowledge.rejected-not-promoted', args),
      // logs.exclude-secret  phases: then  args: value  used by 1 scenario
      //   "logs.exclude-secret"
      'logs.exclude-secret': async (ctx, args, step) => unimplemented('logs.exclude-secret', args),
      // mcp.exercise-routine-tools  phases: when  args: none  used by 1 scenario
      //   "mcp.exercise-routine-tools"
      'mcp.exercise-routine-tools': async (ctx, args, step) => unimplemented('mcp.exercise-routine-tools', args),
      // mcp.start-stdio  phases: given  args: none  used by 1 scenario
      //   "mcp.start-stdio"
      'mcp.start-stdio': async (ctx, args, step) => unimplemented('mcp.start-stdio', args),
      // output.excludes-secret  phases: then  args: value  used by 3 scenarios
      //   "output.excludes-secret"
      'output.excludes-secret': async (ctx, args, step) => unimplemented('output.excludes-secret', args),
      // schema.changed-column-visible  phases: then  args: none  used by 1 scenario
      //   "schema.changed-column-visible"
      'schema.changed-column-visible': async (ctx, args, step) => unimplemented('schema.changed-column-visible', args),
      // schema.fixture-change  phases: when  args: alias, change  used by 1 scenario
      //   "schema.fixture-change"
      'schema.fixture-change': async (ctx, args, step) => unimplemented('schema.fixture-change', args),
      // schema.has-source-and-time  phases: then  args: none  used by 1 scenario
      //   "schema.has-source-and-time"
      'schema.has-source-and-time': async (ctx, args, step) => unimplemented('schema.has-source-and-time', args),
      // schema.no-fabricated-results  phases: then  args: none  used by 1 scenario
      //   "schema.no-fabricated-results"
      'schema.no-fabricated-results': async (ctx, args, step) => unimplemented('schema.no-fabricated-results', args),
      // schema.refresh-after-drift  phases: when  args: alias  used by 1 scenario
      //   "schema.refresh-after-drift"
      'schema.refresh-after-drift': async (ctx, args, step) => unimplemented('schema.refresh-after-drift', args),
      // schema.snapshot  phases: given  args: alias  used by 1 scenario
      //   "schema.snapshot"
      'schema.snapshot': async (ctx, args, step) => unimplemented('schema.snapshot', args),
      // sql.actionable-sanitized-errors  phases: then  args: none  used by 1 scenario
      //   "sql.actionable-sanitized-errors"
      'sql.actionable-sanitized-errors': async (ctx, args, step) => unimplemented('sql.actionable-sanitized-errors', args),
      // sql.all-rejected-before-execution  phases: then  args: none  used by 2 scenarios
      //   "sql.all-rejected-before-execution"
      'sql.all-rejected-before-execution': async (ctx, args, step) => unimplemented('sql.all-rejected-before-execution', args),
      // sql.budgets-enforced  phases: then  args: none  used by 1 scenario
      //   "sql.budgets-enforced"
      'sql.budgets-enforced': async (ctx, args, step) => unimplemented('sql.budgets-enforced', args),
      // sql.exact-statement  phases: then  args: none  used by 1 scenario
      //   "sql.exact-statement"
      'sql.exact-statement': async (ctx, args, step) => unimplemented('sql.exact-statement', args),
      // sql.execute  phases: when  args: alias, sql  used by 1 scenario
      //   "sql.execute"
      'sql.execute': async (ctx, args, step) => unimplemented('sql.execute', args),
      // sql.exercise-budgets  phases: when  args: kinds  used by 1 scenario
      //   "sql.exercise-budgets"
      'sql.exercise-budgets': async (ctx, args, step) => unimplemented('sql.exercise-budgets', args),
      // sql.exercise-failures  phases: when  args: kinds  used by 1 scenario
      //   "sql.exercise-failures"
      'sql.exercise-failures': async (ctx, args, step) => unimplemented('sql.exercise-failures', args),
      // sql.has-query-id  phases: then  args: none  used by 1 scenario
      //   "sql.has-query-id"
      'sql.has-query-id': async (ctx, args, step) => unimplemented('sql.has-query-id', args),
      // sql.has-truncation-indicator  phases: then  args: none  used by 1 scenario
      //   "sql.has-truncation-indicator"
      'sql.has-truncation-indicator': async (ctx, args, step) => unimplemented('sql.has-truncation-indicator', args),
      // sql.reject-corpus  phases: when  args: statements  used by 1 scenario
      //   "sql.reject-corpus"
      'sql.reject-corpus': async (ctx, args, step) => unimplemented('sql.reject-corpus', args),
      // sql.reject-policy-corpus  phases: when  args: cases  used by 1 scenario
      //   "sql.reject-policy-corpus"
      'sql.reject-policy-corpus': async (ctx, args, step) => unimplemented('sql.reject-policy-corpus', args),
      // sql.within-result-budget  phases: then  args: none  used by 1 scenario
      //   "sql.within-result-budget"
      'sql.within-result-budget': async (ctx, args, step) => unimplemented('sql.within-result-budget', args),
      // storage.excludes-plaintext-secret  phases: then  args: value  used by 1 scenario
      //   "storage.excludes-plaintext-secret"
      'storage.excludes-plaintext-secret': async (ctx, args, step) => unimplemented('storage.excludes-plaintext-secret', args),
    },
  };
}

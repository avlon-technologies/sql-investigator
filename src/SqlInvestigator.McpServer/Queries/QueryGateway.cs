using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlInvestigator.Connections;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Queries;

/// <summary>ValidationVerdict as returned by sql_validate.</summary>
internal sealed record ValidationVerdict(string Alias, bool Accepted, IReadOnlyList<string> Reasons, IReadOnlyList<string> Objects);

/// <summary>QueryEvidence as returned by sql_execute.</summary>
internal sealed record QueryEvidence
{
    public required string QueryId { get; init; }
    public required string Alias { get; init; }
    public required string Sql { get; init; }
    public string? InvestigationId { get; init; }
    public required string StartedAt { get; init; }
    public required string CompletedAt { get; init; }
    public required int RowCount { get; init; }
    public required bool Truncated { get; init; }

    /// <summary><c>rows</c> or <c>bytes</c> when <see cref="Truncated"/>; which budget stopped the read.</summary>
    public string? TruncatedBy { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }
    public required int RowLimit { get; init; }
    public required int ByteLimit { get; init; }
}

/// <summary>The investigation as returned by investigation_get: its entries plus the provenance of its queries.</summary>
internal sealed record InvestigationView(InvestigationRecord Investigation, IReadOnlyList<QueryRecord> Queries);

/// <summary>
/// QueryGateway: validates SQL, enforces the budgets, executes one bounded SELECT against one alias and records the
/// evidence. Handles ValidateSql, ExecuteSql, StartInvestigation, RecordFinding and GetInvestigation. The only writer
/// of InvestigationHistory.
/// </summary>
internal sealed class QueryGateway(
    ConnectionCatalogReader catalog,
    SqlConnectionFactory connections,
    InvestigationHistory history,
    Settings settings,
    ILogger<QueryGateway> log)
{
    private readonly SemaphoreSlim running = new(settings.MaxConcurrentQueries, settings.MaxConcurrentQueries);

    public ValidationVerdict Validate(string? alias, string? sql)
    {
        var entry = RequireAudited(alias, sql);
        var result = SqlValidator.Validate(sql, entry, settings.MaxSqlLength);
        if (!result.Accepted)
        {
            history.Audit("sql.rejected", entry.Alias, detail: "sql_validate: " + Clip(sql), reasons: result.Reasons);
        }

        return new ValidationVerdict(entry.Alias, result.Accepted, result.Reasons, result.Objects);
    }

    public async Task<QueryEvidence> ExecuteAsync(string? alias, string? sql, string? investigationId, CancellationToken cancel)
    {
        var entry = RequireAudited(alias, sql);
        var verdict = SqlValidator.Validate(sql, entry, settings.MaxSqlLength);
        if (!verdict.Accepted)
        {
            history.Audit("sql.rejected", entry.Alias, investigationId: investigationId, detail: "sql_execute: " + Clip(sql), reasons: verdict.Reasons);
            throw new InvestigatorException(new InvestigatorError(
                "sql.rejected",
                "The statement was rejected before execution: " + string.Join(" ", verdict.Reasons),
                "Submit exactly one SELECT over the alias's approved objects and columns (call schema_inspect for them).")
            { Reasons = verdict.Reasons });
        }

        var text = sql!;
        var investigation = string.IsNullOrWhiteSpace(investigationId) ? null : investigationId.Trim();

        if (!running.Wait(0, CancellationToken.None))
        {
            history.Audit("budget.concurrency", entry.Alias, investigationId: investigation,
                detail: $"refused: {settings.MaxConcurrentQueries} queries already executing");
            throw new InvestigatorException(new InvestigatorError(
                "budget.concurrency",
                $"{settings.MaxConcurrentQueries} queries are already executing; this one was refused and not run.",
                "Wait for a running query to finish, then submit it again."));
        }

        try
        {
            var (record, refusal) = Reserve(entry.Alias, text, investigation);
            if (refusal is not null)
            {
                throw new InvestigatorException(refusal);
            }

            return await RunAsync(entry, record, cancel);
        }
        finally
        {
            running.Release();
        }
    }

    public InvestigationRecord Start(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw InvestigatorException.Input("An investigation needs a question.", "Pass the question the investigation sets out to answer.");
        }

        return history.Update(document =>
        {
            var record = new InvestigationRecord { InvestigationId = InvestigationHistory.NewId("inv"), Question = Redaction.Apply(question.Trim()), StartedAt = history.Now() };
            document.Investigations.Add(record);
            history.Audit(document, "investigation.started", investigationId: record.InvestigationId);
            return record;
        });
    }

    public InvestigationEntry Record(string? investigationId, string? hypothesis, IReadOnlyList<string>? queryIds, IReadOnlyList<string>? aliases,
        string? finding, string? uncertainty)
    {
        var id = investigationId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hypothesis) && string.IsNullOrWhiteSpace(finding) && string.IsNullOrWhiteSpace(uncertainty))
        {
            throw InvestigatorException.Input("An entry needs a hypothesis, a finding or an uncertainty.", "Record what was hypothesised, found or left uncertain.");
        }

        return history.Update(document =>
        {
            var investigation = FindInvestigation(document, id);
            var ids = (queryIds ?? []).Select(q => q.Trim()).Where(q => q.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            var queries = new List<QueryRecord>();
            foreach (var queryId in ids)
            {
                queries.Add(document.Queries.FirstOrDefault(q => q.QueryId == queryId)
                    ?? throw new InvestigatorException(new InvestigatorError(
                        "query.unknown",
                        $"No executed query has id \"{queryId}\"; evidence must cite queries that actually ran.",
                        "Cite the queryId returned by sql_execute, or omit queryIds.")));
            }

            // the aliases of the cited queries are facts; any others the caller names are kept beside them
            var allAliases = queries.Select(q => q.Alias).Concat((aliases ?? []).Select(a => ConnectionCatalogReader.NormalizeAlias(a)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var entry = new InvestigationEntry
            {
                InvestigationId = investigation.InvestigationId,
                Hypothesis = Redaction.Apply(hypothesis?.Trim()),
                QueryIds = ids,
                Aliases = allAliases,
                Finding = Redaction.Apply(finding?.Trim()),
                Uncertainty = Redaction.Apply(uncertainty?.Trim()),
                RecordedAt = history.Now(),
            };
            investigation.Entries.Add(entry);
            history.Audit(document, "investigation.recorded", investigationId: investigation.InvestigationId, detail: $"{ids.Count} query id(s)");
            return entry;
        });
    }

    public InvestigationView Get(string? investigationId)
    {
        var document = history.Read();
        var investigation = FindInvestigation(document, investigationId?.Trim() ?? string.Empty);
        var cited = investigation.Entries.SelectMany(e => e.QueryIds).ToHashSet(StringComparer.Ordinal);
        var queries = document.Queries
            .Where(q => q.InvestigationId == investigation.InvestigationId || cited.Contains(q.QueryId))
            .OrderBy(q => q.StartedAt, StringComparer.Ordinal)
            .ToList();
        return new InvestigationView(investigation, queries);
    }

    private static InvestigationRecord FindInvestigation(HistoryDocument document, string id)
        => document.Investigations.FirstOrDefault(i => i.InvestigationId == id)
            ?? throw new InvestigatorException(new InvestigatorError(
                "investigation.unknown",
                $"No investigation has id \"{id}\".",
                "Call investigation_start for a new investigation, or pass the investigationId it returned."));

    /// <summary>Fails closed on the catalog and audits the refusal: an unknown alias or unreadable policy executes nothing.</summary>
    private ConnectionEntry RequireAudited(string? alias, string? sql)
    {
        try
        {
            return catalog.Require(alias);
        }
        catch (InvestigatorException e)
        {
            TryAudit("sql.rejected", alias, Clip(sql), [e.Error.Code + ": " + e.Error.Message]);
            throw;
        }
    }

    /// <summary>Checks the investigation budget and records the query as running, in one write, so the budget cannot be raced.</summary>
    private (QueryRecord Record, InvestigatorError? Refusal) Reserve(string alias, string sql, string? investigationId)
        => history.Update(document =>
        {
            if (investigationId is not null)
            {
                FindInvestigation(document, investigationId);
                var used = document.Queries.Count(q => q.InvestigationId == investigationId);
                if (used >= settings.MaxQueriesPerInvestigation)
                {
                    history.Audit(document, "budget.investigation", alias, investigationId: investigationId,
                        detail: $"refused: {used} of {settings.MaxQueriesPerInvestigation} queries already run");
                    return (null!, new InvestigatorError(
                        "budget.investigation",
                        $"Investigation {investigationId} has used its budget of {settings.MaxQueriesPerInvestigation} queries; this one was refused and not run.",
                        "Record what is known with investigation_record, then start a new investigation for further queries."));
                }
            }

            var record = new QueryRecord { QueryId = InvestigationHistory.NewId("q"), InvestigationId = investigationId, Alias = alias, Sql = sql, StartedAt = history.Now() };
            document.Queries.Add(record);
            history.Audit(document, "sql.started", alias, record.QueryId, investigationId);
            return (record, (InvestigatorError?)null);
        });

    private async Task<QueryEvidence> RunAsync(ConnectionEntry entry, QueryRecord record, CancellationToken cancel)
    {
        var columns = new List<string>();
        var rows = new List<IReadOnlyList<string?>>();
        string? truncatedBy = null;

        // a backstop beyond the server-side CommandTimeout, for a connection or read that stalls
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.SqlTimeoutSeconds + settings.ConnectTimeoutSeconds + 5));
        try
        {
            await using var connection = await connections.OpenAsync(entry, deadline.Token);
            await using (var session = new SqlCommand(
                $"SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET LOCK_TIMEOUT {settings.SqlTimeoutSeconds * 1000};",
                connection) { CommandTimeout = settings.SqlTimeoutSeconds })
            {
                await session.ExecuteNonQueryAsync(deadline.Token);
            }

            // the transaction is always rolled back: even a statement that could write would leave nothing behind
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, deadline.Token);
            await using (var command = new SqlCommand(record.Sql, connection, transaction) { CommandTimeout = settings.SqlTimeoutSeconds, CommandType = CommandType.Text })
            {
                var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, deadline.Token);
                try
                {
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        columns.Add(reader.GetName(i) is { Length: > 0 } n ? n : $"column{i + 1}");
                    }

                    var bytes = Encoding.UTF8.GetByteCount(string.Join(",", columns)) + 64;
                    while (await reader.ReadAsync(deadline.Token))
                    {
                        if (rows.Count >= settings.MaxQueryRows)
                        {
                            truncatedBy = "rows";
                            break;
                        }

                        var row = new string?[reader.FieldCount];
                        var size = 2;
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = Format(reader.GetValue(i));
                            size += (row[i] is null ? 4 : Encoding.UTF8.GetByteCount(row[i]!) + 2) + 1;
                        }

                        if (bytes + size > settings.MaxResultBytes)
                        {
                            truncatedBy = "bytes";
                            break;
                        }

                        bytes += size;
                        rows.Add(row);
                    }

                    if (truncatedBy is not null)
                    {
                        command.Cancel(); // stop the server producing rows nobody will read
                    }
                }
                finally
                {
                    await reader.DisposeAsync();
                }
            }

            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            var error = SqlFailures.Translate(e, entry.Alias, cancel.IsCancellationRequested);
            Complete(record, "failed", 0, false, null, error.Code);
            log.LogWarning("query {QueryId} on {Alias} failed: {Code}", record.QueryId, entry.Alias, error.Code);
            throw new InvestigatorException(error with { Message = Redaction.Apply(error.Message) });
        }

        var completedAt = Complete(record, "succeeded", rows.Count, truncatedBy is not null, truncatedBy, null);
        return new QueryEvidence
        {
            QueryId = record.QueryId,
            Alias = entry.Alias,
            Sql = record.Sql,
            InvestigationId = record.InvestigationId,
            StartedAt = record.StartedAt,
            CompletedAt = completedAt,
            RowCount = rows.Count,
            Truncated = truncatedBy is not null,
            TruncatedBy = truncatedBy,
            Columns = columns,
            Rows = rows,
            RowLimit = settings.MaxQueryRows,
            ByteLimit = settings.MaxResultBytes,
        };
    }

    private string Complete(QueryRecord record, string status, int rowCount, bool truncated, string? truncatedBy, string? errorCode)
        => history.Update(document =>
        {
            var stored = document.Queries.FirstOrDefault(q => q.QueryId == record.QueryId);
            var at = history.Now();
            if (stored is not null)
            {
                stored.Status = status;
                stored.CompletedAt = at;
                stored.RowCount = rowCount;
                stored.Truncated = truncated;
                stored.TruncatedBy = truncatedBy;
                stored.ErrorCode = errorCode;
            }

            if (truncatedBy is not null)
            {
                history.Audit(document, "budget." + truncatedBy, record.Alias, record.QueryId, record.InvestigationId,
                    truncatedBy == "rows" ? $"truncated at {settings.MaxQueryRows} rows" : $"truncated at {settings.MaxResultBytes} bytes");
            }

            if (errorCode is "budget.timeout" or "cancelled")
            {
                history.Audit(document, errorCode, record.Alias, record.QueryId, record.InvestigationId,
                    errorCode == "cancelled" ? "cancelled by the caller" : $"cancelled after {settings.SqlTimeoutSeconds}s");
            }

            history.Audit(document, "sql." + status, record.Alias, record.QueryId, record.InvestigationId, errorCode);
            return at;
        });

    private void TryAudit(string name, string? alias, string? detail, IReadOnlyList<string> reasons)
    {
        try
        {
            history.Audit(name, alias, detail: detail, reasons: reasons);
        }
        catch (InvestigatorException e)
        {
            // the refusal stands even if it cannot be recorded; say so on stderr rather than fail open
            log.LogError("audit of {Event} could not be written: {Code}", name, e.Error.Code);
        }
    }

    private static string? Format(object value) => value switch
    {
        DBNull => null,
        string s => s,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        byte[] b => "0x" + Convert.ToHexString(b.Length > 512 ? b[..512] : b) + (b.Length > 512 ? "…" : string.Empty),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Clip(string? sql)
    {
        var text = sql ?? string.Empty;
        return text.Length > 2000 ? text[..2000] + "…" : text;
    }
}

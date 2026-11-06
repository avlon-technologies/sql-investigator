using System.Security.Cryptography;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Queries;

/// <summary>One recorded step of an investigation (InvestigationEntry). Holds reasoning and query ids, never result rows.</summary>
internal sealed record InvestigationEntry
{
    public required string InvestigationId { get; init; }
    public string Hypothesis { get; init; } = string.Empty;
    public IReadOnlyList<string> QueryIds { get; init; } = [];
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string Finding { get; init; } = string.Empty;
    public string Uncertainty { get; init; } = string.Empty;
    public required string RecordedAt { get; init; }
}

internal sealed record InvestigationRecord
{
    public required string InvestigationId { get; init; }
    public required string Question { get; init; }
    public required string StartedAt { get; init; }
    public List<InvestigationEntry> Entries { get; init; } = [];
}

/// <summary>The provenance of one sql_execute request: id, exact SQL, alias, timestamps and outcome. No rows.</summary>
internal sealed record QueryRecord
{
    public required string QueryId { get; init; }
    public string? InvestigationId { get; init; }
    public required string Alias { get; init; }
    public required string Sql { get; init; }
    public required string StartedAt { get; init; }
    public string? CompletedAt { get; set; }
    public int RowCount { get; set; }
    public bool Truncated { get; set; }
    public string? TruncatedBy { get; set; }

    /// <summary><c>running</c>, <c>succeeded</c> or <c>failed</c>.</summary>
    public string Status { get; set; } = "running";
    public string? ErrorCode { get; set; }
}

internal sealed class HistoryDocument
{
    public int Version { get; set; } = 1;
    public List<InvestigationRecord> Investigations { get; set; } = [];
    public List<QueryRecord> Queries { get; set; } = [];
    public List<AuditEvent> Audit { get; set; } = [];
}

/// <summary>
/// The InvestigationHistory store. QueryGateway is its only writer: every write goes through <see cref="Update{T}"/>,
/// which also applies retention. Raw result rows are never stored (binding: no-raw-results-default).
/// </summary>
internal sealed class InvestigationHistory(AppPaths paths, TimeProvider clock, Settings settings)
{
    private readonly JsonFileStore<HistoryDocument> store = new(paths.HistoryFile);

    public string Now() => clock.GetUtcNow().ToString("O");

    public static string NewId(string prefix) => $"{prefix}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";

    public HistoryDocument Read() => store.Read();

    public T Update<T>(Func<HistoryDocument, T> change) => store.Update(document =>
    {
        var result = change(document);
        Prune(document);
        return result;
    });

    public void Audit(HistoryDocument document, string name, string? alias = null, string? queryId = null, string? investigationId = null,
        string? detail = null, IReadOnlyList<string>? reasons = null)
    {
        document.Audit.Add(new AuditEvent
        {
            At = Now(),
            Event = name,
            Alias = alias,
            QueryId = queryId,
            InvestigationId = investigationId,
            Detail = detail is null ? null : Redaction.Apply(detail),
            Reasons = reasons?.Select(Redaction.Apply).ToList(),
        });
    }

    public void Audit(string name, string? alias = null, string? queryId = null, string? investigationId = null, string? detail = null,
        IReadOnlyList<string>? reasons = null)
        => Update(document =>
        {
            Audit(document, name, alias, queryId, investigationId, detail, reasons);
            return 0;
        });

    private void Prune(HistoryDocument document)
    {
        var cutoff = clock.GetUtcNow().AddDays(-settings.HistoryRetentionDays);
        bool Expired(string at) => DateTimeOffset.TryParse(at, out var t) && t < cutoff;

        var expired = document.Investigations.Where(i => Expired(i.StartedAt)).Select(i => i.InvestigationId).ToHashSet(StringComparer.Ordinal);
        document.Investigations.RemoveAll(i => expired.Contains(i.InvestigationId));
        document.Queries.RemoveAll(q => Expired(q.StartedAt) && (q.InvestigationId is null || expired.Contains(q.InvestigationId)));
        document.Audit.RemoveAll(a => Expired(a.At));
        if (document.Audit.Count > settings.MaxAuditEvents)
        {
            document.Audit.RemoveRange(0, document.Audit.Count - settings.MaxAuditEvents);
        }
    }
}

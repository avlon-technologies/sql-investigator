using System.Globalization;

namespace SqlInvestigator.Infrastructure;

/// <summary>
/// Budgets and limits. The names and defaults are the recommended controls in continuum/development-package.json;
/// they are configuration, not architecture (QueryBudgets says only that every query is held to them).
/// </summary>
internal sealed record Settings
{
    public int SqlTimeoutSeconds { get; init; } = 15;
    public int MaxQueryRows { get; init; } = 500;
    public int MaxResultBytes { get; init; } = 1_048_576;
    public int MaxConcurrentQueries { get; init; } = 2;
    public int MaxQueriesPerInvestigation { get; init; } = 50;
    public int ConnectTimeoutSeconds { get; init; } = 10;
    public int MaxSqlLength { get; init; } = 20_000;
    public int SchemaMaxAgeSeconds { get; init; } = 900;
    public int HistoryRetentionDays { get; init; } = 90;
    public int MaxAuditEvents { get; init; } = 10_000;

    public static Settings FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var d = new Settings();
        return new Settings
        {
            SqlTimeoutSeconds = Positive(read, "SQL_TIMEOUT_SECONDS", d.SqlTimeoutSeconds),
            MaxQueryRows = Positive(read, "MAX_QUERY_ROWS", d.MaxQueryRows),
            MaxResultBytes = Positive(read, "MAX_RESULT_BYTES", d.MaxResultBytes),
            MaxConcurrentQueries = Positive(read, "MAX_CONCURRENT_QUERIES", d.MaxConcurrentQueries),
            MaxQueriesPerInvestigation = Positive(read, "MAX_QUERIES_PER_INVESTIGATION", d.MaxQueriesPerInvestigation),
            ConnectTimeoutSeconds = Positive(read, "CONNECT_TIMEOUT_SECONDS", d.ConnectTimeoutSeconds),
            MaxSqlLength = Positive(read, "MAX_SQL_LENGTH", d.MaxSqlLength),
            SchemaMaxAgeSeconds = Positive(read, "SCHEMA_MAX_AGE_SECONDS", d.SchemaMaxAgeSeconds),
            HistoryRetentionDays = Positive(read, "HISTORY_RETENTION_DAYS", d.HistoryRetentionDays),
            MaxAuditEvents = Positive(read, "MAX_AUDIT_EVENTS", d.MaxAuditEvents),
        };
    }

    // A malformed or non-positive value falls back to the default rather than to "unlimited": a budget never fails open.
    private static int Positive(Func<string, string?> read, string name, int fallback)
    {
        var raw = read(name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
    }
}

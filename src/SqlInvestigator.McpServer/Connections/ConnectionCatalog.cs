using System.Text.RegularExpressions;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Connections;

/// <summary>
/// One approved object of an alias's scope: a table or view, and optionally the only columns that may be read.
/// Written <c>dbo.Orders</c> (every column) or <c>dbo.Orders(OrderId,Status)</c> (only those columns).
/// </summary>
internal sealed partial record ScopeEntry
{
    public required string Schema { get; init; }
    public required string Object { get; init; }

    /// <summary>The approved columns; null means every column of the object.</summary>
    public IReadOnlyList<string>? Columns { get; init; }

    public string QualifiedName => $"{Schema}.{Object}";

    public bool Matches(string schema, string name)
        => string.Equals(Schema, schema, StringComparison.OrdinalIgnoreCase) && string.Equals(Object, name, StringComparison.OrdinalIgnoreCase);

    public bool AllowsColumn(string column)
        => Columns is null || Columns.Contains(column, StringComparer.OrdinalIgnoreCase);

    public override string ToString() => Columns is null ? QualifiedName : $"{QualifiedName}({string.Join(",", Columns)})";

    public static ScopeEntry Parse(string text)
    {
        var m = ScopePattern().Match(text.Trim());
        if (!m.Success)
        {
            throw InvestigatorException.Input(
                $"Scope \"{text}\" is not schema.object or schema.object(column,column).",
                "Name each approved table or view with its schema, for example dbo.Orders or dbo.Orders(OrderId,Status).");
        }

        var columns = m.Groups["columns"].Success
            ? m.Groups["columns"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : null;
        if (columns is { Length: 0 })
        {
            throw InvestigatorException.Input($"Scope \"{text}\" lists no columns.", "List at least one column, or omit the parentheses to approve every column.");
        }

        return new ScopeEntry { Schema = m.Groups["schema"].Value, Object = m.Groups["object"].Value, Columns = columns };
    }

    [GeneratedRegex(@"^(?<schema>[A-Za-z_][A-Za-z0-9_]*)\.(?<object>[A-Za-z_][A-Za-z0-9_]*)(\((?<columns>[A-Za-z0-9_, ]*)\))?$")]
    private static partial Regex ScopePattern();
}

/// <summary>Non-secret metadata of one alias. Never holds a credential.</summary>
internal sealed record ConnectionEntry
{
    public required string Alias { get; init; }
    public required string Server { get; init; }
    public required string Database { get; init; }

    /// <summary><c>sql</c> (a SQL login whose secret is in the vault) or <c>windows</c> (integrated security).</summary>
    public required string Authentication { get; init; }

    /// <summary>The SQL login name. Not a secret; the password is in the vault.</summary>
    public string? User { get; init; }

    /// <summary>The environment the data owner approved: <c>fixture</c> or <c>development</c> (ApprovedEnvironmentsOnly).</summary>
    public required string Environment { get; init; }

    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public required IReadOnlyList<ScopeEntry> Scope { get; init; }
    public required string ProvisionedAt { get; init; }

    public ScopeEntry? FindScope(string schema, string name) => Scope.FirstOrDefault(s => s.Matches(schema, name));
}

internal sealed class CatalogDocument
{
    public int Version { get; set; } = 1;
    public List<ConnectionEntry> Connections { get; set; } = [];
    public List<AuditEvent> Audit { get; set; } = [];
}

/// <summary>
/// Reads the ConnectionCatalog. QueryGateway, KnowledgeService and ConnectionRegistry read it; only
/// ConnectionRegistry writes it. Read fresh on every request, so a change at the console applies at once.
/// </summary>
internal sealed partial class ConnectionCatalogReader(AppPaths paths)
{
    /// <summary>Environments the data owner has approved. Production is refused until that approval exists.</summary>
    public static readonly IReadOnlySet<string> ApprovedEnvironments = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fixture", "development" };

    public JsonFileStore<CatalogDocument> Store { get; } = new(paths.CatalogFile);

    public IReadOnlyList<ConnectionEntry> All() => Store.Read().Connections;

    /// <summary>
    /// The alias's entry, usable for a query: it exists, its environment is approved and it has a scope.
    /// Anything else fails closed with the reason.
    /// </summary>
    public ConnectionEntry Require(string? alias)
    {
        var name = NormalizeAlias(alias);
        CatalogDocument document;
        try
        {
            document = Store.Read();
        }
        catch (InvestigatorException e) when (e.Error.Code == "store.unreadable")
        {
            throw new InvestigatorException(new InvestigatorError(
                "policy.unavailable",
                "The connection catalog, which holds each alias's approved scope, cannot be read, so no policy can be applied.",
                "Nothing was executed. Repair or re-provision the catalog at the local console (SqlInvestigator provision)."));
        }

        var entry = document.Connections.FirstOrDefault(c => string.Equals(c.Alias, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvestigatorException(new InvestigatorError(
                "alias.unknown",
                $"No connection alias \"{name}\" is provisioned.",
                "Call connection_list for the provisioned aliases. A new alias is provisioned by the developer at the local console, never through MCP."));

        if (!ApprovedEnvironments.Contains(entry.Environment ?? string.Empty))
        {
            throw new InvestigatorException(new InvestigatorError(
                "environment.unapproved",
                $"Alias \"{name}\" is recorded for environment \"{entry.Environment}\", which the data owner has not approved.",
                "Only fixture and development environments are approved. Production data needs the data owner's approval for its classifications and environment first."));
        }

        if (entry.Scope is not { Count: > 0 } || string.IsNullOrWhiteSpace(entry.Server) || string.IsNullOrWhiteSpace(entry.Database))
        {
            throw new InvestigatorException(new InvestigatorError(
                "policy.unavailable",
                $"Alias \"{name}\" has no approved scope, so no object may be read through it.",
                "Nothing was executed. Re-provision the alias at the local console with at least one --scope."));
        }

        return entry;
    }

    public static string NormalizeAlias(string? alias)
    {
        var name = alias?.Trim() ?? string.Empty;
        if (!AliasPattern().IsMatch(name))
        {
            throw InvestigatorException.Input(
                $"\"{alias}\" is not a valid alias.",
                "An alias is 1-64 characters: a letter, then letters, digits, '-' or '_'. Call connection_list for the provisioned aliases.");
        }

        return name.ToLowerInvariant();
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex AliasPattern();
}

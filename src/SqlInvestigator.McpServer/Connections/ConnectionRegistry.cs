using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Connections;

/// <summary>Non-secret metadata returned by connection_list (ConnectionInfo, plus the approved environment and scope).</summary>
internal sealed record ConnectionInfo(
    string Alias,
    string Server,
    string Database,
    string ProvisionedAt,
    string Authentication,
    string Environment,
    IReadOnlyList<string> Scope);

/// <summary>
/// ConnectionRegistry: handles ProvisionConnection (from the local console), RemoveConnection and ListConnections.
/// The only writer of the ConnectionCatalog. It never accepts, returns or writes a credential.
/// </summary>
internal sealed class ConnectionRegistry(ConnectionCatalogReader catalog, TimeProvider clock, Settings settings)
{
    /// <summary>ListConnections: aliases and non-secret metadata only.</summary>
    public IReadOnlyList<ConnectionInfo> List(string? alias = null)
    {
        var filter = string.IsNullOrWhiteSpace(alias) ? null : ConnectionCatalogReader.NormalizeAlias(alias);
        return catalog.All()
            .Where(c => filter is null || string.Equals(c.Alias, filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Alias, StringComparer.Ordinal)
            .Select(c => new ConnectionInfo(c.Alias, c.Server, c.Database, c.ProvisionedAt, c.Authentication, c.Environment, c.Scope.Select(s => s.ToString()).ToList()))
            .ToList();
    }

    /// <summary>
    /// ProvisionConnection's catalog half, called by the local console after it has placed the secret in the vault.
    /// Refuses an environment the data owner has not approved.
    /// </summary>
    public void RecordProvisioned(ConnectionEntry entry, bool replace)
    {
        if (!ConnectionCatalogReader.ApprovedEnvironments.Contains(entry.Environment))
        {
            throw new InvestigatorException(new InvestigatorError(
                "environment.unapproved",
                $"Environment \"{entry.Environment}\" is not approved for SQL Investigator.",
                "Only fixture and development databases may be provisioned. Production data needs the data owner's approval for its classifications and environment first."));
        }

        catalog.Store.Update(document =>
        {
            var existing = document.Connections.FindIndex(c => string.Equals(c.Alias, entry.Alias, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && !replace)
            {
                throw new InvestigatorException(new InvestigatorError(
                    "alias.exists",
                    $"Alias \"{entry.Alias}\" is already provisioned.",
                    "Pass --replace to re-provision it, or choose another alias."));
            }

            if (existing >= 0)
            {
                document.Connections[existing] = entry;
            }
            else
            {
                document.Connections.Add(entry);
            }

            Audit(document, existing >= 0 ? "connection.reprovisioned" : "connection.provisioned", entry.Alias,
                $"{entry.Server}/{entry.Database} as {entry.Authentication}{(entry.User is null ? string.Empty : " " + entry.User)}; environment {entry.Environment}; scope {string.Join(", ", entry.Scope)}");
            return 0;
        });
    }

    /// <summary>
    /// RemoveConnection: the alias leaves the catalog, so no tool can reach it again. Its DPAPI secret is not a
    /// catalog entry and no service writes the vault; the console's <c>remove</c> verb deletes it.
    /// </summary>
    public bool Remove(string? alias)
    {
        var name = ConnectionCatalogReader.NormalizeAlias(alias);
        return catalog.Store.Update(document =>
        {
            var removed = document.Connections.RemoveAll(c => string.Equals(c.Alias, name, StringComparison.OrdinalIgnoreCase)) > 0;
            Audit(document, removed ? "connection.removed" : "connection.remove-unknown", name, null);
            return removed;
        });
    }

    private void Audit(CatalogDocument document, string name, string alias, string? detail)
    {
        document.Audit.Add(new AuditEvent { At = clock.GetUtcNow().ToString("O"), Event = name, Alias = alias, Detail = detail });
        if (document.Audit.Count > settings.MaxAuditEvents)
        {
            document.Audit.RemoveRange(0, document.Audit.Count - settings.MaxAuditEvents);
        }
    }
}

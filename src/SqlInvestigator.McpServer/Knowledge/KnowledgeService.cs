using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlInvestigator.Connections;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Knowledge;

/// <summary>The result of schema_inspect: the snapshot, whether it was reused or (re)captured, and any drift.</summary>
internal sealed record SchemaInspection(SchemaSnapshot Snapshot, string Status, SchemaDrift? Drift, string VerifiedAt);

internal sealed record KnowledgeResults(IReadOnlyList<SchemaSnapshot> Snapshots, IReadOnlyList<Relationship> Relationships);

/// <summary>
/// KnowledgeService: handles InspectSchema, SearchKnowledge, ProposeRelationship and ReviewRelationship. Maintains
/// schema snapshots and proposed cross-database relationships; the only writer of KnowledgeStore. It reads metadata
/// only (sys.objects, sys.columns, sys.types) and never stores row values.
/// </summary>
internal sealed class KnowledgeService(
    AppPaths paths,
    ConnectionCatalogReader catalog,
    SqlConnectionFactory connections,
    TimeProvider clock,
    Settings settings,
    ILogger<KnowledgeService> log)
{
    private const string CatalogQuery = """
        SELECT o.type_desc, c.name, t.name, c.max_length, c.precision, c.scale, c.is_nullable
        FROM sys.objects AS o
        JOIN sys.schemas AS s ON s.schema_id = o.schema_id
        JOIN sys.columns AS c ON c.object_id = o.object_id
        JOIN sys.types AS t ON t.user_type_id = c.user_type_id
        WHERE s.name = @schema AND o.name = @object AND o.type IN ('U', 'V')
        ORDER BY c.column_id
        """;

    private readonly JsonFileStore<KnowledgeDocument> store = new(paths.KnowledgeFile);

    /// <summary>InspectSchema: the current snapshot, captured or refreshed from the catalog when absent, stale or drifted.</summary>
    public async Task<SchemaInspection> InspectAsync(string? alias, string? scope, bool refresh, CancellationToken cancel)
    {
        var entry = catalog.Require(alias);
        var wanted = entry.Scope;
        if (!string.IsNullOrWhiteSpace(scope))
        {
            var filter = ScopeEntry.Parse(scope);
            wanted = entry.Scope.Where(s => s.Matches(filter.Schema, filter.Object)).ToList();
            if (wanted.Count == 0)
            {
                throw new InvestigatorException(new InvestigatorError(
                    "scope.unapproved",
                    $"{filter.QualifiedName} is not in the approved scope of alias \"{entry.Alias}\".",
                    "Inspect the alias without a scope to see every approved object."));
            }
        }

        var now = clock.GetUtcNow();
        SchemaSnapshot current;
        try
        {
            current = await CaptureAsync(entry, wanted, now, cancel);
        }
        catch (Exception e)
        {
            var error = SqlFailures.Translate(e, entry.Alias, cancel.IsCancellationRequested);
            log.LogWarning("schema capture for {Alias} failed: {Code}", entry.Alias, error.Code);

            // never fall back to a stored snapshot as if it were current: report the failure
            throw new InvestigatorException(error with { Message = Redaction.Apply(error.Message) });
        }

        var covers = string.IsNullOrWhiteSpace(scope) ? "*" : wanted[0].QualifiedName.ToLowerInvariant();
        current = current with { Covers = covers };
        return store.Update(document =>
        {
            var index = document.Snapshots.FindIndex(s => s.Alias == entry.Alias && s.Covers == covers);
            var previous = index >= 0 ? document.Snapshots[index] : null;
            var fresh = previous is not null && DateTimeOffset.TryParse(previous.CapturedAt, out var at) && now - at < TimeSpan.FromSeconds(settings.SchemaMaxAgeSeconds);
            var unchanged = previous is not null && previous.Fingerprint == current.Fingerprint && previous.Source == current.Source;

            if (unchanged && fresh && !refresh)
            {
                return new SchemaInspection(previous!, "current", null, now.ToString("O"));
            }

            SchemaDrift? drift = null;
            var status = previous is null ? "captured" : refresh ? "refreshed" : "stale-refreshed";
            if (previous is not null && !unchanged)
            {
                var before = previous.Objects.ToHashSet(StringComparer.Ordinal);
                var after = current.Objects.ToHashSet(StringComparer.Ordinal);
                drift = new SchemaDrift(
                    after.Except(before).Concat(previous.Missing.Except(current.Missing).Select(m => "found: " + m)).ToList(),
                    before.Except(after).Concat(current.Missing.Except(previous.Missing).Select(m => "missing: " + m)).ToList(),
                    previous.CapturedAt);
                status = "drifted";
            }

            if (index >= 0)
            {
                document.Snapshots[index] = current;
            }
            else
            {
                document.Snapshots.Add(current);
            }

            Audit(document, "schema." + status, entry.Alias, $"{current.Objects.Count} object(s); {current.Missing.Count} missing");
            return new SchemaInspection(current, status, drift, now.ToString("O"));
        });
    }

    /// <summary>SearchKnowledge: snapshots and relationships whose text contains the query, optionally for one alias.</summary>
    public KnowledgeResults Search(string? text, string? alias)
    {
        var query = text?.Trim() ?? string.Empty;
        var aliasFilter = string.IsNullOrWhiteSpace(alias) ? null : ConnectionCatalogReader.NormalizeAlias(alias);
        var document = store.Read();
        bool Hit(string? value) => query.Length == 0 || (value?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

        var snapshots = document.Snapshots
            .Where(s => aliasFilter is null || s.Alias == aliasFilter)
            .Where(s => Hit(s.Alias) || s.Objects.Any(Hit) || s.Missing.Any(Hit))
            .ToList();
        var relationships = document.Relationships
            .Where(r => aliasFilter is null || MentionsAlias(r.Source, aliasFilter) || MentionsAlias(r.Target, aliasFilter))
            .Where(r => Hit(r.Source) || Hit(r.Target) || Hit(r.Evidence) || Hit(r.Provenance) || Hit(r.State) || Hit(r.Review?.Rationale))
            .ToList();
        return new KnowledgeResults(snapshots, relationships);
    }

    /// <summary>ProposeRelationship: the relationship enters as proposed, with its evidence and provenance.</summary>
    public Relationship Propose(string? source, string? target, string? evidence, string? provenance)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            throw InvestigatorException.Input("A relationship needs a source and a target.", "Name both ends as alias.schema-or-table.column, for example alpha.Orders.OrderId.");
        }

        if (string.IsNullOrWhiteSpace(evidence))
        {
            throw InvestigatorException.Input("A relationship needs evidence.", "Cite the query ids and observations that suggest it.");
        }

        return store.Update(document =>
        {
            var relationship = new Relationship
            {
                RelationshipId = $"rel-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}",
                Source = Redaction.Apply(source.Trim()),
                Target = Redaction.Apply(target.Trim()),
                Evidence = Redaction.Apply(evidence.Trim()),
                Provenance = Redaction.Apply(string.IsNullOrWhiteSpace(provenance) ? "inferred by Claude Code (knowledge_propose)" : provenance.Trim()),
                State = "proposed",
                ProposedAt = clock.GetUtcNow().ToString("O"),
            };
            document.Relationships.Add(relationship);
            Audit(document, "relationship.proposed", null, $"{relationship.RelationshipId}: {relationship.Source} -> {relationship.Target}");
            return relationship;
        });
    }

    /// <summary>
    /// ReviewRelationship: the developer's explicit verdict on a proposed relationship. The evidence is kept either way;
    /// a reviewed relationship is closed, so a rejection can never later be promoted.
    /// </summary>
    public Relationship Review(string? relationshipId, string? verdict, string? rationale)
    {
        var v = verdict?.Trim().ToLowerInvariant();
        if (v is not ("accepted" or "rejected"))
        {
            throw InvestigatorException.Input($"\"{verdict}\" is not a verdict.", "Pass verdict \"accepted\" or \"rejected\".");
        }

        if (string.IsNullOrWhiteSpace(rationale))
        {
            throw InvestigatorException.Input("A review needs the developer's rationale.", "State why the developer accepts or rejects the relationship.");
        }

        return store.Update(document =>
        {
            var relationship = document.Relationships.FirstOrDefault(r => r.RelationshipId == relationshipId?.Trim())
                ?? throw new InvestigatorException(new InvestigatorError(
                    "relationship.unknown",
                    $"No relationship has id \"{relationshipId}\".",
                    "Call knowledge_search for the recorded relationships."));
            if (relationship.State != "proposed")
            {
                throw new InvestigatorException(new InvestigatorError(
                    "relationship.closed",
                    $"Relationship {relationship.RelationshipId} was already {relationship.State}; a review is final and is not changed.",
                    "Propose a new relationship with new evidence if the question is open again."));
            }

            relationship.State = v;
            relationship.Review = new RelationshipReviewRecord(v, Redaction.Apply(rationale.Trim()), clock.GetUtcNow().ToString("O"));
            Audit(document, "relationship." + v, null, relationship.RelationshipId);
            return relationship;
        });
    }

    private async Task<SchemaSnapshot> CaptureAsync(ConnectionEntry entry, IReadOnlyList<ScopeEntry> scope, DateTimeOffset now, CancellationToken cancel)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.SqlTimeoutSeconds + settings.ConnectTimeoutSeconds + 5));
        await using var connection = await connections.OpenAsync(entry, deadline.Token);

        var objects = new List<SchemaObject>();
        var missing = new List<string>();
        foreach (var item in scope)
        {
            await using var command = new SqlCommand(CatalogQuery, connection) { CommandTimeout = settings.SqlTimeoutSeconds };
            command.Parameters.Add(new SqlParameter("@schema", System.Data.SqlDbType.NVarChar, 128) { Value = item.Schema });
            command.Parameters.Add(new SqlParameter("@object", System.Data.SqlDbType.NVarChar, 128) { Value = item.Object });
            var kind = string.Empty;
            var columns = new List<SchemaColumn>();
            await using (var reader = await command.ExecuteReaderAsync(deadline.Token))
            {
                while (await reader.ReadAsync(deadline.Token))
                {
                    kind = reader.GetString(0) == "VIEW" ? "view" : "table";
                    columns.Add(new SchemaColumn(reader.GetString(1), TypeName(reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5)), reader.GetBoolean(6)));
                }
            }

            if (columns.Count == 0)
            {
                missing.Add(item.QualifiedName);
                continue;
            }

            // only approved columns are reported; an approved column the catalog lacks is missing, never invented
            var approved = item.Columns is null ? columns : columns.Where(c => item.AllowsColumn(c.Name)).ToList();
            foreach (var name in item.Columns ?? [])
            {
                if (!columns.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    missing.Add($"{item.QualifiedName}.{name}");
                }
            }

            objects.Add(new SchemaObject(item.QualifiedName, kind, approved));
        }

        var described = objects.Select(o => o.Describe()).ToList();
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", described.Concat(missing.Select(m => "!" + m))))));
        return new SchemaSnapshot
        {
            Alias = entry.Alias,
            Source = $"sqlserver://{entry.Server}/{entry.Database} (sys.objects, sys.columns, sys.types) as alias {entry.Alias}",
            CapturedAt = now.ToString("O"),
            Objects = described,
            Detail = objects,
            Missing = missing,
            Fingerprint = fingerprint,
        };
    }

    private static string TypeName(string type, short maxLength, byte precision, byte scale) => type switch
    {
        "varchar" or "char" or "varbinary" or "binary" => $"{type}({(maxLength < 0 ? "max" : maxLength.ToString(System.Globalization.CultureInfo.InvariantCulture))})",
        "nvarchar" or "nchar" => $"{type}({(maxLength < 0 ? "max" : (maxLength / 2).ToString(System.Globalization.CultureInfo.InvariantCulture))})",
        "decimal" or "numeric" => $"{type}({precision},{scale})",
        _ => type,
    };

    private static bool MentionsAlias(string end, string alias)
        => end.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase) || string.Equals(end, alias, StringComparison.OrdinalIgnoreCase);

    private void Audit(KnowledgeDocument document, string name, string? alias, string? detail)
    {
        document.Audit.Add(new AuditEvent { At = clock.GetUtcNow().ToString("O"), Event = name, Alias = alias, Detail = detail });
        if (document.Audit.Count > settings.MaxAuditEvents)
        {
            document.Audit.RemoveRange(0, document.Audit.Count - settings.MaxAuditEvents);
        }
    }
}

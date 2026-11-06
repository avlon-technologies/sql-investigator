using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Knowledge;

/// <summary>One column of an approved object as the catalog reported it.</summary>
internal sealed record SchemaColumn(string Name, string Type, bool Nullable);

/// <summary>One approved object as the catalog reported it.</summary>
internal sealed record SchemaObject(string Name, string Kind, IReadOnlyList<SchemaColumn> Columns)
{
    /// <summary>The compact text form used by SchemaSnapshot.objects: <c>dbo.Orders(Id int, Status nvarchar)</c>.</summary>
    public string Describe() => $"{Name}({string.Join(", ", Columns.Select(c => $"{c.Name} {c.Type}{(c.Nullable ? " null" : string.Empty)}"))})";
}

/// <summary>What changed between the previous snapshot and a new capture.</summary>
internal sealed record SchemaDrift(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, string PreviousCapturedAt);

/// <summary>SchemaSnapshot: an alias's approved objects with the source they were read from and when.</summary>
internal sealed record SchemaSnapshot
{
    public required string Alias { get; init; }
    public required string Source { get; init; }
    public required string CapturedAt { get; init; }
    public required IReadOnlyList<string> Objects { get; init; }
    public required IReadOnlyList<SchemaObject> Detail { get; init; }

    /// <summary>Approved objects or columns the catalog did not report: in the scope, but not (or no longer) in the database.</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>The scope filter the snapshot covers: <c>*</c> for every approved object, else one schema.object.</summary>
    public string Covers { get; init; } = "*";
}

/// <summary>A cross-database relationship: proposed by the agent, accepted or rejected only by the developer's review.</summary>
internal sealed record Relationship
{
    public required string RelationshipId { get; init; }
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required string Evidence { get; init; }
    public required string Provenance { get; init; }

    /// <summary><c>proposed</c>, <c>accepted</c> or <c>rejected</c> (ReviewState).</summary>
    public required string State { get; set; }
    public required string ProposedAt { get; init; }
    public RelationshipReviewRecord? Review { get; set; }
}

internal sealed record RelationshipReviewRecord(string Verdict, string Rationale, string ReviewedAt);

internal sealed class KnowledgeDocument
{
    public int Version { get; set; } = 1;
    public List<SchemaSnapshot> Snapshots { get; set; } = [];
    public List<Relationship> Relationships { get; set; } = [];
    public List<AuditEvent> Audit { get; set; } = [];
}

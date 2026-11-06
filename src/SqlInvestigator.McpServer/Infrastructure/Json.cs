using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlInvestigator.Infrastructure;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly JsonSerializerOptions StoreOptions = new(Options) { WriteIndented = true };
}

/// <summary>An audit event, kept in the store of the service that performed the act (atomic-json-files: auditable).</summary>
internal sealed record AuditEvent
{
    public required string At { get; init; }
    public required string Event { get; init; }
    public string? Alias { get; init; }
    public string? QueryId { get; init; }
    public string? InvestigationId { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<string>? Reasons { get; init; }
}

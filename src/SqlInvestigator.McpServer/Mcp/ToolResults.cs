using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Mcp;

/// <summary>
/// Turns a service result or failure into an MCP tool result. Every result passes through <see cref="Redaction"/>
/// (SecretRedaction, enforcement = output) as the last step before it leaves the process.
/// </summary>
internal static class ToolResults
{
    public static CallToolResult Ok(object value) => Result(value, isError: false);

    public static CallToolResult Error(InvestigatorError error)
        => Result(new { error = new { code = error.Code, message = error.Message, action = error.Action, reasons = error.Reasons } }, isError: true);

    /// <summary>
    /// Redaction is applied to every string value and property name before the JSON is written, never to the JSON
    /// text: a masked value inside serialized text could swallow an escape character and break the document
    /// (cold-start safety run 2026-11-06: an alias like <c>Server=x;Password=y</c> turned an actionable error into
    /// the SDK's generic one). A number or boolean that equals a registered secret becomes the mask.
    /// </summary>
    private static CallToolResult Result(object value, bool isError)
    {
        var node = Redact(JsonSerializer.SerializeToNode(value, Json.Options));
        var text = node?.ToJsonString(Json.Options) ?? "null";
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonDocument.Parse(text).RootElement.Clone(),
            IsError = isError,
        };
    }

    private static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, child) in obj.ToList())
                {
                    obj.Remove(key);
                    var name = Redaction.Apply(key);
                    copy[copy.ContainsKey(name) ? $"{name}#{copy.Count}" : name] = Redact(child);
                }

                return copy;
            case JsonArray array:
                var items = array.ToList();
                array.Clear();
                return new JsonArray([.. items.Select(Redact)]);
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(Redaction.Apply(v.GetValue<string>()));
            case JsonValue v when Redaction.IsSecret(v.ToJsonString()):
                return JsonValue.Create(Redaction.Mask);
            default:
                return node;
        }
    }

    public static async Task<CallToolResult> Run(ILogger log, string tool, Func<Task<object>> body)
    {
        try
        {
            return Ok(await body());
        }
        catch (InvestigatorException e)
        {
            log.LogInformation("{Tool} refused: {Code}", tool, e.Error.Code);
            return Error(e.Error);
        }
        catch (Exception e)
        {
            log.LogError(e, "{Tool} failed unexpectedly", tool);
            return Error(new InvestigatorError("internal.error", $"{tool} failed ({e.GetType().Name}); no result is returned.",
                "Retry; if it persists, inspect the server's stderr diagnostics."));
        }
    }

    public static Task<CallToolResult> Run(ILogger log, string tool, Func<object> body) => Run(log, tool, () => Task.FromResult(body()));
}

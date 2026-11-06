using System.Text.Json;
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
    public static CallToolResult Ok(object value)
    {
        var text = Redaction.Apply(JsonSerializer.Serialize(value, Json.Options));
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonDocument.Parse(text).RootElement.Clone(),
            IsError = false,
        };
    }

    public static CallToolResult Error(InvestigatorError error)
    {
        var text = Redaction.Apply(JsonSerializer.Serialize(new { error = new { code = error.Code, message = error.Message, action = error.Action, reasons = error.Reasons } }, Json.Options));
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonDocument.Parse(text).RootElement.Clone(),
            IsError = true,
        };
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

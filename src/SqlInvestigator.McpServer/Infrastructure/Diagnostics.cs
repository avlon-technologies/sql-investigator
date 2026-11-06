using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SqlInvestigator.Infrastructure;

/// <summary>
/// Structured, redacted diagnostics on stderr: one JSON object per line. stdout belongs to the MCP protocol
/// (SoleConversationalHost), so nothing in this process writes diagnostics anywhere else.
/// </summary>
internal sealed class StderrJsonLoggerProvider(LogLevel minimum, TimeProvider clock) : ILoggerProvider
{
    private static readonly Lock WriteGate = new();

    public ILogger CreateLogger(string categoryName) => new StderrJsonLogger(categoryName, minimum, clock);

    public void Dispose()
    {
    }

    internal static void Write(string level, string category, string message, string? eventName, Exception? exception, TimeProvider clock)
    {
        var line = JsonSerializer.Serialize(new
        {
            ts = clock.GetUtcNow().ToString("O"),
            level,
            category,
            @event = eventName,
            message = Redaction.Apply(message),
            exception = exception is null ? null : Redaction.Apply($"{exception.GetType().Name}: {exception.Message}"),
        }, Json.Options);
        lock (WriteGate)
        {
            Console.Error.WriteLine(line);
            Console.Error.Flush();
        }
    }

    private sealed class StderrJsonLogger(string category, LogLevel minimum, TimeProvider clock) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            Write(logLevel.ToString().ToLowerInvariant(), category, formatter(state, exception), eventId.Name, exception, clock);
        }
    }
}

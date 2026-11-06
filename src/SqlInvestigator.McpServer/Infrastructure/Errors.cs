namespace SqlInvestigator.Infrastructure;

/// <summary>
/// An actionable, sanitized failure (SafeFailure): a stable code, what happened, and what to do next.
/// Every message passes through <see cref="Redaction"/> before it leaves the process.
/// </summary>
internal sealed record InvestigatorError(string Code, string Message, string Action)
{
    public IReadOnlyList<string>? Reasons { get; init; }
}

internal sealed class InvestigatorException(InvestigatorError error) : Exception(error.Message)
{
    public InvestigatorError Error { get; } = error;

    public static InvestigatorException Input(string message, string action = "Correct the argument and call the tool again.")
        => new(new InvestigatorError("input.invalid", message, action));
}

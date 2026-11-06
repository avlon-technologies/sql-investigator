using System.Text.RegularExpressions;

namespace SqlInvestigator.Infrastructure;

/// <summary>
/// SecretRedaction, enforced at output: every string that leaves the process (MCP results and errors, stderr
/// diagnostics, console output) passes through <see cref="Apply"/>. Every secret this process decrypts is registered
/// here first, so it is scrubbed even if a driver or a server echoes it back in an exception message.
/// </summary>
internal static partial class Redaction
{
    public const string Mask = "[redacted]";
    private static readonly Lock Gate = new();
    private static string[] secrets = [];

    public static void Register(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        lock (Gate)
        {
            if (!secrets.Contains(secret, StringComparer.Ordinal))
            {
                // longest first, so a secret that contains another is masked whole
                secrets = [.. secrets.Append(secret).OrderByDescending(s => s.Length)];
            }
        }
    }

    /// <summary>True when the whole text is a registered secret (used for JSON numbers and booleans).</summary>
    public static bool IsSecret(string text) => secrets.Contains(text, StringComparer.Ordinal);

    public static string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = text;
        foreach (var secret in secrets)
        {
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
        }

        // Anything shaped like a connection-string credential, whoever produced it.
        return ConnectionStringSecret().Replace(result, m => $"{m.Groups["key"].Value}={Mask}");
    }

    [GeneratedRegex(@"(?<key>\b(password|pwd)\s*)=\s*(""[^""]*""|'[^']*'|[^;""'\s]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringSecret();
}

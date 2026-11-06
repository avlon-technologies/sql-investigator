using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Connections;

/// <summary>
/// Opens a connection for one alias: catalog metadata plus the vault secret, never anything an MCP caller supplied.
/// Before first use in a process, the alias's principal is checked: a principal that can write is refused, so a
/// mis-provisioned login cannot become the only barrier between the agent and a write.
/// </summary>
internal sealed class SqlConnectionFactory(CredentialVault vault, Settings settings)
{
    private readonly ConcurrentDictionary<string, bool> verified = new(StringComparer.OrdinalIgnoreCase);

    public static SqlConnectionStringBuilder Describe(ConnectionEntry entry, string? secret, int connectTimeoutSeconds)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = entry.Server,
            InitialCatalog = entry.Database,
            ApplicationName = "SqlInvestigator",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            Encrypt = entry.Encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = entry.TrustServerCertificate,
            ConnectTimeout = connectTimeoutSeconds,
            ConnectRetryCount = 0,
            MultipleActiveResultSets = false,
            Pooling = true,
            PersistSecurityInfo = false,
        };
        if (string.Equals(entry.Authentication, "windows", StringComparison.OrdinalIgnoreCase))
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.UserID = entry.User ?? string.Empty;
            b.Password = secret ?? string.Empty;
        }

        return b;
    }

    public async Task<SqlConnection> OpenAsync(ConnectionEntry entry, CancellationToken cancel)
    {
        var secret = string.Equals(entry.Authentication, "windows", StringComparison.OrdinalIgnoreCase) ? null : vault.ReadSecret(entry.Alias);
        var connection = new SqlConnection(Describe(entry, secret, settings.ConnectTimeoutSeconds).ConnectionString);
        try
        {
            try
            {
                await connection.OpenAsync(cancel);
            }
            catch (SqlException e) when (!SqlFailures.IsLoginOrDatabaseFailure(e) && !cancel.IsCancellationRequested)
            {
                // the connection never opened and the server did not reject the login: the server was not reached
                throw new InvestigatorException(new InvestigatorError("server.unreachable",
                    $"The server of alias \"{entry.Alias}\" could not be reached (error {e.Number}).",
                    "Check that SQL Server is running and reachable from this machine, then retry."));
            }

            var key = $"{entry.Alias}|{entry.ProvisionedAt}";
            if (!verified.ContainsKey(key))
            {
                await PrincipalGuard.RequireReadOnlyAsync(connection, entry, cancel);
                verified[key] = true;
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

/// <summary>Checks that a principal holds no write, DDL or execute permission in its database or on its scope.</summary>
internal static class PrincipalGuard
{
    private const string Probe = """
        SELECT
          CAST(IS_SRVROLEMEMBER('sysadmin') AS int) AS sysadmin,
          CAST(IS_ROLEMEMBER('db_owner') AS int) AS db_owner,
          CAST(IS_ROLEMEMBER('db_datawriter') AS int) AS db_datawriter,
          CAST(IS_ROLEMEMBER('db_ddladmin') AS int) AS db_ddladmin,
          ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT'), 1) + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE'), 1)
            + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE'), 1) + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), 1)
            + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'), 1) + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'EXECUTE'), 1)
            + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 1) + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE PROCEDURE'), 1)
            + ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY SCHEMA'), 1) AS database_writes
        """;

    private const string ObjectProbe = """
        SELECT ISNULL(HAS_PERMS_BY_NAME(@o, 'OBJECT', 'INSERT'), 0) + ISNULL(HAS_PERMS_BY_NAME(@o, 'OBJECT', 'UPDATE'), 0)
             + ISNULL(HAS_PERMS_BY_NAME(@o, 'OBJECT', 'DELETE'), 0) + ISNULL(HAS_PERMS_BY_NAME(@o, 'OBJECT', 'ALTER'), 0)
             + ISNULL(HAS_PERMS_BY_NAME(@o, 'OBJECT', 'CONTROL'), 0) + ISNULL(HAS_PERMS_BY_NAME(@o, 'OBJECT', 'TAKE OWNERSHIP'), 0)
        """;

    public static async Task RequireReadOnlyAsync(SqlConnection connection, ConnectionEntry entry, CancellationToken cancel)
    {
        var problems = new List<string>();
        await using (var command = new SqlCommand(Probe, connection) { CommandTimeout = 15 })
        await using (var reader = await command.ExecuteReaderAsync(cancel))
        {
            if (await reader.ReadAsync(cancel))
            {
                string[] roles = ["sysadmin", "db_owner", "db_datawriter", "db_ddladmin"];
                for (var i = 0; i < roles.Length; i++)
                {
                    if (!reader.IsDBNull(i) && reader.GetInt32(i) == 1)
                    {
                        problems.Add($"member of {roles[i]}");
                    }
                }

                // an unknown answer counts as a write: the guard fails closed
                if (reader.IsDBNull(4) || reader.GetInt32(4) > 0)
                {
                    problems.Add("holds a database-level write, DDL or execute permission");
                }
            }
        }

        foreach (var scope in entry.Scope)
        {
            await using var command = new SqlCommand(ObjectProbe, connection) { CommandTimeout = 15 };
            command.Parameters.Add(new SqlParameter("@o", System.Data.SqlDbType.NVarChar, 600) { Value = scope.QualifiedName });
            var writes = await command.ExecuteScalarAsync(cancel);
            if (writes is not int n || n > 0)
            {
                problems.Add($"can write or alter {scope.QualifiedName}");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvestigatorException(new InvestigatorError(
                "principal.unsafe",
                $"The SQL principal of alias \"{entry.Alias}\" is not read-only: {string.Join("; ", problems)}.",
                "Provision the alias with a dedicated least-privilege login that holds only SELECT on the approved objects.")
            { Reasons = problems });
        }
    }
}

/// <summary>Turns a driver or server failure into an actionable, sanitized error. Never includes a connection string.</summary>
internal static class SqlFailures
{
    public static InvestigatorError Translate(Exception e, string alias, bool cancelledByCaller)
    {
        if (cancelledByCaller)
        {
            return new InvestigatorError("cancelled", $"The request on alias \"{alias}\" was cancelled before it completed; no result is returned.",
                "Run it again if it is still needed.");
        }

        if (e is InvestigatorException ie)
        {
            return ie.Error;
        }

        if (e is SqlException sql)
        {
            // a login rejection can arrive as a later error of the collection, behind a transport error
            var n = sql.Errors.Cast<SqlError>().Select(x => x.Number).FirstOrDefault(IsLoginOrDatabaseFailure, sql.Number);
            return n switch
            {
                -2 => new InvestigatorError("budget.timeout", $"The query on alias \"{alias}\" exceeded the SQL timeout and was cancelled.",
                    "Narrow the query (filter, TOP, fewer joins) and run it again."),
                18456 or 18452 or 18486 or 18487 or 18488 or 233 => new InvestigatorError("login.failed",
                    $"SQL Server rejected the login or password for alias \"{alias}\" (error {n}).",
                    "Check the login and password with the DBA, then provision the alias again at the local console (add --replace if it already exists)."),
                4060 => new InvestigatorError("database.unavailable", $"The database of alias \"{alias}\" cannot be opened by its principal (error {n}).",
                    "Check that the database exists and the alias's login is mapped to a user in it."),
                -1 or 2 or 26 or 40 or 53 or 64 or 121 or 258 or 1225 or 10054 or 10060 or 10061 or 10065 or 11001 or 11004 => new InvestigatorError("server.unreachable",
                    $"The server of alias \"{alias}\" could not be reached (error {n}).",
                    "Check that SQL Server is running and reachable from this machine, then retry."),
                229 or 230 or 262 or 297 or 300 or 916 => new InvestigatorError("permission.denied",
                    $"The principal of alias \"{alias}\" is not permitted to do this (error {n}).",
                    "Stay within the alias's approved objects; the read-only principal does not grant more."),
                207 or 208 or 4104 => new InvestigatorError("sql.error",
                    $"SQL Server could not resolve a name in the query on alias \"{alias}\" (error {n}): {Redaction.Apply(FirstLine(sql.Message))}",
                    "The schema may have changed: call schema_inspect for the alias and correct the query."),
                0 when sql.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase) => new InvestigatorError("cancelled",
                    $"The query on alias \"{alias}\" was cancelled; no result is returned.", "Run it again if it is still needed."),
                _ => new InvestigatorError("sql.error", $"SQL Server returned error {n} for the query on alias \"{alias}\": {Redaction.Apply(FirstLine(sql.Message))}",
                    "Correct the query and run it again; nothing is returned for a failed query."),
            };
        }

        if (e is OperationCanceledException)
        {
            return new InvestigatorError("budget.timeout", $"The request on alias \"{alias}\" did not complete within its time budget and was cancelled.",
                "Narrow the query and run it again, or check that the server is reachable.");
        }

        return new InvestigatorError("internal.error", $"The request on alias \"{alias}\" failed ({e.GetType().Name}).",
            "Retry; if it persists, inspect the server's stderr diagnostics.");
    }

    /// <summary>
    /// The server answered and refused the login or database. Error 233 ("no process is on the other end of the pipe") is
    /// how a refused SQL login surfaces over shared memory and named pipes, so it counts as a refusal, not as unreachable.
    /// </summary>
    public static bool IsLoginOrDatabaseFailure(int number) => number is 18456 or 18452 or 18486 or 18487 or 18488 or 233 or 4060 or 916;

    public static bool IsLoginOrDatabaseFailure(SqlException e) => e.Errors.Cast<SqlError>().Any(x => IsLoginOrDatabaseFailure(x.Number)) || IsLoginOrDatabaseFailure(e.Number);

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length > 300 ? line[..300] : line;
    }
}

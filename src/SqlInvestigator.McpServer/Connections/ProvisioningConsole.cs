using System.Text;
using Microsoft.Data.SqlClient;
using SqlInvestigator.Infrastructure;

namespace SqlInvestigator.Connections;

/// <summary>
/// The local interactive console (ProvisionConnection): the only place a connection and its secret enter the system.
/// The secret is typed at the console (or piped on stdin by the developer's own script); it is never accepted as a
/// command-line argument, so it cannot land in shell history or a process listing. No MCP tool reaches this code.
/// </summary>
internal sealed class ProvisioningConsole(AppPaths paths, Settings settings, TimeProvider clock, TextReader input, TextWriter output, TextWriter error)
{
    public const string Usage = """
        SqlInvestigator — local MCP stdio server for bounded, read-only SQL Server investigation.

        Run without arguments, it speaks MCP over stdio (Claude Code starts it this way).

        Local console commands (run by the developer, never through MCP):
          provision --alias <name> --server <host[,port]> --database <db> --environment fixture|development
                    --scope <schema.object[(col,...)]> [--scope ...]
                    (--user <login> | --windows-auth)
                    [--trust-server-certificate] [--no-encrypt] [--replace] [--no-verify]
              The password is prompted for at the console (not echoed), or read as one line from standard
              input when input is redirected. It is never accepted as an argument.
              The login is checked to be read-only before it is stored (skip with --no-verify).
          remove --alias <name>       remove the alias and delete its DPAPI-protected secret
          list                        list aliases (no secrets): alias, server/database, authentication,
                                      environment, scope, provisioned at; tab-separated
          erase-investigation --id <investigationId>   delete an investigation and its query provenance
          erase-relationship --id <relationshipId>     delete a relationship with its evidence and review
          erase-snapshots --alias <name>               delete the stored schema snapshots of an alias
          --version                   print the version
          --help                      print this text (also after any command, e.g. provision --help)
        """;

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" || args.Any(a => a is "--help" or "-h" or "/?"))
        {
            output.WriteLine(Usage);
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "provision" => await ProvisionAsync(Options.Parse(args[1..])),
                "remove" => Remove(Options.Parse(args[1..])),
                "list" => List(),
                "erase-investigation" => Erase(o => Gateway().EraseInvestigation(o.Required("id")), args, "investigation"),
                "erase-relationship" => Erase(o => Knowledge().EraseRelationship(o.Required("id")), args, "relationship"),
                "erase-snapshots" => Erase(o => Knowledge().EraseSnapshots(o.Required("alias")) > 0, args, "schema snapshots"),
                _ => Fail($"Unknown command \"{args[0]}\". Run SqlInvestigator --help."),
            };
        }
        catch (InvestigatorException e)
        {
            return Fail($"{e.Error.Code}: {Redaction.Apply(e.Error.Message)}\n  {e.Error.Action}");
        }
    }

    private async Task<int> ProvisionAsync(Options o)
    {
        var alias = ConnectionCatalogReader.NormalizeAlias(o.Required("alias"));
        var environment = o.Required("environment").ToLowerInvariant();
        if (!ConnectionCatalogReader.ApprovedEnvironments.Contains(environment))
        {
            return Fail($"environment.unapproved: \"{environment}\" is not an approved environment. Only fixture and development databases may be provisioned; production data needs the data owner's approval first.");
        }

        var windows = o.Flag("windows-auth");
        var user = o.Value("user");
        if (windows == (user is not null))
        {
            return Fail("Pass exactly one of --user <login> or --windows-auth.");
        }

        var scopes = o.Values("scope").Select(ScopeEntry.Parse).ToList();
        if (scopes.Count == 0)
        {
            return Fail("Pass at least one --scope schema.object (for example --scope dbo.Orders). Nothing outside the scope can be read.");
        }

        var entry = new ConnectionEntry
        {
            Alias = alias,
            Server = o.Required("server"),
            Database = o.Required("database"),
            Authentication = windows ? "windows" : "sql",
            User = user,
            Environment = environment,
            Encrypt = !o.Flag("no-encrypt"),
            TrustServerCertificate = o.Flag("trust-server-certificate"),
            Scope = scopes,
            ProvisionedAt = clock.GetUtcNow().ToString("O"),
        };

        var catalog = new ConnectionCatalogReader(paths);
        var replace = o.Flag("replace");
        if (!replace && catalog.All().Any(c => c.Alias == alias))
        {
            return Fail($"alias.exists: alias \"{alias}\" is already provisioned. Pass --replace to re-provision it.");
        }

        string? secret = null;
        if (!windows)
        {
            secret = ReadSecret($"Password for {user} on {entry.Server}/{entry.Database}: ");
            if (string.IsNullOrEmpty(secret))
            {
                return Fail("No password was entered; nothing was provisioned.");
            }

            Redaction.Register(secret);
        }

        if (!o.Flag("no-verify"))
        {
            output.WriteLine($"Checking that {(windows ? "the Windows login" : user)} can connect and is read-only...");
            await using var connection = new SqlConnection(SqlConnectionFactory.Describe(entry, secret, settings.ConnectTimeoutSeconds).ConnectionString);
            try
            {
                await connection.OpenAsync();
                await PrincipalGuard.RequireReadOnlyAsync(connection, entry, CancellationToken.None);
            }
            catch (Exception e) when (e is not InvestigatorException)
            {
                var failure = SqlFailures.Translate(e, alias, false);
                return Fail($"{failure.Code}: {Redaction.Apply(failure.Message)} Nothing was provisioned.\n  {failure.Action}");
            }
        }

        var vault = new CredentialVault(paths);
        var provisioner = new VaultProvisioner(paths, vault);
        SecureFiles.EnsurePrivateDirectory(paths.Home);
        if (secret is not null)
        {
            provisioner.Store(alias, secret);
        }
        else
        {
            provisioner.Delete(alias);
        }

        new ConnectionRegistry(catalog, clock, settings).RecordProvisioned(entry, replace);
        output.WriteLine($"Provisioned alias \"{alias}\": {entry.Server}/{entry.Database}, {entry.Environment}, scope {string.Join(", ", scopes)}.");
        return 0;
    }

    private int Remove(Options o)
    {
        var alias = ConnectionCatalogReader.NormalizeAlias(o.Required("alias"));
        var removed = new ConnectionRegistry(new ConnectionCatalogReader(paths), clock, settings).Remove(alias);
        var deleted = new VaultProvisioner(paths, new CredentialVault(paths)).Delete(alias);
        output.WriteLine(removed || deleted ? $"Removed alias \"{alias}\"{(deleted ? " and its stored secret" : string.Empty)}." : $"Alias \"{alias}\" was not provisioned.");
        return 0;
    }

    private int List()
    {
        var connections = new ConnectionRegistry(new ConnectionCatalogReader(paths), clock, settings).List();
        if (connections.Count == 0)
        {
            output.WriteLine("No aliases are provisioned.");
        }

        foreach (var c in connections)
        {
            output.WriteLine($"{c.Alias}\t{c.Server}/{c.Database}\t{c.Authentication}\t{c.Environment}\t{string.Join(" ", c.Scope)}\t{c.ProvisionedAt}");
        }

        return 0;
    }

    private int Erase(Func<Options, bool> erase, string[] args, string what)
    {
        var erased = erase(Options.Parse(args[1..]));
        output.WriteLine(erased ? $"Erased the {what}." : $"No such {what}; nothing was erased.");
        return 0;
    }

    private Queries.QueryGateway Gateway()
    {
        var vault = new CredentialVault(paths);
        return new Queries.QueryGateway(new ConnectionCatalogReader(paths), new SqlConnectionFactory(vault, settings),
            new Queries.InvestigationHistory(paths, clock, settings), settings, Microsoft.Extensions.Logging.Abstractions.NullLogger<Queries.QueryGateway>.Instance);
    }

    private Knowledge.KnowledgeService Knowledge()
        => new(paths, new ConnectionCatalogReader(paths), new SqlConnectionFactory(new CredentialVault(paths), settings), clock, settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Knowledge.KnowledgeService>.Instance);

    /// <summary>Reads the secret without echoing it when a console is attached; otherwise one line of stdin.</summary>
    private string? ReadSecret(string prompt)
    {
        output.Write(prompt);
        output.Flush();
        if (!ReferenceEquals(input, Console.In) || Console.IsInputRedirected)
        {
            // Windows PowerShell pipes text to a native program with a UTF-8 byte-order mark, which is never part of a password
            var line = input.ReadLine()?.TrimStart('﻿');
            output.WriteLine();
            return line;
        }

        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                output.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }

    private int Fail(string message)
    {
        error.WriteLine(Redaction.Apply(message));
        return 1;
    }

    private sealed class Options
    {
        private static readonly HashSet<string> Flags = ["windows-auth", "trust-server-certificate", "no-encrypt", "replace", "no-verify"];
        private static readonly HashSet<string> Known = ["alias", "server", "database", "environment", "scope", "user", "id"];
        private static readonly HashSet<string> Forbidden = ["password", "pwd", "secret", "credential", "connection-string"];
        private readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> flags = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    throw InvestigatorException.Input($"Unexpected argument \"{args[i]}\".", "Run SqlInvestigator --help.");
                }

                var name = args[i][2..];
                if (Forbidden.Contains(name))
                {
                    throw InvestigatorException.Input("A secret is never accepted as an argument.", "Omit it: the console prompts for the password.");
                }

                if (Flags.Contains(name))
                {
                    o.flags.Add(name);
                }
                else if (Known.Contains(name) && i + 1 < args.Length)
                {
                    (o.values.TryGetValue(name, out var list) ? list : o.values[name] = []).Add(args[++i]);
                }
                else
                {
                    throw InvestigatorException.Input($"Unknown or incomplete option \"--{name}\".", "Run SqlInvestigator --help.");
                }
            }

            return o;
        }

        public bool Flag(string name) => flags.Contains(name);

        public string? Value(string name) => values.TryGetValue(name, out var v) ? v[^1] : null;

        public IReadOnlyList<string> Values(string name) => values.TryGetValue(name, out var v) ? v : [];

        public string Required(string name) => Value(name) is { Length: > 0 } v
            ? v
            : throw InvestigatorException.Input($"--{name} is required.", "Run SqlInvestigator --help.");
    }
}

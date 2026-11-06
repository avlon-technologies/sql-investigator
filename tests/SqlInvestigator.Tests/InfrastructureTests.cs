using System.Reflection;
using ModelContextProtocol.Server;
using SqlInvestigator.Connections;
using SqlInvestigator.Infrastructure;
using SqlInvestigator.Mcp;
using SqlInvestigator.Testing;

namespace SqlInvestigator.Tests;

public sealed class RedactionTests
{
    [Fact]
    public void Masks_a_registered_secret_wherever_it_appears()
    {
        Redaction.Register("s3cr3t-unit-value");
        Assert.Equal("login failed for [redacted] at [redacted]", Redaction.Apply("login failed for s3cr3t-unit-value at s3cr3t-unit-value"));
    }

    [Theory]
    [InlineData("Server=x;Password=hunter2;User ID=u", "Server=x;Password=[redacted];User ID=u")]
    [InlineData("pwd = 'a b c'; x", "pwd =[redacted]; x")]
    [InlineData("PASSWORD=\"q;w\"", "PASSWORD=[redacted]")]
    public void Masks_anything_shaped_like_a_connection_string_password(string input, string expected)
        => Assert.Equal(expected, Redaction.Apply(input));

    [Fact]
    public void Tool_results_are_redacted_before_they_leave()
    {
        Redaction.Register("tool-output-secret");
        var result = ToolResults.Ok(new { note = "the value tool-output-secret leaked" });
        var text = string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        Assert.DoesNotContain("tool-output-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool-output-secret", result.StructuredContent.ToString(), StringComparison.Ordinal);
    }
}

public sealed class SettingsTests
{
    [Fact]
    public void Defaults_are_the_recommended_values()
    {
        var s = Settings.FromEnvironment(_ => null);
        Assert.Equal((15, 500, 1_048_576, 2, 50), (s.SqlTimeoutSeconds, s.MaxQueryRows, s.MaxResultBytes, s.MaxConcurrentQueries, s.MaxQueriesPerInvestigation));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("lots")]
    [InlineData("")]
    public void A_bad_value_falls_back_to_the_default_never_to_unlimited(string value)
        => Assert.Equal(500, Settings.FromEnvironment(name => name == "MAX_QUERY_ROWS" ? value : null).MaxQueryRows);

    [Fact]
    public void A_valid_value_is_read()
        => Assert.Equal(7, Settings.FromEnvironment(name => name == "SQL_TIMEOUT_SECONDS" ? "7" : null).SqlTimeoutSeconds);
}

public sealed class ScopeTests
{
    [Fact]
    public void Parses_a_table_with_and_without_columns()
    {
        var all = ScopeEntry.Parse("dbo.Orders");
        Assert.Null(all.Columns);
        Assert.True(all.AllowsColumn("anything"));

        var some = ScopeEntry.Parse("dbo.Orders(OrderId, Status)");
        Assert.Equal(["OrderId", "Status"], some.Columns);
        Assert.True(some.AllowsColumn("orderid"));
        Assert.False(some.AllowsColumn("CustomerEmail"));
        Assert.Equal("dbo.Orders(OrderId,Status)", some.ToString());
    }

    [Theory]
    [InlineData("Orders")]
    [InlineData("db.dbo.Orders")]
    [InlineData("dbo.Orders()")]
    [InlineData("dbo.Orders; DROP TABLE x")]
    public void Refuses_a_malformed_scope(string text)
        => Assert.Equal("input.invalid", Assert.Throws<InvestigatorException>(() => ScopeEntry.Parse(text)).Error.Code);

    [Theory]
    [InlineData("alpha", "alpha")]
    [InlineData(" Beta-2 ", "beta-2")]
    public void Normalizes_an_alias(string alias, string expected) => Assert.Equal(expected, ConnectionCatalogReader.NormalizeAlias(alias));

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("../vault")]
    [InlineData("a b")]
    public void Refuses_an_alias_that_could_name_a_path(string alias)
        => Assert.Throws<InvestigatorException>(() => ConnectionCatalogReader.NormalizeAlias(alias));
}

public sealed class StoreTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "sqlinv-unit-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(home, true);

    private AppPaths Paths()
    {
        SecureFiles.EnsurePrivateDirectory(home);
        return new AppPaths(home);
    }

    private static ConnectionEntry Entry(string environment = "fixture") => new()
    {
        Alias = "alpha",
        Server = "s",
        Database = "d",
        Authentication = "sql",
        User = "u",
        Environment = environment,
        Scope = [ScopeEntry.Parse("dbo.Orders")],
        ProvisionedAt = "2026-11-06T00:00:00Z",
    };

    [Fact]
    public void A_corrupt_catalog_fails_closed_as_policy_unavailable()
    {
        var paths = Paths();
        File.WriteAllText(paths.CatalogFile, "{ not json");
        Assert.Equal("policy.unavailable", Assert.Throws<InvestigatorException>(() => new ConnectionCatalogReader(paths).Require("alpha")).Error.Code);
    }

    [Fact]
    public void An_unknown_alias_is_refused()
        => Assert.Equal("alias.unknown", Assert.Throws<InvestigatorException>(() => new ConnectionCatalogReader(Paths()).Require("gamma")).Error.Code);

    [Fact]
    public void Production_is_refused_at_provisioning_and_at_query_time()
    {
        var paths = Paths();
        var catalog = new ConnectionCatalogReader(paths);
        var registry = new ConnectionRegistry(catalog, TimeProvider.System, new Settings());
        Assert.Equal("environment.unapproved", Assert.Throws<InvestigatorException>(() => registry.RecordProvisioned(Entry("production"), false)).Error.Code);

        // even if a production entry were written into the catalog by hand, it is not usable
        catalog.Store.Update(d =>
        {
            d.Connections.Add(Entry("production"));
            return 0;
        });
        Assert.Equal("environment.unapproved", Assert.Throws<InvestigatorException>(() => catalog.Require("alpha")).Error.Code);
    }

    [Fact]
    public void Registry_lists_metadata_and_removes()
    {
        var paths = Paths();
        var registry = new ConnectionRegistry(new ConnectionCatalogReader(paths), TimeProvider.System, new Settings());
        registry.RecordProvisioned(Entry(), false);
        Assert.Equal("alias.exists", Assert.Throws<InvestigatorException>(() => registry.RecordProvisioned(Entry(), false)).Error.Code);
        var listed = Assert.Single(registry.List());
        Assert.Equal(("alpha", "s", "d"), (listed.Alias, listed.Server, listed.Database));
        Assert.True(registry.Remove("alpha"));
        Assert.Empty(registry.List());
        Assert.False(registry.Remove("alpha"));
    }

    [Fact]
    public void Stores_are_written_atomically_and_restricted_to_the_current_user()
    {
        var paths = Paths();
        new ConnectionRegistry(new ConnectionCatalogReader(paths), TimeProvider.System, new Settings()).RecordProvisioned(Entry(), false);
        Assert.True(VaultInspector.IsCurrentUserOnly(paths.CatalogFile));
        Assert.True(VaultInspector.IsCurrentUserOnly(home));
        Assert.Empty(Directory.GetFiles(home, "*.tmp"));
    }

    [Fact]
    public void History_and_knowledge_entries_can_be_erased_with_an_audit_event()
    {
        var paths = Paths();
        var settings = new Settings();
        var catalog = new ConnectionCatalogReader(paths);
        var connections = new SqlConnectionFactory(new CredentialVault(paths), settings);
        var gateway = new Queries.QueryGateway(catalog, connections, new Queries.InvestigationHistory(paths, TimeProvider.System, settings), settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Queries.QueryGateway>.Instance);
        var investigation = gateway.Start("erase me");
        gateway.Record(investigation.InvestigationId, "h", null, null, "f", "u");
        Assert.True(gateway.EraseInvestigation(investigation.InvestigationId));
        Assert.Equal("investigation.unknown", Assert.Throws<InvestigatorException>(() => gateway.Get(investigation.InvestigationId)).Error.Code);
        Assert.Contains("investigation.erased", File.ReadAllText(paths.HistoryFile), StringComparison.Ordinal);

        var knowledge = new Knowledge.KnowledgeService(paths, catalog, connections, TimeProvider.System, settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Knowledge.KnowledgeService>.Instance);
        var relationship = knowledge.Propose("a.T.x", "b.U.x", "q-1", null);
        Assert.Equal("proposed", relationship.State);
        Assert.Equal("relationship.closed", Assert.Throws<InvestigatorException>(() =>
        {
            knowledge.Review(relationship.RelationshipId, "rejected", "no");
            return knowledge.Review(relationship.RelationshipId, "accepted", "yes");
        }).Error.Code);
        Assert.Equal("rejected", Assert.Single(knowledge.Search("", null).Relationships).State);
        Assert.True(knowledge.EraseRelationship(relationship.RelationshipId));
        Assert.Empty(knowledge.Search("", null).Relationships);
    }

    [Fact]
    public void The_vault_holds_a_dpapi_current_user_blob_bound_to_its_alias()
    {
        var paths = Paths();
        var vault = new CredentialVault(paths);
        new VaultProvisioner(paths, vault).Store("alpha", "vault-unit-secret");

        var file = vault.FileFor("alpha");
        var blob = File.ReadAllBytes(file);
        Assert.True(VaultInspector.IsDpapiCurrentUserBlob(blob));
        Assert.True(VaultInspector.DecryptsForAlias(blob, "alpha", "vault-unit-secret"));
        Assert.True(VaultInspector.IsCurrentUserOnly(file));
        Assert.Empty(VaultInspector.FilesContaining(home, "vault-unit-secret"));
        Assert.Equal("vault-unit-secret", vault.ReadSecret("alpha"));

        // a blob copied to another alias does not decrypt there
        File.Copy(file, vault.FileFor("beta"));
        Assert.Equal("credential.unavailable", Assert.Throws<InvestigatorException>(() => vault.ReadSecret("beta")).Error.Code);
        Assert.Equal("credential.unavailable", Assert.Throws<InvestigatorException>(() => vault.ReadSecret("gamma")).Error.Code);
    }
}

public sealed class ToolSurfaceTests
{
    private static readonly string[] Expected =
    [
        "connection_list", "connection_remove", "schema_inspect", "sql_validate", "sql_execute", "investigation_start",
        "investigation_record", "investigation_get", "knowledge_search", "knowledge_propose", "knowledge_review",
    ];

    private static IEnumerable<(MethodInfo Method, McpServerToolAttribute Tool)> Tools()
        => typeof(InvestigatorTools).GetMethods()
            .Select(m => (m, m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(t => t.Item2 is not null)
            .Select(t => (t.m, t.Item2!));

    [Fact]
    public void Exactly_the_eleven_declared_tools()
        => Assert.Equal(Expected.Order(), Tools().Select(t => t.Tool.Name!).Order());

    [Fact]
    public void No_tool_accepts_a_credential()
    {
        foreach (var (method, _) in Tools())
        {
            Assert.DoesNotContain(method.GetParameters(), p => p.Name is { } n && (n.Contains("password", StringComparison.OrdinalIgnoreCase)
                || n.Contains("secret", StringComparison.OrdinalIgnoreCase) || n.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || n.Contains("connection", StringComparison.OrdinalIgnoreCase) || n.Equals("user", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Only_connection_remove_is_destructive()
        => Assert.Equal(["connection_remove"], Tools().Where(t => t.Tool.Destructive).Select(t => t.Tool.Name!));

    [Fact]
    public async Task The_console_refuses_a_secret_on_the_command_line()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var home = Path.Combine(Path.GetTempPath(), "sqlinv-console-" + Guid.NewGuid().ToString("N"));
        var console = new ProvisioningConsole(new AppPaths(home), new Settings(), TimeProvider.System, new StringReader(string.Empty), output, error);
        var code = await console.RunAsync(["provision", "--alias", "a", "--password", "argv-secret"]);
        Assert.Equal(1, code);
        Assert.Contains("never accepted as an argument", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(home));
    }

    [Fact]
    public async Task The_console_refuses_production()
    {
        var error = new StringWriter();
        var home = Path.Combine(Path.GetTempPath(), "sqlinv-console-" + Guid.NewGuid().ToString("N"));
        var console = new ProvisioningConsole(new AppPaths(home), new Settings(), TimeProvider.System, new StringReader("pw\n"), new StringWriter(), error);
        var code = await console.RunAsync(["provision", "--alias", "prod", "--server", "s", "--database", "d", "--user", "u", "--environment", "production", "--scope", "dbo.T"]);
        Assert.Equal(1, code);
        Assert.Contains("environment.unapproved", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(home));
    }
}

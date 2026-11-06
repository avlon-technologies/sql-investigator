using System.Reflection;
using System.Runtime.Versioning;
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

    // Cold-start safety run 2026-11-06: redacting the serialized JSON text swallowed the backslash of an escaped
    // quote after "Password=", so the document no longer parsed and the actionable error became the SDK's generic one.
    [Fact]
    public void An_error_quoting_a_connection_string_stays_an_actionable_redacted_error()
    {
        var result = ToolResults.Error(new InvestigatorError("input.invalid", "Alias \"Server=x;Password=Fake-Secret-123\" is not valid.", "Use an alias."));
        Assert.True(result.IsError);
        var error = result.StructuredContent!.Value.GetProperty("error");
        Assert.Equal("input.invalid", error.GetProperty("code").GetString());
        Assert.Equal("Alias \"Server=x;Password=[redacted]\" is not valid.", error.GetProperty("message").GetString());
    }

    [Fact]
    public void Row_values_with_quotes_and_backslashes_after_a_password_key_keep_the_result_valid()
    {
        Redaction.Register("424242");
        var result = ToolResults.Ok(new { rows = new object[] { new { note = "pwd=a\\\"b and \"x\"", pin = 424242, ok = 7 } } });
        var row = result.StructuredContent!.Value.GetProperty("rows")[0];
        Assert.Equal("pwd=[redacted]\"b and \"x\"", row.GetProperty("note").GetString());
        Assert.Equal("[redacted]", row.GetProperty("pin").GetString());
        Assert.Equal(7, row.GetProperty("ok").GetInt32());
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

    public void Dispose()
    {
        if (OperatingSystem.IsMacOS())
        {
            foreach (var alias in new[] { "alpha", "beta", "gamma" })
            {
                KeychainProbe.Delete(home, alias);
            }
        }

        if (Directory.Exists(home))
        {
            Directory.Delete(home, true);
        }
    }

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

    // Cold-start release validation 2026-11-06: SQLINVESTIGATOR_HOME under a folder that grants only Modify (like a
    // folder made directly under C:\) crashed the server, because writing the owner needs WRITE_OWNER.
    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void A_private_directory_can_be_made_under_a_folder_that_grants_only_modify()
    {
        var parent = Path.Combine(home, "modify-only");
        Directory.CreateDirectory(parent);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var restricted = new System.Security.AccessControl.DirectorySecurity();
        restricted.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user,
            System.Security.AccessControl.FileSystemRights.Modify | System.Security.AccessControl.FileSystemRights.Synchronize,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(parent).SetAccessControl(restricted);

        var data = Path.Combine(parent, "data");
        SecureFiles.EnsurePrivateDirectory(data);
        Assert.True(VaultInspector.IsCurrentUserOnly(data));
        new ConnectionRegistry(new ConnectionCatalogReader(new AppPaths(data)), TimeProvider.System, new Settings()).RecordProvisioned(Entry(), false);
        Assert.True(VaultInspector.IsCurrentUserOnly(Path.Combine(data, "catalog.json")));
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

    [WindowsFact]
    [SupportedOSPlatform("windows")]
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

    // Gap 2026-11-06-remove-connection-secret: connection_remove (RemoveConnection) writes only the catalog; the secret
    // is deleted only by the local console, including after the alias was already removed over MCP.
    [Fact]
    public async Task Removing_an_alias_over_mcp_keeps_its_secret_until_the_console_removes_it()
    {
        var paths = Paths();
        var vault = new CredentialVault(paths);
        new VaultProvisioner(paths, vault).Store("alpha", "remove-unit-secret");
        var registry = new ConnectionRegistry(new ConnectionCatalogReader(paths), TimeProvider.System, new Settings());
        registry.RecordProvisioned(Entry(), false);

        Assert.True(registry.Remove("alpha"));
        Assert.Equal("alias.unknown", Assert.Throws<InvestigatorException>(() => new ConnectionCatalogReader(paths).Require("alpha")).Error.Code);
        Assert.True(VaultInspector.Check(home, "alpha", "remove-unit-secret").Present);

        var output = new StringWriter();
        var console = new ProvisioningConsole(paths, new Settings(), TimeProvider.System, new StringReader(string.Empty), output, new StringWriter());
        Assert.Equal(0, await console.RunAsync(["remove", "--alias", "alpha"]));
        Assert.Contains("and its stored secret", output.ToString(), StringComparison.Ordinal);
        Assert.False(VaultInspector.Check(home, "alpha", "remove-unit-secret").Present);
    }

    // ADR-007: on macOS the secret is a generic-password item in the login keychain, named for this data home and
    // this alias, and nothing about it lands under the data home. That only SqlInvestigator itself may read it is a
    // cross-process property, checked by acceptance scenario B01 (the test host is the application that added it).
    [MacFact]
    [SupportedOSPlatform("macos")]
    public void The_vault_keeps_a_keychain_item_per_alias_and_nothing_under_the_data_home()
    {
        var paths = Paths();
        var vault = new CredentialVault(paths);
        var provisioner = new VaultProvisioner(paths, vault);
        provisioner.Store("alpha", "keychain-unit-secret");

        Assert.Equal(KeychainProbe.Success, KeychainProbe.Lookup(home, "alpha"));
        Assert.Equal(KeychainProbe.ItemNotFound, KeychainProbe.Lookup(home, "beta"));
        Assert.Equal("keychain-unit-secret", vault.ReadSecret("alpha"));
        Assert.False(Directory.Exists(paths.VaultDirectory));
        Assert.Empty(VaultInspector.FilesContaining(home, "keychain-unit-secret"));

        provisioner.Store("alpha", "keychain-unit-secret-2"); // --replace updates the item in place
        Assert.Equal("keychain-unit-secret-2", vault.ReadSecret("alpha"));

        // another data home never sees this home's item
        var other = new CredentialVault(new AppPaths(home + "-other"));
        Assert.Equal("credential.unavailable", Assert.Throws<InvestigatorException>(() => other.ReadSecret("alpha")).Error.Code);

        Assert.True(provisioner.Delete("alpha"));
        Assert.False(provisioner.Delete("alpha"));
        Assert.Equal("credential.unavailable", Assert.Throws<InvestigatorException>(() => vault.ReadSecret("alpha")).Error.Code);
    }

    [MacFact]
    [SupportedOSPlatform("macos")]
    public void Data_folders_are_0700_and_store_files_0600_on_macos()
    {
        var paths = Paths();
        new ConnectionRegistry(new ConnectionCatalogReader(paths), TimeProvider.System, new Settings()).RecordProvisioned(Entry(), false);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(home));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(paths.CatalogFile));

        // a folder someone else could read is tightened, not trusted
        var loose = Path.Combine(home, "loose");
        Directory.CreateDirectory(loose);
        File.SetUnixFileMode(loose, (UnixFileMode)0b111_101_101);
        SecureFiles.EnsurePrivateDirectory(loose);
        Assert.True(VaultInspector.IsCurrentUserOnly(loose));
    }

    [MacFact]
    [SupportedOSPlatform("macos")]
    public void The_default_data_home_on_macos_is_under_application_support()
    {
        var previous = Environment.GetEnvironmentVariable("SQLINVESTIGATOR_HOME");
        try
        {
            Environment.SetEnvironmentVariable("SQLINVESTIGATOR_HOME", null);
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "SqlInvestigator", "data");
            Assert.Equal(expected, AppPaths.FromEnvironment().Home);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SQLINVESTIGATOR_HOME", previous);
        }
    }
}

/// <summary>A fact that runs on Windows only (DPAPI and NTFS ACLs).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only";
        }
    }
}

/// <summary>A fact that runs on macOS only (the login keychain and POSIX modes).</summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "macOS only";
        }
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
    public void Connection_remove_says_it_keeps_the_secret_and_names_the_console_command()
    {
        var description = typeof(InvestigatorTools).GetMethod(nameof(InvestigatorTools.ConnectionRemove))!
            .GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
        Assert.Contains("does NOT delete", description, StringComparison.Ordinal);
        Assert.Contains("SqlInvestigator remove --alias", description, StringComparison.Ordinal);
    }

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

    // Cold-start safety run 2026-11-06: `$pw | SqlInvestigator.exe provision ...` in Windows PowerShell 5.1 sends
    // EF BB BF first, and the BOM became part of the stored password, so every login failed.
    [Fact]
    public async Task A_piped_password_loses_a_leading_byte_order_mark()
    {
        var home = Path.Combine(Path.GetTempPath(), "sqlinv-console-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(home);
            var console = new ProvisioningConsole(paths, new Settings(), TimeProvider.System, new StringReader("﻿piped-secret\n"), new StringWriter(), new StringWriter());
            var code = await console.RunAsync(["provision", "--alias", "bom", "--server", "s", "--database", "d", "--user", "u", "--environment", "fixture", "--scope", "dbo.T", "--no-verify"]);
            Assert.Equal(0, code);
            Assert.Equal("piped-secret", new CredentialVault(paths).ReadSecret("bom"));
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                KeychainProbe.Delete(home, "bom");
            }

            if (Directory.Exists(home))
            {
                Directory.Delete(home, true);
            }
        }
    }

    // Cold-start evaluation 2026-11-06: `provision --help` was rejected as an unknown option.
    [Fact]
    public async Task Help_after_a_command_prints_the_usage_and_mentions_stdin()
    {
        var output = new StringWriter();
        var home = Path.Combine(Path.GetTempPath(), "sqlinv-console-" + Guid.NewGuid().ToString("N"));
        var console = new ProvisioningConsole(new AppPaths(home), new Settings(), TimeProvider.System, new StringReader(string.Empty), output, new StringWriter());
        Assert.Equal(0, await console.RunAsync(["provision", "--help"]));
        Assert.Contains("read as one line from standard input when input is redirected", System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " "), StringComparison.Ordinal);
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

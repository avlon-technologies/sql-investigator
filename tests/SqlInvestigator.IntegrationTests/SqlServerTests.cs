using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlInvestigator.Connections;
using SqlInvestigator.Infrastructure;
using SqlInvestigator.Knowledge;
using SqlInvestigator.Queries;
using SqlInvestigator.Testing;

namespace SqlInvestigator.IntegrationTests;

/// <summary>
/// Against a disposable SQL Server fixture on the local test server (SQLINVESTIGATOR_FIXTURE_SERVER). These tests need
/// that server: they fail, rather than skip, when it is absent, so a green run always means they ran.
/// </summary>
public sealed class SqlServerTests : IDisposable
{
    private const string Scope = "dbo.Orders(Id,OrderId,Status,CreatedAt)";
    private readonly List<FixtureDatabase> fixtures = [];
    private readonly string home = Path.Combine(Path.GetTempPath(), "sqlinv-int-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        foreach (var f in fixtures)
        {
            f.Drop();
        }

        if (Directory.Exists(home))
        {
            Directory.Delete(home, true);
        }
    }

    private FixtureDatabase Fixture(string kind = "orders", IReadOnlyList<string>? grant = null)
    {
        var f = FixtureDatabase.Create("it", kind, grant ?? [Scope]);
        fixtures.Add(f);
        return f;
    }

    private (AppPaths Paths, Settings Settings, ConnectionCatalogReader Catalog, SqlConnectionFactory Connections) Provision(FixtureDatabase f, string scope = Scope, Settings? settings = null)
    {
        SecureFiles.EnsurePrivateDirectory(home);
        var paths = new AppPaths(home);
        settings ??= new Settings();
        var catalog = new ConnectionCatalogReader(paths);
        var vault = new CredentialVault(paths);
        new VaultProvisioner(paths, vault).Store("alpha", FixtureDatabase.Secret);
        new ConnectionRegistry(catalog, TimeProvider.System, settings).RecordProvisioned(Entry(f, scope), replace: true);
        return (paths, settings, catalog, new SqlConnectionFactory(vault, settings));
    }

    private static ConnectionEntry Entry(FixtureDatabase f, string scope = Scope) => new()
    {
        Alias = "alpha",
        Server = f.Server,
        Database = f.Database,
        Authentication = "sql",
        User = f.Login,
        Environment = "fixture",
        TrustServerCertificate = true,
        Scope = [ScopeEntry.Parse(scope)],
        ProvisionedAt = DateTimeOffset.UtcNow.ToString("O"),
    };

    private QueryGateway Gateway(FixtureDatabase f, Settings? settings = null)
    {
        var (paths, s, catalog, connections) = Provision(f, settings: settings);
        return new QueryGateway(catalog, connections, new InvestigationHistory(paths, TimeProvider.System, s), s, NullLogger<QueryGateway>.Instance);
    }

    /// <summary>The second barrier, on its own: the principal is denied writes and DDL with no validator in the way.</summary>
    [Theory]
    [InlineData("INSERT INTO dbo.Orders DEFAULT VALUES")]
    [InlineData("UPDATE dbo.Orders SET Id = Id")]
    [InlineData("UPDATE dbo.Orders SET Status = N'x'")]
    [InlineData("DELETE FROM dbo.Orders")]
    [InlineData("TRUNCATE TABLE dbo.Orders")]
    [InlineData("CREATE TABLE dbo.Probe (Id int)")]
    [InlineData("DROP TABLE dbo.Orders")]
    [InlineData("ALTER TABLE dbo.Orders ADD Evil int")]
    [InlineData("SELECT OrderId INTO dbo.Copy FROM dbo.Orders")]
    [InlineData("SELECT CustomerEmail FROM dbo.Orders")]
    [InlineData("SELECT Id FROM dbo.Customers")]
    [InlineData("GRANT SELECT ON dbo.Customers TO public")]
    public void The_principal_alone_denies_writes_and_unapproved_reads(string sql)
    {
        var f = Fixture();
        var (error, message) = f.ExecuteAsPrincipal(sql);
        Assert.True(error is 229 or 230 or 262 or 297 or 300 or 1088 or 2760 or 4701 or 15151 or 3701 or 4902, $"{sql}: {error} {message}");
        Assert.Equal(2000, f.RowCount("dbo.Orders"));
        Assert.False(f.TableExists("dbo.Probe"));
        Assert.False(f.TableExists("dbo.Copy"));
    }

    [Fact]
    public async Task The_guard_refuses_a_principal_that_can_write()
    {
        var f = Fixture();
        using (var admin = new SqlConnection(FixtureDatabase.AdminConnectionString(f.Database, f.Server)))
        {
            await admin.OpenAsync();
            await new SqlCommand($"GRANT INSERT ON dbo.Orders TO [{f.Login}]", admin).ExecuteNonQueryAsync();
        }

        var gateway = Gateway(f);
        var e = await Assert.ThrowsAsync<InvestigatorException>(() => gateway.ExecuteAsync("alpha", "SELECT OrderId FROM dbo.Orders", null, CancellationToken.None));
        Assert.Equal("principal.unsafe", e.Error.Code);
        Assert.Contains(e.Error.Reasons!, r => r.Contains("dbo.Orders", StringComparison.Ordinal));
    }

    /// <summary>
    /// The fixture server's certificate is self-signed. Without --trust-server-certificate the TLS handshake fails, and
    /// that must read as a connection problem with the flag as the remedy, never as "correct the query".
    /// </summary>
    [Fact]
    public async Task An_untrusted_server_certificate_is_reported_with_the_trust_flag_as_the_remedy()
    {
        var f = Fixture();
        var entry = Entry(f) with { TrustServerCertificate = false };
        await using var connection = new SqlConnection(SqlConnectionFactory.Describe(entry, FixtureDatabase.Secret, 15).ConnectionString);
        var e = await Assert.ThrowsAsync<SqlException>(() => connection.OpenAsync());

        var error = SqlFailures.Translate(e, "alpha", false);
        Assert.Equal("server.unreachable", error.Code);
        Assert.Contains("TLS", error.Message, StringComparison.Ordinal);
        Assert.Contains("--trust-server-certificate", error.Action, StringComparison.Ordinal);
        Assert.DoesNotContain(FixtureDatabase.Secret, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_guard_refuses_a_db_owner()
    {
        var f = Fixture();
        using (var admin = new SqlConnection(FixtureDatabase.AdminConnectionString(f.Database, f.Server)))
        {
            await admin.OpenAsync();
            await new SqlCommand($"ALTER ROLE db_owner ADD MEMBER [{f.Login}]", admin).ExecuteNonQueryAsync();
        }

        var e = await Assert.ThrowsAsync<InvestigatorException>(() => Gateway(f).ExecuteAsync("alpha", "SELECT OrderId FROM dbo.Orders", null, CancellationToken.None));
        Assert.Equal("principal.unsafe", e.Error.Code);
    }

    [Fact]
    public async Task Executes_one_bounded_select_and_records_provenance_without_rows()
    {
        var f = Fixture();
        var gateway = Gateway(f, new Settings { MaxQueryRows = 25 });
        var investigation = gateway.Start("how many orders are delayed?");
        const string sql = "SELECT OrderId, Status FROM dbo.Orders WHERE Status = N'delayed' ORDER BY OrderId";
        var evidence = await gateway.ExecuteAsync("alpha", sql, investigation.InvestigationId, CancellationToken.None);

        Assert.Equal(sql, evidence.Sql);
        Assert.Equal(25, evidence.RowCount);
        Assert.True(evidence.Truncated);
        Assert.Equal("rows", evidence.TruncatedBy);
        Assert.Equal(["OrderId", "Status"], evidence.Columns);
        Assert.All(evidence.Rows, r => Assert.Equal("delayed", r[1]));

        gateway.Record(investigation.InvestigationId, "a quarter are delayed", [evidence.QueryId], null, "500 of 2000", "fixture data");
        var view = gateway.Get(investigation.InvestigationId);
        var q = Assert.Single(view.Queries);
        Assert.Equal((evidence.QueryId, sql, "succeeded"), (q.QueryId, q.Sql, q.Status));
        var stored = File.ReadAllText(Path.Combine(home, "history.json"));
        Assert.DoesNotContain("\"rows\":", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(evidence.Rows[0][0]!, stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_is_cancelled_and_reported_never_returned_as_a_result()
    {
        var f = Fixture();
        var gateway = Gateway(f, new Settings { SqlTimeoutSeconds = 1 });
        var e = await Assert.ThrowsAsync<InvestigatorException>(() => gateway.ExecuteAsync("alpha",
            "SELECT COUNT_BIG(a.OrderId) AS n FROM dbo.Orders AS a CROSS JOIN dbo.Orders AS b CROSS JOIN dbo.Orders AS c WHERE (a.OrderId * b.OrderId + c.OrderId) % 7919 = 3",
            null, CancellationToken.None));
        Assert.Equal("budget.timeout", e.Error.Code);
    }

    [Fact]
    public async Task A_rejected_statement_never_reaches_the_server()
    {
        var f = Fixture();
        var gateway = Gateway(f);
        var e = await Assert.ThrowsAsync<InvestigatorException>(() => gateway.ExecuteAsync("alpha", "DELETE FROM dbo.Orders", null, CancellationToken.None));
        Assert.Equal("sql.rejected", e.Error.Code);
        Assert.Equal(2000, f.RowCount("dbo.Orders"));
        var history = File.ReadAllText(Path.Combine(home, "history.json"));
        Assert.Contains("sql.rejected", history, StringComparison.Ordinal);
        Assert.DoesNotContain("sql.started", history, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_snapshots_report_approved_columns_and_drift()
    {
        var f = Fixture();
        var (paths, settings, catalog, connections) = Provision(f);
        var knowledge = new KnowledgeService(paths, catalog, connections, TimeProvider.System, settings, NullLogger<KnowledgeService>.Instance);

        var first = await knowledge.InspectAsync("alpha", null, false, CancellationToken.None);
        Assert.Equal("captured", first.Status);
        var columns = Assert.Single(first.Snapshot.Detail).Columns.Select(c => c.Name).ToList();
        Assert.Equal(["Id", "OrderId", "Status", "CreatedAt"], columns);
        Assert.DoesNotContain("CustomerEmail", string.Join(",", first.Snapshot.Objects), StringComparison.Ordinal);

        f.RenameColumn("dbo.Orders", "Status", "OrderStatus");
        var after = await knowledge.InspectAsync("alpha", null, false, CancellationToken.None);
        Assert.Equal("drifted", after.Status);
        Assert.Contains("dbo.Orders.Status", after.Snapshot.Missing);
        Assert.DoesNotContain(after.Snapshot.Detail.Single().Columns, c => c.Name == "Status");
    }
}

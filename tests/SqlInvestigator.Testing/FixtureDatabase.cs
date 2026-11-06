using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace SqlInvestigator.Testing;

/// <summary>
/// A disposable SQL Server fixture: a database with synthetic data and a dedicated SELECT-only login. Created on a
/// local test server (a container), never against production. Every fixture database is named SqlInvFixture_*, and
/// every login sqlinv_fixture_*, so cleanup can find leftovers.
/// </summary>
public sealed partial class FixtureDatabase
{
    /// <summary>The synthetic password of every fixture login. A test value, used to prove it never leaks.</summary>
    public const string Secret = "fixture-secret";

    public const string DatabasePrefix = "SqlInvFixture_";
    public const string LoginPrefix = "sqlinv_fixture_";

    private FixtureDatabase(string server, string database, string login)
    {
        Server = server;
        Database = database;
        Login = login;
    }

    public string Server { get; }
    public string Database { get; }
    public string Login { get; }

    /// <summary>The test server: SQLINVESTIGATOR_FIXTURE_SERVER, default the local container at 127.0.0.1,1433.</summary>
    public static string DefaultServer => Environment.GetEnvironmentVariable("SQLINVESTIGATOR_FIXTURE_SERVER") is { Length: > 0 } s ? s : "127.0.0.1,1433";

    /// <summary>The admin connection to the test server. The admin password comes from the environment or a local dev file.</summary>
    public static string AdminConnectionString(string? database = null, string? server = null)
    {
        var password = Environment.GetEnvironmentVariable("SQLINVESTIGATOR_FIXTURE_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlInvestigator", "dev-fixture", "sa-password.txt");
            password = File.Exists(file) ? File.ReadAllText(file).Trim() : throw new InvalidOperationException(
                "No fixture admin password: set SQLINVESTIGATOR_FIXTURE_ADMIN_PASSWORD or write it to " + file);
        }

        return new SqlConnectionStringBuilder
        {
            DataSource = server ?? DefaultServer,
            InitialCatalog = database ?? "master",
            UserID = Environment.GetEnvironmentVariable("SQLINVESTIGATOR_FIXTURE_ADMIN_USER") is { Length: > 0 } u ? u : "sa",
            Password = password,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            Pooling = false,
        }.ConnectionString;
    }

    /// <summary>
    /// Creates a fixture. <paramref name="kind"/> is <c>orders</c> (dbo.Orders, dbo.Customers) or <c>shipments</c>
    /// (dbo.Shipments). The login gets SELECT on <paramref name="grant"/> only (default dbo.Orders / dbo.Shipments).
    /// </summary>
    public static FixtureDatabase Create(string name, string kind, IReadOnlyList<string>? grant = null, string? server = null)
    {
        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException("A fixture name is letters, digits and underscores.", nameof(name));
        }

        var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var fixture = new FixtureDatabase(server ?? DefaultServer, $"{DatabasePrefix}{name}_{suffix}", $"{LoginPrefix}{name}_{suffix}");
        Exec(AdminConnectionString(server: fixture.Server), $"CREATE DATABASE [{fixture.Database}]; ALTER DATABASE [{fixture.Database}] SET RECOVERY SIMPLE;");
        Exec(AdminConnectionString(server: fixture.Server),
            $"CREATE LOGIN [{fixture.Login}] WITH PASSWORD = N'{Secret}', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{fixture.Database}];");

        var db = AdminConnectionString(fixture.Database, fixture.Server);
        switch (kind)
        {
            case "orders":
                Exec(db, """
                    CREATE SEQUENCE dbo.OrderIds AS int START WITH 1;
                    CREATE TABLE dbo.Customers (Id int NOT NULL PRIMARY KEY, Name nvarchar(100) NOT NULL, Email nvarchar(200) NOT NULL);
                    CREATE TABLE dbo.Orders (
                        Id int NOT NULL CONSTRAINT DF_Orders_Id DEFAULT (NEXT VALUE FOR dbo.OrderIds) PRIMARY KEY,
                        OrderId int NULL, Status nvarchar(20) NULL, CreatedAt datetime2(0) NULL, CustomerEmail nvarchar(200) NULL);
                    WITH n AS (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS k FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b)
                    INSERT INTO dbo.Orders (OrderId, Status, CreatedAt, CustomerEmail)
                    SELECT 1000 + k, CHOOSE(1 + k % 4, N'placed', N'packed', N'shipped', N'delayed'),
                           DATEADD(hour, k, '2026-01-01'), CONCAT(N'customer', k, N'@example.test')
                    FROM n;
                    INSERT INTO dbo.Customers (Id, Name, Email) VALUES (1, N'Synthetic One', N'one@example.test'), (2, N'Synthetic Two', N'two@example.test');
                    """);
                break;
            case "shipments":
                // matched (1001-1010), one OrderId shipped twice (1011, ambiguous) and one with no order (9999, unmatched)
                Exec(db, """
                    CREATE TABLE dbo.Shipments (ShipmentId int NOT NULL PRIMARY KEY, OrderId int NOT NULL, Carrier nvarchar(40) NOT NULL, ShippedAt datetime2(0) NULL);
                    INSERT INTO dbo.Shipments (ShipmentId, OrderId, Carrier, ShippedAt) VALUES
                      (1, 1001, N'north', '2026-01-02'), (2, 1002, N'north', '2026-01-02'), (3, 1003, N'south', '2026-01-03'),
                      (4, 1004, N'south', '2026-01-03'), (5, 1005, N'north', '2026-01-04'), (6, 1006, N'east', NULL),
                      (7, 1007, N'east', '2026-01-05'), (8, 1008, N'west', '2026-01-05'), (9, 1009, N'west', '2026-01-06'),
                      (10, 1010, N'north', '2026-01-06'), (11, 1011, N'south', '2026-01-07'), (12, 1011, N'east', '2026-01-08'),
                      (13, 9999, N'west', '2026-01-09');
                    """);
                break;
            default:
                throw new ArgumentException($"Unknown fixture kind \"{kind}\".", nameof(kind));
        }

        Exec(db, $"CREATE USER [{fixture.Login}] FOR LOGIN [{fixture.Login}];");
        foreach (var target in grant ?? [kind == "orders" ? "dbo.Orders" : "dbo.Shipments"])
        {
            var open = target.IndexOf('(', StringComparison.Ordinal);
            var columns = open < 0 ? string.Empty : " (" + string.Join(", ", target[(open + 1)..].TrimEnd(')').Split(',').Select(c => "[" + c.Trim() + "]")) + ")";
            Exec(db, $"GRANT SELECT ON {Quote(open < 0 ? target : target[..open])}{columns} TO [{fixture.Login}];");
        }

        return fixture;
    }

    public static FixtureDatabase Attach(string server, string database, string login) => new(server, database, login);

    /// <summary>Drops the fixture database and login.</summary>
    public void Drop() => DropNamed(Server, Database, Login);

    public static void DropNamed(string server, string database, string login)
    {
        if (!database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || !login.StartsWith(LoginPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only fixture databases and logins are dropped.");
        }

        SqlConnection.ClearAllPools();
        Exec(AdminConnectionString(server: server), $"""
            IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END;
            IF SUSER_ID(N'{login}') IS NOT NULL BEGIN
              DECLARE @k nvarchar(max) = N'';
              SELECT @k += N'KILL ' + CAST(session_id AS nvarchar(10)) + N';' FROM sys.dm_exec_sessions WHERE login_name = N'{login}';
              EXEC (@k);
              DROP LOGIN [{login}];
            END;
            """);
    }

    /// <summary>Drops every fixture database and login on the server (leftovers of interrupted runs).</summary>
    public static int DropAll(string? server = null)
    {
        var target = server ?? DefaultServer;
        var names = new List<(string Kind, string Name)>();
        using (var c = new SqlConnection(AdminConnectionString(server: target)))
        {
            c.Open();
            using var cmd = new SqlCommand(
                $"SELECT 'db', name FROM sys.databases WHERE name LIKE '{DatabasePrefix}%' UNION ALL SELECT 'login', name FROM sys.server_principals WHERE name LIKE '{LoginPrefix}%'", c);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                names.Add((r.GetString(0), r.GetString(1)));
            }
        }

        SqlConnection.ClearAllPools();
        foreach (var (kind, name) in names)
        {
            Exec(AdminConnectionString(server: target), kind == "db"
                ? $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];"
                : $"DECLARE @k nvarchar(max) = N''; SELECT @k += N'KILL ' + CAST(session_id AS nvarchar(10)) + N';' FROM sys.dm_exec_sessions WHERE login_name = N'{name}'; EXEC (@k); DROP LOGIN [{name}];");
        }

        return names.Count;
    }

    public void RenameColumn(string table, string from, string to)
        => Exec(AdminConnectionString(Database, Server), $"EXEC sp_rename N'{table}.{from}', N'{to}', N'COLUMN';");

    public long RowCount(string table)
    {
        using var c = new SqlConnection(AdminConnectionString(Database, Server));
        c.Open();
        using var cmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM {Quote(table)}", c);
        return (long)cmd.ExecuteScalar()!;
    }

    public bool TableExists(string table)
    {
        using var c = new SqlConnection(AdminConnectionString(Database, Server));
        c.Open();
        using var cmd = new SqlCommand("SELECT OBJECT_ID(@t, 'U')", c);
        cmd.Parameters.AddWithValue("@t", table);
        return cmd.ExecuteScalar() is not DBNull and not null;
    }

    /// <summary>The fixture principal's own connection string, bypassing SQL Investigator entirely.</summary>
    public string PrincipalConnectionString(string? password = null) => new SqlConnectionStringBuilder
    {
        DataSource = Server,
        InitialCatalog = Database,
        UserID = Login,
        Password = password ?? Secret,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = true,
        ConnectTimeout = 15,
        Pooling = false,
    }.ConnectionString;

    /// <summary>Runs a statement directly as the fixture principal. Returns the SQL Server error number, or null if it succeeded.</summary>
    public (int? Error, string? Message) ExecuteAsPrincipal(string sql)
    {
        try
        {
            using var c = new SqlConnection(PrincipalConnectionString());
            c.Open();
            using var cmd = new SqlCommand(sql, c) { CommandTimeout = 30 };
            cmd.ExecuteNonQuery();
            return (null, null);
        }
        catch (SqlException e)
        {
            return (e.Number, e.Message);
        }
    }

    private static string Quote(string twoPart)
        => string.Join(".", twoPart.Split('.').Select(p => "[" + p.Trim('[', ']').Replace("]", "]]", StringComparison.Ordinal) + "]"));

    private static void Exec(string connectionString, string sql)
    {
        using var c = new SqlConnection(connectionString);
        c.Open();
        using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
        cmd.ExecuteNonQuery();
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,40}$")]
    private static partial Regex NamePattern();

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Server}/{Database} as {Login}");
}

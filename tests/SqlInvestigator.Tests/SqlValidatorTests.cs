using SqlInvestigator.Connections;
using SqlInvestigator.Queries;

namespace SqlInvestigator.Tests;

/// <summary>
/// The SelectOnlyAllowlist, on its own: what a bounded SELECT may be, and everything it must refuse. The scope used
/// here mirrors the acceptance fixture: dbo.Orders with CustomerEmail NOT approved, dbo.Shipments fully approved,
/// dbo.Customers not approved at all.
/// </summary>
public sealed class SqlValidatorTests
{
    private static readonly ConnectionEntry Alias = new()
    {
        Alias = "alpha",
        Server = "127.0.0.1,1433",
        Database = "Fixture",
        Authentication = "sql",
        User = "reader",
        Environment = "fixture",
        Scope = [ScopeEntry.Parse("dbo.Orders(Id,OrderId,Status,CreatedAt)"), ScopeEntry.Parse("dbo.Shipments")],
        ProvisionedAt = "2026-11-06T00:00:00Z",
    };

    private static ValidationResult Validate(string sql, int maxLength = 20_000) => SqlValidator.Validate(sql, Alias, maxLength);

    [Theory]
    [InlineData("SELECT OrderId FROM dbo.Orders")]
    [InlineData("select orderid, status from dbo.orders")]
    [InlineData("SELECT [OrderId] FROM [dbo].[Orders]")]
    [InlineData("SELECT OrderId FROM dbo.Orders;")]
    [InlineData("SELECT TOP (10) o.OrderId, o.Status FROM dbo.Orders AS o WHERE o.Status = N'delayed' ORDER BY o.CreatedAt DESC")]
    [InlineData("SELECT o.OrderId, s.Carrier FROM dbo.Orders AS o JOIN dbo.Shipments AS s ON s.OrderId = o.OrderId")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o LEFT JOIN dbo.Shipments AS s ON s.OrderId = o.OrderId WHERE s.ShipmentId IS NULL")]
    [InlineData("SELECT Status, COUNT(*) AS n FROM dbo.Orders GROUP BY Status HAVING COUNT(*) > 1 ORDER BY n DESC")]
    [InlineData("SELECT COUNT_BIG(*) FROM dbo.Orders")]
    [InlineData("WITH late AS (SELECT OrderId, CreatedAt FROM dbo.Orders WHERE Status = 'delayed') SELECT OrderId FROM late WHERE CreatedAt > '2026-01-01'")]
    [InlineData("SELECT OrderId, ROW_NUMBER() OVER (PARTITION BY Status ORDER BY CreatedAt) AS rn FROM dbo.Orders")]
    [InlineData("SELECT OrderId FROM dbo.Orders WHERE OrderId IN (SELECT OrderId FROM dbo.Shipments)")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o WHERE EXISTS (SELECT 1 FROM dbo.Shipments AS s WHERE s.OrderId = o.OrderId)")]
    [InlineData("SELECT t.OrderId FROM (SELECT OrderId FROM dbo.Orders WHERE Status LIKE 'del%') AS t")]
    [InlineData("SELECT OrderId FROM dbo.Orders ORDER BY OrderId OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY")]
    [InlineData("SELECT OrderId FROM dbo.Orders UNION ALL SELECT OrderId FROM dbo.Shipments")]
    [InlineData("SELECT CASE WHEN Status = 'shipped' THEN 1 ELSE 0 END AS done, CAST(OrderId AS varchar(10)) AS k FROM dbo.Orders")]
    [InlineData("SELECT DATEADD(day, 1, CreatedAt) AS d, DATEDIFF(hour, CreatedAt, SYSUTCDATETIME()) AS h FROM dbo.Orders")]
    [InlineData("SELECT COALESCE(Status, 'none') AS s, NULLIF(OrderId, 0) AS o, IIF(OrderId > 1, 1, 0) AS b FROM dbo.Orders")]
    [InlineData("SELECT OrderId AS Ref FROM dbo.Orders ORDER BY Ref")]
    [InlineData("SELECT * FROM dbo.Shipments")]
    [InlineData("SELECT s.* FROM dbo.Shipments AS s")]
    [InlineData("SELECT 1")]
    [InlineData("SELECT Status, STRING_AGG(CAST(OrderId AS varchar(12)), ',') WITHIN GROUP (ORDER BY OrderId) AS ids FROM dbo.Orders GROUP BY Status")]
    public void Accepts_a_bounded_select_within_scope(string sql)
    {
        var result = Validate(sql);
        Assert.True(result.Accepted, string.Join(" | ", result.Reasons));
        Assert.Empty(result.Reasons);
    }

    /// <summary>The write and bypass corpus of b03-reject-writes, verbatim.</summary>
    [Theory]
    [InlineData("INSERT INTO dbo.Orders DEFAULT VALUES")]
    [InlineData("UPDATE dbo.Orders SET OrderId = 2")]
    [InlineData("DELETE FROM dbo.Orders")]
    [InlineData("MERGE dbo.Orders AS t USING dbo.Orders AS s ON 1=0 WHEN NOT MATCHED THEN INSERT DEFAULT VALUES;")]
    [InlineData("CREATE TABLE dbo.Evil (Id int)")]
    [InlineData("DROP TABLE dbo.Orders")]
    [InlineData("ALTER TABLE dbo.Orders ADD Evil int")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT OrderId INTO dbo.Copy FROM dbo.Orders")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("TRUNCATE TABLE dbo.Orders")]
    [InlineData("GRANT SELECT ON dbo.Orders TO public")]
    [InlineData("BULK INSERT dbo.Orders FROM 'c:\\temp\\x.csv'")]
    [InlineData("WITH x AS (SELECT OrderId FROM dbo.Orders) DELETE FROM x")]
    [InlineData("SELECT OrderId FROM dbo.Orders -- trailing comment\nDROP TABLE dbo.Orders")]
    [InlineData("/* comment */ UPDATE dbo.Orders SET OrderId = 3")]
    [InlineData("SELECT OrderId FROM (SELECT OrderId FROM dbo.Orders) AS t; EXEC('DROP TABLE dbo.Orders')")]
    [InlineData("EXEC('SELECT 1')")]
    [InlineData("DECLARE @s nvarchar(100) = N'DROP TABLE dbo.Orders'; EXEC sp_executesql @s")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("WAITFOR DELAY '00:01:00'")]
    [InlineData("SELECT * FROM [linked].[db].[dbo].[Orders]")]
    [InlineData("SELECT * FROM OPENQUERY(linked, 'SELECT 1')")]
    [InlineData("SELECT * FROM OtherDb.dbo.Orders")]
    [InlineData("SHUTDOWN")]
    public void Rejects_every_write_and_bypass(string sql) => AssertRejected(sql);

    [Theory]
    // parser ambiguity: a column with QUOTED_IDENTIFIER ON, a string with it OFF
    [InlineData("SELECT \"OrderId\" FROM dbo.Orders", "ambiguous")]
    // objects outside the scope
    [InlineData("SELECT Id FROM dbo.Customers", "outside the approved scope")]
    [InlineData("SELECT OrderId FROM dbo.Orders WHERE OrderId = (SELECT MAX(Id) FROM dbo.Customers)", "outside the approved scope")]
    [InlineData("SELECT OrderId FROM dbo.Orders UNION SELECT Id FROM dbo.Customers", "outside the approved scope")]
    [InlineData("SELECT OrderId FROM Orders", "not schema-qualified")]
    [InlineData("SELECT name FROM sys.objects", "outside the approved scope")]
    [InlineData("SELECT name FROM master.dbo.sysdatabases", "another database")]
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o JOIN OtherDb.dbo.Shipments AS s ON s.OrderId = o.OrderId", "another database")]
    // columns outside the scope, however they are reached
    [InlineData("SELECT CustomerEmail FROM dbo.Orders", "CustomerEmail")]
    [InlineData("SELECT [CustomerEmail] FROM [dbo].[Orders]", "CustomerEmail")]
    [InlineData("SELECT o.CustomerEmail FROM dbo.Orders AS o", "CustomerEmail")]
    [InlineData("SELECT dbo.Orders.CustomerEmail FROM dbo.Orders", "CustomerEmail")]
    [InlineData("SELECT OrderId FROM dbo.Orders WHERE CustomerEmail LIKE '%@example.test'", "CustomerEmail")]
    [InlineData("SELECT OrderId FROM dbo.Orders ORDER BY CustomerEmail", "CustomerEmail")]
    [InlineData("SELECT LEN(CustomerEmail) AS n FROM dbo.Orders", "CustomerEmail")]
    [InlineData("SELECT * FROM dbo.Orders", "SELECT *")]
    [InlineData("SELECT o.* FROM dbo.Orders AS o", "reads every column")]
    [InlineData("WITH x AS (SELECT * FROM dbo.Orders) SELECT OrderId FROM x", "SELECT *")]
    [InlineData("SELECT t.CustomerEmail FROM (SELECT CustomerEmail FROM dbo.Orders) AS t", "CustomerEmail")]
    // an unqualified name in a subquery can resolve outward to the restricted table
    [InlineData("SELECT o.OrderId FROM dbo.Orders AS o WHERE EXISTS (SELECT 1 FROM dbo.Shipments AS s WHERE CustomerEmail = 'x')", "CustomerEmail")]
    // functions, variables and other constructs outside the allowlist
    [InlineData("SELECT dbo.fn(OrderId) FROM dbo.Orders", "user-defined")]
    [InlineData("SELECT USER_NAME() AS u", "not on the list")]
    [InlineData("SELECT OBJECT_ID('dbo.Orders') AS o", "not on the list")]
    [InlineData("SELECT @@VERSION AS v", "variable")]
    [InlineData("SELECT OrderId FROM dbo.Orders WHERE OrderId = @id", "variable")]
    [InlineData("SELECT OrderId FROM dbo.Orders WITH (NOLOCK)", "table hint")]
    [InlineData("SELECT OrderId FROM dbo.Orders OPTION (MAXDOP 1)", "query hint")]
    [InlineData("SELECT OrderId FROM dbo.Orders FOR XML PATH", "FOR clause")]
    [InlineData("SELECT OrderId FROM dbo.Orders TABLESAMPLE (10 PERCENT)", "TABLESAMPLE")]
    [InlineData("SELECT a.x FROM OPENROWSET('SQLNCLI', 'Server=(local);Trusted_Connection=yes;', 'SELECT 1 AS x') AS a", "OPENROWSET")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=x').db.dbo.t", "OPENDATASOURCE")]
    [InlineData("SELECT value FROM OPENJSON('[1]')", "OPENJSON")]
    [InlineData("SELECT NEXT VALUE FOR dbo.OrderIds AS n", "NextValueForExpression")]
    [InlineData("SELECT OrderId FROM dbo.Orders FOR SYSTEM_TIME ALL", "")]
    // not SQL at all
    [InlineData("SELEC OrderId FRM dbo.Orders", "does not parse")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    public void Rejects_with_a_reason(string sql, string reason)
    {
        var result = AssertRejected(sql);
        Assert.Contains(result.Reasons, r => r.Contains(reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_a_statement_over_the_length_limit()
    {
        var result = AssertRejected("SELECT OrderId FROM dbo.Orders WHERE OrderId > 1", maxLength: 20);
        Assert.Contains(result.Reasons, r => r.Contains("limit", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_the_base_objects_an_accepted_query_reads()
    {
        var result = Validate("SELECT o.OrderId FROM dbo.Orders AS o JOIN dbo.Shipments AS s ON s.OrderId = o.OrderId");
        Assert.True(result.Accepted);
        Assert.Equal(["dbo.Orders", "dbo.Shipments"], result.Objects);
    }

    [Fact]
    public void A_fully_approved_table_allows_every_column()
    {
        Assert.True(Validate("SELECT ShipmentId, Carrier, ShippedAt FROM dbo.Shipments").Accepted);
    }

    private static ValidationResult AssertRejected(string sql, int maxLength = 20_000)
    {
        var result = Validate(sql, maxLength);
        Assert.False(result.Accepted, $"accepted: {sql}");
        Assert.NotEmpty(result.Reasons);
        Assert.Empty(result.Objects);
        return result;
    }
}

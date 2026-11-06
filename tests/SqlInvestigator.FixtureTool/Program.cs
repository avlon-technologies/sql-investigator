using System.Text.Json;
using SqlInvestigator.Testing;

// Fixture commands for continuum/acceptance/adapter.mjs. Each prints one JSON object on stdout.
// Test tooling only: it works on SqlInvFixture_* databases on the local test server and nothing else.
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i + 1 < args.Length; i += 2)
{
    options[args[i].TrimStart('-')] = args[i + 1];
}

string Arg(string name) => options.TryGetValue(name, out var v) ? v : throw new ArgumentException($"--{name} is required");
FixtureDatabase Attached() => FixtureDatabase.Attach(options.GetValueOrDefault("server", FixtureDatabase.DefaultServer), Arg("database"), Arg("login"));

try
{
    object result = (args.Length > 0 ? args[0] : "help") switch
    {
        "create" => Describe(FixtureDatabase.Create(Arg("name"), Arg("kind"),
            options.TryGetValue("grant", out var g) ? g.Split(';', StringSplitOptions.RemoveEmptyEntries) : null)),
        "drop" => Done(() => Attached().Drop()),
        "drop-all" => new { ok = true, dropped = FixtureDatabase.DropAll() },
        "rename-column" => Done(() => Attached().RenameColumn(Arg("table"), Arg("from"), Arg("to"))),
        "row-count" => new { ok = true, rows = Attached().RowCount(Arg("table")) },
        "table-exists" => new { ok = true, exists = Attached().TableExists(Arg("table")) },
        "exec-as" => ExecAs(Attached(), Arg("sql")),
        "vault-check" => VaultCheck(Arg("file"), Arg("alias"), Arg("expected")),
        "acl-check" => new { ok = true, currentUserOnly = VaultInspector.IsCurrentUserOnly(Arg("path")) },
        "scan" => new { ok = true, files = VaultInspector.FilesContaining(Arg("root"), Arg("value")) },
        _ => throw new ArgumentException("commands: create, drop, drop-all, rename-column, row-count, table-exists, exec-as, vault-check, acl-check, scan"),
    };
    Console.WriteLine(JsonSerializer.Serialize(result));
    return 0;
}
catch (Exception e)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = $"{e.GetType().Name}: {e.Message}" }));
    return 1;
}

static object Describe(FixtureDatabase f) => new { ok = true, server = f.Server, database = f.Database, login = f.Login };

static object Done(Action a)
{
    a();
    return new { ok = true };
}

static object ExecAs(FixtureDatabase f, string sql)
{
    var (error, message) = f.ExecuteAsPrincipal(sql);
    return new { ok = true, succeeded = error is null, error, message };
}

static object VaultCheck(string file, string alias, string expected)
{
    var blob = File.ReadAllBytes(file);
    return new
    {
        ok = true,
        dpapiCurrentUser = VaultInspector.IsDpapiCurrentUserBlob(blob),
        decryptsForAlias = VaultInspector.DecryptsForAlias(blob, alias, expected),
        fileCurrentUserOnly = VaultInspector.IsCurrentUserOnly(file),
        directoryCurrentUserOnly = VaultInspector.IsCurrentUserOnly(Path.GetDirectoryName(file)!),
    };
}

using System.Text.Json;
using DrawAudit.Hierarchy;
using Microsoft.Data.Sqlite;

// Thin CLI over HierarchyCore: build one Sub's hierarchy and print / export it.
if (args.Length < 3 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: HierarchyBuilder --db <store.sqlite> --sub <subId> [--json <out.json>]");
    return 1;
}

string? dbPath = null, sub = null, jsonOut = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--db") dbPath = args[i + 1];
    else if (args[i] == "--sub") sub = args[i + 1];
    else if (args[i] == "--json") jsonOut = args[i + 1];
}
if (dbPath is null || sub is null)
{
    Console.Error.WriteLine("--db and --sub are required.");
    return 1;
}

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

var store = Store.Load(conn, sub);
if (store.Dl is null)
{
    Console.Error.WriteLine($"No DL header found for sub '{sub}'.");
    return 1;
}

var rows = new List<HierRow>();
var warnings = new List<Issue>();
new Builder(store, rows, warnings).Build();

if (jsonOut is not null)
{
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {jsonOut}");
}

Console.WriteLine($"Sub {sub}  |  L1={store.Dl.AssemblyDrawings}  EXC={store.Dl.ExcClass}  paint={store.Dl.PaintingSystem}");
Console.WriteLine($"Rows: {rows.Count}  (levels: {string.Join(", ", rows.GroupBy(r => r.Level).OrderBy(g => g.Key).Select(g => $"L{g.Key}:{g.Count()}"))})\n");
Console.WriteLine($"{"Lvl",3} | {"Drawing/Part",-42} | {"Rev",5} | {"K",4} | {"Tot",5} | {"Scope",-17} | ISO");
foreach (var r in rows)
{
    string label = new string(' ', (r.Level - 1) * 2) + (r.DrawingNo ?? r.PartName);
    Console.WriteLine($"{r.Level,3} | {Trunc(label, 42),-42} | {r.Revision,5} | {r.QtyPerParent,4} | {r.TotalQty,5} | {r.Scope,-17} | {r.Iso5817}");
}
if (warnings.Count > 0)
{
    Console.WriteLine($"\nWarnings / Errors ({warnings.Count}):");
    foreach (var w in warnings) Console.WriteLine($"  - {w}");
}

return 0;

static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "\u2026";

using DrawAudit.Pipeline;

// Applies the inventory revision-selection rules (see Pipeline.Inventory) and writes the Yes/No
// audit trail to the store.
if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: InventoryReader <inventory.xlsm> [--db <store.sqlite>] [--sub <subId>[,<subId>...]]");
    return 1;
}

string path = args[0];
string? dbPath = null, subFilter = null;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--db") dbPath = args[i + 1];
    else if (args[i] == "--sub") subFilter = args[i + 1];
}

var rows = Inventory.Read(path);
if (Inventory.ParseSubList(subFilter) is { } subSet)
    rows = rows.Where(r => subSet.Contains(r.Sub)).ToList();

var issues = Inventory.Select(rows);
foreach (var i in issues) Console.WriteLine($"! [{i.Sub}] {i.Message}");

if (dbPath is not null)
{
    Inventory.Save(dbPath, rows);
    Console.WriteLine($"Saved {rows.Count} inventory rows to {dbPath}\n");
}

Console.WriteLine($"Rows: {rows.Count}  |  selected: {rows.Count(r => r.Selected)}  |  not: {rows.Count(r => !r.Selected)}");
foreach (var g in rows.GroupBy(r => r.Sub).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key,-10}  files:{g.Count(),4}  selected:{g.Count(r => r.Selected),4}");

if (subFilter is not null)
{
    Console.WriteLine($"\n{"Sel",3} | {"Cat",-7} | {"DocId",-26} | {"Rev",4} | File");
    foreach (var r in rows.OrderBy(r => r.Category).ThenBy(r => r.DocId).ThenBy(r => r.FileName))
        Console.WriteLine($"{(r.Selected ? "Yes" : "No"),3} | {r.Category,-7} | {Trunc(r.DocId, 26),-26} | {r.Revision,4} | {r.FileName}   [{r.Reason}]");
}

return 0;

static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "\u2026";

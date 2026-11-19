using System.Text.Json;
using DrawAudit.Pipeline;

// Reads a Delivery List workbook (see Pipeline.DlReader) and optionally stores / exports it.
if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: DlReader <dl.xlsx> [--json <out.json>] [--db <store.sqlite>]");
    return 1;
}

string path = args[0];
string? jsonOut = null, dbPath = null;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--json") jsonOut = args[i + 1];
    else if (args[i] == "--db") dbPath = args[i + 1];
}

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

var result = DlReader.Read(path);

if (jsonOut is not null)
{
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {jsonOut}");
}

if (dbPath is not null)
{
    DlStore.Save(dbPath, Path.GetFullPath(path), result);
    Console.WriteLine($"Saved DL to {dbPath}");
}

Console.WriteLine($"Sub              : {result.Sub}");
Console.WriteLine($"Assembly Dwg (L1): {string.Join(", ", result.AssemblyDrawings.Select(a => $"{a.DrawingNo}:{a.Revision}"))}");
Console.WriteLine($"Description       : {result.SubDescription}");
Console.WriteLine($"Rev letter        : {result.RevLetter}");
Console.WriteLine($"ITP manufacturing : {result.ItpManufacturing}");
Console.WriteLine($"ITP construction  : {result.ItpConstruction}");
Console.WriteLine($"EXC class (P)     : {result.ExcClass}");
Console.WriteLine($"Painting sys (Q)  : {result.PaintingSystem}");
Console.WriteLine($"Color (S)         : {result.Color ?? "-"}");
Console.WriteLine($"Delivery items    : {result.DeliveryItems.Count}");
Console.WriteLine($"{"Item",4} | {"Qty",4} | {"Drawing No",-12} | Description");
foreach (var it in result.DeliveryItems)
    Console.WriteLine($"{it.ItemNr,4} | {it.Quantity,4} | {it.DrawingNo,-12} | {it.Description}");

return 0;

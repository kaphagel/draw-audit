using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;

// Probe a DWG: report version, entity-type histogram, any TABLE objects, block
// references with attributes, and a sample of TEXT/MTEXT content. This tells us
// whether the BOM/title block are structured objects or just loose text.
if (args.Length < 1)
{
    Console.WriteLine("Usage: DwgProbe <file.dwg>");
    return 1;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Not found: {path}");
    return 1;
}

CadDocument doc = DwgReader.Read(path);
Console.WriteLine($"File     : {Path.GetFileName(path)}");
Console.WriteLine($"Version  : {doc.Header.Version}");

var entities = doc.Entities.ToList();
Console.WriteLine($"Entities : {entities.Count}");
Console.WriteLine("--- entity type histogram ---");
foreach (var g in entities.GroupBy(e => e.GetType().Name).OrderByDescending(g => g.Count()))
    Console.WriteLine($"{g.Count(),6}  {g.Key}");

Console.WriteLine("--- layer histogram ---");
foreach (var g in entities.GroupBy(e => e.Layer?.Name ?? "(none)").OrderByDescending(g => g.Count()))
    Console.WriteLine($"{g.Count(),6}  {g.Key}");

// Show which layers weld-related text lives on.
Console.WriteLine("--- weld-related text -> layer ---");
foreach (var e in entities)
{
    string? val = e switch { TextEntity t => t.Value, MText m => m.Value, _ => null };
    if (val is not null && System.Text.RegularExpressions.Regex.IsMatch(val, "weld|NDT|^a ?\\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        Console.WriteLine($"  [{e.Layer?.Name}] {val.Replace("\n", " ").Trim()}");
}

// TABLE objects (structured BOM would live here).
var tables = entities.OfType<TableEntity>().ToList();
Console.WriteLine($"\nTABLE entities: {tables.Count}");

// Block references + their attributes (structured title block / BOM often uses these).
var inserts = entities.OfType<Insert>().ToList();
Console.WriteLine($"INSERT (block refs): {inserts.Count}");
var withAttrs = inserts.Where(i => i.Attributes.Any()).ToList();
Console.WriteLine($"  with attributes  : {withAttrs.Count}");
foreach (var ins in withAttrs.Take(6))
{
    Console.WriteLine($"  block '{ins.Block?.Name}':");
    foreach (var a in ins.Attributes.Take(12))
        Console.WriteLine($"    {a.Tag,-20} = {a.Value}");
}

// Sample of raw text so we can see the title block / BOM words.
var texts = new List<string>();
foreach (var e in entities)
{
    if (e is TextEntity t && !string.IsNullOrWhiteSpace(t.Value)) texts.Add(t.Value.Trim());
    else if (e is MText m && !string.IsNullOrWhiteSpace(m.Value)) texts.Add(m.Value.Trim());
}
Console.WriteLine($"\nTEXT/MTEXT count: {texts.Count}");
Console.WriteLine("--- first 40 text values ---");
foreach (var s in texts.Take(40))
    Console.WriteLine($"  {s.Replace("\n", " ")}");

return 0;

using System.Text.Json;
using DrawAudit.Extraction;
using DrawAudit.Pipeline;

// Thin CLI over ExtractionCore. Extracts drawing title block + BOM (via the strategy chains)
// and persists to the SQLite store. The extraction logic lives in ExtractionCore so new
// drawing templates are added as strategies there, not here.
if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: DrawingExtractor <file.pdf|folder> [--files <list.txt>] [--json <out.json>] [--db <store.sqlite>]");
    return 1;
}

string path = args[0];
string? jsonOut = null, dbPath = null, filesList = null;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--json") jsonOut = args[i + 1];
    else if (args[i] == "--db") dbPath = args[i + 1];
    else if (args[i] == "--files") filesList = args[i + 1];
}

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };

// Diagnostic: dump each page's lines (word@x) to inspect an unrecognized layout.
if (args.Contains("--dump"))
{
    foreach (var pg in DrawingReader.LoadPages(path))
    {
        Console.WriteLine($"--- page {pg.Number} {pg.Width:F0}x{pg.Height:F0} ---");
        foreach (var l in pg.Lines)
            Console.WriteLine($"[y={l.Y,6:F0}] " + string.Join(" ", l.Words.Select(w => $"{w.Text}@{w.Left:F0}")));
    }
    return 0;
}

// Batch mode: a folder (recursively, skipping Old) or an explicit --files list of PDFs.
if (filesList is not null || Directory.Exists(path))
{
    var pdfs = filesList is not null
        ? File.ReadAllLines(filesList).Select(l => l.Trim()).Where(l => l.Length > 0 && File.Exists(l)).ToList()
        : Directory.EnumerateFiles(path, "*.pdf", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(seg => seg.Equals("Old", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p).ToList();

    var results = new List<DrawingResult>();
    foreach (var f in pdfs)
    {
        try { results.Add(DrawingReader.Extract(f)); }
        catch (Exception ex) { Console.Error.WriteLine($"ERROR {Path.GetFileName(f)}: {ex.Message}"); }
    }

    if (jsonOut is not null)
    {
        File.WriteAllText(jsonOut, JsonSerializer.Serialize(results, jsonOpts));
        Console.WriteLine($"Wrote {jsonOut} ({results.Count} drawings)\n");
    }
    if (dbPath is not null)
    {
        DrawingStore.Save(dbPath, results);
        Console.WriteLine($"Saved {results.Count} drawings to {dbPath}\n");
    }

    Console.WriteLine($"{"Drawing",-12} {"Rev",-4} {"Page",-6} {"BOM",4} {"Sub",4}  Title");
    foreach (var r in results)
        Console.WriteLine($"{r.DrawingNo,-12} {r.Revision,-4} {r.Page,-6} {r.Bom.Count,4} {r.Bom.Count(b => b.IsSubDrawing),4}  {r.Title}");
    Console.WriteLine($"\n{results.Count} drawings, {results.Sum(r => r.Bom.Count)} BOM rows total.");
    return 0;
}

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

var result = DrawingReader.Extract(path);

if (jsonOut is not null)
{
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(result, jsonOpts));
    Console.WriteLine($"Wrote {jsonOut}");
}
if (dbPath is not null)
{
    DrawingStore.Save(dbPath, new[] { result });
    Console.WriteLine($"Saved to {dbPath}");
}

Console.WriteLine($"Drawing No : {result.DrawingNo}");
Console.WriteLine($"Title      : {result.Title}");
Console.WriteLine($"Page (H)   : {result.Page}");
Console.WriteLine($"Revision(I): {result.Revision}");
Console.WriteLine($"Tot.Weight : {result.TotalWeight}");
Console.WriteLine($"ISO 5817(W): {result.Iso5817Class ?? "-"}  | NDT text: {result.MentionsNdt} | Seal weld text: {result.MentionsSealWeld}");
Console.WriteLine($"BOM rows   : {result.Bom.Count}");
Console.WriteLine($"{"Amt",3} | {"Det",4} | {"Drawing No",-12} | {"Description",-28} | {"Specification",-28} | {"Wt",6} | Sub?");
foreach (var r in result.Bom)
    Console.WriteLine($"{r.Amount,3} | {r.DetNo,4} | {r.DrawingNo,-12} | {Trunc(r.Description, 28),-28} | {Trunc(r.Specification, 28),-28} | {r.NetWeight,6} | {(r.IsSubDrawing ? "yes" : "")}");

return 0;

static string Trunc(string? s, int n) => (s ?? "").Length <= n ? (s ?? "") : (s ?? "")[..(n - 1)] + "\u2026";

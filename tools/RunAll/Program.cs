using DrawAudit.Pipeline;

// End-to-end batch driver over the Pipeline library (runs in-process): scan the documents folder
// (optionally cross-checked against an inventory), extract the selected drawings + DLs per Sub, and
// write the summary workbook.
if (args.Length < 2 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: RunAll --root <sourceDir> --out <out.xlsx> [--inventory <inv.xlsm>] [--template <template.xlsx>] [--db <store.sqlite>] [--sub <subId>[,<subId>...]] [--config <appsettings.json>]");
    Console.WriteLine("  --template overrides the summary template built into the application (for testing).");
    return 1;
}

string? inventory = null, root = null, template = null, outPath = null, subFilter = null, config = null;
string db = "store.sqlite";
for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--inventory": inventory = args[i + 1]; break;
        case "--root": root = args[i + 1]; break;
        case "--template": template = args[i + 1]; break;
        case "--out": outPath = args[i + 1]; break;
        case "--db": db = args[i + 1]; break;
        case "--sub": subFilter = args[i + 1]; break;
        case "--config": config = args[i + 1]; break;
    }
}
if (root is null || outPath is null)
{
    Console.Error.WriteLine("--root and --out are required.");
    return 1;
}

var options = new PipelineOptions(inventory, root, template, outPath, db, Inventory.ParseSubList(subFilter), config);
var progress = new SyncProgress(p =>
{
    if (p.Message.Length == 0) return;
    if (p.Message.TrimStart().StartsWith('!')) Console.Error.WriteLine(p.Message);
    else Console.WriteLine(p.Message.StartsWith("===") ? "\n" + p.Message : p.Message);
});

try
{
    var result = PipelineRunner.Run(options, progress);
    Console.WriteLine($"\nDONE. Workbook: {result.OutputPath}  ({result.Subs.Count} Subs, {result.TotalWarnings} warnings{(result.Errors.Count > 0 ? $", {result.Errors.Count} errors" : "")})");
    return result.Errors.Count == 0 ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nFAILED: {ex.Message}");
    return 1;
}

// Reports synchronously on the calling thread (Progress<T> would post to the thread pool and
// reorder console output).
sealed class SyncProgress(Action<PipelineProgress> handler) : IProgress<PipelineProgress>
{
    readonly object _lock = new();
    public void Report(PipelineProgress value) { lock (_lock) handler(value); }
}

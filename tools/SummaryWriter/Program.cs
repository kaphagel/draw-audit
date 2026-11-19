using DrawAudit.Pipeline;

// Writes the summary workbook from an existing store (see Pipeline.SummaryWorkbook).
if (args.Length < 2 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: SummaryWriter --db <store.sqlite> --out <out.xlsx> [--template <template.xlsx>] [--sub <subId>[,<subId>...]] [--config <appsettings.json>]");
    return 1;
}

string? dbPath = null, template = null, outPath = null, onlySub = null, configPath = null;
for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--db": dbPath = args[i + 1]; break;
        case "--template": template = args[i + 1]; break;
        case "--out": outPath = args[i + 1]; break;
        case "--sub": onlySub = args[i + 1]; break;
        case "--config": configPath = args[i + 1]; break;
    }
}
if (dbPath is null || outPath is null)
{
    Console.Error.WriteLine("--db and --out are required.");
    return 1;
}

try
{
    var config = SummaryConfig.Load(configPath);
    SummaryWorkbook.Write(dbPath, template, outPath, Inventory.ParseSubList(onlySub), config, Console.WriteLine);
    Console.WriteLine($"\nSaved {outPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

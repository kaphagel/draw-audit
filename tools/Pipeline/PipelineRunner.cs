using DrawAudit.Extraction;

namespace DrawAudit.Pipeline;

public sealed record PipelineOptions(
    string? InventoryPath,                       // optional: cross-check the folder scan against it
    string SourceRoot,
    string? TemplatePath,                        // null = the summary template built into the application
    string OutputPath,
    string? DbPath = null,                       // null = temporary store, deleted afterwards
    IReadOnlyCollection<string>? Subs = null,    // null/empty = all Subs found in the folder
    string? ConfigPath = null);                  // null = appsettings.json next to the executable

// Percent < 0 means "unchanged"; an empty Message means "no log line" (progress tick only).
public sealed record PipelineProgress(double Percent, string Message);

public sealed record PipelineResult(string OutputPath, List<SubResult> Subs, List<string> UnknownSubs, List<string> Errors)
{
    public int TotalWarnings => Subs.Sum(s => s.Warnings);
}

// A Sub folder found in the documents folder. Processable = it has DL or drawing files to extract;
// InInventory is false only when an inventory was given and does not list the Sub. DlWarning is set
// when the Sub holds several DLs (revisions or differently named DLs) and names the one used.
public sealed record SubInfo(string Sub, string Folder, int Drawings, int Dls, bool InInventory, string? DlWarning = null)
{
    public bool Processable => Drawings + Dls > 0;
}

// End-to-end extraction: scan the documents folder (optionally cross-checked against an inventory)
// -> select the latest revisions -> extract the selected drawings and DLs per Sub -> write the
// summary workbook. Used by both the RunAll CLI and the WPF app.
public static class PipelineRunner
{
    public static PipelineResult Run(PipelineOptions o, IProgress<PipelineProgress>? progress = null, CancellationToken ct = default)
    {
        void Report(double pct, string msg) => progress?.Report(new PipelineProgress(pct, msg));

        var config = SummaryConfig.Load(o.ConfigPath);
        if (CheckOutputWritable(o.OutputPath) is string err) throw new IOException(err);

        bool tempDb = o.DbPath is null;
        string db = o.DbPath ?? Path.Combine(Path.GetTempPath(), $"draw_audit_{Guid.NewGuid():N}.sqlite");
        DeleteDb(db);

        var errors = new List<string>();
        try
        {
            // 1) File selection from the folder scan.
            Report(0, "=== File selection ===");
            var subSet = o.Subs is { Count: > 0 } ? new HashSet<string>(o.Subs, StringComparer.OrdinalIgnoreCase) : null;
            var rows = Inventory.Scan(o.SourceRoot);
            if (subSet is not null) rows = rows.Where(r => subSet.Contains(r.Sub)).ToList();
            var issues = Inventory.Select(rows);
            foreach (var i in issues) Report(1, $"  ! {i.Message}");
            Inventory.Save(db, rows);
            Report(1, $"Files: {rows.Count}  |  selected: {rows.Count(r => r.Selected)}  |  not: {rows.Count(r => !r.Selected)}");

            issues.AddRange(Inventory.RevisionFolderIssues(rows));
            if (issues.Count > 0) Report(1, $"  {issues.Count} file-selection warning(s) - see warnings");
            if (!string.IsNullOrEmpty(o.InventoryPath))
            {
                var inv = Inventory.Read(o.InventoryPath);
                if (subSet is not null) inv = inv.Where(r => subSet.Contains(r.Sub)).ToList();
                var diff = Inventory.Reconcile(rows, inv, o.InventoryPath);
                Report(2, $"Inventory cross-check ({Path.GetFileName(o.InventoryPath)}): {diff.Count} difference(s)");
                issues.AddRange(diff);
            }
            Inventory.SaveIssues(db, issues);

            // 2) Selected files per Sub.
            var bySub = new SortedDictionary<string, (List<string> Dls, List<string> Drawings)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows.Where(r => r.Selected && r.Sub.Length > 0))
            {
                string local = Path.GetFullPath(Path.Combine(o.SourceRoot, r.RelPath.Replace('\\', Path.DirectorySeparatorChar)));
                if (!File.Exists(local)) { errors.Add($"Missing file: {r.RelPath}"); Report(2, $"  ! missing: {r.RelPath}"); continue; }
                if (!bySub.TryGetValue(r.Sub, out var lists)) bySub[r.Sub] = lists = (new(), new());
                if (r.Category == "DL" && r.IsExcel) lists.Dls.Add(local);
                else if (r.Category == "DRAWING") lists.Drawings.Add(local);
            }

            var unknown = subSet?.Where(s => !bySub.ContainsKey(s)).OrderBy(s => s).ToList() ?? new List<string>();
            if (unknown.Count > 0) Report(2, $"  ! Sub(s) not found in the documents folder: {string.Join(", ", unknown)}");
            if (bySub.Count == 0) throw new InvalidOperationException("No matching Subs to process.");

            // 3) Extract per Sub. Progress 2% -> 90% by file count.
            int total = bySub.Values.Sum(l => l.Dls.Count + l.Drawings.Count), done = 0, subNo = 0;
            foreach (var (sub, lists) in bySub)
            {
                ct.ThrowIfCancellationRequested();
                Report(2 + 88.0 * done / total, $"=== Sub {sub}  ({lists.Drawings.Count} drawings, {lists.Dls.Count} DL)  [{++subNo}/{bySub.Count}] ===");

                // PDFs are parsed one at a time: PdfPig is not thread-safe (parallel parsing produced
                // different BOM rows from run to run).
                var results = new List<DrawingResult>();
                foreach (var file in lists.Drawings)
                {
                    ct.ThrowIfCancellationRequested();
                    try { results.Add(DrawingReader.Extract(file)); }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                        Report(-1, $"  ! ERROR {Path.GetFileName(file)}: {ex.Message}");
                    }
                    Report(2 + 88.0 * ++done / total, "");
                }
                var ok = results;
                DrawingStore.Save(db, ok);
                Report(2 + 88.0 * done / total, $"  {ok.Count} drawings, {ok.Sum(r => r.Bom.Count)} BOM rows");

                foreach (var dl in lists.Dls)
                {
                    ct.ThrowIfCancellationRequested();
                    try { DlStore.Save(db, dl, DlReader.Read(dl)); Report(2 + 88.0 * ++done / total, $"  DL {Path.GetFileName(dl)}"); }
                    catch (Exception ex) { errors.Add($"{Path.GetFileName(dl)}: {ex.Message}"); Report(-1, $"  ! ERROR {Path.GetFileName(dl)}: {ex.Message}"); done++; }
                }
            }

            // 4) Summary workbook.
            Report(90, "=== Writing summary workbook ===");
            Report(-1, $"  Template: {(string.IsNullOrEmpty(o.TemplatePath) ? "built-in" : Path.GetFileName(o.TemplatePath))} ({SummaryTemplate.Version(o.TemplatePath)})");
            var subResults = SummaryWorkbook.Write(db, o.TemplatePath, o.OutputPath, bySub.Keys.ToList(), config,
                msg => Report(-1, "  " + msg), ct);
            Report(100, $"Saved {o.OutputPath}");
            return new PipelineResult(o.OutputPath, subResults, unknown, errors);
        }
        finally
        {
            if (tempDb) DeleteDb(db);
        }
    }

    // Returns an error message when the output cannot be written (e.g. open in Excel), else null.
    public static string? CheckOutputWritable(string outputPath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (dir is null || !Directory.Exists(dir)) return $"Output folder does not exist: {dir}";
        if (!File.Exists(outputPath)) return null;
        try
        {
            using var _ = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return null;
        }
        catch (IOException)
        {
            return $"The output file is open in another program (e.g. Excel). Close it and try again:\n{outputPath}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"No permission to write the output file:\n{outputPath}";
        }
    }

    // Inventory workbook at the top of the documents folder (name contains "inventory").
    public static string? FindInventory(string root) =>
        TopFiles(root, "*.xls*").Where(f => Path.GetFileName(f).Contains("inventory", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

    // Sub folders in the documents folder with their selected file counts. When an inventory is
    // given, Subs it does not list are flagged (they are still processable from the scan).
    public static List<SubInfo> DiscoverSubs(string root, string? inventoryPath = null)
    {
        var rows = Inventory.Scan(root);
        Inventory.Select(rows);
        HashSet<string>? invSubs = string.IsNullOrEmpty(inventoryPath) ? null
            : new HashSet<string>(Inventory.Read(inventoryPath).Select(r => r.Sub), StringComparer.OrdinalIgnoreCase);

        var list = rows.Where(r => r.Sub.Length > 0).GroupBy(r => r.Sub, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SubInfo(g.Key, g.First().RelPath.Split('\\', '/')[0],
                g.Count(r => r.Selected && r.Category == "DRAWING"),
                g.Count(r => r.Selected && r.Category == "DL" && r.IsExcel),
                invSubs?.Contains(g.Key) ?? true,
                DlWarning(g)))
            .ToList();

        // Sub folders without any PDF/Excel files are listed too (not processable).
        var known = new HashSet<string>(list.Select(s => s.Sub), StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith("sub", StringComparison.OrdinalIgnoreCase)) continue;
                string sub = Inventory.SubOf(new[] { name });
                if (sub.Length > 0 && known.Add(sub)) list.Add(new SubInfo(sub, name, 0, 0, invSubs?.Contains(sub) ?? true));
            }

        return list.OrderBy(s => s.Sub, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // "3 DLs found (rev A, B, D) - using X.xlsx" when a Sub has several DL versions (revisions or
    // differently named DLs; the Excel and PDF copy of one revision count once). Old folders are ignored.
    static string? DlWarning(IEnumerable<InvRow> subRows)
    {
        var dls = subRows.Where(r => r.Category == "DL" && r.Eligible).ToList();
        var versions = dls.GroupBy(r => (r.DocId, r.Revision.ToUpperInvariant())).ToList();
        if (versions.Count <= 1) return null;

        var used = dls.FirstOrDefault(r => r.Selected);
        string revs = string.Join(", ", versions.Select(v => v.Key.Item2.Length == 0 ? "-" : v.Key.Item2).Distinct().OrderBy(x => x));
        bool renamed = versions.Select(v => v.Key.DocId).Distinct().Count() > 1;
        string ambiguous = dls.Any(r => r.Reason.EndsWith("ambiguous")) ? " - AMBIGUOUS (same revision), check" : "";
        return $"{versions.Count} DLs found (rev {revs}{(renamed ? ", different names" : "")}) - using {used?.FileName ?? "none"}{ambiguous}";
    }

    static IEnumerable<string> TopFiles(string root, string pattern) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly).Where(f => !Path.GetFileName(f).StartsWith("~$"))
            : Enumerable.Empty<string>();

    static void DeleteDb(string db)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
            if (File.Exists(db + ext)) File.Delete(db + ext);
    }
}

using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace DrawAudit.Pipeline;

// An issue found while selecting files (not from a drawing's content), reported in the Sub's
// Warnings / Errors area and on the Warnings Summary sheet.
public sealed record FileIssue(string Sub, string Type, string Item, string Source, string? SourceFile, string Message);

// One row of the authoritative inventory workbook, classified and (after Select) marked as the
// selected latest revision or not, with the reason (the Yes/No audit trail).
public sealed class InvRow(string relPath, string url, string sub, string category, string fileName, string ext,
    string docId, string revision, bool isExcel, bool eligible, bool selected, string reason)
{
    public string RelPath { get; } = relPath;
    public string Url { get; } = url;
    public string Sub { get; } = sub;
    public string Category { get; } = category;
    public string FileName { get; } = fileName;
    public string Ext { get; } = ext;
    public string DocId { get; } = docId;
    public string Revision { get; } = revision;
    public bool IsExcel { get; } = isExcel;
    public bool Eligible { get; } = eligible;
    public bool Selected { get; set; } = selected;
    public string Reason { get; set; } = reason;
}

// Builds the file list (by scanning the documents folder, or from an inventory workbook) and applies
// the revision-selection rules: ignore "Old" folders, keep only supported extensions, group files by
// document identity, select the latest revision, and prefer Excel over PDF for a DL.
public static class Inventory
{
    public static List<InvRow> Read(string path)
    {
        // Inventory workbook: relative path + URL from the row after the "Relative Path" header.
        var list = new List<InvRow>();
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();

        // Find the header row ("Relative Path" / "URL"); data starts on the next row.
        int headerRow = 5;
        foreach (var row in ws.RangeUsed()!.Rows())
        {
            if (row.Cell(1).GetString().Trim().Equals("Relative Path", StringComparison.OrdinalIgnoreCase))
            {
                headerRow = row.RowNumber();
                break;
            }
        }

        int last = ws.LastRowUsed()!.RowNumber();
        for (int r = headerRow + 1; r <= last; r++)
        {
            string rel = ws.Cell(r, 1).GetString().Trim();
            string url = ws.Cell(r, 2).GetString().Trim();
            if (rel.Length == 0) continue;
            list.Add(Classify(rel, url));
        }
        return list;
    }

    // Group eligible files by (sub, category, docId); select the latest revision, Excel over PDF for DLs.
    // Then keep a single DL per Sub (the latest revision); returns warnings for DLs set aside.
    public static List<FileIssue> Select(List<InvRow> rows)
    {
        foreach (var group in rows.Where(r => r.Eligible).GroupBy(r => (r.Sub, r.Category, r.DocId)))
        {
            int maxRank = group.Max(r => RevRank(r.Revision));
            var latest = group.Where(r => RevRank(r.Revision) == maxRank).ToList();

            if (group.Key.Category == "DL")
            {
                var excel = latest.Where(r => r.IsExcel).ToList();
                var chosen = excel.Count > 0 ? excel : latest;
                foreach (var r in group)
                {
                    bool sel = chosen.Contains(r);
                    r.Selected = sel;
                    r.Reason = sel ? (r.IsExcel ? "latest rev, Excel preferred" : "latest rev")
                        : RevRank(r.Revision) < maxRank ? "older revision" : "PDF superseded by Excel";
                }
            }
            else
            {
                // Drawings: keep every page/file of the latest revision.
                foreach (var r in group)
                {
                    r.Selected = RevRank(r.Revision) == maxRank;
                    r.Reason = r.Selected ? "latest rev" : "older revision";
                }
            }
        }

        // One DL per Sub: when differently named DL documents remain selected (e.g. a renamed DL),
        // use the one with the latest revision. Equal revisions cannot be ordered - keep the first by
        // name and report it as ambiguous.
        var issues = new List<FileIssue>();
        foreach (var sub in rows.Where(r => r.Selected && r.Category == "DL").GroupBy(r => r.Sub, StringComparer.OrdinalIgnoreCase))
        {
            var docs = sub.GroupBy(r => r.DocId).ToList();
            if (docs.Count <= 1) continue;

            var ordered = docs.OrderByDescending(d => RevRank(d.First().Revision))
                .ThenBy(d => d.First().FileName, StringComparer.OrdinalIgnoreCase).ToList();
            var used = ordered[0].First();
            foreach (var doc in ordered.Skip(1))
            {
                var r0 = doc.First();
                bool ambiguous = RevRank(r0.Revision) == RevRank(used.Revision);
                foreach (var r in doc)
                {
                    r.Selected = false;
                    r.Reason = ambiguous ? $"same revision as DL {used.FileName} - ambiguous" : $"superseded by newer DL {used.FileName}";
                }
                issues.Add(ambiguous
                    ? new FileIssue(sub.Key, "Ambiguous DL", r0.FileName, used.FileName, r0.FileName,
                        $"Several DLs with the same revision ({Rev(used.Revision)}): used {used.FileName}, not used {r0.FileName}. Check which DL is valid.")
                    : new FileIssue(sub.Key, "DL superseded", r0.FileName, used.FileName, r0.FileName,
                        $"DL {r0.FileName} (rev {Rev(r0.Revision)}) not used - the newer DL {used.FileName} (rev {Rev(used.Revision)}) is used."));
            }
        }
        return issues;

        static string Rev(string rev) => rev.Length == 0 ? "-" : rev;
    }

    public static void Save(string dbPath, List<InvRow> rows)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS inventory (
                    rel_path  TEXT PRIMARY KEY, url TEXT, sub TEXT, category TEXT, file_name TEXT, ext TEXT,
                    doc_id TEXT, revision TEXT, is_excel INTEGER, selected INTEGER, reason TEXT
                );
                DELETE FROM inventory;
                """;
            cmd.ExecuteNonQuery();
        }
        using var tx = conn.BeginTransaction();
        foreach (var r in rows)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO inventory (rel_path, url, sub, category, file_name, ext, doc_id, revision, is_excel, selected, reason)
                VALUES ($rel, $url, $sub, $cat, $fn, $ext, $doc, $rev, $xl, $sel, $reason);
                """;
            cmd.Parameters.AddWithValue("$rel", r.RelPath);
            cmd.Parameters.AddWithValue("$url", r.Url);
            cmd.Parameters.AddWithValue("$sub", r.Sub);
            cmd.Parameters.AddWithValue("$cat", r.Category);
            cmd.Parameters.AddWithValue("$fn", r.FileName);
            cmd.Parameters.AddWithValue("$ext", r.Ext);
            cmd.Parameters.AddWithValue("$doc", r.DocId);
            cmd.Parameters.AddWithValue("$rev", r.Revision);
            cmd.Parameters.AddWithValue("$xl", r.IsExcel ? 1 : 0);
            cmd.Parameters.AddWithValue("$sel", r.Selected ? 1 : 0);
            cmd.Parameters.AddWithValue("$reason", r.Reason);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static void SaveIssues(string dbPath, List<FileIssue> issues)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS file_issues (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, sub TEXT, type TEXT, item TEXT, source TEXT, source_file TEXT, message TEXT
                );
                DELETE FROM file_issues;
                """;
            cmd.ExecuteNonQuery();
        }
        using var tx = conn.BeginTransaction();
        foreach (var i in issues)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO file_issues (sub, type, item, source, source_file, message) VALUES ($s, $t, $i, $src, $f, $m)";
            cmd.Parameters.AddWithValue("$s", i.Sub);
            cmd.Parameters.AddWithValue("$t", i.Type);
            cmd.Parameters.AddWithValue("$i", i.Item);
            cmd.Parameters.AddWithValue("$src", i.Source);
            cmd.Parameters.AddWithValue("$f", (object?)i.SourceFile ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$m", i.Message);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static List<FileIssue> LoadIssues(SqliteConnection conn, string sub)
    {
        var list = new List<FileIssue>();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='file_issues'";
            if (check.ExecuteScalar() is null) return list;
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sub, type, item, source, source_file, message FROM file_issues WHERE sub=$s ORDER BY id";
        cmd.Parameters.AddWithValue("$s", sub);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new FileIssue(rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3),
                rd.IsDBNull(4) ? null : rd.GetString(4), rd.GetString(5)));
        return list;
    }

    // Sub identifier from a path's folder segments, e.g. "Sub1002-2 Casing..." -> "1002-2" (spec section 4).
    public static string SubOf(IEnumerable<string> segments)
    {
        foreach (var s in segments)
        {
            var m = Regex.Match(s, @"[Ss]ub\s*(\d[\d\-&]{0,7})");
            if (m.Success) return m.Groups[1].Value.TrimEnd('-', '&');
        }
        return "";
    }

    // Parses a comma-separated Sub list ("1002-1, 1012-1&2"); null/empty means all Subs.
    public static HashSet<string>? ParseSubList(string? list)
    {
        if (string.IsNullOrWhiteSpace(list)) return null;
        return new HashSet<string>(list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    // Builds the file list by scanning the documents folder: every Sub folder ("Sub..."/"sub...") at
    // the root, recursively. Files in the root itself are ignored (spec section 3). Listed are all
    // files under DL/PDF folders (so Old/unsupported files appear in the audit trail as "No") plus
    // PDF/Excel files elsewhere in the Sub (e.g. revision folders), which are never selected.
    public static List<InvRow> Scan(string root)
    {
        var list = new List<InvRow>();
        string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (!Path.GetFileName(dir).StartsWith("sub", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(file).StartsWith("~$")) continue; // Office lock files
                string rel = Path.GetFullPath(file)[full.Length..].Replace('/', '\\');
                var row = Classify(rel, "");
                bool supported = row.Ext is ".pdf" or ".xlsx" or ".xlsm" or ".xls";
                if (row.Category != "OTHER" || supported) list.Add(row);
            }
        }
        return list;
    }

    // PDFs outside the DL/PDF folders (e.g. "Revision 2025-10-13", "Revised on ...") are not used, but
    // they can hold a newer revision of a drawing, or the only copy of it. Report both per Sub.
    public static List<FileIssue> RevisionFolderIssues(List<InvRow> rows)
    {
        var issues = new List<FileIssue>();
        var selected = rows.Where(r => r.Selected && r.Category == "DRAWING")
            .GroupBy(r => (r.Sub.ToUpperInvariant(), r.DocId.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.First());

        // Newest revision per (Sub, drawing) among the files outside the DL/PDF folders.
        var candidates = rows.Where(r => r.Category == "OTHER" && r.Ext == ".pdf" && r.Reason == "outside DL/PDF folders")
            .Select(r => (Row: r, Id: DrawingIdentity(Path.GetFileNameWithoutExtension(r.FileName))))
            .Where(c => Regex.IsMatch(c.Id.DocId, @"^(V\d{7,8}|\d{9})$"))
            .GroupBy(c => (c.Row.Sub.ToUpperInvariant(), c.Id.DocId.ToUpperInvariant()))
            .Select(g => g.OrderByDescending(c => RevRank(c.Id.Rev)).First());

        foreach (var (r, (docId, rev)) in candidates)
        {
            string folder = Path.GetDirectoryName(r.RelPath) ?? "";

            if (!selected.TryGetValue((r.Sub.ToUpperInvariant(), docId.ToUpperInvariant()), out var used))
            {
                issues.Add(new FileIssue(r.Sub, "Drawing outside PDF folder", docId, folder, r.FileName,
                    $"Drawing {docId} (rev {Rev(rev)}) exists only outside the PDF folder: {r.RelPath} - not used."));
            }
            else if (RevRank(rev) > RevRank(used.Revision))
            {
                issues.Add(new FileIssue(r.Sub, "Newer revision outside PDF folder", docId, folder, r.FileName,
                    $"Drawing {docId}: rev {Rev(rev)} in {r.RelPath} is newer than the rev {Rev(used.Revision)} used ({used.FileName}) - newer revision not used."));
            }
        }
        return issues;

        static string Rev(string rev) => rev.Length == 0 ? "-" : rev;
    }

    // Cross-check the scanned files against an inventory workbook: files on disk the inventory does
    // not list (aggregated per Sub when the whole Sub is missing) and inventory rows not found on disk.
    // Only DL/PDF-folder files are compared, as the inventory lists only those.
    public static List<FileIssue> Reconcile(List<InvRow> scanned, List<InvRow> inventory, string inventoryFile)
    {
        var issues = new List<FileIssue>();
        var invPaths = new HashSet<string>(inventory.Select(r => r.RelPath), StringComparer.OrdinalIgnoreCase);
        var diskPaths = new HashSet<string>(scanned.Select(r => r.RelPath), StringComparer.OrdinalIgnoreCase);
        var invSubs = new HashSet<string>(inventory.Select(r => r.Sub), StringComparer.OrdinalIgnoreCase);
        string invName = Path.GetFileName(inventoryFile);

        foreach (var g in scanned.Where(r => r.Category != "OTHER" && !invPaths.Contains(r.RelPath)).GroupBy(r => r.Sub, StringComparer.OrdinalIgnoreCase))
        {
            if (!invSubs.Contains(g.Key))
            {
                string folder = g.First().RelPath.Split('\\')[0];
                issues.Add(new FileIssue(g.Key, "Sub not in inventory", folder, "Inventory", invName,
                    $"Sub folder '{folder}' is not listed in the inventory {invName} ({g.Count()} files under DL/PDF folders) - processed from the folder scan."));
                continue;
            }
            foreach (var r in g)
                issues.Add(new FileIssue(r.Sub, "File not in inventory", r.FileName, "Inventory", invName,
                    $"File on disk not listed in the inventory {invName}: {r.RelPath}."));
        }

        foreach (var r in inventory.Where(r => r.Sub.Length > 0 && !diskPaths.Contains(r.RelPath)))
            issues.Add(new FileIssue(r.Sub, "Inventory file not on disk", r.FileName, "Inventory", invName,
                $"Listed in the inventory {invName} but not found in the documents folder: {r.RelPath}."));

        return issues;
    }

    static InvRow Classify(string rel, string url)
    {
        string[] segments = rel.Split('\\', '/');
        string fileName = segments[^1];
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
        string sub = SubOf(segments);

        bool inOld = segments.Any(s => s.Equals("Old", StringComparison.OrdinalIgnoreCase));
        bool supported = ext is ".pdf" or ".xlsx" or ".xlsm" or ".xls";

        string category = "OTHER";
        // Category comes from the folder names only (never the file name): files under a DL
        // folder ("1 DL", "01 DL", "DL") are Delivery Lists, under a PDF folder ("3 PDF", "03 PDF",
        // "PDF") drawings.
        var dirs = segments[..^1];
        if (dirs.Any(s => Regex.IsMatch(s.Trim(), @"^\d*\s*DL$", RegexOptions.IgnoreCase))) category = "DL";
        else if (dirs.Any(s => Regex.IsMatch(s.Trim(), @"^\d*\s*PDF$", RegexOptions.IgnoreCase))) category = "DRAWING";

        var (docId, rev) = category == "DRAWING" ? DrawingIdentity(nameNoExt) : DlIdentity(nameNoExt);

        string reason = "";
        bool eligible = true;
        if (inOld) { eligible = false; reason = "Old folder"; }
        else if (!supported) { eligible = false; reason = "unsupported extension"; }
        else if (category == "OTHER") { eligible = false; reason = "outside DL/PDF folders"; }

        return new InvRow(rel, url, sub, category, fileName, ext, docId, rev, ext is ".xlsx" or ".xlsm" or ".xls", eligible, false, reason);
    }

    // Ranks a revision token so the latest can be chosen: "-"/empty = initial (lowest),
    // then alphabetic (A<B<C), then numeric (00<01<02). Series are normally homogeneous per group.
    static int RevRank(string rev)
    {
        rev = rev.Trim().ToUpperInvariant();
        if (rev is "" or "-") return 0;
        if (int.TryParse(rev, out var n)) return 1000 + n;             // numeric series
        if (Regex.IsMatch(rev, "^[A-Z]{1,2}$"))                         // alphabetic series
        {
            int val = 0;
            foreach (char c in rev) val = val * 26 + (c - 'A' + 1);
            return 100 + val;
        }
        return 0;
    }

    static (string DocId, string Rev) DrawingIdentity(string nameNoExt)
    {
        var num = Regex.Match(nameNoExt, @"(V\d{7,8}|\d{9})");
        string docId = num.Success ? num.Value : nameNoExt;

        string rest = num.Success ? nameNoExt[(num.Index + num.Length)..] : nameNoExt;
        // e.g. "_A_NAME", "_-_NAME", " - Rev A", " _ 02", "_A_NAME1(3)"
        var rev = Regex.Match(rest, @"^[_\s]*[-]?\s*(?:Rev\s*)?([A-Z]{1,2}|\d{1,2}|-)(?=[_\s(]|$)", RegexOptions.IgnoreCase);
        return (docId, rev.Success ? rev.Groups[1].Value : "");
    }

    static (string DocId, string Rev) DlIdentity(string nameNoExt)
    {
        var rev = Regex.Match(nameNoExt, @"REV\s*([A-Za-z0-9]+)\s*$", RegexOptions.IgnoreCase);
        string docId = Regex.Replace(nameNoExt, @"[_\s-]*REV\s*[A-Za-z0-9]+\s*$", "", RegexOptions.IgnoreCase).Trim();
        // Same document regardless of separator/case, e.g. "PRIMARY_STRUCTURE" vs "PRIMARY STRUCTURE".
        docId = Regex.Replace(docId, @"[\s_]+", " ").ToUpperInvariant();
        return (docId, rev.Success ? rev.Groups[1].Value : "");
    }
}

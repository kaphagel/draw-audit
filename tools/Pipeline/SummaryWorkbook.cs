using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DrawAudit.Hierarchy;
using Microsoft.Data.Sqlite;

namespace DrawAudit.Pipeline;

// Output colours: LevelColors 1-5 (required) and SummaryHeaderColor (optional, defaults to level 1).
// Colours are "#RRGGBB" or "R, G, B". The defaults are built into the application; an
// appsettings.json next to the executable (or an explicit --config path) overrides them.
public sealed record SummaryConfig(IReadOnlyDictionary<int, XLColor> LevelFill, XLColor SummaryHeaderFill)
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static SummaryConfig Load(string? path = null)
    {
        if (path is not null && !File.Exists(path)) throw new FileNotFoundException($"Config file not found: {path}");
        path ??= File.Exists(DefaultPath) ? DefaultPath : null;

        string json, source;
        if (path is not null)
        {
            json = File.ReadAllText(path);
            source = path;
        }
        else
        {
            using var stream = typeof(SummaryConfig).Assembly.GetManifestResourceStream("DrawAudit.appsettings.json")
                ?? throw new InvalidOperationException("The built-in colour settings are missing from the application.");
            json = new StreamReader(stream).ReadToEnd();
            source = "built-in settings";
        }

        try
        {
            using var doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;

            if (!root.TryGetProperty("LevelColors", out var lc)) throw new FormatException("missing \"LevelColors\"");
            var levels = new Dictionary<int, XLColor>();
            foreach (var p in lc.EnumerateObject())
            {
                if (!int.TryParse(p.Name, out int level) || level is < 1 or > 5)
                    throw new FormatException($"LevelColors key \"{p.Name}\" must be a level 1-5");
                levels[level] = ParseColor(p.Value.GetString() ?? "");
            }
            var missing = Enumerable.Range(1, 5).Where(l => !levels.ContainsKey(l)).ToList();
            if (missing.Count > 0) throw new FormatException($"LevelColors missing level(s) {string.Join(", ", missing)}");

            var header = root.TryGetProperty("SummaryHeaderColor", out var h) ? ParseColor(h.GetString() ?? "") : levels[1];
            return new SummaryConfig(levels, header);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            throw new FormatException($"Invalid config '{source}': {ex.Message}", ex);
        }
    }

    static XLColor ParseColor(string s)
    {
        s = s.Trim();
        var hex = Regex.Match(s, "^#?([0-9A-Fa-f]{6})$");
        if (hex.Success)
        {
            int v = Convert.ToInt32(hex.Groups[1].Value, 16);
            return XLColor.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }
        var parts = s.Split(',').Select(x => x.Trim()).ToArray();
        if (parts.Length == 3 && parts.All(x => int.TryParse(x, out int c) && c is >= 0 and <= 255))
            return XLColor.FromArgb(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        throw new FormatException($"invalid colour \"{s}\" (use \"#RRGGBB\" or \"R, G, B\")");
    }
}

public sealed record SubResult(string Sub, int Rows, int Warnings);

// The summary template built into the application (Resources\SummaryTemplate.xlsx). A template file
// path can still be given to the command-line tools for testing.
public static class SummaryTemplate
{
    const string ResourceName = "DrawAudit.SummaryTemplate.xlsx";

    public static XLWorkbook Open(string? path = null)
    {
        if (!string.IsNullOrEmpty(path)) return new XLWorkbook(path);
        using var stream = typeof(SummaryTemplate).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The built-in summary template is missing from the application.");
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return new XLWorkbook(copy);
    }

    // Template version from the Info sheet (cell B1, e.g. "Rev 4").
    public static string Version(string? path = null)
    {
        using var wb = Open(path);
        return wb.Worksheets.TryGetWorksheet("Info", out var info) ? info.Cell(1, 2).GetString().Trim() : "";
    }
}

// Writes the per-Sub hierarchy into a copy of the summary template workbook. For each Sub it copies
// the "Template" sheet, sets A1 = DL filename, inserts rows above the "Warnings / Errors" block if
// needed, fills columns A-W, colours each row by level (A-W), records builder warnings in the
// Warnings / Errors area, and lists every warning across all Subs on a "Warnings Summary" sheet with
// links to the affected rows.
public static class SummaryWorkbook
{
    // Columns to centre (spec section 13): G,H,I,K,L,N,O,P,Q,S,T,V,W
    static readonly int[] CentreCols = { 7, 8, 9, 11, 12, 14, 15, 16, 17, 19, 20, 22, 23 };
    const int FirstDataRow = 3;

    public static List<SubResult> Write(string dbPath, string? templatePath, string outPath,
        IReadOnlyCollection<string>? subs, SummaryConfig config, Action<string>? log = null, CancellationToken ct = default)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var subList = subs is { Count: > 0 }
            ? subs.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : Store.SubsInStore(conn);
        if (subList.Count == 0) throw new InvalidOperationException("No Subs found in store.");

        using var wb = SummaryTemplate.Open(templatePath);
        var templateSheet = wb.Worksheet("Template");
        var summary = new List<(string Sub, string Sheet, Issue Issue, int? TargetRow, int WarningRow)>();
        var results = new List<SubResult>();

        foreach (var sub in subList)
        {
            ct.ThrowIfCancellationRequested();
            var store = Store.Load(conn, sub);
            if (store.Dl is null)
            {
                log?.Invoke($"Skipping {sub}: no DL header.");
                continue;
            }

            var rows = new List<HierRow>();
            var warnings = new List<Issue>();
            new Builder(store, rows, warnings).Build();

            // File-selection issues (revision folders, inventory cross-check); link to the drawing's
            // first hierarchy row when it appears in the tree.
            foreach (var fi in Inventory.LoadIssues(conn, sub))
            {
                int idx = rows.FindIndex(r => string.Equals(r.DrawingNo, fi.Item, StringComparison.OrdinalIgnoreCase));
                warnings.Add(new Issue(fi.Type, fi.Item, fi.Source, fi.SourceFile, idx >= 0 ? idx : null, fi.Message));
            }

            string sheetName = SafeSheetName(sub);
            if (wb.Worksheets.Contains(sheetName)) wb.Worksheet(sheetName).Delete();
            var ws = templateSheet.CopyTo(sheetName);

            ws.Cell(1, 1).Value = store.Dl.DlFile ?? "";

            int warningsRow = FindWarningsRow(ws);
            int available = warningsRow - FirstDataRow;
            if (rows.Count > available)
            {
                int insert = rows.Count - available;
                ws.Row(warningsRow).InsertRowsAbove(insert);
                warningsRow += insert;
            }

            for (int i = 0; i < rows.Count; i++)
                WriteRow(ws, FirstDataRow + i, rows[i], config.LevelFill);

            WriteWarnings(ws, warningsRow, warnings);
            for (int i = 0; i < warnings.Count; i++)
            {
                int? target = warnings[i].RowIndex is int idx ? FirstDataRow + idx : null;
                summary.Add((sub, sheetName, warnings[i], target, warningsRow + 1 + i));
            }

            results.Add(new SubResult(sub, rows.Count, warnings.Count));
            log?.Invoke($"{sheetName,-10} {rows.Count,4} rows  ({warnings.Count} warnings)");
        }

        PopulateInventory(conn, wb);
        WriteWarningsSummary(conn, wb, summary, config.SummaryHeaderFill);

        ct.ThrowIfCancellationRequested();
        wb.SaveAs(outPath);
        return results;
    }

    // Fill the Inventory sheet with the relative path + Yes/No selection audit trail (spec section 5).
    static void PopulateInventory(SqliteConnection conn, XLWorkbook wb)
    {
        if (!wb.Worksheets.Contains("Inventory")) return;
        if (!TableExists(conn, "inventory")) return;

        var inv = wb.Worksheet("Inventory");
        int row = 2;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rel_path, selected FROM inventory ORDER BY rowid";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            inv.Cell(row, 1).Value = rd.GetString(0);
            inv.Cell(row, 2).Value = rd.GetInt64(1) == 1 ? "Yes" : "No";
            row++;
        }
    }

    static bool TableExists(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    static void WriteRow(IXLWorksheet ws, int row, HierRow r, IReadOnlyDictionary<int, XLColor> levelFill)
    {
        int levelCol = r.Level + 1; // Level 1 -> column B(2) ... Level 5 -> F(6)
        // B-F: drawing number for a drawing-backed item, else the material description.
        ws.Cell(row, levelCol).Value = r.DrawingNo ?? r.PartName;
        // Fill the whole row A..W with the level colour so the level is visible while scrolling.
        if (levelFill.TryGetValue(r.Level, out var fill))
            ws.Range(row, 1, row, 23).Style.Fill.BackgroundColor = fill;

        ws.Cell(row, 7).Value = r.Level;                 // G Drawing level
        ws.Cell(row, 8).Value = r.Page;                  // H Page
        ws.Cell(row, 9).Value = r.Revision;              // I Drawing revision
        ws.Cell(row, 10).Value = r.Name;                 // J Drawing name (indented)
        SetNumOrText(ws.Cell(row, 11), r.QtyPerParent);  // K Quantity per parent
        SetNumOrText(ws.Cell(row, 12), r.TotalQty);      // L Total quantity
        ws.Cell(row, 13).Value = r.Scope;                // M Part of scope?
        ws.Cell(row, 14).Value = r.ItpManufacturing;     // N ITP manufacturing
        ws.Cell(row, 15).Value = r.ItpConstruction;      // O ITP erection
        ws.Cell(row, 16).Value = r.ExcClass;             // P EXC class
        ws.Cell(row, 17).Value = r.Painting;             // Q Surface treatment
        ws.Cell(row, 18).Value = r.SurfaceExtra;         // R Extra info
        ws.Cell(row, 19).Value = r.Colour;               // S Color
        ws.Cell(row, 20).Value = r.Welds;                // T Welds?
        ws.Cell(row, 21).Value = r.Ndt;                  // U NDT requirements
        ws.Cell(row, 22).Value = r.Seal;                 // V Seal / gas-tight welds
        ws.Cell(row, 23).Value = r.Iso5817;              // W ISO 5817 class

        foreach (int c in CentreCols)
            ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    static void SetNumOrText(IXLCell cell, string value)
    {
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
            cell.Value = n;
        else
            cell.Value = value;
    }

    static void WriteWarnings(IXLWorksheet ws, int warningsRow, List<Issue> warnings)
    {
        int r = warningsRow + 1;
        foreach (var w in warnings)
            ws.Cell(r++, 1).Value = w.Message;
    }

    // One row per warning across all Subs, with the Sub folder, the drawing/DL that generates the
    // issue, its file, and a link to the affected row in the Sub sheet.
    static void WriteWarningsSummary(SqliteConnection conn, XLWorkbook wb,
        List<(string Sub, string Sheet, Issue Issue, int? TargetRow, int WarningRow)> summary, XLColor headerFill)
    {
        const string name = "Warnings Summary";
        if (wb.Worksheets.Contains(name)) wb.Worksheet(name).Delete();
        var ws = wb.Worksheets.Add(name, 1);

        string[] headers = { "#", "Sub", "Sub folder", "Issue type", "Affected item", "Generated by (drawing / DL)", "Source file", "Go to", "Details" };
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        var head = ws.Range(1, 1, 1, headers.Length);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = headerFill;

        var folders = new Dictionary<string, string>();
        int row = 2;
        foreach (var (sub, sheet, issue, target, warningRow) in summary)
        {
            if (!folders.TryGetValue(sub, out var folder))
                folders[sub] = folder = SubFolder(conn, sub);

            string address = target is int t ? $"J{t}" : $"A{warningRow}";
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = sub;
            ws.Cell(row, 3).Value = folder;
            ws.Cell(row, 4).Value = issue.Type;
            ws.Cell(row, 5).Value = issue.Item;
            ws.Cell(row, 6).Value = issue.Source;
            ws.Cell(row, 7).Value = issue.SourceFile ?? "";
            var link = ws.Cell(row, 8);
            link.Value = $"{sheet}!{address}";
            link.SetHyperlink(new XLHyperlink($"'{sheet}'!{address}"));
            link.Style.Font.FontColor = XLColor.Blue;
            link.Style.Font.Underline = XLFontUnderlineValues.Single;
            ws.Cell(row, 9).Value = issue.Message;
            row++;
        }
        if (summary.Count == 0) ws.Cell(2, 1).Value = "No warnings.";

        ws.Range(1, 1, Math.Max(row - 1, 1), headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, 8).AdjustToContents();
        ws.Column(9).Width = 120;
    }

    // Root folder of a Sub, taken from its inventory paths (e.g. "Sub1002-1 Casing Walls_2025-07-14").
    static string SubFolder(SqliteConnection conn, string sub)
    {
        if (!TableExists(conn, "inventory")) return "";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rel_path FROM inventory WHERE sub=$s LIMIT 1";
        cmd.Parameters.AddWithValue("$s", sub);
        return cmd.ExecuteScalar() is string p ? p.Split('\\', '/')[0] : "";
    }

    static int FindWarningsRow(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        int last = used?.RangeAddress.LastAddress.RowNumber ?? 40;
        for (int r = 1; r <= last; r++)
            if (ws.Cell(r, 1).GetString().Trim().Equals("Warnings / Errors", StringComparison.OrdinalIgnoreCase))
                return r;
        return last + 2;
    }

    static string SafeSheetName(string sub)
    {
        string s = sub;
        foreach (char c in new[] { '\\', '/', '*', '?', ':', '[', ']' }) s = s.Replace(c, '_');
        return s.Length > 31 ? s[..31] : s;
    }
}

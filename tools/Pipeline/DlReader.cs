using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DrawAudit.Extraction;
using Microsoft.Data.Sqlite;

namespace DrawAudit.Pipeline;

public sealed record DrawingRef(string DrawingNo, string? Revision);
public sealed record DeliveryItem(string ItemNr, string Quantity, string Unit, string DrawingNo, string Description, string NetWeight);
public sealed record DlResult(
    string File, string? Sub, List<DrawingRef> AssemblyDrawings, string? SubDescription, string? RevLetter,
    string? ItpManufacturing, string? ItpConstruction, string? ExcClass,
    string? PaintingSystem, string? Color, string? SurfaceTreatmentRaw,
    List<DeliveryItem> DeliveryItems);

// Reads an Andritz Delivery List (DL) workbook into structured data: header fields (Assembly Dwg ->
// Level 1, ITP/EXC/surface treatment) and the delivery-items table. Fields are anchored on label
// text (value is the cell directly below the label) so the reader tolerates layout shifts.
public static class DlReader
{
    public static DlResult Read(string path)
    {
        using var wb = new XLWorkbook(path);

        string? sub = ValueBelow(wb, "Sub No.");
        string? assemblyRaw = ValueBelow(wb, "Assembly Dwg");
        string? subDesc = ValueBelow(wb, "Sub no description and WP no");
        string? revLetter = ValueBelow(wb, "Rev letter");
        string? itpMfg = ValueBelow(wb, "Inspection plan Manufacturing:");
        string? itpCon = ValueBelow(wb, "Inspection plan Construction:");
        string? excClass = ValueBelow(wb, "Exc class");
        string? surfaceRaw = ValueBelow(wb, "Surface treatment");

        return new DlResult(
            File: Path.GetFileName(path),
            Sub: sub,
            AssemblyDrawings: ParseDrawingRefs(assemblyRaw),
            SubDescription: subDesc,
            RevLetter: revLetter,
            ItpManufacturing: itpMfg,
            ItpConstruction: itpCon,
            ExcClass: excClass,
            PaintingSystem: surfaceRaw is null ? null : MatchOrNull(surfaceRaw, @"section\s*([0-9][0-9.]*)"),
            Color: surfaceRaw is null ? null : MatchOrNull(surfaceRaw, @"(RAL\s*\d+)"),
            SurfaceTreatmentRaw: Collapse(surfaceRaw),
            DeliveryItems: ReadDeliveryItems(wb));
    }

    // Finds a label cell (case-insensitive; label may be a prefix of the cell text) and
    // returns the formatted value from the cell directly below it.
    static string? ValueBelow(XLWorkbook wb, string label)
    {
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used is null) continue;
            foreach (var cell in used.CellsUsed(c =>
                c.GetFormattedString().Trim().StartsWith(label, StringComparison.OrdinalIgnoreCase)))
            {
                var below = ws.Cell(cell.Address.RowNumber + 1, cell.Address.ColumnNumber);
                string v = below.GetFormattedString().Trim();
                if (v.Length > 0) return v;
            }
        }
        return null;
    }

    static List<DrawingRef> ParseDrawingRefs(string? raw)
    {
        var list = new List<DrawingRef>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        // e.g. "V1756987:A" (may list several separated by , ; / or whitespace)
        foreach (Match m in Regex.Matches(raw, @"(V?\d{7,9})\s*[:\-]?\s*([A-Z0-9\-]*)"))
        {
            string no = m.Groups[1].Value;
            string rev = m.Groups[2].Value;
            list.Add(new DrawingRef(no, string.IsNullOrEmpty(rev) ? null : rev));
        }
        return list;
    }

    static List<DeliveryItem> ReadDeliveryItems(XLWorkbook wb)
    {
        var items = new List<DeliveryItem>();
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used is null) continue;

            // Locate the delivery-items header row and its column numbers.
            var header = used.Rows().FirstOrDefault(r =>
                r.Cells().Any(c => c.GetFormattedString().Trim().Equals("Delivery item nr", StringComparison.OrdinalIgnoreCase)));
            if (header is null) continue;

            int Col(params string[] labels) => header.Cells()
                .Where(c => labels.Any(l => c.GetFormattedString().Trim().StartsWith(l, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Address.ColumnNumber).DefaultIfEmpty(0).First();

            int colItem = Col("Delivery item nr");
            int colQty = Col("Quantity");
            int colUnit = Col("Unit");
            int colDwg = Col("Drawing");
            int colDesc = Col("Description");
            int colWeight = Col("Net weight");

            int headerRow = header.RowNumber();
            int lastRow = used.RangeAddress.LastAddress.RowNumber;
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                string Get(int c) => c > 0 ? ws.Cell(r, c).GetFormattedString().Trim() : "";
                string dwg = Get(colDwg);
                string desc = Get(colDesc);
                string item = Get(colItem);

                // A real delivery row has a drawing/description; stop at the surface-treatment footer.
                if (Get(colItem).StartsWith("Surface", StringComparison.OrdinalIgnoreCase)) break;
                if (dwg.Length == 0 && desc.Length == 0 && item.Length == 0) continue;

                // Only rows carrying a delivery item number are delivery units.
                if (item.Length == 0) continue;

                items.Add(new DeliveryItem(
                    ItemNr: item,
                    Quantity: Get(colQty),
                    Unit: Get(colUnit),
                    DrawingNo: dwg,
                    Description: desc,
                    NetWeight: Get(colWeight)));
            }
        }
        return items;
    }

    static string? MatchOrNull(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    static string? Collapse(string? s) =>
        s is null ? null : Regex.Replace(s, @"\s+", " ").Trim();
}

// SQLite persistence of a DL (shared store with the drawings).
public static class DlStore
{
    public static void Save(string dbPath, string filePath, DlResult dl)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        Init(conn);

        using var tx = conn.BeginTransaction();
        Delete(conn, tx, filePath);

        // Use the folder-derived Sub as the join key so it matches the drawings' Sub
        // (the DL's own "Sub No." cell can differ, e.g. 1059-1 vs folder 1059, or 1012-1 vs 1012-1&2).
        string sub = DrawingReader.SubFromPath(filePath) ?? dl.Sub ?? "";

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO dl_header
                  (file_path, file, sub, assembly_drawings, sub_description, rev_letter,
                   itp_manufacturing, itp_construction, exc_class, painting_system, color, surface_raw, extracted_at)
                VALUES
                  ($fp, $file, $sub, $asm, $desc, $rev, $itpm, $itpc, $exc, $paint, $color, $surf, $at);
                """;
            cmd.Parameters.AddWithValue("$fp", filePath);
            cmd.Parameters.AddWithValue("$file", dl.File);
            cmd.Parameters.AddWithValue("$sub", sub);
            cmd.Parameters.AddWithValue("$asm", string.Join(", ", dl.AssemblyDrawings.Select(a => $"{a.DrawingNo}:{a.Revision}")));
            cmd.Parameters.AddWithValue("$desc", (object?)dl.SubDescription ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rev", (object?)dl.RevLetter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$itpm", (object?)dl.ItpManufacturing ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$itpc", (object?)dl.ItpConstruction ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$exc", (object?)dl.ExcClass ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$paint", (object?)dl.PaintingSystem ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$color", (object?)dl.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$surf", (object?)dl.SurfaceTreatmentRaw ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        foreach (var it in dl.DeliveryItems)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO dl_items
                  (file_path, sub, item_nr, quantity, unit, drawing_no, description, net_weight)
                VALUES
                  ($fp, $sub, $item, $qty, $unit, $dno, $desc, $wt);
                """;
            cmd.Parameters.AddWithValue("$fp", filePath);
            cmd.Parameters.AddWithValue("$sub", sub);
            cmd.Parameters.AddWithValue("$item", it.ItemNr);
            cmd.Parameters.AddWithValue("$qty", it.Quantity);
            cmd.Parameters.AddWithValue("$unit", it.Unit);
            cmd.Parameters.AddWithValue("$dno", it.DrawingNo);
            cmd.Parameters.AddWithValue("$desc", it.Description);
            cmd.Parameters.AddWithValue("$wt", it.NetWeight);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    static void Init(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS dl_header (
                file_path         TEXT PRIMARY KEY,
                file              TEXT,
                sub               TEXT,
                assembly_drawings TEXT,
                sub_description   TEXT,
                rev_letter        TEXT,
                itp_manufacturing TEXT,
                itp_construction  TEXT,
                exc_class         TEXT,
                painting_system   TEXT,
                color             TEXT,
                surface_raw       TEXT,
                extracted_at      TEXT
            );
            CREATE TABLE IF NOT EXISTS dl_items (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path   TEXT,
                sub         TEXT,
                item_nr     TEXT,
                quantity    TEXT,
                unit        TEXT,
                drawing_no  TEXT,
                description TEXT,
                net_weight  TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_dl_items_sub ON dl_items(sub, drawing_no);
            """;
        cmd.ExecuteNonQuery();
    }

    static void Delete(SqliteConnection conn, SqliteTransaction tx, string filePath)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM dl_header WHERE file_path=$p; DELETE FROM dl_items WHERE file_path=$p;";
        cmd.Parameters.AddWithValue("$p", filePath);
        cmd.ExecuteNonQuery();
    }
}

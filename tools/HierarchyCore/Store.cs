using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace DrawAudit.Hierarchy;

// In-memory view of the SQLite store scoped to a single Sub.
public sealed class Store
{
    public DlHeaderRec? Dl { get; private init; }
    private Dictionary<string, DrawingRec> _drawings = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<BomChildRec>> _bom = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DlItemRec> _dlItems = new(StringComparer.OrdinalIgnoreCase);

    public DrawingRec? Drawing(string no) => _drawings.GetValueOrDefault(no);
    public DlItemRec? DlItem(string no) => _dlItems.GetValueOrDefault(no);

    // DL delivery items identified by a material specification instead of a drawing number.
    public List<DlItemRec> DlMaterialItems() =>
        _dlItems.Values.Where(i => !Regex.IsMatch(i.DrawingNo, @"^V?\d{7,9}$")).ToList();
    public IReadOnlyList<BomChildRec> BomChildren(string parent) =>
        _bom.GetValueOrDefault(parent) ?? (IReadOnlyList<BomChildRec>)Array.Empty<BomChildRec>();

    // Delivered drawings that head the tree: present as drawings and not a BOM child of another.
    public List<string> TopDeliveryDrawings()
    {
        var children = new HashSet<string>(
            _bom.Values.SelectMany(l => l).Where(b => b.IsSubDrawing).Select(b => b.ChildDrawingNo),
            StringComparer.OrdinalIgnoreCase);
        return _dlItems.Keys
            .Where(no => _drawings.ContainsKey(no) && !children.Contains(no))
            .ToList();
    }

    // Distinct Sub identifiers present in the DL header table.
    public static List<string> SubsInStore(SqliteConnection conn)
    {
        var subs = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT sub FROM dl_header WHERE sub IS NOT NULL ORDER BY sub";
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) subs.Add(rd.GetString(0));
        return subs;
    }

    public static Store Load(SqliteConnection conn, string sub)
    {
        var drawings = new Dictionary<string, DrawingRec>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT drawing_no, revision, page, title, iso5817, mentions_ndt, mentions_seal, file FROM drawings WHERE sub=$s AND drawing_no IS NOT NULL";
            cmd.Parameters.AddWithValue("$s", sub);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var rec = new DrawingRec(
                    rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4),
                    !rd.IsDBNull(5) && rd.GetInt64(5) == 1, !rd.IsDBNull(6) && rd.GetInt64(6) == 1,
                    rd.IsDBNull(7) ? null : rd.GetString(7));
                // Dedupe multi-sheet drawings: prefer the sheet-file that has a title.
                if (!drawings.TryGetValue(rec.DrawingNo, out var existing) || Better(rec, existing))
                    drawings[rec.DrawingNo] = rec;
            }
        }

        var bom = new Dictionary<string, List<BomChildRec>>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT parent_drawing_no, amount, det_no, child_drawing_no, description, specification, net_weight, is_sub_drawing FROM bom_items WHERE parent_sub=$s AND parent_drawing_no IS NOT NULL ORDER BY CAST(det_no AS INTEGER)";
            cmd.Parameters.AddWithValue("$s", sub);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var rec = new BomChildRec(
                    rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2),
                    rd.IsDBNull(3) ? "" : rd.GetString(3), rd.IsDBNull(4) ? "" : rd.GetString(4),
                    rd.IsDBNull(5) ? "" : rd.GetString(5), rd.IsDBNull(6) ? "" : rd.GetString(6),
                    !rd.IsDBNull(7) && rd.GetInt64(7) == 1);
                // The parts list is repeated on every sheet of a multi-sheet drawing; dedupe here.
                string norm = Regex.Replace(rec.Description.ToUpperInvariant(), @"\s+", " ").Trim();
                string key = rec.IsSubDrawing
                    ? $"{rec.ParentDrawingNo}|SUB|{rec.ChildDrawingNo}"
                    : $"{rec.ParentDrawingNo}|MAT|{rec.DetNo}|{norm}";
                if (!seen.Add(key)) continue;
                (bom.TryGetValue(rec.ParentDrawingNo, out var list) ? list : bom[rec.ParentDrawingNo] = new()).Add(rec);
            }
        }

        var dlItems = new Dictionary<string, DlItemRec>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT item_nr, quantity, drawing_no, description, net_weight FROM dl_items WHERE sub=$s";
            cmd.Parameters.AddWithValue("$s", sub);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                string dno = rd.IsDBNull(2) ? "" : rd.GetString(2);
                if (dno.Length == 0) continue;
                dno = NormNo(dno); // strip a ":A" rev suffix so it matches drawings.drawing_no
                dlItems[dno] = new DlItemRec(rd.GetString(0), rd.IsDBNull(1) ? "" : rd.GetString(1), dno,
                    rd.IsDBNull(3) ? "" : rd.GetString(3), rd.IsDBNull(4) ? "" : rd.GetString(4));
            }
        }

        DlHeaderRec? dl = null;
        using (var cmd = conn.CreateCommand())
        {
            // The pipeline selects one DL per Sub; should a store still hold several DL files for a
            // Sub, merge their assembly lists so each assembly becomes a Level-1 branch.
            cmd.CommandText = "SELECT assembly_drawings, itp_manufacturing, itp_construction, exc_class, painting_system, color, file FROM dl_header WHERE sub=$s";
            cmd.Parameters.AddWithValue("$s", sub);
            using var rd = cmd.ExecuteReader();
            var assemblies = new List<string>();
            while (rd.Read())
            {
                if (!rd.IsDBNull(0) && rd.GetString(0).Length > 0) assemblies.Add(rd.GetString(0));
                dl ??= new DlHeaderRec("",
                    rd.IsDBNull(1) ? null : rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4),
                    rd.IsDBNull(5) ? null : rd.GetString(5), rd.IsDBNull(6) ? null : rd.GetString(6));
            }
            if (dl is not null) dl = dl with { AssemblyDrawings = string.Join(", ", assemblies) };
        }

        return new Store { Dl = dl, _drawings = drawings, _bom = bom, _dlItems = dlItems };
    }

    static bool Better(DrawingRec candidate, DrawingRec existing) =>
        !string.IsNullOrEmpty(candidate.Title) && string.IsNullOrEmpty(existing.Title);

    // Bare drawing number (leading V+digits or 9 digits); falls back to the original text.
    static string NormNo(string s)
    {
        var m = Regex.Match(s, @"^(V?\d{7,9})");
        return m.Success ? m.Groups[1].Value : s;
    }
}

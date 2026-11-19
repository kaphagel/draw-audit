using System.Globalization;

namespace DrawAudit.Hierarchy;

// Walks the BOM tree from the DL assembly (Level 1) to at most Level 5, producing summary rows.
public sealed class Builder(Store store, List<HierRow> rows, List<Issue> warnings)
{
    string? FileOf(string drawingNo) => store.Drawing(drawingNo)?.File;

    public void Build()
    {
        var assemblies = store.Dl!.AssemblyList().Select(a => a.No).ToList();
        var present = assemblies.Where(no => store.Drawing(no) is not null).ToList();

        // The DL "Assembly Dwg" sometimes references a parent-project assembly not delivered in
        // this Sub. When none of those are present, fall back to the delivered top drawings
        // (delivery items that are present and not a BOM child of another delivered drawing).
        if (present.Count == 0)
        {
            present = store.TopDeliveryDrawings();
            if (present.Count == 0)
                foreach (var no in assemblies)
                    warnings.Add(new Issue("Level 1 drawing not found", no, "DL Assembly Dwg", store.Dl.DlFile, null,
                        $"Assembly drawing {no} from DL not found among extracted drawings."));
        }

        foreach (var no in present)
        {
            var dwg = store.Drawing(no)!;
            bool delivered = store.DlItem(no) is not null; // a delivered top drawing carries its scope down
            rows.Add(MakeDrawingRow(1, dwg, qtyPerParent: 1, totalQty: 1, scope: "", delivered));
            Expand(no, level: 2, cumQty: 1, ancestorDelivered: delivered,
                path: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { no });
        }
    }

    void Expand(string parentDrawingNo, int level, double cumQty, bool ancestorDelivered, HashSet<string> path)
    {
        if (level > 5) return;
        foreach (var child in store.BomChildren(parentDrawingNo))
        {
            double k = ParseNum(child.Amount, 1);
            double total = cumQty * k;

            if (child.IsSubDrawing)
            {
                var dwg = store.Drawing(child.ChildDrawingNo);
                bool delivered = store.DlItem(child.ChildDrawingNo) is not null;
                string scope = delivered ? "Yes" : ancestorDelivered ? "In parent delivery" : "Excluded";

                if (dwg is null)
                {
                    rows.Add(new HierRow(level, child.ChildDrawingNo, child.Description,
                        Page: "-", Revision: "Missing", Name: Indent(level, $"{child.ChildDrawingNo} \u2013 {child.Description}"),
                        QtyPerParent: child.Amount, TotalQty: Fmt(total), Scope: scope,
                        ItpManufacturing: store.Dl!.ItpManufacturing ?? "", ItpConstruction: store.Dl.ItpConstruction ?? "",
                        ExcClass: store.Dl.ExcClass ?? "", Painting: delivered ? store.Dl.PaintingSystem ?? "-" : "-",
                        SurfaceExtra: "-", Colour: delivered ? store.Dl.Color ?? "-" : "-",
                        Welds: "review", Ndt: "review", Seal: "review", Iso5817: "Unclear"));
                    warnings.Add(new Issue("Missing drawing", child.ChildDrawingNo, parentDrawingNo, FileOf(parentDrawingNo), rows.Count - 1,
                        $"L{level} drawing {child.ChildDrawingNo} referenced in BOM of {parentDrawingNo} but no drawing file present in Sub - marked Missing."));
                    continue;
                }

                rows.Add(MakeDrawingRow(level, dwg, k, total, scope, delivered));

                if (level < 5 && path.Add(child.ChildDrawingNo))
                {
                    Expand(child.ChildDrawingNo, level + 1, total, ancestorDelivered || delivered, path);
                    path.Remove(child.ChildDrawingNo);
                }
                else if (level >= 5 && store.BomChildren(child.ChildDrawingNo).Any())
                {
                    warnings.Add(new Issue("Level 6 required", child.ChildDrawingNo, child.ChildDrawingNo, dwg.File, rows.Count - 1,
                        $"{child.ChildDrawingNo} would require level 6 - not expanded (max level 5)."));
                }
            }
            else
            {
                string name = NameOf(child);
                string scope = ancestorDelivered ? "In parent delivery" : "Excluded";
                bool dlUnit = false;

                // A DL line without a drawing number can deliver this material directly.
                var dlMat = Materials.Match(child, store.DlMaterialItems());
                if (dlMat is not null)
                {
                    dlUnit = true;
                    string bomMat = Materials.Parse(child.Description, child.Specification).Material;
                    string dlMatSpec = Materials.Parse(dlMat.Description, dlMat.DrawingNo).Material;
                    if (bomMat == dlMatSpec)
                        scope = "Yes";
                    else
                    {
                        scope = "Unclear";
                        warnings.Add(new Issue("Material conflict", name, $"{parentDrawingNo} item {child.DetNo} / DL item {dlMat.ItemNr}",
                            $"{FileOf(parentDrawingNo)} / {store.Dl!.DlFile}", rows.Count,
                            $"Material conflict: drawing {parentDrawingNo} BOM item {child.DetNo} '{name}' vs " +
                            $"DL {store.Dl!.DlFile} item {dlMat.ItemNr} '{dlMat.Description} {dlMat.DrawingNo}' " +
                            $"(drawing: {Show(bomMat)}, DL: {Show(dlMatSpec)})."));
                    }
                }

                rows.Add(new HierRow(level, DrawingNo: null, PartName: name,
                    Page: "-", Revision: "-", Name: Indent(level, name),
                    QtyPerParent: child.Amount, TotalQty: Fmt(total),
                    Scope: scope,
                    ItpManufacturing: store.Dl!.ItpManufacturing ?? "", ItpConstruction: store.Dl.ItpConstruction ?? "",
                    ExcClass: store.Dl.ExcClass ?? "",
                    Painting: dlUnit ? store.Dl.PaintingSystem ?? "-" : "-", SurfaceExtra: "-",
                    Colour: dlUnit ? store.Dl.Color ?? "-" : "-",
                    Welds: "-", Ndt: "-", Seal: "-", Iso5817: "-"));
            }
        }
    }

    HierRow MakeDrawingRow(int level, DrawingRec dwg, double qtyPerParent, double totalQty, string scope, bool delivered)
    {
        string title = dwg.Title ?? "";
        return new HierRow(level, dwg.DrawingNo, PartName: title,
            Page: dwg.Page ?? "-", Revision: dwg.Revision ?? "-",
            Name: Indent(level, string.IsNullOrEmpty(title) ? dwg.DrawingNo : $"{dwg.DrawingNo} \u2013 {title}"),
            QtyPerParent: qtyPerParent.ToString(CultureInfo.InvariantCulture), TotalQty: Fmt(totalQty),
            Scope: level == 1 ? "" : scope,
            ItpManufacturing: store.Dl!.ItpManufacturing ?? "", ItpConstruction: store.Dl.ItpConstruction ?? "",
            ExcClass: store.Dl.ExcClass ?? "",
            Painting: level == 1 ? "" : delivered ? store.Dl.PaintingSystem ?? "-" : "-",
            SurfaceExtra: "-",
            Colour: level == 1 ? "" : delivered ? store.Dl.Color ?? "-" : "-",
            // T (Workshop/Site) needs weld-symbol geometry, unsolved from text; U/V best-effort from own drawing.
            Welds: "review",
            Ndt: dwg.MentionsNdt ? "see drawing" : "-",
            Seal: dwg.MentionsSeal ? "see drawing" : "-",
            Iso5817: dwg.Iso5817 ?? "-");
    }

    static string NameOf(BomChildRec c) =>
        string.Join(" ", new[] { c.Description, c.Specification }.Where(s => !string.IsNullOrWhiteSpace(s)));

    static string Show(string material) => material.Length == 0 ? "not stated" : material;

    static string Indent(int level, string s) => new string(' ', 5 * (level - 1)) + s;
    static string Fmt(double d) => d == Math.Floor(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("0.###", CultureInfo.InvariantCulture);
    static double ParseNum(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

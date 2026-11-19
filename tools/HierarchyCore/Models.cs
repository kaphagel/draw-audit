using System.Text.RegularExpressions;

namespace DrawAudit.Hierarchy;

public sealed record DrawingRec(string DrawingNo, string? Revision, string? Page, string? Title, string? Iso5817, bool MentionsNdt, bool MentionsSeal, string? File = null);

// A warning raised while building a Sub's hierarchy. Source/SourceFile identify the drawing or DL
// that generates the issue; RowIndex is the index into the produced rows of the affected row.
public sealed record Issue(string Type, string Item, string Source, string? SourceFile, int? RowIndex, string Message)
{
    public override string ToString() => Message;
}

public sealed record BomChildRec(string ParentDrawingNo, string Amount, string DetNo, string ChildDrawingNo, string Description, string Specification, string NetWeight, bool IsSubDrawing);

public sealed record DlItemRec(string ItemNr, string Quantity, string DrawingNo, string Description, string NetWeight);

public sealed record DlHeaderRec(string AssemblyDrawings, string? ItpManufacturing, string? ItpConstruction, string? ExcClass, string? PaintingSystem, string? Color, string? DlFile)
{
    public IEnumerable<(string No, string? Rev)> AssemblyList()
    {
        foreach (Match m in Regex.Matches(AssemblyDrawings, @"(V?\d{7,9})\s*[:\-]?\s*([A-Z0-9\-]*)"))
            yield return (m.Groups[1].Value, string.IsNullOrEmpty(m.Groups[2].Value) ? null : m.Groups[2].Value);
    }
}

// One produced hierarchy row (maps to the summary template columns A-W).
public sealed record HierRow(
    int Level, string? DrawingNo, string PartName,
    string Page, string Revision, string Name, string QtyPerParent, string TotalQty, string Scope,
    string ItpManufacturing, string ItpConstruction, string ExcClass, string Painting, string SurfaceExtra, string Colour,
    string Welds, string Ndt, string Seal, string Iso5817);

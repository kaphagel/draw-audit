namespace DrawAudit.Extraction;

public sealed record TitleBlock(string? DrawingNo, string? Sheet, string? Rev, string? Title, string? TotWeight);

public sealed record BomRow(string Amount, string DetNo, string DrawingNo, string Description, string Specification, string NetWeight, bool IsSubDrawing);

public sealed record DrawingResult(
    string File, string FilePath, string? Sub, string FileHash,
    string? DrawingNo, string? Page, string? Revision, string? Title, string? TotalWeight,
    string? Iso5817Class, bool MentionsNdt, bool MentionsSealWeld, List<BomRow> Bom);

// A title-block layout adapter. Returns null when it does not recognize the drawing
// (so the next strategy in the chain is tried).
public interface ITitleBlockStrategy
{
    TitleBlock? TryExtract(IReadOnlyList<PageWords> pages, string? hintNumber);
}

// A parts-list (BOM) layout adapter. Returns null when the header for this layout is not
// present on the page (so the next strategy in the chain is tried).
public interface IBomStrategy
{
    IReadOnlyList<BomRow>? TryExtract(PageWords page);
}

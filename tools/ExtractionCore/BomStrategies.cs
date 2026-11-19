using System.Globalization;
using System.Text.RegularExpressions;

namespace DrawAudit.Extraction;

// Shared row parsing + region scan used by the BOM strategies. Rows sit above the header in a
// narrow x-band; drawing/weld annotations further right are clipped out.
internal static class BomScan
{
    // Net weight: decimal point or comma ("103.4", "103,4") or a whole number ("139").
    const string WeightPattern = @"^\d+(?:[.,]\d+)?$";

    // Max vertical distance between consecutive table lines (row pitch is ~12-13 units).
    const double MaxRowGap = 30;

    // A wrapped row continues on the lines directly below it (one line pitch apart, ~12-13 units).
    const double MaxContinuationGap = 16;

    // Words of one table row can sit a few units apart vertically; rows are >= ~12 units apart.
    const double SameRowTolerance = 5;

    public static List<BomRow> Rows(IReadOnlyList<Line> lines, Line header, double bandLeft, double bandRight, bool stripSubNo)
    {
        List<string> RowTokens(Line l)
        {
            // Single letters beyond the weight column are the drawing-frame grid references (A, B, ... Q).
            var t = l.Words.Where(w => w.CenterX >= bandLeft && w.CenterX <= bandRight)
                .Where(w => !(w.CenterX > bandRight - 15 && Regex.IsMatch(w.Text, "^[A-Z]$")))
                .OrderBy(w => w.Left).Select(w => w.Text).ToList();
            // Some assembly parts lists prefix each row with a 4-digit Sub number (e.g. 1002-3).
            if (stripSubNo && t.Count > 0 && Regex.IsMatch(t[0], @"^\d{4}(-[\d&]+)?$")) t = t.Skip(1).ToList();
            // A drawing-frame grid letter (e.g. "P") can sit right after the weight at the frame edge.
            if (t.Count > 1 && Regex.IsMatch(t[^1], "^[A-Z]$") && Regex.IsMatch(t[^2], WeightPattern)) t.RemoveAt(t.Count - 1);
            return t;
        }

        // Rebuild the lines above the header from the words inside the column band only, grouped
        // by each word's own baseline. Page-wide lines mix in drawing annotations and can split a
        // table row in two (e.g. "1 148" at y=294 and "V3745486 PLATE ..." at y=290).
        var bandWords = lines.Where(l => l.Y > header.Y).SelectMany(l => l.Words)
            .Where(w => w.CenterX >= bandLeft && w.CenterX <= bandRight)
            .OrderByDescending(w => w.Y).ToList();
        var above = new List<Line>();
        var current = new List<TextWord>();
        foreach (var w in bandWords)
        {
            if (current.Count > 0 && current[0].Y - w.Y > SameRowTolerance)
            {
                above.Add(new Line(current.Average(x => x.Y), current.OrderBy(x => x.Left).ToList()));
                current = new List<TextWord>();
            }
            current.Add(w);
        }
        if (current.Count > 0) above.Add(new Line(current.Average(x => x.Y), current.OrderBy(x => x.Left).ToList()));

        // The table is the contiguous block of lines directly above the header. Walk upward and
        // stop at the first vertical gap wider than a couple of rows, so row-like drawing
        // annotations further up (e.g. a "4400 - 2 5" dimension) are not taken as BOM rows.
        double top = double.NaN, prevY = header.Y;
        foreach (var l in above.OrderBy(l => l.Y))
        {
            var tk = RowTokens(l);
            if (tk.Count == 0) continue;
            if (l.Y - prevY > MaxRowGap) break;
            prevY = l.Y;
            if (IsRow(tk)) top = l.Y;
        }
        if (double.IsNaN(top)) return new List<BomRow>();

        var region = above.Where(l => l.Y <= top + 2 && l.Y > header.Y).OrderByDescending(l => l.Y);

        var rows = new List<BomRow>();
        double lastY = double.MaxValue;
        foreach (var line in region)
        {
            var tokens = RowTokens(line);
            if (tokens.Count == 0) continue;

            if (IsRow(tokens))
            {
                rows.Add(Parse(tokens));
                lastY = line.Y;
            }
            // Lines below a row (up to the next row) are its wrapped description/specification.
            // Single-letter tokens are the drawing-frame grid references (A, B, ... Q), not spec text.
            else if (rows.Count > 0 && lastY - line.Y < MaxContinuationGap && !tokens.All(t => Regex.IsMatch(t, "^[A-Z]$")))
            {
                var last = rows[^1];
                rows[^1] = last with { Specification = (last.Specification + " " + string.Join(" ", tokens)).Trim() };
                lastY = line.Y;
            }
        }

        return rows.OrderBy(r => int.TryParse(r.DetNo, out var d) ? d : int.MaxValue).ToList();
    }

    // Two leading integers (Amount/Qty, Det/Item) then a Drawing-No token, or a decimal weight,
    // or a material row (description word after the item number; the weight may be missing,
    // e.g. "1 6 RUBBER STRIP L=3250mm").
    public static bool IsRow(List<string> tokens)
    {
        if (tokens.Count < 3) return false;
        if (!int.TryParse(tokens[0], NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return false;
        if (Regex.IsMatch(tokens[1], @"^(V?\d{7,9}|n/?a|N/?A|-)$")) return true;
        if (!int.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return false;
        bool drawingNoAt2 = Regex.IsMatch(tokens[2], @"^(V?\d{7,9}|n/?a|N/?A|-)$");
        bool hasDecimalWeight = Regex.IsMatch(tokens[^1], @"^\d+[.,]\d+$");
        bool materialRow = Regex.IsMatch(tokens[2], "^[A-Za-z]")
            && (tokens.Count >= 4 || Regex.IsMatch(tokens[^1], WeightPattern));
        return drawingNoAt2 || hasDecimalWeight || materialRow;
    }

    static BomRow Parse(List<string> tokens)
    {
        var t = new List<string>(tokens);
        string amount = t[0];
        int idx = 1;

        string detNo = "";
        if (idx < t.Count
            && int.TryParse(t[idx], NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            && !Regex.IsMatch(t[idx], @"^\d{7,9}$"))
        {
            detNo = t[idx];
            idx++;
        }

        string drawingNo = "";
        if (idx < t.Count && Regex.IsMatch(t[idx], @"^(V?\d{7,9}|n/?a|N/?A|-)$"))
        {
            drawingNo = t[idx];
            idx++;
        }

        string netWeight = "";
        if (t.Count > idx + 1 && Regex.IsMatch(t[^1], WeightPattern))
        {
            netWeight = t[^1];
            t.RemoveAt(t.Count - 1);
        }

        var rest = t.Skip(idx).ToList();
        // A standard/profile prefix starts the specification only when it is not the start of an
        // ordinary word ("HEA200", "EN10241", "GB/T1591" - but not "HEAD", "HEATING", "END").
        int specStart = rest.FindIndex(tok => Regex.IsMatch(tok,
            @"^((GB|EN|ISO|DIN|ASTM|JIS|HW|HE[AB]|IPE|UPN|UPE|FB)(?![A-Za-z])|Q\d|Ø|M\d|L\d+|\d+x\d+|\d+\*\d+)"));
        string description = specStart >= 0 ? string.Join(" ", rest.Take(specStart)) : string.Join(" ", rest);
        string specification = specStart >= 0 ? string.Join(" ", rest.Skip(specStart)) : "";

        return new BomRow(amount, detNo, drawingNo, description.Trim(), specification.Trim(), netWeight,
            IsSubDrawing: Regex.IsMatch(drawingNo, @"^V?\d{7,9}$"));
    }
}

// Header "Amount | Det No | Drawing No | Description | Specification | Net Weight" (tokens may merge).
public sealed class AmountDetBomStrategy : IBomStrategy
{
    public IReadOnlyList<BomRow>? TryExtract(PageWords page)
    {
        var lines = page.Lines;
        var header = lines.FindLineWith("Drawing", "Description", "Specification", "Weight");
        if (header is null) return null;
        // "Qty | Item" headers also contain Drawing/Description/Specification/Weight; leave them
        // to QtyItemBomStrategy, which handles the optional Sub No column.
        if (header.Words.Any(w => string.Equals(w.Text, "Qty", StringComparison.OrdinalIgnoreCase))) return null;

        var amountWord = header.Words.FirstOrDefault(w => w.Text.StartsWith("Amount", StringComparison.OrdinalIgnoreCase));
        var weightWord = header.Words.LastOrDefault(w => string.Equals(w.Text, "Weight", StringComparison.OrdinalIgnoreCase));
        double bandLeft = (amountWord?.Left ?? header.Words.Min(w => w.Left)) - 12;
        double bandRight = (weightWord?.Right ?? header.Words.Max(w => w.Right)) + 15;

        return BomScan.Rows(lines, header, bandLeft, bandRight, stripSubNo: false);
    }
}

// Older 703-series assembly parts list: "[Sub No] | Qty | Item | Drw. No. | Description |
// Specification | Weight". Uses "Drw." (not "Drawing") and may prefix rows with a Sub number.
public sealed class QtyItemBomStrategy : IBomStrategy
{
    public IReadOnlyList<BomRow>? TryExtract(PageWords page)
    {
        var lines = page.Lines;
        var header = lines.FindLineWith("Qty", "Item", "Description", "Weight");
        if (header is null) return null;

        var qtyWord = header.Words.FirstOrDefault(w => string.Equals(w.Text, "Qty", StringComparison.OrdinalIgnoreCase));
        var subWord = header.Words.FirstOrDefault(w => string.Equals(w.Text, "Sub", StringComparison.OrdinalIgnoreCase));
        var weightWord = header.Words.LastOrDefault(w => string.Equals(w.Text, "Weight", StringComparison.OrdinalIgnoreCase));
        // Start the band at the Qty column so the Sub No column (e.g. "1026-1", "1030", "WP110")
        // is clipped out by position; fall back to stripping a Sub-number token without Qty.
        double bandLeft = qtyWord is not null
            ? qtyWord.Left - 8
            : (subWord?.Left ?? header.Words.Min(w => w.Left)) - 12;
        double bandRight = (weightWord?.Right ?? header.Words.Max(w => w.Right)) + 15;

        return BomScan.Rows(lines, header, bandLeft, bandRight, stripSubNo: subWord is not null && qtyWord is null);
    }
}

using System.Text.RegularExpressions;

namespace DrawAudit.Extraction;

// Modern ANDRITZ template: a "Drawing No. | Sheet | Rev" label row with the values on the
// line just below, plus a "Prepared | Date | Title | Tot.Weight" row.
public sealed class VTemplateTitleStrategy : ITitleBlockStrategy
{
    public TitleBlock? TryExtract(IReadOnlyList<PageWords> pages, string? hintNumber)
    {
        var page = pages.FirstOrDefault(p => p.Lines.FindLineWith("Drawing", "Rev") is not null) ?? pages[0];
        var lines = page.Lines;

        string? drawingNo = null, sheet = null, rev = null, docTitle = null, totWeight = null;

        var idRow = lines.FindLineWith("Drawing", "Sheet", "Rev");
        if (idRow is not null)
        {
            double xDrawing = idRow.LabelX("Drawing");
            double xSheet = idRow.LabelX("Sheet");
            double xRev = idRow.LabelX("Rev");

            var valueRow = lines
                .Where(l => l.Y < idRow.Y && l.Y > idRow.Y - 32)
                .Where(l => l.Words.Any(w => Regex.IsMatch(w.Text, @"^V?\d{7,9}$") && w.CenterX > xDrawing - 40))
                .OrderByDescending(l => l.Y)
                .FirstOrDefault();

            if (valueRow is not null)
            {
                var dwg = valueRow.Words
                    .Where(w => Regex.IsMatch(w.Text, @"^V?\d{7,9}$"))
                    .OrderBy(w => Math.Abs(w.CenterX - xDrawing)).FirstOrDefault();
                var sheetWord = valueRow.Words
                    .Where(w => Regex.IsMatch(w.Text, @"^\d+\(\d+\)$"))
                    .OrderBy(w => Math.Abs(w.CenterX - xSheet)).FirstOrDefault();
                var revWord = valueRow.Words
                    .Where(w => w != dwg && w != sheetWord && w.CenterX > xSheet)
                    .OrderBy(w => Math.Abs(w.CenterX - xRev)).FirstOrDefault();

                drawingNo = dwg?.Text;
                sheet = sheetWord?.Text;
                rev = revWord?.Text;
            }
        }

        if (drawingNo is null) return null; // not this template

        var titleRow = lines.FindLineWith("Title", "Tot.Weight");
        if (titleRow is not null)
        {
            double xTitle = titleRow.LabelX("Title");
            double xWeight = titleRow.LabelX("Tot.Weight");
            var valueRow = lines.Below(titleRow.Y);
            if (valueRow is not null)
            {
                docTitle = valueRow.JoinColumn(xTitle - 8, xWeight - 8);
                totWeight = valueRow.JoinColumn(xWeight - 8, double.MaxValue);
            }
        }

        return new TitleBlock(LineOps.Clean(drawingNo), LineOps.Clean(sheet), LineOps.Clean(rev),
            LineOps.Clean(docTitle), LineOps.Clean(totWeight));
    }
}

// Older 703-series template: the drawing number sits in the bottom-right title block on the
// same line as the title, with the revision just to its right. The "Drawing No" label repeats
// (Replacement/Copy/Old...), so we locate the number via the filename hint instead.
public sealed class Series703TitleStrategy : ITitleBlockStrategy
{
    public TitleBlock? TryExtract(IReadOnlyList<PageWords> pages, string? hintNumber)
    {
        if (hintNumber is null) return null;

        foreach (var page in pages)
        {
            // The number can also appear in notes lines ("Replacement for Drawing No ...");
            // the real title block is where it sits furthest right.
            var candidates = page.Lines
                .Where(l => l.Y < page.Height * 0.30 && l.Words.Any(w => w.Text == hintNumber))
                .ToList();
            if (candidates.Count == 0) continue;

            var numLine = candidates.OrderByDescending(l => l.Words.First(w => w.Text == hintNumber).Left).First();
            var numWord = numLine.Words.First(w => w.Text == hintNumber);

            string? rev = numLine.Words.Where(w => w.Left > numWord.Right).OrderBy(w => w.Left).FirstOrDefault()?.Text;
            string title = RightmostCluster(numLine.Words
                .Where(w => w.Right <= numWord.Left && Regex.IsMatch(w.Text, @"^[A-Za-z(]"))
                .OrderBy(w => w.Left).ToList());

            return new TitleBlock(hintNumber, Sheet703(page.Lines), LineOps.Clean(rev), LineOps.Clean(title), null);
        }
        return null;
    }

    // Titles are tightly spaced; a boilerplate note to the left is separated by a wider gap.
    // Keep only the last contiguous run of words (gap <= 60) before the drawing number.
    static string RightmostCluster(List<TextWord> words)
    {
        var cluster = new List<TextWord>();
        foreach (var w in words)
        {
            if (cluster.Count > 0 && w.Left - cluster[^1].Right > 60) cluster.Clear();
            cluster.Add(w);
        }
        return string.Join(" ", cluster.Select(w => w.Text));
    }

    // Reads the "Sheet/Sheets" value and normalizes "1 / 2" -> "1(2)".
    static string? Sheet703(IReadOnlyList<Line> lines)
    {
        foreach (var l in lines)
        {
            var lbl = l.Words.FirstOrDefault(w => w.Text.Contains("Sheet/Sheets", StringComparison.OrdinalIgnoreCase));
            if (lbl is null) continue;
            // Drawing annotations can share the band just below the label; use the line that has
            // a "/" under the label.
            var below = lines.Where(x => x.Y < l.Y && x.Y > l.Y - 20
                    && x.Words.Any(w => w.Text == "/" && Math.Abs(w.CenterX - lbl.CenterX) < 80))
                .OrderByDescending(x => x.Y).FirstOrDefault();
            if (below is null) return null;
            var slash = below.Words.Where(w => w.Text == "/" && Math.Abs(w.CenterX - lbl.CenterX) < 80)
                .OrderBy(w => Math.Abs(w.CenterX - lbl.CenterX)).FirstOrDefault();
            if (slash is null) return null;
            var left = below.Words.Where(w => w.Right <= slash.Left && Regex.IsMatch(w.Text, @"^\d+$")).OrderByDescending(w => w.Left).FirstOrDefault();
            var right = below.Words.Where(w => w.Left >= slash.Right && Regex.IsMatch(w.Text, @"^\d+$")).OrderBy(w => w.Left).FirstOrDefault();
            return left is not null && right is not null ? $"{left.Text}({right.Text})" : null;
        }
        return null;
    }
}

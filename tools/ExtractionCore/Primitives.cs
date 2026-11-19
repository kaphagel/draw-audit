using UglyToad.PdfPig.Content;

namespace DrawAudit.Extraction;

// A single positioned word (simplified from PdfPig). Y is the word's own baseline (bottom).
public sealed record TextWord(string Text, double Left, double Right, double Y = 0)
{
    public double CenterX => (Left + Right) / 2.0;
}

// Words sharing a baseline, ordered left-to-right.
public sealed record Line(double Y, List<TextWord> Words);

// One PDF page reduced to horizontal words grouped into lines (top-to-bottom).
public sealed class PageWords
{
    public int Number { get; }
    public double Width { get; }
    public double Height { get; }
    public IReadOnlyList<Line> Lines { get; }

    public PageWords(int number, double width, double height, IEnumerable<Word> words)
    {
        Number = number;
        Width = width;
        Height = height;
        var lines = words
            .Where(w => w.TextOrientation == TextOrientation.Horizontal && !string.IsNullOrWhiteSpace(w.Text))
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 6.0))
            .Select(g => (Y: g.Average(w => w.BoundingBox.Bottom), Words: g.ToList()))
            .OrderByDescending(l => l.Y)
            .ToList();

        // Fixed 6-unit bands split a text line whose words straddle a band edge (e.g. one table row
        // at y=491 and y=489). Lines this close together are pieces of one line; real lines (table
        // rows) are at least ~12 units apart.
        var merged = new List<(double Y, List<Word> Words)>();
        foreach (var l in lines)
        {
            if (merged.Count > 0 && merged[^1].Y - l.Y < 2.5)
            {
                var all = merged[^1].Words.Concat(l.Words).ToList();
                merged[^1] = (all.Average(w => w.BoundingBox.Bottom), all);
            }
            else merged.Add(l);
        }

        Lines = merged
            .Select(l => new Line(l.Y,
                l.Words.Select(w => new TextWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom)).OrderBy(w => w.Left).ToList()))
            .ToList();
    }
}

// Shared line/word helpers used by the extraction strategies.
public static class LineOps
{
    public static Line? FindLineWith(this IReadOnlyList<Line> lines, params string[] tokens) =>
        lines.FirstOrDefault(l => tokens.All(t =>
            l.Words.Any(w => string.Equals(w.Text, t, StringComparison.OrdinalIgnoreCase))));

    public static Line? Below(this IReadOnlyList<Line> lines, double y) =>
        lines.Where(l => l.Y < y - 4).OrderByDescending(l => l.Y).FirstOrDefault();

    public static double LabelX(this Line line, string token) =>
        line.Words.Where(w => string.Equals(w.Text, token, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.Left).DefaultIfEmpty(0).First();

    public static string JoinColumn(this Line line, double left, double right) =>
        string.Join(" ", line.Words.Where(w => w.CenterX >= left && w.CenterX < right)
            .OrderBy(w => w.Left).Select(w => w.Text));

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

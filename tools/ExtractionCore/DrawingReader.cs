using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace DrawAudit.Extraction;

// Extraction engine: runs the title-block and BOM strategy chains against a drawing PDF.
// To support a new drawing template, add a strategy to the relevant list below - nothing
// downstream changes.
public static class DrawingReader
{
    static readonly ITitleBlockStrategy[] TitleStrategies =
    {
        new VTemplateTitleStrategy(),
        new Series703TitleStrategy(),
    };

    static readonly IBomStrategy[] BomStrategies =
    {
        new AmountDetBomStrategy(),
        new QtyItemBomStrategy(),
    };

    public static DrawingResult Extract(string path)
    {
        var pages = LoadPages(path);

        string? hintNumber = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^(V\d{7,8}|\d{9})").Value is { Length: > 0 } h ? h : null;

        var title = RunTitleChain(pages, hintNumber);
        var bom = RunBomChain(pages);

        string allText = string.Join(" ", pages.SelectMany(p => p.Lines).SelectMany(l => l.Words).Select(w => w.Text));
        string? iso5817 = MatchOrNull(allText, @"ISO\s*5817\s*Quality\s*level\s*Class\s*([A-D])");

        return new DrawingResult(
            File: Path.GetFileName(path),
            FilePath: Path.GetFullPath(path),
            Sub: SubFromPath(path),
            FileHash: HashFile(path),
            DrawingNo: title.DrawingNo,
            Page: title.Sheet,
            Revision: title.Rev,
            Title: title.Title,
            TotalWeight: title.TotWeight,
            Iso5817Class: iso5817,
            MentionsNdt: Regex.IsMatch(allText, @"\bNDT\b", RegexOptions.IgnoreCase),
            MentionsSealWeld: Regex.IsMatch(allText, @"seal\s*weld", RegexOptions.IgnoreCase),
            Bom: bom);
    }

    // Loads every page of a PDF into the simplified line/word model.
    public static IReadOnlyList<PageWords> LoadPages(string path)
    {
        using var doc = PdfDocument.Open(path);
        var pages = new List<PageWords>();
        for (int i = 1; i <= doc.NumberOfPages; i++)
        {
            var page = doc.GetPage(i);
            pages.Add(new PageWords(i, page.Width, page.Height, page.GetWords()));
        }
        return pages;
    }

    static TitleBlock RunTitleChain(IReadOnlyList<PageWords> pages, string? hintNumber)
    {
        foreach (var strat in TitleStrategies)
        {
            var t = strat.TryExtract(pages, hintNumber);
            if (t is not null && t.DrawingNo is not null) return t;
        }
        return new TitleBlock(null, null, null, null, null);
    }

    static List<BomRow> RunBomChain(IReadOnlyList<PageWords> pages)
    {
        List<BomRow>? firstApplicable = null;
        foreach (var strat in BomStrategies)
            foreach (var page in pages)
            {
                var r = strat.TryExtract(page);
                if (r is null) continue;            // layout not present on this page
                if (r.Count > 0) return r.ToList();  // prefer a populated result
                firstApplicable ??= r.ToList();
            }
        return firstApplicable ?? new List<BomRow>();
    }

    // Sub identifier from the folder path, e.g. "Sub1002-2 Casing..." -> "1002-2".
    public static string? SubFromPath(string path)
    {
        foreach (var seg in Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            var m = Regex.Match(seg, @"[Ss]ub\s*(\d[\d\-&]{0,7})");
            if (m.Success) return m.Groups[1].Value.TrimEnd('-', '&');
        }
        return null;
    }

    static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    static string? MatchOrNull(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}

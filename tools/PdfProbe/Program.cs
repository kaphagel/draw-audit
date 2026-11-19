using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

if (args.Length < 1)
{
    Console.WriteLine("Usage: PdfProbe <file.pdf | folder> [maxFiles]");
    return 1;
}

var target = args[0];
int maxFiles = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 5;

var files = Directory.Exists(target)
    ? Directory.EnumerateFiles(target, "*.pdf", SearchOption.AllDirectories).Take(maxFiles).ToList()
    : new List<string> { target };

foreach (var path in files)
{
    Console.WriteLine(new string('=', 70));
    Console.WriteLine(Path.GetFileName(path));
    try
    {
        using var doc = PdfDocument.Open(path);
        int pageCount = doc.NumberOfPages;
        var page = doc.GetPage(1);
        var words = page.GetWords().ToList();
        int imageCount = page.GetImages().Count();

        Console.WriteLine($"Pages: {pageCount} | Page1 size: {page.Width:F0}x{page.Height:F0} " +
                          $"| Words on p1: {words.Count} | Images on p1: {imageCount}");
        string verdict = words.Count > 30
            ? "VECTOR/TEXT (extractable text present)"
            : imageCount > 0 ? "LIKELY SCANNED (image, little/no text)" : "UNKNOWN";
        Console.WriteLine($"Verdict: {verdict}");

        Console.WriteLine("--- first 40 words ---");
        Console.WriteLine(string.Join(" ", words.Take(40).Select(w => w.Text)));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
    }
}

return 0;

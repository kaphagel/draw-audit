using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

if (args.Length < 1)
{
    Console.WriteLine("Usage: DocxToMarkdown <input.docx> [output.md]");
    return 1;
}

string inputPath = args[0];
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {inputPath}");
    return 1;
}

string outputPath = args.Length > 1
    ? args[1]
    : Path.ChangeExtension(inputPath, ".md");

XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

using var zip = ZipFile.OpenRead(inputPath);

// Load hyperlink relationships so we can resolve external URLs.
var relationships = new Dictionary<string, string>();
var relsEntry = zip.GetEntry("word/_rels/document.xml.rels");
if (relsEntry is not null)
{
    using var relsStream = relsEntry.Open();
    var relsDoc = XDocument.Load(relsStream);
    XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
    foreach (var rel in relsDoc.Descendants(pr + "Relationship"))
    {
        var id = rel.Attribute("Id")?.Value;
        var target = rel.Attribute("Target")?.Value;
        if (id is not null && target is not null)
            relationships[id] = target;
    }
}

var docEntry = zip.GetEntry("word/document.xml")
    ?? throw new InvalidOperationException("Not a valid .docx: word/document.xml missing.");

XDocument doc;
using (var docStream = docEntry.Open())
    doc = XDocument.Load(docStream);

var body = doc.Root?.Element(w + "body")
    ?? throw new InvalidOperationException("Document body not found.");

var sb = new StringBuilder();

foreach (var element in body.Elements())
{
    if (element.Name == w + "p")
        sb.Append(ConvertParagraph(element));
    else if (element.Name == w + "tbl")
        sb.Append(ConvertTable(element));
}

File.WriteAllText(outputPath, sb.ToString().TrimEnd() + Environment.NewLine, new UTF8Encoding(false));
Console.WriteLine($"Wrote {outputPath}");
return 0;

string ConvertParagraph(XElement p)
{
    string text = ExtractRuns(p);
    var pPr = p.Element(w + "pPr");
    string styleId = pPr?.Element(w + "pStyle")?.Attribute(w + "val")?.Value ?? "";

    // Heading detection from style id (e.g. "Heading1" or "Heading 2").
    int headingLevel = GetHeadingLevel(styleId);
    if (headingLevel > 0)
    {
        if (text.Length == 0) return string.Empty;
        return $"{new string('#', Math.Min(headingLevel, 6))} {text}\n\n";
    }

    // List detection via numbering properties.
    var numPr = pPr?.Element(w + "numPr");
    if (numPr is not null)
    {
        int ilvl = int.TryParse(numPr.Element(w + "ilvl")?.Attribute(w + "val")?.Value, out var lvl) ? lvl : 0;
        string indent = new string(' ', ilvl * 2);
        bool ordered = IsOrdered(numPr);
        string bullet = ordered ? "1." : "-";
        if (text.Length == 0) return string.Empty;
        return $"{indent}{bullet} {text}\n";
    }

    if (text.Length == 0) return "\n";
    return $"{text}\n\n";
}

bool IsOrdered(XElement numPr)
{
    // Without parsing numbering.xml, default to unordered; treat decimal ids heuristically.
    // Kept simple: unordered bullets unless explicitly a decimal format is unavailable.
    return false;
}

int GetHeadingLevel(string styleId)
{
    if (string.IsNullOrEmpty(styleId)) return 0;
    string normalized = styleId.Replace(" ", "");
    if (normalized.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
    {
        string digits = normalized.Substring("Heading".Length);
        if (int.TryParse(digits, out int level)) return level;
        if (digits.Length == 0) return 1;
    }
    if (normalized.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;
    return 0;
}

string ExtractRuns(XElement container)
{
    var sb = new StringBuilder();

    foreach (var node in container.Elements())
    {
        if (node.Name == w + "hyperlink")
        {
            string inner = ExtractRuns(node);
            string? relId = node.Attribute(r + "id")?.Value;
            if (relId is not null && relationships.TryGetValue(relId, out var url) && inner.Length > 0)
                sb.Append($"[{inner}]({url})");
            else
                sb.Append(inner);
        }
        else if (node.Name == w + "r")
        {
            sb.Append(ConvertRun(node));
        }
    }

    return sb.ToString();
}

string ConvertRun(XElement run)
{
    var textParts = new StringBuilder();
    foreach (var child in run.Elements())
    {
        if (child.Name == w + "t")
            textParts.Append(child.Value);
        else if (child.Name == w + "tab")
            textParts.Append('\t');
        else if (child.Name == w + "br" || child.Name == w + "cr")
            textParts.Append("  \n");
    }

    string text = textParts.ToString();
    if (text.Length == 0) return string.Empty;

    var rPr = run.Element(w + "rPr");
    bool bold = rPr?.Element(w + "b") is XElement bEl && !IsOff(bEl);
    bool italic = rPr?.Element(w + "i") is XElement iEl && !IsOff(iEl);

    // Preserve leading/trailing spaces outside the emphasis markers.
    string trimmed = text.Trim();
    if (trimmed.Length == 0) return text;
    string lead = text[..(text.Length - text.TrimStart().Length)];
    string trail = text[text.TrimEnd().Length..];

    if (bold && italic) trimmed = $"***{trimmed}***";
    else if (bold) trimmed = $"**{trimmed}**";
    else if (italic) trimmed = $"*{trimmed}*";

    return lead + trimmed + trail;
}

bool IsOff(XElement toggle)
{
    var val = toggle.Attribute(w + "val")?.Value;
    return val is "0" or "false" or "off";
}

string ConvertTable(XElement tbl)
{
    var rows = tbl.Elements(w + "tr").ToList();
    if (rows.Count == 0) return string.Empty;

    var sb = new StringBuilder();
    var renderedRows = new List<List<string>>();
    int maxCols = 0;

    foreach (var tr in rows)
    {
        var cells = tr.Elements(w + "tc")
            .Select(tc => string.Join(" ", tc.Elements(w + "p").Select(ExtractRuns))
                .Replace("\n", " ").Replace("|", "\\|").Trim())
            .ToList();
        renderedRows.Add(cells);
        maxCols = Math.Max(maxCols, cells.Count);
    }

    if (maxCols == 0) return string.Empty;

    for (int i = 0; i < renderedRows.Count; i++)
    {
        var cells = renderedRows[i];
        while (cells.Count < maxCols) cells.Add("");
        sb.Append("| " + string.Join(" | ", cells) + " |\n");
        if (i == 0)
            sb.Append("| " + string.Join(" | ", Enumerable.Repeat("---", maxCols)) + " |\n");
    }

    sb.Append('\n');
    return sb.ToString();
}

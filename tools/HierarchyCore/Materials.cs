using System.Text.RegularExpressions;

namespace DrawAudit.Hierarchy;

// Pairs a BOM-only material line with a DL material line (a DL row without a drawing number) and
// compares their material. Lines are the same item when description and dimensions agree
// (e.g. FLAT BAR 80x12 L=400); the material is the standards + steel grades (GB/T1591 Q355B).
public static class Materials
{
    public sealed record Spec(string Key, string Material);

    public static Spec Parse(string description, string specification)
    {
        string text = Regex.Replace($"{description} {specification}".ToUpperInvariant(), @"\s*[X*]\s*(?=\d)", "x");
        text = Regex.Replace(text, @"\s*=\s*", "=");

        var standards = Regex.Matches(text, @"\b(?:GB(?:/T)?|EN|ISO|DIN|ASTM|JIS)\s*-?\s*\d+(?:-\d+)?")
            .Select(m => Regex.Replace(m.Value, @"[\s-]", "")).ToList();
        var grades = Regex.Matches(text, @"\b(?:Q\d{3}[A-Z]{0,2}|S\d{3}[A-Z0-9+]*)\b")
            .Select(m => m.Value).ToList();
        var dims = Regex.Matches(text, @"(?<![A-Z\d])\d+(?:[.,]\d+)?(?:x\d+(?:[.,]\d+)?)+|\b[LTD]=\d+(?:[.,]\d+)?")
            .Select(m => m.Value.Replace(',', '.')).OrderBy(s => s, StringComparer.Ordinal).ToList();

        // Description = the leading words before any standard/grade/dimension token.
        string desc = Regex.Match(text, @"^[A-Z][A-Z/ \-]*?(?=\s+(?:GB|EN|ISO|DIN|ASTM|JIS|Q\d|S\d{3}|\d|[LTD]=)|$)").Value.Trim();
        desc = Regex.Replace(desc, @"\s+", " ");

        string key = dims.Count == 0 || desc.Length == 0 ? "" : $"{desc}|{string.Join(" ", dims)}";
        string material = string.Join(" ", standards.Concat(grades).Distinct().OrderBy(s => s, StringComparer.Ordinal));
        return new Spec(key, material);
    }

    // The single DL material line matching the BOM line, or null when none/ambiguous.
    public static DlItemRec? Match(BomChildRec bom, IEnumerable<DlItemRec> dlMaterials)
    {
        var key = Parse(bom.Description, bom.Specification).Key;
        if (key.Length == 0) return null;
        var hits = dlMaterials.Where(d => Parse(d.Description, d.DrawingNo).Key == key).Take(2).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }
}

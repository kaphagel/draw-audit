using ClosedXML.Excel;

// Diagnostic: dump every worksheet's used range as row/col cell values so we can see
// the Delivery List layout (labels, Assembly Dwg, delivery item nr, ITP/EXC/surface fields).
if (args.Length < 1)
{
    Console.WriteLine("Usage: ExcelProbe <file.xlsx> [maxRows]");
    return 1;
}

string path = args[0];
int maxRows = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 80;

using var wb = new XLWorkbook(path);
Console.WriteLine($"File: {Path.GetFileName(path)}  | worksheets: {wb.Worksheets.Count}");

foreach (var ws in wb.Worksheets)
{
    var used = ws.RangeUsed();
    Console.WriteLine($"\n===== SHEET '{ws.Name}'  used={(used?.RangeAddress.ToString() ?? "empty")} =====");
    if (used is null) continue;

    int firstRow = used.RangeAddress.FirstAddress.RowNumber;
    int lastRow = Math.Min(used.RangeAddress.LastAddress.RowNumber, firstRow + maxRows - 1);
    int firstCol = used.RangeAddress.FirstAddress.ColumnNumber;
    int lastCol = used.RangeAddress.LastAddress.ColumnNumber;

    for (int r = firstRow; r <= lastRow; r++)
    {
        var cells = new List<string>();
        for (int c = firstCol; c <= lastCol; c++)
        {
            var cell = ws.Cell(r, c);
            string v = cell.GetFormattedString().Trim();
            if (v.Length > 0)
                cells.Add($"{XLHelper.GetColumnLetterFromNumber(c)}{r}={v}");
        }
        if (cells.Count > 0)
            Console.WriteLine(string.Join("  |  ", cells));
    }
}

return 0;

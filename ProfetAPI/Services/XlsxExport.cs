using ClosedXML.Excel;

namespace ProfetAPI.Services;

/// <summary>Arma un .xlsx simple (una hoja, encabezado con estilo) para las exportaciones de la plataforma.</summary>
public static class XlsxExport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        for (int c = 0; c < headers.Count; c++)
        {
            var h = ws.Cell(1, c + 1);
            h.Value = headers[c];
            h.Style.Font.Bold = true;
            h.Style.Font.FontColor = XLColor.White;
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
        }

        int r = 2;
        foreach (var row in rows)
        {
            for (int c = 0; c < row.Length && c < headers.Count; c++) Put(ws.Cell(r, c + 1), row[c]);
            r++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 60);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Put(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: return;
            case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; return;
            case decimal m: cell.Value = m; return;
            case int i: cell.Value = i; return;
            case long l: cell.Value = l; return;
            case double db: cell.Value = db; return;
            case bool b: cell.Value = b ? "Sí" : "No"; return;
            default:
                var text = value.ToString() ?? "";
                // Un texto que empieza con = + - @ Excel lo interpreta como fórmula: se neutraliza (inyección de fórmulas).
                if (text.Length > 0 && "=+-@".Contains(text[0])) text = "'" + text;
                cell.Value = text;
                return;
        }
    }
}

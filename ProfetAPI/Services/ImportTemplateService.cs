using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Leads;

namespace ProfetAPI.Services;

public interface IImportTemplateService
{
    /// <summary>Archivo de ejemplo descargable para importar "leads" o "deals", con instrucciones y los catálogos de la cuenta.</summary>
    Task<(byte[] bytes, string contentType, string fileName)> BuildAsync(string entity, int? accountId, string format, CancellationToken ct = default);
}

public class ImportTemplateService(ApplicationDbContext db) : IImportTemplateService
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<(byte[] bytes, string contentType, string fileName)> BuildAsync(string entity, int? accountId, string format, CancellationToken ct = default)
    {
        var isDeals = entity == "deals";
        var labels = DealImportFields.LabelsFor(entity);
        var fields = DealImportFields.For(entity);
        var headers = fields.Select(f => labels[f]).ToList();

        var sources = await db.ProspectSources.AsNoTracking().OrderBy(s => s.SourceId).Select(s => s.Name).ToListAsync(ct);
        var stages = new List<string>();
        if (isDeals && accountId.HasValue)
            stages = await db.Stages.AsNoTracking().Where(s => s.Funnel.AccountId == accountId.Value).OrderBy(s => s.Order).Select(s => s.Name).ToListAsync(ct);
        if (isDeals && stages.Count == 0) stages = new List<string> { "Nuevo", "Contactado", "Propuesta", "Negociación", "Cierre" };

        var examples = isDeals ? DealExamples(stages, sources) : LeadExamples(sources);
        var baseName = isDeals ? "plantilla_oportunidades" : "plantilla_prospectos";

        if (format == "csv")
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(Csv)));
            foreach (var row in examples) sb.AppendLine(string.Join(",", row.Select(Csv)));
            var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return (bytes, "text/csv; charset=utf-8", baseName + ".csv");
        }

        using var wb = new XLWorkbook();

        // ── Hoja 1: Instrucciones ─────────────────────────────────────────────
        var ins = wb.Worksheets.Add("Instrucciones");
        var lines = isDeals ? DealInstructions(headers) : LeadInstructions(headers);
        for (int i = 0; i < lines.Count; i++)
        {
            var cell = ins.Cell(i + 1, 1);
            cell.Value = lines[i].text;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            if (lines[i].kind == "title") { cell.Style.Font.Bold = true; cell.Style.Font.FontSize = 16; cell.Style.Font.FontColor = XLColor.FromHtml("#0F766E"); }
            else if (lines[i].kind == "h") { cell.Style.Font.Bold = true; cell.Style.Font.FontSize = 12; }
        }
        ins.Column(1).Width = 110;

        // ── Hoja 2: Plantilla (los datos se leen de aquí) ──────────────────────
        var tpl = wb.Worksheets.Add("Plantilla");
        for (int c = 0; c < headers.Count; c++)
        {
            var h = tpl.Cell(1, c + 1);
            h.Value = headers[c];
            h.Style.Font.Bold = true;
            h.Style.Font.FontColor = XLColor.White;
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
            tpl.Column(c + 1).Width = Math.Max(18, headers[c].Length + 6);
            tpl.Column(c + 1).Style.NumberFormat.Format = "@"; // todo como texto: no se pierden ceros ni se cambian las fechas
        }
        for (int r = 0; r < examples.Count; r++)
            for (int c = 0; c < examples[r].Count; c++)
                tpl.Cell(r + 2, c + 1).Value = examples[r][c];
        tpl.SheetView.FreezeRows(1);

        // ── Hoja 3: Catálogos ───────────────────────────────────────────────────
        var cat = wb.Worksheets.Add("Catálogos");
        void Column(int col, string title, IEnumerable<string> values)
        {
            var h = cat.Cell(1, col); h.Value = title; h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#CCFBF1");
            int r = 2; foreach (var v in values) cat.Cell(r++, col).Value = v;
            cat.Column(col).Width = Math.Max(24, title.Length + 4);
        }
        int catCol = 1;
        var catalogCols = new Dictionary<string, (int col, int count)>();
        void AddCatalog(string key, string title, List<string> values) { Column(catCol, title, values); catalogCols[key] = (catCol, values.Count); catCol++; }

        if (isDeals)
        {
            AddCatalog("stage", "Etapa (embudo de la cuenta)", stages);
            AddCatalog("status", "Estatus", DealImportService.Statuses.ToList());
            AddCatalog("type", "Tipo de oportunidad", DealImportService.DealTypeLabels.ToList());
        }
        AddCatalog("source", "Fuente", sources);
        cat.Cell(1, catCol + 1).Value = "Estos son los valores válidos. Cópialos tal cual en la hoja Plantilla.";
        cat.Cell(1, catCol + 1).Style.Font.Italic = true;

        // Listas desplegables en la Plantilla (la persona elige en vez de escribir)
        void Dropdown(string field, string key)
        {
            var idx = Array.IndexOf(fields, field);
            if (idx < 0 || !catalogCols.TryGetValue(key, out var info) || info.count == 0) return;
            var range = tpl.Range(2, idx + 1, 1001, idx + 1);
            var src = cat.Range(2, info.col, info.count + 1, info.col);
            var dv = range.CreateDataValidation();
            dv.List(src, true);
            dv.IgnoreBlanks = true;
            dv.ShowErrorMessage = false; // solo ayuda: el sistema valida y corrige al importar
        }
        if (isDeals) { Dropdown("stage", "stage"); Dropdown("status", "status"); Dropdown("dealType", "type"); }
        Dropdown("prospectSource", "source");

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), Xlsx, baseName + ".xlsx");
    }

    private static string Csv(string v) => v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    private static List<List<string>> LeadExamples(List<string> sources)
    {
        string S(int i) => sources.Count > i ? sources[i] : "Facebook";
        return new()
        {
            new() { "Laura Gómez", "laura.gomez@ejemplo.com", "5512345678", "Constructora Gómez", "Directora Comercial", "Monterrey", S(0), "Quiero una cotización para 20 equipos" },
            new() { "Carlos Pérez", "carlos.perez@ejemplo.com", "8181234567", "Pérez y Asociados", "Gerente de Compras", "Guadalajara", S(4), "" },
        };
    }

    private static List<List<string>> DealExamples(List<string> stages, List<string> sources)
    {
        string St(int i) => stages.Count > i ? stages[i] : stages[^1];
        string S(int i) => sources.Count > i ? sources[i] : "Facebook";
        return new()
        {
            new() { "Equipos de oficina — Constructora Gómez", "45000", "Constructora Gómez", "Laura Gómez", "laura.gomez@ejemplo.com", "5512345678", St(0), "Abierto", "2026-12-15", "Nuevo negocio", S(0) },
            new() { "Renovación de licencias — Pérez y Asociados", "18500.50", "Pérez y Asociados", "Carlos Pérez", "carlos.perez@ejemplo.com", "8181234567", St(Math.Min(2, stages.Count - 1)), "Abierto", "2026-11-30", "Renovación", S(4) },
        };
    }

    private static List<(string kind, string text)> LeadInstructions(List<string> headers) => new()
    {
        ("title", "Cómo importar prospectos"),
        ("p", ""),
        ("h", "Paso a paso"),
        ("p", "1. Ve a la hoja \"Plantilla\": ahí están los encabezados y 2 filas de ejemplo."),
        ("p", "2. Borra las filas de ejemplo y pega tus prospectos, uno por fila. No cambies el orden ni el nombre de los encabezados (aunque el sistema también reconoce otros nombres)."),
        ("p", "3. Guarda el archivo como Excel (.xlsx) o CSV y súbelo en Prospectos > Importar."),
        ("p", "4. Confirma a qué campo corresponde cada columna (la IA lo sugiere por ti) y revisa los errores que se marquen."),
        ("p", "5. Elige qué hacer con duplicados y si quieres que la IA califique los prospectos. Listo."),
        ("p", ""),
        ("h", "Reglas de cada columna"),
        ("p", $"• {headers[0]} o {headers[1]}: al menos uno es obligatorio en cada fila."),
        ("p", $"• {headers[1]}: formato nombre@dominio.com. Si el correo ya existe en la cuenta, la fila se omite (o se crea, según elijas)."),
        ("p", $"• {headers[2]}: entre 8 y 15 dígitos; puedes escribirlo con espacios o guiones."),
        ("p", $"• {headers[6]}: usa un valor de la hoja \"Catálogos\" para que tus reportes por fuente salgan completos. Si lo dejas vacío se guarda como \"Importación\"."),
        ("p", "• Todas las demás columnas son opcionales."),
        ("p", ""),
        ("h", "Límites y consejos"),
        ("p", "• Hasta 2,000 filas por archivo."),
        ("p", "• Antes de importar verás un resumen: las filas con error se pueden corregir ahí mismo (la IA propone la corrección) o se omiten."),
        ("p", "• Los prospectos importados reciben la secuencia de tareas de tu cuenta, igual que uno creado a mano."),
    };

    private static List<(string kind, string text)> DealInstructions(List<string> headers) => new()
    {
        ("title", "Cómo importar oportunidades"),
        ("p", ""),
        ("h", "Paso a paso"),
        ("p", "1. Ve a la hoja \"Plantilla\": ahí están los encabezados y 2 filas de ejemplo."),
        ("p", "2. Borra las filas de ejemplo y pega tus oportunidades, una por fila. Las columnas con lista (Etapa, Estatus, Tipo, Fuente) se pueden elegir del menú desplegable."),
        ("p", "3. Guarda el archivo como Excel (.xlsx) o CSV y súbelo en Oportunidades > Importar."),
        ("p", "4. Confirma a qué campo corresponde cada columna (la IA lo sugiere por ti) y corrige lo que se marque."),
        ("p", ""),
        ("h", "Reglas de cada columna"),
        ("p", $"• {headers[0]} o {headers[2]}: al menos uno es obligatorio. Si solo pones la empresa, el nombre se arma solo."),
        ("p", $"• {headers[1]}: solo número; se aceptan formatos como 15000, 15,000.50 o $15,000."),
        ("p", $"• {headers[2]}: si la empresa ya existe en la cuenta se reutiliza; si no, se crea."),
        ("p", $"• {headers[4]}: sirve para ligar o crear el contacto. Si ya existe un contacto con ese correo, se reutiliza."),
        ("p", $"• {headers[6]}: debe ser una etapa del embudo de la cuenta (ver hoja \"Catálogos\"). Si la dejas vacía, entra en la primera etapa."),
        ("p", $"• {headers[7]}: Abierto, Ganado o Perdido. Vacío = Abierto."),
        ("p", $"• {headers[8]}: formato año-mes-día (2026-12-31) o día/mes/año (31/12/2026)."),
        ("p", $"• {headers[9]}: Nuevo negocio, Venta adicional o Renovación. Vacío = Nuevo negocio."),
        ("p", ""),
        ("h", "Límites y consejos"),
        ("p", "• Hasta 2,000 filas por archivo."),
        ("p", "• Una oportunidad con el mismo nombre y la misma empresa que otra de la cuenta se considera duplicada y se omite (o se crea, según elijas)."),
        ("p", "• Las oportunidades se crean en la etapa que indicas y no disparan las tareas automáticas de esa etapa."),
        ("p", "• Los catálogos de la hoja \"Catálogos\" son los de la cuenta seleccionada; descarga la plantilla con la cuenta correcta."),
    };
}

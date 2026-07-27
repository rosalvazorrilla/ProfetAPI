using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Leads;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

public interface ILeadImportService
{
    Task<ParsedFileResult> ParseFileAsync(Stream stream, string fileName, CancellationToken ct = default);
    Task<SuggestMappingResultDto> SuggestMappingAsync(SuggestMappingRequestDto req, CancellationToken ct = default);
    Task<CommitImportResultDto> CommitAsync(CommitImportRequestDto req, int accountId, string? ownerUserId, CancellationToken ct = default);
}

/// <summary>
/// P2: importación de leads desde CSV/Excel. La IA solo sugiere el mapeo de columnas
/// (texto → campo); nunca decide qué se guarda — el usuario confirma antes de crear nada.
/// </summary>
public class LeadImportService(
    ApplicationDbContext db, IAiClient ai, PlaybookService playbooks,
    ITimelineLogger timeline, IScoringAiService scoringAi,
    IServiceScopeFactory scopeFactory, ILogger<LeadImportService> logger) : ILeadImportService
{
    private const int MaxRows = 2000;

    // ── Parseo de archivo (sin persistir nada) ────────────────────────────────
    public async Task<ParsedFileResult> ParseFileAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".csv" => await ParseCsvAsync(stream, ct),
            ".xlsx" or ".xls" => ParseExcel(stream),
            _ => throw new InvalidOperationException("Formato no soportado. Usa .csv o .xlsx."),
        };
    }

    private static async Task<ParsedFileResult> ParseCsvAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            if (lines.Count > MaxRows + 1) break;
        }
        if (lines.Count == 0) return new ParsedFileResult();

        var columns = SplitCsvLine(lines[0]);
        var result = new ParsedFileResult { Columns = columns, TotalRows = lines.Count - 1, Truncated = lines.Count - 1 > MaxRows };

        foreach (var l in lines.Skip(1).Take(MaxRows))
        {
            var values = SplitCsvLine(l);
            var row = new Dictionary<string, string>();
            for (int i = 0; i < columns.Count; i++)
                row[columns[i]] = i < values.Count ? values[i] : "";
            result.Rows.Add(row);
        }
        return result;
    }

    /// <summary>Parser CSV simple con soporte de comillas ("campo, con coma").</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString().Trim());
        return fields;
    }

    private static ParsedFileResult ParseExcel(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var usedRange = ws.RangeUsed();
        if (usedRange == null) return new ParsedFileResult();

        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count == 0) return new ParsedFileResult();

        var headerRow = rows[0];
        var columns = headerRow.Cells().Select(c => c.GetString().Trim()).Where(s => s.Length > 0).ToList();

        var result = new ParsedFileResult
        {
            Columns = columns,
            TotalRows = rows.Count - 1,
            Truncated = rows.Count - 1 > MaxRows,
        };

        foreach (var r in rows.Skip(1).Take(MaxRows))
        {
            var row = new Dictionary<string, string>();
            for (int i = 0; i < columns.Count; i++)
                row[columns[i]] = r.Cell(i + 1).GetString().Trim();
            result.Rows.Add(row);
        }
        return result;
    }

    // ── F2-T2: mapeo de columnas sugerido por IA ──────────────────────────────
    public async Task<SuggestMappingResultDto> SuggestMappingAsync(SuggestMappingRequestDto req, CancellationToken ct = default)
    {
        if (!ai.IsConfigured || req.Columns.Count == 0) return new SuggestMappingResultDto();

        var fieldsText = string.Join(", ", LeadImportFields.All.Select(f => $"{f} ({LeadImportFields.Labels[f]})"));
        var system = $"""
Recibes las columnas de un archivo de prospectos y una muestra de filas. Para cada columna, decide a cuál de
estos campos corresponde: {fieldsText}. Si una columna no corresponde a ninguno, usa "" (cadena vacía).
No inventes campos fuera de la lista. Responde en español.
""";
        var schema = """
{"type":"object","additionalProperties":false,"required":["mapping"],"properties":{"mapping":{"type":"object","additionalProperties":{"type":"string"}}}}
""";
        var sb = new StringBuilder();
        sb.AppendLine("Columnas: " + string.Join(" | ", req.Columns));
        sb.AppendLine("Muestra de filas:");
        foreach (var row in req.SampleRows.Take(5))
            sb.AppendLine(JsonSerializer.Serialize(row));

        try
        {
            var json = await ai.CompleteJsonAsync(system, sb.ToString(), schema, ct);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<SuggestMappingResultDto>(json, opts) ?? new();

            // Anti-alucinación: solo aceptar campos válidos, y solo columnas que realmente vienen del archivo
            var valid = new HashSet<string>(LeadImportFields.All);
            var cleaned = parsed.Mapping
                .Where(kv => req.Columns.Contains(kv.Key) && (string.IsNullOrEmpty(kv.Value) || valid.Contains(kv.Value)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            return new SuggestMappingResultDto { Mapping = cleaned };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo sugerir el mapeo de columnas");
            return new SuggestMappingResultDto();
        }
    }

    // ── P2-T3/T4: crear los leads (transaccional, dedup) + enriquecimiento IA ─
    public async Task<CommitImportResultDto> CommitAsync(CommitImportRequestDto req, int accountId, string? ownerUserId, CancellationToken ct = default)
    {
        var result = new CommitImportResultDto();
        var newLeadIds = new List<long>();

        using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Emails/teléfonos ya existentes en la cuenta, para deduplicar
            var existingEmails = req.DuplicateStrategy == "skip"
                ? (await db.Leads.Where(l => l.AccountId == accountId && l.Deleted != true && l.Email != null)
                    .Select(l => l.Email!).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? Get(Dictionary<string, string> row, string field)
            {
                if (!req.Mapping.TryGetValue(field, out var col) || string.IsNullOrEmpty(col)) return null;
                return row.TryGetValue(col, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            }

            foreach (var row in req.Rows)
            {
                try
                {
                    var email = Get(row, "email");
                    var name  = Get(row, "name");
                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
                        continue; // fila vacía / sin datos útiles, se omite en silencio

                    if (!string.IsNullOrWhiteSpace(email) && existingEmails.Contains(email))
                    {
                        result.Duplicates++;
                        continue;
                    }

                    var lead = new Lead
                    {
                        AccountId      = accountId,
                        CampaignId     = 0,
                        Name           = name ?? email,
                        Email          = email,
                        Phone          = Get(row, "phone"),
                        Company        = Get(row, "company"),
                        Position       = Get(row, "position"),
                        City           = Get(row, "city"),
                        ProspectSource = Get(row, "prospectSource") ?? "Importación",
                        InitialMessage = Get(row, "initialMessage"),
                        Status         = "Nuevo",
                        OriginType     = "Import",
                        OwnerUserId    = ownerUserId,
                        Active         = true,
                        Deleted        = false,
                        CreatedOn      = DateTime.UtcNow,
                    };
                    db.Leads.Add(lead);
                    await db.SaveChangesAsync(ct);

                    if (!string.IsNullOrWhiteSpace(email)) existingEmails.Add(email);
                    newLeadIds.Add(lead.LeadId);
                    result.Created++;

                    await timeline.LogAsync(accountId, "Lead", lead.LeadId, "lead_created",
                        "Prospecto importado", detail: "Origen: importación de archivo", userId: ownerUserId);
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    if (result.ErrorDetails.Count < 20) result.ErrorDetails.Add(ex.Message);
                }
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Aplicar playbook predeterminado + enriquecimiento/auto-scoring en background (no bloquea la respuesta)
        if (newLeadIds.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var scopedDb        = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var scopedPlaybooks = scope.ServiceProvider.GetRequiredService<PlaybookService>();
                var scopedScoringAi = scope.ServiceProvider.GetRequiredService<IScoringAiService>();

                foreach (var leadId in newLeadIds)
                {
                    try
                    {
                        await scopedPlaybooks.ApplyDefaultAsync(accountId, leadId, ownerUserId);
                        if (req.EnrichWithAi && scopedScoringAi.IsConfigured)
                            await scopedScoringAi.ScoreAndPersistAsync(leadId);
                    }
                    catch { /* best-effort: un lead que falle no debe tumbar el resto */ }
                }
            }, CancellationToken.None);
        }

        return result;
    }
}

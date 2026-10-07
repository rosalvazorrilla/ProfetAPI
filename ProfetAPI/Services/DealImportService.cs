using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Leads;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

public interface IDealImportService
{
    Task<ValidateImportResultDto> ValidateAsync(ValidateImportRequestDto req, int accountId, CancellationToken ct = default);
    Task<CommitImportResultDto> CommitAsync(CommitImportRequestDto req, int accountId, string? ownerUserId, CancellationToken ct = default);
}

/// <summary>
/// Importación de oportunidades desde CSV/Excel. Mismo criterio que la de prospectos: la IA solo sugiere
/// (mapeo y correcciones), el usuario confirma, y las filas con error sin resolver se omiten.
/// No dispara las secuencias de tareas por etapa: lo importado ya viene en la etapa que trae el archivo.
/// </summary>
public class DealImportService(ApplicationDbContext db, ITimelineLogger timeline) : IDealImportService
{
    public static readonly string[] Statuses = { "Abierto", "Ganado", "Perdido" };
    public static readonly string[] DealTypeLabels = { "Nuevo negocio", "Venta adicional", "Renovación" };

    private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.Compiled);

    // ── Normalización y conversiones tolerantes ─────────────────────────────────
    public static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var formD = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Regex.Replace(sb.ToString(), @"\s+", " ");
    }

    public static bool TryParseAmount(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = Regex.Replace(text, @"[^\d.,\-]", "");
        if (t.Length == 0) return false;
        if (t.Contains(',') && t.Contains('.')) t = t.Replace(",", "");                 // 1,200.50
        else if (Regex.IsMatch(t, @"^\-?\d{1,3}(,\d{3})+$")) t = t.Replace(",", "");     // 1,200
        else t = t.Replace(',', '.');                                                    // 1200,50
        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static readonly string[] DateFormats =
        { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy/MM/dd", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss" };

    public static bool TryParseDate(string? text, out DateTime value) =>
        DateTime.TryParseExact((text ?? "").Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    public static string? MapStatus(string? text) => Norm(text) switch
    {
        "abierto" or "abierta" or "open" => "Abierto",
        "ganado" or "ganada" or "won" or "cerrado ganado" => "Ganado",
        "perdido" or "perdida" or "lost" or "cerrado perdido" => "Perdido",
        _ => null,
    };

    public static string? MapDealType(string? text) => Norm(text) switch
    {
        "nuevo negocio" or "newbusiness" or "new business" or "nuevo" => "NewBusiness",
        "venta adicional" or "upsell" or "up sell" => "Upsell",
        "renovacion" or "renewal" => "Renewal",
        _ => null,
    };

    private static bool Empty(string? s) => string.IsNullOrWhiteSpace(s);

    // ── Contexto del embudo de la cuenta ────────────────────────────────────────
    private async Task<List<(int id, string name)>> StagesAsync(int accountId, CancellationToken ct) =>
        (await db.Stages.AsNoTracking().Where(s => s.Funnel.AccountId == accountId).OrderBy(s => s.Order)
            .Select(s => new { s.StageId, s.Name }).ToListAsync(ct)).Select(s => (s.StageId, s.Name)).ToList();

    // ── Revisión previa ──────────────────────────────────────────────────────────
    public async Task<ValidateImportResultDto> ValidateAsync(ValidateImportRequestDto req, int accountId, CancellationToken ct = default)
    {
        var result = new ValidateImportResultDto { TotalRows = req.Rows.Count };
        var stages = await StagesAsync(accountId, ct);
        var stageNames = stages.Select(s => s.name).ToList();

        var existing = new HashSet<string>();
        if (req.DuplicateStrategy == "skip")
        {
            var rows = await db.Deals.AsNoTracking().Where(d => d.AccountId == accountId)
                .Select(d => new { d.DealName, Company = d.Company != null ? d.Company.Name : null }).ToListAsync(ct);
            foreach (var r in rows) existing.Add(Norm(r.DealName) + "|" + Norm(r.Company));
        }

        string? Get(Dictionary<string, string> row, string field)
        {
            if (!req.Mapping.TryGetValue(field, out var col) || string.IsNullOrEmpty(col)) return null;
            return row.TryGetValue(col, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        }

        for (int i = 0; i < req.Rows.Count; i++)
        {
            var row = req.Rows[i];
            var rowNumber = i + 2;
            if (!req.Mapping.Keys.Any(f => Get(row, f) != null)) { result.EmptyRows++; continue; }

            var before = result.Issues.Count;
            void Add(string field, string code, string message, string value, List<string>? allowed = null) =>
                result.Issues.Add(new ImportIssueDto { RowIndex = i, RowNumber = rowNumber, Field = field, Code = code, Message = message, Value = value, Allowed = allowed });

            var name = Get(row, "dealName"); var company = Get(row, "company");
            if (name == null && company == null)
                Add("dealName", "missing_identity", "La fila no tiene nombre de oportunidad ni empresa.", "");

            var amount = Get(row, "amount");
            if (amount != null && !TryParseAmount(amount, out _))
                Add("amount", "invalid_amount", "El monto debe ser un número (ej. 15000 o 15,000.50).", amount);

            var stage = Get(row, "stage");
            if (stage != null && !stageNames.Any(n => Norm(n) == Norm(stage)))
                Add("stage", "invalid_stage", "La etapa no existe en el embudo de esta cuenta.", stage, stageNames);

            var status = Get(row, "status");
            if (status != null && MapStatus(status) == null)
                Add("status", "invalid_status", "El estatus debe ser Abierto, Ganado o Perdido.", status, Statuses.ToList());

            var type = Get(row, "dealType");
            if (type != null && MapDealType(type) == null)
                Add("dealType", "invalid_type", "El tipo debe ser Nuevo negocio, Venta adicional o Renovación.", type, DealTypeLabels.ToList());

            var date = Get(row, "closeDate");
            if (date != null && !TryParseDate(date, out _))
                Add("closeDate", "invalid_date", "La fecha debe verse así: 2026-12-31 (año-mes-día) o 31/12/2026.", date);

            var email = Get(row, "contactEmail");
            if (email != null && !EmailRx.IsMatch(email))
                Add("contactEmail", "invalid_email", "El correo del contacto no tiene un formato válido.", email);

            var phone = Get(row, "contactPhone");
            if (phone != null && (phone.Count(char.IsDigit) is < 8 or > 15))
                Add("contactPhone", "invalid_phone", "El teléfono debe tener entre 8 y 15 dígitos.", phone);

            if (result.Issues.Count > before) { result.RowsWithIssues++; continue; }

            var key = Norm(name ?? $"{company}") + "|" + Norm(company);
            if (existing.Contains(key)) { result.Duplicates++; continue; }
            existing.Add(key);
            result.ValidRows++;
        }
        return result;
    }

    // ── Importación ──────────────────────────────────────────────────────────────
    public async Task<CommitImportResultDto> CommitAsync(CommitImportRequestDto req, int accountId, string? ownerUserId, CancellationToken ct = default)
    {
        var result = new CommitImportResultDto();
        var stages = await StagesAsync(accountId, ct);
        var firstStageId = stages.Count > 0 ? (int?)stages[0].id : null;

        string? Get(Dictionary<string, string> row, string field)
        {
            if (!req.Mapping.TryGetValue(field, out var col) || string.IsNullOrEmpty(col)) return null;
            return row.TryGetValue(col, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        }

        var companyIds = new Dictionary<string, int>();   // nombre normalizado → CompanyId
        foreach (var c in await db.Companies.AsNoTracking().Where(c => c.AccountId == accountId).Select(c => new { c.CompanyId, c.Name }).ToListAsync(ct))
            companyIds.TryAdd(Norm(c.Name), c.CompanyId);
        var contactIds = new Dictionary<string, int>();   // correo en minúsculas → ContactId
        foreach (var c in await db.Contacts.AsNoTracking().Where(c => c.AccountId == accountId && c.Email != null).Select(c => new { c.ContactId, c.Email }).ToListAsync(ct))
            contactIds.TryAdd(c.Email!.ToLowerInvariant(), c.ContactId);

        var existing = new HashSet<string>();
        if (req.DuplicateStrategy == "skip")
        {
            var rows = await db.Deals.AsNoTracking().Where(d => d.AccountId == accountId)
                .Select(d => new { d.DealName, Company = d.Company != null ? d.Company.Name : null }).ToListAsync(ct);
            foreach (var r in rows) existing.Add(Norm(r.DealName) + "|" + Norm(r.Company));
        }

        using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var row in req.Rows)
            {
                try
                {
                    var companyName = Get(row, "company");
                    var contactName = Get(row, "contactName");
                    var contactEmail = Get(row, "contactEmail")?.ToLowerInvariant();
                    var name = Get(row, "dealName")
                        ?? (companyName != null ? $"{companyName} — {(contactName ?? "Oportunidad").Split(' ')[0]}" : null);
                    if (name == null) continue;

                    var dedupKey = Norm(name) + "|" + Norm(companyName);
                    if (existing.Contains(dedupKey)) { result.Duplicates++; continue; }

                    // Empresa (se reutiliza por nombre dentro de la cuenta)
                    int? companyId = null;
                    if (companyName != null)
                    {
                        if (!companyIds.TryGetValue(Norm(companyName), out var cid))
                        {
                            var company = new Company { AccountId = accountId, Name = companyName, LifecycleStatus = "Prospecto", CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow };
                            db.Companies.Add(company);
                            await db.SaveChangesAsync(ct);
                            cid = company.CompanyId;
                            companyIds[Norm(companyName)] = cid;
                        }
                        companyId = cid;
                    }

                    // Contacto (por correo dentro de la cuenta; si no trae correo, se crea uno nuevo si trae nombre)
                    int? contactId = null;
                    if (contactEmail != null && contactIds.TryGetValue(contactEmail, out var existingContact)) contactId = existingContact;
                    else if (contactEmail != null || contactName != null)
                    {
                        var parts = (contactName ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        var contact = new Contact
                        {
                            AccountId = accountId, CompanyId = companyId,
                            FirstName = parts.Length > 0 ? parts[0] : contactEmail, LastName = parts.Length > 1 ? parts[1] : null,
                            Email = contactEmail, PhoneNumber = Get(row, "contactPhone"),
                            LifecycleStatus = "Lead", CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow,
                        };
                        db.Contacts.Add(contact);
                        await db.SaveChangesAsync(ct);
                        contactId = contact.ContactId;
                        if (contactEmail != null) contactIds[contactEmail] = contact.ContactId;
                    }

                    var stageText = Get(row, "stage");
                    var stageId = stageText != null ? stages.FirstOrDefault(s => Norm(s.name) == Norm(stageText)).id : 0;
                    var amountText = Get(row, "amount");
                    var dateText = Get(row, "closeDate");

                    var deal = new Deal
                    {
                        DealName = name, AccountId = accountId, CompanyId = companyId, PrimaryContactId = contactId,
                        StageId = stageId != 0 ? stageId : firstStageId,
                        Status = MapStatus(Get(row, "status")) ?? "Abierto",
                        DealType = MapDealType(Get(row, "dealType")) ?? "NewBusiness",
                        QuotedAmount = amountText != null && TryParseAmount(amountText, out var amt) ? amt : null,
                        CloseDate = dateText != null && TryParseDate(dateText, out var cd) ? DateTime.SpecifyKind(cd, DateTimeKind.Utc) : null,
                        ProspectSource = Get(row, "prospectSource"),
                        OriginType = "Import",
                        CreatedOn = DateTime.UtcNow,
                    };
                    db.Deals.Add(deal);
                    await db.SaveChangesAsync(ct);

                    if (!string.IsNullOrEmpty(ownerUserId))
                    {
                        db.DealUsers.Add(new DealUser { DealId = deal.DealId, UserId = ownerUserId, RoleInDeal = "Owner" });
                        await db.SaveChangesAsync(ct);
                    }

                    existing.Add(dedupKey);
                    result.Created++;
                    await timeline.LogAsync(accountId, "Deal", deal.DealId, "deal_created",
                        "Oportunidad importada", detail: "Origen: importación de archivo", userId: ownerUserId);
                }
                catch (Exception ex)
                {
                    db.ChangeTracker.Clear();
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
        return result;
    }
}

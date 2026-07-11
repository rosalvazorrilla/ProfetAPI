using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>
/// Motor que ejecuta los pasos de una AutomationRule en orden.
/// Recibe un contexto de campos (string→string) y los transforma/envía según cada paso.
/// </summary>
public class AutomationExecutorService(
    ApplicationDbContext db,
    IHttpClientFactory httpFactory,
    IEmailService emailService,
    ILogger<AutomationExecutorService> logger)
{
    // ── Public entry points ──────────────────────────────────────────────────

    /// <summary>Dispara todas las reglas activas para una cuenta que coincidan con el triggerType.</summary>
    public async Task FireAsync(int accountId, string triggerType, Dictionary<string, string> fields)
    {
        var rules = await db.AutomationRules
            .Where(r => r.AccountId == accountId && r.TriggerType == triggerType
                     && r.IsActive && !r.Deleted)
            .Include(r => r.Steps.Where(s => s.IsActive).OrderBy(s => s.StepOrder))
            .ToListAsync();

        foreach (var rule in rules)
        {
            if (!EvaluateConditions(rule.ConditionsJson, fields)) continue;
            await ExecuteRuleAsync(rule, new Dictionary<string, string>(fields));
        }
    }

    /// <summary>Ejecuta una regla específica (para webhooks entrantes con clave).</summary>
    public async Task ExecuteRuleAsync(AutomationRule rule, Dictionary<string, string> fields)
    {
        var stepsResult = new List<object>();
        var success     = true;
        string? error   = null;

        foreach (var step in rule.Steps.Where(s => s.IsActive).OrderBy(s => s.StepOrder))
        {
            try
            {
                var result = await ExecuteStepAsync(step, fields, rule.AccountId);
                stepsResult.Add(new { step = step.StepType, order = step.StepOrder, ok = result });
                if (!result) { success = false; break; }
            }
            catch (Exception ex)
            {
                error   = ex.Message;
                success = false;
                stepsResult.Add(new { step = step.StepType, order = step.StepOrder, ok = false, error = ex.Message });
                logger.LogWarning(ex, "Automation {RuleId} step {StepType} failed", rule.RuleId, step.StepType);
                break;
            }
        }

        db.AutomationLogs.Add(new AutomationLog
        {
            RuleId          = rule.RuleId,
            ExecutedAt      = DateTime.UtcNow,
            Success         = success,
            StepsResultJson = JsonSerializer.Serialize(stepsResult),
            ErrorMessage    = error,
            PayloadPreview  = JsonSerializer.Serialize(fields)[..Math.Min(500, JsonSerializer.Serialize(fields).Length)],
        });
        await db.SaveChangesAsync();
    }

    // ── Step dispatcher ──────────────────────────────────────────────────────

    private async Task<bool> ExecuteStepAsync(AutomationStep step, Dictionary<string, string> fields, int accountId)
    {
        var cfg = string.IsNullOrWhiteSpace(step.ConfigJson)
            ? new JsonElement()
            : JsonSerializer.Deserialize<JsonElement>(step.ConfigJson);

        return step.StepType switch
        {
            "Formatter"    => ExecuteFormatter(cfg, fields),
            "InsertLead"   => await ExecuteInsertLeadAsync(cfg, fields, accountId),
            "HttpPost"     => await ExecuteHttpPostAsync(cfg, fields),
            "Email"        => await ExecuteEmailAsync(cfg, fields, accountId),
            "Notification" => await ExecuteNotificationAsync(cfg, fields, accountId),
            _              => true,
        };
    }

    // ── Formatter ────────────────────────────────────────────────────────────

    private static bool ExecuteFormatter(JsonElement cfg, Dictionary<string, string> fields)
    {
        if (!cfg.TryGetProperty("mappings", out var mappings)) return true;

        foreach (var m in mappings.EnumerateArray())
        {
            var from  = m.TryGetProperty("from",  out var f) ? f.GetString() ?? "" : "";
            var to    = m.TryGetProperty("to",    out var t) ? t.GetString() ?? "" : "";
            var def   = m.TryGetProperty("default", out var d) ? d.GetString() : null;

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;

            if (fields.TryGetValue(from, out var val))
            {
                fields[to] = val;
                if (from != to) fields.Remove(from);
            }
            else if (def != null)
            {
                fields[to] = def;
            }
        }
        return true;
    }

    // ── InsertLead ───────────────────────────────────────────────────────────

    private async Task<bool> ExecuteInsertLeadAsync(JsonElement cfg, Dictionary<string, string> fields, int accountId)
    {
        int? stageId = cfg.TryGetProperty("defaultStageId", out var sid) && sid.ValueKind != JsonValueKind.Null
            ? sid.GetInt32() : null;
        string? ownerId = cfg.TryGetProperty("defaultOwnerId", out var oid) ? oid.GetString() : null;

        var lead = new Lead
        {
            AccountId      = accountId,
            CampaignId     = 0,
            Name           = Get(fields, "name", "email", "full_name"),
            Email          = Get(fields, "email"),
            Phone          = Get(fields, "phone", "phone_number", "teléfono"),
            Company        = Get(fields, "company", "empresa"),
            Position       = Get(fields, "position", "cargo", "position"),
            City           = Get(fields, "city", "ciudad"),
            ProspectSource = Get(fields, "prospectSource", "prospect_source", "fuente", "source"),
            CampaignName   = Get(fields, "campaignName", "campaign_name", "campaña"),
            AdName         = Get(fields, "adName", "ad_name"),
            InitialMessage = Get(fields, "message", "initialMessage", "comentarios"),
            StageId        = stageId,
            OwnerUserId    = ownerId,
            Status         = "Nuevo",
            OriginType     = "Automation",
            Active         = true,
            Deleted        = false,
            CreatedOn      = DateTime.UtcNow,
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        // Poner el leadId en el contexto para pasos posteriores
        fields["_leadId"] = lead.LeadId.ToString();
        return true;
    }

    // ── HttpPost ─────────────────────────────────────────────────────────────

    private async Task<bool> ExecuteHttpPostAsync(JsonElement cfg, Dictionary<string, string> fields)
    {
        if (!cfg.TryGetProperty("url", out var urlEl)) return false;
        var url = urlEl.GetString();
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Build payload: if "fields" specified → only those; else entire context
        Dictionary<string, string> payload;
        if (cfg.TryGetProperty("fields", out var flds) && flds.ValueKind == JsonValueKind.Array)
        {
            payload = flds.EnumerateArray()
                .Select(f => f.GetString()!)
                .Where(k => fields.ContainsKey(k))
                .ToDictionary(k => k, k => fields[k]);
        }
        else
        {
            payload = new Dictionary<string, string>(fields.Where(kv => !kv.Key.StartsWith('_')));
        }

        var client  = httpFactory.CreateClient();
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        // Optional custom headers
        if (cfg.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
            foreach (var h in hdrs.EnumerateObject())
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Name, h.Value.GetString());

        var resp = await client.PostAsync(url, content);
        return resp.IsSuccessStatusCode;
    }

    // ── Email ─────────────────────────────────────────────────────────────────

    private async Task<bool> ExecuteEmailAsync(JsonElement cfg, Dictionary<string, string> fields, int accountId)
    {
        var toRaw  = cfg.TryGetProperty("to",      out var t) ? t.GetString() ?? "" : "";
        var subj   = cfg.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "Notificación Profet";
        var body   = cfg.TryGetProperty("body",    out var b) ? b.GetString() ?? "" : "";

        var to     = Interpolate(toRaw, fields);
        subj       = Interpolate(subj,  fields);
        body       = Interpolate(body,  fields);

        if (string.IsNullOrWhiteSpace(to)) return false;

        await emailService.SendAsync(to, subj, body);
        return true;
    }

    // ── Notification ─────────────────────────────────────────────────────────

    private async Task<bool> ExecuteNotificationAsync(JsonElement cfg, Dictionary<string, string> fields, int accountId)
    {
        var msgRaw = cfg.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "Nueva automatización ejecutada";
        var msg    = Interpolate(msgRaw, fields);

        db.Notifications.Add(new Notification
        {
            Message    = msg,
            Status     = false,
            Date       = DateTime.UtcNow,
            EntityType = "Automation",
        });
        await db.SaveChangesAsync();
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Retorna el primer campo no vacío que encuentre en el contexto.</summary>
    private static string? Get(Dictionary<string, string> f, params string[] keys)
    {
        foreach (var k in keys)
            if (f.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return null;
    }

    /// <summary>Reemplaza {{campo}} con el valor del contexto.</summary>
    private static string Interpolate(string template, Dictionary<string, string> fields) =>
        Regex.Replace(template, @"\{\{(\w+)\}\}", m =>
            fields.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Evalúa las condiciones JSON contra el contexto. Retorna true si pasan todas.</summary>
    private static bool EvaluateConditions(string? conditionsJson, Dictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(conditionsJson)) return true;
        try
        {
            var conditions = JsonSerializer.Deserialize<JsonElement>(conditionsJson);
            foreach (var c in conditions.EnumerateArray())
            {
                var field = c.TryGetProperty("field", out var fEl) ? fEl.GetString() ?? "" : "";
                var op    = c.TryGetProperty("op",    out var oEl) ? oEl.GetString() ?? "eq" : "eq";
                var value = c.TryGetProperty("value", out var vEl) ? vEl.GetString() ?? "" : "";

                var actual = fields.TryGetValue(field, out var a) ? a : "";
                var pass   = op switch
                {
                    "eq"       => string.Equals(actual, value, StringComparison.OrdinalIgnoreCase),
                    "neq"      => !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase),
                    "contains" => actual.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "gt"       => double.TryParse(actual, out var da) && double.TryParse(value, out var dv) && da > dv,
                    "lt"       => double.TryParse(actual, out var da2) && double.TryParse(value, out var dv2) && da2 < dv2,
                    _          => true,
                };
                if (!pass) return false;
            }
        }
        catch { return true; }
        return true;
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProfetAPI.Services;

/// <summary>
/// Aplica reglas de transformación (Formatter) sobre los valores del webhook
/// antes de que se usen para crear un Lead, Contacto, etc.
/// </summary>
public static class FormatterEngine
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Aplica las reglas definidas en <paramref name="formatterJson"/> sobre
    /// <paramref name="crmValues"/> (ya mapeados desde el payload raw) y devuelve
    /// un diccionario actualizado.
    /// </summary>
    public static Dictionary<string, string> Apply(
        string? formatterJson,
        Dictionary<string, string> rawFields,
        Dictionary<string, string> crmValues)
    {
        if (string.IsNullOrWhiteSpace(formatterJson)) return crmValues;

        List<FormatterRule>? rules;
        try { rules = JsonSerializer.Deserialize<List<FormatterRule>>(formatterJson, _json); }
        catch { return crmValues; }
        if (rules == null || rules.Count == 0) return crmValues;

        var result = new Dictionary<string, string>(crmValues, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            try
            {
                var output = rule.Type?.ToLower() switch
                {
                    "text"        => ApplyText(rule, GetSource(rule.SourceField, rawFields, result)),
                    "conditional" => ApplyConditional(rule, GetSource(rule.SourceField, rawFields, result)),
                    "lookup"      => ApplyLookup(rule, GetSource(rule.SourceField, rawFields, result)),
                    "combine"     => ApplyCombine(rule, rawFields, result),
                    "number"      => ApplyNumber(rule, GetSource(rule.SourceField, rawFields, result)),
                    "date"        => ApplyDate(rule, GetSource(rule.SourceField, rawFields, result)),
                    _ => null
                };

                if (output != null && !string.IsNullOrEmpty(rule.TargetField))
                    result[rule.TargetField] = output;
            }
            catch { }
        }

        return result;
    }

    // ── Source resolution ────────────────────────────────────────────────────

    private static string GetSource(string? field, Dictionary<string, string> raw, Dictionary<string, string> crm)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (raw.TryGetValue(field, out var rv))  return rv;
        if (crm.TryGetValue(field, out var cv))  return cv;
        return "";
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static string? ApplyText(FormatterRule rule, string value)
    {
        return rule.Operation?.ToLower() switch
        {
            "capitalize"       => CapitalizeWords(value),
            "capitalize_first" => CapitalizeFirst(value),
            "upper"            => value.ToUpper(),
            "lower"            => value.ToLower(),
            "trim"             => value.Trim(),
            "replace"          => ApplyReplace(rule, value),
            "extract"          => ApplyExtract(rule, value),
            _                  => value
        };
    }

    private static string CapitalizeWords(string s) =>
        string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));

    private static string CapitalizeFirst(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    private static string? ApplyReplace(FormatterRule rule, string value)
    {
        var find    = rule.Params?.GetValueOrDefault("find")    ?? "";
        var replace = rule.Params?.GetValueOrDefault("replace") ?? "";
        return string.IsNullOrEmpty(find) ? value : value.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ApplyExtract(FormatterRule rule, string value)
    {
        var pattern = rule.Params?.GetValueOrDefault("pattern");
        if (string.IsNullOrEmpty(pattern)) return value;
        try
        {
            var m = Regex.Match(value, pattern);
            return m.Success ? (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value) : "";
        }
        catch { return value; }
    }

    // ── Conditional ──────────────────────────────────────────────────────────

    private static string? ApplyConditional(FormatterRule rule, string value)
    {
        if (rule.Conditions == null) return rule.Default;

        foreach (var cond in rule.Conditions)
        {
            if (EvalCondition(cond.Operator, value, cond.Value))
                return cond.Output;
        }
        return rule.Default ?? "";
    }

    private static bool EvalCondition(string? op, string value, string condValue) =>
        op?.ToLower() switch
        {
            "eq"           => string.Equals(value, condValue, StringComparison.OrdinalIgnoreCase),
            "neq"          => !string.Equals(value, condValue, StringComparison.OrdinalIgnoreCase),
            "contains"     => value.Contains(condValue, StringComparison.OrdinalIgnoreCase),
            "not_contains" => !value.Contains(condValue, StringComparison.OrdinalIgnoreCase),
            "starts_with"  => value.StartsWith(condValue, StringComparison.OrdinalIgnoreCase),
            "ends_with"    => value.EndsWith(condValue, StringComparison.OrdinalIgnoreCase),
            "is_empty"     => string.IsNullOrWhiteSpace(value),
            "not_empty"    => !string.IsNullOrWhiteSpace(value),
            "gt"           => double.TryParse(value, out var a) && double.TryParse(condValue, out var b) && a > b,
            "lt"           => double.TryParse(value, out var c) && double.TryParse(condValue, out var d) && c < d,
            _              => false
        };

    // ── Lookup ───────────────────────────────────────────────────────────────

    private static string? ApplyLookup(FormatterRule rule, string value)
    {
        if (rule.Table == null) return value;
        foreach (var entry in rule.Table)
        {
            if (entry.From == "*") continue;
            if (string.Equals(entry.From, value, StringComparison.OrdinalIgnoreCase))
                return entry.To;
        }
        // wildcard fallback
        var wildcard = rule.Table.FirstOrDefault(e => e.From == "*");
        return wildcard?.To ?? value;
    }

    // ── Combine ──────────────────────────────────────────────────────────────

    private static string? ApplyCombine(FormatterRule rule, Dictionary<string, string> raw, Dictionary<string, string> crm)
    {
        if (rule.SourceFields == null || rule.SourceFields.Length == 0) return null;
        var sep = rule.Separator ?? " ";
        var parts = rule.SourceFields
            .Select(f => GetSource(f, raw, crm))
            .Where(v => !string.IsNullOrWhiteSpace(v));
        return string.Join(sep, parts);
    }

    // ── Number ───────────────────────────────────────────────────────────────

    private static string? ApplyNumber(FormatterRule rule, string value)
    {
        return rule.Operation?.ToLower() switch
        {
            "digits_only" => Regex.Replace(value, @"[^\d]", ""),
            "format"      => double.TryParse(Regex.Replace(value, @"[^\d\.\-]", ""), out var n) ? n.ToString("N2") : value,
            _             => value
        };
    }

    // ── Date ─────────────────────────────────────────────────────────────────

    private static string? ApplyDate(FormatterRule rule, string value)
    {
        var fmt = rule.OutputFormat ?? "dd/MM/yyyy";
        if (DateTime.TryParse(value, out var d))
            return d.ToString(fmt);
        return value;
    }
}

// ── DTOs del formatter ────────────────────────────────────────────────────────

public class FormatterRule
{
    public string?   Id           { get; set; }
    public string    Type         { get; set; } = "";
    public string?   SourceField  { get; set; }
    public string[]? SourceFields { get; set; }
    public string?   TargetField  { get; set; }
    public string?   Operation    { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    public List<ConditionRule>? Conditions    { get; set; }
    public string?   Default      { get; set; }
    public List<LookupEntry>? Table           { get; set; }
    public string?   Separator    { get; set; }
    public string?   OutputFormat { get; set; }
}

public class ConditionRule
{
    public string Operator { get; set; } = "eq";
    public string Value    { get; set; } = "";
    public string Output   { get; set; } = "";
}

public class LookupEntry
{
    public string From { get; set; } = "";
    public string To   { get; set; } = "";
}

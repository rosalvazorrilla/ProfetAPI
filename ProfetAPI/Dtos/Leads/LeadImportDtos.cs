namespace ProfetAPI.Dtos.Leads;

/// <summary>Campos del Lead a los que se puede mapear una columna del archivo.</summary>
public static class LeadImportFields
{
    public static readonly string[] All =
        { "name", "email", "phone", "company", "position", "city", "prospectSource", "initialMessage" };

    public static readonly Dictionary<string, string> Labels = new()
    {
        ["name"] = "Nombre", ["email"] = "Correo", ["phone"] = "Teléfono", ["company"] = "Empresa",
        ["position"] = "Puesto", ["city"] = "Ciudad", ["prospectSource"] = "Fuente", ["initialMessage"] = "Mensaje inicial",
    };
}

public class ParsedFileResult
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public bool Truncated { get; set; }
}

public class SuggestMappingRequestDto
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, string>> SampleRows { get; set; } = new();
}

public class SuggestMappingResultDto
{
    /// <summary>columna del archivo → campo del lead (o "" si no aplica).</summary>
    public Dictionary<string, string> Mapping { get; set; } = new();
}

public class CommitImportRequestDto
{
    public int? AccountId { get; set; }
    /// <summary>campo del lead → columna del archivo.</summary>
    public Dictionary<string, string> Mapping { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
    /// <summary>"skip" (omitir duplicados por email/teléfono) | "create" (crear siempre).</summary>
    public string DuplicateStrategy { get; set; } = "skip";
    /// <summary>Enriquecer y auto-calificar cada lead importado con IA (puede tardar más).</summary>
    public bool EnrichWithAi { get; set; } = true;
}

public class CommitImportResultDto
{
    public int Created    { get; set; }
    public int Duplicates { get; set; }
    public int Errors     { get; set; }
    public List<string> ErrorDetails { get; set; } = new();
}

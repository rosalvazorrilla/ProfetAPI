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

// ── Revisión de errores antes de importar (con sugerencias de IA) ─────────────

public class ValidateImportRequestDto
{
    public int? AccountId { get; set; }
    /// <summary>campo del lead → columna del archivo.</summary>
    public Dictionary<string, string> Mapping { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
    public string DuplicateStrategy { get; set; } = "skip";
}

public class ImportIssueDto
{
    /// <summary>Posición de la fila dentro de la lista enviada (base 0).</summary>
    public int RowIndex { get; set; }
    /// <summary>Número de fila como lo ve el usuario en Excel (encabezado = 1).</summary>
    public int RowNumber { get; set; }
    public string Field { get; set; } = "";
    /// <summary>missing_identity | invalid_email | invalid_phone.</summary>
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string Value { get; set; } = "";
}

public class ValidateImportResultDto
{
    public int TotalRows { get; set; }
    public int EmptyRows { get; set; }
    public int Duplicates { get; set; }
    public int ValidRows { get; set; }
    public int RowsWithIssues { get; set; }
    public List<ImportIssueDto> Issues { get; set; } = new();
}

public class SuggestFixesRequestDto
{
    public List<FixRequestItemDto> Issues { get; set; } = new();
}

public class FixRequestItemDto
{
    public int RowIndex { get; set; }
    public string Field { get; set; } = "";
    public string Code { get; set; } = "";
    public string Value { get; set; } = "";
    /// <summary>Resto de los datos de esa fila (campo del lead → valor), solo como contexto.</summary>
    public Dictionary<string, string> Context { get; set; } = new();
}

public class FixSuggestionDto
{
    public int RowIndex { get; set; }
    public string Field { get; set; } = "";
    public string? SuggestedValue { get; set; }
    public string Reason { get; set; } = "";
}

public class SuggestFixesResultDto
{
    public List<FixSuggestionDto> Suggestions { get; set; } = new();
}

public class CommitImportResultDto
{
    public int Created    { get; set; }
    public int Duplicates { get; set; }
    public int Errors     { get; set; }
    public List<string> ErrorDetails { get; set; } = new();
}

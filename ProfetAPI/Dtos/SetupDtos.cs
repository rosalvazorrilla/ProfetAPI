using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos
{
    // ════════════════════════════════════════════════════════════
    // STATUS / CHECKLIST
    // ════════════════════════════════════════════════════════════

    public class SetupStatusDto
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = null!;
        public string PlanName { get; set; } = null!;
        public int CurrentStep { get; set; }
        public bool CanComplete { get; set; }
        public List<SetupAccountChecklistDto> Accounts { get; set; } = new();
        public List<SetupUserSummaryDto> Users { get; set; } = new();
        public List<SetupTeamSummaryDto> Teams { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class SetupAccountChecklistDto
    {
        public int AccountId { get; set; }
        public string Name { get; set; } = null!;
        public SetupChecklistItem Funnel { get; set; } = new();
        public SetupChecklistItem Variables { get; set; } = new();
        public SetupChecklistItem Scoring { get; set; } = new();
        public SetupChecklistItem Catalogs { get; set; } = new();
        public SetupChecklistItem Users { get; set; } = new();
    }

    public class SetupChecklistItem
    {
        public bool Done { get; set; }
        public string? Detail { get; set; }
    }

    public class SetupUserSummaryDto
    {
        public string UserId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;
        public List<int> AccountsAssigned { get; set; } = new();
    }

    public class SetupTeamSummaryDto
    {
        public int TeamId { get; set; }
        public string Name { get; set; } = null!;
        public int MemberCount { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    // ACCOUNTS
    // ════════════════════════════════════════════════════════════

    public class CreateSetupAccountDto
    {
        [Required]
        /// <example>Ventas B2B</example>
        [SwaggerSchema("Nombre de la cuenta/campaña.", Nullable = false)]
        public string Name { get; set; } = null!;

        [SwaggerSchema("Descripción opcional de la cuenta.")]
        public string? Description { get; set; }

        /// <example>Carrusel</example>
        [SwaggerSchema("Tipo de asignación de leads: 'Carrusel' | 'Manual' | 'Usuario'.")]
        public string AssignmentType { get; set; } = "Carrusel";

        [SwaggerSchema("ID del usuario responsable (solo si AssignmentType = 'Usuario').")]
        public string? AssignmentUserId { get; set; }
    }

    public class UpdateSetupAccountDto
    {
        [Required]
        [SwaggerSchema("Nuevo nombre de la cuenta.")]
        public string Name { get; set; } = null!;

        [SwaggerSchema("Nueva descripción.")]
        public string? Description { get; set; }

        [SwaggerSchema("Tipo de asignación.")]
        public string AssignmentType { get; set; } = "Carrusel";

        [SwaggerSchema("ID del usuario responsable.")]
        public string? AssignmentUserId { get; set; }
    }

    public class SetupAccountResponseDto
    {
        public int AccountId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Status { get; set; } = null!;
        public string AssignmentType { get; set; } = null!;
    }

    // ════════════════════════════════════════════════════════════
    // INDUSTRIAS POR CUENTA
    // ════════════════════════════════════════════════════════════

    public class SetupAccountIndustriesDto
    {
        [Required]
        [SwaggerSchema("IDs de los sectores seleccionados para esta cuenta.", Nullable = false)]
        public List<long> IndustryIds { get; set; } = new();
    }

    // ════════════════════════════════════════════════════════════
    // EMBUDO
    // ════════════════════════════════════════════════════════════

    public class SetupFunnelDto
    {
        /// <example>2</example>
        [SwaggerSchema("ID de la plantilla a clonar. Si es null, se usa el array de stages personalizado.")]
        public int? TemplateId { get; set; }

        [SwaggerSchema("Etapas personalizadas (requerido si TemplateId es null).")]
        public List<SetupStageDto> Stages { get; set; } = new();
    }

    public class SetupStageDto
    {
        [Required]
        [SwaggerSchema("Nombre de la etapa.", Nullable = false)]
        public string Name { get; set; } = null!;

        [SwaggerSchema("Posición/orden de la etapa.")]
        public int Order { get; set; }

        [SwaggerSchema("Color hex de la etapa. Ej: #3498db")]
        public string? Color { get; set; }
    }

    public class SetupFunnelResponseDto
    {
        public int FunnelId { get; set; }
        public string Name { get; set; } = null!;
        public int? OriginatingTemplateId { get; set; }
        public List<SetupStageResponseDto> Stages { get; set; } = new();
    }

    public class SetupStageResponseDto
    {
        public int StageId { get; set; }
        public string Name { get; set; } = null!;
        public int Order { get; set; }
        public string? Color { get; set; }
    }

    public class UpdateSetupFunnelStagesDto
    {
        [Required]
        [SwaggerSchema("Lista completa de etapas. Incluir StageId para actualizar existentes, omitirlo para crear nuevas.")]
        public List<UpdateSetupStageDto> Stages { get; set; } = new();
    }

    public class UpdateSetupStageDto
    {
        [SwaggerSchema("ID de la etapa a actualizar. Null = crear nueva.")]
        public int? StageId { get; set; }

        [Required]
        [SwaggerSchema("Nombre de la etapa.")]
        public string Name { get; set; } = null!;

        [SwaggerSchema("Posición/orden.")]
        public int Order { get; set; }

        [SwaggerSchema("Color hex.")]
        public string? Color { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    // VARIABLES
    // ════════════════════════════════════════════════════════════

    public class SetupVariablesDto
    {
        [Required]
        [SwaggerSchema("Lista de variables a activar para esta cuenta.", Nullable = false)]
        public List<SetupVariableItemDto> Fields { get; set; } = new();
    }

    public class SetupVariableItemDto
    {
        [Required]
        [SwaggerSchema("ID del CustomFieldDefinition del catálogo global.", Nullable = false)]
        public int FieldId { get; set; }

        [SwaggerSchema("Si el campo se muestra en la tarjeta del lead.")]
        public bool IsVisibleOnCard { get; set; } = false;
    }

    // ════════════════════════════════════════════════════════════
    // SCORING
    // ════════════════════════════════════════════════════════════

    public class SetupScoringDto
    {
        /// <example>1</example>
        [SwaggerSchema("ID del ScoringTemplate a clonar. Si es null, se usa la configuración manual.")]
        public int? TemplateId { get; set; }

        /// <example>Mi modelo de calificación</example>
        [SwaggerSchema("Nombre del modelo de calificación.")]
        public string ModelName { get; set; } = "Modelo de Calificación";

        [SwaggerSchema("Preguntas del modelo (requerido si TemplateId es null).")]
        public List<SetupScoringQuestionDto> Questions { get; set; } = new();

        [SwaggerSchema("Umbrales de clasificación del lead. Si se omite se crean 3 tiers por defecto (Frío/Tibio/Caliente).")]
        public List<SetupTierDto> Tiers { get; set; } = new();
    }

    public class SetupScoringQuestionDto
    {
        [Required]
        [SwaggerSchema("Texto de la pregunta.", Nullable = false)]
        public string QuestionText { get; set; } = null!;

        [SwaggerSchema("SingleChoice | MultiChoice | OpenText | Numeric")]
        public string QuestionType { get; set; } = "SingleChoice";

        [SwaggerSchema("¿Es obligatoria?")]
        public bool IsRequired { get; set; } = false;

        [SwaggerSchema("Posición en el formulario.")]
        public int OrderPosition { get; set; } = 0;

        [SwaggerSchema("Opciones de respuesta con sus puntos.")]
        public List<SetupAnswerOptionDto> AnswerOptions { get; set; } = new();
    }

    public class SetupAnswerOptionDto
    {
        [Required]
        [SwaggerSchema("Texto de la respuesta.", Nullable = false)]
        public string AnswerText { get; set; } = null!;

        [SwaggerSchema("Puntos directos que suma esta respuesta.")]
        public decimal Points { get; set; } = 0;

        [SwaggerSchema("Posición visual.")]
        public int OrderPosition { get; set; } = 0;
    }

    public class SetupTierDto
    {
        [Required]
        [SwaggerSchema("Nombre del tier. Ej: Frío, Tibio, Caliente.", Nullable = false)]
        public string Name { get; set; } = null!;

        [SwaggerSchema("Score mínimo para caer en este tier.")]
        public decimal MinScore { get; set; } = 0;

        [SwaggerSchema("Score máximo. Null = sin límite superior.")]
        public decimal? MaxScore { get; set; }

        /// <example>#e74c3c</example>
        [SwaggerSchema("Color hex para el UI.")]
        public string? Color { get; set; }
    }

    public class SetupScoringResponseDto
    {
        public int ScoringModelId { get; set; }
        public string ModelName { get; set; } = null!;
        public int? OriginatingTemplateId { get; set; }
        public List<SetupScoringQuestionResponseDto> Questions { get; set; } = new();
        public List<SetupTierResponseDto> Tiers { get; set; } = new();
    }

    public class SetupScoringQuestionResponseDto
    {
        public int QuestionId { get; set; }
        public string QuestionText { get; set; } = null!;
        public string QuestionType { get; set; } = null!;
        public bool IsRequired { get; set; }
        public int OrderPosition { get; set; }
        public List<SetupAnswerOptionResponseDto> AnswerOptions { get; set; } = new();
    }

    public class SetupAnswerOptionResponseDto
    {
        public int AnswerOptionId { get; set; }
        public string AnswerText { get; set; } = null!;
        public decimal Points { get; set; }
        public int OrderPosition { get; set; }
    }

    public class SetupTierResponseDto
    {
        public int TierId { get; set; }
        public string Name { get; set; } = null!;
        public decimal MinScore { get; set; }
        public decimal? MaxScore { get; set; }
        public string? Color { get; set; }
    }

    public class UpdateSetupScoringQuestionsDto
    {
        [Required]
        [SwaggerSchema("Lista completa de preguntas con sus respuestas. Reemplaza todo el modelo.")]
        public List<SetupScoringQuestionDto> Questions { get; set; } = new();
    }

    // ════════════════════════════════════════════════════════════
    // CATÁLOGOS
    // ════════════════════════════════════════════════════════════

    public class SetupCatalogsDto
    {
        [SwaggerSchema("IDs de los motivos de pérdida del catálogo global a habilitar para esta cuenta.")]
        public List<int> LostReasonIds { get; set; } = new();

        [SwaggerSchema("IDs de las fuentes de prospectos globales a habilitar.")]
        public List<int> ProspectSourceIds { get; set; } = new();

        [SwaggerSchema("Etiquetas propias de esta cuenta.")]
        public List<SetupTagDto> Tags { get; set; } = new();
    }

    public class SetupTagDto
    {
        [Required]
        [SwaggerSchema("Nombre de la etiqueta.", Nullable = false)]
        public string Name { get; set; } = null!;

        /// <example>#3498db</example>
        [SwaggerSchema("Color de fondo hex.")]
        public string? Color { get; set; }

        [SwaggerSchema("Color del texto hex.")]
        public string? FontColor { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    // USUARIOS
    // ════════════════════════════════════════════════════════════

    public class CreateSetupUserDto
    {
        [Required]
        [EmailAddress]
        [SwaggerSchema("Correo del usuario (será su login).", Nullable = false)]
        public string Email { get; set; } = null!;

        [Required]
        [SwaggerSchema("Contraseña inicial.", Nullable = false)]
        public string Password { get; set; } = null!;

        [Required]
        [SwaggerSchema("Nombre(s).", Nullable = false)]
        public string FirstName { get; set; } = null!;

        [SwaggerSchema("Apellido(s).")]
        public string? LastName { get; set; }

        [SwaggerSchema("Teléfono.")]
        public string? Phone { get; set; }

        /// <example>SalesRep</example>
        [SwaggerSchema("Rol global del usuario: 'AccountAdmin' | 'SalesRep' | 'Viewer'.")]
        public string Role { get; set; } = "SalesRep";
    }

    public class UpdateSetupUserDto
    {
        [SwaggerSchema("Nuevo nombre.")]
        public string? FirstName { get; set; }

        [SwaggerSchema("Nuevo apellido.")]
        public string? LastName { get; set; }

        [SwaggerSchema("Nuevo teléfono.")]
        public string? Phone { get; set; }

        [SwaggerSchema("Nuevo rol global.")]
        public string? Role { get; set; }
    }

    public class SetupUserResponseDto
    {
        public string UserId { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Role { get; set; } = null!;
        public bool Active { get; set; }
        public List<SetupUserAccountAssignmentDto> Accounts { get; set; } = new();
    }

    public class SetupUserAccountAssignmentDto
    {
        public int AccountId { get; set; }
        public string AccountName { get; set; } = null!;
        public string RoleInAccount { get; set; } = null!;
    }

    // ════════════════════════════════════════════════════════════
    // ASIGNACIÓN USUARIO → CUENTA
    // ════════════════════════════════════════════════════════════

    public class AssignUserToAccountDto
    {
        [Required]
        [SwaggerSchema("ID del usuario a asignar (debe pertenecer al mismo customer).", Nullable = false)]
        public string UserId { get; set; } = null!;

        /// <example>SalesRep</example>
        [Required]
        [SwaggerSchema("Rol dentro de la cuenta: 'Admin' | 'SalesRep' | 'Viewer'.", Nullable = false)]
        public string RoleInAccount { get; set; } = null!;
    }

    // ════════════════════════════════════════════════════════════
    // EQUIPOS
    // ════════════════════════════════════════════════════════════

    public class CreateSetupTeamDto
    {
        [Required]
        [SwaggerSchema("Nombre del equipo.", Nullable = false)]
        public string Name { get; set; } = null!;

        [SwaggerSchema("IDs de usuarios que pertenecen al equipo.")]
        public List<string> UserIds { get; set; } = new();
    }

    public class UpdateSetupTeamDto
    {
        [Required]
        [SwaggerSchema("Nuevo nombre del equipo.")]
        public string Name { get; set; } = null!;

        [SwaggerSchema("IDs de usuarios. Reemplaza la lista actual.")]
        public List<string> UserIds { get; set; } = new();
    }

    public class SetupTeamResponseDto
    {
        public int TeamId { get; set; }
        public string Name { get; set; } = null!;
        public List<SetupTeamMemberDto> Members { get; set; } = new();
    }

    public class SetupTeamMemberDto
    {
        public string UserId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
    }

    // ════════════════════════════════════════════════════════════
    // PREVIEW + COMPLETE
    // ════════════════════════════════════════════════════════════

    public class SetupPreviewDto
    {
        public SetupPreviewCustomerDto Customer { get; set; } = null!;
        public List<SetupPreviewAccountDto> Accounts { get; set; } = new();
        public List<SetupUserResponseDto> Users { get; set; } = new();
        public List<SetupTeamResponseDto> Teams { get; set; } = new();
        public SetupValidationDto Validation { get; set; } = null!;
    }

    public class SetupPreviewCustomerDto
    {
        public string Name { get; set; } = null!;
        public string? Email { get; set; }
        public string PlanName { get; set; } = null!;
        public decimal PriceAgreed { get; set; }
        public string BillingCycle { get; set; } = null!;
    }

    public class SetupPreviewAccountDto
    {
        public int AccountId { get; set; }
        public string Name { get; set; } = null!;
        public List<string> Industries { get; set; } = new();
        public SetupFunnelResponseDto? Funnel { get; set; }
        public List<string> ActiveVariables { get; set; } = new();
        public SetupScoringResponseDto? Scoring { get; set; }
        public List<string> LostReasons { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<SetupUserAccountAssignmentDto> AssignedUsers { get; set; } = new();
    }

    public class SetupValidationDto
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class SetupCompleteResponseDto
    {
        public string Message { get; set; } = null!;
        public string AdminEmail { get; set; } = null!;
        public string LoginUrl { get; set; } = null!;
        public int AccountsActivated { get; set; }
        public int UsersActivated { get; set; }
    }
}

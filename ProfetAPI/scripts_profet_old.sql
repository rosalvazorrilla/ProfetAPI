-- ============================================================================
-- scripts_profet_old.sql — Lleva Profet_db (legacy, LeadsMVC — LA BASE
-- PRODUCTIVA REAL hoy) a la misma estructura Y estado de datos que ya tiene
-- Profet_new (el clon usado para construir el sistema nuevo).
--
-- Secciones A-D: estructura (tablas/columnas/índices), generadas por diff
-- exhaustivo entre scriptold.sql (Profet_db) y scriptd.sql (Profet_new).
-- Sección E: datos — reconstruida el 2026-07-17 leyendo el estado REAL de
-- Profet_new en vivo (no adivinada), porque el plan de migración de datos
-- que había en Querys.sql (Fase 1-3, con Leads_New/swap) nunca se ejecutó
-- así — el resultado real en Profet_new usó un camino más simple. Ver el
-- comentario de la Sección E para el detalle de qué se verificó y qué no.
-- Sección F: limpieza de tablas legacy sin lugar en la arquitectura nueva.
--
-- NO EJECUTADO. Correr esto SOLO sobre una COPIA/backup de Profet_db antes
-- de la migración real, nunca directo sobre producción sin probar antes.
-- El orden importa: A → B → C → D → E → F.
-- ============================================================================


-- ============================================================================
-- SECCIÓN A — 54 tablas que existen en Profet_new y NO existen en Profet_db.
-- CREATE TABLE literal, tal cual el esquema real de Profet_new.
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountCustomFields')
BEGIN

CREATE TABLE [dbo].[AccountCustomFields](
	[AccountId] [int] NOT NULL,
	[FieldId] [int] NOT NULL,
	[IsVisibleOnCard] [bit] NOT NULL,
 CONSTRAINT [PK_AccountCustomFields] PRIMARY KEY CLUSTERED 
(
	[AccountId] ASC,
	[FieldId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountIndustries')
BEGIN

CREATE TABLE [dbo].[AccountIndustries](
	[CampaignId] [bigint] NULL,
	[IndustryId] [bigint] NOT NULL,
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountInternalUsers')
BEGIN

CREATE TABLE [dbo].[AccountInternalUsers](
	[AccountId] [int] NOT NULL,
	[UserId] [nvarchar](128) NOT NULL,
	[RoleInAccount] [nvarchar](100) NOT NULL,
 CONSTRAINT [PK_AccountInternalUsers] PRIMARY KEY CLUSTERED 
(
	[AccountId] ASC,
	[UserId] ASC,
	[RoleInAccount] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountNotificationRecipients')
BEGIN

CREATE TABLE [dbo].[AccountNotificationRecipients](
	[RecipientId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[Email] [nvarchar](255) NOT NULL,
	[IsActive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[RecipientId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountProspectSources')
BEGIN

CREATE TABLE [dbo].[AccountProspectSources](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[SourceId] [int] NOT NULL,
 CONSTRAINT [PK_AccountProspectSources] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Accounts')
BEGIN

CREATE TABLE [dbo].[Accounts](
	[AccountId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](max) NOT NULL,
	[Description] [nvarchar](max) NULL,
	[CustomerId] [int] NOT NULL,
	[LandingUrl] [varchar](200) NULL,
	[AssignmentType] [varchar](100) NULL,
	[AssignmentUserId] [nvarchar](128) NULL,
	[LeadDealsTypesPackagesId] [int] NULL,
	[ActivitiesTemplateId] [int] NULL,
	[Status] [nvarchar](50) NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[SmtpHost] [nvarchar](200) NULL,
	[SmtpPort] [int] NULL,
	[SmtpUser] [nvarchar](200) NULL,
	[SmtpPassword] [nvarchar](500) NULL,
	[SmtpFromAddress] [nvarchar](320) NULL,
	[SmtpFromName] [nvarchar](200) NULL,
	[SmtpEnableSsl] [bit] NULL,
	[SmtpIsVerified] [bit] NULL,
	[SmtpVerifiedAt] [datetime2](7) NULL,
	[SmtpLastError] [nvarchar](max) NULL,
	[SmtpEnabled] [bit] NULL,
	[MetaAdAccountId] [nvarchar](50) NULL,
PRIMARY KEY CLUSTERED 
(
	[AccountId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountSettings')
BEGIN

CREATE TABLE [dbo].[AccountSettings](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CampId] [int] NULL,
	[Setparameter] [nvarchar](50) NULL,
	[Setvalue] [text] NULL,
	[AccountId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountStatusHistory')
BEGIN

CREATE TABLE [dbo].[AccountStatusHistory](
	[id] [bigint] IDENTITY(1,1) NOT NULL,
	[AccountId] [bigint] NULL,
	[initial_date] [datetime] NOT NULL,
	[end_date] [datetime] NULL,
	[active_days] [int] NULL,
	[Status] [nvarchar](50) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountUsers')
BEGIN

CREATE TABLE [dbo].[AccountUsers](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CampaignId] [bigint] NOT NULL,
	[UserId] [nvarchar](128) NOT NULL,
	[AccountId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountWebhooks')
BEGIN

CREATE TABLE [dbo].[AccountWebhooks](
	[WebhookId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[Direction] [nvarchar](20) NOT NULL,
	[Platform] [nvarchar](50) NULL,
	[ActionType] [nvarchar](50) NULL,
	[WebhookKey] [nvarchar](64) NULL,
	[MetaAppId] [nvarchar](200) NULL,
	[MetaAppSecret] [nvarchar](500) NULL,
	[MetaVerifyToken] [nvarchar](200) NULL,
	[MetaPageAccessToken] [nvarchar](max) NULL,
	[MetaPageId] [nvarchar](100) NULL,
	[DestFunnelId] [int] NULL,
	[DestLeadStatus] [nvarchar](50) NULL,
	[TriggerEvent] [nvarchar](100) NULL,
	[TargetUrl] [nvarchar](500) NULL,
	[OutgoingSecret] [nvarchar](300) NULL,
	[IsActive] [bit] NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[LastTriggeredAt] [datetime2](7) NULL,
	[TriggerCount] [int] NOT NULL,
	[LastError] [nvarchar](max) NULL,
	[MetaFormId] [nvarchar](50) NULL,
	[MetaFormName] [nvarchar](200) NULL,
	[MetaPageName] [nvarchar](200) NULL,
	[FieldMappingJson] [nvarchar](max) NULL,
	[FormatterJson] [nvarchar](max) NULL,
	[MetaAdAccountId] [nvarchar](50) NULL,
PRIMARY KEY CLUSTERED 
(
	[WebhookId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ActivityPlaybooks')
BEGIN

CREATE TABLE [dbo].[ActivityPlaybooks](
	[PlaybookId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[IsActive] [bit] NOT NULL,
	[IsDefault] [bit] NOT NULL,
	[Deleted] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PlaybookId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ActivityTypes')
BEGIN

CREATE TABLE [dbo].[ActivityTypes](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Title] [varchar](200) NOT NULL,
	[Icon] [varchar](200) NULL,
	[Active] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AddOns')
BEGIN

CREATE TABLE [dbo].[AddOns](
	[AddOnId] [int] IDENTITY(1,1) NOT NULL,
	[FeatureId] [int] NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[Price] [decimal](18, 2) NOT NULL,
	[BillingCycle] [nvarchar](50) NOT NULL,
	[Value] [decimal](18, 2) NOT NULL,
	[IsAdditive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[AddOnId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AutomationLogs')
BEGIN

CREATE TABLE [dbo].[AutomationLogs](
	[LogId] [bigint] IDENTITY(1,1) NOT NULL,
	[RuleId] [int] NOT NULL,
	[ExecutedAt] [datetime2](7) NOT NULL,
	[Success] [bit] NOT NULL,
	[StepsResultJson] [nvarchar](max) NULL,
	[ErrorMessage] [nvarchar](1000) NULL,
	[PayloadPreview] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[LogId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AutomationRules')
BEGIN

CREATE TABLE [dbo].[AutomationRules](
	[RuleId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[IsActive] [bit] NOT NULL,
	[Deleted] [bit] NOT NULL,
	[TriggerType] [nvarchar](50) NOT NULL,
	[TriggerPlatform] [nvarchar](50) NULL,
	[WebhookKey] [nvarchar](60) NULL,
	[ConditionsJson] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[MetaFormId] [nvarchar](80) NULL,
	[MetaPageName] [nvarchar](200) NULL,
	[MetaFormName] [nvarchar](200) NULL,
	[MetaPageId] [nvarchar](80) NULL,
PRIMARY KEY CLUSTERED 
(
	[RuleId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AutomationSteps')
BEGIN

CREATE TABLE [dbo].[AutomationSteps](
	[StepId] [int] IDENTITY(1,1) NOT NULL,
	[RuleId] [int] NOT NULL,
	[StepOrder] [int] NOT NULL,
	[StepType] [nvarchar](30) NOT NULL,
	[ConfigJson] [nvarchar](max) NULL,
	[IsActive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[StepId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CallDetails')
BEGIN

CREATE TABLE [dbo].[CallDetails](
	[CallDetailId] [int] IDENTITY(1,1) NOT NULL,
	[ActivityId] [int] NOT NULL,
	[RecordingUrl] [nvarchar](500) NULL,
	[Duration] [nvarchar](50) NULL,
	[CallSid] [nvarchar](200) NULL,
PRIMARY KEY CLUSTERED 
(
	[CallDetailId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_CallDetails_ActivityId] UNIQUE NONCLUSTERED 
(
	[ActivityId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Companies')
BEGIN

CREATE TABLE [dbo].[Companies](
	[CompanyId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Address] [nvarchar](500) NULL,
	[City] [nvarchar](100) NULL,
	[State] [nvarchar](100) NULL,
	[PhoneNumber] [varchar](50) NULL,
	[CreatedOn] [datetime2](3) NOT NULL,
	[PostalCode] [nvarchar](20) NULL,
	[Website] [nvarchar](255) NULL,
	[ModifiedOn] [datetime2](3) NULL,
	[LifecycleStatus] [nvarchar](50) NULL,
PRIMARY KEY CLUSTERED 
(
	[CompanyId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Contacts')
BEGIN

CREATE TABLE [dbo].[Contacts](
	[ContactId] [int] IDENTITY(1,1) NOT NULL,
	[CompanyId] [int] NULL,
	[FirstName] [nvarchar](100) NULL,
	[LastName] [nvarchar](100) NULL,
	[FullName]  AS (isnull([FirstName]+' ','')+isnull([LastName],'')) PERSISTED NOT NULL,
	[Email] [nvarchar](255) NULL,
	[PhoneNumber] [varchar](50) NULL,
	[Position] [nvarchar](150) NULL,
	[CreatedOn] [datetime2](3) NOT NULL,
	[IsWhatsappContact] [bit] NOT NULL,
	[LifecycleStatus] [nvarchar](50) NULL,
	[ModifiedOn] [datetime2](3) NULL,
	[PostalCode] [nvarchar](20) NULL,
	[IsArchived] [bit] NOT NULL,
	[LinkedContactId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[ContactId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CustomerPurchasedAddOns')
BEGIN

CREATE TABLE [dbo].[CustomerPurchasedAddOns](
	[PurchasedAddOnId] [int] IDENTITY(1,1) NOT NULL,
	[SubscriptionId] [int] NOT NULL,
	[AddOnId] [int] NOT NULL,
	[PricePaid] [decimal](18, 2) NOT NULL,
	[PurchaseDate] [datetime2](7) NOT NULL,
	[ExpiryDate] [datetime2](7) NULL,
	[Quantity] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PurchasedAddOnId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CustomFieldDefinitions')
BEGIN

CREATE TABLE [dbo].[CustomFieldDefinitions](
	[FieldId] [int] IDENTITY(1,1) NOT NULL,
	[FieldCode] [nvarchar](100) NOT NULL,
	[FieldName] [nvarchar](200) NOT NULL,
	[FieldType] [nvarchar](50) NOT NULL,
	[Options] [nvarchar](max) NULL,
	[IsSystem] [bit] NOT NULL,
 CONSTRAINT [PK_CustomFieldDefinitions] PRIMARY KEY CLUSTERED 
(
	[FieldId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_CustomFieldDefinitions_FieldCode] UNIQUE NONCLUSTERED 
(
	[FieldCode] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CustomFieldValues')
BEGIN

CREATE TABLE [dbo].[CustomFieldValues](
	[ValueId] [int] IDENTITY(1,1) NOT NULL,
	[EntityId] [bigint] NOT NULL,
	[EntityType] [nvarchar](50) NOT NULL,
	[FieldId] [int] NOT NULL,
	[Value] [nvarchar](max) NULL,
 CONSTRAINT [PK_CustomFieldValues] PRIMARY KEY CLUSTERED 
(
	[ValueId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DashboardLayouts')
BEGIN

CREATE TABLE [dbo].[DashboardLayouts](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[UserId] [nvarchar](450) NULL,
	[LayoutJson] [nvarchar](max) NOT NULL,
	[ModifiedOn] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Deals')
BEGIN

CREATE TABLE [dbo].[Deals](
	[DealId] [int] IDENTITY(1,1) NOT NULL,
	[DealName] [nvarchar](300) NOT NULL,
	[StageId] [int] NULL,
	[QuotedAmount] [decimal](20, 2) NULL,
	[CloseDate] [date] NULL,
	[OwnerUserId] [nvarchar](128) NULL,
	[PrimaryContactId] [int] NULL,
	[CompanyId] [int] NULL,
	[OriginatingLeadId] [bigint] NULL,
	[CreatedOn] [datetime2](3) NOT NULL,
	[ProspectSourceId] [int] NULL,
	[ModifiedOn] [datetime2](3) NULL,
	[PublicId] [nvarchar](100) NULL,
	[ExternalId] [nvarchar](100) NULL,
	[FinalAmount] [decimal](18, 2) NULL,
	[AccountId] [int] NULL,
	[LeadLostReasonId] [int] NULL,
	[LeadTierId] [int] NULL,
	[DealType] [nvarchar](50) NOT NULL,
	[AdName] [nvarchar](255) NULL,
	[OriginType] [nvarchar](50) NULL,
	[ProspectSource] [nvarchar](255) NULL,
	[Status] [nvarchar](50) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[DealId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED 
(
	[OriginatingLeadId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DealUsers')
BEGIN

CREATE TABLE [dbo].[DealUsers](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[DealId] [int] NOT NULL,
	[UserId] [nvarchar](128) NOT NULL,
	[RoleInDeal] [nvarchar](50) NULL,
 CONSTRAINT [PK_DealUsers] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmailLogs')
BEGIN

CREATE TABLE [dbo].[EmailLogs](
	[EmailLogId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NULL,
	[LeadId] [int] NULL,
	[DealId] [int] NULL,
	[ContactId] [int] NULL,
	[SentByUserId] [nvarchar](128) NULL,
	[ToAddress] [nvarchar](320) NOT NULL,
	[CcAddress] [nvarchar](320) NULL,
	[Subject] [nvarchar](500) NOT NULL,
	[BodyHtml] [nvarchar](max) NOT NULL,
	[SentAt] [datetime2](7) NOT NULL,
	[IsSuccess] [bit] NOT NULL,
	[ErrorMessage] [nvarchar](max) NULL,
PRIMARY KEY CLUSTERED 
(
	[EmailLogId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Features')
BEGIN

CREATE TABLE [dbo].[Features](
	[FeatureId] [int] IDENTITY(1,1) NOT NULL,
	[FeatureCode] [nvarchar](100) NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[Type] [nvarchar](20) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[FeatureId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED 
(
	[FeatureCode] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FunnelTemplates')
BEGIN

CREATE TABLE [dbo].[FunnelTemplates](
	[TemplateId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Description] [nvarchar](1000) NULL,
PRIMARY KEY CLUSTERED 
(
	[TemplateId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FunnelTemplateStages')
BEGIN

CREATE TABLE [dbo].[FunnelTemplateStages](
	[TemplateStageId] [int] IDENTITY(1,1) NOT NULL,
	[TemplateId] [int] NOT NULL,
	[StageName] [nvarchar](255) NOT NULL,
	[StageOrder] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[TemplateStageId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'GlobalBranding')
BEGIN

CREATE TABLE [dbo].[GlobalBranding](
	[Id] [int] NOT NULL,
	[AppName] [nvarchar](100) NULL,
	[LogoLargeUrl] [nvarchar](500) NULL,
	[LogoSmallUrl] [nvarchar](500) NULL,
	[PrimaryColor] [varchar](20) NULL,
	[SecondaryColor] [varchar](20) NULL,
	[FaviconUrl] [nvarchar](500) NULL,
 CONSTRAINT [PK_GlobalBranding] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadLostReasonTemplates')
BEGIN

CREATE TABLE [dbo].[LeadLostReasonTemplates](
	[TemplateId] [int] IDENTITY(1,1) NOT NULL,
	[Description] [nvarchar](200) NOT NULL,
	[CountsForCharts] [bit] NOT NULL,
	[IsActive] [bit] NOT NULL,
 CONSTRAINT [PK_LeadLostReasonTemplates] PRIMARY KEY CLUSTERED 
(
	[TemplateId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadScoringAnswers')
BEGIN

CREATE TABLE [dbo].[LeadScoringAnswers](
	[ScoringAnswerId] [int] IDENTITY(1,1) NOT NULL,
	[LeadId] [bigint] NOT NULL,
	[QuestionId] [int] NOT NULL,
	[AnswerOptionId] [int] NOT NULL,
	[TextValue] [nvarchar](max) NULL,
	[NumericValue] [decimal](10, 2) NULL,
	[PointsAwarded] [decimal](10, 2) NOT NULL,
	[Source] [nvarchar](20) NULL,
	[Confidence] [decimal](4, 3) NULL,
PRIMARY KEY CLUSTERED 
(
	[ScoringAnswerId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadTiers')
BEGIN

CREATE TABLE [dbo].[LeadTiers](
	[TierId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[ScoringModelId] [int] NULL,
	[MinScore] [decimal](10, 2) NOT NULL,
	[MaxScore] [decimal](10, 2) NULL,
	[Color] [nvarchar](50) NULL,
PRIMARY KEY CLUSTERED 
(
	[TierId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlanFeatures')
BEGIN

CREATE TABLE [dbo].[PlanFeatures](
	[PlanId] [int] NOT NULL,
	[FeatureId] [int] NOT NULL,
	[Limit] [nvarchar](100) NULL,
 CONSTRAINT [PK_PlanFeatures] PRIMARY KEY CLUSTERED 
(
	[PlanId] ASC,
	[FeatureId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlanPriceHistory')
BEGIN

CREATE TABLE [dbo].[PlanPriceHistory](
	[PriceHistoryId] [int] IDENTITY(1,1) NOT NULL,
	[PlanId] [int] NOT NULL,
	[MonthlyPrice] [decimal](18, 2) NOT NULL,
	[AnnualPrice] [decimal](18, 2) NOT NULL,
	[EffectiveDate] [datetime2](7) NOT NULL,
	[EndDate] [datetime2](7) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PriceHistoryId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Plans')
BEGIN

CREATE TABLE [dbo].[Plans](
	[PlanId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[IsPublic] [bit] NOT NULL,
	[IsActive] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[PlanId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlaybookTasks')
BEGIN

CREATE TABLE [dbo].[PlaybookTasks](
	[TaskId] [int] IDENTITY(1,1) NOT NULL,
	[PlaybookId] [int] NOT NULL,
	[TaskName] [nvarchar](1000) NOT NULL,
	[Order] [int] NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[Priority] [nvarchar](20) NOT NULL,
	[OffsetDays] [int] NOT NULL,
	[ActionType] [nvarchar](30) NOT NULL,
	[TargetStageId] [int] NULL,
PRIMARY KEY CLUSTERED 
(
	[TaskId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProspectSources')
BEGIN

CREATE TABLE [dbo].[ProspectSources](
	[SourceId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[SourceId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SavedReports')
BEGIN

CREATE TABLE [dbo].[SavedReports](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[UserId] [nvarchar](450) NULL,
	[Name] [nvarchar](200) NOT NULL,
	[LayoutJson] [nvarchar](max) NOT NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[Deleted] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringAnswerOptions')
BEGIN

CREATE TABLE [dbo].[ScoringAnswerOptions](
	[AnswerOptionId] [int] IDENTITY(1,1) NOT NULL,
	[QuestionId] [int] NOT NULL,
	[AnswerText] [nvarchar](1000) NOT NULL,
	[Points] [decimal](10, 2) NOT NULL,
	[OrderPosition] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[AnswerOptionId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringModels')
BEGIN

CREATE TABLE [dbo].[ScoringModels](
	[ScoringModelId] [int] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[ScoringModelId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringQuestions')
BEGIN

CREATE TABLE [dbo].[ScoringQuestions](
	[QuestionId] [int] IDENTITY(1,1) NOT NULL,
	[ScoringModelId] [int] NOT NULL,
	[QuestionText] [nvarchar](1000) NOT NULL,
	[QuestionType] [nvarchar](20) NOT NULL,
	[IsRequired] [bit] NOT NULL,
	[OrderPosition] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[QuestionId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringRuleConditions')
BEGIN

CREATE TABLE [dbo].[ScoringRuleConditions](
	[ConditionId] [int] IDENTITY(1,1) NOT NULL,
	[RuleId] [int] NOT NULL,
	[QuestionId] [int] NULL,
	[AnswerOptionId] [int] NULL,
	[LogicOperator] [nvarchar](5) NOT NULL,
	[ConditionType] [nvarchar](50) NOT NULL,
	[FieldId] [int] NULL,
	[ConditionValue] [nvarchar](500) NULL,
	[SignalField] [nvarchar](50) NULL,
 CONSTRAINT [PK_ScoringRuleConditions] PRIMARY KEY CLUSTERED 
(
	[ConditionId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringRules')
BEGIN

CREATE TABLE [dbo].[ScoringRules](
	[RuleId] [int] IDENTITY(1,1) NOT NULL,
	[ScoringModelId] [int] NOT NULL,
	[ConditionQuestionId] [int] NULL,
	[ConditionAnswerOptionId] [int] NULL,
	[ActionType] [nvarchar](100) NULL,
	[ActionValue] [nvarchar](255) NULL,
	[ExecutionOrder] [int] NOT NULL,
	[Name] [nvarchar](200) NULL,
	[BonusPoints] [decimal](10, 2) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[RuleId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringTemplateAnswerOptions')
BEGIN

CREATE TABLE [dbo].[ScoringTemplateAnswerOptions](
	[TemplateAnswerId] [int] IDENTITY(1,1) NOT NULL,
	[TemplateQuestionId] [int] NOT NULL,
	[AnswerText] [nvarchar](1000) NOT NULL,
	[Points] [decimal](10, 2) NOT NULL,
	[OrderPosition] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[TemplateAnswerId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringTemplateQuestions')
BEGIN

CREATE TABLE [dbo].[ScoringTemplateQuestions](
	[TemplateQuestionId] [int] IDENTITY(1,1) NOT NULL,
	[TemplateId] [int] NOT NULL,
	[QuestionText] [nvarchar](1000) NOT NULL,
	[QuestionType] [nvarchar](20) NOT NULL,
	[IsRequired] [bit] NOT NULL,
	[OrderPosition] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[TemplateQuestionId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringTemplates')
BEGIN

CREATE TABLE [dbo].[ScoringTemplates](
	[TemplateId] [int] IDENTITY(1,1) NOT NULL,
	[CategoryId] [int] NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Description] [nvarchar](1000) NULL,
	[IndustryId] [bigint] NULL,
PRIMARY KEY CLUSTERED 
(
	[TemplateId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SubscriptionFeatureOverrides')
BEGIN

CREATE TABLE [dbo].[SubscriptionFeatureOverrides](
	[SubscriptionId] [int] NOT NULL,
	[FeatureId] [int] NOT NULL,
	[CustomLimit] [nvarchar](50) NOT NULL,
 CONSTRAINT [PK_SubFeatureOverrides] PRIMARY KEY CLUSTERED 
(
	[SubscriptionId] ASC,
	[FeatureId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SubscriptionPeriods')
BEGIN

CREATE TABLE [dbo].[SubscriptionPeriods](
	[PeriodId] [int] IDENTITY(1,1) NOT NULL,
	[SubscriptionId] [int] NOT NULL,
	[PeriodStartDate] [datetime2](7) NOT NULL,
	[PeriodEndDate] [datetime2](7) NOT NULL,
	[AmountBilled] [decimal](18, 2) NOT NULL,
	[Status] [nvarchar](50) NOT NULL,
	[PaymentDate] [datetime2](7) NULL,
	[InvoiceUrl] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[PeriodId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Subscriptions')
BEGIN

CREATE TABLE [dbo].[Subscriptions](
	[SubscriptionId] [int] IDENTITY(1,1) NOT NULL,
	[CustomerId] [int] NOT NULL,
	[PlanId] [int] NOT NULL,
	[Status] [nvarchar](50) NOT NULL,
	[PriceAgreed] [decimal](18, 2) NOT NULL,
	[BillingCycle] [nvarchar](50) NOT NULL,
	[DiscountAmount] [decimal](18, 2) NOT NULL,
	[TrialEndDate] [datetime2](7) NULL,
	[SubscriptionStartDate] [datetime2](7) NOT NULL,
	[CanceledDate] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[SubscriptionId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TemplateCategories')
BEGIN

CREATE TABLE [dbo].[TemplateCategories](
	[CategoryId] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[CategoryId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TimelineEvents')
BEGIN

CREATE TABLE [dbo].[TimelineEvents](
	[TimelineEventId] [bigint] IDENTITY(1,1) NOT NULL,
	[AccountId] [int] NOT NULL,
	[EntityType] [nvarchar](20) NOT NULL,
	[EntityId] [bigint] NOT NULL,
	[Type] [nvarchar](30) NOT NULL,
	[Title] [nvarchar](200) NOT NULL,
	[Detail] [nvarchar](max) NULL,
	[MetaJson] [nvarchar](max) NULL,
	[CreatedByUserId] [nvarchar](450) NULL,
	[CreatedOn] [datetime2](7) NOT NULL,
	[Deleted] [bit] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[TimelineEventId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'UserProfiles')
BEGIN

CREATE TABLE [dbo].[UserProfiles](
	[UserId] [nvarchar](128) NOT NULL,
	[FirstName] [nvarchar](max) NULL,
	[LastName] [nvarchar](max) NULL,
	[Phone] [nvarchar](max) NULL,
	[PhoneExt] [nvarchar](max) NULL,
	[Mobile] [nvarchar](max) NULL,
	[IndustrySector] [nvarchar](max) NULL,
	[CallPickerExtensionName] [varchar](200) NULL,
	[CallPickerExtension] [varchar](200) NULL,
	[CallPickerKey] [varchar](200) NULL,
	[ProfilePicture] [bit] NULL,
	[Pass64] [varchar](200) NULL,
	[IsAdmin] [bit] NULL,
	[LastLoginDate] [datetime2](7) NULL,
	[Preferences] [nvarchar](max) NULL,
 CONSTRAINT [PK_UserProfiles] PRIMARY KEY CLUSTERED 
(
	[UserId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WebhookEventLogs')
BEGIN

CREATE TABLE [dbo].[WebhookEventLogs](
	[EventLogId] [bigint] IDENTITY(1,1) NOT NULL,
	[WebhookId] [int] NOT NULL,
	[ReceivedAt] [datetime2](7) NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[Summary] [nvarchar](300) NULL,
	[ExternalId] [nvarchar](100) NULL,
	[ErrorMessage] [nvarchar](500) NULL,
PRIMARY KEY CLUSTERED 
(
	[EventLogId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
END
GO


-- ============================================================================
-- SECCIÓN B — Renombres de columna (NO son columnas nuevas, son PKs que
-- cambiaron de nombre entre Profet_db y Profet_new). Verificado leyendo
-- ambos esquemas completos, no son adiciones.
-- ============================================================================

-- Leads: la PK se llamaba [Id] en Profet_db, en Profet_new es [LeadId].
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Leads') AND name = 'Id')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Leads') AND name = 'LeadId')
    EXEC sp_rename 'dbo.Leads.Id', 'LeadId', 'COLUMN';
GO

-- LeadLostReasons: OJO — esta tabla no es un simple rename, tuvo un rediseño
-- más profundo. En Profet_db tiene [Id] + [LeadLostReasonsPackagesId] (FK a
-- un paquete compartido); en Profet_new la PK es [LostReasonId] y el motivo
-- cuelga directo de [AccountId] (ya no de un "paquete"). NO genero aquí un
-- sp_rename ni un ALTER automático porque mapear los datos existentes de
-- LeadLostReasonsPackagesId -> AccountId requiere decidir la regla de
-- negocio (posible tabla puente LeadLostReasonsPackages, ver Sección D).
-- REVISAR MANUALMENTE antes de migrar esta tabla.


-- ============================================================================
-- SECCIÓN C — Columnas nuevas en tablas que SÍ existen en ambas bases
-- (61 columnas agregadas incrementalmente a Profet_new y nunca replicadas
-- en Profet_db). Verificado columna por columna contra ambos esquemas reales.
-- ============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'AccountId')
    ALTER TABLE dbo.Activities ADD [AccountId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'Priority')
    ALTER TABLE dbo.Activities ADD [Priority] [nvarchar](20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'TaskStatus')
    ALTER TABLE dbo.Activities ADD [TaskStatus] [nvarchar](30) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'AssignedToUserId')
    ALTER TABLE dbo.Activities ADD [AssignedToUserId] [nvarchar](128) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'DueDate')
    ALTER TABLE dbo.Activities ADD [DueDate] [datetime2](7) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'CreatedOn')
    ALTER TABLE dbo.Activities ADD [CreatedOn] [datetime2](7) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'ActivityType')
    ALTER TABLE dbo.Activities ADD [ActivityType] [nvarchar](50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'Subject')
    ALTER TABLE dbo.Activities ADD [Subject] [nvarchar](300) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'OwnerUserId')
    ALTER TABLE dbo.Activities ADD [OwnerUserId] [nvarchar](128) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'EntityId')
    ALTER TABLE dbo.Activities ADD [EntityId] [bigint] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'EntityType')
    ALTER TABLE dbo.Activities ADD [EntityType] [nvarchar](50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'IsCompleted')
    ALTER TABLE dbo.Activities ADD [IsCompleted] [bit] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Funnels') AND name = N'AccountId')
    ALTER TABLE dbo.Funnels ADD [AccountId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Funnels') AND name = N'OriginatingTemplateId')
    ALTER TABLE dbo.Funnels ADD [OriginatingTemplateId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Roles') AND name = N'NormalizedName')
    ALTER TABLE dbo.Roles ADD [NormalizedName] [nvarchar](256) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Roles') AND name = N'ConcurrencyStamp')
    ALTER TABLE dbo.Roles ADD [ConcurrencyStamp] [nvarchar](max) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LeadLostReasons') AND name = N'AccountId')
    ALTER TABLE dbo.LeadLostReasons ADD [AccountId] [int] NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LeadLostReasons') AND name = N'CountsForCharts')
    ALTER TABLE dbo.LeadLostReasons ADD [CountsForCharts] [bit] NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LeadLostReasons') AND name = N'IsActive')
    ALTER TABLE dbo.LeadLostReasons ADD [IsActive] [bit] NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Reports') AND name = N'AccountId')
    ALTER TABLE dbo.Reports ADD [AccountId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'NormalizedUserName')
    ALTER TABLE dbo.Users ADD [NormalizedUserName] [nvarchar](256) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'NormalizedEmail')
    ALTER TABLE dbo.Users ADD [NormalizedEmail] [nvarchar](256) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'ConcurrencyStamp')
    ALTER TABLE dbo.Users ADD [ConcurrencyStamp] [nvarchar](max) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'LockoutEnd')
    ALTER TABLE dbo.Users ADD [LockoutEnd] [datetimeoffset](7) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'CreatedOn')
    ALTER TABLE dbo.Users ADD [CreatedOn] [datetime2](7) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'UserType')
    ALTER TABLE dbo.Users ADD [UserType] [nvarchar](50) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'Email')
    ALTER TABLE dbo.Customers ADD [Email] [varchar](255) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'SetupToken')
    ALTER TABLE dbo.Customers ADD [SetupToken] [varchar](128) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'SetupStep')
    ALTER TABLE dbo.Customers ADD [SetupStep] [int] NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'Status')
    ALTER TABLE dbo.Customers ADD [Status] [varchar](50) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'BrandName')
    ALTER TABLE dbo.Customers ADD [BrandName] [nvarchar](100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'BrandLogoUrl')
    ALTER TABLE dbo.Customers ADD [BrandLogoUrl] [nvarchar](500) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'BrandPrimaryColor')
    ALTER TABLE dbo.Customers ADD [BrandPrimaryColor] [varchar](20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'BrandSecondaryColor')
    ALTER TABLE dbo.Customers ADD [BrandSecondaryColor] [varchar](20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'BrandFaviconUrl')
    ALTER TABLE dbo.Customers ADD [BrandFaviconUrl] [nvarchar](500) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'BrandLogoSmallUrl')
    ALTER TABLE dbo.Customers ADD [BrandLogoSmallUrl] [nvarchar](500) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'WhatsappEnabled')
    ALTER TABLE dbo.Customers ADD [WhatsappEnabled] [bit] NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'WebhookReceiveId')
    ALTER TABLE dbo.Customers ADD [WebhookReceiveId] [nvarchar](100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'WebhookSentId')
    ALTER TABLE dbo.Customers ADD [WebhookSentId] [nvarchar](100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'MetaManagedByUs')
    ALTER TABLE dbo.Customers ADD [MetaManagedByUs] [bit] NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Webhooks') AND name = N'AccountId')
    ALTER TABLE dbo.Webhooks ADD [AccountId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Teams') AND name = N'AccountId')
    ALTER TABLE dbo.Teams ADD [AccountId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Teams') AND name = N'LeaderId')
    ALTER TABLE dbo.Teams ADD [LeaderId] [nvarchar](450) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ContactsWhatsapps') AND name = N'AccountId')
    ALTER TABLE dbo.ContactsWhatsapps ADD [AccountId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ContactsWhatsapps') AND name = N'LinkedContactId')
    ALTER TABLE dbo.ContactsWhatsapps ADD [LinkedContactId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'InitialMessage')
    ALTER TABLE dbo.Leads ADD [InitialMessage] [nvarchar](max) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'CreatedOn')
    ALTER TABLE dbo.Leads ADD [CreatedOn] [datetime] NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'OwnerUserId')
    ALTER TABLE dbo.Leads ADD [OwnerUserId] [nvarchar](128) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'ProspectSourceId')
    ALTER TABLE dbo.Leads ADD [ProspectSourceId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'AccountId')
    ALTER TABLE dbo.Leads ADD [AccountId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'ContactId')
    ALTER TABLE dbo.Leads ADD [ContactId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'ContactFormId')
    ALTER TABLE dbo.Leads ADD [ContactFormId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'OriginType')
    ALTER TABLE dbo.Leads ADD [OriginType] [nvarchar](50) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'LifecycleStatus')
    ALTER TABLE dbo.Leads ADD [LifecycleStatus] [nvarchar](50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'Score')
    ALTER TABLE dbo.Leads ADD [Score] [decimal](6, 2) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'TierId')
    ALTER TABLE dbo.Leads ADD [TierId] [int] NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'ScoreReasoning')
    ALTER TABLE dbo.Leads ADD [ScoreReasoning] [nvarchar](max) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'ScoredAt')
    ALTER TABLE dbo.Leads ADD [ScoredAt] [datetime2](7) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'ScoreSource')
    ALTER TABLE dbo.Leads ADD [ScoreSource] [nvarchar](20) NULL;
GO


-- ============================================================================
-- SECCIÓN D — Índices que existen en Profet_new y deben replicarse en
-- Profet_db (37 índices, DDL literal tomado del esquema real de Profet_new,
-- + 2 agregados el 2026-07-17 para no quedar desalineado con Profet_new).
-- ============================================================================

-- Agregados 2026-07-17 (ver scripts_profet_new.sql Sección F): sin esto,
-- ScoringAnswerOptions (3.6M filas) hace table scan completo en cada
-- apertura de un prospecto con scoring.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScoringAnswerOptions_QuestionId' AND object_id = OBJECT_ID('dbo.ScoringAnswerOptions'))
    CREATE INDEX IX_ScoringAnswerOptions_QuestionId ON dbo.ScoringAnswerOptions(QuestionId) INCLUDE (AnswerText, Points);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScoringQuestions_ScoringModelId' AND object_id = OBJECT_ID('dbo.ScoringQuestions'))
    CREATE INDEX IX_ScoringQuestions_ScoringModelId ON dbo.ScoringQuestions(ScoringModelId, OrderPosition);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IndustryId' AND object_id = OBJECT_ID('dbo.AccountIndustries'))

/****** Object:  Index [IX_IndustryId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_IndustryId] ON [dbo].[AccountIndustries]
(
	[IndustryId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountWebhooks_AccountId' AND object_id = OBJECT_ID('dbo.AccountWebhooks'))

/****** Object:  Index [IX_AccountWebhooks_AccountId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_AccountWebhooks_AccountId] ON [dbo].[AccountWebhooks]
(
	[AccountId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AccountWebhooks_WebhookKey' AND object_id = OBJECT_ID('dbo.AccountWebhooks'))

/****** Object:  Index [UX_AccountWebhooks_WebhookKey]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [UX_AccountWebhooks_WebhookKey] ON [dbo].[AccountWebhooks]
(
	[WebhookKey] ASC
)
WHERE ([WebhookKey] IS NOT NULL)
WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Activities_Entity' AND object_id = OBJECT_ID('dbo.Activities'))

/****** Object:  Index [IX_Activities_Entity]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Activities_Entity] ON [dbo].[Activities]
(
	[EntityType] ASC,
	[EntityId] ASC,
	[ActivityType] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Activities_LeadId' AND object_id = OBJECT_ID('dbo.Activities'))

/****** Object:  Index [IX_Activities_LeadId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Activities_LeadId] ON [dbo].[Activities]
(
	[LeadId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Activities_UserId' AND object_id = OBJECT_ID('dbo.Activities'))

/****** Object:  Index [IX_Activities_UserId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Activities_UserId] ON [dbo].[Activities]
(
	[UserId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ActivityPlaybooks_AccountDefault' AND object_id = OBJECT_ID('dbo.ActivityPlaybooks'))

/****** Object:  Index [IX_ActivityPlaybooks_AccountDefault]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_ActivityPlaybooks_AccountDefault] ON [dbo].[ActivityPlaybooks]
(
	[AccountId] ASC,
	[IsDefault] ASC,
	[IsActive] ASC,
	[Deleted] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ActivityTypes_Title' AND object_id = OBJECT_ID('dbo.ActivityTypes'))

/****** Object:  Index [IX_ActivityTypes_Title]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [IX_ActivityTypes_Title] ON [dbo].[ActivityTypes]
(
	[Title] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AutomationLogs_RuleId' AND object_id = OBJECT_ID('dbo.AutomationLogs'))

/****** Object:  Index [IX_AutomationLogs_RuleId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_AutomationLogs_RuleId] ON [dbo].[AutomationLogs]
(
	[RuleId] ASC,
	[ExecutedAt] DESC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AutomationRules_AccountId' AND object_id = OBJECT_ID('dbo.AutomationRules'))

/****** Object:  Index [IX_AutomationRules_AccountId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_AutomationRules_AccountId] ON [dbo].[AutomationRules]
(
	[AccountId] ASC,
	[IsActive] ASC,
	[Deleted] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AutomationRules_WebhookKey' AND object_id = OBJECT_ID('dbo.AutomationRules'))

/****** Object:  Index [IX_AutomationRules_WebhookKey]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [IX_AutomationRules_WebhookKey] ON [dbo].[AutomationRules]
(
	[WebhookKey] ASC
)
WHERE ([WebhookKey] IS NOT NULL)
WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserId' AND object_id = OBJECT_ID('dbo.CalendarEvents'))

/****** Object:  Index [IX_UserId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_UserId] ON [dbo].[CalendarEvents]
(
	[UserId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserId' AND object_id = OBJECT_ID('dbo.Campaigns'))

/****** Object:  Index [IX_UserId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_UserId] ON [dbo].[Campaigns]
(
	[UserId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Companies_Name' AND object_id = OBJECT_ID('dbo.Companies'))

/****** Object:  Index [IX_Companies_Name]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [IX_Companies_Name] ON [dbo].[Companies]
(
	[Name] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Contacts_Email' AND object_id = OBJECT_ID('dbo.Contacts'))

/****** Object:  Index [IX_Contacts_Email]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [IX_Contacts_Email] ON [dbo].[Contacts]
(
	[Email] ASC
)
WHERE ([Email] IS NOT NULL AND [Email]<>'')
WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_SetupToken' AND object_id = OBJECT_ID('dbo.Customers'))

/****** Object:  Index [IX_Customers_SetupToken]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Customers_SetupToken] ON [dbo].[Customers]
(
	[SetupToken] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_WhatsappNumber' AND object_id = OBJECT_ID('dbo.Customers'))

/****** Object:  Index [IX_Customers_WhatsappNumber]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Customers_WhatsappNumber] ON [dbo].[Customers]
(
	[WhatsappNumber] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DashboardLayouts_UserAccount' AND object_id = OBJECT_ID('dbo.DashboardLayouts'))

/****** Object:  Index [IX_DashboardLayouts_UserAccount]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_DashboardLayouts_UserAccount] ON [dbo].[DashboardLayouts]
(
	[UserId] ASC,
	[AccountId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Deals_AccountId_Stage' AND object_id = OBJECT_ID('dbo.Deals'))

/****** Object:  Index [IX_Deals_AccountId_Stage]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Deals_AccountId_Stage] ON [dbo].[Deals]
(
	[AccountId] ASC,
	[StageId] ASC,
	[CreatedOn] DESC
)
INCLUDE([Status],[DealName],[QuotedAmount],[FinalAmount],[CloseDate],[CompanyId],[PrimaryContactId]) WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmailLogs_AccountId' AND object_id = OBJECT_ID('dbo.EmailLogs'))

/****** Object:  Index [IX_EmailLogs_AccountId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_EmailLogs_AccountId] ON [dbo].[EmailLogs]
(
	[AccountId] ASC,
	[SentAt] DESC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmailLogs_ContactId' AND object_id = OBJECT_ID('dbo.EmailLogs'))

/****** Object:  Index [IX_EmailLogs_ContactId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_EmailLogs_ContactId] ON [dbo].[EmailLogs]
(
	[ContactId] ASC
)
WHERE ([ContactId] IS NOT NULL)
WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmailLogs_DealId' AND object_id = OBJECT_ID('dbo.EmailLogs'))

/****** Object:  Index [IX_EmailLogs_DealId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_EmailLogs_DealId] ON [dbo].[EmailLogs]
(
	[DealId] ASC
)
WHERE ([DealId] IS NOT NULL)
WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmailLogs_LeadId' AND object_id = OBJECT_ID('dbo.EmailLogs'))

/****** Object:  Index [IX_EmailLogs_LeadId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_EmailLogs_LeadId] ON [dbo].[EmailLogs]
(
	[LeadId] ASC
)
WHERE ([LeadId] IS NOT NULL)
WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CampaignId' AND object_id = OBJECT_ID('dbo.Leads'))

/****** Object:  Index [IX_CampaignId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_CampaignId] ON [dbo].[Leads]
(
	[CampaignId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_AccountId_CreatedOn' AND object_id = OBJECT_ID('dbo.Leads'))

/****** Object:  Index [IX_Leads_AccountId_CreatedOn]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Leads_AccountId_CreatedOn] ON [dbo].[Leads]
(
	[AccountId] ASC,
	[Deleted] ASC,
	[CreatedOn] DESC
)
INCLUDE([Name],[Email],[Phone],[Company],[ContactId],[OwnerUserId],[ProspectSource],[Status],[OriginType],[AdName],[TierId],[Score]) WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_Dashboard_Filter' AND object_id = OBJECT_ID('dbo.Leads'))

/****** Object:  Index [IX_Leads_Dashboard_Filter]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Leads_Dashboard_Filter] ON [dbo].[Leads]
(
	[CampaignId] ASC,
	[CreatedOn] ASC
)
INCLUDE([LeadId],[ProspectSource],[StateLead],[LeadScore],[OwnerUserId],[StageId],[IndistrySector],[UpsellPotential],[Called],[BuyingDecision],[BuyingTime],[Name],[Email]) WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'RoleNameIndex' AND object_id = OBJECT_ID('dbo.Roles'))

/****** Object:  Index [RoleNameIndex]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [RoleNameIndex] ON [dbo].[Roles]
(
	[Name] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SavedReports_Account' AND object_id = OBJECT_ID('dbo.SavedReports'))

/****** Object:  Index [IX_SavedReports_Account]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_SavedReports_Account] ON [dbo].[SavedReports]
(
	[AccountId] ASC,
	[Deleted] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TimelineEvents_Entity' AND object_id = OBJECT_ID('dbo.TimelineEvents'))

/****** Object:  Index [IX_TimelineEvents_Entity]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_TimelineEvents_Entity] ON [dbo].[TimelineEvents]
(
	[AccountId] ASC,
	[EntityType] ASC,
	[EntityId] ASC,
	[CreatedOn] DESC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserId' AND object_id = OBJECT_ID('dbo.UserClaims'))

/****** Object:  Index [IX_UserId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_UserId] ON [dbo].[UserClaims]
(
	[UserId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserId' AND object_id = OBJECT_ID('dbo.UserLogins'))

/****** Object:  Index [IX_UserId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_UserId] ON [dbo].[UserLogins]
(
	[UserId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RoleId' AND object_id = OBJECT_ID('dbo.UserRoles'))

/****** Object:  Index [IX_RoleId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_RoleId] ON [dbo].[UserRoles]
(
	[RoleId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserId' AND object_id = OBJECT_ID('dbo.UserRoles'))

/****** Object:  Index [IX_UserId]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_UserId] ON [dbo].[UserRoles]
(
	[UserId] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_NormalizedEmail' AND object_id = OBJECT_ID('dbo.Users'))

/****** Object:  Index [IX_Users_NormalizedEmail]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_Users_NormalizedEmail] ON [dbo].[Users]
(
	[NormalizedEmail] ASC
)
WHERE ([NormalizedEmail] IS NOT NULL)
WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_NormalizedUserName' AND object_id = OBJECT_ID('dbo.Users'))

/****** Object:  Index [IX_Users_NormalizedUserName]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [IX_Users_NormalizedUserName] ON [dbo].[Users]
(
	[NormalizedUserName] ASC
)
WHERE ([NormalizedUserName] IS NOT NULL)
WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UserNameIndex' AND object_id = OBJECT_ID('dbo.Users'))

/****** Object:  Index [UserNameIndex]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE UNIQUE NONCLUSTERED INDEX [UserNameIndex] ON [dbo].[Users]
(
	[UserName] ASC
)WITH (STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WebhookEventLogs_WebhookId_ReceivedAt' AND object_id = OBJECT_ID('dbo.WebhookEventLogs'))

/****** Object:  Index [IX_WebhookEventLogs_WebhookId_ReceivedAt]    Script Date: 16/07/2026 12:47:14 p. m. ******/
CREATE NONCLUSTERED INDEX [IX_WebhookEventLogs_WebhookId_ReceivedAt] ON [dbo].[WebhookEventLogs]
(
	[WebhookId] ASC,
	[ReceivedAt] DESC
)WITH (STATISTICS_NORECOMPUTE = OFF, DROP_EXISTING = OFF, ONLINE = OFF, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO


-- ============================================================================
-- SECCIÓN E — MIGRACIÓN DE DATOS: Leads.AccountId + generación de Deals.
--
-- Contexto: en Querys.sql había un plan de migración mucho más elaborado
-- (Fase 1-3: tablas Leads_New/Activities_New + swap + Contacts/Companies vía
-- polimorfismo) que NUNCA se ejecutó tal cual — se abandonó a favor de un
-- enfoque más simple. Verifiqué esto contra el estado REAL de Profet_new
-- (que sí tengo en vivo) el 2026-07-17 y esto es lo que en realidad pasó ahí:
--
--   - Leads.AccountId = Leads.CampaignId (mapeo 1:1 directo, confirmado:
--     0 filas con AccountId != CampaignId). 113,827 de 114,121 leads (98.7%)
--     quedaron con AccountId asignado — el resto no tenía CampaignId con
--     Account correspondiente.
--   - Leads.StageId NO se tocó — es la MISMA columna vieja, sin transformar.
--   - Contacts y Companies quedaron en 0 filas — NUNCA se pobló esa parte
--     (a pesar de que el Deal sí tiene PrimaryContactId/CompanyId como FK,
--     ambas quedaron NULL en las 113,827 filas). Leads.ContactId también
--     quedó en 0 filas pobladas. Esto es un hueco real, no algo que deba
--     "replicar" — Profet_new tampoco lo tiene resuelto.
--   - Por cada Lead con AccountId se creó exactamente 1 Deal (113,827 Deals,
--     coincide exacto). Patrón confirmado con muestras reales:
--       DealName      = ISNULL(Name, 'Trato sin nombre') + ' - Deal'
--       AccountId     = Leads.AccountId
--       StageId       = NULLIF(Leads.StageId, 0)   (0 → NULL, resto igual)
--       Status        = CASE StateLead WHEN 1 THEN 'Ganado' WHEN 0 THEN 'Perdido' ELSE 'Abierto' END
--       QuotedAmount  = Leads.QuotedAmount (pasa tal cual, confirmado con valores >0)
--       DealType      = 'NewBusiness' (100% de las filas)
--       OriginatingLeadId = Leads.LeadId (0 huérfanos, relación limpia)
--       CloseDate     = probablemente Leads.LeadDate (NUNCA es NULL en los
--                        113,827 Deals, y LeadDate es el único campo NOT NULL
--                        de Leads que encaja — pero esto NO lo pude confirmar
--                        al 100% con muestras, verifícalo con un par de casos
--                        reales en Profet_db antes de confiar en él a ciegas).
--       CompanyId / PrimaryContactId / OwnerUserId / ProspectSourceId / AdName
--       / OriginType / ExternalId / PublicId / LeadLostReasonId / LeadTierId
--       = siempre NULL, nunca se llenaron.
--
-- NO EJECUTADO. Corre esto DESPUÉS de las Secciones A-D de este archivo
-- (necesita que Leads.AccountId/LeadId y Deals ya existan con la estructura
-- nueva). Es idempotente vía el NOT EXISTS del INSERT.
-- ============================================================================

-- E.1: Backfill de Leads.AccountId (solo donde el CampaignId corresponde a un
-- Account ya migrado por la Sección A de este archivo).
UPDATE l
SET l.AccountId = l.CampaignId
FROM dbo.Leads l
WHERE l.AccountId IS NULL
  AND EXISTS (SELECT 1 FROM dbo.Accounts a WHERE a.AccountId = l.CampaignId);
GO

-- E.2: Un Deal por cada Lead con AccountId, replicando el patrón real
-- verificado en Profet_new (ver comentario arriba).
INSERT INTO dbo.Deals (DealName, AccountId, StageId, Status, QuotedAmount, CreatedOn, CloseDate, DealType, OriginatingLeadId)
SELECT
    ISNULL(l.Name, 'Trato sin nombre') + ' - Deal',
    l.AccountId,
    NULLIF(l.StageId, 0),
    CASE WHEN l.StateLead = 1 THEN 'Ganado' WHEN l.StateLead = 0 THEN 'Perdido' ELSE 'Abierto' END,
    l.QuotedAmount,
    l.LeadDate,
    l.LeadDate,  -- CloseDate: mejor hipótesis disponible, ver nota arriba — verificar antes de confiar
    'NewBusiness',
    l.LeadId
FROM dbo.Leads l
WHERE l.AccountId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.Deals d WHERE d.OriginatingLeadId = l.LeadId);
GO


-- ============================================================================
-- SECCIÓN F — Tablas que existen en Profet_db y YA NO tienen lugar en la
-- arquitectura nueva (fueron reemplazadas por el modelo Account/Deal).
-- ⚠️ CONTIENEN DATOS REALES DE PRODUCCIÓN VIEJOS. No tengo una conexión activa
-- a Profet_db para contarte cuántas filas tiene cada una hoy — antes de
-- correr esto, exporta un respaldo (BACKUP DATABASE o exportar a .bak/.csv)
-- de cada una por si luego hace falta recuperar algo.
--
-- Reemplazos: CampaignIndustries->AccountIndustries, CampaignSettings->AccountSettings,
-- CampaignUsers->AccountInternalUsers/DealUsers, CampaingsActiveDates->AccountStatusHistory,
-- LeadLostReasonsPackages->LeadLostReasons+ItemCatalog.
-- ManagerRelations/ManagerAdminRelations: SIN reemplazo en Profet_new todavía
-- (gap real pendiente en el roadmap) — piensa si de verdad quieres borrarlas
-- ya, o esperar a que se construya el módulo de jerarquía de managers.
-- ============================================================================

-- IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CampaignIndustries')     DROP TABLE dbo.CampaignIndustries;
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CampaignSettings')       DROP TABLE dbo.CampaignSettings;
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CampaignUsers')          DROP TABLE dbo.CampaignUsers;
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CampaingsActiveDates')   DROP TABLE dbo.CampaingsActiveDates;
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadLostReasonsPackages') DROP TABLE dbo.LeadLostReasonsPackages;
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ManagerRelations')       DROP TABLE dbo.ManagerRelations;       -- sin reemplazo aún, revisar antes
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ManagerAdminRelations')  DROP TABLE dbo.ManagerAdminRelations;  -- sin reemplazo aún, revisar antes

-- Dejé los DROP comentados a propósito: descomenta solo los que confirmes
-- después de respaldar. No los descomenté yo porque implica borrar datos
-- reales y no tengo forma de verificar cuántas filas tiene cada una.


-- ============================================================================
-- SECCIÓN G — Migración de datos: LeadCalls → Activities + CallDetails.
-- Agregada 2026-07-17 (misma lógica que scripts_profet_new.sql Sección G,
-- adaptada para correr aquí). Requiere que ya hayan corrido las Secciones
-- A-E de ESTE archivo (necesita dbo.Leads.LeadId/AccountId, dbo.Activities
-- con las columnas nuevas, y dbo.CallDetails ya creada).
--
-- Mapeo (idéntico al de Profet_new, ver ese archivo para el detalle):
--   ActivityType='Call' · EntityType='Lead' · EntityId=LeadCalls.Lead_id
--   Subject según LeadCalls.Type · Notes = Status/Phone/Message
--   OwnerUserId = LeadCalls.UserId · AccountId = Leads.AccountId (join)
--   CallDetails.Duration/CallSid = LeadCalls.Duration/call_id
--   CallDetails.RecordingUrl = Record_keys+'/'+RecordName — NO CONFIRMADO
--   como URL reproducible (ver advertencia completa en scripts_profet_new.sql).
--
-- NO EJECUTADO. Pensado para correr una sola vez.
-- ============================================================================

-- NOTA: un INSERT...SELECT normal NO puede referenciar columnas de la tabla
-- origen en su OUTPUT (solo ve "inserted.*") — por eso esto usa MERGE, que sí
-- permite mapear el Id viejo al Id nuevo en el mismo statement.
DECLARE @CallMapOld TABLE (OldLeadCallId INT, NewActivityId INT);

MERGE INTO dbo.Activities AS tgt
USING (
    SELECT
        lc.Id AS OldLeadCallId,
        CASE WHEN lc.Type = 'inbound' THEN 'Llamada entrante'
             WHEN lc.Type = 'outbound' THEN 'Llamada saliente'
             ELSE ISNULL(lc.Type, 'Llamada') END AS Subject,
        lc.[Date] AS CallDate,
        CONCAT_WS(' · ', lc.Status, lc.Phone, lc.Message) AS Notes,
        lc.UserId AS OwnerUserId,
        lc.Lead_id AS EntityId,
        l.AccountId AS AccountId
    FROM dbo.LeadCalls lc
    JOIN dbo.Leads l ON l.LeadId = lc.Lead_id
    WHERE lc.Lead_id IS NOT NULL
      AND (lc.call_id IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.CallDetails cd WHERE cd.CallSid = lc.call_id))
) AS src
ON 1 = 0  -- fuerza que nunca "matchee" — siempre inserta, nunca actualiza
WHEN NOT MATCHED THEN
    -- LeadId y TypeActivityId son columnas LEGACY de Activities (NOT NULL, sin
    -- default, sin FK) — ver la nota completa en scripts_profet_new.sql, misma
    -- lógica: LeadId = mismo lead (cast a int), TypeActivityId = 1 (placeholder,
    -- la tabla ActivityTypes que le daba significado ya no existe/estaba vacía).
    INSERT (LeadId, TypeActivityId, ActivityType, Subject, [Date], Notes, IsCompleted, OwnerUserId, EntityId, EntityType, AccountId, CreatedOn)
    VALUES (CAST(src.EntityId AS INT), 1, 'Call', src.Subject, src.CallDate, src.Notes, 1, src.OwnerUserId, src.EntityId, 'Lead', src.AccountId, GETUTCDATE())
OUTPUT inserted.Id, src.OldLeadCallId INTO @CallMapOld(NewActivityId, OldLeadCallId);

-- SIN GO aquí a propósito: @CallMapOld es variable de tabla, no sobrevive a un
-- límite de batch — debe ir en el mismo batch que el MERGE de arriba.
INSERT INTO dbo.CallDetails (ActivityId, RecordingUrl, Duration, CallSid)
SELECT
    cm.NewActivityId,
    NULLIF(CONCAT(lc.Record_keys, '/', lc.RecordName), '/'),
    lc.Duration,
    lc.call_id
FROM dbo.LeadCalls lc
JOIN @CallMapOld cm ON cm.OldLeadCallId = lc.Id;
GO


-- ============================================================================
-- SECCIÓN H — Migración de datos: LeadPayments → DealPayments (93 filas).
-- Agregada 2026-07-17 (misma lógica que scripts_profet_new.sql Sección H,
-- EJECUTADA y confirmada ahí — ver ese archivo para el detalle completo del
-- mapeo). Requiere que ya hayan corrido las Secciones A-E de este archivo.
--
-- Resumen: LeadPayments.DealId apunta a la tabla vieja LeadDeals (no a
-- Deals nueva) — se resuelve LeadDeals.LeadId o LeadPayments.LeadId directo
-- → Deals.OriginatingLeadId. Verificado en Profet_new: 80 de 93 filas
-- resuelven a un Deal real.
--
-- NO EJECUTADO.
-- ============================================================================

INSERT INTO dbo.DealPayments (DealId, Amount, PaymentDate, Description)
SELECT
    d.DealId,
    lp.Amount,
    lp.[Date],
    CONCAT_WS(' · ', lp.Description, lp.Type)
FROM dbo.LeadPayments lp
LEFT JOIN dbo.LeadDeals ld ON ld.Id = lp.DealId
JOIN dbo.Deals d ON d.OriginatingLeadId = COALESCE(lp.LeadId, ld.LeadId)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.DealPayments dp
    WHERE dp.DealId = d.DealId AND dp.Amount = lp.Amount AND dp.PaymentDate = lp.[Date]
);
GO


-- ============================================================================
-- SECCIÓN I — Migración de datos: LeadNotes → Notes (19,852 filas).
-- Agregada 2026-07-17 (misma lógica que scripts_profet_new.sql Sección I,
-- EJECUTADA y confirmada ahí). Requiere Secciones A-E de este archivo.
--
-- Mapeo directo: Content=Note, AuthorUserId=UserId, CreatedOn=Date,
-- EntityId=LeadId, EntityType='Lead'. Verificado en Profet_new: 19,764 de
-- 19,852 filas resuelven a un Lead existente.
--
-- NO EJECUTADO.
-- ============================================================================

INSERT INTO dbo.Notes (Content, AuthorUserId, CreatedOn, EntityId, EntityType)
SELECT
    CAST(ln.Note AS NVARCHAR(MAX)),
    ln.UserId,
    ln.[Date],
    l.LeadId,
    'Lead'
FROM dbo.LeadNotes ln
JOIN dbo.Leads l ON l.LeadId = ln.LeadId
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Notes n
    WHERE n.EntityId = l.LeadId AND n.EntityType = 'Lead' AND n.CreatedOn = ln.[Date]
      AND (n.Content = CAST(ln.Note AS NVARCHAR(MAX)) OR (n.Content IS NULL AND ln.Note IS NULL))
);
GO


-- ============================================================================
-- SECCIÓN J — Migración de datos: LeadFiles → Attachments (115 filas).
-- Agregada 2026-07-17 (misma lógica que scripts_profet_new.sql Sección J,
-- EJECUTADA y confirmada ahí). Requiere Secciones A-E de este archivo.
--
-- FilePath es una URL INFERIDA del frontend viejo (detailLead.js):
-- 'https://www.burocreativo.com/profet-mail/files/' + nombre — no
-- verificado que el archivo siga ahí. Verificado en Profet_new: 112 de 115
-- filas resuelven a un Lead existente.
--
-- NO EJECUTADO.
-- ============================================================================

INSERT INTO dbo.Attachments (FileName, FilePath, UploaderUserId, CreatedOn, EntityId, EntityType)
SELECT
    lf.Name,
    'https://www.burocreativo.com/profet-mail/files/' + lf.Name,
    lf.UserId,
    lf.[Date],
    l.LeadId,
    'Lead'
FROM dbo.LeadFiles lf
JOIN dbo.Leads l ON l.LeadId = lf.LeadId
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Attachments a
    WHERE a.EntityId = l.LeadId AND a.EntityType = 'Lead' AND a.FileName = lf.Name AND a.CreatedOn = lf.[Date]
);
GO


-- ============================================================================
-- SECCIÓN K — Calls → Activities + CallDetails (54 filas). Agregada
-- 2026-07-17, misma lógica que scripts_profet_new.sql Sección K, EJECUTADA
-- y confirmada ahí. NOTA: NO incluye InboundCalls/DirectCalls (sin LeadId,
-- requerirían cruce por teléfono, no se hizo por riesgo de mal-attribución).
-- ============================================================================
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @CallMap2 TABLE (OldCallId INT, NewActivityId INT);

MERGE INTO dbo.Activities AS tgt
USING (
    SELECT
        c.ID AS OldCallId,
        'Llamada' AS Subject,
        c.[Date] AS CallDate,
        CONCAT_WS(' · ', c.DialCallStatus, c.PhoneNumber) AS Notes,
        c.Userid AS OwnerUserId,
        c.Leadid AS EntityId,
        l.AccountId AS AccountId
    FROM dbo.Calls c
    JOIN dbo.Leads l ON l.LeadId = c.Leadid
    WHERE c.Leadid IS NOT NULL
      AND (c.CallSid IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.CallDetails cd WHERE cd.CallSid = c.CallSid))
) AS src
ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (LeadId, TypeActivityId, ActivityType, Subject, [Date], Notes, IsCompleted, OwnerUserId, EntityId, EntityType, AccountId, CreatedOn)
    VALUES (CAST(src.EntityId AS INT), 1, 'Call', src.Subject, src.CallDate, src.Notes, 1, src.OwnerUserId, src.EntityId, 'Lead', src.AccountId, GETUTCDATE())
OUTPUT inserted.Id, src.OldCallId INTO @CallMap2(NewActivityId, OldCallId);

INSERT INTO dbo.CallDetails (ActivityId, RecordingUrl, Duration, CallSid)
SELECT cm.NewActivityId, c.RecordingUrl, NULL, c.CallSid
FROM dbo.Calls c
JOIN @CallMap2 cm ON cm.OldCallId = c.ID;
GO


-- ============================================================================
-- SECCIÓN L — AccountUsers → AccountInternalUsers (594 filas). Agregada
-- 2026-07-17, misma lógica que scripts_profet_new.sql Sección L.
-- CORRECCIÓN 2026-07-21: el intento de ejecución falló con Msg 547
-- (FK_AccountInternalUsers_Users) — algunas filas de AccountUsers referencian
-- un UserId ya borrado de dbo.Users. Se agregó EXISTS contra dbo.Users para
-- excluirlos. Reintentada con el filtro corregido: EJECUTADA, 590 de 594
-- filas migradas (4 con UserId huérfano, esperado). Confirmado con COUNT(*)
-- real (AccountInternalUsers WHERE RoleInAccount='SalesRep' = 590).
-- ============================================================================

INSERT INTO dbo.AccountInternalUsers (AccountId, UserId, RoleInAccount)
SELECT DISTINCT au.AccountId, au.UserId, 'SalesRep'
FROM dbo.AccountUsers au
WHERE au.AccountId IS NOT NULL
  AND EXISTS (SELECT 1 FROM dbo.Users u WHERE u.Id = au.UserId)
  AND NOT EXISTS (
      SELECT 1 FROM dbo.AccountInternalUsers aiu
      WHERE aiu.AccountId = au.AccountId AND aiu.UserId = au.UserId AND aiu.RoleInAccount = 'SalesRep'
  );
GO


-- ============================================================================
-- SECCIÓN M — Webhooks → AccountWebhooks (28 filas). Agregada 2026-07-17,
-- misma lógica que scripts_profet_new.sql Sección M, EJECUTADA y confirmada
-- ahí. Es preservación histórica, no reactivación funcional.
-- ============================================================================

INSERT INTO dbo.AccountWebhooks (AccountId, Name, Direction, ActionType, TargetUrl, IsActive, CreatedAt)
SELECT
    w.AccountId,
    ISNULL(w.description, w.Action),
    'Outgoing',
    w.Action,
    w.Url,
    1,
    GETUTCDATE()
FROM dbo.Webhooks w
WHERE w.AccountId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.AccountWebhooks aw
      WHERE aw.AccountId = w.AccountId AND aw.TargetUrl = w.Url AND aw.ActionType = w.Action
  );
GO

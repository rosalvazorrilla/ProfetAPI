--Se divide la info de user
-- 1. Asegurar llave primaria en Customers

-- Agregar Email (vital para la invitaciÃ³n)
ALTER TABLE Customers ADD Email VARCHAR(255) NULL;

-- Agregar controles del Wizard
ALTER TABLE Customers ADD SetupToken VARCHAR(128) NULL;
ALTER TABLE Customers ADD SetupStep INT NOT NULL DEFAULT 0;

-- Agregar el nuevo campo de Estatus
ALTER TABLE Customers ADD Status VARCHAR(50) NOT NULL DEFAULT 'Pendiente de Setup';

-- Crear Ã­ndice para que el login con token sea rapidÃ­simo
CREATE INDEX IX_Customers_SetupToken ON Customers(SetupToken);


CREATE TABLE dbo.UserProfiles (
    UserId NVARCHAR(128) NOT NULL,
    FirstName NVARCHAR(MAX) NULL,
    LastName NVARCHAR(MAX) NULL,
    Phone NVARCHAR(MAX) NULL,
    PhoneExt NVARCHAR(MAX) NULL,
    Mobile NVARCHAR(MAX) NULL,
    IndustrySector NVARCHAR(MAX) NULL,
    CallPickerExtensionName VARCHAR(200) NULL,
    CallPickerExtension VARCHAR(200) NULL,
    CallPickerKey VARCHAR(200) NULL,
    ProfilePicture BIT NULL,
    Pass64 VARCHAR(200) NULL,
    IsAdmin BIT NULL,
    LastLoginDate DATETIME2 NULL,
    Preferences NVARCHAR(MAX) NULL,

    CONSTRAINT PK_UserProfiles PRIMARY KEY (UserId),
    CONSTRAINT FK_UserProfiles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
);
GO

-- 2. Migrar los datos de Users a UserProfiles, construyendo el JSON
INSERT INTO dbo.UserProfiles 
    (UserId, FirstName, LastName, Phone, PhoneExt, Mobile, IndustrySector, CallPickerExtensionName, CallPickerExtension, CallPickerKey, ProfilePicture, Pass64, IsAdmin, LastLoginDate, Preferences)
SELECT 
    Id, 
    FirstName, 
    LastName, 
    Phone, 
    PhoneExt, 
    Mobile, 
    IndustrySector, 
    cp_extension_name, 
    cp_extension, 
    cp_key, 
    ProfilePicture, 
    Pass64, 
    isAdmin,
    NULL, -- LastLoginDate se llenarÃ¡ en el futuro
    -- AquÃ­ construimos el JSON dinÃ¡micamente con los valores REALES de la tabla Users
   CONCAT(
        '{',
        '"alertAssignment":', 
        CASE WHEN ISNULL(alertAssignment, 0) = 1 THEN 'true' ELSE 'false' END, 
        ',"hasWhatsApp":false', -- <-- Usamos 'false' como valor por defecto
        '}'
    ) AS Preferences
FROM dbo.Users;
GO
DECLARE @sql NVARCHAR(MAX) = N'';

ALTER TABLE dbo.Users DROP CONSTRAINT [DF__Users__IsAdmin__39AD8A7F];
ALTER TABLE dbo.Users DROP CONSTRAINT [DF__Users__profilePi__3C89F72A];
ALTER TABLE dbo.Users DROP CONSTRAINT [DF__Users__AlertAssi__6462DE5A];

PRINT '--- Generando comandos para borrar CONSTRAINTS de Users. Copia y ejecuta la salida. ---';
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += 'ALTER TABLE dbo.Users DROP CONSTRAINT ' + QUOTENAME(dc.name) + ';' + CHAR(13)
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.Users') AND c.name IN (
    'FirstName', 'LastName', 'Position', 'Address', 'Date', 'CompanyName', 'Phone', 'Web', 'Contact', 'PhoneExt', 
    'Mobile', 'IndustrySector', 'TwilioNumber', 'cp_extension_name', 'cp_extension', 'cp_key', 'ProfilePicture', 
    'Pass64', 'alertAssignment', 'isAdmin'
);
PRINT @sql;
GO
ALTER TABLE dbo.Users
DROP COLUMN FirstName, LastName, Position, Address, Date, CompanyName, Phone, Web, Contact, PhoneExt, Mobile, IndustrySector, TwilioNumber, cp_extension_name, cp_extension, cp_key, ProfilePicture, Pass64, alertAssignment, isAdmin;
GO

-- 1. ConexiÃ³n de Users con Customers
ALTER TABLE dbo.Users
ADD CONSTRAINT FK_Users_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(id);
GO

-- 2. ConexiÃ³n de JerarquÃ­a en Users
ALTER TABLE dbo.Users
ALTER COLUMN ParentId NVARCHAR(128) NULL;
GO
ALTER TABLE dbo.Users
ADD CONSTRAINT FK_Users_ParentUser FOREIGN KEY (ParentId) REFERENCES dbo.Users(Id);
GO

-- 3. ConexiÃ³n de UserTeams
ALTER TABLE dbo.UserTeams
ADD CONSTRAINT FK_UserTeams_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
GO
ALTER TABLE dbo.UserTeams
ADD CONSTRAINT FK_UserTeams_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id);
GO

-- 4. ConexiÃ³n de UserRoles
ALTER TABLE dbo.UserRoles
ADD CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
GO
ALTER TABLE dbo.UserRoles
ADD CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id);
GO

--Planes

CREATE TABLE dbo.Plans (
    PlanId INT PRIMARY KEY IDENTITY(1,1), 
    Name NVARCHAR(100) NOT NULL, 
    Description NVARCHAR(1000) NULL,
    IsPublic BIT NOT NULL DEFAULT 1, 
    IsActive BIT NOT NULL DEFAULT 1
);

CREATE TABLE dbo.Features (
    FeatureId INT PRIMARY KEY IDENTITY(1,1), 
    FeatureCode NVARCHAR(100) NOT NULL UNIQUE, 
    Name NVARCHAR(255) NOT NULL, 
    Description NVARCHAR(1000) NULL
);

CREATE TABLE dbo.AddOns (
    AddOnId INT PRIMARY KEY IDENTITY(1,1), 
    FeatureId INT NOT NULL, 
    Name NVARCHAR(255) NOT NULL, 
    Description NVARCHAR(1000) NULL,
    Price DECIMAL(18, 2) NOT NULL, 
    BillingCycle NVARCHAR(50) NOT NULL,
    CONSTRAINT FK_AddOns_Features FOREIGN KEY (FeatureId) REFERENCES dbo.Features(FeatureId)
);


CREATE TABLE dbo.PlanPriceHistory (
    PriceHistoryId INT PRIMARY KEY IDENTITY(1,1), 
    PlanId INT NOT NULL, 
    MonthlyPrice DECIMAL(18, 2) NOT NULL, 
    AnnualPrice DECIMAL(18, 2) NOT NULL,
    EffectiveDate DATETIME2 NOT NULL, 
    EndDate DATETIME2 NULL,
    CONSTRAINT FK_PlanPriceHistory_Plans FOREIGN KEY (PlanId) REFERENCES dbo.Plans(PlanId)
);


CREATE TABLE dbo.PlanFeatures (
    PlanId INT NOT NULL, FeatureId INT NOT NULL, 
    Limit NVARCHAR(100) NULL,
    CONSTRAINT PK_PlanFeatures PRIMARY KEY (PlanId, FeatureId),
    CONSTRAINT FK_PlanFeatures_Plans FOREIGN KEY (PlanId) REFERENCES dbo.Plans(PlanId),
    CONSTRAINT FK_PlanFeatures_Features FOREIGN KEY (FeatureId) REFERENCES dbo.Features(FeatureId)
);

CREATE TABLE dbo.Subscriptions (
    SubscriptionId INT PRIMARY KEY IDENTITY(1,1), 
    CustomerId INT NOT NULL, 
    PlanId INT NOT NULL, Status NVARCHAR(50) NOT NULL,
    PriceAgreed DECIMAL(18, 2) NOT NULL, 
    BillingCycle NVARCHAR(50) NOT NULL,
    DiscountAmount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    TrialEndDate DATETIME2 NULL, 
    SubscriptionStartDate DATETIME2 NOT NULL, 
    CanceledDate DATETIME2 NULL,
    CONSTRAINT FK_Subscriptions_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(id),
    CONSTRAINT FK_Subscriptions_Plans FOREIGN KEY (PlanId) REFERENCES dbo.Plans(PlanId)
);

CREATE TABLE dbo.SubscriptionPeriods (
    PeriodId INT PRIMARY KEY IDENTITY(1,1), 
    SubscriptionId INT NOT NULL, 
    PeriodStartDate DATETIME2 NOT NULL, 
    PeriodEndDate DATETIME2 NOT NULL,
    AmountBilled DECIMAL(18, 2) NOT NULL, 
    Status NVARCHAR(50) NOT NULL, 
    PaymentDate DATETIME2 NULL, 
    InvoiceUrl NVARCHAR(500) NULL,
    CONSTRAINT FK_SubscriptionPeriods_Subscriptions FOREIGN KEY (SubscriptionId) REFERENCES dbo.Subscriptions(SubscriptionId)
);

CREATE TABLE dbo.CustomerPurchasedAddOns (
    PurchasedAddOnId INT PRIMARY KEY IDENTITY(1,1), 
    SubscriptionId INT NOT NULL, 
    AddOnId INT NOT NULL, 
    PricePaid DECIMAL(18, 2) NOT NULL,
    PurchaseDate DATETIME2 NOT NULL, 
    ExpiryDate DATETIME2 NULL,
    CONSTRAINT FK_PurchasedAddOns_Subscriptions FOREIGN KEY (SubscriptionId) REFERENCES dbo.Subscriptions(SubscriptionId),
    CONSTRAINT FK_PurchasedAddOns_AddOns FOREIGN KEY (AddOnId) REFERENCES dbo.AddOns(AddOnId)
);

DROP TABLE dbo.ManagerAdminRelations;
GO
DROP TABLE dbo.ManagerRelations;
GO


-- ####################################################################
-- ### 2. MEJORA: Limpieza Robusta y Correcta de la Tabla `Users`
-- ### Reemplaza tu secciÃ³n de limpieza de `Users` con este proceso de 2 partes.
-- ####################################################################
PRINT '--- Mejorando el proceso de limpieza de la tabla Users... ---';

-- PARTE A: Generar los comandos para borrar los "candados" (default constraints) automÃ¡ticamente.
-- Esto evita errores y asegura que se borren todos, sin tener que buscarlos a mano.
PRINT '--- PARTE A: Generando comandos para borrar CONSTRAINTS. Copia y ejecuta la salida de este bloque. ---';

DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += 'ALTER TABLE dbo.Users DROP CONSTRAINT ' + QUOTENAME(dc.name) + ';' + CHAR(13)
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.Users') AND c.name IN (
    -- Esta es la lista correcta de columnas que se migraron o se volvieron obsoletas
    'FirstName', 'LastName', 'Position', 'Address', 'Date', 'CompanyName', 'Phone', 'Web', 'Contact', 'PhoneExt', 
    'Mobile', 'IndustrySector', 'TwilioNumber', 'cp_extension_name', 'cp_extension', 'cp_key', 'ProfilePicture', 
    'Pass64', 'alertAssignment', 'isAdmin', 'canEdit' -- 'canEdit' estaba en tu script original
);
PRINT @sql;
GO

--Accounts

BEGIN TRANSACTION; -- Iniciamos una transacciÃ³n para asegurar que todo se ejecute correctamente o nada.
GO

PRINT '--- PASO 1: Creando las nuevas tablas de soporte para Accounts ---';

-- 1.1: Crear la tabla para los usuarios internos (SalesRep, PM)
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AccountInternalUsers]') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.AccountInternalUsers (
        AccountId INT NOT NULL,
        UserId NVARCHAR(128) NOT NULL,
        RoleInAccount NVARCHAR(100) NOT NULL, -- 'SalesRep' o 'ProjectManager'
        CONSTRAINT PK_AccountInternalUsers PRIMARY KEY (AccountId, UserId, RoleInAccount)
    );
    PRINT 'Tabla AccountInternalUsers creada.';
END
ELSE
BEGIN
    PRINT 'Tabla AccountInternalUsers ya existe.';
END
GO

-- 1.2: Crear la tabla para los destinatarios de notificaciones
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AccountNotificationRecipients]') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.AccountNotificationRecipients (
        RecipientId INT PRIMARY KEY IDENTITY(1,1),
        AccountId INT NOT NULL,
        Email NVARCHAR(255) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1
    );
    PRINT 'Tabla AccountNotificationRecipients creada.';
END
ELSE
BEGIN
    PRINT 'Tabla AccountNotificationRecipients ya existe.';
END
GO

-- ####################################################################

PRINT '--- PASO 2: Creando la nueva tabla Accounts ---';
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Accounts]') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.Accounts (
        AccountId INT PRIMARY KEY IDENTITY(1,1),
        Name NVARCHAR(MAX) NOT NULL,
        Description NVARCHAR(MAX) NULL,
        CustomerId INT NOT NULL,
        
        -- Configuraciones
        LandingUrl VARCHAR(200) NULL,
        AssignmentType VARCHAR(100) NULL,
        AssignmentUserId NVARCHAR(128) NULL,
        
        -- Paquetes de ConfiguraciÃ³n (FKs a otras tablas)
        LeadLostReasonsPackagesId INT NULL,
        LeadDealsTypesPackagesId INT NULL,
        ActivitiesTemplateId INT NULL,
        
        -- Estado y Fechas
        Status NVARCHAR(50) NOT NULL DEFAULT 'Por Iniciar', -- Estado en EspaÃ±ol como acordamos
        CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT 'Tabla Accounts creada.';
END
ELSE
BEGIN
    PRINT 'Tabla Accounts ya existe.';
END
GO

-- ####################################################################

PRINT '--- PASO 3: Migrando datos de Campaigns a Accounts y sus tablas de soporte ---';

-- 3.1: Migrar los datos principales a la tabla Accounts
-- Se usa SET IDENTITY_INSERT para mantener los IDs originales y facilitar la migraciÃ³n de tablas dependientes.
SET IDENTITY_INSERT dbo.Accounts ON;

INSERT INTO dbo.Accounts (
    AccountId, Name, Description, CustomerId, LandingUrl, AssignmentType, AssignmentUserId, 
    LeadLostReasonsPackagesId, LeadDealsTypesPackagesId, ActivitiesTemplateId, 
    Status, CreatedOn
)
SELECT 
    Id, Name, Description, CustomerId, LandingUrl, AssignmentType, AssignmentUser, 
    LeadLostReasonsPackagesId, LeadDealsTypesPackagesId, ActivitiesTemplateId,
    CASE WHEN Deleted = 1 THEN 'Archivado' WHEN active = 0 THEN 'Pausado' ELSE 'Activo' END, -- Mapeamos el estado
    ISNULL(Date, GETDATE())
FROM dbo.Campaigns c
WHERE NOT EXISTS (SELECT 1 FROM dbo.Accounts a WHERE a.AccountId = c.Id) and CustomerId is not null; -- Evitar duplicados si se corre de nuevo

SET IDENTITY_INSERT dbo.Accounts OFF;
PRINT 'Datos de Campaigns migrados a Accounts.';
GO

-- 3.2: Migrar los usuarios internos (asumiendo que los campos guardan el UserId)
--INSERT INTO dbo.AccountInternalUsers (AccountId, UserId, RoleInAccount)
--SELECT Id, SalesRep, 'SalesRep'
--FROM dbo.Campaigns
--WHERE SalesRep IS NOT NULL AND LEN(SalesRep) > 1
--AND NOT EXISTS (SELECT 1 FROM dbo.AccountInternalUsers WHERE AccountId = Id AND UserId = SalesRep AND RoleInAccount = 'SalesRep');

--INSERT INTO dbo.AccountInternalUsers (AccountId, UserId, RoleInAccount)
--SELECT Id, AccountMgmt, 'ProjectManager'
--FROM dbo.Campaigns
--WHERE AccountMgmt IS NOT NULL AND LEN(AccountMgmt) > 1
--AND NOT EXISTS (SELECT 1 FROM dbo.AccountInternalUsers WHERE AccountId = Id AND UserId = AccountMgmt AND RoleInAccount = 'ProjectManager');
--PRINT 'Usuarios internos migrados.';
--GO 

-- 3.3: Migrar los correos de notificaciÃ³n (parseando el texto)
INSERT INTO dbo.AccountNotificationRecipients (AccountId, Email)
SELECT Id, value
FROM dbo.Campaigns
CROSS APPLY STRING_SPLIT(EmailLeadBooster, ',')
WHERE EmailLeadBooster IS NOT NULL AND LTRIM(RTRIM(value)) <> '';
PRINT 'Correos de notificaciÃ³n migrados.';
GO


-- ####################################################################

PRINT '--- PASO 4: Evolucionando CampaingsActiveDates a AccountStatusHistory ---';

-- 4.1: Renombrar la tabla y la columna
EXEC sp_rename 'dbo.CampaingsActiveDates', 'AccountStatusHistory';
EXEC sp_rename 'dbo.AccountStatusHistory.id_campaign', 'AccountId', 'COLUMN';
GO

-- 4.2: AÃ±adir la columna de estado y poblarla
ALTER TABLE dbo.AccountStatusHistory ADD Status NVARCHAR(50) NOT NULL DEFAULT 'Por Iniciar';
PRINT 'Tabla CampaingsActiveDates evolucionada a AccountStatusHistory.';
GO

UPDATE ash
SET ash.Status = CASE 
                    WHEN c.Deleted = 1 THEN 'Archivado' 
                    WHEN c.active = 0 THEN 'Pausado' 
                    ELSE 'Activo' 
                 END
FROM dbo.AccountStatusHistory ash
JOIN dbo.Campaigns c ON ash.AccountId = c.Id;
GO

-- ####################################################################

PRINT '--- PASO 5: AÃ±adiendo las llaves forÃ¡neas finales ---';

ALTER TABLE dbo.Accounts
ADD CONSTRAINT FK_Accounts_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(id);
GO
ALTER TABLE dbo.AccountInternalUsers
ADD CONSTRAINT FK_AccountInternalUsers_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId) ON DELETE CASCADE;
GO
ALTER TABLE dbo.AccountInternalUsers
ADD CONSTRAINT FK_AccountInternalUsers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE;
GO
ALTER TABLE dbo.AccountNotificationRecipients
ADD CONSTRAINT FK_AccountNotificationRecipients_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId) ON DELETE CASCADE;
GO
ALTER TABLE dbo.AccountStatusHistory
ADD CONSTRAINT FK_AccountStatusHistory_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId) ON DELETE CASCADE;
GO

PRINT '--- Todas las llaves forÃ¡neas han sido aÃ±adidas. ---';

-- ####################################################################

-- ####################################################################
-- ### 3. COMPLETAR: Re-cableado de Todas las Dependencias de `Campaigns`
-- ### Esta es la secciÃ³n completa que faltaba para actualizar todas las tablas que apuntaban a 'Campaigns'.
-- ####################################################################
PRINT '--- Re-cableando TODAS las tablas dependientes de Campaigns para que apunten a Accounts ---';

-- Tabla: Teams
ALTER TABLE dbo.Teams ADD AccountId INT NULL;
GO
UPDATE t SET t.AccountId = c.Id FROM dbo.Teams t JOIN dbo.Campaigns c ON t.CampaignId = c.Id;
GO
ALTER TABLE dbo.Teams ADD CONSTRAINT FK_Teams_Accounts_New FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO
-- ALTER TABLE dbo.Teams DROP COLUMN CampaignId; -- Descomentar al final

-- Tabla: CampaignIndustries
EXEC sp_rename 'dbo.CampaignIndustries', 'AccountIndustries';
GO
ALTER TABLE dbo.AccountIndustries ADD AccountId INT NULL;
GO
UPDATE dbo.AccountIndustries SET AccountId = CampaignId;
GO
PRINT 'Limpiando tabla: AccountIndustries...';
DELETE FROM dbo.AccountIndustries
WHERE AccountId NOT IN (SELECT AccountId FROM dbo.Accounts);
GO
ALTER TABLE dbo.AccountIndustries ADD CONSTRAINT FK_AccountIndustries_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO


-- ALTER TABLE dbo.AccountIndustries DROP COLUMN CampaignId;

-- Tabla: CampaignSettings
EXEC sp_rename 'dbo.CampaignSettings', 'AccountSettings';
GO
ALTER TABLE dbo.AccountSettings ADD AccountId INT NULL;
GO
UPDATE dbo.AccountSettings SET AccountId = CampId; -- Ojo, el nombre de la columna era 'CampId'
GO
PRINT 'Limpiando tabla: AccountSettings...';
DELETE FROM dbo.AccountSettings
WHERE AccountId NOT IN (SELECT AccountId FROM dbo.Accounts);
GO
ALTER TABLE dbo.AccountSettings ADD CONSTRAINT FK_AccountSettings_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO
-- ALTER TABLE dbo.AccountSettings DROP COLUMN CampId;

-- Tabla: CampaignUsers
EXEC sp_rename 'dbo.CampaignUsers', 'AccountUsers';
GO
ALTER TABLE dbo.AccountUsers ADD AccountId INT NULL;
GO

UPDATE dbo.AccountUsers SET AccountId = CampaignId;
GO
PRINT 'Limpiando tabla: AccountUsers...';
DELETE FROM dbo.AccountUsers
WHERE AccountId NOT IN (SELECT AccountId FROM dbo.Accounts);
GO

ALTER TABLE dbo.AccountUsers ADD CONSTRAINT FK_AccountUsers_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO
-- ALTER TABLE dbo.AccountUsers DROP COLUMN CampaignId;

-- Tabla: Reports
ALTER TABLE dbo.Reports ADD AccountId INT NULL;
GO
UPDATE dbo.Reports SET AccountId = CampaignId;
GO
DELETE FROM dbo.Reports
WHERE AccountId NOT IN (SELECT AccountId FROM dbo.Accounts);
GO
ALTER TABLE dbo.Reports ADD CONSTRAINT FK_Reports_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO
-- ALTER TABLE dbo.Reports DROP COLUMN CampaignId;

-- Tabla: Webhooks
ALTER TABLE dbo.Webhooks ADD AccountId INT NULL;
GO
UPDATE dbo.Webhooks SET AccountId = CampaignId;
GO
PRINT 'Limpiando tabla: Teams...';
DELETE FROM dbo.Webhooks
WHERE AccountId NOT IN (SELECT AccountId FROM dbo.Accounts) AND AccountId IS NOT NULL;
ALTER TABLE dbo.Webhooks ADD CONSTRAINT FK_Webhooks_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO

PRINT '--- MÃ³dulo 2: Adaptando Funnels y Stages existentes... ---';

-- ####################################################################
-- ### PASO 2.1: CREAR LAS NUEVAS TABLAS PARA LA FUNCIONALIDAD DE PLANTILLAS
-- ### Estas tablas se crearÃ¡n vacÃ­as, listas para que las uses como una nueva caracterÃ­stica.
-- ####################################################################
PRINT '--- Creando tablas para la nueva funcionalidad de Plantillas de Funnels... ---';

CREATE TABLE dbo.FunnelTemplates (
    TemplateId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(1000) NULL
);
GO
CREATE TABLE dbo.FunnelTemplateStages (
    TemplateStageId INT PRIMARY KEY IDENTITY(1,1),
    TemplateId INT NOT NULL,
    StageName NVARCHAR(255) NOT NULL,
    "Order" INT NOT NULL,
    CONSTRAINT FK_FunnelTemplateStages_Templates FOREIGN KEY (TemplateId) REFERENCES dbo.FunnelTemplates(TemplateId) ON DELETE CASCADE
);
GO

-- ####################################################################
-- ### PASO 2.2: VINCULAR TUS FUNNELS EXISTENTES A LAS NUEVAS ACCOUNTS
-- ### Mantenemos tus Funnels intactos y solo actualizamos su "dueÃ±o".
-- ####################################################################
PRINT '--- Vinculando Funnels existentes a las nuevas Accounts... ---';

-- 1. AÃ±adimos la columna AccountId a la tabla Funnels
ALTER TABLE dbo.Funnels ADD AccountId INT NULL;
GO

-- 2. Migramos la relaciÃ³n: Asignamos cada Funnel a la Account correspondiente
--    basÃ¡ndonos en el FunnelId que tenÃ­as en la vieja tabla Campaigns.
UPDATE f
SET f.AccountId = c.Id
FROM dbo.Funnels f
JOIN dbo.Campaigns c ON f.Id = c.FunnelId;
GO

-- 3. Creamos la llave forÃ¡nea para formalizar la relaciÃ³n
ALTER TABLE dbo.Funnels
ADD CONSTRAINT FK_Funnels_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO

-- 4. Nos aseguramos de que Stages siga bien conectada a Funnels
ALTER TABLE dbo.Stages
ADD CONSTRAINT FK_Stages_Funnels FOREIGN KEY (FunnelId) REFERENCES dbo.Funnels(Id);
GO

BEGIN TRANSACTION;
GO

PRINT '--- MÃ“DULO 3: Creando y Migrando el Motor de CalificaciÃ³n... ---';

-- ####################################################################
-- ### PASO 3.1: CREAR LAS TABLAS DEL MOTOR DE CALIFICACIÃ“N
-- ####################################################################
PRINT '--- Creando las tablas del Motor de CalificaciÃ³n... ---';

CREATE TABLE dbo.TemplateCategories( CategoryId INT PRIMARY KEY IDENTITY(1,1), Name NVARCHAR(255) NOT NULL );
GO
CREATE TABLE dbo.ScoringTemplates( TemplateId INT PRIMARY KEY IDENTITY(1,1), CategoryId INT NULL, Name NVARCHAR(255) NOT NULL, Description NVARCHAR(1000) NULL, CONSTRAINT FK_ScoringTemplates_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.TemplateCategories(CategoryId) );
GO
CREATE TABLE dbo.ScoringTemplateQuestions( TemplateQuestionId INT PRIMARY KEY IDENTITY(1,1), TemplateId INT NOT NULL, QuestionText NVARCHAR(1000) NOT NULL, CONSTRAINT FK_TemplateQuestions_Templates FOREIGN KEY (TemplateId) REFERENCES dbo.ScoringTemplates(TemplateId) ON DELETE CASCADE );
GO
CREATE TABLE dbo.ScoringTemplateAnswerOptions( TemplateAnswerId INT PRIMARY KEY IDENTITY(1,1), TemplateQuestionId INT NOT NULL, AnswerText NVARCHAR(1000) NOT NULL, CONSTRAINT FK_TemplateAnswerOptions_TemplateQuestions FOREIGN KEY (TemplateQuestionId) REFERENCES dbo.ScoringTemplateQuestions(TemplateQuestionId) ON DELETE CASCADE );
GO
CREATE TABLE dbo.ScoringModels( ScoringModelId INT PRIMARY KEY IDENTITY(1,1), AccountId INT NOT NULL, Name NVARCHAR(255) NOT NULL, CONSTRAINT FK_ScoringModels_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId) ON DELETE CASCADE );
GO
CREATE TABLE dbo.ScoringQuestions( QuestionId INT PRIMARY KEY IDENTITY(1,1), ScoringModelId INT NOT NULL, QuestionText NVARCHAR(1000) NOT NULL, CONSTRAINT FK_ScoringQuestions_Models FOREIGN KEY (ScoringModelId) REFERENCES dbo.ScoringModels(ScoringModelId) ON DELETE CASCADE );
GO
CREATE TABLE dbo.ScoringAnswerOptions( AnswerOptionId INT PRIMARY KEY IDENTITY(1,1), QuestionId INT NOT NULL, AnswerText NVARCHAR(1000) NOT NULL, CONSTRAINT FK_AnswerOptions_Questions FOREIGN KEY (QuestionId) REFERENCES dbo.ScoringQuestions(QuestionId) ON DELETE CASCADE );
GO
CREATE TABLE dbo.ScoringRules( RuleId INT PRIMARY KEY IDENTITY(1,1), ScoringModelId INT NOT NULL, ConditionQuestionId INT NULL, ConditionAnswerOptionId INT NULL, ActionType NVARCHAR(100) NOT NULL, ActionValue NVARCHAR(255) NOT NULL, ExecutionOrder INT NOT NULL DEFAULT 0, CONSTRAINT FK_ScoringRules_Models FOREIGN KEY (ScoringModelId) REFERENCES dbo.ScoringModels(ScoringModelId), CONSTRAINT FK_ScoringRules_Questions FOREIGN KEY (ConditionQuestionId) REFERENCES dbo.ScoringQuestions(QuestionId), CONSTRAINT FK_ScoringRules_AnswerOptions FOREIGN KEY (ConditionAnswerOptionId) REFERENCES dbo.ScoringAnswerOptions(AnswerOptionId) );
GO
CREATE TABLE dbo.LeadTiers( TierId INT PRIMARY KEY IDENTITY(1,1), Name NVARCHAR(100) NOT NULL );
GO
INSERT INTO dbo.LeadTiers (Name) VALUES ('EstÃ¡ndar'), ('Premier');
GO
CREATE TABLE dbo.LeadScoringAnswers ( ScoringAnswerId INT PRIMARY KEY IDENTITY(1,1), LeadId BIGINT NOT NULL, QuestionId INT NOT NULL, AnswerOptionId INT NOT NULL );
GO

-- ####################################################################
-- ### PASO 3.2: MIGRAR TU SISTEMA 'LEADPACKAGES' AL NUEVO MOTOR
-- ####################################################################
PRINT '--- Migrando el sistema de calificaciÃ³n existente... ---';

-- 1. Migrar catÃ¡logos existentes a las nuevas plantillas
INSERT INTO dbo.ScoringTemplates (Name, Description) SELECT DescriptionES, DescriptionES FROM dbo.LeadPackages;
GO
INSERT INTO dbo.ScoringTemplateQuestions (TemplateId, QuestionText) SELECT st.TemplateId, lq.DescriptionES FROM dbo.LeadQuestions lq JOIN dbo.LeadPackages lp ON lq.LeadPackageId = lp.Id JOIN dbo.ScoringTemplates st ON lp.DescriptionES = st.Name;
GO
INSERT INTO dbo.ScoringTemplateAnswerOptions (TemplateQuestionId, AnswerText) SELECT stq.TemplateQuestionId, la.DescriptionES FROM dbo.LeadAnswers la JOIN dbo.LeadQuestions lq ON la.LeadQuestionId = lq.Id JOIN dbo.ScoringTemplateQuestions stq ON lq.DescriptionES = stq.QuestionText;
GO

-- 2. Crear los ScoringModels "vivos" para cada Account que tenÃ­a un LeadPackage asignado
INSERT INTO dbo.ScoringModels (AccountId, Name) SELECT a.AccountId, CONCAT('Calificador para ', a.Name) FROM dbo.Accounts a JOIN dbo.Campaigns c ON a.AccountId = c.Id WHERE c.LeadPackageId IS NOT NULL;
GO

-- 3. Copiar las preguntas y respuestas de la plantilla al modelo vivo
INSERT INTO dbo.ScoringQuestions (ScoringModelId, QuestionText) SELECT sm.ScoringModelId, stq.QuestionText FROM dbo.Accounts a JOIN dbo.Campaigns c ON a.AccountId = c.Id JOIN dbo.LeadPackages lp ON c.LeadPackageId = lp.Id JOIN dbo.ScoringTemplates st ON lp.DescriptionES = st.Name JOIN dbo.ScoringTemplateQuestions stq ON st.TemplateId = stq.TemplateId JOIN dbo.ScoringModels sm ON a.AccountId = sm.AccountId;
GO
INSERT INTO dbo.ScoringAnswerOptions (QuestionId, AnswerText) SELECT sq.QuestionId, stao.AnswerText FROM dbo.Accounts a JOIN dbo.Campaigns c ON a.AccountId = c.Id JOIN dbo.LeadPackages lp ON c.LeadPackageId = lp.Id JOIN dbo.ScoringTemplates st ON lp.DescriptionES = st.Name JOIN dbo.ScoringTemplateQuestions stq ON st.TemplateId = stq.TemplateId JOIN dbo.ScoringTemplateAnswerOptions stao ON stq.TemplateQuestionId = stao.TemplateQuestionId JOIN dbo.ScoringModels sm ON a.AccountId = sm.AccountId JOIN dbo.ScoringQuestions sq ON sm.ScoringModelId = sq.ScoringModelId AND stq.QuestionText = sq.QuestionText;
GO

-- 4. Traducir la vieja lÃ³gica de puntuaciÃ³n a Reglas 'ADD_POINTS'
INSERT INTO dbo.ScoringRules (ScoringModelId, ConditionQuestionId, ConditionAnswerOptionId, ActionType, ActionValue)
SELECT sq.ScoringModelId, sq.QuestionId, sao.AnswerOptionId, 'ADD_POINTS', la.Value FROM dbo.LeadAnswers la JOIN dbo.LeadQuestions lq ON la.LeadQuestionId = lq.Id JOIN dbo.ScoringQuestions sq ON lq.DescriptionES = sq.QuestionText JOIN dbo.ScoringAnswerOptions sao ON sq.QuestionId = sao.QuestionId AND la.DescriptionES = sao.AnswerText WHERE ISNUMERIC(la.Value) = 1;
GO

-- ####################################################################
-- ### PASO 3.3: MIGRAR LAS RESPUESTAS HISTÃ“RICAS DE LOS LEADS (PATRÃ“N)
-- ####################################################################
PRINT '--- Migrando las respuestas histÃ³ricas de los Leads... ---';
-- **ACCIÃ“N REQUERIDA:** Este es un patrÃ³n que deberÃ¡s repetir para cada campo de calificaciÃ³n de tu tabla `Leads` (Budget, BuyingTime, etc.)

-- Ejemplo para la pregunta "Budget"
INSERT INTO dbo.LeadScoringAnswers (LeadId, QuestionId, AnswerOptionId)
SELECT l.Id, sq.QuestionId, sao.AnswerOptionId
FROM dbo.Leads l
JOIN dbo.LeadAnswers la ON l.Budget = la.Value
JOIN dbo.LeadQuestions lq ON la.LeadQuestionId = lq.Id AND lq.LeadField = 'Budget'
JOIN dbo.ScoringQuestions sq ON lq.DescriptionES = sq.QuestionText
JOIN dbo.ScoringAnswerOptions sao ON sq.QuestionId = sao.QuestionId AND la.DescriptionES = sao.AnswerText
WHERE l.Budget IS NOT NULL;
GO
-- Repite el bloque INSERT anterior para tus otros campos de calificaciÃ³n (`BuyingTime`, etc.)


-- ####################################################################
-- ### PASO 3.4: LIMPIEZA DE TODAS LAS TABLAS DE CALIFICACIÃ“N OBSOLETAS
-- ####################################################################
PRINT '--- Limpieza de tablas de calificaciÃ³n obsoletas... ---';
PRINT '--- Revisa que la migraciÃ³n sea correcta antes de descomentar y ejecutar este bloque. ---';
/*
-- Reemplazada por ScoringTemplates
DROP TABLE dbo.LeadPackages;

-- Reemplazada por ScoringTemplateQuestions y ScoringQuestions
DROP TABLE dbo.LeadQuestions;

-- Reemplazada por ScoringTemplateAnswerOptions y ScoringAnswerOptions
DROP TABLE dbo.LeadAnswers;

-- Reemplazadas por el nuevo Motor de Reglas
DROP TABLE dbo.ScoreQuestions;
DROP TABLE dbo.ScoreIndicators;
DROP TABLE dbo.SelectLeads;
DROP TABLE dbo.SelectsLeads;

*/
GO


COMMIT TRANSACTION;
GO

PRINT '--- MÃ³dulo de CalificaciÃ³n completado. ---';


-- ####################################################################
-- ### INICIO DE LA TRANSACCIÃ“N GLOBAL
-- ####################################################################
BEGIN TRANSACTION;
GO

-- ####################################################################
-- ### FASE 1: CREAR LAS NUEVAS TABLAS
-- ### Creamos el esqueleto de todo el nuevo nÃºcleo del CRM.
-- ####################################################################
PRINT '--- FASE 1: Creando las nuevas tablas del nÃºcleo del CRM... ---';

-- 1.1: El Directorio Central
CREATE TABLE dbo.Companies (
    CompanyId INT PRIMARY KEY IDENTITY(1,1), Name NVARCHAR(255) NOT NULL, Website NVARCHAR(500) NULL, PhoneNumber NVARCHAR(100) NULL,
    Address NVARCHAR(1000) NULL, City NVARCHAR(255) NULL, State NVARCHAR(255) NULL, PostalCode NVARCHAR(50) NULL,
    LifecycleStatus NVARCHAR(50) NOT NULL DEFAULT 'Prospecto',
    CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(), ModifiedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
GO
CREATE TABLE dbo.Contacts (
    ContactId INT PRIMARY KEY IDENTITY(1,1), CompanyId INT NULL, FirstName NVARCHAR(255) NULL, LastName NVARCHAR(255) NULL,
    Email NVARCHAR(255) NULL, PhoneNumber NVARCHAR(100) NULL, Position NVARCHAR(255) NULL, OriginatingLeadId BIGINT NULL,
    CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(), ModifiedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_Contacts_Email UNIQUE (Email)
);
GO

-- 1.2: El Flujo de Trabajo
CREATE TABLE dbo.Leads_New (
    LeadId BIGINT PRIMARY KEY IDENTITY(1,1), AccountId INT NOT NULL, OwnerUserId NVARCHAR(128) NOT NULL, ContactId INT NOT NULL,
    ProspectSource NVARCHAR(MAX) NULL, AdName VARCHAR(200) NULL, ContactFormId INT NULL,
    InitialMessage NVARCHAR(MAX) NULL, OriginType NVARCHAR(50) NOT NULL DEFAULT 'Inbound',
    Status NVARCHAR(50) NOT NULL DEFAULT 'Nuevo',
    CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
GO
CREATE TABLE dbo.Deals (
    DealId INT PRIMARY KEY IDENTITY(1,1), PublicId VARCHAR(20) NULL, ExternalId VARCHAR(100) NULL,
    DealName NVARCHAR(500) NOT NULL, QuotedAmount DECIMAL(18, 2) NULL, FinalAmount DECIMAL(18, 2) NULL,
    AccountId INT NOT NULL, CompanyId INT NULL, PrimaryContactId INT NULL, StageId INT NULL, LeadLostReasonId INT NULL, LeadTierId INT NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Abierto',
    CloseDate DATETIME2 NULL, DealType NVARCHAR(50) NOT NULL DEFAULT 'NewBusiness',
    OriginatingLeadId BIGINT NULL, CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ProspectSource NVARCHAR(MAX) NULL, AdName VARCHAR(200) NULL, OriginType NVARCHAR(50) NULL
);
GO
CREATE TABLE dbo.DealUsers ( DealId INT NOT NULL, UserId NVARCHAR(128) NOT NULL, RoleInDeal NVARCHAR(50) NOT NULL, CONSTRAINT PK_DealUsers PRIMARY KEY (DealId, UserId));
GO

-- 1.3: El Historial Unificado (Tablas PolimÃ³rficas)
CREATE TABLE dbo.Notes (NoteId INT PRIMARY KEY IDENTITY(1,1), Content NVARCHAR(MAX) NULL, AuthorUserId NVARCHAR(128) NULL, CreatedOn DATETIME2, EntityId BIGINT, EntityType VARCHAR(50));
GO
CREATE TABLE dbo.Attachments (AttachmentId INT PRIMARY KEY IDENTITY(1,1), FileName NVARCHAR(255), FilePath NVARCHAR(1000), UploaderUserId NVARCHAR(128), CreatedOn DATETIME2, EntityId BIGINT, EntityType VARCHAR(50));
GO
CREATE TABLE dbo.Activities_New ( ActivityId INT PRIMARY KEY IDENTITY(1,1), ActivityType NVARCHAR(50) NULL, Subject NVARCHAR(500) NULL, "Date" DATETIME2, Notes NVARCHAR(MAX) NULL, IsCompleted BIT, OwnerUserId NVARCHAR(128), EntityId BIGINT, EntityType VARCHAR(50));
GO
CREATE TABLE dbo.CallDetails ( CallDetailId INT PRIMARY KEY IDENTITY(1,1), ActivityId INT NOT NULL, RecordingUrl VARCHAR(500) NULL, Duration VARCHAR(255) NULL, CallSid VARCHAR(100) NULL);
GO

-- 1.4: Tablas de Soporte Adaptadas
CREATE TABLE dbo.DealPayments ( PaymentId INT PRIMARY KEY IDENTITY(1,1), DealId INT NOT NULL, Amount DECIMAL(18,2), PaymentDate DATETIME2, Description NVARCHAR(500) );
GO
CREATE TABLE dbo.ContactReferrals ( ReferralId INT PRIMARY KEY IDENTITY(1,1), ReferrerContactId INT NOT NULL, ReferredContactId INT NOT NULL, Description NVARCHAR(500), ReferralDate DATETIME2 );
GO

-- ####################################################################
-- ### FASE 2: LA GRAN MIGRACIÃ“N DE DATOS
-- ####################################################################
PRINT '--- FASE 2: Iniciando la Gran MigraciÃ³n desde Leads y tablas relacionadas... ---';

-- 2.1: Poblar el directorio central
INSERT INTO dbo.Companies (Name, PhoneNumber, Address)
SELECT DISTINCT Company, Phone, StreetAndNumber FROM dbo.Leads WHERE Company IS NOT NULL AND Company <> '';
GO
INSERT INTO dbo.Contacts (CompanyId, FirstName, Email, PhoneNumber, Position, OriginatingLeadId)
SELECT comp.CompanyId, le.Name, le.Email, le.Phone, le.Position, le.Id
FROM dbo.Leads le LEFT JOIN dbo.Companies comp ON le.Company = comp.Name;
GO

-- 2.2: Poblar la nueva tabla de Leads (simplificada)
INSERT INTO dbo.Leads_New (AccountId, OwnerUserId, ContactId, ProspectSource, AdName, InitialMessage, OriginType, Status, CreatedOn)
SELECT 
    le.AccountId, ISNULL(le.UserId, '00000000-0000-0000-0000-000000000000'), ct.ContactId, le.ProspectSource, le.AdName, le.MessageSent, 
    CASE WHEN le.Outbound = 1 THEN 'Outbound' ELSE 'Inbound' END,
    'Convertido', -- Asumimos que todos los leads viejos ya fueron procesados
    le.LeadDate
FROM dbo.Leads le
JOIN dbo.Contacts ct ON le.Id = ct.OriginatingLeadId
WHERE le.AccountId IS NOT NULL AND le.UserId IS NOT NULL;
GO

-- 2.3: Poblar la tabla de Deals
INSERT INTO dbo.Deals (DealName, QuotedAmount, FinalAmount, AccountId, CompanyId, PrimaryContactId, StageId, LeadLostReasonId, Status, CloseDate, ExternalId, OriginatingLeadId, CreatedOn, ProspectSource, AdName, OriginType)
SELECT
    ISNULL(le.Name, 'Trato sin nombre'), le.QuotedAmount, le.SellApproxAmount, le.AccountId, comp.CompanyId, ct.ContactId, le.StageId, le.LeadLostReasonsId,
    CASE WHEN le.statelead = 1 THEN 'Ganado' WHEN le.statelead = 0 THEN 'Perdido' ELSE 'Abierto' END,
    le.StateLeadDate, le.DealId, le.Id, le.LeadDate, le.ProspectSource, le.AdName, CASE WHEN le.Outbound = 1 THEN 'Outbound' ELSE 'Inbound' END
FROM dbo.Leads le
JOIN dbo.Contacts ct ON le.Id = ct.OriginatingLeadId
LEFT JOIN dbo.Companies comp ON ct.CompanyId = comp.CompanyId
WHERE le.AccountId IS NOT NULL;
GO
INSERT INTO dbo.DealUsers (DealId, UserId, RoleInDeal)
SELECT d.DealId, l.UserId, 'Owner'
FROM dbo.Deals d JOIN dbo.Leads l ON d.OriginatingLeadId = l.Id WHERE l.UserId IS NOT NULL;
GO

-- 2.4: Migrar las tablas relacionadas al sistema polimÃ³rfico
INSERT INTO dbo.Notes (Content, AuthorUserId, CreatedOn, EntityId, EntityType) SELECT Note, UserId, Date, LeadId, 'Lead' FROM dbo.LeadNotes;
GO
INSERT INTO dbo.Notes (Content, EntityId, EntityType) SELECT Comments, Id, 'Lead' FROM dbo.Leads WHERE Comments IS NOT NULL;
GO
INSERT INTO dbo.Attachments (FileName, UploaderUserId, CreatedOn, EntityId, EntityType) SELECT Name, UserId, Date, LeadId, 'Lead' FROM dbo.LeadFiles;
GO
INSERT INTO dbo.Activities_New (ActivityType, Subject, "Date", Notes, IsCompleted, OwnerUserId, EntityId, EntityType) SELECT ta.Title, a.Title, a.Date, a.Notes, a.Completed, a.UserId, a.LeadId, 'Lead' FROM dbo.Activities a JOIN dbo.TypeActivitiys ta ON a.TypeActivityId = ta.Id;
GO
-- MigraciÃ³n de LeadCalls a Activities y CallDetails
DECLARE @CallMap TABLE (OldLeadCallId INT, NewActivityId INT);
INSERT INTO dbo.Activities_New (ActivityType, Subject, "Date", OwnerUserId, EntityId, EntityType, IsCompleted)
OUTPUT INSERTED.ActivityId, T.id INTO @CallMap(NewActivityId, OldLeadCallId)
SELECT 'Call', lc.RecordName, lc.date, lc.UserId, lc.lead_id, 'Lead', CASE WHEN lc.status = 'completed' THEN 1 ELSE 0 END
FROM dbo.LeadCalls lc;
INSERT INTO dbo.CallDetails (ActivityId, RecordingUrl, Duration, CallSid)
SELECT cm.NewActivityId, CONCAT(lc.record_keys, '/', lc.RecordName), lc.duration, lc.call_id
FROM dbo.LeadCalls lc JOIN @CallMap cm ON lc.id = cm.OldLeadCallId;
GO

-- 2.5: Migrar tablas de soporte a su nuevo hogar
INSERT INTO dbo.DealPayments (DealId, Amount, PaymentDate, Description) SELECT d.DealId, lp.Amount, lp.Date, lp.Description FROM dbo.LeadPayments lp JOIN dbo.Deals d ON lp.DealId = d.DealId;
GO
INSERT INTO dbo.ContactReferrals (ReferrerContactId, ReferredContactId, Description, ReferralDate) SELECT c1.ContactId, c2.ContactId, lr.Description, lr.Date FROM dbo.LeadRefers lr JOIN dbo.Contacts c1 ON lr.LeadId = c1.OriginatingLeadId JOIN dbo.Contacts c2 ON lr.ReferId = c2.OriginatingLeadId;
GO

-- 2.6: MigraciÃ³n de Campos Personalizados ("Variables")
-- **ACCIÃ“N REQUERIDA:** Repite este patrÃ³n para cada columna de "variable" que quieras migrar.
PRINT '--- Migrando Campos Personalizados (Variables)... ---';
INSERT INTO dbo.CustomFieldValues (FieldId, EntityId, EntityType, Value)
SELECT 
    (SELECT FieldId FROM CustomFieldDefinitions WHERE FieldCode = 'Size'),
    d.DealId, 'Deal', l.Size
FROM dbo.Leads l JOIN dbo.Deals d ON l.Id = d.OriginatingLeadId WHERE l.Size IS NOT NULL;
GO

-- ####################################################################
-- ### FASE 3: AÃ‘ADIR LLAVES FORÃNEAS Y LIMPIEZA FINAL
-- ####################################################################
PRINT '--- FASE 3: AÃ±adiendo llaves forÃ¡neas y limpiando... ---';

-- 3.1: AÃ±adir todas las llaves forÃ¡neas
ALTER TABLE dbo.Deals ADD CONSTRAINT FK_Deals_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO
ALTER TABLE dbo.Deals ADD CONSTRAINT FK_Deals_Companies FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(CompanyId);
GO
ALTER TABLE dbo.Deals ADD CONSTRAINT FK_Deals_Contacts FOREIGN KEY (PrimaryContactId) REFERENCES dbo.Contacts(ContactId);
GO
ALTER TABLE dbo.DealUsers ADD CONSTRAINT FK_DealUsers_Deals FOREIGN KEY (DealId) REFERENCES dbo.Deals(DealId);
GO
ALTER TABLE dbo.DealUsers ADD CONSTRAINT FK_DealUsers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
GO
ALTER TABLE dbo.CallDetails ADD CONSTRAINT FK_CallDetails_Activities FOREIGN KEY (ActivityId) REFERENCES dbo.Activities_New(ActivityId) ON DELETE CASCADE;
GO
-- ... y el resto de las llaves forÃ¡neas para las nuevas tablas.

-- 3.2: Renombrar y Limpiar
EXEC sp_rename 'dbo.Leads', 'Leads_Old_Archived';
EXEC sp_rename 'dbo.Leads_New', 'Leads';
EXEC sp_rename 'dbo.Activities', 'Activities_Old_Archived';
EXEC sp_rename 'dbo.Activities_New', 'Activities';
GO
PRINT '--- Revisa que la migraciÃ³n sea correcta antes de descomentar y ejecutar el bloque de limpieza final. ---';
/*
DROP TABLE dbo.LeadNotes;
DROP TABLE dbo.LeadFiles;
DROP TABLE dbo.LeadLogs;
DROP TABLE dbo.StageLeadLogs;
DROP TABLE dbo.LeadCalls;
DROP TABLE dbo.Calls;
DROP TABLE dbo.DirectCalls;
DROP TABLE dbo.InboundCalls;
DROP TABLE dbo.LeadRefers;
DROP TABLE dbo.LeadPayments;
DROP TABLE dbo.LeadCommissions;
DROP TABLE dbo.LeadsTags;
DROP TABLE dbo.TagsLeads;
DROP TABLE dbo.LeadDeals;
DROP TABLE dbo.DealsTypesDeals;
DROP TABLE dbo.LeadDealsTypes;
DROP TABLE dbo.LeadDealsTypesPackages;
DROP TABLE dbo.OutputLeads;
DROP TABLE dbo.Leads_Old_Archived;
DROP TABLE dbo.Activities_Old_Archived;
*/

COMMIT TRANSACTION;
GO

-- ####################################################################
-- ### FIX: Renombrar columna Order â†’ StageOrder en FunnelTemplateStages
-- ### (Order es palabra reservada en SQL Server)
-- ####################################################################
EXEC sp_rename 'dbo.FunnelTemplateStages.Order', 'StageOrder', 'COLUMN';
GO

-- ####################################################################
-- ### TABLAS FALTANTES: CustomFieldDefinitions, AccountCustomFields,
-- ### CustomFieldValues â€” ejecutar en Azure SQL una sola vez
-- ####################################################################

-- Pool global de variables capturables en leads
CREATE TABLE dbo.CustomFieldDefinitions (
    FieldId    INT           IDENTITY(1,1) NOT NULL,
    FieldCode  NVARCHAR(100) NOT NULL,
    FieldName  NVARCHAR(200) NOT NULL,
    FieldType  NVARCHAR(50)  NOT NULL CONSTRAINT DF_CFD_FieldType DEFAULT 'text',
    CONSTRAINT PK_CustomFieldDefinitions PRIMARY KEY (FieldId),
    CONSTRAINT UQ_CustomFieldDefinitions_FieldCode UNIQUE (FieldCode)
);
GO

-- Variables activadas por Account (llave compuesta AccountId + FieldId)
CREATE TABLE dbo.AccountCustomFields (
    AccountId       INT NOT NULL,
    FieldId         INT NOT NULL,
    IsVisibleOnCard BIT NOT NULL CONSTRAINT DF_ACF_IsVisibleOnCard DEFAULT 0,
    CONSTRAINT PK_AccountCustomFields PRIMARY KEY (AccountId, FieldId),
    CONSTRAINT FK_ACF_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId) ON DELETE CASCADE,
    CONSTRAINT FK_ACF_FieldDef FOREIGN KEY (FieldId)   REFERENCES dbo.CustomFieldDefinitions(FieldId)
);
GO

-- Valores de campos personalizados por entidad (Lead, Deal, Contact, etc.)
CREATE TABLE dbo.CustomFieldValues (
    ValueId    INT           IDENTITY(1,1) NOT NULL,
    EntityId   BIGINT        NOT NULL,
    EntityType NVARCHAR(50)  NOT NULL,
    FieldId    INT           NOT NULL,
    Value      NVARCHAR(MAX) NULL,
    CONSTRAINT PK_CustomFieldValues PRIMARY KEY (ValueId),
    CONSTRAINT FK_CFV_FieldDef FOREIGN KEY (FieldId) REFERENCES dbo.CustomFieldDefinitions(FieldId)
);
GO

-- ####################################################################
-- ### SEED: Plantillas de Embudos de Venta (3 sugerencias base)
-- ####################################################################

-- â”€â”€ 1. Ciclo de Venta B2B â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
--    Para ventas empresariales con ciclo largo y mÃºltiples decisores
DECLARE @FunnelB2B INT;
INSERT INTO dbo.FunnelTemplates (Name, Description)
VALUES ('Ciclo de Venta B2B', 'Embudo para ventas empresariales con mÃºltiples etapas de evaluaciÃ³n y decisores.');
SET @FunnelB2B = SCOPE_IDENTITY();

INSERT INTO dbo.FunnelTemplateStages (TemplateId, StageName, StageOrder) VALUES
(@FunnelB2B, 'Prospecto',           1),
(@FunnelB2B, 'Primer Contacto',     2),
(@FunnelB2B, 'CalificaciÃ³n',        3),
(@FunnelB2B, 'Demo / PresentaciÃ³n', 4),
(@FunnelB2B, 'CotizaciÃ³n',          5),
(@FunnelB2B, 'NegociaciÃ³n',         6),
(@FunnelB2B, 'Cierre',              7);
GO

-- â”€â”€ 2. Venta Directa â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
--    Para ventas rÃ¡pidas, transaccionales o de consumidor final
DECLARE @FunnelDirecto INT;
INSERT INTO dbo.FunnelTemplates (Name, Description)
VALUES ('Venta Directa', 'Embudo Ã¡gil para ciclos cortos: primer contacto, cotizaciÃ³n y cierre en pocos pasos.');
SET @FunnelDirecto = SCOPE_IDENTITY();

INSERT INTO dbo.FunnelTemplateStages (TemplateId, StageName, StageOrder) VALUES
(@FunnelDirecto, 'Nuevo Lead',         1),
(@FunnelDirecto, 'Primer Contacto',    2),
(@FunnelDirecto, 'CotizaciÃ³n Enviada', 3),
(@FunnelDirecto, 'Seguimiento',        4),
(@FunnelDirecto, 'Cierre',             5);
GO

-- â”€â”€ 3. Sector Inmobiliario â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
--    Para ventas de bienes raÃ­ces con recorridos, documentaciÃ³n y escritura
DECLARE @FunnelInmobiliario INT;
INSERT INTO dbo.FunnelTemplates (Name, Description)
VALUES ('Sector Inmobiliario', 'Embudo para venta de propiedades: desde el primer contacto hasta la escrituraciÃ³n.');
SET @FunnelInmobiliario = SCOPE_IDENTITY();

INSERT INTO dbo.FunnelTemplateStages (TemplateId, StageName, StageOrder) VALUES
(@FunnelInmobiliario, 'Prospecto',          1),
(@FunnelInmobiliario, 'Primer Contacto',    2),
(@FunnelInmobiliario, 'Visita / Recorrido', 3),
(@FunnelInmobiliario, 'Interesado',         4),
(@FunnelInmobiliario, 'DocumentaciÃ³n',      5),
(@FunnelInmobiliario, 'Oferta Presentada',  6),
(@FunnelInmobiliario, 'Proceso Legal',      7),
(@FunnelInmobiliario, 'EscrituraciÃ³n',      8);
GO

PRINT '--- Â¡MIGRACIÃ“N DEL NÃšCLEO DEL CRM COMPLETADA! ---';

PRINT '--- Creando tablas de metadatos para reportes... ---';

-- Tabla para definir los tipos de grÃ¡ficos disponibles (barras, pastel, etc.)
CREATE TABLE dbo.ChartTypes (
    ChartTypeId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL, -- Ej: "GrÃ¡fica de Barras"
    Code NVARCHAR(50) NOT NULL UNIQUE -- Ej: "bar", "pie", "line"
);
GO

-- Tabla diccionario de todos los campos que se pueden usar en un reporte
CREATE TABLE dbo.ReportableFields (
    FieldId INT PRIMARY KEY IDENTITY(1,1),
    DisplayName NVARCHAR(255) NOT NULL, -- El nombre que ve el usuario, ej: "Monto del Trato"
    TechnicalName NVARCHAR(255) NOT NULL, -- El nombre tÃ©cnico que usa la API, ej: "Deals.FinalAmount"
    FieldType NVARCHAR(50) NOT NULL, -- 'Metrica' o 'Dimension'
    AggregationType NVARCHAR(50) NULL -- Solo para MÃ©tricas, ej: 'SUM', 'COUNT', 'AVG'
);
GO

PRINT '--- Creando tablas para Dashboards y Widgets... ---';

-- Tabla para los Dashboards o "Pizarrones" de cada usuario
CREATE TABLE dbo.Dashboards (
    DashboardId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(255) NOT NULL,
    OwnerUserId NVARCHAR(128) NOT NULL,
    CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ModifiedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_Dashboards_Users FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(Id)
);
GO

-- Tabla para cada reporte individual (un "Widget" o grÃ¡fica) dentro de un dashboard
CREATE TABLE dbo.Widgets (
    WidgetId INT PRIMARY KEY IDENTITY(1,1),
    DashboardId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL, -- Ej: "Ventas por Vendedor este Mes"
    
    -- La "receta" del reporte
    MetricFieldId INT NOT NULL,
    DimensionFieldId INT NOT NULL,
    ChartTypeId INT NOT NULL,
    
    Filters NVARCHAR(MAX) NULL, -- Para guardar filtros avanzados en formato JSON

    -- PosiciÃ³n y tamaÃ±o en el dashboard
    PositionX INT NOT NULL DEFAULT 0,
    PositionY INT NOT NULL DEFAULT 0,
    Width INT NOT NULL DEFAULT 6,
    Height INT NOT NULL DEFAULT 4,

    CONSTRAINT FK_Widgets_Dashboards FOREIGN KEY (DashboardId) REFERENCES dbo.Dashboards(DashboardId) ON DELETE CASCADE,
    CONSTRAINT FK_Widgets_MetricField FOREIGN KEY (MetricFieldId) REFERENCES dbo.ReportableFields(FieldId),
    CONSTRAINT FK_Widgets_DimensionField FOREIGN KEY (DimensionFieldId) REFERENCES dbo.ReportableFields(FieldId),
    CONSTRAINT FK_Widgets_ChartTypes FOREIGN KEY (ChartTypeId) REFERENCES dbo.ChartTypes(ChartTypeId)
);
GO

PRINT '--- Creando tablas para la funcionalidad de compartir reportes... ---';

-- Tabla para generar los enlaces Ãºnicos que se van a compartir
CREATE TABLE dbo.ReportShareLinks (
    ShareLinkId INT PRIMARY KEY IDENTITY(1,1),
    PublicId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- Un ID Ãºnico y seguro para la URL pÃºblica
    Description NVARCHAR(500) NULL, -- Para que el usuario sepa para quÃ© es este enlace
    OwnerUserId NVARCHAR(128) NOT NULL,
    CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ExpiresOn DATETIME2 NULL, -- Para que los enlaces puedan expirar
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_ReportShareLinks_Users FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(Id)
);
GO

-- Tabla de uniÃ³n que define quÃ© Widgets se incluyen en cada enlace compartido
CREATE TABLE dbo.SharedWidgets (
    ShareLinkId INT NOT NULL,
    WidgetId INT NOT NULL,
    
    CONSTRAINT PK_SharedWidgets PRIMARY KEY (ShareLinkId, WidgetId),
    CONSTRAINT FK_SharedWidgets_Links FOREIGN KEY (ShareLinkId) REFERENCES dbo.ReportShareLinks(ShareLinkId) ON DELETE CASCADE,
    CONSTRAINT FK_SharedWidgets_Widgets FOREIGN KEY (WidgetId) REFERENCES dbo.Widgets(WidgetId) ON DELETE CASCADE
);
GO

-- ####################################################################
-- ### MÃ“DULO: CATÃLOGO DE PRODUCTOS/SERVICIOS (ÃTEMS)
-- ####################################################################
PRINT '--- Creando el nuevo sistema de CatÃ¡logo de Ãtems... ---';

-- 1. Crear las nuevas tablas

-- La tabla para los "agrupadores" o catÃ¡logos, ligada a una Account
CREATE TABLE dbo.ItemCatalogs (
    CatalogId INT PRIMARY KEY IDENTITY(1,1),
    AccountId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    CONSTRAINT FK_ItemCatalogs_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId)
);
GO

-- La tabla para los productos/servicios individuales dentro de un catÃ¡logo
CREATE TABLE dbo.CatalogItems (
    ItemId INT PRIMARY KEY IDENTITY(1,1),
    CatalogId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Price DECIMAL(18, 2) NULL,
    Code NVARCHAR(100) NULL, -- CÃ³digo de producto/SKU
    CONSTRAINT FK_CatalogItems_Catalogs FOREIGN KEY (CatalogId) REFERENCES dbo.ItemCatalogs(CatalogId) ON DELETE CASCADE
);
GO

-- La tabla de uniÃ³n que conecta un Deal con los Ã­tems que se estÃ¡n vendiendo
CREATE TABLE dbo.DealItems (
    DealId INT NOT NULL,
    ItemId INT NOT NULL,
    Quantity INT NOT NULL DEFAULT 1,
    Price DECIMAL(18, 2) NOT NULL, -- El precio final acordado para este Ã­tem en este trato
    CONSTRAINT PK_DealItems PRIMARY KEY (DealId, ItemId),
    CONSTRAINT FK_DealItems_Deals FOREIGN KEY (DealId) REFERENCES dbo.Deals(DealId) ON DELETE CASCADE,
    CONSTRAINT FK_DealItems_Items FOREIGN KEY (ItemId) REFERENCES dbo.CatalogItems(ItemId)
);
GO

-- ####################################################################
-- ### MIGRACIÃ“N DE DATOS (de tus viejas tablas a las nuevas)
-- ####################################################################
PRINT '--- Migrando datos del viejo catÃ¡logo de productos... ---';

-- 1. Migrar los "paquetes" a ItemCatalogs
INSERT INTO dbo.ItemCatalogs (AccountId, Name)
SELECT a.AccountId, ldtp.Description
FROM dbo.LeadDealsTypesPackages ldtp
JOIN dbo.Accounts a ON ldtp.Id = a.LeadDealsTypesPackagesId; -- Asumiendo que esta FK existe en la nueva tabla Accounts
GO

-- 2. Migrar los "tipos" a CatalogItems
INSERT INTO dbo.CatalogItems (CatalogId, Name, Price, Code)
SELECT ic.CatalogId, ldt.Description, ldt.Price, ldt.Code
FROM dbo.LeadDealsTypes ldt
JOIN dbo.LeadDealsTypesPackages ldtp ON ldt.LeadDealsTypePackageId = ldtp.Id
JOIN dbo.ItemCatalogs ic ON ldtp.Description = ic.Name;
GO

-- ####################################################################
-- ### LIMPIEZA DE TABLAS OBSOLETAS
-- ### AÃ±ade este bloque a tu secciÃ³n de limpieza final.
-- ####################################################################
PRINT '--- Marcando para limpieza las tablas de catÃ¡logo obsoletas... ---';
/*
DROP TABLE dbo.DealsTypesDeals;
DROP TABLE dbo.LeadDealsTypes;
DROP TABLE dbo.LeadDealsTypesPackages;
*/

-- ####################################################################
-- ### MÃ“DULO: SISTEMA DE ETIQUETADO (TAGS)
-- ####################################################################
PRINT '--- Creando y migrando el nuevo sistema de etiquetado... ---';

-- 1. Evolucionar tu catÃ¡logo de etiquetas 'TagsLeads' a una tabla genÃ©rica 'Tags'
PRINT '--- Evolucionando la tabla de catÃ¡logo TagsLeads a Tags... ---';
EXEC sp_rename 'dbo.TagsLeads', 'Tags';
GO
EXEC sp_rename 'dbo.Tags.Id', 'TagId', 'COLUMN';
GO

-- 2. Crear la nueva tabla de uniÃ³n polimÃ³rfica 'Taggings'
-- Esta tabla reemplazarÃ¡ a 'LeadsTags' y permitirÃ¡ etiquetar cualquier entidad.
PRINT '--- Creando la nueva tabla polimÃ³rfica Taggings... ---';
CREATE TABLE dbo.Taggings (
    TagId INT NOT NULL,
    EntityId BIGINT NOT NULL, -- Puede ser un LeadId, DealId, ContactId, etc.
    EntityType NVARCHAR(50) NOT NULL, -- 'Lead', 'Deal', 'Contact'

    CONSTRAINT PK_Taggings PRIMARY KEY (TagId, EntityId, EntityType),
    CONSTRAINT FK_Taggings_Tags FOREIGN KEY (TagId) REFERENCES dbo.Tags(TagId) ON DELETE CASCADE
);
GO

-- 3. Migrar los datos existentes de 'LeadsTags' a la nueva tabla 'Taggings'
PRINT '--- Migrando las etiquetas existentes de los leads... ---';
INSERT INTO dbo.Taggings (TagId, EntityId, EntityType)
SELECT 
    lt.TagsLeadId,
    lt.LeadId,
    'Lead' -- Todos los registros existentes son de tipo 'Lead'
FROM dbo.LeadsTags lt;
GO

-- 4. Limpieza de la tabla obsoleta
-- AÃ±ade este bloque a tu secciÃ³n de limpieza final.
PRINT '--- Marcando para limpieza la tabla obsoleta LeadsTags... ---';
/*
DROP TABLE dbo.LeadsTags;
*/

BEGIN TRANSACTION;
GO

-- ####################################################################
-- ### MÃ“DULO 1: INTEGRACIÃ“N CON WHATSAPP
-- ####################################################################
PRINT '--- MÃ³dulo 1: Integrando WhatsApp... ---';

-- 1.1: AÃ±adir la bandera a la tabla Contacts para identificar contactos de WhatsApp
ALTER TABLE dbo.Contacts
ADD IsWhatsappContact BIT NOT NULL DEFAULT 0;
GO

-- 1.2: Migrar la informaciÃ³n, marcando los contactos existentes que son de WhatsApp
-- (Este script asume que se pueden cruzar por el nÃºmero de telÃ©fono)
UPDATE c
SET c.IsWhatsappContact = 1
FROM dbo.Contacts c
JOIN dbo.ContactsWhatsapp cwa ON c.PhoneNumber = cwa.PhoneNumber; -- Ajusta el JOIN si la relaciÃ³n es otra
GO

-- 1.3: Formalizar la relaciÃ³n de la tabla de mensajes con el directorio de contactos
ALTER TABLE dbo.MessagesWhatsapp
ADD CONSTRAINT FK_MessagesWhatsapp_Contacts FOREIGN KEY (ContactId) REFERENCES dbo.Contacts(ContactId);
GO

-- ####################################################################
-- ### MÃ“DULO 2: SISTEMA DE NOTIFICACIONES
-- ####################################################################
PRINT '--- MÃ³dulo 2: Evolucionando Notificaciones... ---';

-- 2.1: Hacer la tabla 'Notifications' polimÃ³rfica
ALTER TABLE dbo.Notifications
ADD EntityId BIGINT NULL,
    EntityType NVARCHAR(50) NULL;
GO

-- 2.2: Asegurar las llaves y relaciones
ALTER TABLE dbo.NotificationTypes
ADD CONSTRAINT PK_NotificationTypes PRIMARY KEY (Id);
GO
ALTER TABLE dbo.Notifications
ADD CONSTRAINT FK_Notifications_Types FOREIGN KEY (NotificationType) REFERENCES dbo.NotificationTypes(Id);
GO

-- ####################################################################
-- ### MÃ“DULO 3: HISTORIAL Y LOGS (AUDITORÃA)
-- ####################################################################
PRINT '--- MÃ³dulo 3: Unificando Historial de AuditorÃ­a... ---';

-- 3.1: Crear la nueva tabla polimÃ³rfica para auditorÃ­a de acciones de usuario
CREATE TABLE dbo.AuditLogs (
    LogId INT PRIMARY KEY IDENTITY(1,1),
    EntityId BIGINT NOT NULL,
    EntityType NVARCHAR(50) NOT NULL,
    AuthorUserId NVARCHAR(128) NULL,
    Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    EventType NVARCHAR(100) NOT NULL, -- 'FieldUpdate', 'StageChange', 'NoteAdded'
    Description NVARCHAR(MAX) NOT NULL,
    ChangeData NVARCHAR(MAX) NULL -- JSON con OldValue y NewValue
);
GO

-- 3.2: Migrar los datos de 'LeadLogs' y 'StageLeadLogs' a la nueva tabla
-- MigraciÃ³n de LeadLogs
INSERT INTO dbo.AuditLogs (EntityId, EntityType, AuthorUserId, Timestamp, EventType, Description)
SELECT LeadId, 'Lead', UserId, Date, 'LegacyLog', Description
FROM dbo.LeadLogs;
GO
-- MigraciÃ³n de StageLeadLogs
INSERT INTO dbo.AuditLogs (EntityId, EntityType, AuthorUserId, Timestamp, EventType, Description, ChangeData)
SELECT LeadId, 'Lead', UserId, Date, 'StageChange', 
       CONCAT('Cambio de etapa. TardÃ³ ', Hours), -- DescripciÃ³n genÃ©rica
       CONCAT('{"oldStageId":', LastStageId, ',"newStageId":', StageId, '}') -- Guardamos los detalles en JSON
FROM dbo.StageLeadLogs;
GO

-- ####################################################################
-- ### MÃ“DULO 4: ACTIVIDADES Y SUS PLANTILLAS
-- ####################################################################
PRINT '--- MÃ³dulo 4: Evolucionando Actividades y sus Plantillas... ---';

-- 4.1: Crear las nuevas tablas para las "GuÃ­as de Tareas"
CREATE TABLE dbo.ActivityPlaybooks (
    PlaybookId INT PRIMARY KEY IDENTITY(1,1),
    AccountId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    CONSTRAINT FK_ActivityPlaybooks_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId)
);
GO
CREATE TABLE dbo.PlaybookTasks (
    TaskId INT PRIMARY KEY IDENTITY(1,1),
    PlaybookId INT NOT NULL,
    TaskName NVARCHAR(1000) NOT NULL,
    "Order" INT NOT NULL,
    CONSTRAINT FK_PlaybookTasks_Playbooks FOREIGN KEY (PlaybookId) REFERENCES dbo.ActivityPlaybooks(PlaybookId) ON DELETE CASCADE
);
GO

-- 4.2: Migrar tus plantillas existentes
INSERT INTO dbo.ActivityPlaybooks (AccountId, Name)
SELECT a.AccountId, at.Name
FROM dbo.ActivitiesTemplates at
JOIN dbo.Accounts a ON at.Id = a.ActivitiesTemplateId; -- Asumiendo que la FK estÃ¡ en Accounts
GO
INSERT INTO dbo.PlaybookTasks (PlaybookId, TaskName, "Order")
SELECT ap.PlaybookId, atr.Title, ISNULL(atr.ParentId, 0) -- Usamos ParentId como el orden, ajusta si es necesario
FROM dbo.ActivitiesTemplatesRecords atr
JOIN dbo.ActivitiesTemplates at ON atr.ActivitiesTemplatesId = at.Id
JOIN dbo.ActivityPlaybooks ap ON at.Name = ap.Name;
GO

-- ####################################################################
-- ### MÃ“DULO 5: CONFIGURACIÃ“N DE TELEFONÃA
-- ####################################################################
PRINT '--- MÃ³dulo 5: Formalizando ConfiguraciÃ³n de TelefonÃ­a... ---';

ALTER TABLE dbo.Lines
ADD CONSTRAINT PK_Lines PRIMARY KEY (Id);
GO
ALTER TABLE dbo.UserLines
ADD CONSTRAINT FK_UserLines_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
GO
ALTER TABLE dbo.UserLines
ADD CONSTRAINT FK_UserLines_Lines FOREIGN KEY (LineId) REFERENCES dbo.Lines(Id);
GO

-- ####################################################################
-- ### MÃ“DULO 6: CATÃLOGOS FINALES
-- ####################################################################
PRINT '--- MÃ³dulo 6: Creando y Vinculando CatÃ¡logos Finales... ---';

-- 6.1: Crear y poblar el nuevo catÃ¡logo de ProspectSources
CREATE TABLE dbo.ProspectSources (
    SourceId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL
);
GO
INSERT INTO dbo.ProspectSources (Name) VALUES 
('Facebook'), ('Google'), ('LinkedIn'), ('Lead Booster'), ('Landing Page'), 
('Visitante'), ('WhatsApp'), ('Expo'), ('RecomendaciÃ³n'), ('Llamada'), 
('Inbox'), ('Instagram'), ('Correo'), ('Pagina Web'), ('Medio Tradicional'), 
('PanorÃ¡mico'), ('Broker'), ('Prospeccion');
GO

-- 6.2: Asegurar llaves primarias en catÃ¡logos existentes
ALTER TABLE dbo.Industries
ADD CONSTRAINT PK_Industries PRIMARY KEY (Id);
GO
ALTER TABLE dbo.ContactForms
ADD CONSTRAINT PK_ContactForms PRIMARY KEY (Id);
GO

COMMIT TRANSACTION;
GO


PRINT '--- AÃ±adiendo la columna ProspectSourceId a Leads_New y Deals... ---';

ALTER TABLE dbo.Leads
ADD ProspectSourceId INT NULL;
GO

ALTER TABLE dbo.Deals
ADD ProspectSourceId INT NULL;
GO

PRINT '--- Migrando los datos de texto de ProspectSource a los nuevos IDs... ---';

-- MigraciÃ³n para la tabla Deals
UPDATE d
SET d.ProspectSourceId = ps.SourceId
FROM dbo.Deals d
JOIN dbo.ProspectSources ps ON d.ProspectSource = ps.Name
WHERE d.ProspectSource IS NOT NULL;
GO

-- MigraciÃ³n para la tabla Leads
UPDATE ln
SET ln.ProspectSourceId = ps.SourceId
FROM dbo.Leads ln
JOIN dbo.ProspectSources ps ON ln.ProspectSource = ps.Name
WHERE ln.ProspectSource IS NOT NULL;
GO

PRINT '--- Limpiando columnas obsoletas y aÃ±adiendo llaves forÃ¡neas... ---';

-- 1. Eliminar las viejas columnas de texto
ALTER TABLE dbo.Deals
DROP COLUMN ProspectSource;
GO

ALTER TABLE dbo.Leads
DROP COLUMN ProspectSource;
GO

-- 2. AÃ±adir las llaves forÃ¡neas a las nuevas columnas de ID
ALTER TABLE dbo.Deals
ADD CONSTRAINT FK_Deals_ProspectSources FOREIGN KEY (ProspectSourceId) REFERENCES dbo.ProspectSources(SourceId);
GO

ALTER TABLE dbo.Leads
ADD CONSTRAINT FK_Leads_ProspectSources FOREIGN KEY (ProspectSourceId) REFERENCES dbo.ProspectSources(SourceId);
GO


-- BLOQUE 1: CREACIÃ“N DE COLUMNAS (Solo Estructura)
BEGIN TRANSACTION;

PRINT '--- 1. Agregando columnas faltantes... ---';

-- Columnas estÃ¡ndar de Identity
IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'NormalizedUserName' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD NormalizedUserName NVARCHAR(256) NULL;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'NormalizedEmail' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD NormalizedEmail NVARCHAR(256) NULL;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'EmailConfirmed' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD EmailConfirmed BIT NOT NULL DEFAULT 0;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'PasswordHash' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD PasswordHash NVARCHAR(MAX) NULL;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'SecurityStamp' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD SecurityStamp NVARCHAR(MAX) NULL;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'ConcurrencyStamp' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD ConcurrencyStamp NVARCHAR(MAX) NULL;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'PhoneNumberConfirmed' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD PhoneNumberConfirmed BIT NOT NULL DEFAULT 0;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'TwoFactorEnabled' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD TwoFactorEnabled BIT NOT NULL DEFAULT 0;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'LockoutEnd' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD LockoutEnd DATETIMEOFFSET NULL;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'LockoutEnabled' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD LockoutEnabled BIT NOT NULL DEFAULT 0;

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'AccessFailedCount' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD AccessFailedCount INT NOT NULL DEFAULT 0;

-- Columnas Personalizadas (CreatedOn, UserType, ParentId)
IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'CreatedOn' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE();

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'UserType' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD UserType NVARCHAR(50) NOT NULL DEFAULT 'Client';

IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'ParentId' AND Object_ID = Object_ID(N'dbo.Users'))
AND NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'parentId' AND Object_ID = Object_ID(N'dbo.Users'))
    ALTER TABLE dbo.Users ADD ParentId NVARCHAR(128) NULL;

COMMIT TRANSACTION;
GO 
-- ^^^ ESTE GO ES LA CLAVE. Obliga a que las columnas existan antes de seguir.


-- BLOQUE 2: ACTUALIZACIÃ“N DE DATOS
BEGIN TRANSACTION;

PRINT '--- 2. Poblando datos normalizados... ---';

-- Ahora sÃ­, como ya ejecutamos el bloque anterior, estas columnas existen.
UPDATE dbo.Users
SET 
    NormalizedUserName = UPPER(UserName),
    NormalizedEmail = UPPER(Email),
    SecurityStamp = NEWID() -- Generamos uno para evitar errores de seguridad en el login
WHERE NormalizedUserName IS NULL;

COMMIT TRANSACTION;
GO


-- BLOQUE 3: CREACIÃ“N DE ÃNDICES
BEGIN TRANSACTION;

PRINT '--- 3. Creando Ã­ndices de rendimiento... ---';

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UserNameIndex' AND object_id = OBJECT_ID('dbo.Users'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UserNameIndex] ON [dbo].[Users] ([NormalizedUserName] ASC) WHERE ([NormalizedUserName] IS NOT NULL);
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'EmailIndex' AND object_id = OBJECT_ID('dbo.Users'))
BEGIN
    CREATE INDEX [EmailIndex] ON [dbo].[Users] ([NormalizedEmail] ASC);
END

COMMIT TRANSACTION;
GO

PRINT '--- Â¡REPARACIÃ“N COMPLETADA! ---';


ALTER TABLE Features 
ADD [Type] NVARCHAR(20) NOT NULL DEFAULT 'Numeric'; 

-- 2. Actualizamos AddOns para que tengan un valor base y sepamos si se suma
ALTER TABLE AddOns 
ADD [Value] DECIMAL(18,2) NOT NULL DEFAULT 0,
    [IsAdditive] BIT NOT NULL DEFAULT 1; -- 1 = Suma al plan, 0 = Solo activa/reemplaza

-- 3. Aseguramos que la compra del cliente guarde la personalizaciÃ³n
-- Si ya existen estos campos, puedes saltar este paso
ALTER TABLE CustomerPurchasedAddOns
ADD [Quantity] INT NOT NULL DEFAULT 1,

    [PricePaid] DECIMAL(18,2) NOT NULL DEFAULT 0;

-- ============================================================
-- FASE 1 â€” CATÃLOGOS ADMIN GLOBAL + SCORING REDISEÃ‘ADO
-- Fecha: 2026-04-02
-- EJECUTAR ESTE BLOQUE COMPLETO EN Profet_new
-- ============================================================

-- ------------------------------------------------------------
-- 1. TABLA: AccountIndustries (junction Account <-> Industry)
--    (ya existe en la DB vieja, la recreamos limpia para el nuevo esquema)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountIndustries')
BEGIN
    CREATE TABLE [dbo].[AccountIndustries] (
        [Id]         BIGINT IDENTITY(1,1) NOT NULL,
        [AccountId]  INT    NOT NULL,
        [IndustryId] BIGINT NOT NULL,
        CONSTRAINT [PK_AccountIndustries] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AccountIndustries_Accounts]   FOREIGN KEY ([AccountId])  REFERENCES [dbo].[Accounts]([AccountId]),
        CONSTRAINT [FK_AccountIndustries_Industries] FOREIGN KEY ([IndustryId]) REFERENCES [dbo].[Industries]([Id])
    );
END
GO

-- ------------------------------------------------------------
-- 2. TABLA: LeadLostReasons (catÃ¡logo global â€” Admin lo pre-carga)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadLostReasons')
BEGIN
    CREATE TABLE [dbo].[LeadLostReasons] (
        [Id]             INT IDENTITY(1,1) NOT NULL,
        [Description]    NVARCHAR(200)     NOT NULL,
        [ConversionRate] BIT               NOT NULL DEFAULT 0,
        [IsActive]       BIT               NOT NULL DEFAULT 1,
        CONSTRAINT [PK_LeadLostReasons] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END
GO

-- ------------------------------------------------------------
-- 3. TABLA: AccountLeadLostReasons (quÃ© razones habilita cada Account)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountLeadLostReasons')
BEGIN
    CREATE TABLE [dbo].[AccountLeadLostReasons] (
        [AccountId]      INT NOT NULL,
        [LostReasonId]   INT NOT NULL,
        CONSTRAINT [PK_AccountLeadLostReasons] PRIMARY KEY CLUSTERED ([AccountId] ASC, [LostReasonId] ASC),
        CONSTRAINT [FK_AccLostReasons_Accounts]   FOREIGN KEY ([AccountId])    REFERENCES [dbo].[Accounts]([AccountId]),
        CONSTRAINT [FK_AccLostReasons_LostReasons] FOREIGN KEY ([LostReasonId]) REFERENCES [dbo].[LeadLostReasons]([Id])
    );
END
GO

-- ------------------------------------------------------------
-- 4. SCORING â€” Agregar campos a tablas existentes
-- ------------------------------------------------------------

-- ScoringTemplateQuestions: tipo de pregunta, obligatoriedad y orden
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringTemplateQuestions') AND name = 'QuestionType')
    ALTER TABLE [dbo].[ScoringTemplateQuestions] ADD [QuestionType] NVARCHAR(20) NOT NULL DEFAULT 'SingleChoice';
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringTemplateQuestions') AND name = 'IsRequired')
    ALTER TABLE [dbo].[ScoringTemplateQuestions] ADD [IsRequired] BIT NOT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringTemplateQuestions') AND name = 'OrderPosition')
    ALTER TABLE [dbo].[ScoringTemplateQuestions] ADD [OrderPosition] INT NOT NULL DEFAULT 0;
GO

-- ScoringTemplateAnswerOptions: puntos directos y orden
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringTemplateAnswerOptions') AND name = 'Points')
    ALTER TABLE [dbo].[ScoringTemplateAnswerOptions] ADD [Points] DECIMAL(10,2) NOT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringTemplateAnswerOptions') AND name = 'OrderPosition')
    ALTER TABLE [dbo].[ScoringTemplateAnswerOptions] ADD [OrderPosition] INT NOT NULL DEFAULT 0;
GO

-- ScoringTemplates: vÃ­nculo a industria
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringTemplates') AND name = 'IndustryId')
    ALTER TABLE [dbo].[ScoringTemplates] ADD [IndustryId] BIGINT NULL REFERENCES [dbo].[Industries]([Id]);
GO

-- ScoringQuestions: tipo, obligatoriedad y orden (instancia del account)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringQuestions') AND name = 'QuestionType')
    ALTER TABLE [dbo].[ScoringQuestions] ADD [QuestionType] NVARCHAR(20) NOT NULL DEFAULT 'SingleChoice';
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringQuestions') AND name = 'IsRequired')
    ALTER TABLE [dbo].[ScoringQuestions] ADD [IsRequired] BIT NOT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringQuestions') AND name = 'OrderPosition')
    ALTER TABLE [dbo].[ScoringQuestions] ADD [OrderPosition] INT NOT NULL DEFAULT 0;
GO

-- ScoringAnswerOptions: puntos directos y orden
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringAnswerOptions') AND name = 'Points')
    ALTER TABLE [dbo].[ScoringAnswerOptions] ADD [Points] DECIMAL(10,2) NOT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringAnswerOptions') AND name = 'OrderPosition')
    ALTER TABLE [dbo].[ScoringAnswerOptions] ADD [OrderPosition] INT NOT NULL DEFAULT 0;
GO

-- LeadTiers: convertirlos en umbrales por ScoringModel
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadTiers') AND name = 'ScoringModelId')
    ALTER TABLE [dbo].[LeadTiers] ADD [ScoringModelId] INT NULL REFERENCES [dbo].[ScoringModels]([ScoringModelId]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadTiers') AND name = 'MinScore')
    ALTER TABLE [dbo].[LeadTiers] ADD [MinScore] DECIMAL(10,2) NOT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadTiers') AND name = 'MaxScore')
    ALTER TABLE [dbo].[LeadTiers] ADD [MaxScore] DECIMAL(10,2) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadTiers') AND name = 'Color')
    ALTER TABLE [dbo].[LeadTiers] ADD [Color] NVARCHAR(50) NULL;
GO

-- LeadScoringAnswers: soporte para respuestas abiertas y puntos guardados
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadScoringAnswers') AND name = 'TextValue')
    ALTER TABLE [dbo].[LeadScoringAnswers] ADD [TextValue] NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadScoringAnswers') AND name = 'NumericValue')
    ALTER TABLE [dbo].[LeadScoringAnswers] ADD [NumericValue] DECIMAL(10,2) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadScoringAnswers') AND name = 'PointsAwarded')
    ALTER TABLE [dbo].[LeadScoringAnswers] ADD [PointsAwarded] DECIMAL(10,2) NOT NULL DEFAULT 0;
GO

-- ScoringRules: nombre descriptivo y puntos de bono directos
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringRules') AND name = 'Name')
    ALTER TABLE [dbo].[ScoringRules] ADD [Name] NVARCHAR(200) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringRules') AND name = 'BonusPoints')
    ALTER TABLE [dbo].[ScoringRules] ADD [BonusPoints] DECIMAL(10,2) NOT NULL DEFAULT 0;
GO

-- ------------------------------------------------------------
-- 5. TABLA: ScoringRuleConditions (condiciones compuestas AND/OR)
-- ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ScoringRuleConditions')
BEGIN
    CREATE TABLE [dbo].[ScoringRuleConditions] (
        [ConditionId]    INT IDENTITY(1,1) NOT NULL,
        [RuleId]         INT          NOT NULL,
        [QuestionId]     INT          NOT NULL,
        [AnswerOptionId] INT          NOT NULL,
        [LogicOperator]  NVARCHAR(5)  NOT NULL DEFAULT 'AND',  -- 'AND' | 'OR'
        CONSTRAINT [PK_ScoringRuleConditions] PRIMARY KEY CLUSTERED ([ConditionId] ASC),
        CONSTRAINT [FK_ScoringRuleCond_Rule]   FOREIGN KEY ([RuleId])         REFERENCES [dbo].[ScoringRules]([RuleId]),
        CONSTRAINT [FK_ScoringRuleCond_Ques]   FOREIGN KEY ([QuestionId])     REFERENCES [dbo].[ScoringQuestions]([QuestionId]),
        CONSTRAINT [FK_ScoringRuleCond_Ans]    FOREIGN KEY ([AnswerOptionId]) REFERENCES [dbo].[ScoringAnswerOptions]([AnswerOptionId])
    );
END
GO

-- ============================================================
-- FIN FASE 1 â€” CATÃLOGOS ADMIN GLOBAL + SCORING REDISEÃ‘ADO
-- ============================================================

-- ============================================================
-- SUSCRIPCIONES â€” OVERRIDES POR CLIENTE
-- Fecha: 2026-04-02
-- Permite negociar lÃ­mites de features distintos al plan base
-- EJECUTAR EN Profet_new
-- ============================================================

-- CustomerPurchasedAddOns: Quantity ya fue agregado en bloque anterior.
-- Solo verificar que PricePaid exista (por si se ejecuta en DB limpia).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CustomerPurchasedAddOns') AND name = 'Quantity')
    ALTER TABLE [dbo].[CustomerPurchasedAddOns] ADD [Quantity] INT NOT NULL DEFAULT 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CustomerPurchasedAddOns') AND name = 'PricePaid')
    ALTER TABLE [dbo].[CustomerPurchasedAddOns] ADD [PricePaid] DECIMAL(18,2) NOT NULL DEFAULT 0;
GO

-- Nueva tabla: lÃ­mites de features negociados por cliente
-- Tiene prioridad sobre PlanFeatures al calcular lÃ­mites reales del tenant
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SubscriptionFeatureOverrides')
BEGIN
    CREATE TABLE [dbo].[SubscriptionFeatureOverrides] (
        [SubscriptionId] INT           NOT NULL,
        [FeatureId]      INT           NOT NULL,
        [CustomLimit]    NVARCHAR(50)  NOT NULL,
        CONSTRAINT [PK_SubFeatureOverrides] PRIMARY KEY CLUSTERED ([SubscriptionId] ASC, [FeatureId] ASC),
        CONSTRAINT [FK_SubFeatOverride_Sub]     FOREIGN KEY ([SubscriptionId]) REFERENCES [dbo].[Subscriptions]([SubscriptionId]),
        CONSTRAINT [FK_SubFeatOverride_Feature] FOREIGN KEY ([FeatureId])      REFERENCES [dbo].[Features]([FeatureId])
    );
END
GO

-- ============================================================
-- FIN SUSCRIPCIONES â€” OVERRIDES
-- ============================================================

-- ============================================================
-- CustomerPurchasedAddOns â€” agregar Quantity
-- ============================================================
ALTER TABLE CustomerPurchasedAddOns ADD Quantity INT NOT NULL DEFAULT 1;
GO

-- ============================================================
-- LeadLostReasons â€” agregar IsActive (si la tabla fue creada sin esta columna)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeadLostReasons') AND name = 'IsActive')
BEGIN
    ALTER TABLE LeadLostReasons ADD IsActive BIT NOT NULL DEFAULT 1;
END
GO

-- ============================================================
-- PlanPriceHistory â€” agregar CreatedAt
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PlanPriceHistory') AND name = 'CreatedAt')
BEGIN
    ALTER TABLE PlanPriceHistory ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE();
END
GO

-- ============================================================
-- Funnels â€” agregar OriginatingTemplateId (FK a FunnelTemplates)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Funnels') AND name = 'OriginatingTemplateId')
BEGIN
    ALTER TABLE Funnels ADD OriginatingTemplateId INT NULL;
    ALTER TABLE Funnels ADD CONSTRAINT FK_Funnels_FunnelTemplates
        FOREIGN KEY (OriginatingTemplateId) REFERENCES FunnelTemplates(TemplateId);
END
GO

-- ============================================================
-- SEED â€” Motivos de PÃ©rdida (catÃ¡logo global)
-- Solo inserta si la tabla estÃ¡ vacÃ­a para no duplicar en re-ejecuciones
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM LeadLostReasons)
BEGIN
    INSERT INTO LeadLostReasons (Description, ConversionRate, IsActive) VALUES
    ('Precio muy alto',                     1, 1),
    ('EligiÃ³ a la competencia',             1, 1),
    ('Sin presupuesto en este momento',     1, 1),
    ('No hay necesidad inmediata',          0, 1),
    ('Sin respuesta / No contactable',      0, 1),
    ('Proyecto cancelado o pausado',        0, 1),
    ('No cumple requerimientos tÃ©cnicos',   0, 1),
    ('Tiempo de decisiÃ³n muy largo',        0, 1),
    ('Cambio de prioridades internas',      0, 1),
    ('Mala experiencia con el proceso',     1, 1);
END
GO

-- ============================================================
-- Customers â€” agregar columnas White-Label / Branding
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Customers') AND name = 'BrandName')
    ALTER TABLE Customers ADD BrandName NVARCHAR(100) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Customers') AND name = 'BrandLogoUrl')
    ALTER TABLE Customers ADD BrandLogoUrl NVARCHAR(500) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Customers') AND name = 'BrandPrimaryColor')
    ALTER TABLE Customers ADD BrandPrimaryColor VARCHAR(20) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Customers') AND name = 'BrandSecondaryColor')
    ALTER TABLE Customers ADD BrandSecondaryColor VARCHAR(20) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Customers') AND name = 'BrandFaviconUrl')
    ALTER TABLE Customers ADD BrandFaviconUrl NVARCHAR(500) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Customers') AND name = 'BrandLogoSmallUrl')
    ALTER TABLE Customers ADD BrandLogoSmallUrl NVARCHAR(500) NULL;
GO

-- ============================================================
-- GlobalBranding â€” tabla de marca global de la plataforma (1 sola fila)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('GlobalBranding') AND type = 'U')
BEGIN
    CREATE TABLE GlobalBranding (
        Id              INT          NOT NULL DEFAULT 1,
        AppName         NVARCHAR(100) NULL,
        LogoLargeUrl    NVARCHAR(500) NULL,
        LogoSmallUrl    NVARCHAR(500) NULL,
        PrimaryColor    VARCHAR(20)  NULL,
        SecondaryColor  VARCHAR(20)  NULL,
        FaviconUrl      NVARCHAR(500) NULL,
        CONSTRAINT PK_GlobalBranding PRIMARY KEY (Id),
        CONSTRAINT CK_GlobalBranding_SingleRow CHECK (Id = 1)
    );
    -- Insertar la fila por defecto
    INSERT INTO GlobalBranding (Id) VALUES (1);
END
GO

-- ============================================================
-- Teams.LeaderId â€” lÃ­der del equipo (Manager que supervisa a los miembros)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Teams') AND name = 'LeaderId')
BEGIN
    ALTER TABLE Teams ADD LeaderId NVARCHAR(450) NULL;
    ALTER TABLE Teams ADD CONSTRAINT FK_Teams_Leader
        FOREIGN KEY (LeaderId) REFERENCES Users(Id)
        ON DELETE SET NULL;
END
GO

-- ============================================================
-- MIGRACIÃ“N DE VARIABLES (ejecutar una sola vez)
-- ============================================================
-- PASO 1: dbo.Variables â†’ CustomFieldDefinitions
-- Mueve el pool global de campos al nuevo esquema.
-- Usa GROUP BY para evitar duplicados si dbo.Variables tiene filas repetidas.
-- ============================================================
INSERT INTO dbo.CustomFieldDefinitions (FieldCode, FieldName, FieldType)
SELECT
    v.Value             AS FieldCode,
    MAX(v.Description)  AS FieldName,
    'text'              AS FieldType
FROM dbo.Variables v
WHERE v.Value IS NOT NULL
  AND v.Description IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.CustomFieldDefinitions cfd
      WHERE cfd.FieldCode = v.Value
  )
GROUP BY v.Value;
GO

-- ============================================================
-- PASO 2: Campaigns.Variables (CSV) â†’ AccountCustomFields
-- La columna Variables quedÃ³ en dbo.Campaigns (la tabla vieja).
-- El CSV usa tokens sin prefijo (ej: "name","email","phone").
-- CustomFieldDefinitions.FieldCode usa prefijo "t_" (ej: "t_name","t_email").
-- Mapeo: token = FieldCode sin "t_"  â†’  busca "t_" + token en CustomFieldDefinitions.
-- Tokens del sistema (id, leadDate, leadScore, status...) no tienen match â†’ se ignoran.
-- La relaciÃ³n con Accounts se hace por Campaigns.Id = Accounts.AccountId (mismos IDs).
-- ============================================================
INSERT INTO dbo.AccountCustomFields (AccountId, FieldId, IsVisibleOnCard)
SELECT DISTINCT
    a.AccountId,
    cfd.FieldId,
    0 AS IsVisibleOnCard
FROM dbo.Campaigns c
INNER JOIN dbo.Accounts a ON a.AccountId = c.Id
CROSS APPLY STRING_SPLIT(c.Variables, ',') AS tokens
INNER JOIN dbo.CustomFieldDefinitions cfd
    ON cfd.FieldCode = 't_' + LTRIM(RTRIM(tokens.value))
WHERE c.Variables IS NOT NULL
  AND c.Variables != ''
  AND NOT EXISTS (
      SELECT 1 FROM dbo.AccountCustomFields acf
      WHERE acf.AccountId = a.AccountId AND acf.FieldId = cfd.FieldId
  );
GO

-- ============================================================
-- CustomFieldDefinitions: columna Options para campos tipo lista
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CustomFieldDefinitions') AND name = 'Options')
    ALTER TABLE dbo.CustomFieldDefinitions ADD Options NVARCHAR(MAX) NULL;
GO

-- ============================================================
-- TRADUCCIONES: CustomFieldDefinitions â†’ nombres en espaÃ±ol
-- Ejecutar una sola vez. Idempotente (UPDATE por FieldCode).
-- ============================================================
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Nombre'                WHERE FieldCode = 't_name';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Correo electrÃ³nico'    WHERE FieldCode = 't_email';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'TelÃ©fono'              WHERE FieldCode = 't_phone';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Empresa'               WHERE FieldCode = 't_company';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Calle y nÃºmero'        WHERE FieldCode = 't_streetAndNumber';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Colonia'               WHERE FieldCode = 't_colony';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Ciudad'                WHERE FieldCode = 't_city';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Estado'                WHERE FieldCode = 't_state';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'CÃ³digo postal'         WHERE FieldCode = 't_cp';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Cargo / Puesto'        WHERE FieldCode = 't_jobTitle';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Edad'                  WHERE FieldCode = 't_age';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'GÃ©nero'                WHERE FieldCode = 't_genre';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Mensaje enviado'       WHERE FieldCode = 't_messageSent';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Comentarios'           WHERE FieldCode = 't_comments';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Fuente del prospecto'  WHERE FieldCode = 't_prospectSource';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Formulario de contacto' WHERE FieldCode = 't_contactForm';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Fecha de nacimiento'   WHERE FieldCode = 't_birthday';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Estado civil'          WHERE FieldCode = 't_civilStatus';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'NSE'                   WHERE FieldCode = 't_nse';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Tipo de empresa'       WHERE FieldCode = 't_companyType';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Sector / Industria'    WHERE FieldCode = 't_industrySector';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'NÃºmero de empleados'   WHERE FieldCode = 't_employees';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'InterÃ©s'               WHERE FieldCode = 't_interest';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Preferencias'          WHERE FieldCode = 't_preferences';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Actividades'           WHERE FieldCode = 't_activities';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'PrÃ¡ctica deportiva'    WHERE FieldCode = 't_practiceSports';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'HÃ¡bitos'               WHERE FieldCode = 't_habits';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Valores'               WHERE FieldCode = 't_values';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Creencias'             WHERE FieldCode = 't_beliefs';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Presupuesto'           WHERE FieldCode = 't_budget';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'DecisiÃ³n de compra'    WHERE FieldCode = 't_buyingDecision';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Grado de necesidad'    WHERE FieldCode = 't_needDegree';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Tiempo de compra'      WHERE FieldCode = 't_buyingTime';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Calidad del prospecto' WHERE FieldCode = 't_qualityScore';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Nivel de engagement'   WHERE FieldCode = 't_engagement';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Contacto adicional'    WHERE FieldCode = 't_contact';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'PosiciÃ³n'              WHERE FieldCode = 't_position';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'TamaÃ±o de empresa'     WHERE FieldCode = 't_size';
GO

-- ============================================================
-- TRADUCCIONES: campos restantes (segunda tanda)
-- ============================================================
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Activo'                    WHERE FieldCode = 't_active';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Nombre del anuncio'        WHERE FieldCode = 't_adName';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Asesor'                    WHERE FieldCode = 't_adviser';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Creencias'                 WHERE FieldCode = 't_believes';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'FacturaciÃ³n'               WHERE FieldCode = 't_billing';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Sucursal'                  WHERE FieldCode = 't_branch';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Marca'                     WHERE FieldCode = 't_brand';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Actividad empresarial'     WHERE FieldCode = 't_businessActivity';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'RazÃ³n social'              WHERE FieldCode = 't_businessName';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Capacidad'                 WHERE FieldCode = 't_capacity';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Nombre del contacto'       WHERE FieldCode = 't_contactName';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'CrÃ©dito'                   WHERE FieldCode = 't_credit';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'BurÃ³ de crÃ©dito'           WHERE FieldCode = 't_creditBureau';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Cliente'                   WHERE FieldCode = 't_customer';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Fecha'                     WHERE FieldCode = 't_date';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'DiagnÃ³stico'               WHERE FieldCode = 't_diagnosis';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Emociones'                 WHERE FieldCode = 't_emotions';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Experiencia'               WHERE FieldCode = 't_experience';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Frecuencia'                WHERE FieldCode = 't_frequency';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Sector / Industria'        WHERE FieldCode = 't_indistrySector';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Intereses'                 WHERE FieldCode = 't_interests';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Interesado en'             WHERE FieldCode = 't_interestedIn';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Tipo'                      WHERE FieldCode = 't_kind';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Nivel de estudios'         WHERE FieldCode = 't_levelOfStudy';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'UbicaciÃ³n'                 WHERE FieldCode = 't_location';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Modelo'                    WHERE FieldCode = 't_model';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Valor de la oportunidad'   WHERE FieldCode = 't_opportunityValue';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Mascotas'                  WHERE FieldCode = 't_pets';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Producto'                  WHERE FieldCode = 't_product';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Empresa generadora'        WHERE FieldCode = 't_prospectGeneratorCompany';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Cantidad'                  WHERE FieldCode = 't_quantity';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Rango'                     WHERE FieldCode = 't_rank';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Servicio'                  WHERE FieldCode = 't_service';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'CURP / NSS'                WHERE FieldCode = 't_socialSecurityNumber';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Tiempo'                    WHERE FieldCode = 't_time';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Unidad'                    WHERE FieldCode = 't_unit';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'VersiÃ³n'                   WHERE FieldCode = 't_version';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Volumen'                   WHERE FieldCode = 't_volume';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'GarantÃ­a'                  WHERE FieldCode = 't_warranty';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'AÃ±o'                       WHERE FieldCode = 't_year';
UPDATE dbo.CustomFieldDefinitions SET FieldName = 'Zona'                      WHERE FieldCode = 't_zone';
GO

-- ============================================================
-- IsSystem: columna + marcar campos internos del CRM
-- No aparecen en el wizard (el usuario no los configura).
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CustomFieldDefinitions') AND name = 'IsSystem')
    ALTER TABLE dbo.CustomFieldDefinitions ADD IsSystem BIT NOT NULL DEFAULT 0;
GO

UPDATE dbo.CustomFieldDefinitions SET IsSystem = 1 WHERE FieldCode IN (
    't_id',               -- ID interno
    't_dealId',           -- ID del trato
    't_stageId',          -- ID de etapa
    't_stageName',        -- Nombre de etapa (calculado)
    't_leadDate',         -- Fecha del lead (auto)
    't_leadExportDate',   -- Fecha de exportaciÃ³n (auto)
    't_leadScore',        -- Puntaje (calculado)
    't_leadLostReasonsId',-- Motivo de pÃ©rdida (manejado aparte)
    't_stateLead',        -- Estado del lead (calculado)
    't_stateLeadDate',    -- Fecha de estado (auto)
    't_dateStage',        -- Fecha de etapa (auto)
    't_userId',           -- Responsable (asignaciÃ³n)
    't_userName',         -- Nombre de usuario (calculado)
    't_outbound',         -- Tipo de lead (sistema)
    't_active'            -- Activo/inactivo (sistema)
);
GO

-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
-- ScoringRuleConditions â€” nuevas columnas para condiciones extendidas
-- (variables, tiempo de respuesta, fuente de prospecto)
-- â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringRuleConditions') AND name = 'ConditionType')
    ALTER TABLE dbo.ScoringRuleConditions ADD ConditionType NVARCHAR(50) NOT NULL DEFAULT 'answer';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringRuleConditions') AND name = 'FieldId')
    ALTER TABLE dbo.ScoringRuleConditions ADD FieldId INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringRuleConditions') AND name = 'ConditionValue')
    ALTER TABLE dbo.ScoringRuleConditions ADD ConditionValue NVARCHAR(500) NULL;
GO

-- QuestionId y AnswerOptionId ahora son nullable (solo aplican a type='answer')
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringRuleConditions') AND name = 'QuestionId'
           AND is_nullable = 0)
BEGIN
    ALTER TABLE dbo.ScoringRuleConditions ALTER COLUMN QuestionId INT NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ScoringRuleConditions') AND name = 'AnswerOptionId'
           AND is_nullable = 0)
BEGIN
    ALTER TABLE dbo.ScoringRuleConditions ALTER COLUMN AnswerOptionId INT NULL;
END
GO

-- ============================================================
-- REDISEÃ‘O LeadLostReasons â€” ejecutar desde aquÃ­ hacia abajo
--
-- Estado actual de la BD:
--   LeadLostReasonsPackages (Id, CustomerId, Description) â€” un paquete por customer
--   LeadLostReasons         (Id, LeadLostReasonsPackagesId, Description, ConversionRate, IsActive)
--   Accounts.LeadLostReasonsPackagesId â†’ LeadLostReasonsPackages.Id
--
-- Resultado:
--   LeadLostReasonTemplates â€” descripciones Ãºnicas del sistema viejo (el admin puede editar despuÃ©s)
--   LeadLostReasons (nueva) â€” por cuenta, ligadas a su template de origen
-- ============================================================

-- PASO 1: Renombrar tabla vieja para liberar el nombre
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadLostReasons')
   AND NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '_OldLeadLostReasons')
BEGIN
    EXEC sp_rename 'dbo.LeadLostReasons', '_OldLeadLostReasons';
    PRINT 'OK: LeadLostReasons â†’ _OldLeadLostReasons';
END
GO

-- Eliminar AccountLeadLostReasons si existe (ya no se necesita)
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountLeadLostReasons')
BEGIN
    DECLARE @fk NVARCHAR(MAX) = '';
    SELECT @fk += 'ALTER TABLE dbo.AccountLeadLostReasons DROP CONSTRAINT ' + name + '; '
    FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('AccountLeadLostReasons');
    IF @fk <> '' EXEC sp_executesql @fk;
    DROP TABLE dbo.AccountLeadLostReasons;
    PRINT 'OK: AccountLeadLostReasons eliminada';
END
GO

-- PASO 2: Crear LeadLostReasonTemplates (sugerencias que el admin gestiona)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadLostReasonTemplates')
BEGIN
    CREATE TABLE [dbo].[LeadLostReasonTemplates] (
        [TemplateId]      INT IDENTITY(1,1) NOT NULL,
        [Description]     NVARCHAR(200)     NOT NULL,
        [CountsForCharts] BIT               NOT NULL DEFAULT 1,
        [IsActive]        BIT               NOT NULL DEFAULT 1,
        CONSTRAINT [PK_LeadLostReasonTemplates] PRIMARY KEY CLUSTERED ([TemplateId] ASC)
    );
    PRINT 'OK: LeadLostReasonTemplates creada';
END
GO

-- PASO 3: Crear nueva LeadLostReasons por cuenta
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LeadLostReasons')
BEGIN
    CREATE TABLE [dbo].[LeadLostReasons] (
        [LostReasonId]    INT IDENTITY(1,1) NOT NULL,
        [AccountId]       INT               NOT NULL,
        [Description]     NVARCHAR(200)     NOT NULL,
        [CountsForCharts] BIT               NOT NULL DEFAULT 1,
        [IsActive]        BIT               NOT NULL DEFAULT 1,
        CONSTRAINT [PK_LeadLostReasons]          PRIMARY KEY CLUSTERED ([LostReasonId] ASC),
        CONSTRAINT [FK_LeadLostReasons_Accounts] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts]([AccountId])
    );
    PRINT 'OK: Nueva LeadLostReasons creada';
END
GO

-- PASO 4: Insertar templates (sugerencias globales)
INSERT INTO dbo.LeadLostReasonTemplates (Description, CountsForCharts, IsActive)
SELECT Description, CountsForCharts, IsActive FROM (VALUES
    ('Fuera de presupuesto', 1, 1),
    ('No estÃ¡ interesado',   0, 1),
    ('Datos incorrectos',    0, 1),
    ('No se localizÃ³',       0, 1),
    ('SPAM',                 0, 1)
) v (Description, CountsForCharts, IsActive)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.LeadLostReasonTemplates t WHERE t.Description = v.Description
);
GO

-- PASO 5: Migrar razones a CADA account del customer
-- Un paquete es por customer â†’ todas las accounts de ese customer heredan las razones
INSERT INTO dbo.LeadLostReasons (AccountId, Description, CountsForCharts, IsActive)
SELECT
    a.AccountId,
    old.Description,
    old.ConversionRate,
    old.IsActive
FROM dbo.Accounts a
JOIN dbo.LeadLostReasonsPackages pkg ON pkg.CustomerId = a.CustomerId
JOIN dbo._OldLeadLostReasons     old ON old.LeadLostReasonsPackagesId = pkg.Id
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.LeadLostReasons lr
    WHERE lr.AccountId = a.AccountId AND lr.Description = old.Description
);
PRINT CONCAT('OK: Razones migradas: ', @@ROWCOUNT);
GO

-- VERIFICACIÃ“N
SELECT a.AccountId, a.Name, lr.Description, lr.CountsForCharts
FROM dbo.LeadLostReasons lr
JOIN dbo.Accounts a ON a.AccountId = lr.AccountId
ORDER BY a.AccountId;
GO

-- ============================================================
-- LIMPIEZA â€” ejecutar solo cuando todo se vea bien
-- ============================================================

-- 1. Quitar FK de Accounts â†’ LeadLostReasonsPackages (si existe)
DECLARE @fkAccounts NVARCHAR(MAX) = '';
SELECT @fkAccounts += 'ALTER TABLE dbo.Accounts DROP CONSTRAINT ' + name + '; '
FROM sys.foreign_keys
WHERE parent_object_id = OBJECT_ID('dbo.Accounts')
  AND referenced_object_id = OBJECT_ID('dbo.LeadLostReasonsPackages');
IF @fkAccounts <> '' EXEC sp_executesql @fkAccounts;

-- 2. Quitar la columna del modelo
-- ALTER TABLE dbo.Accounts DROP COLUMN LeadLostReasonsPackagesId;

-- 3. Borrar tablas viejas
-- DROP TABLE dbo._OldLeadLostReasons;
-- DROP TABLE dbo.LeadLostReasonsPackages;
GO

-- ============================================================
-- FUENTES DE PROSPECTOS â€” ejecutar desde aquÃ­ hacia abajo
-- ============================================================

-- Agregar TikTok que no estaba en el seed original
INSERT INTO dbo.ProspectSources (Name)
SELECT 'TikTok' WHERE NOT EXISTS (SELECT 1 FROM dbo.ProspectSources WHERE Name = 'TikTok');
GO

-- Crear tabla AccountProspectSources (fuentes por cuenta)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AccountProspectSources')
BEGIN
    CREATE TABLE [dbo].[AccountProspectSources] (
        [Id]        INT IDENTITY(1,1) NOT NULL,
        [AccountId] INT               NOT NULL,
        [SourceId]  INT               NOT NULL,
        CONSTRAINT [PK_AccountProspectSources]          PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AccountProspectSources_Accounts] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts]([AccountId]),
        CONSTRAINT [FK_AccountProspectSources_Sources]  FOREIGN KEY ([SourceId])  REFERENCES [dbo].[ProspectSources]([SourceId])
    );
    PRINT 'OK: AccountProspectSources creada';
END
GO

-- Poblar todas las cuentas con todas las fuentes
INSERT INTO dbo.AccountProspectSources (AccountId, SourceId)
SELECT a.AccountId, s.SourceId
FROM dbo.Accounts a
CROSS JOIN dbo.ProspectSources s
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.AccountProspectSources aps
    WHERE aps.AccountId = a.AccountId AND aps.SourceId = s.SourceId
);
PRINT CONCAT('OK: Fuentes cargadas: ', @@ROWCOUNT);
GO

-- ============================================================
-- FUNNELS Y STAGES PARA CUENTAS EXISTENTES
-- ============================================================

-- PASO 1: Agregar columna 'color' a Stages si no existe
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Stages') AND name = 'color')
BEGIN
    ALTER TABLE dbo.Stages ADD color NVARCHAR(20) NULL;
    PRINT 'OK: Columna color agregada a Stages';
END
GO

-- PASO 2: Tabla temporal con la paleta de colores del CRM
--         (gradiente teal oscuro segÃºn nÃºmero total de etapas y posiciÃ³n)
CREATE TABLE #StageColors (
    TotalStages INT NOT NULL,
    Position    INT NOT NULL,
    Color       NVARCHAR(20) NOT NULL,
    PRIMARY KEY (TotalStages, Position)
);

INSERT INTO #StageColors (TotalStages, Position, Color) VALUES
-- 1 etapa
(1,1,'#1eb099'),
-- 2 etapas
(2,1,'#1eb099'),(2,2,'#0f594d'),
-- 3 etapas
(3,1,'#1eb099'),(3,2,'#147565'),(3,3,'#0a3b33'),
-- 4 etapas
(4,1,'#1eb099'),(4,2,'#168472'),(4,3,'#0f584d'),(4,4,'#072c26'),
-- 5 etapas
(5,1,'#1eb099'),(5,2,'#188c79'),(5,3,'#12695b'),(5,4,'#0c453c'),(5,5,'#06211d'),
-- 6 etapas
(6,1,'#1eb099'),(6,2,'#19937f'),(6,3,'#147566'),(6,4,'#0f584d'),(6,5,'#0a3b33'),(6,6,'#051d19'),
-- 7 etapas
(7,1,'#1eb099'),(7,2,'#1a9985'),(7,3,'#16806f'),(7,4,'#116659'),(7,5,'#0d4d43'),(7,6,'#09332c'),(7,7,'#041a16'),
-- 8 etapas
(8,1,'#1eb099'),(8,2,'#1a9a85'),(8,3,'#178474'),(8,4,'#126e60'),(8,5,'#0f584d'),(8,6,'#0c4239'),(8,7,'#072c25'),(8,8,'#041614'),
-- 9 etapas
(9,1,'#1eb099'),(9,2,'#1b9d88'),(9,3,'#188978'),(9,4,'#137667'),(9,5,'#116255'),(9,6,'#0e4f44'),(9,7,'#0a3c32'),(9,8,'#072823'),(9,9,'#041512'),
-- 10 etapas
(10,1,'#1eb099'),(10,2,'#1b9e89'),(10,3,'#188c7a'),(10,4,'#147a6b'),(10,5,'#12695b'),(10,6,'#0f574b'),(10,7,'#0b453a'),(10,8,'#09332c'),(10,9,'#06211d'),(10,10,'#031210'),
-- 11 etapas
(11,1,'#1eb099'),(11,2,'#1da389'),(11,3,'#1d937d'),(11,4,'#1a8470'),(11,5,'#177563'),(11,6,'#146656'),(11,7,'#12594b'),(11,8,'#0f453b'),(11,9,'#0b362d'),(11,10,'#082620'),(11,11,'#041412'),
-- 12 etapas
(12,1,'#1eb099'),(12,2,'#1da389'),(12,3,'#1d937d'),(12,4,'#1a8470'),(12,5,'#177563'),(12,6,'#146656'),(12,7,'#12594b'),(12,8,'#0f493e'),(12,9,'#0c3a31'),(12,10,'#092b24'),(12,11,'#061c17'),(12,12,'#030c0a'),
-- 13 etapas
(13,1,'#22bfa1'),(13,2,'#1eb099'),(13,3,'#1da389'),(13,4,'#1d937d'),(13,5,'#1a8470'),(13,6,'#177563'),(13,7,'#146656'),(13,8,'#12594b'),(13,9,'#0f493e'),(13,10,'#0c3a31'),(13,11,'#092b24'),(13,12,'#061c17'),(13,13,'#030c0a'),
-- 14 etapas
(14,1,'#27ccac'),(14,2,'#22bfa1'),(14,3,'#1eb099'),(14,4,'#1da389'),(14,5,'#1d937d'),(14,6,'#1a8470'),(14,7,'#177563'),(14,8,'#146656'),(14,9,'#12594b'),(14,10,'#0f493e'),(14,11,'#0c3a31'),(14,12,'#092b24'),(14,13,'#061c17'),(14,14,'#030c0a'),
-- 15 etapas
(15,1,'#2bd8b7'),(15,2,'#27ccac'),(15,3,'#22bfa1'),(15,4,'#1eb099'),(15,5,'#1da389'),(15,6,'#1d937d'),(15,7,'#1a8470'),(15,8,'#177563'),(15,9,'#146656'),(15,10,'#12594b'),(15,11,'#0f493e'),(15,12,'#0c3a31'),(15,13,'#092b24'),(15,14,'#061c17'),(15,15,'#030c0a'),
-- 16 etapas
(16,1,'#2ee5c2'),(16,2,'#2bd8b7'),(16,3,'#27ccac'),(16,4,'#22bfa1'),(16,5,'#1eb099'),(16,6,'#1da389'),(16,7,'#1d937d'),(16,8,'#1a8470'),(16,9,'#177563'),(16,10,'#146656'),(16,11,'#12594b'),(16,12,'#0f493e'),(16,13,'#0c3a31'),(16,14,'#092b24'),(16,15,'#061c17'),(16,16,'#030c0a'),
-- 17 etapas
(17,1,'#33f2cd'),(17,2,'#2ee5c2'),(17,3,'#2bd8b7'),(17,4,'#27ccac'),(17,5,'#22bfa1'),(17,6,'#1eb099'),(17,7,'#1da389'),(17,8,'#1d937d'),(17,9,'#1a8470'),(17,10,'#177563'),(17,11,'#146656'),(17,12,'#12594b'),(17,13,'#0f493e'),(17,14,'#0c3a31'),(17,15,'#092b24'),(17,16,'#061c17'),(17,17,'#030c0a'),
-- 18 etapas
(18,1,'#39ffd9'),(18,2,'#33f2cd'),(18,3,'#2ee5c2'),(18,4,'#2bd8b7'),(18,5,'#27ccac'),(18,6,'#22bfa1'),(18,7,'#1eb099'),(18,8,'#1da389'),(18,9,'#1d937d'),(18,10,'#1a8470'),(18,11,'#177563'),(18,12,'#146656'),(18,13,'#12594b'),(18,14,'#0f493e'),(18,15,'#0c3a31'),(18,16,'#092b24'),(18,17,'#061c17'),(18,18,'#030c0a');
GO

-- PASO 3: Para cada Account sin Funnel â†’ crear Pipeline Principal con 5 etapas
DECLARE @AccId INT, @FunnelId INT;

DECLARE cur CURSOR FOR
    SELECT a.AccountId
    FROM dbo.Accounts a
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Funnels f WHERE f.AccountId = a.AccountId);

OPEN cur;
FETCH NEXT FROM cur INTO @AccId;

WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO dbo.Funnels (Title, AccountId)
    VALUES ('Pipeline Principal', @AccId);
    SET @FunnelId = SCOPE_IDENTITY();

    INSERT INTO dbo.Stages (FunnelId, Title, Position, color) VALUES
    (@FunnelId, 'Nuevo',      1, '#1eb099'),
    (@FunnelId, 'Contactado', 2, '#188c79'),
    (@FunnelId, 'Calificado', 3, '#12695b'),
    (@FunnelId, 'Propuesta',  4, '#0c453c'),
    (@FunnelId, 'Cierre',     5, '#06211d');

    PRINT CONCAT('OK: Funnel creado para AccountId=', @AccId);
    FETCH NEXT FROM cur INTO @AccId;
END

CLOSE cur;
DEALLOCATE cur;
GO

-- PASO 4: Asignar colores de la paleta a etapas existentes sin color
--         Detecta cuÃ¡ntas etapas tiene el funnel y aplica el gradiente correcto
;WITH StageRanked AS (
    SELECT
        s.Id,
        s.FunnelId,
        s.Position,
        COUNT(*) OVER (PARTITION BY s.FunnelId) AS TotalInFunnel
    FROM dbo.Stages s
    WHERE s.color IS NULL OR s.color = ''
),
Capped AS (
    -- Si el funnel tiene mÃ¡s de 18 etapas, usa la paleta de 18 (la mÃ¡s oscura al final)
    SELECT
        sr.Id,
        CASE WHEN sr.TotalInFunnel > 18 THEN 18 ELSE sr.TotalInFunnel END AS TotalStages,
        CASE WHEN sr.TotalInFunnel > 18
             THEN CAST(ROUND(CAST(sr.Position AS FLOAT) / sr.TotalInFunnel * 18, 0) AS INT)
             ELSE sr.Position
        END AS MappedPosition
    FROM StageRanked sr
)
UPDATE s
SET s.color = sc.Color
FROM dbo.Stages s
JOIN Capped c ON c.Id = s.Id
JOIN #StageColors sc ON sc.TotalStages = c.TotalStages
    AND sc.Position = CASE WHEN c.MappedPosition < 1 THEN 1
                           WHEN c.MappedPosition > c.TotalStages THEN c.TotalStages
                           ELSE c.MappedPosition END;
PRINT CONCAT('OK: Colores asignados a etapas existentes: ', @@ROWCOUNT);

DROP TABLE #StageColors;
GO

-- ============================================================
-- DEALS DESDE LEADS EXISTENTES
-- Un Deal por cada Lead que aÃºn no tenga Deal asociado
-- ============================================================

-- Subquery con la primera etapa de cada funnel por cuenta
;WITH FirstStage AS (
    SELECT
        f.AccountId,
        s.Id AS StageId,
        ROW_NUMBER() OVER (PARTITION BY f.AccountId ORDER BY s.Position ASC) AS rn
    FROM dbo.Funnels f
    JOIN dbo.Stages s ON s.FunnelId = f.Id
)
INSERT INTO dbo.Deals
    (DealName, AccountId, PrimaryContactId, CompanyId, StageId,
     Status, DealType, OriginatingLeadId, CreatedOn, ProspectSource, AdName, OriginType)
SELECT
    -- Nombre del deal: "Nombre Apellido" del contacto, o fallback al email, o ID
    COALESCE(
        NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))), ''),
        c.Email,
        CONCAT('Lead #', l.LeadId)
    ),
    l.AccountId,
    l.ContactId,
    c.CompanyId,
    fs.StageId,
    -- Mapear status del lead al status del deal
    CASE l.Status
        WHEN 'Convertido' THEN 'Ganado'
        WHEN 'Perdido'    THEN 'Perdido'
        ELSE                   'Abierto'
    END,
    'NewBusiness',
    l.LeadId,
    l.CreatedOn,
    l.ProspectSource,
    l.AdName,
    l.OriginType
FROM dbo.Leads l
LEFT JOIN dbo.Contacts c ON c.ContactId = l.ContactId
JOIN FirstStage fs ON fs.AccountId = l.AccountId AND fs.rn = 1
WHERE
    -- Solo leads que aÃºn no tengan deal
    NOT EXISTS (
        SELECT 1 FROM dbo.Deals d WHERE d.OriginatingLeadId = l.LeadId
    )
    AND l.AccountId IS NOT NULL;

PRINT CONCAT('OK: Deals creados desde Leads: ', @@ROWCOUNT);
GO

-- Asignar Owner del Deal = OwnerUserId del Lead
INSERT INTO dbo.DealUsers (DealId, UserId, RoleInDeal)
SELECT d.DealId, l.OwnerUserId, 'Owner'
FROM dbo.Deals d
JOIN dbo.Leads l ON l.LeadId = d.OriginatingLeadId
WHERE
    l.OwnerUserId IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM dbo.DealUsers du
        WHERE du.DealId = d.DealId AND du.UserId = l.OwnerUserId
    );

PRINT CONCAT('OK: DealUsers (Owner) asignados: ', @@ROWCOUNT);
GO

-- VERIFICACIÃ“N
SELECT
    a.AccountId,
    a.Name AS Cuenta,
    f.Id   AS FunnelId,
    f.Title AS Embudo,
    COUNT(DISTINCT s.Id) AS NumEtapas,
    COUNT(DISTINCT d.DealId) AS NumDeals
FROM dbo.Accounts a
LEFT JOIN dbo.Funnels f ON f.AccountId = a.AccountId
LEFT JOIN dbo.Stages  s ON s.FunnelId  = f.Id
LEFT JOIN dbo.Deals   d ON d.AccountId = a.AccountId
GROUP BY a.AccountId, a.Name, f.Id, f.Title
ORDER BY a.AccountId;
GO

-- ####################################################################
-- ### MÃ“DULO WHATSAPP â€” AdaptaciÃ³n a tablas existentes (idempotente)
-- ### Las tablas ContactsWhatsapps, MessagesWhatsapps, SavedResponseWhatsapps
-- ### ya existen en producciÃ³n con datos. Solo agregamos columnas faltantes.
-- ### NO usar dotnet ef migrations add. Ejecutar con SSMS / Azure Data Studio.
-- ####################################################################

-- â”€â”€ 1. Customers: TwoChatApiKey (nueva; hasWhatsApp ya existe con ese nombre exacto) â”€â”€
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'TwoChatApiKey')
    ALTER TABLE dbo.Customers ADD TwoChatApiKey NVARCHAR(200) NULL;
GO
-- Activar hasWhatsApp para customers que ya tenÃ­an configuraciÃ³n
UPDATE dbo.Customers SET hasWhatsApp = 1
WHERE (WhatsappChannel IS NOT NULL OR WhatsappNumber IS NOT NULL) AND (hasWhatsApp IS NULL OR hasWhatsApp = 0);
GO
-- Para habilitar manualmente: UPDATE dbo.Customers SET hasWhatsApp = 1 WHERE id = <id>;

-- â”€â”€ 2. ContactsWhatsapps: agregar columnas nuevas â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ContactsWhatsapps') AND name = N'AccountId')
    ALTER TABLE dbo.ContactsWhatsapps ADD AccountId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ContactsWhatsapps') AND name = N'LinkedContactId')
    ALTER TABLE dbo.ContactsWhatsapps ADD LinkedContactId INT NULL;
GO

-- â”€â”€ 3. FK: ContactsWhatsapps.LinkedContactId â†’ Contacts.ContactId â”€â”€â”€â”€â”€â”€â”€â”€â”€
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ContactsWhatsapps_LinkedContact')
    ALTER TABLE dbo.ContactsWhatsapps ADD CONSTRAINT FK_ContactsWhatsapps_LinkedContact
        FOREIGN KEY (LinkedContactId) REFERENCES dbo.Contacts(ContactId) ON DELETE NO ACTION;
GO

-- NOTA: Las tablas MessagesWhatsapps y SavedResponseWhatsapps ya existen
-- en producciÃ³n y NO deben recrearse. El modelo C# ahora apunta a esos
-- nombres correctos (con 's' al final). No crear MessagesWhatsapp ni
-- SavedResponseWhatsapp (sin 's') â€” esas versiones ya no se usan.

-- â”€â”€ 4. Customers: columnas para almacenar IDs de webhooks de 2Chat â”€â”€â”€â”€â”€â”€â”€â”€
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'WebhookReceiveId')
    ALTER TABLE dbo.Customers ADD WebhookReceiveId NVARCHAR(100) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'WebhookSentId')
    ALTER TABLE dbo.Customers ADD WebhookSentId NVARCHAR(100) NULL;
GO

-- ============================================================
-- FLUJO DE LEADS â€” Fase CRM
-- Asegurar columnas en Contacts para calificaciÃ³n y conversiÃ³n
-- ============================================================

-- Contacts: LifecycleStatus (etapa del ciclo de vida del contacto)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Contacts') AND name = N'LifecycleStatus')
    ALTER TABLE dbo.Contacts ADD LifecycleStatus NVARCHAR(50) NULL;
GO

-- Contacts: PostalCode
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Contacts') AND name = N'PostalCode')
    ALTER TABLE dbo.Contacts ADD PostalCode NVARCHAR(20) NULL;
GO

-- Leads: LifecycleStatus (para tracking del ciclo)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'LifecycleStatus')
    ALTER TABLE dbo.Leads ADD LifecycleStatus NVARCHAR(50) NULL;
GO

-- Leads: StageId (stage en kanban de prospectos, si aplica)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'StageId')
    ALTER TABLE dbo.Leads ADD StageId INT NULL;
GO

-- Leads: LeadLostReasonsId
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'LeadLostReasonsId')
    ALTER TABLE dbo.Leads ADD LeadLostReasonsId INT NULL;
GO

-- Leads: ProspectSourceId (FK normalizada)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Leads') AND name = N'ProspectSourceId')
    ALTER TABLE dbo.Leads ADD ProspectSourceId INT NULL;
GO

-- ============================================================
-- FIX: Columnas bool con NULL en registros existentes
-- EF Core lanza SqlNullValueException al leer NULL en bool no-nullable
-- ============================================================

-- Customers.hasWhatsApp â€” rellenar NULLs con 0 (false)
UPDATE dbo.Customers SET hasWhatsApp = 0 WHERE hasWhatsApp IS NULL;
GO

-- Contacts.IsWhatsappContact â€” rellenar NULLs con 0 (false)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Contacts') AND name = N'IsWhatsappContact')
    UPDATE dbo.Contacts SET IsWhatsappContact = 0 WHERE IsWhatsappContact IS NULL;
GO

-- MessagesWhatsapp.IsRead â€” rellenar NULLs con 0
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MessagesWhatsapp') AND name = N'IsRead')
    UPDATE dbo.MessagesWhatsapp SET IsRead = 0 WHERE IsRead IS NULL;
GO

-- ============================================================
-- ADD-ON: EMAIL PROPIO â€” Config SMTP por Account
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpEnabled')
    ALTER TABLE dbo.Accounts ADD SmtpEnabled BIT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpHost')
    ALTER TABLE dbo.Accounts ADD SmtpHost NVARCHAR(200) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpPort')
    ALTER TABLE dbo.Accounts ADD SmtpPort INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpUser')
    ALTER TABLE dbo.Accounts ADD SmtpUser NVARCHAR(200) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpPassword')
    ALTER TABLE dbo.Accounts ADD SmtpPassword NVARCHAR(500) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpFromAddress')
    ALTER TABLE dbo.Accounts ADD SmtpFromAddress NVARCHAR(320) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpFromName')
    ALTER TABLE dbo.Accounts ADD SmtpFromName NVARCHAR(200) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpEnableSsl')
    ALTER TABLE dbo.Accounts ADD SmtpEnableSsl BIT NULL DEFAULT 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpIsVerified')
    ALTER TABLE dbo.Accounts ADD SmtpIsVerified BIT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpVerifiedAt')
    ALTER TABLE dbo.Accounts ADD SmtpVerifiedAt DATETIME2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Accounts') AND name = N'SmtpLastError')
    ALTER TABLE dbo.Accounts ADD SmtpLastError NVARCHAR(MAX) NULL;
GO

-- ============================================================
-- EMAIL LOG â€” Historial de correos enviados desde el CRM
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.EmailLogs'))
BEGIN
    CREATE TABLE dbo.EmailLogs (
        EmailLogId    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        AccountId     INT NULL,
        LeadId        INT NULL,
        DealId        INT NULL,
        ContactId     INT NULL,
        SentByUserId  NVARCHAR(128) NULL,
        ToAddress     NVARCHAR(320) NOT NULL,
        CcAddress     NVARCHAR(320) NULL,
        Subject       NVARCHAR(500) NOT NULL,
        BodyHtml      NVARCHAR(MAX) NOT NULL,
        SentAt        DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        IsSuccess     BIT NOT NULL DEFAULT 1,
        ErrorMessage  NVARCHAR(MAX) NULL,

        CONSTRAINT FK_EmailLogs_Users
            FOREIGN KEY (SentByUserId) REFERENCES dbo.Users(Id)
    );

    -- Ãndices para consultas frecuentes por entidad
    CREATE INDEX IX_EmailLogs_AccountId ON dbo.EmailLogs (AccountId, SentAt DESC);
    CREATE INDEX IX_EmailLogs_LeadId    ON dbo.EmailLogs (LeadId)    WHERE LeadId IS NOT NULL;
    CREATE INDEX IX_EmailLogs_DealId    ON dbo.EmailLogs (DealId)    WHERE DealId IS NOT NULL;
    CREATE INDEX IX_EmailLogs_ContactId ON dbo.EmailLogs (ContactId) WHERE ContactId IS NOT NULL;
END
GO

-- ============================================================
-- TAREAS â€” Ampliar Activities para soportar el mÃ³dulo de tareas
-- ============================================================

-- Activities: AccountId (aislamiento multi-tenant)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'AccountId')
    ALTER TABLE dbo.Activities ADD AccountId INT NULL;
GO

-- Activities: Priority (Alta / Media / Baja)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'Priority')
    ALTER TABLE dbo.Activities ADD Priority NVARCHAR(20) NULL;
GO

-- Activities: TaskStatus (Pendiente / En progreso / Completada / Cancelada)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'TaskStatus')
    ALTER TABLE dbo.Activities ADD TaskStatus NVARCHAR(30) NULL;
GO

-- Activities: AssignedToUserId (quiÃ©n debe hacer la tarea, distinto del owner/creador)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'AssignedToUserId')
    ALTER TABLE dbo.Activities ADD AssignedToUserId NVARCHAR(128) NULL;
GO

-- Activities: DueDate (fecha lÃ­mite especÃ­fica, separada de ActivityDate)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'DueDate')
    ALTER TABLE dbo.Activities ADD DueDate DATETIME2 NULL;
GO

-- Activities: CreatedOn
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'CreatedOn')
    ALTER TABLE dbo.Activities ADD CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE();
GO

-- Ãndice para buscar tareas por cuenta rÃ¡pidamente
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Activities') AND name = N'IX_Activities_AccountId_Type')
    CREATE INDEX IX_Activities_AccountId_Type ON dbo.Activities (AccountId, ActivityType);
GO


-- ============================================================
-- WEBHOOKS â€” Entrantes y Salientes por cuenta
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.AccountWebhooks'))
BEGIN
    CREATE TABLE dbo.AccountWebhooks (
        WebhookId    INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        AccountId    INT NOT NULL,
        Name         NVARCHAR(200) NOT NULL,

        -- 'Incoming' | 'Outgoing'
        Direction    NVARCHAR(20)  NOT NULL DEFAULT 'Incoming',

        -- Entrante â€” plataforma de origen
        -- 'MetaLeadAds' | 'TikTokLeadGen' | 'GoogleLeads' | 'CustomHttp'
        Platform     NVARCHAR(50)  NULL,

        -- Entrante â€” acciÃ³n al recibir
        -- 'CreateLead' | 'CreateContact' | 'CreateCompany' | 'LogOnly'
        ActionType   NVARCHAR(50)  NULL DEFAULT 'CreateLead',

        -- Entrante â€” URL key Ãºnico
        WebhookKey   NVARCHAR(64)  NULL,

        -- Entrante â€” configuraciÃ³n Meta
        MetaAppId           NVARCHAR(200) NULL,
        MetaAppSecret       NVARCHAR(500) NULL,
        MetaVerifyToken     NVARCHAR(200) NULL,
        MetaPageAccessToken NVARCHAR(MAX) NULL,
        MetaPageId          NVARCHAR(100) NULL,

        -- Entrante â€” destino en Profet
        DestFunnelId   INT          NULL,
        DestLeadStatus NVARCHAR(50) NULL DEFAULT 'Nuevo',

        -- Saliente â€” evento disparador
        -- 'LeadCreated' | 'LeadUpdated' | 'LeadStatusChanged' | 'ContactCreated' | 'DealCreated' | 'DealWon' | 'DealLost' | 'TaskCreated'
        TriggerEvent   NVARCHAR(100) NULL,

        -- Saliente â€” URL destino
        TargetUrl      NVARCHAR(500) NULL,
        OutgoingSecret NVARCHAR(300) NULL,

        -- Estado y mÃ©tricas
        IsActive         BIT       NOT NULL DEFAULT 1,
        CreatedAt        DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        LastTriggeredAt  DATETIME2 NULL,
        TriggerCount     INT       NOT NULL DEFAULT 0,
        LastError        NVARCHAR(MAX) NULL,

        CONSTRAINT FK_AccountWebhooks_Accounts
            FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId)
    );
    CREATE INDEX IX_AccountWebhooks_AccountId ON dbo.AccountWebhooks (AccountId);
    CREATE UNIQUE INDEX UX_AccountWebhooks_WebhookKey ON dbo.AccountWebhooks (WebhookKey) WHERE WebhookKey IS NOT NULL;
END
GO

-- Insertar el webhook de Meta con los valores ya generados (ajusta AccountId)
-- INSERT INTO dbo.AccountWebhooks (AccountId, Name, Direction, Platform, ActionType, WebhookKey, MetaVerifyToken, DestLeadStatus, IsActive, CreatedAt, TriggerCount)
-- VALUES (1, 'Leads Facebook', 'Incoming', 'MetaLeadAds', 'CreateLead', '09c0e03b92198732795c755acdae7ac8', '3fa90349b9ce903050348a6e5a429eaaeec811f8', 'Nuevo', 1, GETUTCDATE(), 0);
GO

-- Agregar columnas para el formulario de Lead Ads de Meta
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AccountWebhooks') AND name = N'MetaFormId')
    ALTER TABLE dbo.AccountWebhooks ADD MetaFormId NVARCHAR(50) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AccountWebhooks') AND name = N'MetaFormName')
    ALTER TABLE dbo.AccountWebhooks ADD MetaFormName NVARCHAR(200) NULL;
GO

-- Agregar columna MetaManagedByUs a Customers
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customers') AND name = N'MetaManagedByUs')
    ALTER TABLE dbo.Customers ADD MetaManagedByUs BIT NOT NULL DEFAULT 0;
GO

-- ============================================================
-- MIGRACIÓN: Marcar clientes legacy como setup completado
-- Aplica a clientes que ya tienen cuentas configuradas en el
-- sistema viejo y no necesitan pasar por el Setup Wizard.
-- Equivale a haber ejecutado POST /api/setup/complete.
-- ============================================================

-- 1. Activar las cuentas de clientes legacy (Borrador → Activo)
UPDATE dbo.Accounts
SET Status = 'Activo'
WHERE Status != 'Activo'
  AND CustomerId IN (
      SELECT Id FROM dbo.Customers
      WHERE Status != 'Activo' AND Deleted = 0
      -- Solo clientes que ya tenían cuentas antes del nuevo sistema
      -- (tienen AccountId bajo cierto umbral o sin SetupToken reciente)
  );
GO

-- 2. Activar usuarios de clientes legacy
UPDATE dbo.Users
SET Active = 1
WHERE CustomerId IN (
    SELECT Id FROM dbo.Customers
    WHERE Status != 'Activo' AND Deleted = 0
      AND Id IN (SELECT DISTINCT CustomerId FROM dbo.Accounts)
);
GO

-- 3. Marcar el Customer como Activo y limpiar SetupToken
UPDATE dbo.Customers
SET Status    = 'Activo',
    SetupStep = 7,
    SetupToken = NULL
WHERE Status != 'Activo'
  AND Deleted = 0
  AND Id IN (
      -- Solo clientes que ya tienen al menos una cuenta
      SELECT DISTINCT CustomerId FROM dbo.Accounts
  );
GO

-- ── Nombre de página Meta en webhooks ────────────────────────────────────────
ALTER TABLE dbo.AccountWebhooks ADD MetaPageName NVARCHAR(200) NULL;
GO

-- ── Historial de eventos de webhooks ─────────────────────────────────────────
CREATE TABLE dbo.WebhookEventLogs (
    EventLogId  BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    WebhookId   INT NOT NULL,
    ReceivedAt  DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status      NVARCHAR(20) NOT NULL DEFAULT 'Success',
    Summary     NVARCHAR(300) NULL,
    ExternalId  NVARCHAR(100) NULL,
    ErrorMessage NVARCHAR(500) NULL,
    CONSTRAINT FK_WebhookEventLogs_Webhook FOREIGN KEY (WebhookId)
        REFERENCES dbo.AccountWebhooks(WebhookId) ON DELETE CASCADE
);
CREATE INDEX IX_WebhookEventLogs_WebhookId_ReceivedAt
    ON dbo.WebhookEventLogs(WebhookId, ReceivedAt DESC);
GO

-- ── Mapeo de campos Meta → CRM en webhooks ───────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountWebhooks') AND name = 'FieldMappingJson')
    ALTER TABLE dbo.AccountWebhooks ADD FieldMappingJson NVARCHAR(MAX) NULL;
GO


-- ── Formatter: reglas de transformación por webhook ─────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountWebhooks') AND name = 'FormatterJson')
    ALTER TABLE dbo.AccountWebhooks ADD FormatterJson NVARCHAR(MAX) NULL;
GO

-- ── Meta Ads Account ID: cruce de métricas de inversión publicitaria ────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountWebhooks') AND name = 'MetaAdAccountId')
    ALTER TABLE dbo.AccountWebhooks ADD MetaAdAccountId NVARCHAR(50) NULL;
GO

-- ── Meta Ad Account ID a nivel de Account ───────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Accounts') AND name = 'MetaAdAccountId')
    ALTER TABLE dbo.Accounts ADD MetaAdAccountId NVARCHAR(50) NULL;
GO


-- ── Automatizaciones (mini-Zapier) ───────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AutomationRules')
CREATE TABLE dbo.AutomationRules (
    RuleId           INT IDENTITY(1,1) PRIMARY KEY,
    AccountId        INT NOT NULL REFERENCES dbo.Accounts(AccountId),
    Name             NVARCHAR(200) NOT NULL,
    IsActive         BIT NOT NULL DEFAULT 1,
    Deleted          BIT NOT NULL DEFAULT 0,
    TriggerType      NVARCHAR(50)  NOT NULL DEFAULT 'WebhookIncoming',
    TriggerPlatform  NVARCHAR(50)  NULL,
    WebhookKey       NVARCHAR(60)  NULL,
    ConditionsJson   NVARCHAR(MAX) NULL,
    CreatedAt        DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AutomationSteps')
CREATE TABLE dbo.AutomationSteps (
    StepId      INT IDENTITY(1,1) PRIMARY KEY,
    RuleId      INT NOT NULL REFERENCES dbo.AutomationRules(RuleId) ON DELETE CASCADE,
    StepOrder   INT NOT NULL DEFAULT 1,
    StepType    NVARCHAR(30)  NOT NULL,
    ConfigJson  NVARCHAR(MAX) NULL,
    IsActive    BIT NOT NULL DEFAULT 1
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AutomationLogs')
CREATE TABLE dbo.AutomationLogs (
    LogId           BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleId          INT NOT NULL REFERENCES dbo.AutomationRules(RuleId) ON DELETE CASCADE,
    ExecutedAt      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Success         BIT NOT NULL DEFAULT 1,
    StepsResultJson NVARCHAR(MAX) NULL,
    ErrorMessage    NVARCHAR(1000) NULL,
    PayloadPreview  NVARCHAR(500)  NULL
);
GO

-- Índices para consultas frecuentes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AutomationRules_AccountId')
    CREATE INDEX IX_AutomationRules_AccountId ON dbo.AutomationRules(AccountId, IsActive, Deleted);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AutomationRules_WebhookKey')
    CREATE UNIQUE INDEX IX_AutomationRules_WebhookKey ON dbo.AutomationRules(WebhookKey) WHERE WebhookKey IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AutomationLogs_RuleId')
    CREATE INDEX IX_AutomationLogs_RuleId ON dbo.AutomationLogs(RuleId, ExecutedAt DESC);
GO

-- ####################################################################
-- ### MODULO: PLAYBOOKS (secuencias de tareas configurables por cuenta)
-- ### Crea ActivityPlaybooks / PlaybookTasks si no existen y las extiende.
-- ####################################################################
PRINT '--- Modulo Playbooks: creando/actualizando tablas... ---';

-- Tablas base (solo si no existen; las columnas nuevas se agregan mas abajo)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ActivityPlaybooks')
CREATE TABLE dbo.ActivityPlaybooks (
    PlaybookId INT PRIMARY KEY IDENTITY(1,1),
    AccountId  INT NOT NULL,
    Name       NVARCHAR(255) NOT NULL,
    CONSTRAINT FK_ActivityPlaybooks_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlaybookTasks')
CREATE TABLE dbo.PlaybookTasks (
    TaskId     INT PRIMARY KEY IDENTITY(1,1),
    PlaybookId INT NOT NULL,
    TaskName   NVARCHAR(1000) NOT NULL,
    "Order"    INT NOT NULL,
    CONSTRAINT FK_PlaybookTasks_Playbooks FOREIGN KEY (PlaybookId) REFERENCES dbo.ActivityPlaybooks(PlaybookId) ON DELETE CASCADE
);
GO

-- ActivityPlaybooks: activo, predeterminado, descripcion, soft delete
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ActivityPlaybooks') AND name = 'Description')
    ALTER TABLE dbo.ActivityPlaybooks ADD Description NVARCHAR(1000) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ActivityPlaybooks') AND name = 'IsActive')
    ALTER TABLE dbo.ActivityPlaybooks ADD IsActive BIT NOT NULL DEFAULT 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ActivityPlaybooks') AND name = 'IsDefault')
    ALTER TABLE dbo.ActivityPlaybooks ADD IsDefault BIT NOT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ActivityPlaybooks') AND name = 'Deleted')
    ALTER TABLE dbo.ActivityPlaybooks ADD Deleted BIT NOT NULL DEFAULT 0;
GO

-- PlaybookTasks: descripcion, prioridad, dias de offset para la fecha limite
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PlaybookTasks') AND name = 'Description')
    ALTER TABLE dbo.PlaybookTasks ADD Description NVARCHAR(1000) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PlaybookTasks') AND name = 'Priority')
    ALTER TABLE dbo.PlaybookTasks ADD Priority NVARCHAR(20) NOT NULL DEFAULT 'Media';
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PlaybookTasks') AND name = 'OffsetDays')
    ALTER TABLE dbo.PlaybookTasks ADD OffsetDays INT NOT NULL DEFAULT 0;
GO

-- Indice: buscar rapido el playbook predeterminado activo de una cuenta
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ActivityPlaybooks_AccountDefault')
    CREATE INDEX IX_ActivityPlaybooks_AccountDefault ON dbo.ActivityPlaybooks(AccountId, IsDefault, IsActive, Deleted);
GO

-- PlaybookTasks: tipo de accion del paso y etapa destino (recorrido lead -> oportunidad)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PlaybookTasks') AND name = 'ActionType')
    ALTER TABLE dbo.PlaybookTasks ADD ActionType NVARCHAR(30) NOT NULL DEFAULT 'Task';
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PlaybookTasks') AND name = 'TargetStageId')
    ALTER TABLE dbo.PlaybookTasks ADD TargetStageId INT NULL;
GO

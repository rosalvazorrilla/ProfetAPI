--Se divide la info de user
-- 1. Asegurar llave primaria en Customers

-- Agregar Email (vital para la invitación)
ALTER TABLE Customers ADD Email VARCHAR(255) NULL;

-- Agregar controles del Wizard
ALTER TABLE Customers ADD SetupToken VARCHAR(128) NULL;
ALTER TABLE Customers ADD SetupStep INT NOT NULL DEFAULT 0;

-- Agregar el nuevo campo de Estatus
ALTER TABLE Customers ADD Status VARCHAR(50) NOT NULL DEFAULT 'Pendiente de Setup';

-- Crear índice para que el login con token sea rapidísimo
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
    NULL, -- LastLoginDate se llenará en el futuro
    -- Aquí construimos el JSON dinámicamente con los valores REALES de la tabla Users
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

-- 1. Conexión de Users con Customers
ALTER TABLE dbo.Users
ADD CONSTRAINT FK_Users_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(id);
GO

-- 2. Conexión de Jerarquía en Users
ALTER TABLE dbo.Users
ALTER COLUMN ParentId NVARCHAR(128) NULL;
GO
ALTER TABLE dbo.Users
ADD CONSTRAINT FK_Users_ParentUser FOREIGN KEY (ParentId) REFERENCES dbo.Users(Id);
GO

-- 3. Conexión de UserTeams
ALTER TABLE dbo.UserTeams
ADD CONSTRAINT FK_UserTeams_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id);
GO
ALTER TABLE dbo.UserTeams
ADD CONSTRAINT FK_UserTeams_Teams FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id);
GO

-- 4. Conexión de UserRoles
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
-- ### Reemplaza tu sección de limpieza de `Users` con este proceso de 2 partes.
-- ####################################################################
PRINT '--- Mejorando el proceso de limpieza de la tabla Users... ---';

-- PARTE A: Generar los comandos para borrar los "candados" (default constraints) automáticamente.
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

BEGIN TRANSACTION; -- Iniciamos una transacción para asegurar que todo se ejecute correctamente o nada.
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
        
        -- Paquetes de Configuración (FKs a otras tablas)
        LeadLostReasonsPackagesId INT NULL,
        LeadDealsTypesPackagesId INT NULL,
        ActivitiesTemplateId INT NULL,
        
        -- Estado y Fechas
        Status NVARCHAR(50) NOT NULL DEFAULT 'Por Iniciar', -- Estado en Español como acordamos
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
-- Se usa SET IDENTITY_INSERT para mantener los IDs originales y facilitar la migración de tablas dependientes.
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

-- 3.3: Migrar los correos de notificación (parseando el texto)
INSERT INTO dbo.AccountNotificationRecipients (AccountId, Email)
SELECT Id, value
FROM dbo.Campaigns
CROSS APPLY STRING_SPLIT(EmailLeadBooster, ',')
WHERE EmailLeadBooster IS NOT NULL AND LTRIM(RTRIM(value)) <> '';
PRINT 'Correos de notificación migrados.';
GO


-- ####################################################################

PRINT '--- PASO 4: Evolucionando CampaingsActiveDates a AccountStatusHistory ---';

-- 4.1: Renombrar la tabla y la columna
EXEC sp_rename 'dbo.CampaingsActiveDates', 'AccountStatusHistory';
EXEC sp_rename 'dbo.AccountStatusHistory.id_campaign', 'AccountId', 'COLUMN';
GO

-- 4.2: Añadir la columna de estado y poblarla
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

PRINT '--- PASO 5: Añadiendo las llaves foráneas finales ---';

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

PRINT '--- Todas las llaves foráneas han sido añadidas. ---';

-- ####################################################################

-- ####################################################################
-- ### 3. COMPLETAR: Re-cableado de Todas las Dependencias de `Campaigns`
-- ### Esta es la sección completa que faltaba para actualizar todas las tablas que apuntaban a 'Campaigns'.
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

PRINT '--- Módulo 2: Adaptando Funnels y Stages existentes... ---';

-- ####################################################################
-- ### PASO 2.1: CREAR LAS NUEVAS TABLAS PARA LA FUNCIONALIDAD DE PLANTILLAS
-- ### Estas tablas se crearán vacías, listas para que las uses como una nueva característica.
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
-- ### Mantenemos tus Funnels intactos y solo actualizamos su "dueño".
-- ####################################################################
PRINT '--- Vinculando Funnels existentes a las nuevas Accounts... ---';

-- 1. Añadimos la columna AccountId a la tabla Funnels
ALTER TABLE dbo.Funnels ADD AccountId INT NULL;
GO

-- 2. Migramos la relación: Asignamos cada Funnel a la Account correspondiente
--    basándonos en el FunnelId que tenías en la vieja tabla Campaigns.
UPDATE f
SET f.AccountId = c.Id
FROM dbo.Funnels f
JOIN dbo.Campaigns c ON f.Id = c.FunnelId;
GO

-- 3. Creamos la llave foránea para formalizar la relación
ALTER TABLE dbo.Funnels
ADD CONSTRAINT FK_Funnels_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO

-- 4. Nos aseguramos de que Stages siga bien conectada a Funnels
ALTER TABLE dbo.Stages
ADD CONSTRAINT FK_Stages_Funnels FOREIGN KEY (FunnelId) REFERENCES dbo.Funnels(Id);
GO

BEGIN TRANSACTION;
GO

PRINT '--- MÓDULO 3: Creando y Migrando el Motor de Calificación... ---';

-- ####################################################################
-- ### PASO 3.1: CREAR LAS TABLAS DEL MOTOR DE CALIFICACIÓN
-- ####################################################################
PRINT '--- Creando las tablas del Motor de Calificación... ---';

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
INSERT INTO dbo.LeadTiers (Name) VALUES ('Estándar'), ('Premier');
GO
CREATE TABLE dbo.LeadScoringAnswers ( ScoringAnswerId INT PRIMARY KEY IDENTITY(1,1), LeadId BIGINT NOT NULL, QuestionId INT NOT NULL, AnswerOptionId INT NOT NULL );
GO

-- ####################################################################
-- ### PASO 3.2: MIGRAR TU SISTEMA 'LEADPACKAGES' AL NUEVO MOTOR
-- ####################################################################
PRINT '--- Migrando el sistema de calificación existente... ---';

-- 1. Migrar catálogos existentes a las nuevas plantillas
INSERT INTO dbo.ScoringTemplates (Name, Description) SELECT DescriptionES, DescriptionES FROM dbo.LeadPackages;
GO
INSERT INTO dbo.ScoringTemplateQuestions (TemplateId, QuestionText) SELECT st.TemplateId, lq.DescriptionES FROM dbo.LeadQuestions lq JOIN dbo.LeadPackages lp ON lq.LeadPackageId = lp.Id JOIN dbo.ScoringTemplates st ON lp.DescriptionES = st.Name;
GO
INSERT INTO dbo.ScoringTemplateAnswerOptions (TemplateQuestionId, AnswerText) SELECT stq.TemplateQuestionId, la.DescriptionES FROM dbo.LeadAnswers la JOIN dbo.LeadQuestions lq ON la.LeadQuestionId = lq.Id JOIN dbo.ScoringTemplateQuestions stq ON lq.DescriptionES = stq.QuestionText;
GO

-- 2. Crear los ScoringModels "vivos" para cada Account que tenía un LeadPackage asignado
INSERT INTO dbo.ScoringModels (AccountId, Name) SELECT a.AccountId, CONCAT('Calificador para ', a.Name) FROM dbo.Accounts a JOIN dbo.Campaigns c ON a.AccountId = c.Id WHERE c.LeadPackageId IS NOT NULL;
GO

-- 3. Copiar las preguntas y respuestas de la plantilla al modelo vivo
INSERT INTO dbo.ScoringQuestions (ScoringModelId, QuestionText) SELECT sm.ScoringModelId, stq.QuestionText FROM dbo.Accounts a JOIN dbo.Campaigns c ON a.AccountId = c.Id JOIN dbo.LeadPackages lp ON c.LeadPackageId = lp.Id JOIN dbo.ScoringTemplates st ON lp.DescriptionES = st.Name JOIN dbo.ScoringTemplateQuestions stq ON st.TemplateId = stq.TemplateId JOIN dbo.ScoringModels sm ON a.AccountId = sm.AccountId;
GO
INSERT INTO dbo.ScoringAnswerOptions (QuestionId, AnswerText) SELECT sq.QuestionId, stao.AnswerText FROM dbo.Accounts a JOIN dbo.Campaigns c ON a.AccountId = c.Id JOIN dbo.LeadPackages lp ON c.LeadPackageId = lp.Id JOIN dbo.ScoringTemplates st ON lp.DescriptionES = st.Name JOIN dbo.ScoringTemplateQuestions stq ON st.TemplateId = stq.TemplateId JOIN dbo.ScoringTemplateAnswerOptions stao ON stq.TemplateQuestionId = stao.TemplateQuestionId JOIN dbo.ScoringModels sm ON a.AccountId = sm.AccountId JOIN dbo.ScoringQuestions sq ON sm.ScoringModelId = sq.ScoringModelId AND stq.QuestionText = sq.QuestionText;
GO

-- 4. Traducir la vieja lógica de puntuación a Reglas 'ADD_POINTS'
INSERT INTO dbo.ScoringRules (ScoringModelId, ConditionQuestionId, ConditionAnswerOptionId, ActionType, ActionValue)
SELECT sq.ScoringModelId, sq.QuestionId, sao.AnswerOptionId, 'ADD_POINTS', la.Value FROM dbo.LeadAnswers la JOIN dbo.LeadQuestions lq ON la.LeadQuestionId = lq.Id JOIN dbo.ScoringQuestions sq ON lq.DescriptionES = sq.QuestionText JOIN dbo.ScoringAnswerOptions sao ON sq.QuestionId = sao.QuestionId AND la.DescriptionES = sao.AnswerText WHERE ISNUMERIC(la.Value) = 1;
GO

-- ####################################################################
-- ### PASO 3.3: MIGRAR LAS RESPUESTAS HISTÓRICAS DE LOS LEADS (PATRÓN)
-- ####################################################################
PRINT '--- Migrando las respuestas históricas de los Leads... ---';
-- **ACCIÓN REQUERIDA:** Este es un patrón que deberás repetir para cada campo de calificación de tu tabla `Leads` (Budget, BuyingTime, etc.)

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
-- Repite el bloque INSERT anterior para tus otros campos de calificación (`BuyingTime`, etc.)


-- ####################################################################
-- ### PASO 3.4: LIMPIEZA DE TODAS LAS TABLAS DE CALIFICACIÓN OBSOLETAS
-- ####################################################################
PRINT '--- Limpieza de tablas de calificación obsoletas... ---';
PRINT '--- Revisa que la migración sea correcta antes de descomentar y ejecutar este bloque. ---';
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

PRINT '--- Módulo de Calificación completado. ---';


-- ####################################################################
-- ### INICIO DE LA TRANSACCIÓN GLOBAL
-- ####################################################################
BEGIN TRANSACTION;
GO

-- ####################################################################
-- ### FASE 1: CREAR LAS NUEVAS TABLAS
-- ### Creamos el esqueleto de todo el nuevo núcleo del CRM.
-- ####################################################################
PRINT '--- FASE 1: Creando las nuevas tablas del núcleo del CRM... ---';

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

-- 1.3: El Historial Unificado (Tablas Polimórficas)
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
-- ### FASE 2: LA GRAN MIGRACIÓN DE DATOS
-- ####################################################################
PRINT '--- FASE 2: Iniciando la Gran Migración desde Leads y tablas relacionadas... ---';

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

-- 2.4: Migrar las tablas relacionadas al sistema polimórfico
INSERT INTO dbo.Notes (Content, AuthorUserId, CreatedOn, EntityId, EntityType) SELECT Note, UserId, Date, LeadId, 'Lead' FROM dbo.LeadNotes;
GO
INSERT INTO dbo.Notes (Content, EntityId, EntityType) SELECT Comments, Id, 'Lead' FROM dbo.Leads WHERE Comments IS NOT NULL;
GO
INSERT INTO dbo.Attachments (FileName, UploaderUserId, CreatedOn, EntityId, EntityType) SELECT Name, UserId, Date, LeadId, 'Lead' FROM dbo.LeadFiles;
GO
INSERT INTO dbo.Activities_New (ActivityType, Subject, "Date", Notes, IsCompleted, OwnerUserId, EntityId, EntityType) SELECT ta.Title, a.Title, a.Date, a.Notes, a.Completed, a.UserId, a.LeadId, 'Lead' FROM dbo.Activities a JOIN dbo.TypeActivitiys ta ON a.TypeActivityId = ta.Id;
GO
-- Migración de LeadCalls a Activities y CallDetails
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

-- 2.6: Migración de Campos Personalizados ("Variables")
-- **ACCIÓN REQUERIDA:** Repite este patrón para cada columna de "variable" que quieras migrar.
PRINT '--- Migrando Campos Personalizados (Variables)... ---';
INSERT INTO dbo.CustomFieldValues (FieldId, EntityId, EntityType, Value)
SELECT 
    (SELECT FieldId FROM CustomFieldDefinitions WHERE FieldCode = 'Size'),
    d.DealId, 'Deal', l.Size
FROM dbo.Leads l JOIN dbo.Deals d ON l.Id = d.OriginatingLeadId WHERE l.Size IS NOT NULL;
GO

-- ####################################################################
-- ### FASE 3: AÑADIR LLAVES FORÁNEAS Y LIMPIEZA FINAL
-- ####################################################################
PRINT '--- FASE 3: Añadiendo llaves foráneas y limpiando... ---';

-- 3.1: Añadir todas las llaves foráneas
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
-- ... y el resto de las llaves foráneas para las nuevas tablas.

-- 3.2: Renombrar y Limpiar
EXEC sp_rename 'dbo.Leads', 'Leads_Old_Archived';
EXEC sp_rename 'dbo.Leads_New', 'Leads';
EXEC sp_rename 'dbo.Activities', 'Activities_Old_Archived';
EXEC sp_rename 'dbo.Activities_New', 'Activities';
GO
PRINT '--- Revisa que la migración sea correcta antes de descomentar y ejecutar el bloque de limpieza final. ---';
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

PRINT '--- ¡MIGRACIÓN DEL NÚCLEO DEL CRM COMPLETADA! ---';

PRINT '--- Creando tablas de metadatos para reportes... ---';

-- Tabla para definir los tipos de gráficos disponibles (barras, pastel, etc.)
CREATE TABLE dbo.ChartTypes (
    ChartTypeId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL, -- Ej: "Gráfica de Barras"
    Code NVARCHAR(50) NOT NULL UNIQUE -- Ej: "bar", "pie", "line"
);
GO

-- Tabla diccionario de todos los campos que se pueden usar en un reporte
CREATE TABLE dbo.ReportableFields (
    FieldId INT PRIMARY KEY IDENTITY(1,1),
    DisplayName NVARCHAR(255) NOT NULL, -- El nombre que ve el usuario, ej: "Monto del Trato"
    TechnicalName NVARCHAR(255) NOT NULL, -- El nombre técnico que usa la API, ej: "Deals.FinalAmount"
    FieldType NVARCHAR(50) NOT NULL, -- 'Metrica' o 'Dimension'
    AggregationType NVARCHAR(50) NULL -- Solo para Métricas, ej: 'SUM', 'COUNT', 'AVG'
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

-- Tabla para cada reporte individual (un "Widget" o gráfica) dentro de un dashboard
CREATE TABLE dbo.Widgets (
    WidgetId INT PRIMARY KEY IDENTITY(1,1),
    DashboardId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL, -- Ej: "Ventas por Vendedor este Mes"
    
    -- La "receta" del reporte
    MetricFieldId INT NOT NULL,
    DimensionFieldId INT NOT NULL,
    ChartTypeId INT NOT NULL,
    
    Filters NVARCHAR(MAX) NULL, -- Para guardar filtros avanzados en formato JSON

    -- Posición y tamaño en el dashboard
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

-- Tabla para generar los enlaces únicos que se van a compartir
CREATE TABLE dbo.ReportShareLinks (
    ShareLinkId INT PRIMARY KEY IDENTITY(1,1),
    PublicId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- Un ID único y seguro para la URL pública
    Description NVARCHAR(500) NULL, -- Para que el usuario sepa para qué es este enlace
    OwnerUserId NVARCHAR(128) NOT NULL,
    CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ExpiresOn DATETIME2 NULL, -- Para que los enlaces puedan expirar
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_ReportShareLinks_Users FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(Id)
);
GO

-- Tabla de unión que define qué Widgets se incluyen en cada enlace compartido
CREATE TABLE dbo.SharedWidgets (
    ShareLinkId INT NOT NULL,
    WidgetId INT NOT NULL,
    
    CONSTRAINT PK_SharedWidgets PRIMARY KEY (ShareLinkId, WidgetId),
    CONSTRAINT FK_SharedWidgets_Links FOREIGN KEY (ShareLinkId) REFERENCES dbo.ReportShareLinks(ShareLinkId) ON DELETE CASCADE,
    CONSTRAINT FK_SharedWidgets_Widgets FOREIGN KEY (WidgetId) REFERENCES dbo.Widgets(WidgetId) ON DELETE CASCADE
);
GO

-- ####################################################################
-- ### MÓDULO: CATÁLOGO DE PRODUCTOS/SERVICIOS (ÍTEMS)
-- ####################################################################
PRINT '--- Creando el nuevo sistema de Catálogo de Ítems... ---';

-- 1. Crear las nuevas tablas

-- La tabla para los "agrupadores" o catálogos, ligada a una Account
CREATE TABLE dbo.ItemCatalogs (
    CatalogId INT PRIMARY KEY IDENTITY(1,1),
    AccountId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    CONSTRAINT FK_ItemCatalogs_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId)
);
GO

-- La tabla para los productos/servicios individuales dentro de un catálogo
CREATE TABLE dbo.CatalogItems (
    ItemId INT PRIMARY KEY IDENTITY(1,1),
    CatalogId INT NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Price DECIMAL(18, 2) NULL,
    Code NVARCHAR(100) NULL, -- Código de producto/SKU
    CONSTRAINT FK_CatalogItems_Catalogs FOREIGN KEY (CatalogId) REFERENCES dbo.ItemCatalogs(CatalogId) ON DELETE CASCADE
);
GO

-- La tabla de unión que conecta un Deal con los ítems que se están vendiendo
CREATE TABLE dbo.DealItems (
    DealId INT NOT NULL,
    ItemId INT NOT NULL,
    Quantity INT NOT NULL DEFAULT 1,
    Price DECIMAL(18, 2) NOT NULL, -- El precio final acordado para este ítem en este trato
    CONSTRAINT PK_DealItems PRIMARY KEY (DealId, ItemId),
    CONSTRAINT FK_DealItems_Deals FOREIGN KEY (DealId) REFERENCES dbo.Deals(DealId) ON DELETE CASCADE,
    CONSTRAINT FK_DealItems_Items FOREIGN KEY (ItemId) REFERENCES dbo.CatalogItems(ItemId)
);
GO

-- ####################################################################
-- ### MIGRACIÓN DE DATOS (de tus viejas tablas a las nuevas)
-- ####################################################################
PRINT '--- Migrando datos del viejo catálogo de productos... ---';

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
-- ### Añade este bloque a tu sección de limpieza final.
-- ####################################################################
PRINT '--- Marcando para limpieza las tablas de catálogo obsoletas... ---';
/*
DROP TABLE dbo.DealsTypesDeals;
DROP TABLE dbo.LeadDealsTypes;
DROP TABLE dbo.LeadDealsTypesPackages;
*/

-- ####################################################################
-- ### MÓDULO: SISTEMA DE ETIQUETADO (TAGS)
-- ####################################################################
PRINT '--- Creando y migrando el nuevo sistema de etiquetado... ---';

-- 1. Evolucionar tu catálogo de etiquetas 'TagsLeads' a una tabla genérica 'Tags'
PRINT '--- Evolucionando la tabla de catálogo TagsLeads a Tags... ---';
EXEC sp_rename 'dbo.TagsLeads', 'Tags';
GO
EXEC sp_rename 'dbo.Tags.Id', 'TagId', 'COLUMN';
GO

-- 2. Crear la nueva tabla de unión polimórfica 'Taggings'
-- Esta tabla reemplazará a 'LeadsTags' y permitirá etiquetar cualquier entidad.
PRINT '--- Creando la nueva tabla polimórfica Taggings... ---';
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
-- Añade este bloque a tu sección de limpieza final.
PRINT '--- Marcando para limpieza la tabla obsoleta LeadsTags... ---';
/*
DROP TABLE dbo.LeadsTags;
*/

BEGIN TRANSACTION;
GO

-- ####################################################################
-- ### MÓDULO 1: INTEGRACIÓN CON WHATSAPP
-- ####################################################################
PRINT '--- Módulo 1: Integrando WhatsApp... ---';

-- 1.1: Añadir la bandera a la tabla Contacts para identificar contactos de WhatsApp
ALTER TABLE dbo.Contacts
ADD IsWhatsappContact BIT NOT NULL DEFAULT 0;
GO

-- 1.2: Migrar la información, marcando los contactos existentes que son de WhatsApp
-- (Este script asume que se pueden cruzar por el número de teléfono)
UPDATE c
SET c.IsWhatsappContact = 1
FROM dbo.Contacts c
JOIN dbo.ContactsWhatsapp cwa ON c.PhoneNumber = cwa.PhoneNumber; -- Ajusta el JOIN si la relación es otra
GO

-- 1.3: Formalizar la relación de la tabla de mensajes con el directorio de contactos
ALTER TABLE dbo.MessagesWhatsapp
ADD CONSTRAINT FK_MessagesWhatsapp_Contacts FOREIGN KEY (ContactId) REFERENCES dbo.Contacts(ContactId);
GO

-- ####################################################################
-- ### MÓDULO 2: SISTEMA DE NOTIFICACIONES
-- ####################################################################
PRINT '--- Módulo 2: Evolucionando Notificaciones... ---';

-- 2.1: Hacer la tabla 'Notifications' polimórfica
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
-- ### MÓDULO 3: HISTORIAL Y LOGS (AUDITORÍA)
-- ####################################################################
PRINT '--- Módulo 3: Unificando Historial de Auditoría... ---';

-- 3.1: Crear la nueva tabla polimórfica para auditoría de acciones de usuario
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
-- Migración de LeadLogs
INSERT INTO dbo.AuditLogs (EntityId, EntityType, AuthorUserId, Timestamp, EventType, Description)
SELECT LeadId, 'Lead', UserId, Date, 'LegacyLog', Description
FROM dbo.LeadLogs;
GO
-- Migración de StageLeadLogs
INSERT INTO dbo.AuditLogs (EntityId, EntityType, AuthorUserId, Timestamp, EventType, Description, ChangeData)
SELECT LeadId, 'Lead', UserId, Date, 'StageChange', 
       CONCAT('Cambio de etapa. Tardó ', Hours), -- Descripción genérica
       CONCAT('{"oldStageId":', LastStageId, ',"newStageId":', StageId, '}') -- Guardamos los detalles en JSON
FROM dbo.StageLeadLogs;
GO

-- ####################################################################
-- ### MÓDULO 4: ACTIVIDADES Y SUS PLANTILLAS
-- ####################################################################
PRINT '--- Módulo 4: Evolucionando Actividades y sus Plantillas... ---';

-- 4.1: Crear las nuevas tablas para las "Guías de Tareas"
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
JOIN dbo.Accounts a ON at.Id = a.ActivitiesTemplateId; -- Asumiendo que la FK está en Accounts
GO
INSERT INTO dbo.PlaybookTasks (PlaybookId, TaskName, "Order")
SELECT ap.PlaybookId, atr.Title, ISNULL(atr.ParentId, 0) -- Usamos ParentId como el orden, ajusta si es necesario
FROM dbo.ActivitiesTemplatesRecords atr
JOIN dbo.ActivitiesTemplates at ON atr.ActivitiesTemplatesId = at.Id
JOIN dbo.ActivityPlaybooks ap ON at.Name = ap.Name;
GO

-- ####################################################################
-- ### MÓDULO 5: CONFIGURACIÓN DE TELEFONÍA
-- ####################################################################
PRINT '--- Módulo 5: Formalizando Configuración de Telefonía... ---';

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
-- ### MÓDULO 6: CATÁLOGOS FINALES
-- ####################################################################
PRINT '--- Módulo 6: Creando y Vinculando Catálogos Finales... ---';

-- 6.1: Crear y poblar el nuevo catálogo de ProspectSources
CREATE TABLE dbo.ProspectSources (
    SourceId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL
);
GO
INSERT INTO dbo.ProspectSources (Name) VALUES 
('Facebook'), ('Google'), ('LinkedIn'), ('Lead Booster'), ('Landing Page'), 
('Visitante'), ('WhatsApp'), ('Expo'), ('Recomendación'), ('Llamada'), 
('Inbox'), ('Instagram'), ('Correo'), ('Pagina Web'), ('Medio Tradicional'), 
('Panorámico'), ('Broker'), ('Prospeccion');
GO

-- 6.2: Asegurar llaves primarias en catálogos existentes
ALTER TABLE dbo.Industries
ADD CONSTRAINT PK_Industries PRIMARY KEY (Id);
GO
ALTER TABLE dbo.ContactForms
ADD CONSTRAINT PK_ContactForms PRIMARY KEY (Id);
GO

COMMIT TRANSACTION;
GO


PRINT '--- Añadiendo la columna ProspectSourceId a Leads_New y Deals... ---';

ALTER TABLE dbo.Leads
ADD ProspectSourceId INT NULL;
GO

ALTER TABLE dbo.Deals
ADD ProspectSourceId INT NULL;
GO

PRINT '--- Migrando los datos de texto de ProspectSource a los nuevos IDs... ---';

-- Migración para la tabla Deals
UPDATE d
SET d.ProspectSourceId = ps.SourceId
FROM dbo.Deals d
JOIN dbo.ProspectSources ps ON d.ProspectSource = ps.Name
WHERE d.ProspectSource IS NOT NULL;
GO

-- Migración para la tabla Leads
UPDATE ln
SET ln.ProspectSourceId = ps.SourceId
FROM dbo.Leads ln
JOIN dbo.ProspectSources ps ON ln.ProspectSource = ps.Name
WHERE ln.ProspectSource IS NOT NULL;
GO

PRINT '--- Limpiando columnas obsoletas y añadiendo llaves foráneas... ---';

-- 1. Eliminar las viejas columnas de texto
ALTER TABLE dbo.Deals
DROP COLUMN ProspectSource;
GO

ALTER TABLE dbo.Leads
DROP COLUMN ProspectSource;
GO

-- 2. Añadir las llaves foráneas a las nuevas columnas de ID
ALTER TABLE dbo.Deals
ADD CONSTRAINT FK_Deals_ProspectSources FOREIGN KEY (ProspectSourceId) REFERENCES dbo.ProspectSources(SourceId);
GO

ALTER TABLE dbo.Leads
ADD CONSTRAINT FK_Leads_ProspectSources FOREIGN KEY (ProspectSourceId) REFERENCES dbo.ProspectSources(SourceId);
GO


-- BLOQUE 1: CREACIÓN DE COLUMNAS (Solo Estructura)
BEGIN TRANSACTION;

PRINT '--- 1. Agregando columnas faltantes... ---';

-- Columnas estándar de Identity
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


-- BLOQUE 2: ACTUALIZACIÓN DE DATOS
BEGIN TRANSACTION;

PRINT '--- 2. Poblando datos normalizados... ---';

-- Ahora sí, como ya ejecutamos el bloque anterior, estas columnas existen.
UPDATE dbo.Users
SET 
    NormalizedUserName = UPPER(UserName),
    NormalizedEmail = UPPER(Email),
    SecurityStamp = NEWID() -- Generamos uno para evitar errores de seguridad en el login
WHERE NormalizedUserName IS NULL;

COMMIT TRANSACTION;
GO


-- BLOQUE 3: CREACIÓN DE ÍNDICES
BEGIN TRANSACTION;

PRINT '--- 3. Creando índices de rendimiento... ---';

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

PRINT '--- ¡REPARACIÓN COMPLETADA! ---';


ALTER TABLE Features 
ADD [Type] NVARCHAR(20) NOT NULL DEFAULT 'Numeric'; 

-- 2. Actualizamos AddOns para que tengan un valor base y sepamos si se suma
ALTER TABLE AddOns 
ADD [Value] DECIMAL(18,2) NOT NULL DEFAULT 0,
    [IsAdditive] BIT NOT NULL DEFAULT 1; -- 1 = Suma al plan, 0 = Solo activa/reemplaza

-- 3. Aseguramos que la compra del cliente guarde la personalización
-- Si ya existen estos campos, puedes saltar este paso
ALTER TABLE CustomerPurchasedAddOns 
ADD [Quantity] INT NOT NULL DEFAULT 1,
    [PricePaid] DECIMAL(18,2) NOT NULL DEFAULT 0;
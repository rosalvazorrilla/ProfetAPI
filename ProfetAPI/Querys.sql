--Se divide la info de user
-- 1. Asegurar llave primaria en Customers

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
        ',"hasWhatsApp":', 
        CASE WHEN ISNULL(hasWhatsApp, 0) = 1 THEN 'true' ELSE 'false' END,
        '}'
    ) AS Preferences
FROM dbo.Users;
GO
DECLARE @sql NVARCHAR(MAX) = N'';

ALTER TABLE dbo.Users DROP CONSTRAINT [DF__Users__IsAdmin__39AD8A7F];
ALTER TABLE dbo.Users DROP CONSTRAINT [DF__Users__profilePi__3C89F72A];
ALTER TABLE dbo.Users DROP CONSTRAINT [DF__Users__AlertAssi__6462DE5A];

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
WHERE NOT EXISTS (SELECT 1 FROM dbo.Accounts a WHERE a.AccountId = c.Id); -- Evitar duplicados si se corre de nuevo

SET IDENTITY_INSERT dbo.Accounts OFF;
PRINT 'Datos de Campaigns migrados a Accounts.';
GO

-- 3.2: Migrar los usuarios internos (asumiendo que los campos guardan el UserId)
INSERT INTO dbo.AccountInternalUsers (AccountId, UserId, RoleInAccount)
SELECT Id, SalesRep, 'SalesRep'
FROM dbo.Campaigns
WHERE SalesRep IS NOT NULL AND LEN(SalesRep) > 1
AND NOT EXISTS (SELECT 1 FROM dbo.AccountInternalUsers WHERE AccountId = Id AND UserId = SalesRep AND RoleInAccount = 'SalesRep');

INSERT INTO dbo.AccountInternalUsers (AccountId, UserId, RoleInAccount)
SELECT Id, AccountMgmt, 'ProjectManager'
FROM dbo.Campaigns
WHERE AccountMgmt IS NOT NULL AND LEN(AccountMgmt) > 1
AND NOT EXISTS (SELECT 1 FROM dbo.AccountInternalUsers WHERE AccountId = Id AND UserId = AccountMgmt AND RoleInAccount = 'ProjectManager');
PRINT 'Usuarios internos migrados.';
GO

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
ALTER TABLE dbo.AccountStatusHistory ADD Status NVARCHAR(50) NOT NULL DEFAULT 'Activo';
PRINT 'Tabla CampaingsActiveDates evolucionada a AccountStatusHistory.';
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

PRINT '--- PASO 6 (OPCIONAL): Limpieza Final ---';
PRINT 'Verifica que todos los datos se hayan migrado correctamente antes de ejecutar este paso.';
/*
-- 6.1: Re-cablear todas las tablas que dependían de 'Campaigns' para que apunten a 'Accounts'.
-- Ejemplo para la tabla 'Leads':
ALTER TABLE dbo.Leads ADD AccountId INT NULL;
GO
UPDATE dbo.Leads SET AccountId = CampaignId;
GO
ALTER TABLE dbo.Leads ADD CONSTRAINT FK_Leads_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts(AccountId);
GO
-- **DEBES REPETIR ESTE PATRÓN PARA TODAS LAS TABLAS DEPENDIENTES ANTES DE BORRAR CAMPAIGNS**
-- Tablas a revisar: CampaignIndustries, CampaignSettings, CampaignUsers, Reports, Webhooks, Teams, etc.


-- 6.2: Eliminar la tabla 'Campaigns' obsoleta
PRINT '*** ¡ADVERTENCIA! El siguiente paso es irreversible. ***';
-- DROP TABLE dbo.Campaigns;

*/

COMMIT TRANSACTION; -- Si todo salió bien, confirmamos los cambios.
GO

PRINT '--- Proceso completado exitosamente. ---';


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


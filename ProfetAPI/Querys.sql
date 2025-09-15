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
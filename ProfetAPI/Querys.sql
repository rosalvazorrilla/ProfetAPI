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

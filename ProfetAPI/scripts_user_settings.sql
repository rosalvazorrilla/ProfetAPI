-- Configuraciones individuales de cada usuario: foto de perfil, zona horaria y tema. Idempotente.
IF OBJECT_ID('dbo.UserSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserSettings (
        UserId      NVARCHAR(450) NOT NULL CONSTRAINT PK_UserSettings PRIMARY KEY,
        TimeZoneId  NVARCHAR(64)  NULL,
        Theme       NVARCHAR(10)  NOT NULL CONSTRAINT DF_UserSettings_Theme DEFAULT 'light',  -- light | dark | system
        AvatarUrl   NVARCHAR(500) NULL,
        UpdatedOn   DATETIME2     NOT NULL CONSTRAINT DF_UserSettings_UpdatedOn DEFAULT SYSUTCDATETIME()
    );
END
GO
SELECT OBJECT_ID('dbo.UserSettings', 'U') AS UserSettings_id;

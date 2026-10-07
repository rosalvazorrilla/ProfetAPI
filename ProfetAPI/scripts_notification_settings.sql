-- Qué alertas quiere recibir cada usuario y por qué medio (dentro del sistema y/o por correo). Idempotente.
IF OBJECT_ID('dbo.UserNotificationSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserNotificationSettings (
        UserId          NVARCHAR(450) NOT NULL CONSTRAINT PK_UserNotificationSettings PRIMARY KEY,
        DealWonSystem   BIT NOT NULL CONSTRAINT DF_UNS_DealWonSystem DEFAULT 1,
        DealWonEmail    BIT NOT NULL CONSTRAINT DF_UNS_DealWonEmail  DEFAULT 0,
        TaskDueSystem   BIT NOT NULL CONSTRAINT DF_UNS_TaskDueSystem DEFAULT 1,
        TaskDueEmail    BIT NOT NULL CONSTRAINT DF_UNS_TaskDueEmail  DEFAULT 0,
        UpdatedOn       DATETIME2 NOT NULL CONSTRAINT DF_UNS_UpdatedOn DEFAULT SYSUTCDATETIME()
    );
END
GO
SELECT OBJECT_ID('dbo.UserNotificationSettings', 'U') AS tabla_id;

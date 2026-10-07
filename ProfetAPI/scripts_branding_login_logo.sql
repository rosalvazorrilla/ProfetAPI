-- Logo de login de la plataforma (versión blanca para el fondo de color). Solo marca global de Profet:
-- los clientes con marca propia no lo heredan ni lo configuran. Idempotente.
IF COL_LENGTH('dbo.GlobalBranding', 'LoginLogoUrl') IS NULL
BEGIN
    ALTER TABLE dbo.GlobalBranding ADD LoginLogoUrl NVARCHAR(1000) NULL;
END
GO
SELECT COL_LENGTH('dbo.GlobalBranding', 'LoginLogoUrl') AS LoginLogoUrl_len;

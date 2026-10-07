-- Marca de migración al nuevo esquema de planes (Basic/Premium/Evolution).
-- IsMigrated = el cliente ya está en el nuevo sistema (tiene plan/suscripción). Idempotente.
IF COL_LENGTH('dbo.Customers', 'IsMigrated') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD IsMigrated BIT NOT NULL CONSTRAINT DF_Customers_IsMigrated DEFAULT 0;
END
GO
IF COL_LENGTH('dbo.Customers', 'MigratedOn') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD MigratedOn DATETIME2 NULL;
END
GO
-- Los que ya tienen una suscripción quedan como migrados.
UPDATE c SET IsMigrated = 1, MigratedOn = COALESCE(c.MigratedOn, SYSUTCDATETIME())
FROM dbo.Customers c
WHERE c.IsMigrated = 0
  AND EXISTS (SELECT 1 FROM dbo.Subscriptions s WHERE s.CustomerId = c.Id);
GO
SELECT SUM(CASE WHEN IsMigrated = 1 THEN 1 ELSE 0 END) AS Migrados, SUM(CASE WHEN IsMigrated = 0 THEN 1 ELSE 0 END) AS NoMigrados FROM dbo.Customers;

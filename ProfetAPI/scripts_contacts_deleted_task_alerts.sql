-- 1) Borrado lógico de contactos (el proyecto nunca borra físico).
IF COL_LENGTH('dbo.Contacts', 'Deleted') IS NULL
    ALTER TABLE dbo.Contacts ADD Deleted BIT NOT NULL CONSTRAINT DF_Contacts_Deleted DEFAULT 0;
GO
-- 2) Marcas para que el aviso de vencimiento se mande UNA vez por tarea (sin consultar la tabla Notifications,
--    que tiene ~1.4 millones de filas y ningún índice más que la llave primaria).
IF COL_LENGTH('dbo.Activities', 'DueSoonNotifiedOn') IS NULL
    ALTER TABLE dbo.Activities ADD DueSoonNotifiedOn DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.Activities', 'OverdueNotifiedOn') IS NULL
    ALTER TABLE dbo.Activities ADD OverdueNotifiedOn DATETIME2 NULL;
GO
SELECT COL_LENGTH('dbo.Contacts','Deleted') AS c_deleted, COL_LENGTH('dbo.Activities','DueSoonNotifiedOn') AS a_soon, COL_LENGTH('dbo.Activities','OverdueNotifiedOn') AS a_over;

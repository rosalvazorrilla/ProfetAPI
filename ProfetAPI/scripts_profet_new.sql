-- ============================================================================
-- scripts_profet_new.sql — ARCHIVO OFICIAL de migraciones pendientes para
-- Profet_new (reemplaza a Querys.sql, deprecado — no agregar nada nuevo ahí).
--
-- CONVENCIÓN DE TRABAJO (acordada 2026-07-17): este archivo se REESCRIBE.
-- Cuando se confirma que un tramo ya se ejecutó, se quita el DDL de aquí y
-- se deja solo su registro en el CHANGELOG de abajo — así el archivo activo
-- siempre refleja SOLO lo pendiente. (El otro archivo, scripts_profet_old.sql,
-- funciona al revés: ahí siempre se AGREGA, nunca se quita nada.)
-- ============================================================================

-- ── CHANGELOG (ya ejecutado contra Profet_new, verificado con dumps reales) ──
-- 2026-07-16/17 — Secciones A-D + E.1 + F (13 tablas faltantes, columnas de
--   AutomationRules, fix de MessagesWhatsapps.IsRead, 4 alineaciones de
--   nulabilidad, limpieza de 9 tablas huérfanas vacías, índices de
--   ScoringAnswerOptions/ScoringQuestions). Detalle completo de qué se hizo
--   y por qué: ver memoria del proyecto / historial de conversación.
--   Resultado: Profet_new quedó estructuralmente al 100% respecto a lo que
--   el código C# actual necesita.
-- 2026-07-17 — Sección G: LeadCalls → Activities+CallDetails. EJECUTADA:
--   73,985 de 75,964 filas migradas (~1,979 con Lead_id huérfano, no
--   migradas — esperado). RecordingUrl resultó ser una URL real de
--   api.callpicker.com (mejor de lo esperado, no se confirmó reproducción
--   de audio pero el formato es correcto). El texto completo de la Sección
--   G (por si hace falta releerla o adaptarla para Profet_db) sigue vivo
--   en scripts_profet_old.sql — ese archivo es append-only, no se toca.
-- 2026-07-17 — Sección H: LeadPayments → DealPayments. EJECUTADA: 80 de 93
--   filas migradas (13 sin Lead/LeadDeal válido, esperado). Confirmado con
--   COUNT(*) real. Texto completo sigue vivo en scripts_profet_old.sql.
-- 2026-07-17 — Sección I: LeadNotes → Notes. EJECUTADA: 19,764 de 19,852
--   filas migradas (88 con LeadId huérfano, esperado). Confirmado con
--   COUNT(*) real. Texto completo sigue vivo en scripts_profet_old.sql.
-- 2026-07-21 — Sección J: LeadFiles → Attachments. EJECUTADA: 112 filas
--   (de 115; 3 con LeadId huérfano). Confirmado con COUNT(*) real
--   (Attachments WHERE EntityType='Lead' = 112). Texto completo sigue vivo
--   en scripts_profet_old.sql.
-- 2026-07-21 — Sección K: Calls → Activities+CallDetails. EJECUTADA: 40
--   filas (de 54; el resto ya estaba cubierto por CallSid o sin LeadId).
--   Confirmado con COUNT(*) real (Activities ActivityType='Call' pasó de
--   73,985 a 74,025 = +40). Texto completo sigue vivo en scripts_profet_old.sql.
-- 2026-07-21 — Sección L: AccountUsers → AccountInternalUsers. Falló en el
--   primer intento (Msg 547, FK_AccountInternalUsers_Users — UserId
--   huérfanos borrados del sistema viejo); se corrigió agregando EXISTS
--   contra dbo.Users. EJECUTADA en el reintento: 590 de 594 filas migradas.
--   Confirmado con COUNT(*) real (AccountInternalUsers WHERE
--   RoleInAccount='SalesRep' = 590). Texto completo (ya corregido) sigue
--   vivo en scripts_profet_old.sql.
-- 2026-07-21 — Sección M: Webhooks → AccountWebhooks. EJECUTADA: 28 filas
--   nuevas (preservación histórica, no reactivación funcional). Confirmado
--   con COUNT(*) real. Texto completo sigue vivo en scripts_profet_old.sql.
-- 2026-07-24 — D4: Google Ads KPIs. Agregadas 3 columnas nullable a
--   dbo.Accounts (GoogleAdsCustomerId, GoogleAdsAccountName,
--   GoogleAdsRefreshTokenEncrypted). EJECUTADA (confirmado por el usuario).
--   Texto completo del DDL: ver historial de conversación / git blame de
--   este archivo.
-- 2026-08-04 — Correo de seguimiento por usuario: tabla dbo.UserEmailConfigs
--   (UserId PK, campos Smtp* igual que Accounts). EJECUTADA (confirmado por
--   el usuario). Texto completo: ver historial de conversación.
-- 2026-08-06 — Aislar campos personalizados entre clientes: columna OwnerCustomerId
--   en dbo.CustomFieldDefinitions (null = sugerencia global, con valor = privado de
--   ese cliente) + reclasificación de datos existentes. EJECUTADA (confirmado por
--   el usuario). Texto completo: ver historial de conversación.

-- ── DDL PENDIENTE DE EJECUTAR (correr contra Profet_new antes de desplegar) ──
-- 2026-08-07 — Código de acceso del wizard + contraseña por correo al activar.
-- Idempotente.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Customers') AND name = 'SetupAccessCode')
    ALTER TABLE dbo.Customers ADD SetupAccessCode NVARCHAR(10) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserProfiles') AND name = 'TempPasswordEncrypted')
    ALTER TABLE dbo.UserProfiles ADD TempPasswordEncrypted NVARCHAR(500) NULL;
GO

-- 2026-08-18 — Secuencias con estado real: checklist de Lead + gating de etapas
-- en Deal. StageId en PlaybookTasks distingue paso de fase Lead (null) de paso
-- de una etapa de Deal (con valor). GatingMode en ActivityPlaybooks define si el
-- admin configuró bloquear o solo advertir cuando hay tareas abiertas. En
-- Activities: SourcePlaybookTaskId liga la tarea real al paso de plantilla que
-- la generó, StageId denormaliza a qué etapa pertenece (para el query de
-- gating sin join extra), ResolutionNote guarda el motivo cuando el estado es
-- "Omitida" (se resolvió distinto a como se definió, pero cuenta como cerrada
-- para efectos de gating). Idempotente.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PlaybookTasks') AND name = 'StageId')
    ALTER TABLE dbo.PlaybookTasks ADD StageId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ActivityPlaybooks') AND name = 'GatingMode')
    ALTER TABLE dbo.ActivityPlaybooks ADD GatingMode NVARCHAR(20) NOT NULL DEFAULT 'Warn';
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Activities') AND name = 'SourcePlaybookTaskId')
    ALTER TABLE dbo.Activities ADD SourcePlaybookTaskId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Activities') AND name = 'StageId')
    ALTER TABLE dbo.Activities ADD StageId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Activities') AND name = 'ResolutionNote')
    ALTER TABLE dbo.Activities ADD ResolutionNote NVARCHAR(500) NULL;
GO

-- ── PENDIENTE DE DECISIÓN (no técnico, no se genera DDL hasta que se decida) ──
-- 32 tablas huérfanas en Profet_new SÍ tienen datos reales (nadie las lee hoy;
-- LeadCalls/LeadFiles/Calls/AccountUsers/Webhooks ya se sacaron de esta
-- lista, migradas arriba):
--   LeadLogs (143,957), StageLeadLogs (22,710), LeadNotes (19,852),
--   LeadAnswers (10,150), UserCharts (6,093), LeadQuestions (2,436),
--   ScoreQuestions (2,348), InboundCalls (205), LeadDeals (204),
--   LeadPackages (203), Campaigns (201), ShareLinkCharts (197),
--   FunnelColors (171), LeadDealsTypes (151), Variables (95),
--   LeadPayments (93), SelectLeads (54), DirectCalls (49),
--   LeadDealsTypesPackages (32), SellersCharts (29), LeadCommissions (20),
--   LeadRefers (19), ShareLinks (17), Charts (12), TypeActivitiys (6),
--   SellersTypeCharts (5), AccountSettings (5, OJO: nombre distinto a la
--   tabla nueva del mismo nombre, revisar si es remanente), CustomFields (3),
--   TagsActivities (3), ActivitiesTemplates (2), ToAssigns (2).
-- Nota: InboundCalls/DirectCalls (205+49=254 filas) son OTRO historial de
-- llamadas viejo, separado de LeadCalls/Calls — bloqueado (ver más abajo),
-- no tienen LeadId, solo PhoneNumber.
-- Candidatas a migrar que quedan: LeadCommissions/LeadDeals→módulo de
-- comisiones (no existe tabla destino aún, gap de roadmap distinto,
-- deprioritizado) · LeadAnswers/LeadQuestions→posible respaldo histórico
-- de scoring viejo.

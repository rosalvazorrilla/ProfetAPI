-- Planes definitivos: Basic ($59) / Premium ($99) / Evolution ($149)
-- Fuente: tabla de planes validada el 2026-10-07. Aprobado por el usuario para Profet_new.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

-- 1) Nombres de los planes
UPDATE Plans SET Name = N'Basic',     Description = N'1 cuenta · base del CRM + analista IA (5 consultas)' WHERE PlanId = 1;
UPDATE Plans SET Name = N'Premium',   Description = N'3 cuentas · automatizaciones, WhatsApp, SMTP propio, click-to-call' WHERE PlanId = 2;
UPDATE Plans SET Name = N'Evolution', Description = N'5 cuentas · todo Premium + API externa y lo próximo (flujos WhatsApp, Agent Voice)' WHERE PlanId = 3;

-- 2) Funciones nuevas (base del CRM sin candado + lo que viene)
DECLARE @new TABLE (Code NVARCHAR(100), Name NVARCHAR(200), Descr NVARCHAR(500));
INSERT INTO @new VALUES
 (N'INBOX',               N'Inbox unificado (WhatsApp + Email, resumen/sugerencia IA)', N'Incluido en todos los planes'),
 (N'TASKS_CALENDAR',      N'Tareas y Calendario',                                       N'Incluido en todos los planes'),
 (N'NOTIFICATIONS_SEARCH',N'Notificaciones y búsqueda global',                          N'Incluido en todos los planes'),
 (N'NEXT_ACTION_AI',      N'"Qué hacer ahora" (IA)',                                    N'Incluido en todos los planes'),
 (N'DASHBOARD_WIDGETS',   N'Dashboard (widgets + Best practices)',                      N'Incluido en todos los planes'),
 (N'LEAD_IMPORT',         N'Importación de leads (CSV/Excel con IA)',                   N'Incluido en todos los planes'),
 (N'ADMIN_TOOLS',         N'Administración (usuarios, equipos, catálogos, branding, logs)', N'Incluido en todos los planes'),
 (N'WHATSAPP_FLOWS',      N'Flujos conversacionales automatizados para WhatsApp (próximamente)', N'En desarrollo'),
 (N'AGENT_VOICE',         N'Agent Voice para llamadas - Seguimiento comercial Voz AI (próximamente)', N'En desarrollo');
INSERT INTO Features (FeatureCode, Name, Description, [Type])
SELECT n.Code, n.Name, n.Descr, N'Numeric' FROM @new n
WHERE NOT EXISTS (SELECT 1 FROM Features f WHERE f.FeatureCode = n.Code);

-- 3) Nombres visibles iguales a los de la tabla comercial (los códigos no cambian)
UPDATE Features SET Name = N'CRM core (Leads, Deals, Contactos, Compañías, Tags, Timeline)' WHERE FeatureCode = N'CRM';
UPDATE Features SET Name = N'Calificación (Scoring + asistente IA)'                         WHERE FeatureCode = N'LEAD_SCORING';
UPDATE Features SET Name = N'No. total de usuarios'                                          WHERE FeatureCode = N'MAX_USERS';
UPDATE Features SET Name = N'No. total de leads / mes'                                       WHERE FeatureCode = N'MAX_LEADS';
UPDATE Features SET Name = N'No. de cuentas'                                                 WHERE FeatureCode = N'MAX_ACCOUNTS';
UPDATE Features SET Name = N'Analíticas personalizadas'                                      WHERE FeatureCode = N'ANALYTICS';
UPDATE Features SET Name = N'Automatizaciones / Secuencias (WhatsApp - Email)'               WHERE FeatureCode = N'SEQUENCE_AUTOMATION';
UPDATE Features SET Name = N'Analista AI (análisis IA de prospecto) x consultas / mes'       WHERE FeatureCode = N'AI_QUANT_LEAD_SCORE';
UPDATE Features SET Name = N'WhatsApp sync - Profet'                                         WHERE FeatureCode = N'WHATSAPP_LEADS';
UPDATE Features SET Name = N'Correo (SMTP) propio'                                           WHERE FeatureCode = N'SMTP_OWN';
UPDATE Features SET Name = N'Profet Analytics avanzado'                                      WHERE FeatureCode = N'DASHBOARD_BI';
UPDATE Features SET Name = N'Click-to-call'                                                  WHERE FeatureCode = N'PHONE_LINES';
UPDATE Features SET Name = N'API externa / Webhooks'                                         WHERE FeatureCode = N'EXTERNAL_API';
UPDATE Features SET Name = N'First Contact Calls - Contact Center (llamadas agente humano)'  WHERE FeatureCode = N'FIRST_CONTACT_CALL';
UPDATE Features SET Name = N'Llamadas grabadas'                                              WHERE FeatureCode = N'CALL_RECORDING';

-- 4) Matriz Plan x Función. Presencia de fila = incluido; Limit = tope ("-1" = ilimitado).
DELETE FROM PlanFeatures WHERE PlanId IN (1,2,3);
DECLARE @m TABLE (PlanId INT, Code NVARCHAR(100), Lim NVARCHAR(100));
INSERT INTO @m VALUES
 (1,N'CRM',NULL),(2,N'CRM',NULL),(3,N'CRM',NULL),
 (1,N'LEAD_SCORING',NULL),(2,N'LEAD_SCORING',NULL),(3,N'LEAD_SCORING',NULL),
 (1,N'INBOX',NULL),(2,N'INBOX',NULL),(3,N'INBOX',NULL),
 (1,N'TASKS_CALENDAR',NULL),(2,N'TASKS_CALENDAR',NULL),(3,N'TASKS_CALENDAR',NULL),
 (1,N'NOTIFICATIONS_SEARCH',NULL),(2,N'NOTIFICATIONS_SEARCH',NULL),(3,N'NOTIFICATIONS_SEARCH',NULL),
 (1,N'NEXT_ACTION_AI',NULL),(2,N'NEXT_ACTION_AI',NULL),(3,N'NEXT_ACTION_AI',NULL),
 (1,N'DASHBOARD_WIDGETS',NULL),(2,N'DASHBOARD_WIDGETS',NULL),(3,N'DASHBOARD_WIDGETS',NULL),
 (1,N'LEAD_IMPORT',NULL),(2,N'LEAD_IMPORT',NULL),(3,N'LEAD_IMPORT',NULL),
 (1,N'ADMIN_TOOLS',NULL),(2,N'ADMIN_TOOLS',NULL),(3,N'ADMIN_TOOLS',NULL),
 (1,N'SETUP',NULL),(2,N'SETUP',NULL),(3,N'SETUP',NULL),
 (1,N'MAX_USERS',N'-1'),(2,N'MAX_USERS',N'-1'),(3,N'MAX_USERS',N'-1'),
 (1,N'MAX_LEADS',N'-1'),(2,N'MAX_LEADS',N'-1'),(3,N'MAX_LEADS',N'-1'),
 (1,N'MAX_ACCOUNTS',N'1'),(2,N'MAX_ACCOUNTS',N'3'),(3,N'MAX_ACCOUNTS',N'5'),
 (1,N'ANALYTICS',NULL),(2,N'ANALYTICS',NULL),(3,N'ANALYTICS',NULL),
 (2,N'SEQUENCE_AUTOMATION',NULL),(3,N'SEQUENCE_AUTOMATION',NULL),
 (1,N'AI_QUANT_LEAD_SCORE',N'5'),(2,N'AI_QUANT_LEAD_SCORE',N'20'),(3,N'AI_QUANT_LEAD_SCORE',N'50'),
 (2,N'WHATSAPP_LEADS',NULL),(3,N'WHATSAPP_LEADS',NULL),
 (2,N'SMTP_OWN',NULL),(3,N'SMTP_OWN',NULL),
 (1,N'META_ADS',NULL),(2,N'META_ADS',NULL),(3,N'META_ADS',NULL),
 (1,N'GOOGLE_ADS',NULL),(2,N'GOOGLE_ADS',NULL),(3,N'GOOGLE_ADS',NULL),
 (2,N'DASHBOARD_BI',NULL),(3,N'DASHBOARD_BI',NULL),
 (2,N'PHONE_LINES',NULL),(3,N'PHONE_LINES',NULL),
 (3,N'EXTERNAL_API',NULL),
 (1,N'SUPPORT',NULL),(2,N'SUPPORT',NULL),(3,N'SUPPORT',NULL),
 (2,N'FIRST_CONTACT_CALL',NULL),(3,N'FIRST_CONTACT_CALL',NULL),
 (2,N'CALL_RECORDING',NULL),(3,N'CALL_RECORDING',NULL),
 (3,N'WHATSAPP_FLOWS',NULL),(3,N'AGENT_VOICE',NULL);
INSERT INTO PlanFeatures (PlanId, FeatureId, [Limit])
SELECT m.PlanId, f.FeatureId, m.Lim FROM @m m JOIN Features f ON f.FeatureCode = m.Code;

-- 5) Precios: se cierran los vigentes y se registran los nuevos (anual = 12 x mensual)
DECLARE @now DATETIME2 = SYSUTCDATETIME();
UPDATE PlanPriceHistory SET EndDate = @now WHERE EndDate IS NULL AND PlanId IN (1,2,3);
INSERT INTO PlanPriceHistory (PlanId, MonthlyPrice, AnnualPrice, EffectiveDate, EndDate, CreatedAt) VALUES
 (1,  59.00,  708.00, @now, NULL, @now),
 (2,  99.00, 1188.00, @now, NULL, @now),
 (3, 149.00, 1788.00, @now, NULL, @now);

SELECT p.Name, COUNT(*) AS Funciones FROM PlanFeatures pf JOIN Plans p ON p.PlanId = pf.PlanId GROUP BY p.Name, p.PlanId ORDER BY p.PlanId;
COMMIT;

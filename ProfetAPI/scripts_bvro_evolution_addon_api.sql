-- 1) Complemento "API externa / Webhooks" (feature EXTERNAL_API) para poder regalarlo a clientes que ya usan webhooks.
--    Precio de catálogo PROVISIONAL (49 USD/mes): ajustarlo en /admin/planes antes de venderlo.
IF NOT EXISTS (SELECT 1 FROM AddOns a JOIN Features f ON f.FeatureId = a.FeatureId WHERE f.FeatureCode = N'EXTERNAL_API')
BEGIN
    INSERT INTO AddOns (FeatureId, Name, Description, Price, BillingCycle, [Value], IsAdditive)
    SELECT FeatureId, N'API externa / Webhooks', N'Webhooks de captura y salida, y API con llaves.', 49.00, N'Monthly', 0, 0
    FROM Features WHERE FeatureCode = N'EXTERNAL_API';
END
GO
-- 2) BVRO (cliente 47) es la cuenta base de Profet: plan Evolution, con cuentas y consultas del Analista IA sin tope.
UPDATE Subscriptions SET PlanId = 3 WHERE CustomerId = 47 AND PlanId <> 3;
DECLARE @sub INT = (SELECT TOP 1 SubscriptionId FROM Subscriptions WHERE CustomerId = 47 ORDER BY SubscriptionId DESC);
INSERT INTO SubscriptionFeatureOverrides (SubscriptionId, FeatureId, CustomLimit)
SELECT @sub, f.FeatureId, N'-1' FROM Features f
WHERE f.FeatureCode IN (N'MAX_ACCOUNTS', N'AI_QUANT_LEAD_SCORE')
  AND NOT EXISTS (SELECT 1 FROM SubscriptionFeatureOverrides o WHERE o.SubscriptionId = @sub AND o.FeatureId = f.FeatureId);
GO
SELECT s.CustomerId, p.Name AS Plan_, (SELECT COUNT(*) FROM SubscriptionFeatureOverrides o WHERE o.SubscriptionId = s.SubscriptionId) AS Overrides
FROM Subscriptions s JOIN Plans p ON p.PlanId = s.PlanId WHERE s.CustomerId = 47;
SELECT AddOnId, Name, Price FROM AddOns WHERE Name LIKE N'API externa%';

-- Read-only diagnostics on the intended database after all eight module migrations.
SET NOCOUNT ON;
SELECT DB_NAME() AS DatabaseName;
SELECT s.name AS SchemaName, t.name AS TableName FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN ('delivery', 'payments', 'notifications') ORDER BY s.name, t.name;
SELECT name, filter_definition, is_unique FROM sys.indexes
WHERE object_id = OBJECT_ID('delivery.Shipments') AND is_unique = 1;
SELECT COUNT_BIG(*) AS InvalidCashCollections FROM payments.CodPayments
WHERE Status = 'Collected' AND (DriverId IS NULL OR CollectedAt IS NULL OR CollectionOperationId IS NULL);
SELECT COUNT_BIG(*) AS InvalidDeliveredOrders FROM orders.Orders o
LEFT JOIN delivery.Shipments d ON d.OrderId = o.Id
LEFT JOIN payments.CodPayments p ON p.OrderId = o.Id
WHERE o.Status = 'Delivered' AND (o.PaymentStatus <> 'Paid' OR d.OrderId IS NULL OR d.Status <> 'Delivered'
    OR p.OrderId IS NULL OR p.Status <> 'Collected' OR p.Amount <> o.Total);
SELECT s.Id AS SettlementId, s.Amount, SUM(p.Amount) AS RecordedAmount
FROM payments.CashSettlements s LEFT JOIN payments.CodPayments p ON p.SettlementId = s.Id
GROUP BY s.Id, s.Amount HAVING SUM(p.Amount) IS NULL OR SUM(p.Amount) <> s.Amount;
SELECT COUNT_BIG(*) AS DuplicateMessageKeys FROM
(SELECT DeduplicationKey FROM notifications.Messages GROUP BY DeduplicationKey HAVING COUNT_BIG(*) > 1) invalid;

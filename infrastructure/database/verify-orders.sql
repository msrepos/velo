-- Read-only. Run against the intended velo database after both migrations.
SET NOCOUNT ON;
SELECT DB_NAME() AS DatabaseName;
SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'orders') ORDER BY name;
SELECT name, definition, is_disabled, is_not_trusted FROM sys.check_constraints
WHERE parent_object_id IN (OBJECT_ID(N'orders.Orders'), OBJECT_ID(N'orders.OrderLines'));
SELECT name, is_unique FROM sys.indexes WHERE object_id = OBJECT_ID(N'orders.Orders');
SELECT MigrationId, ProductVersion FROM inventory.__EFMigrationsHistory ORDER BY MigrationId;
SELECT MigrationId, ProductVersion FROM orders.__EFMigrationsHistory ORDER BY MigrationId;
SELECT OwnerModule, COUNT_BIG(*) AS ReservationCount FROM inventory.Reservations GROUP BY OwnerModule;
SELECT COUNT_BIG(*) AS InvalidOrderTotals FROM orders.Orders
WHERE Subtotal <= 0 OR DeliveryFee < 0 OR Total <> Subtotal + DeliveryFee;
SELECT COUNT_BIG(*) AS OrdersWithoutMatchingOwnedReservation
FROM orders.Orders o LEFT JOIN inventory.Reservations r ON r.Id = o.ReservationId
WHERE r.Id IS NULL OR r.OwnerModule <> 'Orders' OR r.CreatedBy <> o.CustomerId OR r.WarehouseId <> o.WarehouseId;
-- Pending + expired reservation can temporarily await the expiry reconciliation pass.
SELECT o.Id, o.Status AS OrderStatus, r.Status AS ReservationStatus FROM orders.Orders o
JOIN inventory.Reservations r ON r.Id = o.ReservationId
WHERE (o.Status = 'Confirmed' AND r.Status <> 'Committed') OR
      (o.Status = 'Cancelled' AND r.Status <> 'Released') OR
      (o.Status = 'Expired' AND r.Status NOT IN ('Expired', 'Released'));

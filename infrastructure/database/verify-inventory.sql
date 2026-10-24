-- Read-only verification. Run against the intended velo database.
SET NOCOUNT ON;
SELECT DB_NAME() AS DatabaseName;
SELECT t.name AS TableName
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = N'inventory' ORDER BY t.name;
SELECT name, definition, is_disabled, is_not_trusted
FROM sys.check_constraints
WHERE parent_object_id IN (OBJECT_ID(N'inventory.StockBalances'), OBJECT_ID(N'inventory.ReservationLines'));
SELECT OBJECT_NAME(object_id) AS TableName, name AS IndexName, is_unique
FROM sys.indexes WHERE object_id IN (OBJECT_ID(N'inventory.Warehouses'), OBJECT_ID(N'inventory.Reservations'))
AND is_unique = 1;
SELECT MigrationId, ProductVersion FROM inventory.__EFMigrationsHistory ORDER BY MigrationId;
SELECT COUNT_BIG(*) AS InvalidBalanceCount FROM inventory.StockBalances
WHERE OnHand < 0 OR Reserved < 0 OR Reserved > OnHand;

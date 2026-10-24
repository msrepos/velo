-- Read-only. Run against the intended velo database.
SET NOCOUNT ON;
SELECT DB_NAME() AS DatabaseName;
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN (N'customers', N'orders') ORDER BY s.name, t.name;
SELECT name, is_unique, filter_definition FROM sys.indexes
WHERE object_id = OBJECT_ID(N'customers.Addresses') AND name = N'IX_Addresses_CustomerId_IsDefault';
SELECT name, definition, is_disabled, is_not_trusted FROM sys.check_constraints
WHERE parent_object_id IN (OBJECT_ID(N'customers.Addresses'), OBJECT_ID(N'orders.CartItems'));
SELECT MigrationId, ProductVersion FROM customers.__EFMigrationsHistory ORDER BY MigrationId;
SELECT MigrationId, ProductVersion FROM orders.__EFMigrationsHistory ORDER BY MigrationId;
SELECT COUNT_BIG(*) AS CustomersWithInvalidDefaults FROM
(SELECT CustomerId FROM customers.Addresses GROUP BY CustomerId HAVING SUM(CONVERT(int, IsDefault)) <> 1) invalid;
SELECT COUNT_BIG(*) AS InvalidCartQuantities FROM orders.CartItems WHERE Quantity NOT BETWEEN 1 AND 1000;

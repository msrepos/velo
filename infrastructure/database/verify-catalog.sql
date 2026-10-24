SET NOCOUNT ON;

IF (
    SELECT COUNT(*) FROM sys.tables
    WHERE schema_id = SCHEMA_ID(N'catalog')
      AND name IN (N'Categories', N'Products', N'ProductVariants')
) <> 3
    THROW 50101, 'Catalog tables are missing.', 1;

IF (
    SELECT COUNT(*) FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.schema_id = SCHEMA_ID(N'catalog') AND c.name=N'RowVersion'
      AND c.system_type_id=189 AND c.is_nullable=0
) <> 3
    THROW 50102, 'Catalog concurrency columns are missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'[catalog].[ProductVariants]')
      AND name=N'IX_ProductVariants_Sku' AND is_unique=1
)
    THROW 50103, 'Unique SKU index is missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'[catalog].[ProductVariants]')
      AND name=N'CK_ProductVariants_Price' AND is_disabled=0
)
    THROW 50104, 'Positive price constraint is missing.', 1;

IF OBJECT_ID(N'[catalog].[__EFMigrationsHistory]') IS NULL
    THROW 50105, 'Catalog migration history is missing.', 1;

SELECT MigrationId, ProductVersion FROM [catalog].[__EFMigrationsHistory];
PRINT 'PASS: Catalog tables, rowversions, unique SKU and price constraint.';

IF OBJECT_ID(N'[catalog].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'catalog') IS NULL EXEC(N'CREATE SCHEMA [catalog];');
    CREATE TABLE [catalog].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    IF SCHEMA_ID(N'catalog') IS NULL EXEC(N'CREATE SCHEMA [catalog];');
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    CREATE TABLE [catalog].[Categories] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [NormalizedName] nvarchar(160) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    CREATE TABLE [catalog].[Products] (
        [Id] uniqueidentifier NOT NULL,
        [CategoryId] uniqueidentifier NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [ImageUrl] nvarchar(2048) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [catalog].[Categories] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    CREATE TABLE [catalog].[ProductVariants] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [Sku] varchar(64) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Barcode] nvarchar(64) NULL,
        [Price] decimal(18,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ProductVariants] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductVariants_Price] CHECK ([Price] > 0),
        CONSTRAINT [FK_ProductVariants_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [catalog].[Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_NormalizedName] ON [catalog].[Categories] ([NormalizedName]);
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryId_IsActive] ON [catalog].[Products] ([CategoryId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProductVariants_Barcode] ON [catalog].[ProductVariants] ([Barcode]) WHERE [Barcode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    CREATE INDEX [IX_ProductVariants_ProductId] ON [catalog].[ProductVariants] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariants_Sku] ON [catalog].[ProductVariants] ([Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [catalog].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023195502_InitialCatalog'
)
BEGIN
    INSERT INTO [catalog].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023195502_InitialCatalog', N'10.0.13');
END;

COMMIT;
GO


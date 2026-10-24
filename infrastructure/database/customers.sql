IF OBJECT_ID(N'[customers].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'customers') IS NULL EXEC(N'CREATE SCHEMA [customers];');
    CREATE TABLE [customers].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [customers].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223101_InitialCustomerAddresses'
)
BEGIN
    IF SCHEMA_ID(N'customers') IS NULL EXEC(N'CREATE SCHEMA [customers];');
END;

IF NOT EXISTS (
    SELECT * FROM [customers].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223101_InitialCustomerAddresses'
)
BEGIN
    CREATE TABLE [customers].[Addresses] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [Label] nvarchar(80) NOT NULL,
        [RecipientName] nvarchar(160) NOT NULL,
        [PhoneNumber] varchar(16) NOT NULL,
        [City] nvarchar(100) NOT NULL,
        [Area] nvarchar(160) NOT NULL,
        [Street] nvarchar(300) NOT NULL,
        [Building] nvarchar(80) NOT NULL,
        [Floor] nvarchar(40) NULL,
        [Apartment] nvarchar(40) NULL,
        [Instructions] nvarchar(500) NULL,
        [Latitude] decimal(9,6) NOT NULL,
        [Longitude] decimal(9,6) NOT NULL,
        [IsDefault] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Addresses] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Addresses_Coordinates] CHECK ([Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [customers].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223101_InitialCustomerAddresses'
)
BEGIN
    CREATE INDEX [IX_Addresses_CustomerId] ON [customers].[Addresses] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [customers].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223101_InitialCustomerAddresses'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Addresses_CustomerId_IsDefault] ON [customers].[Addresses] ([CustomerId], [IsDefault]) WHERE [IsDefault] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [customers].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223101_InitialCustomerAddresses'
)
BEGIN
    INSERT INTO [customers].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023223101_InitialCustomerAddresses', N'10.0.13');
END;

COMMIT;
GO


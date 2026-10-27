IF OBJECT_ID(N'[payments].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'payments') IS NULL EXEC(N'CREATE SCHEMA [payments];');
    CREATE TABLE [payments].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    IF SCHEMA_ID(N'payments') IS NULL EXEC(N'CREATE SCHEMA [payments];');
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    CREATE TABLE [payments].[CashSettlements] (
        [Id] uniqueidentifier NOT NULL,
        [DriverId] uniqueidentifier NOT NULL,
        [ActorUserId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Reference] nvarchar(160) NOT NULL,
        [Fingerprint] varchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_CashSettlements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CashSettlements_Amount] CHECK ([Amount] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    CREATE TABLE [payments].[CodPayments] (
        [OrderId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] varchar(3) NOT NULL,
        [Status] varchar(16) NOT NULL,
        [DriverId] uniqueidentifier NULL,
        [CollectionOperationId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CollectedAt] datetimeoffset NULL,
        [SettlementId] uniqueidentifier NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_CodPayments] PRIMARY KEY ([OrderId]),
        CONSTRAINT [CK_CodPayments_Amount] CHECK ([Amount] > 0),
        CONSTRAINT [FK_CodPayments_CashSettlements_SettlementId] FOREIGN KEY ([SettlementId]) REFERENCES [payments].[CashSettlements] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CashSettlements_Reference] ON [payments].[CashSettlements] ([Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CodPayments_CollectionOperationId] ON [payments].[CodPayments] ([CollectionOperationId]) WHERE [CollectionOperationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    CREATE INDEX [IX_CodPayments_CustomerId] ON [payments].[CodPayments] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    CREATE INDEX [IX_CodPayments_DriverId_SettlementId] ON [payments].[CodPayments] ([DriverId], [SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    CREATE INDEX [IX_CodPayments_SettlementId] ON [payments].[CodPayments] ([SettlementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [payments].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210523_InitialCodPayments'
)
BEGIN
    INSERT INTO [payments].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024210523_InitialCodPayments', N'10.0.13');
END;

COMMIT;
GO


IF OBJECT_ID(N'[inventory].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'inventory') IS NULL EXEC(N'CREATE SCHEMA [inventory];');
    CREATE TABLE [inventory].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    IF SCHEMA_ID(N'inventory') IS NULL EXEC(N'CREATE SCHEMA [inventory];');
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE TABLE [inventory].[Operations] (
        [Id] uniqueidentifier NOT NULL,
        [ActorUserId] uniqueidentifier NOT NULL,
        [Fingerprint] varchar(64) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Operations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE TABLE [inventory].[Warehouses] (
        [Id] uniqueidentifier NOT NULL,
        [Code] varchar(32) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Type] varchar(16) NOT NULL,
        [Address] nvarchar(500) NULL,
        [Latitude] decimal(9,6) NULL,
        [Longitude] decimal(9,6) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Warehouses] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE TABLE [inventory].[Reservations] (
        [Id] uniqueidentifier NOT NULL,
        [WarehouseId] uniqueidentifier NOT NULL,
        [Reference] nvarchar(64) NOT NULL,
        [Status] varchar(16) NOT NULL,
        [CreatedBy] uniqueidentifier NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [CompletedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Reservations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Reservations_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE TABLE [inventory].[StockBalances] (
        [WarehouseId] uniqueidentifier NOT NULL,
        [VariantId] uniqueidentifier NOT NULL,
        [OnHand] int NOT NULL,
        [Reserved] int NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_StockBalances] PRIMARY KEY ([WarehouseId], [VariantId]),
        CONSTRAINT [CK_StockBalances_Quantities] CHECK ([OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]),
        CONSTRAINT [FK_StockBalances_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [inventory].[Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE TABLE [inventory].[ReservationLines] (
        [ReservationId] uniqueidentifier NOT NULL,
        [VariantId] uniqueidentifier NOT NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_ReservationLines] PRIMARY KEY ([ReservationId], [VariantId]),
        CONSTRAINT [CK_ReservationLines_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_ReservationLines_Reservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [inventory].[Reservations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE TABLE [inventory].[StockMovements] (
        [Id] uniqueidentifier NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [WarehouseId] uniqueidentifier NOT NULL,
        [VariantId] uniqueidentifier NOT NULL,
        [Kind] varchar(32) NOT NULL,
        [OnHandDelta] int NOT NULL,
        [ReservedDelta] int NOT NULL,
        [OnHandAfter] int NOT NULL,
        [ReservedAfter] int NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [ActorUserId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_StockMovements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockMovements_StockBalances_WarehouseId_VariantId] FOREIGN KEY ([WarehouseId], [VariantId]) REFERENCES [inventory].[StockBalances] ([WarehouseId], [VariantId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Reservations_Reference] ON [inventory].[Reservations] ([Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE INDEX [IX_Reservations_Status_ExpiresAt] ON [inventory].[Reservations] ([Status], [ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE INDEX [IX_Reservations_WarehouseId] ON [inventory].[Reservations] ([WarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE INDEX [IX_StockMovements_OperationId] ON [inventory].[StockMovements] ([OperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE INDEX [IX_StockMovements_WarehouseId_VariantId_CreatedAt] ON [inventory].[StockMovements] ([WarehouseId], [VariantId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Warehouses_Code] ON [inventory].[Warehouses] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023213505_InitialInventory'
)
BEGIN
    INSERT INTO [inventory].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023213505_InitialInventory', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174146_OrderReservationOwnership'
)
BEGIN
    ALTER TABLE [inventory].[Reservations] ADD [OwnerModule] varchar(16) NOT NULL DEFAULT 'Inventory';
END;

IF NOT EXISTS (
    SELECT * FROM [inventory].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174146_OrderReservationOwnership'
)
BEGIN
    INSERT INTO [inventory].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024174146_OrderReservationOwnership', N'10.0.13');
END;

COMMIT;
GO


IF OBJECT_ID(N'[orders].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'orders') IS NULL EXEC(N'CREATE SCHEMA [orders];');
    CREATE TABLE [orders].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223107_InitialCart'
)
BEGIN
    IF SCHEMA_ID(N'orders') IS NULL EXEC(N'CREATE SCHEMA [orders];');
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223107_InitialCart'
)
BEGIN
    CREATE TABLE [orders].[Carts] (
        [CustomerId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Carts] PRIMARY KEY ([CustomerId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223107_InitialCart'
)
BEGIN
    CREATE TABLE [orders].[CartItems] (
        [CustomerId] uniqueidentifier NOT NULL,
        [VariantId] uniqueidentifier NOT NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_CartItems] PRIMARY KEY ([CustomerId], [VariantId]),
        CONSTRAINT [CK_CartItems_Quantity] CHECK ([Quantity] BETWEEN 1 AND 1000),
        CONSTRAINT [FK_CartItems_Carts_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [orders].[Carts] ([CustomerId]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023223107_InitialCart'
)
BEGIN
    INSERT INTO [orders].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023223107_InitialCart', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174153_CheckoutOrders'
)
BEGIN
    CREATE TABLE [orders].[Orders] (
        [Id] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [WarehouseId] uniqueidentifier NOT NULL,
        [ReservationId] uniqueidentifier NOT NULL,
        [Fingerprint] varchar(64) NOT NULL,
        [AddressJson] nvarchar(max) NOT NULL,
        [Status] varchar(16) NOT NULL,
        [Currency] varchar(3) NOT NULL,
        [PaymentMethod] varchar(32) NOT NULL,
        [Subtotal] decimal(18,2) NOT NULL,
        [DeliveryFee] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Orders_Totals] CHECK ([Subtotal] > 0 AND [DeliveryFee] >= 0 AND [Total] = [Subtotal] + [DeliveryFee])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174153_CheckoutOrders'
)
BEGIN
    CREATE TABLE [orders].[OrderEvents] (
        [OrderId] uniqueidentifier NOT NULL,
        [Sequence] int NOT NULL,
        [Status] varchar(16) NOT NULL,
        [ActorUserId] uniqueidentifier NULL,
        [Reason] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_OrderEvents] PRIMARY KEY ([OrderId], [Sequence]),
        CONSTRAINT [FK_OrderEvents_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [orders].[Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174153_CheckoutOrders'
)
BEGIN
    CREATE TABLE [orders].[OrderLines] (
        [OrderId] uniqueidentifier NOT NULL,
        [VariantId] uniqueidentifier NOT NULL,
        [ProductName] nvarchar(160) NOT NULL,
        [VariantName] nvarchar(160) NOT NULL,
        [Sku] varchar(64) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_OrderLines] PRIMARY KEY ([OrderId], [VariantId]),
        CONSTRAINT [CK_OrderLines_Amounts] CHECK ([Quantity] BETWEEN 1 AND 1000 AND [UnitPrice] > 0 AND [LineTotal] = [UnitPrice] * [Quantity]),
        CONSTRAINT [FK_OrderLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [orders].[Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174153_CheckoutOrders'
)
BEGIN
    CREATE INDEX [IX_Orders_CustomerId_CreatedAt] ON [orders].[Orders] ([CustomerId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174153_CheckoutOrders'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Orders_ReservationId] ON [orders].[Orders] ([ReservationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174153_CheckoutOrders'
)
BEGIN
    CREATE INDEX [IX_Orders_Status_ExpiresAt] ON [orders].[Orders] ([Status], [ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [orders].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024174153_CheckoutOrders'
)
BEGIN
    INSERT INTO [orders].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024174153_CheckoutOrders', N'10.0.13');
END;

COMMIT;
GO


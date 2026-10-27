IF OBJECT_ID(N'[delivery].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'delivery') IS NULL EXEC(N'CREATE SCHEMA [delivery];');
    CREATE TABLE [delivery].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [delivery].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210521_InitialDelivery'
)
BEGIN
    IF SCHEMA_ID(N'delivery') IS NULL EXEC(N'CREATE SCHEMA [delivery];');
END;

IF NOT EXISTS (
    SELECT * FROM [delivery].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210521_InitialDelivery'
)
BEGIN
    CREATE TABLE [delivery].[Drivers] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [IsActive] bit NOT NULL,
        [Latitude] decimal(9,6) NULL,
        [Longitude] decimal(9,6) NULL,
        [LocationAt] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Drivers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [delivery].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210521_InitialDelivery'
)
BEGIN
    CREATE TABLE [delivery].[Shipments] (
        [OrderId] uniqueidentifier NOT NULL,
        [CustomerId] uniqueidentifier NOT NULL,
        [DriverId] uniqueidentifier NOT NULL,
        [Status] varchar(24) NOT NULL,
        [DropoffJson] nvarchar(max) NOT NULL,
        [AmountDue] decimal(18,2) NOT NULL,
        [ProofProtected] nvarchar(2048) NULL,
        [ProofExpiresAt] datetimeoffset NULL,
        [ProofAttempts] int NOT NULL,
        [Attempt] int NOT NULL,
        [AssignedAt] datetimeoffset NOT NULL,
        [DispatchedAt] datetimeoffset NULL,
        [CompletedAt] datetimeoffset NULL,
        [FailureReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Shipments] PRIMARY KEY ([OrderId]),
        CONSTRAINT [FK_Shipments_Drivers_DriverId] FOREIGN KEY ([DriverId]) REFERENCES [delivery].[Drivers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [delivery].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210521_InitialDelivery'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Drivers_UserId] ON [delivery].[Drivers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [delivery].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210521_InitialDelivery'
)
BEGIN
    CREATE INDEX [IX_Shipments_CustomerId_AssignedAt] ON [delivery].[Shipments] ([CustomerId], [AssignedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [delivery].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210521_InitialDelivery'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Shipments_DriverId] ON [delivery].[Shipments] ([DriverId]) WHERE [Status] IN (''Assigned'', ''OutForDelivery'', ''Failed'')');
END;

IF NOT EXISTS (
    SELECT * FROM [delivery].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210521_InitialDelivery'
)
BEGIN
    INSERT INTO [delivery].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024210521_InitialDelivery', N'10.0.13');
END;

COMMIT;
GO


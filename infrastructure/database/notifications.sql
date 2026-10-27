IF OBJECT_ID(N'[notifications].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'notifications') IS NULL EXEC(N'CREATE SCHEMA [notifications];');
    CREATE TABLE [notifications].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [notifications].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210526_InitialNotifications'
)
BEGIN
    IF SCHEMA_ID(N'notifications') IS NULL EXEC(N'CREATE SCHEMA [notifications];');
END;

IF NOT EXISTS (
    SELECT * FROM [notifications].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210526_InitialNotifications'
)
BEGIN
    CREATE TABLE [notifications].[Messages] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [DeduplicationKey] varchar(160) NOT NULL,
        [Kind] varchar(80) NOT NULL,
        [Title] nvarchar(160) NOT NULL,
        [Body] nvarchar(1000) NOT NULL,
        [DataJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ReadAt] datetimeoffset NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [notifications].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210526_InitialNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Messages_DeduplicationKey] ON [notifications].[Messages] ([DeduplicationKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [notifications].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210526_InitialNotifications'
)
BEGIN
    CREATE INDEX [IX_Messages_UserId_CreatedAt] ON [notifications].[Messages] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [notifications].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024210526_InitialNotifications'
)
BEGIN
    INSERT INTO [notifications].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024210526_InitialNotifications', N'10.0.13');
END;

COMMIT;
GO


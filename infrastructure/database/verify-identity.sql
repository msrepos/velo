-- Read-only verification. Run against the intended velo database after migration.
SET NOCOUNT ON;

IF SCHEMA_ID(N'identity') IS NULL
    THROW 50001, 'Identity schema is missing.', 1;

IF (
    SELECT COUNT(*) FROM sys.tables
    WHERE schema_id = SCHEMA_ID(N'identity')
      AND name IN (N'Users', N'Roles', N'UserRoles', N'UserClaims',
                   N'RoleClaims', N'UserLogins', N'UserTokens')
) <> 7
    THROW 50002, 'One or more Identity tables are missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[identity].[Users]')
      AND name = N'PhoneNumber' AND is_nullable = 0 AND max_length = 32
)
    THROW 50003, 'PhoneNumber must be required nvarchar(16).', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID(N'[identity].[Users]')
      AND i.name = N'PhoneNumberIndex' AND i.is_unique = 1 AND i.is_disabled = 0
      AND i.has_filter = 0 AND c.name = N'PhoneNumber' AND ic.key_ordinal = 1
)
    THROW 50004, 'Unique phone-number index is missing.', 1;

IF OBJECT_ID(N'[identity].[__EFMigrationsHistory]') IS NULL
    THROW 50005, 'Module migration history is missing.', 1;

IF NOT EXISTS (
    SELECT 1 FROM [identity].[__EFMigrationsHistory]
    WHERE MigrationId LIKE N'%[_]InitialIdentity'
)
    THROW 50006, 'InitialIdentity has not been recorded.', 1;

IF (
    SELECT COUNT(*) FROM sys.tables
    WHERE schema_id = SCHEMA_ID(N'identity')
      AND name IN (N'OtpChallenges', N'Sessions', N'AuthThrottles', N'DataProtectionKeys')
) <> 4
    THROW 50007, 'One or more authentication tables are missing.', 1;

IF NOT EXISTS (SELECT 1 FROM [identity].[Roles] WHERE NormalizedName=N'CUSTOMER')
    THROW 50008, 'Customer role is missing.', 1;

SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = N'identity'
ORDER BY t.name;

SELECT MigrationId, ProductVersion FROM [identity].[__EFMigrationsHistory];
PRINT 'PASS: Identity/auth schema, tables, unique phone, customer role and migration history.';

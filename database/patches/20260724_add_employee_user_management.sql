/*
    QLHV_APP - Employee role and safe account-management schema.

    - Adds a persisted, case-insensitive normalized username key.
    - Adds forced-password-change, credential revocation and failed-login fields.
    - Adds database-enforced append-only, secret-free user-management audit history.
    - Seeds/localizes Admin, Employee and Viewer role definitions.
    - Extends Auto Sync trigger/actor constraints for authenticated app-open refresh.
    - Never creates a user and never contains a password or password hash.
*/

USE [QLHV_APP];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF @@TRANCOUNT <> 0
BEGIN
    THROW 527400, 'Run this patch outside an existing transaction.', 1;
END;

IF (2 & @@OPTIONS) = 2
BEGIN
    THROW 527424, 'Run this patch with IMPLICIT_TRANSACTIONS OFF.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_User', N'U') IS NULL
       OR OBJECT_ID(N'dbo.App_Role', N'U') IS NULL
       OR OBJECT_ID(N'dbo.App_UserRole', N'U') IS NULL
    BEGIN
        THROW 527401, 'Apply the App_User authorization patch before Employee user management.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_User', N'MustChangePassword') IS NULL
    BEGIN
        ALTER TABLE dbo.App_User
            ADD MustChangePassword bit NOT NULL
                CONSTRAINT DF_App_User_MustChangePassword DEFAULT (0) WITH VALUES;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS columnRow
        INNER JOIN sys.types AS typeRow
            ON typeRow.user_type_id = columnRow.user_type_id
        WHERE columnRow.object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND columnRow.name = N'MustChangePassword'
          AND typeRow.name = N'bit'
          AND columnRow.is_computed = 0
          AND columnRow.generated_always_type = 0
          AND columnRow.is_nullable = 0
    )
    BEGIN
        THROW 527402, 'dbo.App_User.MustChangePassword has an incompatible schema.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND name = N'MustChangePassword'
          AND is_computed = 0
          AND default_object_id <> 0
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_User
                ADD CONSTRAINT DF_App_User_MustChangePassword
                DEFAULT (0) FOR MustChangePassword;';
    END;

    IF
    (
        SELECT LOWER(REPLACE(REPLACE(REPLACE(
            OBJECT_DEFINITION(default_object_id),
            N'(', N''), N')', N''), N' ', N''))
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND name = N'MustChangePassword'
    ) <> N'0'
    BEGIN
        THROW 527409, 'dbo.App_User.MustChangePassword must default to 0.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_User', N'LastFailedLoginAt') IS NULL
    BEGIN
        ALTER TABLE dbo.App_User
            ADD LastFailedLoginAt datetime2(7) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS columnRow
        INNER JOIN sys.types AS typeRow
            ON typeRow.user_type_id = columnRow.user_type_id
        WHERE columnRow.object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND columnRow.name = N'LastFailedLoginAt'
          AND typeRow.name = N'datetime2'
          AND columnRow.scale = 7
          AND columnRow.is_computed = 0
          AND columnRow.is_nullable = 1
    )
    BEGIN
        THROW 527403, 'dbo.App_User.LastFailedLoginAt has an incompatible schema.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_User', N'SecurityStamp') IS NULL
    BEGIN
        ALTER TABLE dbo.App_User
            ADD SecurityStamp uniqueidentifier NOT NULL
                CONSTRAINT DF_App_User_SecurityStamp DEFAULT (NEWID()) WITH VALUES;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS columnRow
        INNER JOIN sys.types AS typeRow
            ON typeRow.user_type_id = columnRow.user_type_id
        WHERE columnRow.object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND columnRow.name = N'SecurityStamp'
          AND typeRow.name = N'uniqueidentifier'
          AND columnRow.is_computed = 0
          AND columnRow.is_nullable = 0
    )
    BEGIN
        THROW 527407, 'dbo.App_User.SecurityStamp has an incompatible schema.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND name = N'SecurityStamp'
          AND is_computed = 0
          AND default_object_id <> 0
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_User
                ADD CONSTRAINT DF_App_User_SecurityStamp
                DEFAULT (NEWID()) FOR SecurityStamp;';
    END;

    IF
    (
        SELECT LOWER(REPLACE(REPLACE(REPLACE(
            OBJECT_DEFINITION(default_object_id),
            N'(', N''), N')', N''), N' ', N''))
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND name = N'SecurityStamp'
    ) NOT IN (N'newid', N'newsequentialid')
    BEGIN
        THROW 527410, 'dbo.App_User.SecurityStamp must have a nonzero GUID default.', 1;
    END;

    EXEC sys.sp_executesql N'
        UPDATE dbo.App_User
        SET SecurityStamp = NEWID()
        WHERE SecurityStamp =
            ''00000000-0000-0000-0000-000000000000'';

        IF EXISTS
        (
            SELECT 1
            FROM dbo.App_User
            WHERE SecurityStamp =
                ''00000000-0000-0000-0000-000000000000''
        )
        BEGIN
            THROW 527413, ''dbo.App_User contains an invalid SecurityStamp.'', 1;
        END;';

    IF OBJECT_ID(N'dbo.CK_App_User_SecurityStamp_NotEmpty', N'C') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_User
                DROP CONSTRAINT CK_App_User_SecurityStamp_NotEmpty;';
    END;

    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.App_User WITH CHECK
            ADD CONSTRAINT CK_App_User_SecurityStamp_NotEmpty CHECK
            (
                SecurityStamp <>
                    ''00000000-0000-0000-0000-000000000000''
            );
        ALTER TABLE dbo.App_User
            WITH CHECK CHECK CONSTRAINT CK_App_User_SecurityStamp_NotEmpty;';

    IF EXISTS
    (
        SELECT
            UPPER(LTRIM(RTRIM(UserName)))
                COLLATE Latin1_General_100_CI_AS AS NormalizedUserName
        FROM dbo.App_User
        GROUP BY
            UPPER(LTRIM(RTRIM(UserName)))
                COLLATE Latin1_General_100_CI_AS
        HAVING COUNT_BIG(*) > 1
    )
    BEGIN
        THROW 527404, 'Existing usernames collide after case-insensitive normalization.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.App_User
        WHERE NULLIF(LTRIM(RTRIM(UserName)), N'') IS NULL
    )
    BEGIN
        THROW 527405, 'Existing usernames contain an empty normalized value.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.App_User
        WHERE DATALENGTH(UserName) / 2 NOT BETWEEN 3 AND 100
           OR LEFT(UserName, 1)
                    COLLATE Latin1_General_100_BIN2
                NOT LIKE N'[A-Za-z0-9]'
           OR CHARINDEX(
                    N' ',
                    UserName COLLATE Latin1_General_100_BIN2) > 0
           OR UserName
                    COLLATE Latin1_General_100_BIN2
                LIKE N'%[^A-Za-z0-9._-]%'
    )
    BEGIN
        THROW 527432, 'Existing usernames contain unsupported characters or lengths.', 1;
    END;

    IF OBJECT_ID(N'dbo.CK_App_User_UserName_NotBlank', N'C') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_User
                DROP CONSTRAINT CK_App_User_UserName_NotBlank;';
    END;

    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.App_User WITH CHECK
            ADD CONSTRAINT CK_App_User_UserName_NotBlank CHECK
            (
                DATALENGTH(UserName) / 2 BETWEEN 3 AND 100
                AND LEFT(UserName, 1)
                        COLLATE Latin1_General_100_BIN2
                    LIKE N''[A-Za-z0-9]''
                AND CHARINDEX(
                        N'' '',
                        UserName COLLATE Latin1_General_100_BIN2) = 0
                AND UserName
                        COLLATE Latin1_General_100_BIN2
                    NOT LIKE N''%[^A-Za-z0-9._-]%''
            );
        ALTER TABLE dbo.App_User
            WITH CHECK CHECK CONSTRAINT CK_App_User_UserName_NotBlank;';

    IF COL_LENGTH(N'dbo.App_User', N'NormalizedUserName') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_User
                ADD NormalizedUserName AS
                    (
                        UPPER(LTRIM(RTRIM([UserName])))
                            COLLATE Latin1_General_100_CI_AS
                    ) PERSISTED;';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.computed_columns AS computedColumn
        WHERE computedColumn.object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND computedColumn.name = N'NormalizedUserName'
          AND computedColumn.is_persisted = 1
          AND computedColumn.is_nullable = 1
          AND computedColumn.max_length = 200
          AND computedColumn.collation_name = N'Latin1_General_100_CI_AS'
          AND COLUMNPROPERTY(
                  computedColumn.object_id,
                  computedColumn.name,
                  'IsDeterministic') = 1
          AND COLUMNPROPERTY(
                  computedColumn.object_id,
                  computedColumn.name,
                  'IsPrecise') = 1
    )
    BEGIN
        THROW 527406, 'dbo.App_User.NormalizedUserName has an incompatible schema.', 1;
    END;

    IF
    (
        SELECT LOWER(
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                definition,
                N' ', N''),
                N'[', N''),
                N']', N''),
                N'(', N''),
                N')', N''))
        FROM sys.computed_columns
        WHERE object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND name = N'NormalizedUserName'
    ) <> N'upperltrimrtrimusername'
          + N'collatelatin1_general_100_ci_as'
    BEGIN
        THROW 527411, 'dbo.App_User.NormalizedUserName has an unexpected definition.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexRow
        INNER JOIN sys.index_columns AS keyColumn
            ON keyColumn.object_id = indexRow.object_id
           AND keyColumn.index_id = indexRow.index_id
           AND keyColumn.key_ordinal = 1
        INNER JOIN sys.columns AS columnRow
            ON columnRow.object_id = keyColumn.object_id
           AND columnRow.column_id = keyColumn.column_id
        WHERE indexRow.object_id = OBJECT_ID(N'dbo.App_User', N'U')
          AND indexRow.is_unique = 1
          AND indexRow.is_disabled = 0
          AND indexRow.is_hypothetical = 0
          AND indexRow.has_filter = 0
          AND indexRow.filter_definition IS NULL
          AND indexRow.type = 2
          AND columnRow.name = N'NormalizedUserName'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS extraKey
              WHERE extraKey.object_id = indexRow.object_id
                AND extraKey.index_id = indexRow.index_id
                AND extraKey.key_ordinal > 1
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.App_User', N'U')
              AND name = N'UX_App_User_NormalizedUserName'
        )
        BEGIN
            THROW 527412, 'UX_App_User_NormalizedUserName exists with an incompatible definition.', 1;
        END;

        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX UX_App_User_NormalizedUserName
                ON dbo.App_User (NormalizedUserName);';
    END;

    IF OBJECT_ID(N'dbo.App_UserManagementAudit', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_UserManagementAudit
        (
            UserManagementAuditId bigint IDENTITY(1, 1) NOT NULL
                CONSTRAINT PK_App_UserManagementAudit PRIMARY KEY,
            TargetUserId bigint NOT NULL,
            ActorUserId bigint NOT NULL,
            ActorUsername nvarchar(100) NOT NULL,
            ActionCode nvarchar(30) NOT NULL,
            NewRole nvarchar(50) NULL,
            NewIsActive bit NULL,
            NewMustChangePassword bit NULL,
            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_UserManagementAudit_CreatedAtUtc
                DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT CK_App_UserManagementAudit_ActionCode CHECK
            (
                ActionCode IN
                (
                    N'CREATE',
                    N'UPDATE',
                    N'RESET_PASSWORD',
                    N'CHANGE_PASSWORD'
                )
            ),
            CONSTRAINT FK_App_UserManagementAudit_TargetUser
                FOREIGN KEY (TargetUserId) REFERENCES dbo.App_User (UserId),
            CONSTRAINT FK_App_UserManagementAudit_ActorUser
                FOREIGN KEY (ActorUserId) REFERENCES dbo.App_User (UserId)
        );
    END;

    IF
    (
        SELECT COUNT_BIG(*)
        FROM sys.columns AS columnRow
        INNER JOIN sys.types AS typeRow
            ON typeRow.user_type_id = columnRow.user_type_id
        WHERE columnRow.object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND columnRow.is_computed = 0
          AND columnRow.generated_always_type = 0
          AND
          (
              (columnRow.name = N'UserManagementAuditId'
               AND typeRow.name = N'bigint'
               AND columnRow.max_length = 8
               AND columnRow.is_nullable = 0
               AND columnRow.is_identity = 1)
              OR
              (columnRow.name IN (N'TargetUserId', N'ActorUserId')
               AND typeRow.name = N'bigint'
               AND columnRow.max_length = 8
               AND columnRow.is_nullable = 0
               AND columnRow.is_identity = 0)
              OR
              (columnRow.name = N'ActorUsername'
               AND typeRow.name = N'nvarchar'
               AND columnRow.max_length = 200
               AND columnRow.is_nullable = 0)
              OR
              (columnRow.name = N'ActionCode'
               AND typeRow.name = N'nvarchar'
               AND columnRow.max_length = 60
               AND columnRow.is_nullable = 0)
              OR
              (columnRow.name = N'NewRole'
               AND typeRow.name = N'nvarchar'
               AND columnRow.max_length = 100
               AND columnRow.is_nullable = 1)
              OR
              (columnRow.name IN
                    (N'NewIsActive', N'NewMustChangePassword')
               AND typeRow.name = N'bit'
               AND columnRow.max_length = 1
               AND columnRow.is_nullable = 1)
              OR
              (columnRow.name = N'CreatedAtUtc'
               AND typeRow.name = N'datetime2'
               AND columnRow.scale = 7
               AND columnRow.is_nullable = 0)
          )
    ) <> 9
    BEGIN
        THROW 527408, 'dbo.App_UserManagementAudit has an incompatible schema.', 1;
    END;

    IF
    (
        SELECT COUNT_BIG(*)
        FROM sys.columns
        WHERE object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
    ) <> 9
    BEGIN
        THROW 527425, 'dbo.App_UserManagementAudit contains unexpected columns.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexRow
        INNER JOIN sys.index_columns AS keyColumn
            ON keyColumn.object_id = indexRow.object_id
           AND keyColumn.index_id = indexRow.index_id
           AND keyColumn.key_ordinal = 1
        INNER JOIN sys.columns AS columnRow
            ON columnRow.object_id = keyColumn.object_id
           AND columnRow.column_id = keyColumn.column_id
        WHERE indexRow.object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND indexRow.is_primary_key = 1
          AND indexRow.is_unique = 1
          AND indexRow.is_disabled = 0
          AND columnRow.name = N'UserManagementAuditId'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS extraKey
              WHERE extraKey.object_id = indexRow.object_id
                AND extraKey.index_id = indexRow.index_id
                AND extraKey.key_ordinal > 1
          )
    )
    BEGIN
        THROW 527414, 'dbo.App_UserManagementAudit must have the expected primary key.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS foreignKey
        INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
            ON foreignKeyColumn.constraint_object_id =
                foreignKey.object_id
        WHERE foreignKey.parent_object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND foreignKey.referenced_object_id =
                OBJECT_ID(N'dbo.App_User', N'U')
          AND foreignKey.is_disabled = 0
          AND foreignKey.is_not_trusted = 0
          AND COL_NAME(
                  foreignKey.parent_object_id,
                  foreignKeyColumn.parent_column_id) = N'TargetUserId'
          AND COL_NAME(
                  foreignKey.referenced_object_id,
                  foreignKeyColumn.referenced_column_id) = N'UserId'
    )
    BEGIN
        THROW 527415, 'dbo.App_UserManagementAudit.TargetUserId foreign key is missing or untrusted.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS foreignKey
        INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
            ON foreignKeyColumn.constraint_object_id =
                foreignKey.object_id
        WHERE foreignKey.parent_object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND foreignKey.referenced_object_id =
                OBJECT_ID(N'dbo.App_User', N'U')
          AND foreignKey.is_disabled = 0
          AND foreignKey.is_not_trusted = 0
          AND COL_NAME(
                  foreignKey.parent_object_id,
                  foreignKeyColumn.parent_column_id) = N'ActorUserId'
          AND COL_NAME(
                  foreignKey.referenced_object_id,
                  foreignKeyColumn.referenced_column_id) = N'UserId'
    )
    BEGIN
        THROW 527416, 'dbo.App_UserManagementAudit.ActorUserId foreign key is missing or untrusted.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND name = N'CreatedAtUtc'
          AND default_object_id <> 0
    )
    BEGIN
        THROW 527426, 'dbo.App_UserManagementAudit.CreatedAtUtc default is missing.', 1;
    END;

    IF
    (
        SELECT LOWER(REPLACE(REPLACE(REPLACE(
            OBJECT_DEFINITION(default_object_id),
            N'(', N''), N')', N''), N' ', N''))
        FROM sys.columns
        WHERE object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND name = N'CreatedAtUtc'
    ) <> N'sysutcdatetime'
    BEGIN
        THROW 527417, 'dbo.App_UserManagementAudit.CreatedAtUtc must default to SYSUTCDATETIME().', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND name <> N'CK_App_UserManagementAudit_ActionCode'
          AND LOWER(definition) LIKE N'%actioncode%'
    )
    BEGIN
        THROW 527418, 'dbo.App_UserManagementAudit has an unknown ActionCode constraint.', 1;
    END;

    IF OBJECT_ID(N'dbo.CK_App_UserManagementAudit_ActionCode', N'C') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_UserManagementAudit
                DROP CONSTRAINT CK_App_UserManagementAudit_ActionCode;';
    END;

    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.App_UserManagementAudit WITH CHECK
            ADD CONSTRAINT CK_App_UserManagementAudit_ActionCode CHECK
            (
                ActionCode IN
                (
                    N''CREATE'',
                    N''UPDATE'',
                    N''RESET_PASSWORD'',
                    N''CHANGE_PASSWORD''
                )
            );
        ALTER TABLE dbo.App_UserManagementAudit
            WITH CHECK CHECK CONSTRAINT
                CK_App_UserManagementAudit_ActionCode;';

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND name = N'IX_App_UserManagementAudit_Target_Created'
          AND
          (
              is_disabled = 1
              OR is_hypothetical = 1
              OR has_filter = 1
              OR is_unique = 1
          )
    )
    BEGIN
        THROW 527419, 'IX_App_UserManagementAudit_Target_Created has an incompatible definition.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexRow
        WHERE indexRow.object_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND indexRow.name =
                N'IX_App_UserManagementAudit_Target_Created'
          AND indexRow.is_disabled = 0
          AND indexRow.is_hypothetical = 0
          AND indexRow.has_filter = 0
          AND indexRow.is_unique = 0
          AND
          (
              SELECT COUNT_BIG(*)
              FROM sys.index_columns
              WHERE object_id = indexRow.object_id
                AND index_id = indexRow.index_id
                AND key_ordinal > 0
          ) = 3
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS keyColumn
              INNER JOIN sys.columns AS columnRow
                  ON columnRow.object_id = keyColumn.object_id
                 AND columnRow.column_id = keyColumn.column_id
              WHERE keyColumn.object_id = indexRow.object_id
                AND keyColumn.index_id = indexRow.index_id
                AND keyColumn.key_ordinal = 1
                AND keyColumn.is_descending_key = 0
                AND columnRow.name = N'TargetUserId'
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS keyColumn
              INNER JOIN sys.columns AS columnRow
                  ON columnRow.object_id = keyColumn.object_id
                 AND columnRow.column_id = keyColumn.column_id
              WHERE keyColumn.object_id = indexRow.object_id
                AND keyColumn.index_id = indexRow.index_id
                AND keyColumn.key_ordinal = 2
                AND keyColumn.is_descending_key = 1
                AND columnRow.name = N'CreatedAtUtc'
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS keyColumn
              INNER JOIN sys.columns AS columnRow
                  ON columnRow.object_id = keyColumn.object_id
                 AND columnRow.column_id = keyColumn.column_id
              WHERE keyColumn.object_id = indexRow.object_id
                AND keyColumn.index_id = indexRow.index_id
                AND keyColumn.key_ordinal = 3
                AND keyColumn.is_descending_key = 1
                AND columnRow.name = N'UserManagementAuditId'
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns
              WHERE object_id = indexRow.object_id
                AND index_id = indexRow.index_id
                AND is_included_column = 1
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id =
                    OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
              AND name =
                    N'IX_App_UserManagementAudit_Target_Created'
        )
        BEGIN
            THROW 527420, 'IX_App_UserManagementAudit_Target_Created key order is incompatible.', 1;
        END;

        EXEC sys.sp_executesql N'
            CREATE INDEX IX_App_UserManagementAudit_Target_Created
                ON dbo.App_UserManagementAudit
                    (
                        TargetUserId,
                        CreatedAtUtc DESC,
                        UserManagementAuditId DESC
                    );';
    END;

    EXEC sys.sp_executesql N'
        CREATE OR ALTER TRIGGER
            dbo.TR_App_UserManagementAudit_AppendOnly
        ON dbo.App_UserManagementAudit
        INSTEAD OF UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 527430,
                ''App_UserManagementAudit is append-only.'',
                1;
        END;';

    EXEC sys.sp_executesql N'
        ENABLE TRIGGER TR_App_UserManagementAudit_AppendOnly
            ON dbo.App_UserManagementAudit;';

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.triggers
        WHERE parent_id =
                OBJECT_ID(N'dbo.App_UserManagementAudit', N'U')
          AND name = N'TR_App_UserManagementAudit_AppendOnly'
          AND is_disabled = 0
    )
    BEGIN
        THROW 527431, 'App_UserManagementAudit append-only trigger is unavailable.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode NOT IN (N'Admin', N'Employee', N'Viewer')
    )
    BEGIN
        THROW 527433, 'dbo.App_Role contains an unsupported RoleCode; no role rows were changed.', 1;
    END;

    DECLARE @AppRoleObjectId int =
        OBJECT_ID(N'dbo.App_Role', N'U');
    DECLARE @RoleCodeColumnId int =
        COLUMNPROPERTY(
            @AppRoleObjectId,
            N'RoleCode',
            N'ColumnId');

    IF @RoleCodeColumnId IS NULL
    BEGIN
        THROW 527434, 'dbo.App_Role.RoleCode is unavailable.', 1;
    END;

    IF OBJECT_ID(N'dbo.CK_App_Role_RoleCode_Allowed') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.check_constraints
           WHERE object_id =
                   OBJECT_ID(N'dbo.CK_App_Role_RoleCode_Allowed')
             AND parent_object_id = @AppRoleObjectId
       )
    BEGIN
        THROW 527435, 'CK_App_Role_RoleCode_Allowed is occupied by an incompatible object.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS constraintRow
        WHERE constraintRow.parent_object_id = @AppRoleObjectId
          AND
          (
              constraintRow.parent_column_id = @RoleCodeColumnId
              OR EXISTS
              (
                  SELECT 1
                  FROM sys.sql_expression_dependencies AS dependencyRow
                  WHERE dependencyRow.referencing_id =
                          constraintRow.object_id
                    AND dependencyRow.referenced_id =
                          @AppRoleObjectId
                    AND dependencyRow.referenced_minor_id =
                          @RoleCodeColumnId
              )
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.sql_expression_dependencies AS otherDependencyRow
              WHERE otherDependencyRow.referencing_id =
                      constraintRow.object_id
                AND otherDependencyRow.referenced_id =
                      @AppRoleObjectId
                AND otherDependencyRow.referenced_minor_id > 0
                AND otherDependencyRow.referenced_minor_id <>
                      @RoleCodeColumnId
          )
    )
    BEGIN
        THROW 527436, 'An App_Role check combines RoleCode with other columns; review it before applying this patch.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS constraintRow
        WHERE constraintRow.parent_object_id = @AppRoleObjectId
          AND LOWER(constraintRow.definition) LIKE N'%rolecode%'
          AND constraintRow.parent_column_id <> @RoleCodeColumnId
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.sql_expression_dependencies AS dependencyRow
              WHERE dependencyRow.referencing_id =
                      constraintRow.object_id
                AND dependencyRow.referenced_id =
                      @AppRoleObjectId
                AND dependencyRow.referenced_minor_id =
                      @RoleCodeColumnId
          )
    )
    BEGIN
        THROW 527437, 'An App_Role RoleCode check could not be validated safely.', 1;
    END;

    DECLARE @DropRoleCodeConstraintsSql nvarchar(max) = N'';

    SELECT @DropRoleCodeConstraintsSql =
        @DropRoleCodeConstraintsSql
        + N'ALTER TABLE dbo.App_Role DROP CONSTRAINT '
        + QUOTENAME(constraintRow.name)
        + N';'
    FROM sys.check_constraints AS constraintRow
    WHERE constraintRow.parent_object_id = @AppRoleObjectId
      AND
      (
          constraintRow.parent_column_id = @RoleCodeColumnId
          OR EXISTS
          (
              SELECT 1
              FROM sys.sql_expression_dependencies AS dependencyRow
              WHERE dependencyRow.referencing_id =
                      constraintRow.object_id
                AND dependencyRow.referenced_id =
                      @AppRoleObjectId
                AND dependencyRow.referenced_minor_id =
                      @RoleCodeColumnId
          )
      )
    ORDER BY constraintRow.name;

    IF @DropRoleCodeConstraintsSql <> N''
    BEGIN
        EXEC sys.sp_executesql @DropRoleCodeConstraintsSql;
    END;

    ALTER TABLE dbo.App_Role WITH CHECK
        ADD CONSTRAINT CK_App_Role_RoleCode_Allowed
        CHECK (RoleCode IN (N'Admin', N'Employee', N'Viewer'));

    ALTER TABLE dbo.App_Role
        WITH CHECK CHECK CONSTRAINT CK_App_Role_RoleCode_Allowed;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = @AppRoleObjectId
          AND name = N'CK_App_Role_RoleCode_Allowed'
          AND is_disabled = 0
          AND is_not_trusted = 0
    )
    BEGIN
        THROW 527438, 'CK_App_Role_RoleCode_Allowed is unavailable or untrusted.', 1;
    END;

    INSERT INTO dbo.App_Role
        (RoleCode, RoleName, MoTa, IsDeleted, CreatedAt, CreatedBy)
    SELECT
        N'Admin',
        N'Quản trị viên',
        N'Toàn quyền quản trị và vận hành QLHV.',
        0,
        SYSUTCDATETIME(),
        N'employee-user-management-patch'
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = N'Admin'
    );

    INSERT INTO dbo.App_Role
        (RoleCode, RoleName, MoTa, IsDeleted, CreatedAt, CreatedBy)
    SELECT
        N'Employee',
        N'Nhân viên',
        N'Xem, cập nhật và in/xuất dữ liệu nghiệp vụ; không có quyền quản trị.',
        0,
        SYSUTCDATETIME(),
        N'employee-user-management-patch'
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = N'Employee'
    );

    INSERT INTO dbo.App_Role
        (RoleCode, RoleName, MoTa, IsDeleted, CreatedAt, CreatedBy)
    SELECT
        N'Viewer',
        N'Chỉ xem',
        N'Chỉ xem dữ liệu và trạng thái QLHV.',
        0,
        SYSUTCDATETIME(),
        N'employee-user-management-patch'
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = N'Viewer'
    );

    IF OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U') IS NOT NULL
       AND COL_LENGTH(
               N'dbo.App_QlhvSyncOperationHistory',
               N'Actor') IS NOT NULL
    BEGIN
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.columns AS columnRow
            INNER JOIN sys.types AS typeRow
                ON typeRow.user_type_id = columnRow.user_type_id
            WHERE columnRow.object_id =
                    OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
              AND columnRow.name = N'Actor'
              AND typeRow.name = N'nvarchar'
              AND columnRow.max_length = 200
              AND columnRow.is_nullable = 0
              AND columnRow.is_computed = 0
        )
        BEGIN
            THROW 527427, 'App_QlhvSyncOperationHistory.Actor has an incompatible schema.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE parent_object_id =
                    OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
              AND name <>
                    N'CK_App_QlhvSyncOperationHistory_Actor'
              AND LOWER(definition) LIKE N'%actor%'
        )
        BEGIN
            THROW 527421, 'App_QlhvSyncOperationHistory has an unknown Actor constraint.', 1;
        END;

        IF OBJECT_ID(
               N'dbo.CK_App_QlhvSyncOperationHistory_Actor',
               N'C') IS NOT NULL
        BEGIN
            EXEC sys.sp_executesql N'
                ALTER TABLE dbo.App_QlhvSyncOperationHistory
                    DROP CONSTRAINT
                        CK_App_QlhvSyncOperationHistory_Actor;';
        END;

        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_QlhvSyncOperationHistory WITH CHECK
                ADD CONSTRAINT CK_App_QlhvSyncOperationHistory_Actor
                CHECK
                (
                    Actor IN
                    (
                        N''MANUAL_ADMIN'',
                        N''SYSTEM_AUTO_SYNC'',
                        N''SYSTEM_SESSION_START'',
                        N''SYSTEM_APP_OPEN''
                    )
                );
            ALTER TABLE dbo.App_QlhvSyncOperationHistory
                WITH CHECK CHECK CONSTRAINT
                    CK_App_QlhvSyncOperationHistory_Actor;';
    END;

    IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NOT NULL
       AND COL_LENGTH(
               N'dbo.App_QlhvAutoSyncRun',
               N'TriggerType') IS NOT NULL
    BEGIN
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.columns AS columnRow
            INNER JOIN sys.types AS typeRow
                ON typeRow.user_type_id = columnRow.user_type_id
            WHERE columnRow.object_id =
                    OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND columnRow.name = N'TriggerType'
              AND typeRow.name = N'nvarchar'
              AND columnRow.max_length = 40
              AND columnRow.is_nullable = 0
              AND columnRow.is_computed = 0
        )
        BEGIN
            THROW 527428, 'App_QlhvAutoSyncRun.TriggerType has an incompatible schema.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE parent_object_id =
                    OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND name <> N'CK_App_QlhvAutoSyncRun_TriggerType'
              AND LOWER(definition) LIKE N'%triggertype%'
        )
        BEGIN
            THROW 527422, 'App_QlhvAutoSyncRun has an unknown TriggerType constraint.', 1;
        END;

        IF OBJECT_ID(N'dbo.CK_App_QlhvAutoSyncRun_TriggerType', N'C') IS NOT NULL
        BEGIN
            EXEC sys.sp_executesql N'
                ALTER TABLE dbo.App_QlhvAutoSyncRun
                    DROP CONSTRAINT CK_App_QlhvAutoSyncRun_TriggerType;';
        END;

        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_QlhvAutoSyncRun WITH CHECK
                ADD CONSTRAINT CK_App_QlhvAutoSyncRun_TriggerType
                CHECK
                (
                    TriggerType IN
                    (
                        N''STARTUP'',
                        N''MANUAL'',
                        N''SESSION_START'',
                        N''APP_OPEN''
                    )
                );
            ALTER TABLE dbo.App_QlhvAutoSyncRun
                WITH CHECK CHECK CONSTRAINT
                    CK_App_QlhvAutoSyncRun_TriggerType;';
    END;

    IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.App_QlhvAutoSyncRun', N'Actor') IS NOT NULL
    BEGIN
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.columns AS columnRow
            INNER JOIN sys.types AS typeRow
                ON typeRow.user_type_id = columnRow.user_type_id
            WHERE columnRow.object_id =
                    OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND columnRow.name = N'Actor'
              AND typeRow.name = N'nvarchar'
              AND columnRow.max_length = 200
              AND columnRow.is_nullable = 0
              AND columnRow.is_computed = 0
        )
        BEGIN
            THROW 527429, 'App_QlhvAutoSyncRun.Actor has an incompatible schema.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE parent_object_id =
                    OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND name <> N'CK_App_QlhvAutoSyncRun_Actor'
              AND LOWER(definition) LIKE N'%actor%'
        )
        BEGIN
            THROW 527423, 'App_QlhvAutoSyncRun has an unknown Actor constraint.', 1;
        END;

        IF OBJECT_ID(N'dbo.CK_App_QlhvAutoSyncRun_Actor', N'C') IS NOT NULL
        BEGIN
            EXEC sys.sp_executesql N'
                ALTER TABLE dbo.App_QlhvAutoSyncRun
                    DROP CONSTRAINT CK_App_QlhvAutoSyncRun_Actor;';
        END;

        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.App_QlhvAutoSyncRun WITH CHECK
                ADD CONSTRAINT CK_App_QlhvAutoSyncRun_Actor
                CHECK
                (
                    Actor IN
                    (
                        N''MANUAL_ADMIN'',
                        N''SYSTEM_AUTO_SYNC'',
                        N''SYSTEM_SESSION_START'',
                        N''SYSTEM_APP_OPEN''
                    )
                );
            ALTER TABLE dbo.App_QlhvAutoSyncRun
                WITH CHECK CHECK CONSTRAINT
                    CK_App_QlhvAutoSyncRun_Actor;';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO

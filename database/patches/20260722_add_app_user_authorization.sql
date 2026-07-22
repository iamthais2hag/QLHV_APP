/*
    QLHV_APP - minimal application account and role store.

    - Idempotently creates or validates dbo.App_User, dbo.App_Role and dbo.App_UserRole.
    - Seeds only the Admin and Viewer role definitions.
    - Never creates an application user and never contains a password or password hash.
    - Run the separate --seed-admin command once after this patch when an initial Admin is needed.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

USE [QLHV_APP];
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_Role', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_Role
        (
            RoleId int IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_Role PRIMARY KEY,
            RoleCode nvarchar(50) NOT NULL,
            RoleName nvarchar(150) NOT NULL,
            MoTa nvarchar(500) NULL,
            IsDeleted bit NOT NULL
                CONSTRAINT DF_App_Role_IsDeleted DEFAULT (0),
            CreatedAt datetime2(7) NOT NULL
                CONSTRAINT DF_App_Role_CreatedAt DEFAULT (SYSUTCDATETIME()),
            CreatedBy nvarchar(100) NULL,
            UpdatedAt datetime2(7) NULL,
            UpdatedBy nvarchar(100) NULL,
            RowVersion rowversion NOT NULL
        );
    END;

    IF COL_LENGTH(N'dbo.App_Role', N'RoleId') IS NULL
       OR COL_LENGTH(N'dbo.App_Role', N'RoleCode') IS NULL
       OR COL_LENGTH(N'dbo.App_Role', N'RoleName') IS NULL
       OR COL_LENGTH(N'dbo.App_Role', N'IsDeleted') IS NULL
       OR COL_LENGTH(N'dbo.App_Role', N'CreatedAt') IS NULL
       OR COL_LENGTH(N'dbo.App_Role', N'CreatedBy') IS NULL
       OR COL_LENGTH(N'dbo.App_Role', N'UpdatedAt') IS NULL
       OR COL_LENGTH(N'dbo.App_Role', N'UpdatedBy') IS NULL
    BEGIN
        THROW 51401, 'dbo.App_Role exists but is incompatible with application authorization.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'dbo.App_Role')
          AND i.is_unique = 1
          AND 1 =
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS ic
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                  ON c.object_id = ic.object_id
                 AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal = 1
                AND c.name = N'RoleCode'
          )
    )
    BEGIN
        CREATE UNIQUE INDEX UX_App_Role_RoleCode
            ON dbo.App_Role (RoleCode);
    END;

    IF OBJECT_ID(N'dbo.App_User', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_User
        (
            UserId bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_User PRIMARY KEY,
            UserName nvarchar(100) NOT NULL,
            DisplayName nvarchar(200) NOT NULL,
            PasswordHash nvarchar(500) NULL,
            Email nvarchar(200) NULL,
            Phone nvarchar(50) NULL,
            IsActive bit NOT NULL
                CONSTRAINT DF_App_User_IsActive DEFAULT (1),
            LastLoginAt datetime2(7) NULL,
            FailedLoginCount int NOT NULL
                CONSTRAINT DF_App_User_FailedLoginCount DEFAULT (0),
            IsDeleted bit NOT NULL
                CONSTRAINT DF_App_User_IsDeleted DEFAULT (0),
            DeletedAt datetime2(7) NULL,
            DeletedBy nvarchar(100) NULL,
            DeleteReason nvarchar(500) NULL,
            CreatedAt datetime2(7) NOT NULL
                CONSTRAINT DF_App_User_CreatedAt DEFAULT (SYSUTCDATETIME()),
            CreatedBy nvarchar(100) NULL,
            UpdatedAt datetime2(7) NULL,
            UpdatedBy nvarchar(100) NULL,
            RowVersion rowversion NOT NULL
        );
    END;

    IF COL_LENGTH(N'dbo.App_User', N'UserId') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'UserName') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'DisplayName') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'PasswordHash') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'LastLoginAt') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'FailedLoginCount') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'IsDeleted') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'CreatedAt') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'CreatedBy') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'UpdatedAt') IS NULL
       OR COL_LENGTH(N'dbo.App_User', N'UpdatedBy') IS NULL
    BEGIN
        THROW 51402, 'dbo.App_User exists but is incompatible with application authorization.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'dbo.App_User')
          AND i.is_unique = 1
          AND 1 =
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS ic
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                  ON c.object_id = ic.object_id
                 AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal = 1
                AND c.name = N'UserName'
          )
    )
    BEGIN
        CREATE UNIQUE INDEX UX_App_User_UserName
            ON dbo.App_User (UserName);
    END;

    IF OBJECT_ID(N'dbo.App_UserRole', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_UserRole
        (
            UserRoleId bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_UserRole PRIMARY KEY,
            UserId bigint NOT NULL,
            RoleId int NOT NULL,
            CreatedAt datetime2(7) NOT NULL
                CONSTRAINT DF_App_UserRole_CreatedAt DEFAULT (SYSUTCDATETIME()),
            CreatedBy nvarchar(100) NULL
        );
    END;

    IF COL_LENGTH(N'dbo.App_UserRole', N'UserRoleId') IS NULL
       OR COL_LENGTH(N'dbo.App_UserRole', N'UserId') IS NULL
       OR COL_LENGTH(N'dbo.App_UserRole', N'RoleId') IS NULL
       OR COL_LENGTH(N'dbo.App_UserRole', N'CreatedAt') IS NULL
       OR COL_LENGTH(N'dbo.App_UserRole', N'CreatedBy') IS NULL
    BEGIN
        THROW 51403, 'dbo.App_UserRole exists but is incompatible with application authorization.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'dbo.App_UserRole')
          AND i.is_unique = 1
          AND 2 =
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS ic
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                  ON c.object_id = ic.object_id
                 AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
                AND c.name = N'UserId'
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                  ON c.object_id = ic.object_id
                 AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id
                AND ic.index_id = i.index_id
                AND ic.key_ordinal > 0
                AND c.name = N'RoleId'
          )
    )
    BEGIN
        CREATE UNIQUE INDEX UX_App_UserRole_UserId_RoleId
            ON dbo.App_UserRole (UserId, RoleId);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_key_columns AS fkc
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.App_UserRole')
          AND fkc.parent_column_id = COLUMNPROPERTY(
              OBJECT_ID(N'dbo.App_UserRole'), N'UserId', 'ColumnId')
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.App_User')
          AND fkc.referenced_column_id = COLUMNPROPERTY(
              OBJECT_ID(N'dbo.App_User'), N'UserId', 'ColumnId')
    )
    BEGIN
        ALTER TABLE dbo.App_UserRole WITH CHECK
            ADD CONSTRAINT FK_App_UserRole_User
            FOREIGN KEY (UserId) REFERENCES dbo.App_User (UserId);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_key_columns AS fkc
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.App_UserRole')
          AND fkc.parent_column_id = COLUMNPROPERTY(
              OBJECT_ID(N'dbo.App_UserRole'), N'RoleId', 'ColumnId')
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.App_Role')
          AND fkc.referenced_column_id = COLUMNPROPERTY(
              OBJECT_ID(N'dbo.App_Role'), N'RoleId', 'ColumnId')
    )
    BEGIN
        ALTER TABLE dbo.App_UserRole WITH CHECK
            ADD CONSTRAINT FK_App_UserRole_Role
            FOREIGN KEY (RoleId) REFERENCES dbo.App_Role (RoleId);
    END;

    UPDATE dbo.App_Role
    SET RoleName = N'Administrator',
        MoTa = N'Full access to protected QLHV operations.',
        IsDeleted = 0,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = N'authorization-patch'
    WHERE RoleCode = N'Admin';

    INSERT INTO dbo.App_Role
        (RoleCode, RoleName, MoTa, IsDeleted, CreatedAt, CreatedBy)
    SELECT
        N'Admin', N'Administrator', N'Full access to protected QLHV operations.',
        0, SYSUTCDATETIME(), N'authorization-patch'
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = N'Admin'
    );

    UPDATE dbo.App_Role
    SET RoleName = N'Viewer',
        MoTa = N'Read-only access to QLHV status, diagnostics, plans and history.',
        IsDeleted = 0,
        UpdatedAt = SYSUTCDATETIME(),
        UpdatedBy = N'authorization-patch'
    WHERE RoleCode = N'Viewer';

    INSERT INTO dbo.App_Role
        (RoleCode, RoleName, MoTa, IsDeleted, CreatedAt, CreatedBy)
    SELECT
        N'Viewer', N'Viewer', N'Read-only access to QLHV status, diagnostics, plans and history.',
        0, SYSUTCDATETIME(), N'authorization-patch'
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_Role
        WHERE RoleCode = N'Viewer'
    );

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

/*
    QLHV_APP - CSDT connection profile configuration
    Task 5 B3W1

    This script defines storage for fixed CSDT/DATA/APP connection profiles.
    It is documentation/schema patch only and must be reviewed before manual execution.

    Safety:
    - Do not store plaintext passwords.
    - PasswordCipherText is reserved for encrypted bytes produced by the application.
    - Seed rows contain no real server, database, username, password, or connection string.
*/

IF OBJECT_ID(N'dbo.App_CsdtConnectionProfile', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_CsdtConnectionProfile
    (
        Id uniqueidentifier NOT NULL
            CONSTRAINT PK_App_CsdtConnectionProfile PRIMARY KEY
            CONSTRAINT DF_App_CsdtConnectionProfile_Id DEFAULT NEWID(),
        ProfileCode nvarchar(50) NOT NULL,
        DisplayName nvarchar(200) NOT NULL,
        ProfileGroup nvarchar(50) NOT NULL,
        ServerName nvarchar(255) NULL,
        DatabaseName nvarchar(255) NULL,
        AuthMode nvarchar(30) NOT NULL
            CONSTRAINT DF_App_CsdtConnectionProfile_AuthMode DEFAULT N'Windows',
        UserName nvarchar(255) NULL,
        PasswordCipherText varbinary(max) NULL,
        PasswordUpdatedAt datetime2 NULL,
        IsPasswordConfigured bit NOT NULL
            CONSTRAINT DF_App_CsdtConnectionProfile_IsPasswordConfigured DEFAULT (0),
        IsActive bit NOT NULL
            CONSTRAINT DF_App_CsdtConnectionProfile_IsActive DEFAULT (0),
        LastTestedAt datetime2 NULL,
        LastTestStatus nvarchar(50) NULL,
        LastTestMessage nvarchar(1000) NULL,
        CreatedAt datetime2 NOT NULL
            CONSTRAINT DF_App_CsdtConnectionProfile_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL
            CONSTRAINT DF_App_CsdtConnectionProfile_UpdatedAt DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT UQ_App_CsdtConnectionProfile_ProfileCode UNIQUE (ProfileCode),
        CONSTRAINT CK_App_CsdtConnectionProfile_AuthMode
            CHECK (AuthMode IN (N'Windows', N'SqlLogin')),
        CONSTRAINT CK_App_CsdtConnectionProfile_ProfileGroup
            CHECK (ProfileGroup IN (N'MOTO', N'OTO', N'GPLX', N'DATA', N'APP')),
        CONSTRAINT CK_App_CsdtConnectionProfile_LastTestStatus
            CHECK (LastTestStatus IS NULL OR LastTestStatus IN (N'NotConfigured', N'Success', N'Failed', N'Unknown')),
        CONSTRAINT CK_App_CsdtConnectionProfile_SqlLoginUserName
            CHECK (AuthMode <> N'SqlLogin' OR NULLIF(LTRIM(RTRIM(UserName)), N'') IS NOT NULL),
        CONSTRAINT CK_App_CsdtConnectionProfile_PasswordConfiguredConsistency
            CHECK (
                (IsPasswordConfigured = 0 AND PasswordCipherText IS NULL)
                OR
                (IsPasswordConfigured = 1 AND PasswordCipherText IS NOT NULL)
            )
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.App_CsdtConnectionProfile', N'U')
      AND name = N'IX_App_CsdtConnectionProfile_ProfileGroup'
)
BEGIN
    CREATE INDEX IX_App_CsdtConnectionProfile_ProfileGroup
        ON dbo.App_CsdtConnectionProfile(ProfileGroup, IsActive, ProfileCode);
END;

IF OBJECT_ID(N'dbo.App_CsdtConnectionProfileAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_CsdtConnectionProfileAudit
    (
        Id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_App_CsdtConnectionProfileAudit PRIMARY KEY,
        ProfileId uniqueidentifier NULL,
        ProfileCode nvarchar(50) NOT NULL,
        Action nvarchar(50) NOT NULL,
        ChangedAt datetime2 NOT NULL
            CONSTRAINT DF_App_CsdtConnectionProfileAudit_ChangedAt DEFAULT SYSUTCDATETIME(),
        ChangedBy nvarchar(255) NULL,
        ResultStatus nvarchar(50) NULL,
        SafeMessage nvarchar(1000) NULL,
        MetadataJson nvarchar(max) NULL,
        CONSTRAINT CK_App_CsdtConnectionProfileAudit_Action
            CHECK (Action IN (N'Create', N'Update', N'Test', N'Enable', N'Disable', N'PasswordChange', N'Seed')),
        CONSTRAINT CK_App_CsdtConnectionProfileAudit_ResultStatus
            CHECK (ResultStatus IS NULL OR ResultStatus IN (N'Success', N'Failed', N'Unknown'))
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.App_CsdtConnectionProfileAudit', N'U')
      AND name = N'IX_App_CsdtConnectionProfileAudit_ProfileCode_ChangedAt'
)
BEGIN
    CREATE INDEX IX_App_CsdtConnectionProfileAudit_ProfileCode_ChangedAt
        ON dbo.App_CsdtConnectionProfileAudit(ProfileCode, ChangedAt DESC);
END;

DECLARE @SeedProfiles TABLE
(
    ProfileCode nvarchar(50) NOT NULL,
    DisplayName nvarchar(200) NOT NULL,
    ProfileGroup nvarchar(50) NOT NULL
);

INSERT INTO @SeedProfiles (ProfileCode, DisplayName, ProfileGroup)
VALUES
    (N'CSDT_MOTO', N'CSDT Moto', N'MOTO'),
    (N'CSDT_OTO', N'CSDT Oto', N'OTO'),
    (N'CSDT_MOTO_GPLX', N'CSDT Moto GPLX', N'GPLX'),
    (N'CSDT_OTO_GPLX', N'CSDT Oto GPLX', N'GPLX'),
    (N'DATA_V1', N'Data V1', N'DATA'),
    (N'DATA_V2', N'Data V2', N'DATA'),
    (N'QLHV_APP', N'QLHV App', N'APP');

INSERT INTO dbo.App_CsdtConnectionProfile
(
    ProfileCode,
    DisplayName,
    ProfileGroup,
    AuthMode,
    IsPasswordConfigured,
    IsActive,
    LastTestStatus
)
SELECT
    seed.ProfileCode,
    seed.DisplayName,
    seed.ProfileGroup,
    N'Windows',
    0,
    0,
    N'NotConfigured'
FROM @SeedProfiles AS seed
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.App_CsdtConnectionProfile AS existing
    WHERE existing.ProfileCode = seed.ProfileCode
);

INSERT INTO dbo.App_CsdtConnectionProfileAudit
(
    ProfileId,
    ProfileCode,
    Action,
    ChangedBy,
    ResultStatus,
    SafeMessage
)
SELECT
    profile.Id,
    profile.ProfileCode,
    N'Seed',
    N'System',
    N'Success',
    N'Seeded fixed connection profile without server, database, username, password, or connection string.'
FROM dbo.App_CsdtConnectionProfile AS profile
WHERE profile.ProfileCode IN
(
    N'CSDT_MOTO',
    N'CSDT_OTO',
    N'CSDT_MOTO_GPLX',
    N'CSDT_OTO_GPLX',
    N'DATA_V1',
    N'DATA_V2',
    N'QLHV_APP'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.App_CsdtConnectionProfileAudit AS audit
    WHERE audit.ProfileCode = profile.ProfileCode
      AND audit.Action = N'Seed'
);

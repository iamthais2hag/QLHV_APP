/*
    QLHV_APP - fixed read-only source profiles for refreshed CSDT BAK snapshots.

    This patch is safe to run again. It creates or corrects only:
      - CSDT_OTO_BAK  -> CSDLTTTC / CSDL_OTO_BAK
      - CSDT_MOTO_BAK -> CSDLTTTC / CSDL_MOTO_BAK

    The logical CSDT_OTO and CSDT_MOTO profiles are intentionally untouched.
    Both BAK profiles use Windows Authentication and contain no credentials.
*/

USE [QLHV_APP];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_CsdtConnectionProfile', N'U') IS NULL
    BEGIN
        THROW 527230, 'Missing required table dbo.App_CsdtConnectionProfile.', 1;
    END;

    IF OBJECT_ID(N'dbo.App_CsdtConnectionProfileAudit', N'U') IS NULL
    BEGIN
        THROW 527231, 'Missing required table dbo.App_CsdtConnectionProfileAudit.', 1;
    END;

    DECLARE @DesiredProfiles table
    (
        ProfileCode nvarchar(50) NOT NULL PRIMARY KEY,
        DisplayName nvarchar(200) NOT NULL,
        ProfileGroup nvarchar(50) NOT NULL,
        ServerName nvarchar(255) NOT NULL,
        DatabaseName nvarchar(255) NOT NULL
    );

    INSERT INTO @DesiredProfiles
    (
        ProfileCode,
        DisplayName,
        ProfileGroup,
        ServerName,
        DatabaseName
    )
    VALUES
        (N'CSDT_OTO_BAK', N'CSDT Oto BAK', N'OTO', N'CSDLTTTC', N'CSDL_OTO_BAK'),
        (N'CSDT_MOTO_BAK', N'CSDT Moto BAK', N'MOTO', N'CSDLTTTC', N'CSDL_MOTO_BAK');

    DECLARE @ChangedProfiles table
    (
        ProfileId uniqueidentifier NOT NULL,
        ProfileCode nvarchar(50) NOT NULL,
        Action nvarchar(50) NOT NULL
    );

    UPDATE profile
    SET
        DisplayName = desired.DisplayName,
        ProfileGroup = desired.ProfileGroup,
        ServerName = desired.ServerName,
        DatabaseName = desired.DatabaseName,
        AuthMode = N'Windows',
        UserName = NULL,
        PasswordCipherText = NULL,
        PasswordUpdatedAt = NULL,
        IsPasswordConfigured = 0,
        IsActive = 1,
        LastTestedAt = NULL,
        LastTestStatus = N'NotConfigured',
        LastTestMessage = NULL,
        UpdatedAt = SYSUTCDATETIME()
    OUTPUT
        inserted.Id,
        inserted.ProfileCode,
        N'Update'
    INTO @ChangedProfiles (ProfileId, ProfileCode, Action)
    FROM dbo.App_CsdtConnectionProfile AS profile
    INNER JOIN @DesiredProfiles AS desired
        ON desired.ProfileCode = profile.ProfileCode
    WHERE profile.DisplayName <> desired.DisplayName
       OR profile.ProfileGroup <> desired.ProfileGroup
       OR ISNULL(profile.ServerName, N'') <> desired.ServerName
       OR ISNULL(profile.DatabaseName, N'') <> desired.DatabaseName
       OR profile.AuthMode <> N'Windows'
       OR profile.UserName IS NOT NULL
       OR profile.PasswordCipherText IS NOT NULL
       OR profile.PasswordUpdatedAt IS NOT NULL
       OR profile.IsPasswordConfigured <> 0
       OR profile.IsActive <> 1;

    INSERT INTO dbo.App_CsdtConnectionProfile
    (
        ProfileCode,
        DisplayName,
        ProfileGroup,
        ServerName,
        DatabaseName,
        AuthMode,
        UserName,
        PasswordCipherText,
        PasswordUpdatedAt,
        IsPasswordConfigured,
        IsActive,
        LastTestStatus
    )
    OUTPUT
        inserted.Id,
        inserted.ProfileCode,
        N'Seed'
    INTO @ChangedProfiles (ProfileId, ProfileCode, Action)
    SELECT
        desired.ProfileCode,
        desired.DisplayName,
        desired.ProfileGroup,
        desired.ServerName,
        desired.DatabaseName,
        N'Windows',
        NULL,
        NULL,
        NULL,
        0,
        1,
        N'NotConfigured'
    FROM @DesiredProfiles AS desired
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_CsdtConnectionProfile AS existing
        WHERE existing.ProfileCode = desired.ProfileCode
    );

    IF EXISTS
    (
        SELECT 1
        FROM @DesiredProfiles AS desired
        LEFT JOIN dbo.App_CsdtConnectionProfile AS profile
            ON profile.ProfileCode = desired.ProfileCode
        WHERE profile.Id IS NULL
           OR profile.DisplayName <> desired.DisplayName
           OR profile.ProfileGroup <> desired.ProfileGroup
           OR ISNULL(profile.ServerName, N'') <> desired.ServerName
           OR ISNULL(profile.DatabaseName, N'') <> desired.DatabaseName
           OR profile.AuthMode <> N'Windows'
           OR profile.UserName IS NOT NULL
           OR profile.PasswordCipherText IS NOT NULL
           OR profile.PasswordUpdatedAt IS NOT NULL
           OR profile.IsPasswordConfigured <> 0
           OR profile.IsActive <> 1
    )
    BEGIN
        THROW 527232, 'BAK connection profiles failed post-write validation.', 1;
    END;

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
        changed.ProfileId,
        changed.ProfileCode,
        changed.Action,
        N'System',
        N'Success',
        CASE changed.Action
            WHEN N'Seed' THEN N'Seeded Windows-authenticated BAK profile without credentials.'
            ELSE N'Updated Windows-authenticated BAK profile without credentials.'
        END
    FROM @ChangedProfiles AS changed;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO

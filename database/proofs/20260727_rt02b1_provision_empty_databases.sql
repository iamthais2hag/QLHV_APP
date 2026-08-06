USE [master];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC\QLHVRT02'
    THROW 527330, 'ISOLATED_DATABASE_IDENTITY_REJECTED: wrong server identity.', 1;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) NOT LIKE N'%Developer%'
   OR CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
    THROW 527331, 'ISOLATED_DATABASE_IDENTITY_REJECTED: wrong edition or major version.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
)
    THROW 527332, 'RT02B1 exact database names must be absent before provisioning.', 1;
GO

CREATE DATABASE [QLHV_RT02_OTO_TEST]
ON PRIMARY
(
    NAME = N'QLHV_RT02_OTO_TEST',
    FILENAME = N'D:\QLHV_RT02_SQLDATA\Data\QLHV_RT02_OTO_TEST.mdf',
    SIZE = 32 MB,
    FILEGROWTH = 16 MB
)
LOG ON
(
    NAME = N'QLHV_RT02_OTO_TEST_log',
    FILENAME = N'D:\QLHV_RT02_SQLDATA\Log\QLHV_RT02_OTO_TEST_log.ldf',
    SIZE = 16 MB,
    FILEGROWTH = 16 MB
);
GO

CREATE DATABASE [QLHV_RT02_MOTO_TEST]
ON PRIMARY
(
    NAME = N'QLHV_RT02_MOTO_TEST',
    FILENAME = N'D:\QLHV_RT02_SQLDATA\Data\QLHV_RT02_MOTO_TEST.mdf',
    SIZE = 32 MB,
    FILEGROWTH = 16 MB
)
LOG ON
(
    NAME = N'QLHV_RT02_MOTO_TEST_log',
    FILENAME = N'D:\QLHV_RT02_SQLDATA\Log\QLHV_RT02_MOTO_TEST_log.ldf',
    SIZE = 16 MB,
    FILEGROWTH = 16 MB
);
GO

CREATE DATABASE [QLHV_RT02_TARGET_TEST]
ON PRIMARY
(
    NAME = N'QLHV_RT02_TARGET_TEST',
    FILENAME = N'D:\QLHV_RT02_SQLDATA\Data\QLHV_RT02_TARGET_TEST.mdf',
    SIZE = 32 MB,
    FILEGROWTH = 16 MB
)
LOG ON
(
    NAME = N'QLHV_RT02_TARGET_TEST_log',
    FILENAME = N'D:\QLHV_RT02_SQLDATA\Log\QLHV_RT02_TARGET_TEST_log.ldf',
    SIZE = 16 MB,
    FILEGROWTH = 16 MB
);
GO

USE [QLHV_RT02_OTO_TEST];
GO
EXEC sys.sp_addextendedproperty
    @name = N'RT02_ISOLATED_ENVIRONMENT_ID',
    @value = N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_OWNER_APPROVAL_ID',
    @value = N'RT02B-OPERATOR-APPROVAL-20260727-01';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_DATASET_MODE',
    @value = N'SYNTHETIC';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_PRODUCTION_ROUTE_ALLOWED',
    @value = N'FALSE';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_EXPIRES_AT_UTC',
    @value = N'2026-07-31T16:59:59Z';
GO

USE [QLHV_RT02_MOTO_TEST];
GO
EXEC sys.sp_addextendedproperty
    @name = N'RT02_ISOLATED_ENVIRONMENT_ID',
    @value = N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_OWNER_APPROVAL_ID',
    @value = N'RT02B-OPERATOR-APPROVAL-20260727-01';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_DATASET_MODE',
    @value = N'SYNTHETIC';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_PRODUCTION_ROUTE_ALLOWED',
    @value = N'FALSE';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_EXPIRES_AT_UTC',
    @value = N'2026-07-31T16:59:59Z';
GO

USE [QLHV_RT02_TARGET_TEST];
GO
EXEC sys.sp_addextendedproperty
    @name = N'RT02_ISOLATED_ENVIRONMENT_ID',
    @value = N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_OWNER_APPROVAL_ID',
    @value = N'RT02B-OPERATOR-APPROVAL-20260727-01';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_DATASET_MODE',
    @value = N'SYNTHETIC';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_PRODUCTION_ROUTE_ALLOWED',
    @value = N'FALSE';
EXEC sys.sp_addextendedproperty
    @name = N'RT02_EXPIRES_AT_UTC',
    @value = N'2026-07-31T16:59:59Z';
GO

USE [master];
GO
SELECT
    N'RT02B1_PROVISIONING_COMPLETE' AS Result,
    COUNT_BIG(*) AS CreatedDatabaseCount
FROM sys.databases
WHERE name IN
(
    N'QLHV_RT02_OTO_TEST',
    N'QLHV_RT02_MOTO_TEST',
    N'QLHV_RT02_TARGET_TEST'
);
GO

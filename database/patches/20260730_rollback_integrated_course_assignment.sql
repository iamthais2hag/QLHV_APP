/*
    Exact rollback for 20260730_add_integrated_course_assignment.sql.

    This rollback is deliberately available only while there is no receiver,
    group, assignment, assignment-import data, or assignment metadata history.
    Once business data/history exists, disable the assignment writer and use a
    reviewed roll-forward patch. Never drop assignment history.

    PRODUCTION STATUS: NOT APPLIED BY THIS TASK.
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

DECLARE @ExpectedProductionDatabaseGuid uniqueidentifier =
    '9C44B304-8A84-4D0D-9A82-19C7233FF6BB';
DECLARE @ObservedDatabaseGuid uniqueidentifier =
(
    SELECT database_guid
    FROM sys.database_recovery_status
    WHERE database_id = DB_ID()
);
DECLARE @IsIsolatedRehearsal bit =
    CASE WHEN DB_NAME() LIKE N'QLHV[_]ASSIGNMENT[_]REHEARSAL[_]%' THEN 1 ELSE 0 END;

IF DB_NAME() <> N'QLHV_APP' AND @IsIsolatedRehearsal = 0
    THROW 529350, 'Wrong database. Expected QLHV_APP or an explicitly named isolated assignment rehearsal database.', 1;

IF DB_NAME() = N'QLHV_APP'
   AND (@ObservedDatabaseGuid IS NULL
        OR @ObservedDatabaseGuid <> @ExpectedProductionDatabaseGuid)
    THROW 529351, 'QLHV_APP database identity mismatch.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID()
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
)
    THROW 529352, 'Target database must be ONLINE and read-write.', 1;
GO

IF OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_AssignmentOperation', N'U') IS NULL
   OR DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole') IS NULL
    THROW 529353, 'Assignment schema is incomplete; exact rollback is rejected.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_ImportBatch', N'U')
      AND name IN
      (
          N'FileSha256',
          N'TemplateVersion',
          N'NormalizationVersion',
          N'PreviewExpiresAtUtc',
          N'ConfirmedAtUtc',
          N'IdempotencyKey'
      )
) <> 6
    THROW 529354, 'Assignment import extension is incomplete; exact rollback is rejected.', 1;

IF EXISTS (SELECT 1 FROM dbo.App_HocVien_PhanCong)
   OR EXISTS (SELECT 1 FROM dbo.App_KhoaHoc_NhomDaoTao)
   OR EXISTS (SELECT 1 FROM dbo.App_GiaoVien_hs)
   OR EXISTS (SELECT 1 FROM dbo.App_AssignmentOperation)
   OR EXISTS
(
    SELECT 1
    FROM dbo.App_AuditLog
    WHERE ChucNang IN
          (N'GIAO_VIEN_HO_SO',N'NHOM_DAO_TAO',N'NHOM_DAO_TAO_DEFAULTS',
           N'PHAN_CONG_HOC_VIEN',N'PHAN_CONG_EXCEL')
)
   OR EXISTS
(
    SELECT 1
    FROM dbo.App_ImportBatch
    WHERE EntityType = N'HOCVIEN_ASSIGNMENT'
       OR FileSha256 IS NOT NULL
       OR TemplateVersion IS NOT NULL
       OR NormalizationVersion IS NOT NULL
       OR PreviewExpiresAtUtc IS NOT NULL
       OR ConfirmedAtUtc IS NOT NULL
       OR IdempotencyKey IS NOT NULL
)
    THROW 529355, 'ROLLBACK_BLOCKED_DATA_OR_HISTORY: disable the assignment writer and roll forward; do not drop data.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE referenced_object_id IN
    (
        OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U'),
        OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U'),
        OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U'),
        OBJECT_ID(N'dbo.App_AssignmentOperation', N'U')
    )
      AND parent_object_id NOT IN
      (
          OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U'),
          OBJECT_ID(N'dbo.App_AssignmentOperation', N'U')
      )
)
    THROW 529356, 'A later schema depends on the assignment tables; exact rollback is rejected.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members
    WHERE role_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
)
    THROW 529361, 'QLHV_AssignmentApiRole still has members; remove explicit deployment membership before rollback.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NOT NULL
    BEGIN
        REVOKE INSERT, UPDATE, DELETE
            ON OBJECT::dbo.App_ImportBatch
            FROM [NT SERVICE\QLHV_APP_RealtimeWorker];
        REVOKE INSERT, UPDATE, DELETE
            ON OBJECT::dbo.App_AssignmentOperation
            FROM [NT SERVICE\QLHV_APP_RealtimeWorker];
    END;

    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.App_GiaoVien_hs
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.App_KhoaHoc_NhomDaoTao
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.App_HocVien_PhanCong
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.App_ImportBatch
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT, INSERT ON OBJECT::dbo.App_AssignmentOperation
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT ON OBJECT::dbo.App_KhoaHoc
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT ON OBJECT::dbo.App_HocVien
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT ON OBJECT::dbo.App_GiaoVien
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT ON OBJECT::dbo.App_XeTap
        FROM [QLHV_AssignmentApiRole];
    REVOKE SELECT, INSERT ON OBJECT::dbo.App_AuditLog
        FROM [QLHV_AssignmentApiRole];

    DROP TABLE dbo.App_AssignmentOperation;
    DROP TABLE dbo.App_HocVien_PhanCong;
    DROP TABLE dbo.App_KhoaHoc_NhomDaoTao;
    DROP TABLE dbo.App_GiaoVien_hs;

    DROP INDEX UX_App_ImportBatch_AssignmentIdempotency
        ON dbo.App_ImportBatch;

    ALTER TABLE dbo.App_ImportBatch DROP CONSTRAINT
        CK_App_ImportBatch_AssignmentFileSha256,
        CK_App_ImportBatch_AssignmentVersions,
        CK_App_ImportBatch_AssignmentPreviewTimes,
        CK_App_ImportBatch_AssignmentIdempotency;

    ALTER TABLE dbo.App_ImportBatch DROP COLUMN
        FileSha256,
        TemplateVersion,
        NormalizationVersion,
        PreviewExpiresAtUtc,
        ConfirmedAtUtc,
        IdempotencyKey;

    DROP ROLE [QLHV_AssignmentApiRole];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.App_AssignmentOperation', N'U') IS NOT NULL
    THROW 529357, 'Post-rollback table verification failed.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_ImportBatch', N'U')
      AND name IN
      (
          N'FileSha256',
          N'TemplateVersion',
          N'NormalizationVersion',
          N'PreviewExpiresAtUtc',
          N'ConfirmedAtUtc',
          N'IdempotencyKey'
      )
)
    THROW 529358, 'Post-rollback import-column verification failed.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.App_ImportBatch', N'U')
      AND name = N'UX_App_ImportBatch_AssignmentIdempotency'
)
    THROW 529359, 'Post-rollback import-index verification failed.', 1;

IF DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NOT NULL
   AND EXISTS
(
    SELECT 1
    FROM sys.database_permissions
    WHERE grantee_principal_id =
          DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker')
      AND class = 1
      AND major_id = OBJECT_ID(N'dbo.App_ImportBatch', N'U')
      AND permission_name IN (N'INSERT', N'UPDATE', N'DELETE')
)
    THROW 529360, 'Post-rollback realtime App_ImportBatch permission verification failed.', 1;

IF DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole') IS NOT NULL
    THROW 529362, 'Post-rollback assignment API role verification failed.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    CONVERT(varchar(36), recovery.database_guid) AS DatabaseGuid,
    CONVERT(bit, 1) AS RollbackVerified
FROM sys.database_recovery_status AS recovery
WHERE recovery.database_id = DB_ID();
GO

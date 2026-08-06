USE [$(Rt03TargetDatabase)];
GO

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabaseName sysname = N'$(Rt03TargetDatabase)';
DECLARE @ExpectedDatabaseId int =
    TRY_CONVERT(int, N'$(Rt03ExpectedDatabaseId)');
DECLARE @ExpectedDatabaseGuid uniqueidentifier =
    TRY_CONVERT(uniqueidentifier, N'$(Rt03ExpectedDatabaseGuid)');
DECLARE @ExecutionMode nvarchar(20) = N'$(Rt03ExecutionMode)';

IF @ExecutionMode NOT IN (N'PRODUCTION', N'REHEARSAL')
   OR (@ExecutionMode = N'PRODUCTION'
       AND
       (
           @ExpectedDatabaseName <> N'QLHV_APP'
           OR @ExpectedDatabaseId <> 12
           OR @ExpectedDatabaseGuid <>
              '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
       ))
   OR (@ExecutionMode = N'REHEARSAL'
       AND @ExpectedDatabaseName NOT LIKE
           N'QLHV[_]RT03[_]KHOAHOC[_]REHEARSAL[_]%')
    THROW 527618, N'RT03_SCHEMA_ROLLBACK_EXECUTION_CONTRACT_REJECTED', 1;

IF DB_NAME() <> @ExpectedDatabaseName
   OR DB_ID() <> @ExpectedDatabaseId
   OR @ExpectedDatabaseGuid IS NULL
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
         AND database_guid = @ExpectedDatabaseGuid
   )
    THROW 527610, N'RT03_KHOAHOC_ROLLBACK_WRONG_TARGET_IDENTITY', 1;

IF SESSIONPROPERTY(N'ANSI_NULLS') <> 1
    THROW 527630, N'RT03_ROLLBACK_SESSION_OPTION_ANSI_NULLS_MUST_BE_ON', 1;
IF SESSIONPROPERTY(N'ANSI_PADDING') <> 1
    THROW 527631, N'RT03_ROLLBACK_SESSION_OPTION_ANSI_PADDING_MUST_BE_ON', 1;
IF SESSIONPROPERTY(N'ANSI_WARNINGS') <> 1
    THROW 527632, N'RT03_ROLLBACK_SESSION_OPTION_ANSI_WARNINGS_MUST_BE_ON', 1;
IF SESSIONPROPERTY(N'ARITHABORT') <> 1
    THROW 527633, N'RT03_ROLLBACK_SESSION_OPTION_ARITHABORT_MUST_BE_ON', 1;
IF SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL') <> 1
    THROW 527634,
        N'RT03_ROLLBACK_SESSION_OPTION_CONCAT_NULL_YIELDS_NULL_MUST_BE_ON',
        1;
IF SESSIONPROPERTY(N'QUOTED_IDENTIFIER') <> 1
    THROW 527635,
        N'RT03_ROLLBACK_SESSION_OPTION_QUOTED_IDENTIFIER_MUST_BE_ON',
        1;
IF SESSIONPROPERTY(N'NUMERIC_ROUNDABORT') <> 0
    THROW 527636,
        N'RT03_ROLLBACK_SESSION_OPTION_NUMERIC_ROUNDABORT_MUST_BE_OFF',
        1;

IF OBJECT_ID(N'dbo.App_KhoaHoc', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvDirectRealtimeCycleHistory', N'U') IS NULL
    THROW 527617, N'RT03_SCHEMA_ROLLBACK_REQUIRED_TABLE_MISSING', 1;

DECLARE @NormalizedSourceFilter nvarchar(4000) =
(
    SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(
        COALESCE(filter_definition, N''),
        N'[', N''), N']', N''), N'(', N''), N')', N''))
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc')
      AND name = N'UX_App_KhoaHoc_SourceIdentity'
);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes targetIndex
    WHERE targetIndex.object_id = OBJECT_ID(N'dbo.App_KhoaHoc')
      AND targetIndex.name = N'UX_App_KhoaHoc_SourceIdentity'
      AND targetIndex.type = 2
      AND targetIndex.is_unique = 1
      AND targetIndex.is_disabled = 0
      AND targetIndex.has_filter = 1
      AND REPLACE(@NormalizedSourceFilter, N' ', N'') =
          N'sourceprofilecodeisnotnullandsourcemakhoahocisnotnull'
      AND
      (
          SELECT COUNT(1)
          FROM sys.index_columns item
          WHERE item.object_id = targetIndex.object_id
            AND item.index_id = targetIndex.index_id
      ) = 2
      AND EXISTS
      (
          SELECT 1
          FROM sys.index_columns item
          INNER JOIN sys.columns targetColumn
              ON targetColumn.object_id = item.object_id
             AND targetColumn.column_id = item.column_id
          WHERE item.object_id = targetIndex.object_id
            AND item.index_id = targetIndex.index_id
            AND item.key_ordinal = 1
            AND targetColumn.name = N'SourceProfileCode'
      )
      AND EXISTS
      (
          SELECT 1
          FROM sys.index_columns item
          INNER JOIN sys.columns targetColumn
              ON targetColumn.object_id = item.object_id
             AND targetColumn.column_id = item.column_id
          WHERE item.object_id = targetIndex.object_id
            AND item.index_id = targetIndex.index_id
            AND item.key_ordinal = 2
            AND targetColumn.name = N'SourceMaKhoaHoc'
      )
)
    THROW 527637,
        N'BLOCKED - RT03 SCHEMA DRIFT DETECTED: rollback source identity',
        1;

DECLARE @CycleDefinition nvarchar(max) =
(
    SELECT LOWER(
        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            definition,
            N'[', N''), N']', N''), N'(', N''), N')', N''),
            N' ', N''), CHAR(10), N''))
    FROM sys.check_constraints
    WHERE parent_object_id =
          OBJECT_ID(N'dbo.App_QlhvDirectRealtimeCycleHistory')
      AND name = N'CK_App_QlhvDirectRealtimeCycleHistory_Mutations'
      AND is_disabled = 0
      AND is_not_trusted = 0
);
SET @CycleDefinition = REPLACE(COALESCE(@CycleDefinition, N''), CHAR(13), N'');

DECLARE @BaselineCycleDefinition nvarchar(max) =
    N'insertedrows>=0andinsertedrows<=1andupdatedrows>=0andupdatedrows<=1andinsertedrows+updatedrows<=1anddeletedordeactivatedrows=0andduplicateactiverows=0andcheckpointafter>=checkpointbefore';
DECLARE @MigratedCycleDefinition nvarchar(max) =
    N'insertedrows>=0andupdatedrows>=0anddeletedordeactivatedrows=0andduplicateactiverows=0andcheckpointafter>=checkpointbefore';

DECLARE @GlobalConstraintExact bit =
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.key_constraints keyConstraint
        INNER JOIN sys.indexes targetIndex
            ON targetIndex.object_id = keyConstraint.parent_object_id
           AND targetIndex.index_id = keyConstraint.unique_index_id
        WHERE keyConstraint.parent_object_id =
              OBJECT_ID(N'dbo.App_KhoaHoc')
          AND keyConstraint.name = N'UQ_App_KhoaHoc_MaKhoa'
          AND keyConstraint.type = N'UQ'
          AND targetIndex.type = 2
          AND targetIndex.is_unique = 1
          AND targetIndex.is_disabled = 0
          AND targetIndex.has_filter = 0
          AND
          (
              SELECT COUNT(1)
              FROM sys.index_columns item
              WHERE item.object_id = targetIndex.object_id
                AND item.index_id = targetIndex.index_id
          ) = 1
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns item
              INNER JOIN sys.columns targetColumn
                  ON targetColumn.object_id = item.object_id
                 AND targetColumn.column_id = item.column_id
              WHERE item.object_id = targetIndex.object_id
                AND item.index_id = targetIndex.index_id
                AND item.key_ordinal = 1
                AND targetColumn.name = N'MaKhoa'
          )
    ) THEN 1 ELSE 0 END;

DECLARE @LookupIndexExact bit =
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.indexes targetIndex
        WHERE targetIndex.object_id = OBJECT_ID(N'dbo.App_KhoaHoc')
          AND targetIndex.name = N'IX_App_KhoaHoc_SourceProfile_MaKhoa'
          AND targetIndex.type = 2
          AND targetIndex.is_unique = 0
          AND targetIndex.is_disabled = 0
          AND targetIndex.has_filter = 0
          AND
          (
              SELECT COUNT(1)
              FROM sys.index_columns item
              WHERE item.object_id = targetIndex.object_id
                AND item.index_id = targetIndex.index_id
          ) = 6
          AND
          (
              SELECT COUNT(1)
              FROM sys.index_columns item
              WHERE item.object_id = targetIndex.object_id
                AND item.index_id = targetIndex.index_id
                AND item.key_ordinal > 0
          ) = 2
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns item
              INNER JOIN sys.columns targetColumn
                  ON targetColumn.object_id = item.object_id
                 AND targetColumn.column_id = item.column_id
              WHERE item.object_id = targetIndex.object_id
                AND item.index_id = targetIndex.index_id
                AND item.key_ordinal = 1
                AND targetColumn.name = N'SourceProfileCode'
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns item
              INNER JOIN sys.columns targetColumn
                  ON targetColumn.object_id = item.object_id
                 AND targetColumn.column_id = item.column_id
              WHERE item.object_id = targetIndex.object_id
                AND item.index_id = targetIndex.index_id
                AND item.key_ordinal = 2
                AND targetColumn.name = N'MaKhoa'
          )
          AND
          (
              SELECT COUNT(1)
              FROM sys.index_columns item
              INNER JOIN sys.columns targetColumn
                  ON targetColumn.object_id = item.object_id
                 AND targetColumn.column_id = item.column_id
              WHERE item.object_id = targetIndex.object_id
                AND item.index_id = targetIndex.index_id
                AND item.is_included_column = 1
                AND targetColumn.name IN
                    (N'SourceMaKhoaHoc', N'SourceHash',
                     N'IsDeleted', N'TrangThaiNguon')
          ) = 4
    ) THEN 1 ELSE 0 END;

DECLARE @HasNamedGlobalConstraint bit =
    CASE WHEN OBJECT_ID(N'dbo.UQ_App_KhoaHoc_MaKhoa', N'UQ') IS NOT NULL
         THEN 1 ELSE 0 END;
DECLARE @HasNamedLookupIndex bit =
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc')
          AND name = N'IX_App_KhoaHoc_SourceProfile_MaKhoa'
    ) THEN 1 ELSE 0 END;

DECLARE @BaselineExact bit =
    CASE WHEN @GlobalConstraintExact = 1
                   AND @HasNamedLookupIndex = 0
                   AND @CycleDefinition = @BaselineCycleDefinition
         THEN 1 ELSE 0 END;
DECLARE @MigratedExact bit =
    CASE WHEN @HasNamedGlobalConstraint = 0
                   AND @LookupIndexExact = 1
                   AND @CycleDefinition = @MigratedCycleDefinition
         THEN 1 ELSE 0 END;

IF @BaselineExact = 0 AND @MigratedExact = 0
    THROW 527637, N'BLOCKED - RT03 SCHEMA DRIFT DETECTED', 1;

IF @BaselineExact = 1
BEGIN
    SELECT
        SESSIONPROPERTY(N'ANSI_NULLS') AS ANSI_NULLS,
        SESSIONPROPERTY(N'ANSI_PADDING') AS ANSI_PADDING,
        SESSIONPROPERTY(N'ANSI_WARNINGS') AS ANSI_WARNINGS,
        SESSIONPROPERTY(N'ARITHABORT') AS ARITHABORT,
        SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL') AS CONCAT_NULL_YIELDS_NULL,
        SESSIONPROPERTY(N'QUOTED_IDENTIFIER') AS QUOTED_IDENTIFIER,
        SESSIONPROPERTY(N'NUMERIC_ROUNDABORT') AS NUMERIC_ROUNDABORT,
        N'RT03_KHOAHOC_SCHEMA_ROLLBACK_ALREADY_BASELINE_EXACT' AS Evidence;
    RETURN;
END;

IF EXISTS
(
    SELECT MaKhoa
    FROM dbo.App_KhoaHoc
    GROUP BY MaKhoa
    HAVING COUNT_BIG(1) > 1
)
    THROW 527611,
        N'RT03_KHOAHOC_ROLLBACK_BLOCKED_CROSS_PROFILE_MAKHOA_EXISTS',
        1;

IF EXISTS
(
    SELECT 1
    FROM dbo.App_QlhvDirectRealtimeCycleHistory
    WHERE InsertedRows > 1
       OR UpdatedRows > 1
       OR InsertedRows + UpdatedRows > 1
)
    THROW 527612,
        N'RT03_KHOAHOC_ROLLBACK_BLOCKED_MULTIROW_CYCLE_HISTORY_EXISTS',
        1;

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory
        DROP CONSTRAINT CK_App_QlhvDirectRealtimeCycleHistory_Mutations;

    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory WITH CHECK
        ADD CONSTRAINT CK_App_QlhvDirectRealtimeCycleHistory_Mutations
        CHECK
        (
            InsertedRows BETWEEN 0 AND 1
            AND UpdatedRows BETWEEN 0 AND 1
            AND InsertedRows + UpdatedRows <= 1
            AND DeletedOrDeactivatedRows = 0
            AND DuplicateActiveRows = 0
            AND CheckpointAfter >= CheckpointBefore
        );
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory
        CHECK CONSTRAINT CK_App_QlhvDirectRealtimeCycleHistory_Mutations;

    DROP INDEX IX_App_KhoaHoc_SourceProfile_MaKhoa
        ON dbo.App_KhoaHoc;

    ALTER TABLE dbo.App_KhoaHoc
        ADD CONSTRAINT UQ_App_KhoaHoc_MaKhoa UNIQUE (MaKhoa);

    DECLARE @PostRollbackCycleDefinition nvarchar(max) =
    (
        SELECT LOWER(
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                definition,
                N'[', N''), N']', N''), N'(', N''), N')', N''),
                N' ', N''), CHAR(10), N''))
        FROM sys.check_constraints
        WHERE parent_object_id =
              OBJECT_ID(N'dbo.App_QlhvDirectRealtimeCycleHistory')
          AND name = N'CK_App_QlhvDirectRealtimeCycleHistory_Mutations'
          AND is_disabled = 0
          AND is_not_trusted = 0
    );
    SET @PostRollbackCycleDefinition =
        REPLACE(COALESCE(@PostRollbackCycleDefinition, N''), CHAR(13), N'');

    IF NOT EXISTS
       (
           SELECT 1
           FROM sys.key_constraints keyConstraint
           INNER JOIN sys.indexes targetIndex
               ON targetIndex.object_id = keyConstraint.parent_object_id
              AND targetIndex.index_id = keyConstraint.unique_index_id
           WHERE keyConstraint.parent_object_id =
                 OBJECT_ID(N'dbo.App_KhoaHoc')
             AND keyConstraint.name = N'UQ_App_KhoaHoc_MaKhoa'
             AND keyConstraint.type = N'UQ'
             AND targetIndex.type = 2
             AND targetIndex.is_unique = 1
             AND targetIndex.is_disabled = 0
             AND targetIndex.has_filter = 0
             AND
             (
                 SELECT COUNT(1)
                 FROM sys.index_columns item
                 WHERE item.object_id = targetIndex.object_id
                   AND item.index_id = targetIndex.index_id
             ) = 1
             AND EXISTS
             (
                 SELECT 1
                 FROM sys.index_columns item
                 INNER JOIN sys.columns targetColumn
                     ON targetColumn.object_id = item.object_id
                    AND targetColumn.column_id = item.column_id
                 WHERE item.object_id = targetIndex.object_id
                   AND item.index_id = targetIndex.index_id
                   AND item.key_ordinal = 1
                   AND targetColumn.name = N'MaKhoa'
             )
       )
       OR EXISTS
       (
           SELECT 1
           FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc')
             AND name = N'IX_App_KhoaHoc_SourceProfile_MaKhoa'
       )
       OR @PostRollbackCycleDefinition <> @BaselineCycleDefinition
        THROW 527637,
            N'BLOCKED - RT03 SCHEMA DRIFT DETECTED: rollback assertion',
            1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    SESSIONPROPERTY(N'ANSI_NULLS') AS ANSI_NULLS,
    SESSIONPROPERTY(N'ANSI_PADDING') AS ANSI_PADDING,
    SESSIONPROPERTY(N'ANSI_WARNINGS') AS ANSI_WARNINGS,
    SESSIONPROPERTY(N'ARITHABORT') AS ARITHABORT,
    SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL') AS CONCAT_NULL_YIELDS_NULL,
    SESSIONPROPERTY(N'QUOTED_IDENTIFIER') AS QUOTED_IDENTIFIER,
    SESSIONPROPERTY(N'NUMERIC_ROUNDABORT') AS NUMERIC_ROUNDABORT,
    N'RT03_KHOAHOC_SCHEMA_ROLLBACK_APPLIED_AND_VERIFIED' AS Evidence;

/*
    QLHV_APP - expand App_HocVien.SourceProfileCode for direct CSDT imports.

    Allowed values after this patch:
      DATA_V1, DATA_V2, CSDT_MOTO, CSDT_OTO, and NULL when the column is nullable.

    Safety:
      - Review and execute manually against the intended QLHV_APP database.
      - Existing values are validated before any constraint is replaced.
      - The current CHECK constraint is discovered from SQL Server metadata; its name is not assumed.
      - A constraint involving additional columns causes the patch to stop for manual review.
      - The patch is transactional and safe to run again.
*/

USE [QLHV_APP];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @TableName nvarchar(517) = N'dbo.App_HocVien';
    DECLARE @ColumnName sysname = N'SourceProfileCode';
    DECLARE @TargetObjectId int = OBJECT_ID(@TableName, N'U');
    DECLARE @TargetColumnId int;
    DECLARE @ExistingConstraintName sysname;
    DECLARE @ExistingConstraintDefinition nvarchar(max);
    DECLARE @NormalizedConstraintDefinition nvarchar(max);
    DECLARE @RemainingConstraintDefinition nvarchar(max);
    DECLARE @RecognizedAllowList bit = 0;
    DECLARE @ReplacementConstraintName sysname = N'CK_App_HocVien_SourceProfileCode_Allowed';
    DECLARE @Sql nvarchar(max);

    IF @TargetObjectId IS NULL
    BEGIN
        THROW 527220, 'Missing required table dbo.App_HocVien.', 1;
    END;

    SELECT @TargetColumnId = column_id
    FROM sys.columns
    WHERE object_id = @TargetObjectId
      AND name = @ColumnName;

    IF @TargetColumnId IS NULL
    BEGIN
        THROW 527221, 'Missing required column dbo.App_HocVien.SourceProfileCode.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM dbo.App_HocVien
        WHERE SourceProfileCode IS NOT NULL
          AND SourceProfileCode NOT IN (
              N'DATA_V1',
              N'DATA_V2',
              N'CSDT_MOTO',
              N'CSDT_OTO'
          )
    )
    BEGIN
        THROW 527222, 'App_HocVien contains SourceProfileCode values outside the approved list.', 1;
    END;

    DECLARE @ApplicableConstraints table
    (
        ConstraintName sysname NOT NULL PRIMARY KEY,
        ConstraintDefinition nvarchar(max) NOT NULL,
        ReferencedColumnCount int NOT NULL
    );

    INSERT INTO @ApplicableConstraints
    (
        ConstraintName,
        ConstraintDefinition,
        ReferencedColumnCount
    )
    SELECT
        checkConstraint.name,
        checkConstraint.definition,
        CASE
            WHEN dependencySummary.ReferencedColumnCount = 0
             AND checkConstraint.parent_column_id = @TargetColumnId THEN 1
            ELSE dependencySummary.ReferencedColumnCount
        END
    FROM sys.check_constraints AS checkConstraint
    CROSS APPLY (
        SELECT COUNT(DISTINCT dependency.referenced_minor_id) AS ReferencedColumnCount
        FROM sys.sql_expression_dependencies AS dependency
        WHERE dependency.referencing_id = checkConstraint.object_id
          AND dependency.referenced_id = @TargetObjectId
          AND dependency.referenced_minor_id > 0
    ) AS dependencySummary
    WHERE checkConstraint.parent_object_id = @TargetObjectId
      AND (
          checkConstraint.parent_column_id = @TargetColumnId
          OR EXISTS (
              SELECT 1
              FROM sys.sql_expression_dependencies AS dependency
              WHERE dependency.referencing_id = checkConstraint.object_id
                AND dependency.referenced_id = @TargetObjectId
                AND dependency.referenced_minor_id = @TargetColumnId
          )
      );

    IF EXISTS (
        SELECT 1
        FROM @ApplicableConstraints
        WHERE ReferencedColumnCount <> 1
    )
    BEGIN
        THROW 527223, 'A SourceProfileCode CHECK constraint also references other columns; manual review is required.', 1;
    END;

    IF (SELECT COUNT(1) FROM @ApplicableConstraints) > 1
    BEGIN
        THROW 527224, 'Multiple CHECK constraints apply to SourceProfileCode; manual review is required.', 1;
    END;

    SELECT
        @ExistingConstraintName = ConstraintName,
        @ExistingConstraintDefinition = ConstraintDefinition
    FROM @ApplicableConstraints;

    IF @ExistingConstraintName IS NOT NULL
    BEGIN
        -- Only replace the known positive allow-list shape created by this repository.
        -- Any other one-column rule may carry unrelated validation and must be reviewed manually.
        SET @NormalizedConstraintDefinition = UPPER(@ExistingConstraintDefinition);
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, N' ', N'');
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, NCHAR(9), N'');
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, NCHAR(10), N'');
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, NCHAR(13), N'');
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, N'[', N'');
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, N']', N'');
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, N'(', N'');
        SET @NormalizedConstraintDefinition = REPLACE(@NormalizedConstraintDefinition, N')', N'');

        IF @NormalizedConstraintDefinition IN
        (
            N'SOURCEPROFILECODEISNULLORSOURCEPROFILECODEINN''DATA_V1'',N''DATA_V2''',
            N'SOURCEPROFILECODEISNULLORSOURCEPROFILECODEINN''DATA_V1'',N''DATA_V2'',N''CSDT_MOTO'',N''CSDT_OTO'''
        )
        BEGIN
            SET @RecognizedAllowList = 1;
        END;

        -- SQL Server can expose an IN allow-list in metadata as reordered OR equalities.
        -- Accept only the same known literal set and reject every unrecognized leftover token.
        IF @RecognizedAllowList = 0
           AND @NormalizedConstraintDefinition LIKE N'%SOURCEPROFILECODEISNULL%'
           AND @NormalizedConstraintDefinition LIKE N'%SOURCEPROFILECODE=N''DATA_V1''%'
           AND @NormalizedConstraintDefinition LIKE N'%SOURCEPROFILECODE=N''DATA_V2''%'
           AND
           (
               (
                   @NormalizedConstraintDefinition LIKE N'%SOURCEPROFILECODE=N''CSDT_MOTO''%'
                   AND @NormalizedConstraintDefinition LIKE N'%SOURCEPROFILECODE=N''CSDT_OTO''%'
               )
               OR
               (
                   @NormalizedConstraintDefinition NOT LIKE N'%SOURCEPROFILECODE=N''CSDT_MOTO''%'
                   AND @NormalizedConstraintDefinition NOT LIKE N'%SOURCEPROFILECODE=N''CSDT_OTO''%'
               )
           )
        BEGIN
            SET @RemainingConstraintDefinition = @NormalizedConstraintDefinition;
            SET @RemainingConstraintDefinition = REPLACE(@RemainingConstraintDefinition, N'SOURCEPROFILECODEISNULL', N'');
            SET @RemainingConstraintDefinition = REPLACE(@RemainingConstraintDefinition, N'SOURCEPROFILECODE=N''DATA_V1''', N'');
            SET @RemainingConstraintDefinition = REPLACE(@RemainingConstraintDefinition, N'SOURCEPROFILECODE=N''DATA_V2''', N'');
            SET @RemainingConstraintDefinition = REPLACE(@RemainingConstraintDefinition, N'SOURCEPROFILECODE=N''CSDT_MOTO''', N'');
            SET @RemainingConstraintDefinition = REPLACE(@RemainingConstraintDefinition, N'SOURCEPROFILECODE=N''CSDT_OTO''', N'');
            SET @RemainingConstraintDefinition = REPLACE(@RemainingConstraintDefinition, N'OR', N'');

            IF @RemainingConstraintDefinition = N''
            BEGIN
                SET @RecognizedAllowList = 1;
            END;
        END;

        IF @RecognizedAllowList = 0
        BEGIN
            THROW 527226, 'The SourceProfileCode CHECK constraint is not the recognized allow-list; manual review is required.', 1;
        END;

        SET @Sql = N'ALTER TABLE dbo.App_HocVien DROP CONSTRAINT ' +
            QUOTENAME(@ExistingConstraintName) + N';';
        EXEC sys.sp_executesql @Sql;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = @ReplacementConstraintName
          AND parent_object_id <> @TargetObjectId
    )
    BEGIN
        THROW 527225, 'Replacement CHECK constraint name is already used by another table.', 1;
    END;

    SET @Sql = N'
ALTER TABLE dbo.App_HocVien WITH CHECK
ADD CONSTRAINT ' + QUOTENAME(@ReplacementConstraintName) + N'
CHECK (
    SourceProfileCode IS NULL
    OR SourceProfileCode IN (
        N''DATA_V1'',
        N''DATA_V2'',
        N''CSDT_MOTO'',
        N''CSDT_OTO''
    )
);

ALTER TABLE dbo.App_HocVien
CHECK CONSTRAINT ' + QUOTENAME(@ReplacementConstraintName) + N';';

    EXEC sys.sp_executesql @Sql;

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

USE [$(CourseCompletionTargetDatabase)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF (OBJECT_ID(N'dbo.App_CourseCompletion',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.App_CourseCompletion))
 OR (OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.App_CourseCompletionLearnerSnapshot))
 OR (OBJECT_ID(N'dbo.App_CourseCompletionOperation',N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.App_CourseCompletionOperation))
    THROW 52120, 'COURSE_COMPLETION_V1_ROLLBACK_REFUSED_DATA_EXISTS', 1;

BEGIN TRANSACTION;
IF DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api') IS NOT NULL
BEGIN
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_CourseCompletion FROM qlhv_course_completion_api;
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_CourseCompletionLearnerSnapshot FROM qlhv_course_completion_api;
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_CourseCompletionOperation FROM qlhv_course_completion_api;
    REVOKE SELECT ON OBJECT::dbo.App_KhoaHoc FROM qlhv_course_completion_api;
    REVOKE INSERT ON OBJECT::dbo.App_AuditLog FROM qlhv_course_completion_api;
END;
IF OBJECT_ID(N'dbo.App_CourseCompletionOperation',N'U') IS NOT NULL DROP TABLE dbo.App_CourseCompletionOperation;
IF OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot',N'U') IS NOT NULL DROP TABLE dbo.App_CourseCompletionLearnerSnapshot;
IF OBJECT_ID(N'dbo.App_CourseCompletion',N'U') IS NOT NULL DROP TABLE dbo.App_CourseCompletion;
IF DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api') IS NOT NULL DROP ROLE qlhv_course_completion_api;
COMMIT TRANSACTION;
SELECT N'COURSE_COMPLETION_V1_EMPTY_SCHEMA_ROLLED_BACK' AS Evidence;
GO

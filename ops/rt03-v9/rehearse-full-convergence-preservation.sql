SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

IF TRY_CONVERT(bit,SESSION_CONTEXT(N'QLHV_DISPOSABLE_REHEARSAL'))<>1
    THROW 527790,'RT03_V9_DISPOSABLE_REHEARSAL_CONTEXT_REQUIRED',1;

DECLARE @TargetId bigint=(
    SELECT TOP(1) HocVienId
    FROM dbo.App_HocVien
    WHERE SourceProfileCode=N'CSDT_OTO' AND IsDeleted=0
    ORDER BY HocVienId);
IF @TargetId IS NULL THROW 527791,'RT03_V9_REHEARSAL_TARGET_REQUIRED',1;

DECLARE @Before varbinary(32)=(
    SELECT HASHBYTES('SHA2_256',CONVERT(varbinary(max),(
        SELECT NgayThuNhanAnh,V2RowHash,UpdatedAt,UpdatedBy
        FROM dbo.App_HocVien WHERE HocVienId=@TargetId
        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES))));

CREATE TABLE #QlhvFullSync_HocVien
(
    SourceProfileCode nvarchar(50) NOT NULL,
    SourceMaDK nvarchar(50) NOT NULL,
    NgayThuNhanAnh datetime2 NULL,
    V2RowHash nvarchar(64) NOT NULL,
    RetainReviewedTarget bit NOT NULL,
    PRIMARY KEY(SourceProfileCode,SourceMaDK)
);

INSERT #QlhvFullSync_HocVien
SELECT SourceProfileCode,SourceMaDK,
       DATEADD(day,1,COALESCE(NgayThuNhanAnh,CONVERT(datetime2,'2026-01-01'))),
       REPLICATE(N'A',64),1
FROM dbo.App_HocVien WHERE HocVienId=@TargetId;

MERGE dbo.App_HocVien WITH(HOLDLOCK) AS target
USING #QlhvFullSync_HocVien AS source
ON target.SourceProfileCode=source.SourceProfileCode
AND target.SourceMaDK=source.SourceMaDK
WHEN MATCHED AND source.RetainReviewedTarget=0 AND
    (ISNULL(target.V2RowHash,N'')<>ISNULL(source.V2RowHash,N''))
THEN UPDATE SET
    target.NgayThuNhanAnh=source.NgayThuNhanAnh,
    target.V2RowHash=source.V2RowHash,
    target.UpdatedAt=SYSDATETIME(),
    target.UpdatedBy=N'RT03_V9_DISPOSABLE_REHEARSAL';

DECLARE @After varbinary(32)=(
    SELECT HASHBYTES('SHA2_256',CONVERT(varbinary(max),(
        SELECT NgayThuNhanAnh,V2RowHash,UpdatedAt,UpdatedBy
        FROM dbo.App_HocVien WHERE HocVienId=@TargetId
        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES))));
IF @Before<>@After THROW 527792,'RT03_V9_RETAINED_TARGET_MUTATED',1;

SELECT N'PASS' Result,N'FULL_CONVERGENCE_RETAINED_TARGET_PRESERVED' Contract;

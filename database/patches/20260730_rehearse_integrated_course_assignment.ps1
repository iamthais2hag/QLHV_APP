[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$ServerName = 'CSDLTTTC'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath '..\..'))
$migrationPath = [IO.Path]::GetFullPath((Join-Path `
    -Path $PSScriptRoot `
    -ChildPath '20260730_add_integrated_course_assignment.sql'))
$rollbackPath = [IO.Path]::GetFullPath((Join-Path `
    -Path $PSScriptRoot `
    -ChildPath '20260730_rollback_integrated_course_assignment.sql'))
$databaseName = (
    'QLHV_ASSIGNMENT_REHEARSAL_' +
    [Guid]::NewGuid().ToString('N').Substring(0, 12).ToUpperInvariant())

if ([string]::IsNullOrWhiteSpace($ServerName) -or
    [string]::IsNullOrWhiteSpace($repositoryRoot) -or
    [string]::IsNullOrWhiteSpace($migrationPath) -or
    [string]::IsNullOrWhiteSpace($rollbackPath)) {
    throw 'A required rehearsal argument/path is empty.'
}

if ($databaseName -notmatch
    '^QLHV_ASSIGNMENT_REHEARSAL_[0-9A-F]{12}$') {
    throw 'The generated rehearsal database name is outside the allowlist.'
}

if ($databaseName -eq 'QLHV_APP') {
    throw 'The production database is never a valid rehearsal target.'
}

foreach ($path in @($migrationPath, $rollbackPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required patch is missing: $path"
    }
}

function Open-RehearsalConnection {
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Catalog
    )

    $builder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = $ServerName
    $builder['Initial Catalog'] = $Catalog
    $builder['Integrated Security'] = $true
    $builder['Encrypt'] = $false
    $builder['Application Name'] =
        'QLHV.AssignmentMigration.IsolatedRehearsal'
    $builder['Pooling'] = $false

    $connection =
        [Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open()
        return $connection
    }
    catch {
        $connection.Dispose()
        throw
    }
}

function Invoke-RehearsalSql {
    param(
        [Parameter(Mandatory)]
        [Data.SqlClient.SqlConnection]$Connection,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Sql
    )

    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 60
        $command.CommandText = $Sql
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $command.Dispose()
    }
}

function Read-RehearsalScalar {
    param(
        [Parameter(Mandatory)]
        [Data.SqlClient.SqlConnection]$Connection,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Sql
    )

    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 60
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
}

function Invoke-RehearsalPatch {
    param(
        [Parameter(Mandatory)]
        [Data.SqlClient.SqlConnection]$Connection,

        [Parameter(Mandatory)]
        [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
        [string]$Path
    )

    $sql = [IO.File]::ReadAllText($Path)
    $usePattern = '(?im)^\s*USE\s+\[QLHV_APP\];\s*$'
    if ([regex]::Matches($sql, $usePattern).Count -ne 1) {
        throw "Patch must contain exactly one USE [QLHV_APP]: $Path"
    }

    $sql = [regex]::Replace(
        $sql,
        $usePattern,
        "USE [$databaseName];",
        1)

    if ($sql -match $usePattern) {
        throw "Production USE target remained after rewrite: $Path"
    }

    foreach ($batch in [regex]::Split(
        $sql,
        '(?im)^\s*GO\s*(?:--.*)?$')) {
        if (-not [string]::IsNullOrWhiteSpace($batch)) {
            Invoke-RehearsalSql -Connection $Connection -Sql $batch
        }
    }
}

function Assert-RehearsalSqlError {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Action,

        [Parameter(Mandatory)]
        [int]$ExpectedNumber,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Label
    )

    $captured = $null
    try {
        & $Action
    }
    catch {
        $captured = $_.Exception
        while ($null -ne $captured -and
               $captured -isnot [Data.SqlClient.SqlException]) {
            $captured = $captured.InnerException
        }
    }

    if ($null -eq $captured) {
        throw "Expected SQL rejection was not raised: $Label"
    }

    if ($captured.Number -ne $ExpectedNumber) {
        throw (
            "Unexpected SQL error for {0}: expected={1}; observed={2}; {3}" -f
            $Label,
            $ExpectedNumber,
            $captured.Number,
            $captured.Message)
    }

    Write-Output (
        'REHEARSAL_EXPECTED_REJECTION label={0} error={1}' -f
        $Label,
        $captured.Number)
}

$fixtureSql = @'
CREATE TABLE dbo.App_HocVien
(
    HocVienId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MaDK nvarchar(50) NOT NULL,
    MaKhoa nvarchar(50) NULL,
    SourceProfileCode nvarchar(50) NULL,
    RowVersion rowversion NOT NULL
);
CREATE TABLE dbo.App_KhoaHoc
(
    KhoaHocId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MaKhoa nvarchar(50) NOT NULL UNIQUE,
    SourceProfileCode nvarchar(50) NULL,
    RowVersion rowversion NOT NULL
);
CREATE TABLE dbo.App_GiaoVien
(
    GiaoVienId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RowVersion rowversion NOT NULL
);
CREATE TABLE dbo.App_XeTap
(
    XeTapId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RowVersion rowversion NOT NULL
);
CREATE TABLE dbo.App_ImportBatch
(
    ImportBatchId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    EntityType nvarchar(50) NOT NULL,
    RowVersion rowversion NOT NULL
);
CREATE TABLE dbo.App_AuditLog
(
    AuditLogId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ChucNang nvarchar(100) NOT NULL,
    HanhDong nvarchar(100) NOT NULL,
    EntityType nvarchar(100) NULL,
    EntityId nvarchar(100) NULL,
    EntityKey nvarchar(255) NULL,
    DuLieuTruoc nvarchar(max) NULL,
    DuLieuSau nvarchar(max) NULL,
    KetQua nvarchar(50) NULL,
    Loi nvarchar(max) NULL,
    CreatedAt datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(100) NULL,
    ClientIp nvarchar(100) NULL,
    UserAgent nvarchar(1000) NULL
);
CREATE USER [NT SERVICE\QLHV_APP_RealtimeWorker] WITHOUT LOGIN;
'@

$seedSql = @'
INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode)
VALUES (N'OTO-K1',N'CSDT_OTO'),(N'MOTO-K1',N'CSDT_MOTO');
INSERT dbo.App_HocVien(MaDK,MaKhoa,SourceProfileCode)
VALUES
    (N'OTO-DK1',N'OTO-K1',N'CSDT_OTO'),
    (N'OTO-DK2',N'OTO-K1',N'CSDT_OTO'),
    (N'OTO-DK3',N'OTO-K1',N'CSDT_OTO'),
    (N'MOTO-DK1',N'MOTO-K1',N'CSDT_MOTO');
INSERT dbo.App_GiaoVien DEFAULT VALUES;
INSERT dbo.App_GiaoVien_hs
    (MaGiaoVienHs,HoTen,HoTenSearch,CreatedBy)
VALUES
    (N'HS01',N'Nguyen Van A',N'NGUYEN VAN A',N'rehearsal');
INSERT dbo.App_KhoaHoc_NhomDaoTao
    (KhoaHocId,MaNhom,TenNhom,CreatedBy)
SELECT KhoaHocId,N'N1',N'Nhom 1',N'rehearsal'
FROM dbo.App_KhoaHoc
WHERE MaKhoa=N'OTO-K1';
INSERT dbo.App_KhoaHoc_NhomDaoTao
    (KhoaHocId,MaNhom,TenNhom,CreatedBy)
SELECT KhoaHocId,N'N2',N'Nhom 2',N'rehearsal'
FROM dbo.App_KhoaHoc
WHERE MaKhoa=N'MOTO-K1';
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,NhomDaoTaoId,NguonGan,CreatedBy)
SELECT learner.HocVienId,groupRow.NhomDaoTaoId,'GROUP',N'rehearsal'
FROM dbo.App_HocVien AS learner
INNER JOIN dbo.App_KhoaHoc_NhomDaoTao AS groupRow
    ON groupRow.MaNhom=N'N1'
WHERE learner.MaDK=N'OTO-DK1';
'@

$crossProfileSql = @'
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,NhomDaoTaoId,NguonGan,CreatedBy)
SELECT learner.HocVienId,groupRow.NhomDaoTaoId,'GROUP',N'rehearsal'
FROM dbo.App_HocVien AS learner
INNER JOIN dbo.App_KhoaHoc_NhomDaoTao AS groupRow
    ON groupRow.MaNhom=N'N1'
WHERE learner.MaDK=N'MOTO-DK1';
'@

$invalidInheritedGroupDefaultSql = @'
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,NhomDaoTaoId,GiaoVienDungLopId,NguonGan,CreatedBy)
SELECT
    learner.HocVienId,
    groupRow.NhomDaoTaoId,
    teacher.GiaoVienId,
    'GROUP',
    N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien AS teacher
CROSS JOIN dbo.App_KhoaHoc_NhomDaoTao AS groupRow
WHERE learner.MaDK=N'OTO-DK3'
  AND groupRow.MaNhom=N'N1';
'@

$wrongImportSql = @'
INSERT dbo.App_HocVien_PhanCong
(
    HocVienId,
    GiaoVienHoSoId,
    IsGiaoVienDungLopOverride,
    IsXeTapOverride,
    IsXeBaiSo10Override,
    NguonGan,
    ImportSessionId,
    CreatedBy
)
SELECT
    learner.HocVienId,
    receiver.GiaoVienHsId,
    1,
    1,
    1,
    'EXCEL',
    importBatch.ImportBatchId,
    N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien_hs AS receiver
CROSS JOIN dbo.App_ImportBatch AS importBatch
WHERE learner.MaDK=N'MOTO-DK1'
  AND importBatch.EntityType=N'OTHER';
'@

$invalidNoGroupInheritanceSql = @'
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,GiaoVienDungLopId,NguonGan,CreatedBy)
SELECT learner.HocVienId,teacher.GiaoVienId,'MANUAL',N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien AS teacher
WHERE learner.MaDK=N'MOTO-DK1';
'@

$invalidNoGroupOverrideFlagsSql = @'
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,GiaoVienHoSoId,IsGiaoVienDungLopOverride,IsXeTapOverride,
     IsXeBaiSo10Override,NguonGan,CreatedBy)
SELECT learner.HocVienId,receiver.GiaoVienHsId,1,0,1,'MANUAL',N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien_hs AS receiver
WHERE learner.MaDK=N'OTO-DK3';
'@

$master = $null
$target = $null
try {
    $master = Open-RehearsalConnection -Catalog 'master'
    Invoke-RehearsalSql -Connection $master -Sql (
        "CREATE DATABASE [$databaseName] " +
        'COLLATE SQL_Latin1_General_CP1_CI_AS;')

    $target = Open-RehearsalConnection -Catalog $databaseName
    Invoke-RehearsalSql -Connection $target -Sql $fixtureSql

    Invoke-RehearsalPatch -Connection $target -Path $migrationPath
    $foreignKeyCount = [int](Read-RehearsalScalar -Connection $target -Sql @'
SELECT COUNT(*)
FROM sys.foreign_keys
WHERE parent_object_id IN
(
    OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao'),
    OBJECT_ID(N'dbo.App_HocVien_PhanCong'),
    OBJECT_ID(N'dbo.App_AssignmentOperation')
);
'@)
    $denyCount = [int](Read-RehearsalScalar -Connection $target -Sql @'
SELECT COUNT(*)
FROM sys.database_permissions
WHERE grantee_principal_id =
      DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker')
  AND state = N'D'
  AND permission_name IN (N'INSERT',N'UPDATE',N'DELETE')
  AND major_id IN
  (
      OBJECT_ID(N'dbo.App_GiaoVien_hs'),
      OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao'),
      OBJECT_ID(N'dbo.App_HocVien_PhanCong'),
      OBJECT_ID(N'dbo.App_ImportBatch'),
      OBJECT_ID(N'dbo.App_AssignmentOperation')
  );
'@)
    $assignmentApiGrantCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.database_permissions
WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
  AND state IN (N'G',N'W')
  AND class=1;
'@)
    $assignmentApiDeleteCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.database_permissions
WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
  AND state IN (N'G',N'W')
  AND permission_name=N'DELETE';
'@)
    $primaryKeyTypeCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.columns
WHERE
(
    (object_id=OBJECT_ID(N'dbo.App_GiaoVien_hs')
     AND name=N'GiaoVienHsId')
    OR
    (object_id=OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao')
     AND name=N'NhomDaoTaoId')
    OR
    (object_id=OBJECT_ID(N'dbo.App_HocVien_PhanCong')
     AND name=N'PhanCongId')
    OR
    (object_id=OBJECT_ID(N'dbo.App_AssignmentOperation')
     AND name=N'AssignmentOperationId')
)
  AND system_type_id=127
  AND max_length=8
  AND precision=19
  AND scale=0
  AND is_nullable=0
  AND is_identity=1;
'@)
    $rowVersionCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.columns
WHERE object_id IN
(
    OBJECT_ID(N'dbo.App_GiaoVien_hs'),
    OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao'),
    OBJECT_ID(N'dbo.App_HocVien_PhanCong'),
    OBJECT_ID(N'dbo.App_AssignmentOperation')
)
  AND name=N'RowVersion'
  AND system_type_id=189
  AND max_length=8
  AND is_nullable=0;
'@)
    $importTypeCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.columns
WHERE object_id=OBJECT_ID(N'dbo.App_ImportBatch')
  AND is_nullable=1
  AND
  (
      (name=N'FileSha256' AND system_type_id=175 AND max_length=64)
      OR
      (name=N'TemplateVersion' AND system_type_id=167 AND max_length=40)
      OR
      (name=N'NormalizationVersion' AND system_type_id=167 AND max_length=40)
      OR
      (name=N'PreviewExpiresAtUtc' AND system_type_id=42 AND scale=7)
      OR
      (name=N'ConfirmedAtUtc' AND system_type_id=42 AND scale=7)
      OR
      (name=N'IdempotencyKey' AND system_type_id=231 AND max_length=200)
  );
'@)
    $ledgerUniqueIndexCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.indexes
WHERE object_id=OBJECT_ID(N'dbo.App_AssignmentOperation')
  AND name=N'UQ_App_AssignmentOperation_IdempotencyKey'
  AND is_unique=1;
'@)
    if ($foreignKeyCount -ne 14 -or
        $denyCount -ne 15 -or
        $assignmentApiGrantCount -ne 20 -or
        $assignmentApiDeleteCount -ne 0 -or
        $primaryKeyTypeCount -ne 4 -or
        $rowVersionCount -ne 4 -or
        $importTypeCount -ne 6 -or
        $ledgerUniqueIndexCount -ne 1) {
        throw (
            'Migration verification mismatch: foreignKeys={0}; denies={1}; apiGrants={2}; apiDeletes={3}; primaryKeys={4}; rowVersions={5}; importTypes={6}; ledgerUniqueIndexes={7}' -f
            $foreignKeyCount,
            $denyCount,
            $assignmentApiGrantCount,
            $assignmentApiDeleteCount,
            $primaryKeyTypeCount,
            $rowVersionCount,
            $importTypeCount,
            $ledgerUniqueIndexCount)
    }
    Write-Output (
        'REHEARSAL_MIGRATION_PASS database={0} foreignKeys={1} denies={2} apiGrants={3} apiDeletes={4} primaryKeys={5} rowVersions={6} importTypes={7} ledgerUniqueIndexes={8}' -f
        $databaseName,
        $foreignKeyCount,
        $denyCount,
        $assignmentApiGrantCount,
        $assignmentApiDeleteCount,
        $primaryKeyTypeCount,
        $rowVersionCount,
        $importTypeCount,
        $ledgerUniqueIndexCount)

    Invoke-RehearsalSql -Connection $target -Sql @'
CREATE USER [QLHV_AssignmentRehearsalUser] WITHOUT LOGIN;
ALTER ROLE [QLHV_AssignmentApiRole] ADD MEMBER [QLHV_AssignmentRehearsalUser];
'@
    Assert-RehearsalSqlError `
        -ExpectedNumber 529361 `
        -Label 'assignment-api-role-membership-rollback-guard' `
        -Action {
            Invoke-RehearsalPatch -Connection $target -Path $rollbackPath
        }
    Invoke-RehearsalSql -Connection $target -Sql @'
ALTER ROLE [QLHV_AssignmentApiRole] DROP MEMBER [QLHV_AssignmentRehearsalUser];
DROP USER [QLHV_AssignmentRehearsalUser];
'@

    Invoke-RehearsalPatch -Connection $target -Path $rollbackPath
    $remainingTableCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.tables
WHERE name IN
(
    N'App_GiaoVien_hs',
    N'App_KhoaHoc_NhomDaoTao',
    N'App_HocVien_PhanCong',
    N'App_AssignmentOperation'
);
'@)
    $remainingColumnCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql @'
SELECT COUNT(*)
FROM sys.columns
WHERE object_id=OBJECT_ID(N'dbo.App_ImportBatch')
  AND name IN
  (
      N'FileSha256',
      N'TemplateVersion',
      N'NormalizationVersion',
      N'PreviewExpiresAtUtc',
      N'ConfirmedAtUtc',
      N'IdempotencyKey'
  );
'@)
    $remainingRoleCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'QLHV_AssignmentApiRole';")
    if ($remainingTableCount -ne 0 -or
        $remainingColumnCount -ne 0 -or
        $remainingRoleCount -ne 0) {
        throw (
            'Rollback verification mismatch: tables={0}; columns={1}; roles={2}' -f
            $remainingTableCount,
            $remainingColumnCount,
            $remainingRoleCount)
    }
    Write-Output (
        'REHEARSAL_EMPTY_ROLLBACK_PASS database={0}' -f
        $databaseName)

    Invoke-RehearsalPatch -Connection $target -Path $migrationPath
    Invoke-RehearsalSql -Connection $target -Sql $seedSql

    Invoke-RehearsalSql -Connection $target -Sql @'
CREATE TABLE dbo.AssignmentIdempotencyRehearsalMutation
(
    MutationId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CreatedAt datetime2(7) NOT NULL DEFAULT (SYSUTCDATETIME())
);
'@
    Invoke-RehearsalSql -Connection $target -Sql @'
CREATE PROCEDURE dbo.AssignmentIdempotencyRehearsalConfirm
    @IdempotencyKeySha256 char(64),
    @OperationId nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    DECLARE @Resource nvarchar(255)=
        N'QLHV:ASSIGNMENT:' + @IdempotencyKeySha256;
    EXEC @LockResult=sys.sp_getapplock
        @Resource=@Resource,
        @LockMode=N'Exclusive',
        @LockOwner=N'Transaction',
        @LockTimeout=10000;
    IF @LockResult<0
        THROW 529399, 'Rehearsal idempotency lock failed.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_AssignmentOperation WITH (UPDLOCK,HOLDLOCK)
        WHERE IdempotencyKeySha256=@IdempotencyKeySha256
    )
    BEGIN
        DECLARE @Now datetime2(7)=SYSUTCDATETIME();
        INSERT dbo.AssignmentIdempotencyRehearsalMutation DEFAULT VALUES;
        INSERT dbo.App_AssignmentOperation
            (IdempotencyKeySha256,OperationType,CourseId,SourceProfileCode,
             ScopeId,Actor,PayloadSha256,PreviewTokenSha256,OperationId,
             ImportBatchId,ChangedCount,NoChangeCount,RequiresBulkPermission,
             CompletedAtUtc,RetainUntilUtc,CreatedAt)
        SELECT
            @IdempotencyKeySha256,'ASSIGNMENT',KhoaHocId,SourceProfileCode,
            NULL,N'rehearsal-operator',REPLICATE('A',64),REPLICATE('B',64),
            @OperationId,NULL,1,0,0,@Now,DATEADD(day,180,@Now),@Now
        FROM dbo.App_KhoaHoc
        WHERE MaKhoa=N'OTO-K1';
    END;
    COMMIT TRANSACTION;
END;
'@

    $concurrentConnection1 = $null
    $concurrentConnection2 = $null
    $concurrentCommand1 = $null
    $concurrentCommand2 = $null
    try {
        $concurrentConnection1 = Open-RehearsalConnection -Catalog $databaseName
        $concurrentConnection2 = Open-RehearsalConnection -Catalog $databaseName
        $concurrentSql = (
            'EXEC dbo.AssignmentIdempotencyRehearsalConfirm ' +
            '@IdempotencyKeySha256=@KeyHash,' +
            '@OperationId=@OperationId;')
        $concurrentCommand1 = $concurrentConnection1.CreateCommand()
        $concurrentCommand1.CommandText = $concurrentSql
        $concurrentCommand1.CommandTimeout = 30
        [void]$concurrentCommand1.Parameters.AddWithValue(
            '@KeyHash',
            ('C' * 64))
        [void]$concurrentCommand1.Parameters.AddWithValue(
            '@OperationId',
            'concurrent-operation-1')
        $concurrentCommand2 = $concurrentConnection2.CreateCommand()
        $concurrentCommand2.CommandText = $concurrentSql
        $concurrentCommand2.CommandTimeout = 30
        [void]$concurrentCommand2.Parameters.AddWithValue(
            '@KeyHash',
            ('C' * 64))
        [void]$concurrentCommand2.Parameters.AddWithValue(
            '@OperationId',
            'concurrent-operation-2')

        $concurrentTask1 = $concurrentCommand1.ExecuteNonQueryAsync()
        $concurrentTask2 = $concurrentCommand2.ExecuteNonQueryAsync()
        [void]$concurrentTask1.GetAwaiter().GetResult()
        [void]$concurrentTask2.GetAwaiter().GetResult()
    }
    finally {
        if ($null -ne $concurrentCommand1) {
            $concurrentCommand1.Dispose()
        }
        if ($null -ne $concurrentCommand2) {
            $concurrentCommand2.Dispose()
        }
        if ($null -ne $concurrentConnection1) {
            $concurrentConnection1.Dispose()
        }
        if ($null -ne $concurrentConnection2) {
            $concurrentConnection2.Dispose()
        }
    }

    $concurrentMutationCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql 'SELECT COUNT(*) FROM dbo.AssignmentIdempotencyRehearsalMutation;')
    $concurrentLedgerCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql (
            'SELECT COUNT(*) FROM dbo.App_AssignmentOperation ' +
            "WHERE IdempotencyKeySha256=REPLICATE('C',64);"))
    if ($concurrentMutationCount -ne 1 -or $concurrentLedgerCount -ne 1) {
        throw (
            'Concurrent idempotency mismatch: mutations={0}; ledgerRows={1}' -f
            $concurrentMutationCount,
            $concurrentLedgerCount)
    }
    Write-Output 'REHEARSAL_IDEMPOTENCY_CONCURRENT_SINGLE_FLIGHT_PASS'

    Invoke-RehearsalSql -Connection $target -Sql @'
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Now datetime2(7)=SYSUTCDATETIME();
    INSERT dbo.AssignmentIdempotencyRehearsalMutation DEFAULT VALUES;
    INSERT dbo.App_AssignmentOperation
        (IdempotencyKeySha256,OperationType,CourseId,SourceProfileCode,
         ScopeId,Actor,PayloadSha256,PreviewTokenSha256,OperationId,
         ImportBatchId,ChangedCount,NoChangeCount,RequiresBulkPermission,
         CompletedAtUtc,RetainUntilUtc,CreatedAt)
    SELECT
        REPLICATE('D',64),'ASSIGNMENT',KhoaHocId,SourceProfileCode,NULL,
        N'rehearsal-operator',REPLICATE('A',64),REPLICATE('B',64),
        N'rolled-back-operation',NULL,1,0,0,@Now,DATEADD(day,180,@Now),@Now
    FROM dbo.App_KhoaHoc
    WHERE MaKhoa=N'OTO-K1';
    THROW 529398, 'Expected rehearsal rollback.', 1;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    IF ERROR_NUMBER()<>529398 THROW;
END CATCH;

IF EXISTS
   (SELECT 1 FROM dbo.App_AssignmentOperation
    WHERE IdempotencyKeySha256=REPLICATE('D',64))
    THROW 529397, 'Rolled-back transaction left a completed ledger row.', 1;
'@
    Invoke-RehearsalSql -Connection $target -Sql @'
DECLARE @Now datetime2(7)=SYSUTCDATETIME();
INSERT dbo.App_AssignmentOperation
    (IdempotencyKeySha256,OperationType,CourseId,SourceProfileCode,
     ScopeId,Actor,PayloadSha256,PreviewTokenSha256,OperationId,
     ImportBatchId,ChangedCount,NoChangeCount,RequiresBulkPermission,
     CompletedAtUtc,RetainUntilUtc,CreatedAt)
SELECT
    REPLICATE('D',64),'ASSIGNMENT',KhoaHocId,SourceProfileCode,NULL,
    N'rehearsal-operator',REPLICATE('A',64),REPLICATE('B',64),
    N'retry-after-rollback',NULL,0,1,0,@Now,DATEADD(day,180,@Now),@Now
FROM dbo.App_KhoaHoc
WHERE MaKhoa=N'OTO-K1';
'@
    $retryLedgerCount = [int](Read-RehearsalScalar `
        -Connection $target `
        -Sql (
            'SELECT COUNT(*) FROM dbo.App_AssignmentOperation ' +
            "WHERE IdempotencyKeySha256=REPLICATE('D',64) " +
            'AND ChangedCount=0 AND NoChangeCount=1;'))
    if ($retryLedgerCount -ne 1) {
        throw 'Failed-transaction retry/NO_CHANGE replay ledger verification failed.'
    }
    Write-Output 'REHEARSAL_IDEMPOTENCY_ROLLBACK_RETRY_NO_CHANGE_PASS'

    Assert-RehearsalSqlError `
        -ExpectedNumber 2627 `
        -Label 'idempotency-key-different-payload-scope-operation' `
        -Action {
            Invoke-RehearsalSql -Connection $target -Sql @'
DECLARE @Now datetime2(7)=SYSUTCDATETIME();
INSERT dbo.App_AssignmentOperation
    (IdempotencyKeySha256,OperationType,CourseId,SourceProfileCode,
     ScopeId,Actor,PayloadSha256,PreviewTokenSha256,OperationId,
     ImportBatchId,ChangedCount,NoChangeCount,RequiresBulkPermission,
     CompletedAtUtc,RetainUntilUtc,CreatedAt)
SELECT
    REPLICATE('D',64),'GROUP_DEFAULTS',course.KhoaHocId,
    course.SourceProfileCode,groupRow.NhomDaoTaoId,N'other-operator',
    REPLICATE('E',64),REPLICATE('F',64),N'conflicting-operation',
    NULL,0,1,0,@Now,DATEADD(day,180,@Now),@Now
FROM dbo.App_KhoaHoc AS course
JOIN dbo.App_KhoaHoc_NhomDaoTao AS groupRow
  ON groupRow.KhoaHocId=course.KhoaHocId
WHERE course.MaKhoa=N'OTO-K1' AND groupRow.MaNhom=N'N1';
'@
        }
    Write-Output 'REHEARSAL_IDEMPOTENCY_GLOBAL_CONFLICT_PASS'

    Assert-RehearsalSqlError `
        -ExpectedNumber 529333 `
        -Label 'cross-profile-group-course' `
        -Action {
            Invoke-RehearsalSql `
                -Connection $target `
                -Sql $crossProfileSql
        }

    Invoke-RehearsalSql -Connection $target -Sql @'
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,GiaoVienHoSoId,IsGiaoVienDungLopOverride,IsXeTapOverride,
     IsXeBaiSo10Override,NguonGan,CreatedBy)
SELECT learner.HocVienId,receiver.GiaoVienHsId,1,1,1,'MANUAL',N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien_hs AS receiver
WHERE learner.MaDK=N'OTO-DK2';
'@
    Write-Output 'REHEARSAL_DOSSIER_ONLY_NO_GROUP_PASS'

    Assert-RehearsalSqlError `
        -ExpectedNumber 2601 `
        -Label 'one-current-snapshot' `
        -Action {
            Invoke-RehearsalSql -Connection $target -Sql @'
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,GiaoVienHoSoId,IsGiaoVienDungLopOverride,IsXeTapOverride,
     IsXeBaiSo10Override,NguonGan,CreatedBy)
SELECT learner.HocVienId,receiver.GiaoVienHsId,1,1,1,'MANUAL',N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien_hs AS receiver
WHERE learner.MaDK=N'OTO-DK2';
'@
        }

    Assert-RehearsalSqlError `
        -ExpectedNumber 547 `
        -Label 'no-group-inherited-reference' `
        -Action {
            Invoke-RehearsalSql `
                -Connection $target `
                -Sql $invalidNoGroupInheritanceSql
        }

    Assert-RehearsalSqlError `
        -ExpectedNumber 547 `
        -Label 'no-group-partial-override-flags' `
        -Action {
            Invoke-RehearsalSql `
                -Connection $target `
                -Sql $invalidNoGroupOverrideFlagsSql
        }

    Assert-RehearsalSqlError `
        -ExpectedNumber 529334 `
        -Label 'inherited-value-group-default-mismatch' `
        -Action {
            Invoke-RehearsalSql `
                -Connection $target `
                -Sql $invalidInheritedGroupDefaultSql
        }

    Invoke-RehearsalSql `
        -Connection $target `
        -Sql "INSERT dbo.App_ImportBatch(EntityType) VALUES (N'OTHER');"
    Assert-RehearsalSqlError `
        -ExpectedNumber 529335 `
        -Label 'wrong-import-entity' `
        -Action {
            Invoke-RehearsalSql -Connection $target -Sql $wrongImportSql
        }

    Assert-RehearsalSqlError `
        -ExpectedNumber 529336 `
        -Label 'snapshot-overwrite' `
        -Action {
            Invoke-RehearsalSql `
                -Connection $target `
                -Sql (
                    'UPDATE dbo.App_HocVien_PhanCong ' +
                    "SET GhiChu=N'overwrite';")
        }

    Assert-RehearsalSqlError `
        -ExpectedNumber 529337 `
        -Label 'assignment-hard-delete' `
        -Action {
            Invoke-RehearsalSql `
                -Connection $target `
                -Sql 'DELETE FROM dbo.App_HocVien_PhanCong;'
        }

    Invoke-RehearsalSql -Connection $target -Sql @'
DECLARE @ClosedAtUtc datetime2(7) =
(
    SELECT DATEADD(millisecond, 1, MAX(NgayHieuLuc))
    FROM dbo.App_HocVien_PhanCong
    WHERE IsCurrent = 1
);
UPDATE dbo.App_HocVien_PhanCong
SET
    IsCurrent = 0,
    NgayHetHieuLuc = @ClosedAtUtc,
    UpdatedAt = @ClosedAtUtc,
    UpdatedBy = N'rehearsal-close'
WHERE IsCurrent = 1;
'@
    Write-Output 'REHEARSAL_SNAPSHOT_CLOSE_PASS'

    Assert-RehearsalSqlError `
        -ExpectedNumber 529355 `
        -Label 'rollback-with-data-history' `
        -Action {
            Invoke-RehearsalPatch `
                -Connection $target `
                -Path $rollbackPath
        }

    Write-Output (
        'ASSIGNMENT_MIGRATION_REHEARSAL_PASS database={0}' -f
        $databaseName)
}
finally {
    if ($null -ne $target) {
        $target.Dispose()
    }

    if ($null -eq $master) {
        $master = Open-RehearsalConnection -Catalog 'master'
    }

    try {
        $exists = [int](Read-RehearsalScalar `
            -Connection $master `
            -Sql (
                'SELECT COUNT(*) FROM sys.databases ' +
                "WHERE name=N'$databaseName';"))
        if ($exists -eq 1) {
            Invoke-RehearsalSql -Connection $master -Sql (
                "ALTER DATABASE [$databaseName] " +
                'SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ' +
                "DROP DATABASE [$databaseName];")
            Write-Output (
                'REHEARSAL_CLEANUP_PASS database={0}' -f
                $databaseName)
        }
    }
    finally {
        if ($null -ne $master) {
            $master.Dispose()
        }
    }
}

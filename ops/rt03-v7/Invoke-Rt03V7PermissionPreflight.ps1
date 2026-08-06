[CmdletBinding()]
param(
    [ValidateSet('Baseline', 'Applied')]
    [string]$Mode = 'Baseline',

    [string]$ServerInstance = 'CSDLTTTC',

    [int]$TargetDatabaseId = 12,
    [guid]$TargetDatabaseGuid = '9c44b304-8a84-4d0d-9a82-19c7233ff6bb',
    [int]$OtoDatabaseId = 9,
    [guid]$OtoDatabaseGuid = '9a8b9bc1-18f3-4823-8123-3dc197a9d540',
    [int]$MotoDatabaseId = 8,
    [guid]$MotoDatabaseGuid = '308bdda8-80f3-4acb-9836-578d80a9e98e',

    [string]$TargetSchemaFingerprint =
        'C1572874BA588ECA0707979ED4D6825047EAF140620126C24780390CB75A7BF3',
    [string]$SourceSchemaFingerprint =
        'E401670E788C0C3702E3268089599E2F882388529B2B1BFFB0DAD7D10D26E65D',

    [string]$OutputPath,
    [switch]$SkipWorkerServiceCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$principal = 'NT SERVICE\QLHV_APP_RealtimeWorker'
$workerRole = 'QLHV_RealtimeWorkerRole'
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [string]$Name,
        [bool]$Passed,
        [object]$Observed,
        [object]$Expected
    )

    $script:checks.Add([pscustomobject]@{
        name     = $Name
        passed   = $Passed
        observed = $Observed
        expected = $Expected
    })
}

function Invoke-ReadOnlySql {
    param(
        [string]$Database,
        [string]$Sql
    )

    $connectionString =
        "Server=$ServerInstance;Database=$Database;Integrated Security=True;" +
        'Encrypt=False;TrustServerCertificate=True;Application Name=RT03-V7-ReadOnly-Preflight'
    $connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 60
        $reader = $command.ExecuteReader()
        try {
            $table = [System.Data.DataTable]::new()
            $table.Load($reader)
            return $table
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Convert-ToPermissionKey {
    param([object]$Permission)

    return '{0}|{1}|{2}|{3}' -f
        $Permission.StateDesc,
        $Permission.PermissionName,
        $Permission.SchemaName,
        $Permission.ObjectName
}

function Test-ExactSet {
    param(
        [string[]]$Expected,
        [string[]]$Actual
    )

    $expectedSorted = @($Expected | Sort-Object -Unique)
    $actualSorted = @($Actual | Sort-Object -Unique)
    return ($expectedSorted.Count -eq $actualSorted.Count) -and
        (@(Compare-Object $expectedSorted $actualSorted).Count -eq 0)
}

function Get-DatabaseIdentity {
    param([string]$Database)

    $escaped = $Database.Replace(']', ']]')
    $sql = @"
SELECT
    DB_ID(N'$($Database.Replace("'", "''"))') AS DatabaseId,
    CONVERT(nvarchar(36), recovery.database_guid) AS DatabaseGuid
FROM [$escaped].sys.database_recovery_status recovery
WHERE recovery.database_id = DB_ID(N'$($Database.Replace("'", "''"))');
"@
    return @(Invoke-ReadOnlySql -Database master -Sql $sql)[0]
}

function Get-RoleMemberships {
    param([string]$Database)

    $sql = @"
SELECT roleRow.name AS RoleName
FROM sys.database_role_members memberRow
INNER JOIN sys.database_principals roleRow
  ON roleRow.principal_id = memberRow.role_principal_id
INNER JOIN sys.database_principals memberPrincipal
  ON memberPrincipal.principal_id = memberRow.member_principal_id
WHERE memberPrincipal.name = N'$($principal.Replace("'", "''"))'
ORDER BY roleRow.name;
"@
    return @(
        Invoke-ReadOnlySql -Database $Database -Sql $sql |
            ForEach-Object { [string]$_.RoleName }
    )
}

function Get-WorkerRoleMembers {
    param([string]$Database)

    $sql = @"
SELECT memberPrincipal.name AS MemberName
FROM sys.database_role_members memberRow
INNER JOIN sys.database_principals roleRow
  ON roleRow.principal_id = memberRow.role_principal_id
INNER JOIN sys.database_principals memberPrincipal
  ON memberPrincipal.principal_id = memberRow.member_principal_id
WHERE roleRow.name = N'$workerRole'
ORDER BY memberPrincipal.name;
"@
    return @(
        Invoke-ReadOnlySql -Database $Database -Sql $sql |
            ForEach-Object { [string]$_.MemberName }
    )
}

function Get-DirectPermissions {
    param([string]$Database)

    $sql = @"
SELECT
    permissionRow.state_desc AS StateDesc,
    permissionRow.permission_name AS PermissionName,
    COALESCE(schemaRow.name, N'') AS SchemaName,
    COALESCE(objectRow.name, N'') AS ObjectName
FROM sys.database_permissions permissionRow
INNER JOIN sys.database_principals principalRow
  ON principalRow.principal_id = permissionRow.grantee_principal_id
LEFT JOIN sys.objects objectRow
  ON permissionRow.class = 1
 AND objectRow.object_id = permissionRow.major_id
LEFT JOIN sys.schemas schemaRow
  ON schemaRow.schema_id = objectRow.schema_id
WHERE principalRow.name = N'$($principal.Replace("'", "''"))'
ORDER BY
    permissionRow.state_desc,
    permissionRow.permission_name,
    schemaRow.name,
    objectRow.name;
"@
    return @(
        Invoke-ReadOnlySql -Database $Database -Sql $sql |
            ForEach-Object {
                Convert-ToPermissionKey -Permission $_
            }
    )
}

function Get-RolePermissions {
    param([string]$Database)

    $sql = @"
SELECT
    permissionRow.state_desc AS StateDesc,
    permissionRow.permission_name AS PermissionName,
    COALESCE(schemaRow.name, N'') AS SchemaName,
    COALESCE(objectRow.name, N'') AS ObjectName
FROM sys.database_permissions permissionRow
INNER JOIN sys.database_principals roleRow
  ON roleRow.principal_id = permissionRow.grantee_principal_id
LEFT JOIN sys.objects objectRow
  ON permissionRow.class = 1
 AND objectRow.object_id = permissionRow.major_id
LEFT JOIN sys.schemas schemaRow
  ON schemaRow.schema_id = objectRow.schema_id
WHERE roleRow.name = N'$workerRole'
ORDER BY
    permissionRow.state_desc,
    permissionRow.permission_name,
    schemaRow.name,
    objectRow.name;
"@
    return @(
        Invoke-ReadOnlySql -Database $Database -Sql $sql |
            ForEach-Object {
                Convert-ToPermissionKey -Permission $_
            }
    )
}

function Get-SchemaFingerprint {
    param(
        [string]$Database,
        [object[]]$Objects
    )

    $values = @(
        $Objects | ForEach-Object {
            "(N'$($_.Schema.Replace("'", "''"))'," +
            "N'$($_.Name.Replace("'", "''"))'," +
            "N'$($_.Type.Replace("'", "''"))')"
        }
    ) -join ",`n"

    $sql = @"
DECLARE @ExpectedObjects TABLE
(
    SchemaName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectType char(2) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (SchemaName, ObjectName)
);
INSERT @ExpectedObjects VALUES
$values;

IF (SELECT COUNT(*) FROM @ExpectedObjects) <>
   (
       SELECT COUNT(*)
       FROM @ExpectedObjects expected
       INNER JOIN sys.schemas schemaRow
         ON schemaRow.name COLLATE DATABASE_DEFAULT = expected.SchemaName
       INNER JOIN sys.objects objectRow
         ON objectRow.schema_id = schemaRow.schema_id
        AND objectRow.name COLLATE DATABASE_DEFAULT = expected.ObjectName
        AND objectRow.type COLLATE DATABASE_DEFAULT = expected.ObjectType
   )
BEGIN
    SELECT N'OBJECT_CONTRACT_MISMATCH' AS Fingerprint;
    RETURN;
END;

DECLARE @Canonical nvarchar(max);
;WITH Parts AS
(
    SELECT
        CONCAT(
            N'T|', schemaRow.name, N'|', objectRow.name, N'|',
            columnRow.column_id, N'|', columnRow.name, N'|', typeRow.name,
            N'|', columnRow.max_length, N'|', columnRow.precision, N'|',
            columnRow.scale, N'|', CONVERT(int, columnRow.is_nullable),
            N'|', CONVERT(int, columnRow.is_identity), N'|',
            COALESCE(CONVERT(nvarchar(100), identityRow.seed_value), N''),
            N'|',
            COALESCE(CONVERT(nvarchar(100), identityRow.increment_value), N''),
            N'|', CONVERT(int, columnRow.is_computed), N'|',
            COALESCE(columnRow.collation_name, N''), N'|',
            COALESCE(computedRow.definition, N''), N'|',
            COALESCE(defaultRow.definition, N''))
            COLLATE DATABASE_DEFAULT AS PartValue
    FROM @ExpectedObjects expected
    INNER JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT = expected.SchemaName
    INNER JOIN sys.objects objectRow
      ON objectRow.schema_id = schemaRow.schema_id
     AND objectRow.name COLLATE DATABASE_DEFAULT = expected.ObjectName
     AND objectRow.type COLLATE DATABASE_DEFAULT = expected.ObjectType
    INNER JOIN sys.columns columnRow
      ON columnRow.object_id = objectRow.object_id
    INNER JOIN sys.types typeRow
      ON typeRow.user_type_id = columnRow.user_type_id
    LEFT JOIN sys.identity_columns identityRow
      ON identityRow.object_id = columnRow.object_id
     AND identityRow.column_id = columnRow.column_id
    LEFT JOIN sys.computed_columns computedRow
      ON computedRow.object_id = columnRow.object_id
     AND computedRow.column_id = columnRow.column_id
    LEFT JOIN sys.default_constraints defaultRow
      ON defaultRow.parent_object_id = columnRow.object_id
     AND defaultRow.parent_column_id = columnRow.column_id
    WHERE expected.ObjectType = N'U'

    UNION ALL

    SELECT
        CONCAT(
            N'P|', schemaRow.name, N'|', objectRow.name, N'|',
            CONVERT(
                varchar(64),
                HASHBYTES(
                    'SHA2_256',
                    CONVERT(varbinary(max), moduleRow.definition)),
                2)) COLLATE DATABASE_DEFAULT
    FROM @ExpectedObjects expected
    INNER JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT = expected.SchemaName
    INNER JOIN sys.objects objectRow
      ON objectRow.schema_id = schemaRow.schema_id
     AND objectRow.name COLLATE DATABASE_DEFAULT = expected.ObjectName
     AND objectRow.type COLLATE DATABASE_DEFAULT = expected.ObjectType
    INNER JOIN sys.sql_modules moduleRow
      ON moduleRow.object_id = objectRow.object_id
    WHERE expected.ObjectType = N'P'
),
Numbered AS
(
    SELECT PartValue, ROW_NUMBER() OVER (ORDER BY PartValue) AS RowNumber
    FROM Parts
)
SELECT
    CONVERT(
        varchar(64),
        HASHBYTES(
            'SHA2_256',
            CONVERT(
                varbinary(max),
                STRING_AGG(CONVERT(nvarchar(max), PartValue), NCHAR(10))
                    WITHIN GROUP (ORDER BY RowNumber))),
        2) AS Fingerprint
FROM Numbered;
"@
    return [string](@(Invoke-ReadOnlySql -Database $Database -Sql $sql)[0].Fingerprint)
}

function Test-EffectivePermissions {
    param(
        [string]$Database,
        [object[]]$Required,
        [object[]]$Forbidden
    )

    $requiredValues = @(
        $Required | ForEach-Object {
            "(N'$($_.Schema.Replace("'", "''"))'," +
            "N'$($_.Name.Replace("'", "''"))'," +
            "N'$($_.Permission.Replace("'", "''"))',CONVERT(bit,1))"
        }
    )
    $forbiddenValues = @(
        $Forbidden | ForEach-Object {
            "(N'$($_.Schema.Replace("'", "''"))'," +
            "N'$($_.Name.Replace("'", "''"))'," +
            "N'$($_.Permission.Replace("'", "''"))',CONVERT(bit,0))"
        }
    )
    $allValues = @($requiredValues + $forbiddenValues) -join ",`n"
    $sql = @"
DECLARE @Checks TABLE
(
    SchemaName sysname NOT NULL,
    ObjectName sysname NOT NULL,
    PermissionName nvarchar(60) NOT NULL,
    Expected bit NOT NULL
);
INSERT @Checks VALUES
$allValues;

EXECUTE AS USER = N'$($principal.Replace("'", "''"))';
SELECT
    SchemaName,
    ObjectName,
    PermissionName,
    Expected,
    CONVERT(
        bit,
        HAS_PERMS_BY_NAME(
            QUOTENAME(SchemaName) + N'.' + QUOTENAME(ObjectName),
            N'OBJECT',
            PermissionName)) AS Allowed
FROM @Checks
ORDER BY SchemaName, ObjectName, PermissionName;
REVERT;
"@
    return @(Invoke-ReadOnlySql -Database $Database -Sql $sql)
}

$targetObjects = @(
    'App_CsdtConnectionProfile',
    'App_DataVersion',
    'App_GiaoVien',
    'App_HocVien',
    'App_KhoaHoc',
    'App_KhoaHoc_GiaoVien',
    'App_QlhvAutoSyncRun',
    'App_QlhvDirectRealtimeApplyCheckpoint',
    'App_QlhvDirectRealtimeApplyMarker',
    'App_QlhvDirectRealtimeCycleHistory',
    'App_QlhvDirectRealtimeFeatureState',
    'App_QlhvDirectRealtimeManualReview',
    'App_QlhvDirectRealtimeProfileState',
    'App_QlhvDirectRealtimeWorkerState',
    'App_QlhvSyncOperationHistory',
    'App_QlhvSyncPartitionState',
    'App_XeTap',
    'App_XeTap_RealtimeCheckpoint',
    'App_XeTap_RealtimeEvent',
    'App_XeTap_RealtimeManualReview',
    'App_Rt03FullConvergenceSession',
    'App_Rt03FullConvergenceDomain',
    'App_Rt03FullConvergenceMarker'
) | ForEach-Object {
    [pscustomobject]@{ Schema = 'dbo'; Name = $_; Type = 'U' }
}
$targetObjects += @(
    'usp_App_Rt03BeginFullConvergence',
    'usp_App_Rt03RecordFullConvergenceDomain',
    'usp_App_Rt03VerifyFullConvergence',
    'usp_App_Rt03FinalizeFullConvergence'
) | ForEach-Object {
    [pscustomobject]@{ Schema = 'dbo'; Name = $_; Type = 'P' }
}
$sourceObjects = @(
    'KhoaHoc',
    'GiaoVien',
    'XeTap',
    'NguoiLX',
    'NguoiLX_HoSo',
    'DM_HangDT',
    'DM_DVHC',
    'KhoaHoc_GiaoVien'
) | ForEach-Object {
    [pscustomobject]@{ Schema = 'dbo'; Name = $_; Type = 'U' }
}

$targetPermissionSpec = @'
App_CsdtConnectionProfile|SELECT
App_DataVersion|SELECT
App_DataVersion|UPDATE
App_GiaoVien|INSERT
App_GiaoVien|SELECT
App_GiaoVien|UPDATE
App_HocVien|INSERT
App_HocVien|SELECT
App_HocVien|UPDATE
App_KhoaHoc|INSERT
App_KhoaHoc|SELECT
App_KhoaHoc|UPDATE
App_KhoaHoc_GiaoVien|INSERT
App_KhoaHoc_GiaoVien|SELECT
App_KhoaHoc_GiaoVien|UPDATE
App_QlhvAutoSyncRun|SELECT
App_QlhvDirectRealtimeApplyCheckpoint|INSERT
App_QlhvDirectRealtimeApplyCheckpoint|SELECT
App_QlhvDirectRealtimeApplyCheckpoint|UPDATE
App_QlhvDirectRealtimeApplyMarker|INSERT
App_QlhvDirectRealtimeApplyMarker|SELECT
App_QlhvDirectRealtimeCycleHistory|INSERT
App_QlhvDirectRealtimeCycleHistory|SELECT
App_QlhvDirectRealtimeFeatureState|SELECT
App_QlhvDirectRealtimeManualReview|INSERT
App_QlhvDirectRealtimeManualReview|SELECT
App_QlhvDirectRealtimeProfileState|SELECT
App_QlhvDirectRealtimeProfileState|UPDATE
App_QlhvDirectRealtimeWorkerState|SELECT
App_QlhvDirectRealtimeWorkerState|UPDATE
App_QlhvSyncOperationHistory|SELECT
App_QlhvSyncPartitionState|INSERT
App_QlhvSyncPartitionState|SELECT
App_QlhvSyncPartitionState|UPDATE
App_XeTap|INSERT
App_XeTap|SELECT
App_XeTap|UPDATE
App_XeTap_RealtimeCheckpoint|INSERT
App_XeTap_RealtimeCheckpoint|SELECT
App_XeTap_RealtimeCheckpoint|UPDATE
App_XeTap_RealtimeEvent|INSERT
App_XeTap_RealtimeEvent|SELECT
App_XeTap_RealtimeManualReview|INSERT
App_XeTap_RealtimeManualReview|SELECT
App_Rt03FullConvergenceSession|VIEW DEFINITION
App_Rt03FullConvergenceDomain|VIEW DEFINITION
App_Rt03FullConvergenceMarker|VIEW DEFINITION
usp_App_Rt03BeginFullConvergence|EXECUTE
usp_App_Rt03RecordFullConvergenceDomain|EXECUTE
usp_App_Rt03VerifyFullConvergence|EXECUTE
usp_App_Rt03FinalizeFullConvergence|EXECUTE
'@
$targetPermissions = @(
    $targetPermissionSpec.Trim().Split(
        [Environment]::NewLine,
        [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object {
            $parts = $_.Split('|')
            [pscustomobject]@{
                Schema = 'dbo'
                Name = $parts[0]
                Permission = $parts[1]
            }
        }
)
$sourcePermissionSpec = @'
KhoaHoc|SELECT
GiaoVien|SELECT
XeTap|SELECT
NguoiLX|SELECT
NguoiLX_HoSo|SELECT
DM_HangDT|SELECT
DM_DVHC|SELECT
KhoaHoc_GiaoVien|SELECT
NguoiLX|VIEW CHANGE TRACKING
NguoiLX_HoSo|VIEW CHANGE TRACKING
KhoaHoc|VIEW CHANGE TRACKING
DM_HangDT|VIEW CHANGE TRACKING
DM_DVHC|VIEW CHANGE TRACKING
'@
$sourcePermissions = @(
    $sourcePermissionSpec.Trim().Split(
        [Environment]::NewLine,
        [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object {
            $parts = $_.Split('|')
            [pscustomobject]@{
                Schema = 'dbo'
                Name = $parts[0]
                Permission = $parts[1]
            }
        }
)

$targetDirectExpected = @(
    'GRANT|CONNECT||',
    'DENY|DELETE|dbo|App_HocVien',
    'DENY|DELETE|dbo|App_XeTap',
    'GRANT|INSERT|dbo|App_HocVien',
    'GRANT|UPDATE|dbo|App_HocVien',
    'GRANT|INSERT|dbo|App_QlhvDirectRealtimeApplyCheckpoint',
    'GRANT|UPDATE|dbo|App_QlhvDirectRealtimeApplyCheckpoint',
    'GRANT|INSERT|dbo|App_QlhvDirectRealtimeApplyMarker',
    'GRANT|INSERT|dbo|App_QlhvDirectRealtimeCycleHistory',
    'GRANT|INSERT|dbo|App_QlhvDirectRealtimeManualReview',
    'GRANT|UPDATE|dbo|App_QlhvDirectRealtimeProfileState',
    'GRANT|UPDATE|dbo|App_QlhvDirectRealtimeWorkerState',
    'GRANT|INSERT|dbo|App_XeTap',
    'GRANT|SELECT|dbo|App_XeTap',
    'GRANT|UPDATE|dbo|App_XeTap',
    'GRANT|INSERT|dbo|App_XeTap_RealtimeCheckpoint',
    'GRANT|SELECT|dbo|App_XeTap_RealtimeCheckpoint',
    'GRANT|UPDATE|dbo|App_XeTap_RealtimeCheckpoint',
    'GRANT|INSERT|dbo|App_XeTap_RealtimeEvent',
    'GRANT|SELECT|dbo|App_XeTap_RealtimeEvent',
    'GRANT|INSERT|dbo|App_XeTap_RealtimeManualReview',
    'GRANT|SELECT|dbo|App_XeTap_RealtimeManualReview'
)
$sourceDirectExpected = @(
    'GRANT|CONNECT||',
    'GRANT|VIEW CHANGE TRACKING|dbo|DM_DVHC',
    'GRANT|VIEW CHANGE TRACKING|dbo|DM_HangDT',
    'GRANT|VIEW CHANGE TRACKING|dbo|KhoaHoc',
    'GRANT|VIEW CHANGE TRACKING|dbo|NguoiLX',
    'GRANT|VIEW CHANGE TRACKING|dbo|NguoiLX_HoSo'
)
$targetRoleExpected = @(
    $targetPermissions | ForEach-Object {
        "GRANT|$($_.Permission)|$($_.Schema)|$($_.Name)"
    }
)
$sourceRoleExpected = @(
    $sourcePermissions | ForEach-Object {
        "GRANT|$($_.Permission)|$($_.Schema)|$($_.Name)"
    }
)

try {
    $server = @(Invoke-ReadOnlySql -Database master -Sql @"
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerName,
    CONVERT(bit, CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.server_principals
        WHERE name = N'$($principal.Replace("'", "''"))'
          AND type = N'U'
          AND is_disabled = 0
    ) THEN 1 ELSE 0 END) AS LoginValid,
    CONVERT(int, ISNULL(IS_SRVROLEMEMBER(
        N'sysadmin',
        N'$($principal.Replace("'", "''"))'), -1)) AS IsSysadmin;
"@)[0]
    Add-Check 'server.identity' ([string]$server.ServerName -eq $ServerInstance) `
        ([string]$server.ServerName) $ServerInstance
    Add-Check 'principal.login-valid' ([bool]$server.LoginValid) `
        ([bool]$server.LoginValid) $true
    Add-Check 'principal.not-sysadmin' ([int]$server.IsSysadmin -eq 0) `
        ([int]$server.IsSysadmin) 0

    $databaseContracts = @(
        [pscustomobject]@{
            Name = 'QLHV_APP'
            Id = $TargetDatabaseId
            Guid = $TargetDatabaseGuid
            Fingerprint = $TargetSchemaFingerprint
            Objects = $targetObjects
            Direct = $targetDirectExpected
            RolePermissions = $targetPermissions
            RoleRaw = $targetRoleExpected
            Forbidden = @(
                $targetObjects |
                    Where-Object { $_.Type -eq 'U' } |
                    ForEach-Object {
                        [pscustomobject]@{
                            Schema = $_.Schema
                            Name = $_.Name
                            Permission = 'DELETE'
                        }
                    }
            )
        },
        [pscustomobject]@{
            Name = 'CSDL_OTO'
            Id = $OtoDatabaseId
            Guid = $OtoDatabaseGuid
            Fingerprint = $SourceSchemaFingerprint
            Objects = $sourceObjects
            Direct = $sourceDirectExpected
            RolePermissions = $sourcePermissions
            RoleRaw = $sourceRoleExpected
            Forbidden = @(
                $sourceObjects | ForEach-Object {
                    foreach ($verb in @('INSERT', 'UPDATE', 'DELETE')) {
                        [pscustomobject]@{
                            Schema = $_.Schema
                            Name = $_.Name
                            Permission = $verb
                        }
                    }
                }
            )
        },
        [pscustomobject]@{
            Name = 'CSDL_MOTO'
            Id = $MotoDatabaseId
            Guid = $MotoDatabaseGuid
            Fingerprint = $SourceSchemaFingerprint
            Objects = $sourceObjects
            Direct = $sourceDirectExpected
            RolePermissions = $sourcePermissions
            RoleRaw = $sourceRoleExpected
            Forbidden = @(
                $sourceObjects | ForEach-Object {
                    foreach ($verb in @('INSERT', 'UPDATE', 'DELETE')) {
                        [pscustomobject]@{
                            Schema = $_.Schema
                            Name = $_.Name
                            Permission = $verb
                        }
                    }
                }
            )
        }
    )

    foreach ($contract in $databaseContracts) {
        $identity = Get-DatabaseIdentity -Database $contract.Name
        Add-Check "$($contract.Name).database-id" `
            ([int]$identity.DatabaseId -eq [int]$contract.Id) `
            ([int]$identity.DatabaseId) ([int]$contract.Id)
        Add-Check "$($contract.Name).database-guid" `
            ([guid]$identity.DatabaseGuid -eq [guid]$contract.Guid) `
            ([string]$identity.DatabaseGuid) ([string]$contract.Guid)

        $user = @(Invoke-ReadOnlySql -Database $contract.Name -Sql @"
SELECT
    CONVERT(bit, CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.database_principals
        WHERE name = N'$($principal.Replace("'", "''"))'
          AND type = N'U'
          AND sid = SUSER_SID(N'$($principal.Replace("'", "''"))')
    ) THEN 1 ELSE 0 END) AS UserValid,
    CONVERT(bit, CASE WHEN DATABASE_PRINCIPAL_ID(N'$workerRole') IS NULL
                     THEN 0 ELSE 1 END) AS WorkerRoleExists;
"@)[0]
        Add-Check "$($contract.Name).principal-user-valid" ([bool]$user.UserValid) `
            ([bool]$user.UserValid) $true
        $expectedRoleExists = $Mode -eq 'Applied'
        Add-Check "$($contract.Name).worker-role-existence" `
            ([bool]$user.WorkerRoleExists -eq $expectedRoleExists) `
            ([bool]$user.WorkerRoleExists) $expectedRoleExists

        $fingerprint = Get-SchemaFingerprint `
            -Database $contract.Name `
            -Objects $contract.Objects
        Add-Check "$($contract.Name).schema-fingerprint" `
            ($fingerprint -eq $contract.Fingerprint) `
            $fingerprint $contract.Fingerprint

        $memberships = @(Get-RoleMemberships -Database $contract.Name)
        $expectedMemberships = if ($Mode -eq 'Baseline') {
            @('db_datareader')
        }
        else {
            @($workerRole)
        }
        Add-Check "$($contract.Name).exact-role-membership" `
            (Test-ExactSet -Expected $expectedMemberships -Actual $memberships) `
            $memberships $expectedMemberships

        $direct = @(Get-DirectPermissions -Database $contract.Name)
        Add-Check "$($contract.Name).direct-permission-baseline" `
            (Test-ExactSet -Expected $contract.Direct -Actual $direct) `
            $direct $contract.Direct

        $roleRaw = @(Get-RolePermissions -Database $contract.Name)
        $expectedRoleRaw = if ($Mode -eq 'Baseline') {
            @()
        }
        else {
            @($contract.RoleRaw)
        }
        Add-Check "$($contract.Name).exact-worker-role-grants" `
            (Test-ExactSet -Expected $expectedRoleRaw -Actual $roleRaw) `
            $roleRaw $expectedRoleRaw

        $workerRoleMembers = @(Get-WorkerRoleMembers -Database $contract.Name)
        $expectedWorkerRoleMembers = if ($Mode -eq 'Applied') {
            @($principal)
        }
        else {
            @()
        }
        Add-Check "$($contract.Name).exact-worker-role-members" `
            (Test-ExactSet `
                -Expected $expectedWorkerRoleMembers `
                -Actual $workerRoleMembers) `
            $workerRoleMembers $expectedWorkerRoleMembers

        if ($Mode -eq 'Baseline' -and $contract.Name -eq 'QLHV_APP') {
            $baselineGap = @(Invoke-ReadOnlySql -Database $contract.Name -Sql @"
EXECUTE AS USER = N'$($principal.Replace("'", "''"))';
SELECT CONVERT(
    bit,
    HAS_PERMS_BY_NAME(
        N'dbo.App_KhoaHoc',
        N'OBJECT',
        N'UPDATE')) AS AppKhoaHocUpdateAllowed;
REVERT;
"@)[0]
            Add-Check 'QLHV_APP.baseline-course-update-gap-confirmed' `
                (-not [bool]$baselineGap.AppKhoaHocUpdateAllowed) `
                ([bool]$baselineGap.AppKhoaHocUpdateAllowed) $false
        }

        if ($Mode -eq 'Applied') {
            $effective = @(
                Test-EffectivePermissions `
                    -Database $contract.Name `
                    -Required $contract.RolePermissions `
                    -Forbidden $contract.Forbidden
            )
            $mismatch = @(
                $effective | Where-Object {
                    [bool]$_.Allowed -ne [bool]$_.Expected
                }
            )
            Add-Check "$($contract.Name).effective-object-permissions" `
                ($mismatch.Count -eq 0) `
                $mismatch.Count 0

            $broad = @(Invoke-ReadOnlySql -Database $contract.Name -Sql @"
EXECUTE AS USER = N'$($principal.Replace("'", "''"))';
SELECT
    PermissionName,
    CONVERT(
        bit,
        HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', PermissionName)) AS Allowed
FROM
(
    VALUES
        (N'CONTROL'),
        (N'ALTER'),
        (N'INSERT'),
        (N'UPDATE'),
        (N'DELETE')
) valueRow(PermissionName);
REVERT;
"@)
            $broadAllowed = @($broad | Where-Object { [bool]$_.Allowed })
            Add-Check "$($contract.Name).no-broad-database-permission" `
                ($broadAllowed.Count -eq 0) `
                @($broadAllowed | ForEach-Object { [string]$_.PermissionName }) `
                @()
        }
    }

    if (-not $SkipWorkerServiceCheck) {
        $service = Get-CimInstance Win32_Service -Filter `
            "Name='QLHV_APP_RealtimeWorker'"
        $serviceObserved = if ($null -eq $service) {
            'NOT_FOUND'
        }
        else {
            '{0}/PID={1}' -f $service.State, $service.ProcessId
        }
        $servicePassed =
            ($null -ne $service) -and
            ($service.State -eq 'Stopped') -and
            ([int]$service.ProcessId -eq 0)
        Add-Check 'worker-service.stopped-singleton-safe' $servicePassed `
            $serviceObserved 'Stopped/PID=0'
    }
}
catch {
    Add-Check 'preflight.execution' $false $_.Exception.Message 'no error'
}

$failed = @($checks | Where-Object { -not $_.passed })
$result = [ordered]@{
    contract = 'RT03_V7_WORKER_PERMISSION_PREFLIGHT_1.0'
    mode = $Mode.ToUpperInvariant()
    server = $ServerInstance
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    readOnly = $true
    status = if ($failed.Count -eq 0) { 'PASS' } else { 'BLOCKED' }
    checkCount = $checks.Count
    failedCount = $failed.Count
    checks = $checks
}
$json = $result | ConvertTo-Json -Depth 8
if ($OutputPath) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $resolvedOutput
    if ($outputDirectory) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText(
        $resolvedOutput,
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}
$json

if ($failed.Count -ne 0) {
    exit 20
}
exit 0

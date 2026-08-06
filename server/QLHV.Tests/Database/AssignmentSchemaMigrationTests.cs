using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace QLHV.Tests.Database;

public sealed class AssignmentSchemaMigrationTests
{
    private const string MigrationRelativePath =
        "database/patches/20260730_add_integrated_course_assignment.sql";
    private const string RollbackRelativePath =
        "database/patches/20260730_rollback_integrated_course_assignment.sql";
    private const string BindApiPrincipalRelativePath =
        "database/patches/20260730_bind_assignment_api_principal.sql";
    private const string UnbindApiPrincipalRelativePath =
        "database/patches/20260730_unbind_assignment_api_principal.sql";
    private const string ProductionDatabaseGuid =
        "9C44B304-8A84-4D0D-9A82-19C7233FF6BB";
    private const string RehearsalOptIn =
        "QLHV_RUN_ASSIGNMENT_SQL_REHEARSAL";

    [Fact]
    public void Migration_has_exact_database_identity_and_fresh_schema_preconditions()
    {
        var sql = Read(MigrationRelativePath);

        Assert.Matches(
            @"(?im)^\s*USE\s+\[QLHV_APP\];\s*\r?\n\s*GO\s*$",
            sql);
        Assert.Equal(1, Count(sql, @"(?im)^\s*USE\s+\[QLHV_APP\];\s*$"));
        Assert.Contains(ProductionDatabaseGuid, sql, StringComparison.Ordinal);
        Assert.Contains("QLHV_APP database identity mismatch", sql,
            StringComparison.Ordinal);
        Assert.Contains("SQL_Latin1_General_CP1_CI_AS", sql,
            StringComparison.Ordinal);
        Assert.Contains("(N'App_HocVien', N'HocVienId')", sql,
            StringComparison.Ordinal);
        Assert.Contains("(N'App_KhoaHoc', N'KhoaHocId')", sql,
            StringComparison.Ordinal);
        Assert.Contains("(N'App_GiaoVien', N'GiaoVienId')", sql,
            StringComparison.Ordinal);
        Assert.Contains("(N'App_XeTap', N'XeTapId')", sql,
            StringComparison.Ordinal);
        Assert.Contains("(N'App_ImportBatch', N'ImportBatchId')", sql,
            StringComparison.Ordinal);
        Assert.Contains("AND max_length = 100", sql, StringComparison.Ordinal);
        Assert.Contains("AND columnItem.system_type_id = 189", sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FROM dbo.App_HocVien)", sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FROM dbo.App_KhoaHoc)", sql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_creates_exact_v2_tables_columns_and_rowversions()
    {
        var sql = Read(MigrationRelativePath);

        Assert.Equal(1, Count(sql,
            @"(?i)CREATE\s+TABLE\s+dbo\.App_GiaoVien_hs\b"));
        Assert.Equal(1, Count(sql,
            @"(?i)CREATE\s+TABLE\s+dbo\.App_KhoaHoc_NhomDaoTao\b"));
        Assert.Equal(1, Count(sql,
            @"(?i)CREATE\s+TABLE\s+dbo\.App_HocVien_PhanCong\b"));
        Assert.Equal(1, Count(sql,
            @"(?i)CREATE\s+TABLE\s+dbo\.App_AssignmentOperation\b"));

        foreach (var expected in new[]
        {
            "GiaoVienHsId       BIGINT IDENTITY(1,1) NOT NULL",
            "MaGiaoVienHs       NVARCHAR(50) NOT NULL",
            "HoTen              NVARCHAR(255) NOT NULL",
            "HoTenSearch        NVARCHAR(255) NOT NULL",
            "NhomDaoTaoId          BIGINT IDENTITY(1,1) NOT NULL",
            "KhoaHocId             BIGINT NOT NULL",
            "GiaoVienDungLopId     BIGINT NULL",
            "XeTapId               BIGINT NULL",
            "XeBaiSo10Id           BIGINT NULL",
            "PhanCongId                       BIGINT IDENTITY(1,1) NOT NULL",
            "HocVienId                        BIGINT NOT NULL",
            "GiaoVienHoSoId                   BIGINT NULL",
            "ImportSessionId                  BIGINT NULL",
            "NgayHieuLuc                      DATETIME2(7) NOT NULL",
            "NgayHetHieuLuc                   DATETIME2(7) NULL",
            "IsCurrent                        BIT NOT NULL",
            "AssignmentOperationId   BIGINT IDENTITY(1,1) NOT NULL",
            "IdempotencyKeySha256    CHAR(64) NOT NULL",
            "PayloadSha256           CHAR(64) NOT NULL",
            "PreviewTokenSha256      CHAR(64) NOT NULL",
            "RetainUntilUtc          DATETIME2(7) NOT NULL",
        })
        {
            Assert.Contains(expected, sql, StringComparison.Ordinal);
        }

        Assert.Equal(4, Count(sql, @"(?im)^\s*RowVersion\s+ROWVERSION\s+NOT NULL,?$"));
        Assert.Contains("FileSha256 CHAR(64) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("TemplateVersion VARCHAR(40) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("NormalizationVersion VARCHAR(40) NULL", sql,
            StringComparison.Ordinal);
        Assert.Contains("PreviewExpiresAtUtc DATETIME2(7) NULL", sql,
            StringComparison.Ordinal);
        Assert.Contains("ConfirmedAtUtc DATETIME2(7) NULL", sql,
            StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey NVARCHAR(100) NULL", sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_assignment_foreign_key_is_bigint_and_no_action()
    {
        var sql = Read(MigrationRelativePath);

        Assert.Equal(14, Count(sql, @"(?i)\bFOREIGN\s+KEY\s*\("));
        Assert.Equal(14, Count(sql, @"(?i)\bON\s+DELETE\s+NO\s+ACTION\b"));
        Assert.Equal(14, Count(sql, @"(?i)\bON\s+UPDATE\s+NO\s+ACTION\b"));

        foreach (var constraint in new[]
        {
            "FK_App_KHNDT_KhoaHoc",
            "FK_App_KHNDT_GiaoVienDungLop",
            "FK_App_KHNDT_XeTap",
            "FK_App_KHNDT_XeBaiSo10",
            "FK_App_HVPC_HocVien",
            "FK_App_HVPC_NhomDaoTao",
            "FK_App_HVPC_GiaoVienHoSo",
            "FK_App_HVPC_GiaoVienDungLop",
            "FK_App_HVPC_XeTap",
            "FK_App_HVPC_XeBaiSo10",
            "FK_App_HVPC_ImportSession",
            "FK_App_AssignmentOperation_Course",
            "FK_App_AssignmentOperation_GroupScope",
            "FK_App_AssignmentOperation_ImportBatch",
        })
        {
            Assert.Equal(1, Count(sql, $@"(?i)\bCONSTRAINT\s+{constraint}\b"));
        }
    }

    [Fact]
    public void Migration_enforces_history_override_group_import_and_permission_contracts()
    {
        var sql = Read(MigrationRelativePath);

        foreach (var expected in new[]
        {
            "CK_App_HVPC_OverrideState",
            "CK_App_HVPC_EffectiveState",
            "CK_App_HVPC_ImportSource",
            "UX_App_HVPC_OneCurrentPerHocVien",
            "TR_App_HVPC_ValidateInsert",
            "Assignment group/course/profile mismatch.",
            "Inherited assignment values must equal the group defaults sealed for this snapshot.",
            "EXCEL assignment must reference an HOCVIEN_ASSIGNMENT import session.",
            "TR_App_HVPC_ImmutableSnapshot",
            "TR_App_HVPC_BlockHardDelete",
            "TR_App_KHNDT_ImmutableCourse",
            "TR_App_KHNDT_BlockHardDelete",
            "TR_App_GiaoVien_hs_BlockHardDelete",
            "UQ_App_AssignmentOperation_IdempotencyKey",
            "CK_App_AssignmentOperation_Scope",
            "CK_App_AssignmentOperation_Hashes",
            "IX_App_AssignmentOperation_Retention",
            "minimum 180-day replay window",
        })
        {
            Assert.Contains(expected, sql, StringComparison.Ordinal);
        }

        Assert.Equal(5, Count(
            sql,
            @"(?i)\bTO\s+\[NT SERVICE\\QLHV_APP_RealtimeWorker\]"));
        Assert.Equal(5, Count(
            sql,
            @"(?i)\bDENY\s+INSERT,\s*UPDATE,\s*DELETE\b"));
        Assert.Contains("permission_name IN (N'INSERT', N'UPDATE', N'DELETE')",
            sql,
            StringComparison.Ordinal);

        Assert.Matches(
            @"(?is)CONSTRAINT\s+CK_App_HVPC_OverrideState\s+CHECK\s*\(\s*NhomDaoTaoId\s+IS\s+NOT\s+NULL\s+OR\s*\(\s*IsGiaoVienDungLopOverride\s*=\s*1\s+AND\s*IsXeTapOverride\s*=\s*1\s+AND\s*IsXeBaiSo10Override\s*=\s*1\s*\)\s*\)",
            sql);

        var apiRoleGrants = Regex.Matches(
            sql,
            @"(?is)\bGRANT\s+(?<permissions>[A-Z,\s]+?)\s+ON\s+OBJECT::dbo\.(?<object>\w+)\s+TO\s+\[QLHV_AssignmentApiRole\]\s*;");
        Assert.Equal(10, apiRoleGrants.Count);
        Assert.Equal(
            20,
            apiRoleGrants.Cast<Match>().Sum(match => match.Groups["permissions"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Length));
        Assert.All(apiRoleGrants.Cast<Match>(), match =>
            Assert.DoesNotContain("DELETE", match.Groups["permissions"].Value, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(") <> 20", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Rollback_is_exact_reverse_and_blocks_when_any_data_or_history_exists()
    {
        var sql = Read(RollbackRelativePath);

        Assert.Matches(
            @"(?im)^\s*USE\s+\[QLHV_APP\];\s*\r?\n\s*GO\s*$",
            sql);
        Assert.Equal(1, Count(sql, @"(?im)^\s*USE\s+\[QLHV_APP\];\s*$"));
        Assert.Contains(ProductionDatabaseGuid, sql, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK_BLOCKED_DATA_OR_HISTORY", sql,
            StringComparison.Ordinal);
        Assert.Contains("disable the assignment writer and roll forward", sql,
            StringComparison.Ordinal);

        var ledgerDrop = sql.IndexOf(
            "DROP TABLE dbo.App_AssignmentOperation;",
            StringComparison.Ordinal);
        var assignmentDrop = sql.IndexOf(
            "DROP TABLE dbo.App_HocVien_PhanCong;",
            StringComparison.Ordinal);
        var groupDrop = sql.IndexOf(
            "DROP TABLE dbo.App_KhoaHoc_NhomDaoTao;",
            StringComparison.Ordinal);
        var receiverDrop = sql.IndexOf(
            "DROP TABLE dbo.App_GiaoVien_hs;",
            StringComparison.Ordinal);
        var indexDrop = sql.IndexOf(
            "DROP INDEX UX_App_ImportBatch_AssignmentIdempotency",
            StringComparison.Ordinal);
        var columnDrop = sql.IndexOf(
            "ALTER TABLE dbo.App_ImportBatch DROP COLUMN",
            StringComparison.Ordinal);

        Assert.True(ledgerDrop >= 0);
        Assert.True(assignmentDrop > ledgerDrop);
        Assert.True(groupDrop > assignmentDrop);
        Assert.True(receiverDrop > groupDrop);
        Assert.True(indexDrop > receiverDrop);
        Assert.True(columnDrop > indexDrop);
        Assert.Contains("REVOKE INSERT, UPDATE, DELETE", sql,
            StringComparison.Ordinal);
        Assert.Contains("sys.database_role_members", sql, StringComparison.Ordinal);
        Assert.Contains("THROW 529361", sql, StringComparison.Ordinal);
        Assert.Contains("QLHV_AssignmentApiRole still has members", sql, StringComparison.Ordinal);

        var apiRoleRevokes = Regex.Matches(
            sql,
            @"(?is)\bREVOKE\s+(?<permissions>[A-Z,\s]+?)\s+ON\s+OBJECT::dbo\.(?<object>\w+)\s+FROM\s+\[QLHV_AssignmentApiRole\]\s*;");
        Assert.Equal(10, apiRoleRevokes.Count);
        Assert.Equal(
            20,
            apiRoleRevokes.Cast<Match>().Sum(match => match.Groups["permissions"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Length));
        Assert.All(apiRoleRevokes.Cast<Match>(), match =>
            Assert.DoesNotContain("DELETE", match.Groups["permissions"].Value, StringComparison.OrdinalIgnoreCase));
        var membershipGuard = sql.IndexOf("sys.database_role_members", StringComparison.Ordinal);
        var transaction = sql.IndexOf("BEGIN TRY", membershipGuard, StringComparison.Ordinal);
        var roleDrop = sql.IndexOf("DROP ROLE [QLHV_AssignmentApiRole];", StringComparison.Ordinal);
        Assert.True(membershipGuard >= 0 && transaction > membershipGuard);
        Assert.True(roleDrop > columnDrop);
        Assert.Contains("Post-rollback assignment API role verification failed.", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_role_bind_and_unbind_scripts_are_identity_scoped_explicit_and_fail_closed()
    {
        var bind = Read(BindApiPrincipalRelativePath);
        var unbind = Read(UnbindApiPrincipalRelativePath);

        foreach (var sql in new[] { bind, unbind })
        {
            Assert.Matches(@"(?im)^\s*:on\s+error\s+exit\s*$", sql);
            Assert.Matches(@"(?im)^\s*USE\s+\[QLHV_APP\];\s*$", sql);
            Assert.Equal(1, Count(sql, Regex.Escape(ProductionDatabaseGuid)));
            Assert.Contains("$(AssignmentApiPrincipal)", sql, StringComparison.Ordinal);
            Assert.Contains("NULLIF(LTRIM(RTRIM", sql, StringComparison.Ordinal);
            Assert.Contains("@Principal IS NULL OR @Principal LIKE N'%$(%'", sql, StringComparison.Ordinal);
            Assert.Contains("N'dbo'", sql, StringComparison.Ordinal);
            Assert.Contains(@"N'NT SERVICE\QLHV_APP_RealtimeWorker'", sql, StringComparison.Ordinal);
            Assert.Contains("DATABASE_PRINCIPAL_ID(@Principal)", sql, StringComparison.Ordinal);
            Assert.Contains("PRODUCTION STATUS: NOT EXECUTED BY THIS TASK.", sql, StringComparison.Ordinal);
        }

        Assert.Contains("ALTER ROLE [QLHV_AssignmentApiRole] ADD MEMBER", bind, StringComparison.Ordinal);
        Assert.Contains("ALTER ROLE [QLHV_AssignmentApiRole] DROP MEMBER", unbind, StringComparison.Ordinal);
        Assert.Contains("Assignment API role membership removal verification failed.", unbind, StringComparison.Ordinal);

        var effectiveChecks = Regex.Matches(
            bind,
            @"HAS_PERMS_BY_NAME\(N''(?<object>dbo\.\w+)'',N''OBJECT'',N''(?<permission>SELECT|INSERT|UPDATE|DELETE)''\)<>(?<expected>[01])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.Equal(25, effectiveChecks.Count);
        Assert.Equal(
            20,
            effectiveChecks.Cast<Match>().Count(match =>
                !string.Equals(match.Groups["permission"].Value, "DELETE", StringComparison.OrdinalIgnoreCase) &&
                match.Groups["expected"].Value == "1"));
        var deleteChecks = effectiveChecks.Cast<Match>()
            .Where(match => string.Equals(
                match.Groups["permission"].Value,
                "DELETE",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(5, deleteChecks.Length);
        Assert.All(deleteChecks, match => Assert.Equal("0", match.Groups["expected"].Value));
        Assert.Equal(
            new[]
            {
                "dbo.App_AssignmentOperation",
                "dbo.App_GiaoVien_hs",
                "dbo.App_HocVien_PhanCong",
                "dbo.App_ImportBatch",
                "dbo.App_KhoaHoc_NhomDaoTao",
            },
            deleteChecks.Select(match => match.Groups["object"].Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    [Trait("Category", "IsolatedSql")]
    public async Task Opted_in_isolated_database_rehearses_migration_constraints_and_rollback()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RehearsalOptIn),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var server = Environment.GetEnvironmentVariable(
            "QLHV_ASSIGNMENT_REHEARSAL_SERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            server = "CSDLTTTC";
        }

        var database =
            $"QLHV_ASSIGNMENT_REHEARSAL_{Guid.NewGuid():N}".ToUpperInvariant();
        database = database[..Math.Min(database.Length, 54)];
        Assert.Matches(
            @"^QLHV_ASSIGNMENT_REHEARSAL_[0-9A-F]{28}$",
            database);
        Assert.NotEqual("QLHV_APP", database);

        await using var master = new SqlConnection(
            ConnectionString(server, "master"));
        await master.OpenAsync();

        try
        {
            await ExecuteAsync(master, $"""
CREATE DATABASE [{database}]
COLLATE SQL_Latin1_General_CP1_CI_AS;
""");

            await using var target = new SqlConnection(
                ConnectionString(server, database));
            await target.OpenAsync();
            await ExecuteAsync(target, FixtureSql);

            await ExecutePatchAsync(target, MigrationRelativePath, database);
            Assert.Equal(
                14,
                Convert.ToInt32(await ScalarAsync(
                    target,
                    """
SELECT COUNT(*)
FROM sys.foreign_keys
WHERE parent_object_id IN
(
    OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao'),
    OBJECT_ID(N'dbo.App_HocVien_PhanCong'),
    OBJECT_ID(N'dbo.App_AssignmentOperation')
);
""")));
            Assert.Equal(
                15,
                Convert.ToInt32(await ScalarAsync(
                    target,
                    """
SELECT COUNT(*)
FROM sys.database_permissions
WHERE grantee_principal_id =
      DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker')
  AND state = N'D'
  AND major_id IN
  (
      OBJECT_ID(N'dbo.App_GiaoVien_hs'),
      OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao'),
      OBJECT_ID(N'dbo.App_HocVien_PhanCong'),
      OBJECT_ID(N'dbo.App_ImportBatch'),
      OBJECT_ID(N'dbo.App_AssignmentOperation')
  )
  AND permission_name IN (N'INSERT',N'UPDATE',N'DELETE');
""")));
            Assert.Equal(
                20,
                Convert.ToInt32(await ScalarAsync(
                    target,
                    """
SELECT COUNT(*)
FROM sys.database_permissions
WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
  AND state IN (N'G',N'W')
  AND class=1;
""")));
            Assert.Equal(
                0,
                Convert.ToInt32(await ScalarAsync(
                    target,
                    """
SELECT COUNT(*)
FROM sys.database_permissions
WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
  AND state IN (N'G',N'W')
  AND permission_name=N'DELETE';
""")));

            await ExecuteAsync(
                target,
                """
CREATE USER [QLHV_AssignmentRehearsalUser] WITHOUT LOGIN;
ALTER ROLE [QLHV_AssignmentApiRole] ADD MEMBER [QLHV_AssignmentRehearsalUser];
""");
            await AssertSqlErrorAsync(
                529361,
                () => ExecutePatchAsync(target, RollbackRelativePath, database));
            await ExecuteAsync(
                target,
                """
ALTER ROLE [QLHV_AssignmentApiRole] DROP MEMBER [QLHV_AssignmentRehearsalUser];
DROP USER [QLHV_AssignmentRehearsalUser];
""");

            await ExecutePatchAsync(target, RollbackRelativePath, database);
            Assert.Equal(
                0,
                Convert.ToInt32(await ScalarAsync(
                    target,
                    """
SELECT COUNT(*)
FROM sys.tables
WHERE name IN
(
    N'App_GiaoVien_hs',
    N'App_KhoaHoc_NhomDaoTao',
    N'App_HocVien_PhanCong',
    N'App_AssignmentOperation'
);
""")));
            Assert.Equal(
                0,
                Convert.ToInt32(await ScalarAsync(
                    target,
                    "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'QLHV_AssignmentApiRole';")));

            await ExecutePatchAsync(target, MigrationRelativePath, database);
            await ExecuteAsync(target, SeedValidAssignmentSql);

            await AssertSqlErrorAsync(
                529333,
                () => ExecuteAsync(target, CrossProfileAssignmentSql));
            await ExecuteAsync(target, DossierOnlyNoGroupSql);
            await AssertSqlErrorAsync(
                2601,
                () => ExecuteAsync(target, DossierOnlyNoGroupSql));
            await AssertSqlErrorAsync(
                547,
                () => ExecuteAsync(target, InvalidNoGroupInheritanceSql));
            await AssertSqlErrorAsync(
                547,
                () => ExecuteAsync(target, InvalidNoGroupOverrideFlagsSql));
            await AssertSqlErrorAsync(
                529334,
                () => ExecuteAsync(target, InvalidInheritedGroupDefaultSql));
            await ExecuteAsync(
                target,
                "INSERT dbo.App_ImportBatch(EntityType) VALUES (N'OTHER');");
            await AssertSqlErrorAsync(
                529335,
                () => ExecuteAsync(target, WrongImportEntitySql));
            await AssertSqlErrorAsync(
                529336,
                () => ExecuteAsync(
                    target,
                    "UPDATE dbo.App_HocVien_PhanCong SET GhiChu=N'overwrite';"));
            await AssertSqlErrorAsync(
                529337,
                () => ExecuteAsync(
                    target,
                    "DELETE FROM dbo.App_HocVien_PhanCong;"));

            await ExecuteAsync(
                target,
                """
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
""");

            await AssertSqlErrorAsync(
                529355,
                () => ExecutePatchAsync(
                    target,
                    RollbackRelativePath,
                    database));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (Convert.ToInt32(await ScalarAsync(
                    master,
                    $"SELECT COUNT(*) FROM sys.databases WHERE name=N'{database}';")) == 1)
            {
                await ExecuteAsync(
                    master,
                    $"""
ALTER DATABASE [{database}]
SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [{database}];
""");
            }
        }
    }

    private const string FixtureSql = """
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
    AuditLogId bigint IDENTITY(1,1) NOT NULL PRIMARY KEY
);
CREATE USER [NT SERVICE\QLHV_APP_RealtimeWorker] WITHOUT LOGIN;
""";

    private const string SeedValidAssignmentSql = """
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
""";

    private const string CrossProfileAssignmentSql = """
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,NhomDaoTaoId,NguonGan,CreatedBy)
SELECT learner.HocVienId,groupRow.NhomDaoTaoId,'GROUP',N'rehearsal'
FROM dbo.App_HocVien AS learner
INNER JOIN dbo.App_KhoaHoc_NhomDaoTao AS groupRow
    ON groupRow.MaNhom=N'N1'
WHERE learner.MaDK=N'MOTO-DK1';
""";

    private const string DossierOnlyNoGroupSql = """
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,GiaoVienHoSoId,IsGiaoVienDungLopOverride,IsXeTapOverride,
     IsXeBaiSo10Override,NguonGan,CreatedBy)
SELECT learner.HocVienId,receiver.GiaoVienHsId,1,1,1,'MANUAL',N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien_hs AS receiver
WHERE learner.MaDK=N'OTO-DK2';
""";

    private const string InvalidNoGroupInheritanceSql = """
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,GiaoVienDungLopId,NguonGan,CreatedBy)
SELECT learner.HocVienId,teacher.GiaoVienId,'MANUAL',N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien AS teacher
WHERE learner.MaDK=N'MOTO-DK1';
""";

    private const string InvalidNoGroupOverrideFlagsSql = """
INSERT dbo.App_HocVien_PhanCong
    (HocVienId,GiaoVienHoSoId,IsGiaoVienDungLopOverride,IsXeTapOverride,
     IsXeBaiSo10Override,NguonGan,CreatedBy)
SELECT learner.HocVienId,receiver.GiaoVienHsId,1,0,1,'MANUAL',N'rehearsal'
FROM dbo.App_HocVien AS learner
CROSS JOIN dbo.App_GiaoVien_hs AS receiver
WHERE learner.MaDK=N'OTO-DK3';
""";

    private const string InvalidInheritedGroupDefaultSql = """
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
""";

    private const string WrongImportEntitySql = """
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
""";

    private static async Task AssertSqlErrorAsync(
        int expected,
        Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(action);
        Assert.Equal(expected, exception.Number);
    }

    private static async Task ExecutePatchAsync(
        SqlConnection connection,
        string relativePath,
        string rehearsalDatabase)
    {
        var sql = Read(relativePath);
        const string usePattern = @"(?im)^\s*USE\s+\[QLHV_APP\];\s*$";
        Assert.Single(Regex.Matches(sql, usePattern).Cast<Match>());
        sql = Regex.Replace(
            sql,
            usePattern,
            _ => $"USE [{rehearsalDatabase}];",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        foreach (var batch in Regex.Split(
                     sql,
                     @"(?im)^\s*GO\s*(?:--.*)?$"))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await ExecuteAsync(connection, batch);
            }
        }
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(
        SqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 60;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static string ConnectionString(string server, string database)
        => new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
            Pooling = false,
            ConnectTimeout = 15,
            ApplicationName =
                "QLHV.AssignmentMigration.IsolatedRehearsal",
        }.ConnectionString;

    private static int Count(string value, string pattern)
        => Regex.Matches(value, pattern).Count;

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}

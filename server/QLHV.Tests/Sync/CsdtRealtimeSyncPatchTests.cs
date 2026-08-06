using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeSyncPatchTests
{
    private static readonly string[] RequiredTrackedTables =
    [
        "DM_DonViGTVT",
        "KhoaHoc",
        "BaoCaoI",
        "NguoiLX",
        "NguoiLX_HoSo",
        "NguoiLX_GPLX",
        "NguoiLXHS_GiayTo",
    ];

    private static readonly string[] OptionalTrackedTables =
    [
        "GiaoVien",
        "KhoaHoc_GiaoVien",
    ];

    private static readonly string[] TrackedTables =
        RequiredTrackedTables.Concat(OptionalTrackedTables).ToArray();

    [Theory]
    [InlineData("20260724_enable_oto_v2_change_tracking.sql", "CSDL_OTO")]
    [InlineData("20260724_enable_moto_v2_change_tracking.sql", "CSDL_MOTO")]
    [InlineData("20260724_enable_oto_v2_bak_change_tracking.sql", "CSDL_OTO_BAK")]
    [InlineData("20260724_enable_moto_v2_bak_change_tracking.sql", "CSDL_MOTO_BAK")]
    public void Change_tracking_patches_are_exact_database_idempotent_and_schema_only(
        string fileName,
        string databaseName)
    {
        var patch = ReadPatch(fileName);

        Assert.Contains($"USE [{databaseName}];", patch, StringComparison.Ordinal);
        Assert.Contains($"IF DB_NAME() <> N'{databaseName}'", patch, StringComparison.Ordinal);
        Assert.Contains($"ALTER DATABASE [{databaseName}]", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("state_desc = N'ONLINE'", patch, StringComparison.Ordinal);
        Assert.Contains("is_read_only = 0", patch, StringComparison.Ordinal);
        Assert.Contains("CHANGE_RETENTION = 7 DAYS", patch, StringComparison.Ordinal);
        Assert.Contains("AUTO_CLEANUP = ON", patch, StringComparison.Ordinal);
        Assert.Contains("sys.key_constraints", patch, StringComparison.Ordinal);
        Assert.Contains("primary key", patch, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DECLARE @OptionalTables table", patch, StringComparison.Ordinal);
        Assert.Contains("SKIPPED_UNSUPPORTED_SCHEMA", patch, StringComparison.Ordinal);
        Assert.Contains(") <> 7", patch, StringComparison.Ordinal);
        Assert.Equal(9, CountOccurrences(patch, "ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON)"));

        var requiredBlock = Between(
            patch,
            "INSERT INTO @RequiredTables (SchemaName, TableName)",
            "INSERT INTO @OptionalTables (SchemaName, TableName)");
        foreach (var table in RequiredTrackedTables)
        {
            Assert.Contains($"(N'dbo', N'{table}')", requiredBlock, StringComparison.Ordinal);
            Assert.Contains($"dbo.{table}", patch, StringComparison.Ordinal);
        }

        foreach (var table in OptionalTrackedTables)
        {
            Assert.DoesNotContain($"(N'dbo', N'{table}')", requiredBlock, StringComparison.Ordinal);
            Assert.Contains($"(N'dbo', N'{table}')", patch, StringComparison.Ordinal);
            Assert.Contains($"optional dbo.{table} skipped", patch, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("DISABLE CHANGE_TRACKING", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TRIGGER", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("20260724_make_oto_v1_realtime_compatible.sql", "CSDL_OTO_V1")]
    [InlineData("20260724_make_moto_v1_realtime_compatible.sql", "CSDL_MOTO_V1")]
    [InlineData("20260724_make_oto_v1_bak_realtime_compatible.sql", "CSDL_OTO_V1_BAK")]
    [InlineData("20260724_make_moto_v1_bak_realtime_compatible.sql", "CSDL_MOTO_V1_BAK")]
    public void Compatibility_patches_widen_safely_preserve_fk_and_never_touch_certificate_number(
        string fileName,
        string databaseName)
    {
        var patch = ReadPatch(fileName);

        Assert.Contains($"USE [{databaseName}];", patch, StringComparison.Ordinal);
        Assert.Contains($"IF DB_NAME() <> N'{databaseName}'", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT OFF;", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("OR OBJECT_ID(N'dbo.GiaoVien'", patch, StringComparison.Ordinal);

        Assert.Contains("TenDV nvarchar(1000) NOT NULL", patch, StringComparison.Ordinal);
        Assert.Contains("CoQuanQL nvarchar(1000) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("CacHangDaCo nvarchar(500) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("CacMonHoc nvarchar(500) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("LoaiGiaoVien nvarchar(50) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("NgayHHGPLX datetime NULL", patch, StringComparison.Ordinal);
        Assert.Contains("NoiCapGCN nvarchar(500) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("GhiChu nvarchar(500) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("HinhThuc_TuyenDung nvarchar(50) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("LoaiHinh_DaoTao nvarchar(500) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("QDThucHanhHinh float NULL", patch, StringComparison.Ordinal);

        Assert.Contains("DROP CONSTRAINT FK_GiaoVien_DM_HangGPLX", patch, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN HangGPLX varchar(3) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT FK_GiaoVien_DM_HangGPLX", patch, StringComparison.Ordinal);
        Assert.Contains("WITH CHECK", patch, StringComparison.Ordinal);
        Assert.Contains("CHECK CONSTRAINT FK_GiaoVien_DM_HangGPLX", patch, StringComparison.Ordinal);

        Assert.Contains("SoGiayCNTN", patch, StringComparison.Ordinal);
        Assert.Contains("max_length = 60", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER COLUMN SoGiayCNTN", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE dbo.", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Qlhv_state_patch_has_durable_per_domain_checkpoints_and_guarded_commands()
    {
        var patch = ReadPatch("20260724_add_csdt_realtime_sync.sql");

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("IF DB_NAME() <> N'QLHV_APP'", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);


        var tables = new[]
        {
            "App_CsdtRealtimeStream",
            "App_CsdtRealtimeDomainState",
            "App_CsdtRealtimeRun",
            "App_CsdtRealtimeRunDomain",
            "App_CsdtRealtimeCommand",
            "App_CsdtRealtimeTombstone",
            "App_CsdtRealtimeConflict",
            "App_CsdtRealtimeEntityState",
            "App_CsdtRealtimeSourceIdentity",
        };
        foreach (var table in tables)
        {
            Assert.Contains($"CREATE TABLE dbo.{table}", patch, StringComparison.Ordinal);
        }

        foreach (var table in TrackedTables)
        {
            Assert.Contains($"N'{table}'", patch, StringComparison.Ordinal);
        }

        Assert.Contains("BaselineVersion", patch, StringComparison.Ordinal);
        Assert.Contains("LastSuccessfulVersion", patch, StringComparison.Ordinal);
        Assert.Contains("CurrentSourceVersion", patch, StringComparison.Ordinal);
        Assert.Contains("MinimumValidVersion", patch, StringComparison.Ordinal);
        Assert.Contains("LagVersions", patch, StringComparison.Ordinal);
        Assert.Contains("RetryCount", patch, StringComparison.Ordinal);
        Assert.Contains("SourceRows bigint", patch, StringComparison.Ordinal);
        Assert.Contains("TargetRows bigint", patch, StringComparison.Ordinal);
        Assert.Contains("TombstoneRows", patch, StringComparison.Ordinal);
        Assert.Contains("EntityKeyHash binary(32)", patch, StringComparison.Ordinal);
        Assert.Contains("SourceHash binary(32)", patch, StringComparison.Ordinal);
        Assert.Contains("TargetHash binary(32)", patch, StringComparison.Ordinal);
        Assert.Contains("SourceIdentityHash binary(32)", patch, StringComparison.Ordinal);
        Assert.Contains("IdentityStatus nvarchar(20)", patch, StringComparison.Ordinal);
        Assert.Contains("LastSeenVersion bigint", patch, StringComparison.Ordinal);
        Assert.Contains("LastSeenAtUtc datetime2(7)", patch, StringComparison.Ordinal);
        Assert.Contains("N'PRESENT'", patch, StringComparison.Ordinal);
        Assert.Contains("N'MISSING'", patch, StringComparison.Ordinal);
        Assert.Contains("IX_App_CsdtRealtimeSourceIdentity_Status", patch, StringComparison.Ordinal);

        Assert.Contains("N'OTO_V2_TO_V1'", patch, StringComparison.Ordinal);
        Assert.Contains("N'OTO_V2'", patch, StringComparison.Ordinal);
        Assert.Contains("N'OTO_V1'", patch, StringComparison.Ordinal);
        Assert.Contains("N'66029'", patch, StringComparison.Ordinal);
        Assert.Contains("N'MOTO_V2_TO_V1'", patch, StringComparison.Ordinal);
        Assert.Contains("N'MOTO_V2'", patch, StringComparison.Ordinal);
        Assert.Contains("N'MOTO_V1'", patch, StringComparison.Ordinal);
        Assert.Contains("N'66030'", patch, StringComparison.Ordinal);

        Assert.Contains("UX_App_CsdtRealtimeRun_Active", patch, StringComparison.Ordinal);
        Assert.Contains("UX_App_CsdtRealtimeCommand_Active", patch, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(patch, "WHERE ActiveSlot = 1"));
        Assert.Contains("ActiveSlot IS NULL", patch, StringComparison.Ordinal);
        Assert.Contains("ExpectedRowVersion binary(8)", patch, StringComparison.Ordinal);
        Assert.Contains("N'REVERSE'", patch, StringComparison.Ordinal);
        Assert.Contains("AttemptCount int NOT NULL", patch, StringComparison.Ordinal);
        Assert.Contains("LastAttemptAtUtc datetime2(7)", patch, StringComparison.Ordinal);
        Assert.Contains("SucceededAtUtc datetime2(7)", patch, StringComparison.Ordinal);
        Assert.Contains("CommandStatus IN (N'QUEUED', N'RUNNING', N'SUCCEEDED', N'PARTIAL'", patch, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.App_CsdtRealtimeRunDomain', N'AttemptCount')", patch, StringComparison.Ordinal);

        Assert.DoesNotContain("Password", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", patch, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadPatch(string fileName)
        => File.ReadAllText(FindWorkspaceFile("database", "patches", fileName));

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing marker {start}.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing marker {end}.");
        return source[startIndex..endIndex];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindWorkspaceFile(
        string firstPathPart,
        params string[] remainingPathParts)
        => FindWorkspaceFileFromCaller(
            new[] { firstPathPart }.Concat(remainingPathParts).ToArray());

    private static string FindWorkspaceFileFromCaller(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(pathParts));
    }
}

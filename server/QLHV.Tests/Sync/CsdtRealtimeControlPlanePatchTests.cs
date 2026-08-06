using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeControlPlanePatchTests
{
    public static IEnumerable<object[]> Patches()
    {
        yield return
        [
            "20260726_add_csdt_control_plane_oto_v1.sql",
            "CSDL_OTO_V1",
            "OTO_V1",
            "OTO_V2",
            "OTO_V2_TO_V1",
            "66029",
        ];
        yield return
        [
            "20260726_add_csdt_control_plane_moto_v1.sql",
            "CSDL_MOTO_V1",
            "MOTO_V1",
            "MOTO_V2",
            "MOTO_V2_TO_V1",
            "66030",
        ];
        yield return
        [
            "20260726_add_csdt_control_plane_oto_v1_bak.sql",
            "CSDL_OTO_V1_BAK",
            "OTO_V1_BAK",
            "OTO_V2_BAK",
            "OTO_V2_TO_V1",
            "66029",
        ];
        yield return
        [
            "20260726_add_csdt_control_plane_moto_v1_bak.sql",
            "CSDL_MOTO_V1_BAK",
            "MOTO_V1_BAK",
            "MOTO_V2_BAK",
            "MOTO_V2_TO_V1",
            "66030",
        ];
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Target_control_plane_patches_are_exact_database_and_idempotent(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        var patch = ReadPatch(fileName);
        var lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        Assert.Equal($"USE [{database}];", lines[0]);
        Assert.Equal("GO", lines[1]);
        Assert.Contains($"IF DB_NAME() <> N'{database}'", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains($"TargetProfile = '{targetProfile}'", patch, StringComparison.Ordinal);
        Assert.Contains($"SourceProfile = '{sourceProfile}'", patch, StringComparison.Ordinal);
        Assert.Contains($"StreamCode = '{streamCode}'", patch, StringComparison.Ordinal);
        Assert.Contains($"MaCSDT = '{maCsdt}'", patch, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(", patch, StringComparison.Ordinal);
        Assert.Contains("sys.columns", patch, StringComparison.Ordinal);
        Assert.Contains("sys.indexes", patch, StringComparison.Ordinal);
        Assert.Contains("sys.index_columns", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("THROW 527602", patch, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Target_control_plane_patches_create_every_required_object_and_invariant(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = targetProfile;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);

        foreach (var table in new[]
                 {
                     "QLHV_CsdtRealtimeSourceMembership",
                     "QLHV_CsdtRealtimeOwnershipClaim",
                     "QLHV_CsdtRealtimeMembershipJournal",
                     "QLHV_CsdtRealtimeCycle",
                     "QLHV_CsdtRealtimeCycleDomain",
                     "QLHV_CsdtRealtimeStreamCoverage",
                     "QLHV_CsdtRealtimeCheckpoint",
                 })
        {
            Assert.Contains($"CREATE TABLE dbo.{table}", patch, StringComparison.Ordinal);
        }

        foreach (var required in new[]
                 {
                     "CanonicalBusinessKey varbinary(512) NOT NULL",
                     "TargetEqualityKey varbinary(512) NOT NULL",
                     "DmDonViGtvtMaDV varchar(6)",
                     "GiaoVienMaGV varchar(8)",
                     "KhoaHocMaKH varchar(13)",
                     "KhoaHocGiaoVienMaLichLV int NULL",
                     "BaoCaoIMaBCI varchar(18)",
                     "NguoiLXMaDK varchar(25)",
                     "NguoiLXHoSoMaDK varchar(25)",
                     "GiayToMaGT int NULL",
                     "GiayToMaDK varchar(25)",
                     "COLLATE SQL_Latin1_General_CP1_CI_AS NULL",
                     "CanonicalBusinessKeyHash binary(32) NOT NULL",
                     "HashKeyVersion int NOT NULL",
                     "OwnershipReserved bit NOT NULL",
                     "DeletedAtSourceVersion bigint NULL",
                     "ReactivatedAtSourceVersion bigint NULL",
                     "StagedKeySetHash binary(32) NULL",
                     "TargetCommittedAtUtc datetime2(7) NULL",
                     "CheckpointPublishedAtUtc datetime2(7) NULL",
                     "AppliedSourceVersion bigint NOT NULL",
                     "CommittedCycleId uniqueidentifier NOT NULL",
                     "CheckpointStatus varchar(16) NOT NULL",
                     "PublishedAtUtc datetime2(7) NOT NULL",
                     "VerifiedAtUtc datetime2(7) NULL",
                     "SourceKeySetHash binary(32) NOT NULL",
                     "ResultHash binary(32) NULL",
                     "IsComplete bit NOT NULL",
                     "RowVersion rowversion NOT NULL",
                 })
        {
            Assert.Contains(required, patch, StringComparison.Ordinal);
        }

        Assert.Contains(
            "UX_QLHV_CsdtRealtimeMembership_RouteKey",
            patch,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeMembership_TargetOwner",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "Legacy varbinary target-owner uniqueness is not authoritative",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "IX_QLHV_CsdtRealtimeMembership_TargetEqualityToken",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "UX_QLHV_CsdtRealtimeOwnershipClaim_GiayTo",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE GiayToMaGT IS NOT NULL AND GiayToMaDK IS NOT NULL",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "IX_QLHV_CsdtRealtimeMembership_ActiveLookup",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "UX_QLHV_CsdtRealtimeJournal_Event",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "UX_QLHV_CsdtRealtimeCheckpoint_Stream",
            patch,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CanonicalBusinessKeyHash)",
            Between(
                patch,
                "CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeMembership_RouteKey",
                "IF NOT EXISTS"),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Patch_shape_validation_is_bidirectional_and_exact(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = targetProfile;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);

        foreach (var required in new[]
                 {
                     "ActualColumnCount <> expected.ExpectedColumnCount",
                     "columnMetadata.column_id <> required.ExpectedColumnOrdinal",
                     "columnMetadata.precision <>",
                     "columnMetadata.scale <>",
                     "columnMetadata.is_nullable <> required.IsNullable",
                     "columnMetadata.is_identity <> required.IsIdentity",
                     "columnMetadata.is_computed <> 0",
                     "columnMetadata.system_type_id <> 189",
                     "defaultMetadata.name <> required.ConstraintName",
                     "DECLARE @RequiredPrimaryKeyColumns table",
                     "DECLARE @RequiredForeignKeyColumns table",
                     "foreignKeyColumn.constraint_column_id",
                     "foreignKey.delete_referential_action",
                     "foreignKey.update_referential_action",
                     "DECLARE @RequiredIndexes table",
                     "DECLARE @RequiredIndexColumns table",
                     "required.IsIncluded",
                     "required.IsDescending",
                     "indexMetadata.filter_definition",
                     "indexMetadata.is_disabled = 1",
                     "indexMetadata.is_hypothetical = 1",
                     "DECLARE @RequiredCheckContracts table",
                     "ExpectedLiteralCount",
                     "STRING_SPLIT(required.RequiredLiterals",
                     "STRING_SPLIT(required.RequiredColumns",
                 })
        {
            Assert.Contains(required, patch, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Typed_claim_is_authoritative_and_committed_with_membership(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);

        Assert.Contains(
            "TargetEqualityProofStatus = 'TYPED_CLAIM'",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "TYPED_OWNER_SQLSERVER_SQL_LATIN1_GENERAL_CP1_CI_AS_V1",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            $"TargetProfile = '{targetProfile}'",
            Between(
                patch,
                "ADD CONSTRAINT CK_QLHV_CsdtRealtimeOwnershipClaim_Shape",
                "IF OBJECT_ID("),
            StringComparison.Ordinal);
        Assert.Contains(
            "FK_QLHV_CsdtRealtimeOwnershipClaim_Membership",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "UX_QLHV_CsdtRealtimeOwnershipClaim_Membership",
            patch,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Cycle_membership_and_coverage_references_are_explicit_foreign_keys(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = targetProfile;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);

        foreach (var foreignKey in new[]
                 {
                     "FK_QLHV_CsdtRealtimeMembership_FirstSeenCycle",
                     "FK_QLHV_CsdtRealtimeMembership_LastSeenCycle",
                     "FK_QLHV_CsdtRealtimeMembership_LastAppliedCycle",
                     "FK_QLHV_CsdtRealtimeCoverage_CompletedCycle",
                     "FK_QLHV_CsdtRealtimeCheckpoint_CommittedCycle",
                 })
        {
            Assert.Contains(foreignKey, patch, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("HAVING COUNT(*) = 2", patch, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Journal_idempotency_keeps_distinct_valid_transitions_distinct(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = targetProfile;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);
        var index = Between(
            patch,
            "CREATE UNIQUE NONCLUSTERED INDEX UX_QLHV_CsdtRealtimeJournal_Event",
            "IF NOT EXISTS");

        Assert.Contains("MembershipId, CycleId, BeforeStatus, AfterStatus", index, StringComparison.Ordinal);
        Assert.Contains("SourceVersion, ReasonCode, TargetAction", index, StringComparison.Ordinal);
        Assert.Contains(
            "(N'UX_QLHV_CsdtRealtimeJournal_Event', 7, N'TargetAction', 0, 0)",
            patch,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Failed_and_conflict_states_require_error_and_ordered_timestamps(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = targetProfile;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);
        var cycleError = Between(
            patch,
            "ADD CONSTRAINT CK_QLHV_CsdtRealtimeCycle_Error",
            "IF OBJECT_ID(");
        var cycleTimes = Between(
            patch,
            "ADD CONSTRAINT CK_QLHV_CsdtRealtimeCycle_Timestamps",
            "IF OBJECT_ID(");
        var domainShape = Between(
            patch,
            "ADD CONSTRAINT CK_QLHV_CsdtRealtimeDomain_Counts",
            "IF OBJECT_ID(");

        Assert.Contains("CycleStatus IN ('FAILED', 'CONFLICT')", cycleError, StringComparison.Ordinal);
        Assert.Contains("AND ErrorCode IN", cycleError, StringComparison.Ordinal);
        Assert.Contains("CycleStatus NOT IN ('FAILED', 'CONFLICT')", cycleError, StringComparison.Ordinal);
        Assert.Contains("AND ErrorCode IS NULL", cycleError, StringComparison.Ordinal);
        Assert.Contains("StagedAtUtc >= StartedAtUtc", cycleTimes, StringComparison.Ordinal);
        Assert.Contains("ValidatedAtUtc >= StagedAtUtc", cycleTimes, StringComparison.Ordinal);
        Assert.Contains("CompletedAtUtc >= StartedAtUtc", cycleTimes, StringComparison.Ordinal);
        Assert.Contains("DomainStatus IN ('FAILED', 'CONFLICT')", domainShape, StringComparison.Ordinal);
        Assert.Contains("AND ErrorCode IN", domainShape, StringComparison.Ordinal);
        Assert.Contains("CompletedAtUtc >= StartedAtUtc", domainShape, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Domain_allowlist_is_exact_and_future_domains_need_migration(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = targetProfile;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);
        var tableConstraint = Between(
            patch,
            "ADD CONSTRAINT CK_QLHV_CsdtRealtimeMembership_Table",
            "IF OBJECT_ID(");

        foreach (var domain in new[]
                 {
                     "DM_DonViGTVT",
                     "GiaoVien",
                     "KhoaHoc",
                     "KhoaHoc_GiaoVien",
                     "BaoCaoI",
                     "NguoiLX",
                     "NguoiLX_HoSo",
                     "NguoiLXHS_GiayTo",
                 })
        {
            Assert.Contains($"'{domain}'", tableConstraint, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("'NguoiLX_GPLX'", tableConstraint, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void Target_control_plane_patches_have_no_destructive_or_secret_bearing_sql(
        string fileName,
        string database,
        string targetProfile,
        string sourceProfile,
        string streamCode,
        string maCsdt)
    {
        _ = database;
        _ = targetProfile;
        _ = sourceProfile;
        _ = streamCode;
        _ = maCsdt;
        var patch = ReadPatch(fileName);

        Assert.DoesNotMatch(
            new Regex(
                @"\b(DROP|TRUNCATE|DELETE\s+FROM|MERGE|ALTER\s+DATABASE|DISABLE)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            patch);
        Assert.DoesNotContain("ConnectionString", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE LOGIN", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE USER", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_uses_caller_transaction_and_is_not_wired_to_processor()
    {
        var repository = File.ReadAllText(
            FindWorkspaceFile(
                "server",
                "QLHV.Infrastructure",
                "Sync",
                "Realtime",
                "ControlPlane",
                "CsdtRealtimeTargetControlPlaneRepository.cs"));
        var processor = File.ReadAllText(
            FindWorkspaceFile(
                "server",
                "QLHV.Infrastructure",
                "Sync",
                "Realtime",
                "CsdtRealtimeStreamProcessor.cs"));

        Assert.Contains("DbConnection connection", repository, StringComparison.Ordinal);
        Assert.Contains("DbTransaction transaction", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransaction", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitAsync(", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("RollbackAsync(", repository, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UPDATE dbo.QLHV_CsdtRealtimeMembershipJournal",
            repository,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DELETE FROM dbo.QLHV_CsdtRealtimeMembershipJournal",
            repository,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CsdtRealtimeTargetControlPlaneRepository",
            processor,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ICsdtRealtimeTargetControlPlaneRepository",
            processor,
            StringComparison.Ordinal);
        Assert.Contains(
            "FROM dbo.QLHV_CsdtRealtimeOwnershipClaim AS claim",
            repository,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AND TargetEqualityKey = @TargetEqualityKey",
            repository,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT INTO dbo.QLHV_CsdtRealtimeOwnershipClaim",
            repository,
            StringComparison.Ordinal);
    }

    private static string ReadPatch(string fileName)
        => File.ReadAllText(FindWorkspaceFile("database", "patches", fileName));

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing marker {start}.");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing marker {end}.");
        return source[startIndex..endIndex];
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

        throw new FileNotFoundException(
            "Cannot locate workspace file.",
            Path.Combine(pathParts));
    }
}

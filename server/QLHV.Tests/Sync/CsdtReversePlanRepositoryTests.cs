using System.Data;
using System.Security.Cryptography;
using QLHV.Application.Sync.Realtime;
using QLHV.Infrastructure.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtReversePlanRepositoryTests
{
    private static readonly byte[] OriginalV2Hash = SHA256.HashData([1, 2, 3]);
    private static readonly byte[] ChangedV1Hash = SHA256.HashData([4, 5, 6]);

    [Fact]
    public void Invalid_raw_identity_is_never_reclassified_as_insertable()
    {
        var result = CsdtReversePlanEngine.Classify(
            identityIsValid: false,
            targetExists: false,
            ChangedV1Hash,
            targetHash: null,
            ledger: null);

        Assert.Equal(CsdtReverseClassification.IdentityChanged, result);
    }

    [Fact]
    public void V1_only_row_requires_review_and_is_never_an_insert()
    {
        var result = CsdtReversePlanEngine.Classify(
            identityIsValid: true,
            targetExists: false,
            ChangedV1Hash,
            targetHash: null,
            ledger: null);

        Assert.Equal(CsdtReverseClassification.V1OnlyRequiresReview, result);
    }

    [Fact]
    public void V2_change_after_forward_ledger_is_a_conflict()
    {
        var changedV2 = SHA256.HashData([7, 8, 9]);
        var result = CsdtReversePlanEngine.Classify(
            identityIsValid: true,
            targetExists: true,
            ChangedV1Hash,
            changedV2,
            Ledger(OriginalV2Hash));

        Assert.Equal(CsdtReverseClassification.ConflictRequiresReview, result);
    }

    [Fact]
    public void V1_change_is_safe_only_while_V2_still_matches_forward_ledger()
    {
        var result = CsdtReversePlanEngine.Classify(
            identityIsValid: true,
            targetExists: true,
            ChangedV1Hash,
            OriginalV2Hash,
            Ledger(OriginalV2Hash));

        Assert.Equal(CsdtReverseClassification.SafeUpdate, result);
    }

    [Fact]
    public void Equal_rows_are_skipped_when_V2_still_matches_forward_ledger()
    {
        var result = CsdtReversePlanEngine.Classify(
            identityIsValid: true,
            targetExists: true,
            OriginalV2Hash,
            OriginalV2Hash,
            Ledger(OriginalV2Hash));

        Assert.Equal(CsdtReverseClassification.Skipped, result);
    }

    [Fact]
    public void Stable_plan_token_excludes_generation_time_and_contains_no_raw_key()
    {
        const string rawKey = """{"MaDK":"66029-20260724-000001"}""";
        var assessment = Assessment(
            rawKey,
            CsdtReverseClassification.SafeUpdate,
            OriginalV2Hash,
            ChangedV1Hash);
        var route = CsdtRealtimeStreamCatalog
            .GetLiveByVehicle(CsdtRealtimeVehicleTypes.Oto)
            .Reverse();
        var expiry = new DateTimeOffset(2026, 7, 24, 10, 5, 0, TimeSpan.Zero);

        var first = CsdtReversePlanEngine.BuildPlan(
            route,
            "66029K260001",
            [assessment],
            new DateTimeOffset(2026, 7, 24, 10, 0, 1, TimeSpan.Zero),
            expiry);
        var second = CsdtReversePlanEngine.BuildPlan(
            route,
            "66029K260001",
            [assessment],
            new DateTimeOffset(2026, 7, 24, 10, 0, 59, TimeSpan.Zero),
            expiry);

        Assert.Equal(first.PlanToken, second.PlanToken);
        Assert.Matches("^[0-9A-F]{64}$", first.PlanToken);
        Assert.DoesNotContain("66029-20260724-000001", first.PlanToken, StringComparison.Ordinal);
        Assert.Equal(0, first.SafeInsertRows);
        Assert.Equal(1, first.SafeUpdateRows);
        Assert.True(first.Executable);
    }

    [Fact]
    public void Review_classifications_are_counted_without_exposing_entity_details()
    {
        var assessments = new[]
        {
            Assessment(
                """{"MaDK":"V1-ONLY"}""",
                CsdtReverseClassification.V1OnlyRequiresReview,
                OriginalV2Hash,
                targetHash: null),
            Assessment(
                """{"MaDK":"INVALID"}""",
                CsdtReverseClassification.IdentityChanged,
                OriginalV2Hash,
                targetHash: null),
            Assessment(
                """{"MaDK":"CONFLICT"}""",
                CsdtReverseClassification.ConflictRequiresReview,
                OriginalV2Hash,
                ChangedV1Hash),
        };
        var route = CsdtRealtimeStreamCatalog
            .GetLiveByVehicle(CsdtRealtimeVehicleTypes.Oto)
            .Reverse();

        var plan = CsdtReversePlanEngine.BuildPlan(
            route,
            maKhoaHoc: null,
            assessments,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(plan.Executable);
        Assert.Equal(1, plan.V1OnlyRequiresReview);
        Assert.Equal(1, plan.IdentityChanged);
        Assert.Equal(1, plan.ConflictRequiresReview);
        Assert.Equal(0, plan.SafeInsertRows);
        Assert.All(plan.Blockers, blocker => Assert.DoesNotContain("MaDK", blocker));
    }

    [Fact]
    public void Binary_values_are_hashed_by_content_not_type_name()
    {
        var metadata = Metadata("NguoiLX", ("MaDK", "varchar"), ("Anh", "varbinary"));
        var first = Row(metadata, "66029-20260724-000001", new byte[] { 1, 2, 3 });
        var second = Row(metadata, "66029-20260724-000001", new byte[] { 1, 2, 4 });

        var firstHash = CsdtRealtimeTargetWriter.HashRow(
            first,
            metadata.WritableColumns);
        var secondHash = CsdtRealtimeTargetWriter.HashRow(
            second,
            metadata.WritableColumns);

        Assert.False(CryptographicOperations.FixedTimeEquals(firstHash, secondHash));
    }

    [Fact]
    public void Reverse_writer_source_has_no_insert_delete_or_primary_key_assignment()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            "CsdtRealtimeTargetWriter.cs"));
        var start = source.IndexOf(
            "private static async Task<long> UpdateExistingInTransactionAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private static void ApplyRequiredBusinessMappings",
            start,
            StringComparison.Ordinal);
        var updateOnlyMethod = source[start..end];

        Assert.DoesNotContain("InsertMissingAsync", updateOnlyMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE ", updateOnlyMethod, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UpdateChangedAsync", updateOnlyMethod, StringComparison.Ordinal);

        var updateSqlStart = source.IndexOf(
            "private static async Task<long> UpdateChangedAsync",
            StringComparison.Ordinal);
        var updateSqlEnd = source.IndexOf(
            "private static async Task<DataTable> ReadLockedTargetRowsAsync",
            updateSqlStart,
            StringComparison.Ordinal);
        var updateSql = source[updateSqlStart..updateSqlEnd];
        Assert.Contains("!column.IsPrimaryKey && !column.IsIdentity", updateSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Atomic_writer_uses_one_target_transaction_for_the_whole_domain_batch()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            "CsdtRealtimeTargetWriter.cs"));
        var start = source.IndexOf(
            "public async Task<CsdtReverseAtomicWriteResult> UpdateExistingAtomicallyAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<long> UpdateExistingAsync",
            start,
            StringComparison.Ordinal);
        var atomicBatch = source[start..end];

        Assert.Equal(1, Count(atomicBatch, "BeginTransactionAsync"));
        Assert.Equal(1, Count(atomicBatch, "CommitAsync"));
        Assert.Contains("foreach (var write in remaining)", atomicBatch, StringComparison.Ordinal);
        Assert.Contains("UpdateExistingInTransactionAsync", atomicBatch, StringComparison.Ordinal);
        Assert.Contains("SafeRollbackAsync", atomicBatch, StringComparison.Ordinal);
        Assert.Contains("current?.Domain.IsOptional == true", atomicBatch, StringComparison.Ordinal);
        Assert.Contains("exception is not CsdtRealtimeTargetConflictException", atomicBatch, StringComparison.Ordinal);
        Assert.Contains("SKIPPED_UNSUPPORTED_SCHEMA", atomicBatch, StringComparison.Ordinal);
    }

    [Fact]
    public void Reverse_completion_is_compare_and_set_guarded_and_never_advances_forward_checkpoint()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            "CsdtRealtimeWorkerStateRepository.cs"));
        var start = source.IndexOf(
            "internal async Task CompleteReverseRunAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "internal async Task FailAtomicReverseRunAsync",
            start,
            StringComparison.Ordinal);
        var completion = source[start..end];

        Assert.Contains("EnsureMandatoryDomainsCompleted", completion, StringComparison.Ordinal);
        Assert.Contains("CommandStatus = N'RUNNING'", completion, StringComparison.Ordinal);
        Assert.Contains("ActiveSlot = 1", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("App_CsdtRealtimeStream", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("LastSuccessfulVersion", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("BaselineVersion", completion, StringComparison.Ordinal);
        Assert.DoesNotContain("NextRetryAtUtc", completion, StringComparison.Ordinal);
    }

    [Fact]
    public void Mandatory_failure_rolls_back_attempted_domains_and_leaves_later_domain_pending()
    {
        var statuses = CsdtReverseAtomicExecutionPolicy.BuildRollbackStatuses(
            ["domain-1", "domain-2", "domain-3"],
            ["domain-1", "domain-2"],
            []);

        Assert.Equal("FAILED", statuses["domain-1"]);
        Assert.Equal("FAILED", statuses["domain-2"]);
        Assert.Equal("PENDING", statuses["domain-3"]);
        Assert.DoesNotContain(statuses.Values, status => status == "SUCCEEDED");
    }

    [Fact]
    public void Mandatory_domain_must_succeed_before_reverse_command_can_complete()
    {
        var results = CsdtRealtimeDomainCatalog.Ordered.Select(domain =>
            ExecutionResult(
                domain.Name,
                domain.Name == "KhoaHoc" ? "FAILED" : "SUCCEEDED")).ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CsdtReverseAtomicExecutionPolicy.EnsureMandatoryDomainsCompleted(results));

        Assert.Contains("KhoaHoc", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_unsupported_domain_does_not_hide_mandatory_completion()
    {
        var results = CsdtRealtimeDomainCatalog.Ordered.Select(domain =>
            ExecutionResult(
                domain.Name,
                domain.IsOptional ? "SKIPPED" : "SUCCEEDED")).ToArray();

        CsdtReverseAtomicExecutionPolicy.EnsureMandatoryDomainsCompleted(results);
    }

    [Fact]
    public void Retry_preserves_attempts_only_when_the_approved_source_digest_is_unchanged()
    {
        var assessment = Assessment(
            """{"MaDK":"RAW"}""",
            CsdtReverseClassification.SafeUpdate,
            ChangedV1Hash,
            OriginalV2Hash);
        var digest = CsdtReversePlanEngine.BuildSourceDigest(assessment);
        var recovery = new CsdtReverseRecovery(
            Guid.NewGuid(),
            new Dictionary<string, CsdtReverseDomainIntent>(StringComparer.Ordinal)
            {
                [assessment.Domain.Name] = new(
                    assessment.Domain.Name,
                    assessment.Domain.IsOptional,
                    1,
                    digest,
                    2),
            });

        var intent = Assert.Single(
            CsdtReversePlanEngine.BuildExecutionIntents([assessment], recovery));
        Assert.Equal(2, intent.AttemptCount);

        var changed = Assessment(
            """{"MaDK":"RAW"}""",
            CsdtReverseClassification.SafeUpdate,
            SHA256.HashData([99]),
            OriginalV2Hash);
        Assert.Throws<InvalidOperationException>(() =>
            CsdtReversePlanEngine.BuildExecutionIntents([changed], recovery));
    }

    [Fact]
    public void Retry_after_committed_target_detects_equal_rows_and_plans_no_duplicate_write()
    {
        var committed = Assessment(
            """{"MaDK":"RAW"}""",
            CsdtReverseClassification.Skipped,
            ChangedV1Hash,
            ChangedV1Hash);
        var digest = CsdtReversePlanEngine.BuildSourceDigest(committed);
        var recovery = new CsdtReverseRecovery(
            Guid.NewGuid(),
            new Dictionary<string, CsdtReverseDomainIntent>(StringComparer.Ordinal)
            {
                [committed.Domain.Name] = new(
                    committed.Domain.Name,
                    committed.Domain.IsOptional,
                    1,
                    digest,
                    1),
            });

        var intent = Assert.Single(
            CsdtReversePlanEngine.BuildExecutionIntents([committed], recovery));

        Assert.Equal(1, intent.AttemptCount);
        Assert.Empty(committed.SafeSnapshot.Rows.Rows.Cast<DataRow>());
        Assert.Empty(committed.ExpectedTargetHashes);
    }

    private static CsdtRealtimeEntityLedgerRow Ledger(byte[] sourceHash)
        => new(
            "NguoiLX",
            """{"MaDK":"RAW"}""",
            SHA256.HashData([10]),
            sourceHash,
            sourceHash,
            10);

    private static CsdtReverseDomainAssessment Assessment(
        string keyJson,
        CsdtReverseClassification classification,
        byte[] sourceHash,
        byte[]? targetHash)
    {
        var metadata = Metadata("NguoiLX", ("MaDK", "varchar"));
        var row = Row(metadata, keyJson);
        var entity = new CsdtReverseEntityAssessment(
            "NguoiLX",
            keyJson,
            CsdtRealtimeTargetWriter.HashKey(keyJson),
            sourceHash,
            targetHash,
            OriginalV2Hash,
            OriginalV2Hash,
            classification,
            row);
        var safeRows = row.Table.Clone();
        var expectedTargetHashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (classification == CsdtReverseClassification.SafeUpdate && targetHash is not null)
        {
            safeRows.ImportRow(row);
            expectedTargetHashes.Add(keyJson, targetHash);
        }

        return new CsdtReverseDomainAssessment(
            metadata.Domain,
            [entity],
            new CsdtRealtimeSnapshot(metadata, safeRows),
            expectedTargetHashes);
    }

    private static CsdtReverseDomainExecutionResult ExecutionResult(
        string domain,
        string status)
        => new(domain, status, 1, 0, 0, 1);

    private static int Count(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static CsdtRealtimeTableMetadata Metadata(
        string domain,
        params (string Name, string SqlType)[] columns)
    {
        var definition = new CsdtRealtimeDomainDefinition(
            domain,
            domain,
            [columns[0].Name],
            "1 = 1",
            CsdtRealtimeDomainGroup.Learner);
        var metadata = columns.Select((column, index) =>
            new CsdtRealtimeColumnMetadata(
                column.Name,
                column.SqlType,
                column.SqlType == "varbinary" ? (short)-1 : (short)100,
                0,
                0,
                true,
                false,
                false,
                false,
                index + 1,
                index == 0 ? 1 : null)).ToArray();
        return new CsdtRealtimeTableMetadata(definition, metadata);
    }

    private static DataRow Row(
        CsdtRealtimeTableMetadata metadata,
        params object[] values)
    {
        var table = new DataTable
        {
            CaseSensitive = true,
        };
        foreach (var column in metadata.Columns)
        {
            table.Columns.Add(
                column.Name,
                column.SqlType == "varbinary" ? typeof(byte[]) : typeof(string));
        }

        var row = table.NewRow();
        row.ItemArray = values;
        table.Rows.Add(row);
        return row;
    }

    private static string FindWorkspaceFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}

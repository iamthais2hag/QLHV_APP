using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QLHV.Application;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;
using QLHV.Application.Sync.Rt03;
using QLHV.Infrastructure;
using QLHV.Infrastructure.Sync;
using QLHV.Infrastructure.Sync.Rt01;
using Xunit.Abstractions;

namespace QLHV.Tests.Sync.Rt03;

/// <summary>
/// Separately opted-in RT-03 Task 2 operator harness. It is deliberately hosted
/// only by the test assembly: neither production host registers this writer.
/// Seal mode is SELECT-only. Execute mode accepts exactly one already sealed OTO
/// insert and has no update/delete/deactivate/general-sync path.
/// </summary>
public sealed class Rt03ProductionCanaryExecutionTests
{
    private const string RuntimeConfig = @"D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json";
    private const string EvidenceRootVariable = "QLHV_RT03_TASK2_EVIDENCE_ROOT";
    private const string PlanFileName = "04_sealed_canary_plan.json";
    private const string KeyFileName = "04_sealed_canary_plan.key";
    private const string SealReportFileName = "04_sealed_canary_plan_report.json";
    private const string ExecutionReportFileName = "08_canary_execution_result.json";
    private const string ObservationPlanFileName = "05_sealed_observation_plan.json";
    private const string ObservationKeyFileName = "05_sealed_observation_plan.key";
    private const string ObservationReportFileName = "05_sealed_observation_plan_report.json";
    private const string SourceGuid = "9A8B9BC1-18F3-4823-8123-3DC197A9D540";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITestOutputHelper _output;

    public Rt03ProductionCanaryExecutionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "ProductionReadOnlyOptIn")]
    public async Task Seal_final_window_single_insert_canary_plan_read_only()
    {
        if (!OptedIn("QLHV_RUN_RT03_SEAL_CANARY_PLAN"))
        {
            _output.WriteLine("RT-03 seal was not requested; no production connection opened.");
            return;
        }

        var evidenceRoot = RequireFreshEvidenceRoot();
        var planPath = Path.Combine(evidenceRoot, PlanFileName);
        var keyPath = Path.Combine(evidenceRoot, KeyFileName);
        Assert.False(File.Exists(planPath), "Refusing to overwrite an existing sealed plan.");
        Assert.False(File.Exists(keyPath), "Refusing to reuse an existing plan key.");

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = CreateReader(scope.ServiceProvider);
        var key = RandomNumberGenerator.GetBytes(32);
        var samples = await ReadThreeStableSamplesAsync(reader, key);
        var oto = samples[Rt03Profiles.Oto];
        var moto = samples[Rt03Profiles.Moto];
        var otoEvidence = oto.Evidence[0];
        var motoEvidence = moto.Evidence[0];

        var safeOto = SafeSourceOnlyCandidates(otoEvidence);
        Assert.Single(safeOto);
        Assert.Equal(1, otoEvidence.WouldInsertRows);
        Assert.Equal(0, otoEvidence.WouldUpdateRows);
        Assert.Equal(0, otoEvidence.WouldReactivateRows);
        Assert.Equal(0, otoEvidence.ConflictRows);
        Assert.Equal(0, otoEvidence.ManualReviewRows);
        Assert.Empty(motoEvidence.Candidates);
        Assert.Equal(0, motoEvidence.WouldInsertRows);
        Assert.Equal(0, motoEvidence.WouldUpdateRows);
        Assert.Equal(0, motoEvidence.WouldReactivateRows);
        Assert.Equal(0, motoEvidence.ConflictRows);
        Assert.Equal(0, motoEvidence.ManualReviewRows);

        var selectedProof = safeOto[0];
        var selectedRow = oto.Raw.MappedSourceRows.Single(row =>
            string.Equals(Rt01IdentityHmac(key, row.SourceProfileCode, row.SourceMaDK),
                selectedProof.IdentityHmac, StringComparison.Ordinal));
        var secret = Convert.ToHexString(key);
        var selectedIdentity = Rt03IdentityHmac(secret, selectedRow);
        var qlhvOwnedHash = QlhvOwnedPartitionHmac(key, oto.Raw.TargetRows, null);
        var absenceHash = Rt03Hash.Sha256($"ABSENT|{Rt03Profiles.Oto}|{selectedIdentity}");
        var candidate = new Rt03CanaryCandidate(
            "OTO-INSERT-01",
            Rt03Profiles.Oto,
            Rt03CandidateKind.Insert,
            selectedIdentity,
            "SOURCE_ONLY_NEW_ROW",
            selectedRow.V2RowHash,
            qlhvOwnedHash,
            "INSERT_EXACT_ONE_APP_HOCVIEN",
            "PRESERVE_ALL_PREEXISTING_TARGET_ROWS_AND_QLHV_OWNED_FIELDS",
            absenceHash,
            Array.Empty<string>(),
            Array.Empty<string>());
        var plan = new Rt03CanaryPlan(
            $"RT03-TASK2-{Guid.NewGuid():N}",
            Rt03Modes.Canary,
            "PRODUCTION",
            otoEvidence.MappingFingerprint,
            otoEvidence.SourceSchemaFingerprint,
            motoEvidence.SourceSchemaFingerprint,
            otoEvidence.TargetSchemaFingerprint,
            otoEvidence.StageHash,
            motoEvidence.StageHash,
            otoEvidence.TargetComparisonHash,
            motoEvidence.TargetComparisonHash,
            null,
            null,
            "PENDING",
            [candidate]);
        Rt03CanaryPlanValidator.Validate(plan);

        var sealedAt = DateTime.UtcNow;
        var payload = new Rt03SealedPayload(
            plan,
            sealedAt,
            sealedAt.AddMinutes(30),
            Rt03ProductionCatalog.RequiredDatabases.ToArray(),
            3,
            new Rt03StableProfileSnapshot(
                otoEvidence.SourceActiveRows, otoEvidence.TargetActiveRows,
                otoEvidence.TargetSoftDeletedRows, otoEvidence.NoChangeRows,
                otoEvidence.WouldInsertRows, otoEvidence.WouldUpdateRows,
                otoEvidence.WouldReactivateRows, otoEvidence.TargetOnlyActiveRows,
                otoEvidence.ConflictRows, otoEvidence.ManualReviewRows,
                otoEvidence.SourceKeySetHash, otoEvidence.TargetKeySetHash,
                otoEvidence.SourceOnlyHash),
            new Rt03StableProfileSnapshot(
                motoEvidence.SourceActiveRows, motoEvidence.TargetActiveRows,
                motoEvidence.TargetSoftDeletedRows, motoEvidence.NoChangeRows,
                motoEvidence.WouldInsertRows, motoEvidence.WouldUpdateRows,
                motoEvidence.WouldReactivateRows, motoEvidence.TargetOnlyActiveRows,
                motoEvidence.ConflictRows, motoEvidence.ManualReviewRows,
                motoEvidence.SourceKeySetHash, motoEvidence.TargetKeySetHash,
                motoEvidence.SourceOnlyHash),
            qlhvOwnedHash,
            1,
            null,
            null,
            SHA256Hex(key));
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var envelope = new Rt03SealedEnvelope(payload, SHA256Hex(payloadBytes));
        var planBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

        await File.WriteAllBytesAsync(keyPath, key);
        await File.WriteAllBytesAsync(planPath, planBytes);
        var firstRead = await File.ReadAllBytesAsync(planPath);
        var secondRead = await File.ReadAllBytesAsync(planPath);
        Assert.Equal(firstRead, secondRead);
        Assert.Equal(planBytes, firstRead);
        ValidateEnvelope(DeserializeEnvelope(firstRead), key);

        await WritePrivacySafeJsonAsync(Path.Combine(evidenceRoot, SealReportFileName), new
        {
            Evidence = "RT03_FRESH_SEALED_CANARY_PLAN",
            SealedAtUtc = sealedAt,
            ExpiresAtUtc = payload.ExpiresAtUtc,
            SampleCountPerProfile = 3,
            Oto = payload.Oto,
            Moto = payload.Moto,
            SelectedCandidateHmac = selectedIdentity,
            ExcludedCandidateCount = 0,
            PlanHash = plan.PlanHash,
            EnvelopeHash = envelope.PayloadHash,
            BusinessDataWrites = 0,
            CheckpointPublished = false,
            AutoSyncTouched = false,
        });
        _output.WriteLine($"RT03_SEALED_PLAN_READY plan={plan.PlanHash} candidates=1 excluded=0 writes=0");
    }

    [Fact]
    [Trait("Category", "ProductionReadOnlyOptIn")]
    public async Task Seal_fresh_observation_plan_when_no_candidate_is_canary_eligible()
    {
        if (!OptedIn("QLHV_RUN_RT03_SEAL_OBSERVATION_PLAN"))
        {
            _output.WriteLine("RT-03 observation seal was not requested; no production connection opened.");
            return;
        }

        var evidenceRoot = RequireFreshEvidenceRoot();
        var planPath = Path.Combine(evidenceRoot, ObservationPlanFileName);
        var keyPath = Path.Combine(evidenceRoot, ObservationKeyFileName);
        Assert.False(File.Exists(planPath), "Refusing to overwrite an existing observation plan.");
        Assert.False(File.Exists(keyPath), "Refusing to reuse an observation HMAC key.");

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = CreateReader(scope.ServiceProvider);
        var key = RandomNumberGenerator.GetBytes(32);
        var samples = await ReadThreeStableSamplesAsync(reader, key);
        var oto = samples[Rt03Profiles.Oto];
        var moto = samples[Rt03Profiles.Moto];
        var otoEvidence = oto.Evidence[0];
        var motoEvidence = moto.Evidence[0];

        Assert.Equal(0, otoEvidence.WouldInsertRows);
        Assert.Equal(3, otoEvidence.WouldUpdateRows);
        Assert.Equal(0, otoEvidence.WouldReactivateRows);
        Assert.Equal(0, otoEvidence.TargetOnlyActiveRows);
        Assert.Equal(0, otoEvidence.ConflictRows);
        Assert.Equal(0, otoEvidence.ManualReviewRows);
        Assert.Equal(156, otoEvidence.SourceActiveRows);
        Assert.Equal(156, otoEvidence.TargetActiveRows);
        Assert.Equal(5, motoEvidence.SourceActiveRows);
        Assert.Equal(5, motoEvidence.TargetActiveRows);
        Assert.Empty(motoEvidence.Candidates);

        var unsupported = otoEvidence.Candidates
            .Where(candidate => candidate.CandidateType == "WOULD_UPDATE" &&
                                candidate.Classification == "STALE_IMPORTED_VALUE")
            .OrderBy(candidate => candidate.IdentityHmac, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(3, unsupported.Length);
        Assert.All(unsupported, candidate =>
        {
            Assert.Equal(3, candidate.FieldDifferences.Count);
            Assert.Equal(
                new[] { "AnhRelativePath", "ChatLuongAnh", "NgayThuNhanAnh" },
                candidate.FieldDifferences.Select(field => field.FieldCategory).ToArray());
            Assert.DoesNotContain(candidate.FieldDifferences,
                field => field.FieldCategory == "HoTen");
        });
        Assert.Empty(otoEvidence.Candidates.Where(candidate =>
            candidate.CandidateType == "WOULD_UPDATE" &&
            candidate.FieldDifferences.Count == 1 &&
            candidate.FieldDifferences[0].FieldCategory == "HoTen"));

        var plan = new Rt03CanaryPlan(
            $"RT03-TASK2-OBSERVATION-{Guid.NewGuid():N}",
            Rt03Modes.ObservationOnly,
            "PRODUCTION",
            otoEvidence.MappingFingerprint,
            otoEvidence.SourceSchemaFingerprint,
            motoEvidence.SourceSchemaFingerprint,
            otoEvidence.TargetSchemaFingerprint,
            otoEvidence.StageHash,
            motoEvidence.StageHash,
            otoEvidence.TargetComparisonHash,
            motoEvidence.TargetComparisonHash,
            null,
            null,
            "NOT_RUN_NO_CANARY_ELIGIBLE_CANDIDATE",
            Array.Empty<Rt03CanaryCandidate>());
        Rt03CanaryPlanValidator.Validate(plan);

        var secret = Convert.ToHexString(key);
        var exclusions = unsupported.Select(candidate =>
        {
            var row = oto.Raw.MappedSourceRows.Single(source =>
                string.Equals(candidate.IdentityHmac,
                    Rt01IdentityHmac(key, source.SourceProfileCode, source.SourceMaDK),
                    StringComparison.Ordinal));
            return new Rt03ObservationExclusion(
                Rt03IdentityHmac(secret, row),
                candidate.Classification,
                candidate.FieldDifferences.Select(field => field.FieldCategory).ToArray(),
                "UNSUPPORTED_MULTI_FIELD_PHOTO_DRIFT_NOT_HOTEN_CANARY");
        }).ToArray();
        var sealedAt = DateTime.UtcNow;
        var payload = new Rt03ObservationPayload(
            plan,
            sealedAt,
            sealedAt.AddMinutes(30),
            Rt03ProductionCatalog.RequiredDatabases.ToArray(),
            3,
            new Rt03StableProfileSnapshot(
                otoEvidence.SourceActiveRows, otoEvidence.TargetActiveRows,
                otoEvidence.TargetSoftDeletedRows, otoEvidence.NoChangeRows,
                otoEvidence.WouldInsertRows, otoEvidence.WouldUpdateRows,
                otoEvidence.WouldReactivateRows, otoEvidence.TargetOnlyActiveRows,
                otoEvidence.ConflictRows, otoEvidence.ManualReviewRows,
                otoEvidence.SourceKeySetHash, otoEvidence.TargetKeySetHash,
                otoEvidence.SourceOnlyHash),
            new Rt03StableProfileSnapshot(
                motoEvidence.SourceActiveRows, motoEvidence.TargetActiveRows,
                motoEvidence.TargetSoftDeletedRows, motoEvidence.NoChangeRows,
                motoEvidence.WouldInsertRows, motoEvidence.WouldUpdateRows,
                motoEvidence.WouldReactivateRows, motoEvidence.TargetOnlyActiveRows,
                motoEvidence.ConflictRows, motoEvidence.ManualReviewRows,
                motoEvidence.SourceKeySetHash, motoEvidence.TargetKeySetHash,
                motoEvidence.SourceOnlyHash),
            exclusions,
            SHA256Hex(key));
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var envelope = new Rt03ObservationEnvelope(payload, SHA256Hex(payloadBytes));
        var planBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        await File.WriteAllBytesAsync(keyPath, key);
        await File.WriteAllBytesAsync(planPath, planBytes);
        var firstRead = await File.ReadAllBytesAsync(planPath);
        var secondRead = await File.ReadAllBytesAsync(planPath);
        Assert.Equal(firstRead, secondRead);
        Assert.Equal(envelope.PayloadHash,
            SHA256Hex(JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, JsonOptions)));
        Assert.Equal(planBytes, firstRead);

        await WritePrivacySafeJsonAsync(Path.Combine(evidenceRoot, ObservationReportFileName), new
        {
            Evidence = "RT03_FRESH_SEALED_OBSERVATION_PLAN",
            SealedAtUtc = payload.SealedAtUtc,
            ExpiresAtUtc = payload.ExpiresAtUtc,
            SampleCountPerProfile = payload.SampleCountPerProfile,
            Oto = payload.Oto,
            Moto = payload.Moto,
            ExcludedCandidates = payload.Exclusions,
            EligibleCanaryCandidateCount = 0,
            PlanHash = plan.PlanHash,
            EnvelopeHash = envelope.PayloadHash,
            BusinessDataWrites = 0,
            CheckpointPublished = false,
            CtOrSnapshotChanged = false,
            AutoSyncTouched = false,
        });
        _output.WriteLine($"RT03_OBSERVATION_PLAN_SEALED plan={plan.PlanHash} eligible=0 excluded=3 writes=0");
    }

    [Fact]
    [Trait("Category", "ProductionMutationOptIn")]
    public async Task Execute_exact_sealed_oto_insert_canary_with_atomic_marker_and_exact_rollback()
    {
        if (!OptedIn("QLHV_RUN_RT03_EXECUTE_CANARY"))
        {
            _output.WriteLine("RT-03 canary execution was not requested; no production connection opened.");
            return;
        }

        Assert.True(OptedIn("QLHV_RT03_AUTOSYNC_DISABLED_VERIFIED"),
            "AUTOSYNC_MUTUAL_EXCLUSION_REJECTED: polling disable proof is required.");
        var evidenceRoot = RequireEvidenceRoot();
        var planPath = Path.Combine(evidenceRoot, PlanFileName);
        var keyPath = Path.Combine(evidenceRoot, KeyFileName);
        var firstPlanRead = await File.ReadAllBytesAsync(planPath);
        var secondPlanRead = await File.ReadAllBytesAsync(planPath);
        Assert.Equal(firstPlanRead, secondPlanRead);
        var key = await File.ReadAllBytesAsync(keyPath);
        var envelope = DeserializeEnvelope(firstPlanRead);
        ValidateEnvelope(envelope, key);
        var payload = envelope.Payload;
        var plan = payload.Plan;
        Assert.True(DateTime.UtcNow <= payload.ExpiresAtUtc, "SEALED_PLAN_EXPIRED");

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var autoSyncLease = await scope.ServiceProvider
            .GetRequiredService<IQlhvAutoSyncGlobalLock>().TryAcquireAsync();
        Assert.NotNull(autoSyncLease);

        var reader = CreateReader(scope.ServiceProvider);
        var sourceVersionBefore = await ReadOtoCtVersionAsync(scope.ServiceProvider);
        Assert.NotNull(sourceVersionBefore);
        var samples = await ReadThreeStableSamplesAsync(reader, key);
        var sourceVersionAfter = await ReadOtoCtVersionAsync(scope.ServiceProvider);
        Assert.Equal(sourceVersionBefore, sourceVersionAfter);
        var selectedRow = RevalidateSealedPlan(payload, samples, key);

        var target = await scope.ServiceProvider.GetRequiredService<IConnectionSettingsProvider>()
            .GetQlhvAppConnectionAsync();
        Assert.True(target.IsUsable && !string.IsNullOrWhiteSpace(target.ConnectionString));
        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync();

        long? insertedId = null;
        var cycleId = Guid.NewGuid();
        var committedAt = DateTime.UtcNow;
        var markerHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{cycleId:D}|{plan.PlanHash}|{plan.Candidates[0].IdentityHmac}|{selectedRow.V2RowHash}"));
        var dispositionHash = Rt03Hash.Sha256(
            $"INSERT|{plan.Candidates[0].IdentityHmac}|{selectedRow.V2RowHash}");
        var committed = false;
        var checkpointPublished = false;
        try
        {
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                             System.Data.IsolationLevel.Serializable))
            {
                await RequireTargetIdentityAndFeatureAsync(connection, transaction);
                await connection.ExecuteAsync(Rt03ProductionSql.AcquireProductionProfileLock,
                    new { ExactProfileLockName = "QLHV:RT03:CSDT_OTO" }, transaction);
                await connection.ExecuteAsync(Rt03ProductionSql.RejectActiveAutoSync,
                    transaction: transaction);
                var existing = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK) WHERE SourceMaDK = @SourceMaDK;",
                    new { selectedRow.SourceMaDK }, transaction);
                Assert.Equal(0, existing);
                var beforeRows = (await connection.QueryAsync<QlhvOwnedRow>(QlhvOwnedRowsSql,
                    new { SourceProfileCode = Rt03Profiles.Oto }, transaction)).ToArray();
                Assert.Equal(payload.PreexistingOtoQlhvOwnedHash,
                    QlhvOwnedPartitionHmac(key, beforeRows, null));
                var currentVersion = await ReadOtoCtVersionAsync(scope.ServiceProvider);
                Assert.Equal(sourceVersionAfter, currentVersion);

                insertedId = await connection.ExecuteScalarAsync<long>(
                    Rt03ProductionSql.InsertExactLearner + "\nSELECT CONVERT(bigint, SCOPE_IDENTITY());",
                    InsertParameters(selectedRow, committedAt), transaction);
                var inserted = await connection.QuerySingleAsync<InsertedCanaryRow>(
                    InsertedCanaryRowSql,
                    new { HocVienId = insertedId.Value }, transaction);
                AssertInsertedRow(inserted, selectedRow);

                await connection.ExecuteAsync(Rt03ProductionSql.InsertApplyMarker, new
                {
                    CycleId = cycleId,
                    SourceProfileCode = Rt03Profiles.Oto,
                    PlanHash = plan.PlanHash,
                    MarkerHash = markerHash,
                    DispositionHash = dispositionHash,
                    SourceDatabaseGuid = Guid.Parse(SourceGuid),
                    SourceChangeTrackingVersion = currentVersion!.Value,
                    InsertedRows = 1,
                    UpdatedRows = 0,
                    RetainedRows = 0,
                    PreservedQlhvOwnedHash = payload.PreexistingOtoQlhvOwnedHash,
                    CommittedAtUtc = committedAt,
                }, transaction);
                await transaction.CommitAsync();
                committed = true;
            }

            var post = await ReadThreeStableSamplesAsync(reader, key);
            VerifyPostCanary(payload, post, key);
            var afterRows = (await connection.QueryAsync<QlhvOwnedRow>(QlhvOwnedRowsSql,
                new { SourceProfileCode = Rt03Profiles.Oto })).ToArray();
            Assert.Equal(payload.PreexistingOtoQlhvOwnedHash,
                QlhvOwnedPartitionHmac(key, afterRows, insertedId));
            Assert.Equal(1, afterRows.Count(row => row.HocVienId == insertedId));
            Assert.Equal(0, await ActiveAutoSyncRowsAsync(connection));

            await using (var checkpointTransaction =
                         (SqlTransaction)await connection.BeginTransactionAsync(
                             System.Data.IsolationLevel.Serializable))
            {
                await connection.ExecuteAsync(Rt03ProductionSql.PublishCheckpointAfterVerifiedCommit,
                    new
                    {
                        CycleId = cycleId,
                        PlanHash = plan.PlanHash,
                        MarkerHash = markerHash,
                        SourceProfileCode = Rt03Profiles.Oto,
                        MappingFingerprint = plan.MappingFingerprint,
                        SourceDatabaseGuid = Guid.Parse(SourceGuid),
                        SourceChangeTrackingVersion = sourceVersionAfter!.Value,
                        PublishedAtUtc = DateTime.UtcNow,
                    }, checkpointTransaction);
                await checkpointTransaction.CommitAsync();
                checkpointPublished = true;
            }

            var proof = await connection.QuerySingleAsync<CompletionProof>(CompletionProofSql,
                new
                {
                    CycleId = cycleId,
                    PlanHash = plan.PlanHash,
                    MarkerHash = markerHash,
                    SourceProfileCode = Rt03Profiles.Oto,
                    MappingFingerprint = plan.MappingFingerprint,
                    SourceMaDK = selectedRow.SourceMaDK,
                });
            Assert.Equal(1, proof.MarkerRows);
            Assert.Equal(1, proof.CheckpointRows);
            Assert.Equal(1, proof.ExactLearnerRows);
            Assert.Equal(0, proof.DuplicateActiveRows);
            Assert.Equal(0, proof.ActiveAutoSyncRows);

            await WritePrivacySafeJsonAsync(Path.Combine(evidenceRoot, ExecutionReportFileName), new
            {
                Evidence = "RT03_PRODUCTION_OTO_CANARY_VERIFIED",
                PlanHash = plan.PlanHash,
                EnvelopeHash = envelope.PayloadHash,
                CycleId = cycleId,
                CandidateHmac = plan.Candidates[0].IdentityHmac,
                ExpectedMutation = "INSERT_EXACT_ONE_APP_HOCVIEN",
                InsertedRows = 1,
                UpdatedRows = 0,
                DeletedOrDeactivatedRows = 0,
                MarkerRows = proof.MarkerRows,
                CheckpointRows = proof.CheckpointRows,
                IdempotentReplayExactLearnerRows = proof.ExactLearnerRows,
                DuplicateActiveRows = proof.DuplicateActiveRows,
                ExistingAutoSyncActiveRows = proof.ActiveAutoSyncRows,
                QlhvOwnedHashPreserved = true,
                MotoMutationRows = 0,
                CompletedAtUtc = DateTime.UtcNow,
            });
            _output.WriteLine($"RT03_OTO_CANARY_VERIFIED plan={plan.PlanHash} inserted=1 duplicates=0 checkpoint=1");
        }
        catch
        {
            if (committed && insertedId.HasValue)
            {
                await ExactRollbackAsync(connection, payload, selectedRow, insertedId.Value,
                    cycleId, markerHash, checkpointPublished);
            }
            throw;
        }
    }

    private static ServiceProvider BuildProvider()
    {
        Assert.True(File.Exists(RuntimeConfig), $"Missing runtime config: {RuntimeConfig}");
        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(RuntimeConfig, optional: false, reloadOnChange: false);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplication();
        services.AddInfrastructureCore(configuration, @"D:\QLHV_APP");
        return services.BuildServiceProvider();
    }

    private static Rt01aOtoDriftEvidenceReader CreateReader(IServiceProvider services)
        => new(
            services.GetRequiredService<QlhvImportReadRepository>(),
            services.GetRequiredService<ICsdtConnectionProfileRepository>(),
            services.GetRequiredService<IConnectionPasswordProtector>(),
            services.GetRequiredService<IConnectionSettingsProvider>(),
            services.GetRequiredService<IOptions<SyncOptions>>());

    private static async Task<Dictionary<string, StableSamples>> ReadThreeStableSamplesAsync(
        Rt01aOtoDriftEvidenceReader reader,
        byte[] key)
    {
        var result = new Dictionary<string, StableSamples>(StringComparer.Ordinal);
        foreach (var route in Rt01ShadowRouteCatalog.Ordered)
        {
            var raw = new List<Rt01aRawProbe>();
            var evidence = new List<Rt01aProbeEvidence>();
            for (var index = 0; index < 3; index++)
            {
                var sample = await reader.ReadAsync(route);
                raw.Add(sample);
                evidence.Add(Rt01aDriftClassifier.Classify(sample, key, route.SourceProfileCode));
            }

            var first = evidence[0];
            Assert.All(evidence, item => AssertStable(first, item));
            result.Add(route.SourceProfileCode, new StableSamples(raw[^1], evidence.ToArray()));
        }
        return result;
    }

    private static void AssertStable(Rt01aProbeEvidence expected, Rt01aProbeEvidence actual)
    {
        Assert.Equal(expected.MappingFingerprint, actual.MappingFingerprint);
        Assert.Equal(expected.SourceSchemaFingerprint, actual.SourceSchemaFingerprint);
        Assert.Equal(expected.TargetSchemaFingerprint, actual.TargetSchemaFingerprint);
        Assert.Equal(expected.SourceActiveRows, actual.SourceActiveRows);
        Assert.Equal(expected.TargetActiveRows, actual.TargetActiveRows);
        Assert.Equal(expected.TargetSoftDeletedRows, actual.TargetSoftDeletedRows);
        Assert.Equal(expected.SourceKeySetHash, actual.SourceKeySetHash);
        Assert.Equal(expected.TargetKeySetHash, actual.TargetKeySetHash);
        Assert.Equal(expected.SourceOnlyHash, actual.SourceOnlyHash);
        Assert.Equal(expected.StageHash, actual.StageHash);
        Assert.Equal(expected.TargetComparisonHash, actual.TargetComparisonHash);
        Assert.Equal(0, actual.BusinessDataWrites);
        Assert.False(actual.ApplyCheckpointPublished);
        Assert.False(actual.ExistingAutoSyncTouched);
    }

    private static Rt01aCandidateEvidence[] SafeSourceOnlyCandidates(Rt01aProbeEvidence evidence)
        => evidence.Candidates
            .Where(candidate =>
                candidate.CandidateType == "WOULD_INSERT" &&
                candidate.Classification == "SOURCE_ONLY_NEW_ROW" &&
                candidate.SafeDisposition == "WOULD_INSERT_SAFE_AFTER_APPROVAL" &&
                !candidate.SqlCollationEqualCounterpart &&
                !candidate.AlternateImportedKeyEvidence &&
                !candidate.SoftDeletedCounterpart &&
                !candidate.OtherProfileCounterpart &&
                !candidate.ManualReviewRequired)
            .OrderBy(candidate => candidate.IdentityHmac, StringComparer.Ordinal)
            .ToArray();

    private static QlhvImportHocVienWriteModel RevalidateSealedPlan(
        Rt03SealedPayload payload,
        IReadOnlyDictionary<string, StableSamples> samples,
        byte[] key)
    {
        var plan = payload.Plan;
        var oto = samples[Rt03Profiles.Oto];
        var moto = samples[Rt03Profiles.Moto];
        var otoEvidence = oto.Evidence[0];
        var motoEvidence = moto.Evidence[0];
        var current = new Rt03RevalidationSnapshot(
            otoEvidence.MappingFingerprint,
            otoEvidence.SourceSchemaFingerprint,
            motoEvidence.SourceSchemaFingerprint,
            otoEvidence.TargetSchemaFingerprint,
            otoEvidence.StageHash,
            motoEvidence.StageHash,
            otoEvidence.TargetComparisonHash,
            motoEvidence.TargetComparisonHash,
            otoEvidence.ConflictRows != 0 || motoEvidence.ConflictRows != 0,
            false,
            otoEvidence.WouldReactivateRows != 0 || motoEvidence.WouldReactivateRows != 0);
        Rt03ExecutionGate.ValidateMutationCanary(
            new Rt03ProductionOptions
            {
                EnableRt03ProductionRealtime = true,
                EnableRt03ProductionShadow = true,
                EnableRt03ProductionWrites = true,
                EnableRt03ProductionCanary = true,
                EnableRt03ControlledCutover = false,
                EnableRt03ProductionDeletes = false,
                ValidationOnly = false,
            },
            plan,
            new Rt03AutoSyncExclusionSnapshot(false, false, false, 0, 0, 0, true, false),
            current,
            new Rt03CheckpointState(false, null, null, null, null, null));
        Assert.Equal(payload.Oto.SourceRows, otoEvidence.SourceActiveRows);
        Assert.Equal(payload.Oto.TargetActiveRows, otoEvidence.TargetActiveRows);
        Assert.Equal(payload.Oto.SourceOnlyRows, otoEvidence.WouldInsertRows);
        Assert.Equal(payload.Moto.SourceRows, motoEvidence.SourceActiveRows);
        Assert.Equal(payload.Moto.TargetActiveRows, motoEvidence.TargetActiveRows);
        Assert.Equal(payload.PreexistingOtoQlhvOwnedHash,
            QlhvOwnedPartitionHmac(key, oto.Raw.TargetRows, null));

        var secret = Convert.ToHexString(key);
        var safe = SafeSourceOnlyCandidates(otoEvidence);
        Assert.Equal(payload.ExpectedSafeSourceOnlyCount, safe.Length);
        var selected = oto.Raw.MappedSourceRows.Single(row =>
            string.Equals(Rt03IdentityHmac(secret, row), plan.Candidates[0].IdentityHmac,
                StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(payload.ExcludedCandidateHmac))
        {
            Assert.Contains(oto.Raw.MappedSourceRows, row =>
                string.Equals(Rt03IdentityHmac(secret, row), payload.ExcludedCandidateHmac,
                    StringComparison.Ordinal));
        }
        Assert.Equal(plan.Candidates[0].BeforeSourceOwnedHash, selected.V2RowHash);
        return selected;
    }

    private static void VerifyPostCanary(
        Rt03SealedPayload payload,
        IReadOnlyDictionary<string, StableSamples> post,
        byte[] key)
    {
        var oto = post[Rt03Profiles.Oto];
        var moto = post[Rt03Profiles.Moto];
        var otoEvidence = oto.Evidence[0];
        var motoEvidence = moto.Evidence[0];
        Assert.Equal(payload.Oto.SourceRows, otoEvidence.SourceActiveRows);
        Assert.Equal(payload.Oto.TargetActiveRows + 1, otoEvidence.TargetActiveRows);
        Assert.Equal(payload.Oto.NoChangeRows + 1, otoEvidence.NoChangeRows);
        Assert.Equal(payload.ExpectedSafeSourceOnlyCount - 1, otoEvidence.WouldInsertRows);
        Assert.Equal(0, otoEvidence.WouldUpdateRows);
        Assert.Equal(0, otoEvidence.WouldReactivateRows);
        Assert.Equal(0, otoEvidence.ConflictRows);
        Assert.Equal(0, otoEvidence.ManualReviewRows);
        var remainingSafe = SafeSourceOnlyCandidates(otoEvidence);
        Assert.Equal(payload.ExpectedSafeSourceOnlyCount - 1, remainingSafe.Length);
        if (!string.IsNullOrWhiteSpace(payload.ExcludedCandidateHmac))
        {
            Assert.Equal(payload.ExcludedCandidateHmac,
                Rt03IdentityHmac(Convert.ToHexString(key),
                    oto.Raw.MappedSourceRows.Single(row =>
                        remainingSafe.Any(candidate =>
                            string.Equals(candidate.IdentityHmac,
                                Rt01IdentityHmac(key, row.SourceProfileCode, row.SourceMaDK),
                                StringComparison.Ordinal)))));
        }
        Assert.Equal(payload.Moto.SourceRows, motoEvidence.SourceActiveRows);
        Assert.Equal(payload.Moto.TargetActiveRows, motoEvidence.TargetActiveRows);
        Assert.Empty(motoEvidence.Candidates);
    }

    private static async Task RequireTargetIdentityAndFeatureAsync(
        SqlConnection connection,
        SqlTransaction transaction)
    {
        var identityRows = await connection.QueryAsync(Rt03ProductionSql.RevalidateTargetIdentity,
            transaction: transaction);
        Assert.Single(identityRows);
        var state = await connection.QuerySingleAsync<FeatureState>(FeatureStateSql,
            transaction: transaction);
        Assert.True(state.EnableProductionRealtime);
        Assert.True(state.EnableProductionShadow);
        Assert.True(state.EnableProductionWrites);
        Assert.True(state.EnableProductionCanary);
        Assert.False(state.EnableControlledCutover);
        Assert.False(state.EnableProductionDeletes);
    }

    private static object InsertParameters(QlhvImportHocVienWriteModel row, DateTime committedAt)
        => new
        {
            row.SourceProfileCode,
            row.SourceMaDK,
            row.MaDK,
            row.MaKhoa,
            row.TenKhoa,
            row.MaHangDT,
            row.HangGPLXHoc,
            row.HoTen,
            row.NgaySinh,
            row.GioiTinh,
            row.SoCCCD,
            row.DiaChiThuongTru,
            row.SoGPLXDaCo,
            row.HangGPLXDaCo,
            row.NguoiNhanHoSo,
            row.AnhRelativePath,
            row.ChatLuongAnh,
            row.NgayThuNhanAnh,
            row.NguoiThuNhanAnh,
            row.SourceOfTruth,
            SourceRowHash = row.V2RowHash,
            CommittedAtUtc = committedAt,
        };

    private static void AssertInsertedRow(InsertedCanaryRow actual, QlhvImportHocVienWriteModel source)
    {
        Assert.Equal(source.SourceProfileCode, actual.SourceProfileCode);
        Assert.Equal(source.SourceMaDK, actual.SourceMaDK);
        Assert.Equal(source.V2RowHash, actual.V2RowHash);
        Assert.False(actual.IsDeleted);
        Assert.Equal("Rt03DirectRealtimeCanary", actual.CreatedBy);
        Assert.Null(actual.GhiChuNoiBo);
        Assert.False(actual.DaDoiChieuCccd);
        Assert.False(actual.DaInThe);
        Assert.False(actual.DaTaoXml);
        Assert.Null(actual.UpdatedBy);
        Assert.Null(actual.DeletedBy);
        Assert.Null(actual.DeleteReason);
    }

    private static async Task ExactRollbackAsync(
        SqlConnection connection,
        Rt03SealedPayload payload,
        QlhvImportHocVienWriteModel selected,
        long insertedId,
        Guid cycleId,
        byte[] markerHash,
        bool checkpointPublished)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        var downstreamReferences = await connection.ExecuteScalarAsync<int>(ReferencedFkCountSql,
            transaction: transaction);
        Assert.Equal(0, downstreamReferences);
        if (checkpointPublished)
        {
            var removedCheckpoint = await connection.ExecuteAsync(DeleteExactCheckpointSql, new
            {
                SourceProfileCode = Rt03Profiles.Oto,
                MappingFingerprint = payload.Plan.MappingFingerprint,
                CycleId = cycleId,
                PlanHash = payload.Plan.PlanHash,
                MarkerHash = markerHash,
            }, transaction);
            Assert.Equal(1, removedCheckpoint);
        }
        var removedMarker = await connection.ExecuteAsync(DeleteExactMarkerSql, new
        {
            CycleId = cycleId,
            PlanHash = payload.Plan.PlanHash,
            MarkerHash = markerHash,
        }, transaction);
        Assert.Equal(1, removedMarker);
        var removedLearner = await connection.ExecuteAsync(Rt03ProductionSql.RollbackExactCanaryInsert,
            new
            {
                DownstreamReferenceCount = 0,
                ExactInsertedHocVienId = insertedId,
                selected.SourceProfileCode,
                selected.SourceMaDK,
                ExpectedCurrentSourceOwnedHash = selected.V2RowHash,
            }, transaction);
        await transaction.CommitAsync();
    }

    private static async Task<int> ActiveAutoSyncRowsAsync(SqlConnection connection)
        => await connection.ExecuteScalarAsync<int>(ActiveAutoSyncRowsSql);

    private static async Task<long?> ReadOtoCtVersionAsync(IServiceProvider services)
    {
        var profiles = services.GetRequiredService<ICsdtConnectionProfileRepository>();
        var protector = services.GetRequiredService<IConnectionPasswordProtector>();
        var options = services.GetRequiredService<IOptions<SyncOptions>>().Value;
        var profile = await profiles.GetByCodeAsync(Rt03Profiles.Oto);
        Assert.NotNull(profile);
        Assert.True(profile.IsActive);
        Assert.Equal("CSDL_OTO", profile.DatabaseName);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.ServerName,
            InitialCatalog = profile.DatabaseName,
            ConnectTimeout = Math.Clamp(options.TimeoutSeconds, 5, 30),
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
        };
        if (string.Equals(profile.AuthMode, "SqlLogin", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(profile.IsPasswordConfigured);
            Assert.NotNull(profile.PasswordCipherText);
            Assert.True(protector.IsAvailable);
            builder.IntegratedSecurity = false;
            builder.UserID = profile.UserName;
            builder.Password = protector.Unprotect(profile.PasswordCipherText!);
        }
        else
        {
            builder.IntegratedSecurity = true;
        }
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var identity = await connection.QuerySingleAsync<SourceIdentity>(SourceIdentitySql);
        Assert.Equal("CSDLTTTC", identity.ServerIdentity);
        Assert.Equal("CSDL_OTO", identity.DatabaseName);
        Assert.Equal(9, identity.DatabaseId);
        Assert.Equal(Guid.Parse(SourceGuid), identity.DatabaseGuid);
        return identity.ChangeTrackingVersion;
    }

    private static void ValidateEnvelope(Rt03SealedEnvelope envelope, byte[] key)
    {
        Assert.Equal(32, key.Length);
        Assert.Equal(envelope.PayloadHash,
            SHA256Hex(JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, JsonOptions)));
        Assert.Equal(envelope.Payload.KeySha256, SHA256Hex(key));
        Rt03CanaryPlanValidator.Validate(envelope.Payload.Plan);
        Assert.Single(envelope.Payload.Plan.Candidates);
        Assert.Equal(Rt03CandidateKind.Insert, envelope.Payload.Plan.Candidates[0].Kind);
        Assert.Equal(1, envelope.Payload.ExpectedSafeSourceOnlyCount);
        Assert.Null(envelope.Payload.ExcludedCandidateHmac);
        Assert.Null(envelope.Payload.ExcludedCandidateReason);
        Assert.Equal(3, envelope.Payload.SampleCountPerProfile);
        Assert.Equal(Rt03ProductionCatalog.RequiredDatabases, envelope.Payload.DatabaseIdentities);
    }

    private static Rt03SealedEnvelope DeserializeEnvelope(byte[] bytes)
        => JsonSerializer.Deserialize<Rt03SealedEnvelope>(bytes, JsonOptions)
           ?? throw new InvalidOperationException("SEALED_PLAN_DESERIALIZATION_FAILED");

    private static bool OptedIn(string name)
        => string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private static string RequireFreshEvidenceRoot()
    {
        var root = RequireEvidenceRoot();
        Assert.True(Directory.Exists(root));
        return root;
    }

    private static string RequireEvidenceRoot()
    {
        var root = Environment.GetEnvironmentVariable(EvidenceRootVariable);
        Assert.False(string.IsNullOrWhiteSpace(root), $"Missing {EvidenceRootVariable}.");
        return Path.GetFullPath(root!);
    }

    private static async Task WritePrivacySafeJsonAsync(string path, object value)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions),
            new UTF8Encoding(false));

    private static string Rt03IdentityHmac(string secret, QlhvImportHocVienWriteModel row)
        => Rt03Hash.DiagnosticHmac(secret, "candidate-identity",
            $"{row.SourceProfileCode}|{row.SourceMaDK.Trim()}");

    private static string Rt01IdentityHmac(byte[] key, string? profile, string? identity)
    {
        using var hmac = new HMACSHA256(key);
        var canonical = $"{Rt01aProofContract.HmacVersion}|identity|" +
                        $"{(profile ?? string.Empty).Trim().ToUpperInvariant()}|" +
                        $"{(identity ?? string.Empty).Trim()}";
        return $"{Rt01aProofContract.HmacVersion}:" +
               Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                   .ToLowerInvariant();
    }

    private static string QlhvOwnedPartitionHmac(
        byte[] key,
        IEnumerable<Rt01aTargetHocVienRow> rows,
        long? excludedId)
        => QlhvOwnedPartitionHmac(key, rows.Select(row => new QlhvOwnedRow
        {
            HocVienId = row.HocVienId,
            SourceProfileCode = row.SourceProfileCode,
            SourceMaDK = row.SourceMaDK,
            IsDeleted = row.IsDeleted,
            GhiChuNoiBo = row.GhiChuNoiBo,
            DaDoiChieuCccd = row.DaDoiChieuCccd,
            DaInThe = row.DaInThe,
            DaTaoXml = row.DaTaoXml,
            CreatedBy = row.CreatedBy,
            UpdatedBy = row.UpdatedBy,
            DeletedBy = row.DeletedBy,
            DeleteReason = row.DeleteReason,
        }), excludedId);

    private static string QlhvOwnedPartitionHmac(
        byte[] key,
        IEnumerable<QlhvOwnedRow> rows,
        long? excludedId)
    {
        var canonical = string.Join("\n", rows
            .Where(row => row.HocVienId != excludedId)
            .OrderBy(row => row.HocVienId)
            .Select(row => string.Join("|",
                row.HocVienId.ToString(CultureInfo.InvariantCulture),
                Safe(row.SourceProfileCode),
                Safe(row.SourceMaDK),
                row.IsDeleted ? "1" : "0",
                Safe(row.GhiChuNoiBo),
                row.DaDoiChieuCccd ? "1" : "0",
                row.DaInThe ? "1" : "0",
                row.DaTaoXml ? "1" : "0",
                Safe(row.CreatedBy), Safe(row.UpdatedBy), Safe(row.DeletedBy),
                Safe(row.DeleteReason))));
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(
            $"RT03-QLHV-OWNED-v1|{canonical}"))).ToLowerInvariant();
    }

    private static string Safe(string? value) => value ?? "<NULL>";
    private static string SHA256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record StableSamples(Rt01aRawProbe Raw, Rt01aProbeEvidence[] Evidence);
    private sealed record Rt03StableProfileSnapshot(
        int SourceRows, int TargetActiveRows, int TargetSoftDeletedRows, int NoChangeRows,
        int SourceOnlyRows, int UpdateRows, int ReactivateRows, int TargetOnlyRows,
        int ConflictRows, int ManualReviewRows, string SourceKeySetHash,
        string TargetKeySetHash, string SourceOnlyHash);
    private sealed record Rt03SealedPayload(
        Rt03CanaryPlan Plan,
        DateTime SealedAtUtc,
        DateTime ExpiresAtUtc,
        Rt03ExpectedDatabase[] DatabaseIdentities,
        int SampleCountPerProfile,
        Rt03StableProfileSnapshot Oto,
        Rt03StableProfileSnapshot Moto,
        string PreexistingOtoQlhvOwnedHash,
        int ExpectedSafeSourceOnlyCount,
        string? ExcludedCandidateHmac,
        string? ExcludedCandidateReason,
        string KeySha256);
    private sealed record Rt03SealedEnvelope(Rt03SealedPayload Payload, string PayloadHash);
    private sealed record Rt03ObservationExclusion(
        string IdentityHmac,
        string Classification,
        string[] ExactDifferentFields,
        string ExclusionReason);
    private sealed record Rt03ObservationPayload(
        Rt03CanaryPlan Plan,
        DateTime SealedAtUtc,
        DateTime ExpiresAtUtc,
        Rt03ExpectedDatabase[] DatabaseIdentities,
        int SampleCountPerProfile,
        Rt03StableProfileSnapshot Oto,
        Rt03StableProfileSnapshot Moto,
        Rt03ObservationExclusion[] Exclusions,
        string KeySha256);
    private sealed record Rt03ObservationEnvelope(
        Rt03ObservationPayload Payload,
        string PayloadHash);

    private class QlhvOwnedRow
    {
        public long HocVienId { get; init; }
        public string? SourceProfileCode { get; init; }
        public string? SourceMaDK { get; init; }
        public bool IsDeleted { get; init; }
        public string? GhiChuNoiBo { get; init; }
        public bool DaDoiChieuCccd { get; init; }
        public bool DaInThe { get; init; }
        public bool DaTaoXml { get; init; }
        public string? CreatedBy { get; init; }
        public string? UpdatedBy { get; init; }
        public string? DeletedBy { get; init; }
        public string? DeleteReason { get; init; }
    }

    private sealed class InsertedCanaryRow : QlhvOwnedRow
    {
        public string? V2RowHash { get; init; }
    }

    private sealed class FeatureState
    {
        public bool EnableProductionRealtime { get; init; }
        public bool EnableProductionShadow { get; init; }
        public bool EnableProductionWrites { get; init; }
        public bool EnableProductionCanary { get; init; }
        public bool EnableControlledCutover { get; init; }
        public bool EnableProductionDeletes { get; init; }
    }

    private sealed class SourceIdentity
    {
        public string ServerIdentity { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public int DatabaseId { get; init; }
        public Guid DatabaseGuid { get; init; }
        public long? ChangeTrackingVersion { get; init; }
    }

    private sealed class CompletionProof
    {
        public int MarkerRows { get; init; }
        public int CheckpointRows { get; init; }
        public int ExactLearnerRows { get; init; }
        public int DuplicateActiveRows { get; init; }
        public int ActiveAutoSyncRows { get; init; }
    }

    private const string FeatureStateSql = """
        SELECT EnableProductionRealtime, EnableProductionShadow,
               EnableProductionWrites, EnableProductionCanary,
               EnableControlledCutover, EnableProductionDeletes
        FROM dbo.App_QlhvDirectRealtimeFeatureState WITH (UPDLOCK,HOLDLOCK)
        WHERE FeatureStateId = 1;
        """;

    private const string QlhvOwnedRowsSql = """
        SELECT HocVienId, SourceProfileCode, SourceMaDK, IsDeleted,
               GhiChuNoiBo, DaDoiChieuCCCD AS DaDoiChieuCccd,
               DaInThe, DaTaoXML AS DaTaoXml, CreatedBy, UpdatedBy,
               DeletedBy, DeleteReason
        FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
        WHERE SourceProfileCode = @SourceProfileCode
        ORDER BY HocVienId;
        """;

    private const string InsertedCanaryRowSql = """
        SELECT HocVienId, SourceProfileCode, SourceMaDK, V2RowHash, IsDeleted,
               GhiChuNoiBo, DaDoiChieuCCCD AS DaDoiChieuCccd,
               DaInThe, DaTaoXML AS DaTaoXml, CreatedBy, UpdatedBy,
               DeletedBy, DeleteReason
        FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
        WHERE HocVienId = @HocVienId;
        """;

    private const string SourceIdentitySql = """
        SELECT CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
               DB_NAME() AS DatabaseName, DB_ID() AS DatabaseId,
               databaseIdentity.database_guid AS DatabaseGuid,
               CHANGE_TRACKING_CURRENT_VERSION() AS ChangeTrackingVersion
        FROM sys.database_recovery_status AS databaseIdentity
        WHERE databaseIdentity.database_id = DB_ID();
        """;

    private const string ActiveAutoSyncRowsSql = """
        SELECT
          (SELECT COUNT(1) FROM dbo.App_QlhvAutoSyncRun
           WHERE Status IN (N'QUEUED',N'RUNNING') OR ActiveSlot = 1)
          +
          (SELECT COUNT(1) FROM dbo.App_QlhvSyncOperationHistory
           WHERE Status IN (N'QUEUED',N'RUNNING'));
        """;

    private const string CompletionProofSql = """
        SELECT
          (SELECT COUNT(1) FROM dbo.App_QlhvDirectRealtimeApplyMarker
           WHERE CycleId=@CycleId AND PlanHash=@PlanHash AND MarkerHash=@MarkerHash) AS MarkerRows,
          (SELECT COUNT(1) FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
           WHERE SourceProfileCode=@SourceProfileCode
             AND Mode=N'DIRECT_REALTIME_APPLY'
             AND MappingFingerprint=@MappingFingerprint
             AND EnvironmentId=N'PRODUCTION'
             AND CycleId=@CycleId AND PlanHash=@PlanHash AND MarkerHash=@MarkerHash) AS CheckpointRows,
          (SELECT COUNT(1) FROM dbo.App_HocVien
           WHERE SourceProfileCode=@SourceProfileCode AND SourceMaDK=@SourceMaDK
             AND IsDeleted=0) AS ExactLearnerRows,
          (SELECT COUNT(1) FROM
             (SELECT SourceProfileCode, SourceMaDK FROM dbo.App_HocVien
              WHERE IsDeleted=0 GROUP BY SourceProfileCode, SourceMaDK HAVING COUNT(1)>1) d)
             AS DuplicateActiveRows,
          ((SELECT COUNT(1) FROM dbo.App_QlhvAutoSyncRun
            WHERE Status IN (N'QUEUED',N'RUNNING') OR ActiveSlot=1)
           +
           (SELECT COUNT(1) FROM dbo.App_QlhvSyncOperationHistory
            WHERE Status IN (N'QUEUED',N'RUNNING'))) AS ActiveAutoSyncRows;
        """;

    private const string ReferencedFkCountSql = """
        SELECT COUNT(1)
        FROM sys.foreign_keys
        WHERE referenced_object_id = OBJECT_ID(N'dbo.App_HocVien', N'U');
        """;

    private const string DeleteExactCheckpointSql = """
        DELETE FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
        WHERE SourceProfileCode=@SourceProfileCode
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND MappingFingerprint=@MappingFingerprint
          AND EnvironmentId=N'PRODUCTION'
          AND CycleId=@CycleId AND PlanHash=@PlanHash AND MarkerHash=@MarkerHash;
        """;

    private const string DeleteExactMarkerSql = """
        DELETE FROM dbo.App_QlhvDirectRealtimeApplyMarker
        WHERE CycleId=@CycleId AND PlanHash=@PlanHash AND MarkerHash=@MarkerHash;
        """;
}

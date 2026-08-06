using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QLHV.Application;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;
using QLHV.Infrastructure;
using QLHV.Infrastructure.Sync;
using QLHV.Infrastructure.Sync.Rt01;
using Xunit.Abstractions;

namespace QLHV.Tests.Sync;

public sealed class Rt01ShadowModeTests
{
    private readonly ITestOutputHelper _output;

    public Rt01ShadowModeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Route_catalog_is_live_to_qlhv_only_and_excludes_bak_and_v1()
    {
        Assert.Collection(
            Rt01ShadowRouteCatalog.Ordered,
            oto =>
            {
                Assert.Equal("OTO", oto.SourceType);
                Assert.Equal(CsdtConnectionProfileCodes.CsdtOto, oto.SourceProfileCode);
                Assert.Equal("CSDL_OTO", oto.SourceDatabaseName);
                Assert.Equal("66029", oto.MaCsdt);
            },
            moto =>
            {
                Assert.Equal("MOTO", moto.SourceType);
                Assert.Equal(CsdtConnectionProfileCodes.CsdtMoto, moto.SourceProfileCode);
                Assert.Equal("CSDL_MOTO", moto.SourceDatabaseName);
                Assert.Equal("66030", moto.MaCsdt);
            });

        Assert.All(
            Rt01ShadowRouteCatalog.Ordered,
            route =>
            {
                Assert.DoesNotContain("BAK", route.SourceDatabaseName, StringComparison.Ordinal);
                Assert.DoesNotContain("_V1", route.SourceDatabaseName, StringComparison.Ordinal);
                Assert.DoesNotContain("BAK", route.SourceProfileCode, StringComparison.Ordinal);
                Assert.DoesNotContain("_V1", route.SourceProfileCode, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Planner_reports_exact_shadow_drift_without_business_writes_or_checkpoint()
    {
        var route = Rt01ShadowRouteCatalog.Oto;
        var sourceA = Source("A", "An");
        var sourceB = Source("B", "Binh moi");
        var sourceC = Source("C", "Cuong");
        var sourceE = Source("E", "Em");
        var sourceRows = new[] { sourceA, sourceB, sourceC, sourceE };
        var targetRows = new[]
        {
            Target(sourceA, route, isDeleted: false),
            new QlhvFullSyncTargetRow("B", "old-hash", IsDeleted: false),
            Target(sourceC, route, isDeleted: true),
            new QlhvFullSyncTargetRow("D", "target-only", IsDeleted: false),
        };
        var readStarted = new DateTime(2026, 7, 27, 2, 0, 0, DateTimeKind.Utc);
        var snapshots = Snapshots(route, sourceRows, targetRows, readStarted);

        var observation = Rt01ShadowPlanner.Build(
            route,
            snapshots,
            previousSourceFingerprint: null,
            detectionLatencyBudgetSeconds: 2,
            observedAtUtc: readStarted.AddMilliseconds(25));

        Assert.Equal(Rt01ShadowModes.Shadow, observation.Mode);
        Assert.True(observation.IsReadOnly);
        Assert.Equal(Rt01ShadowStatuses.DriftDetected, observation.Status);
        Assert.Equal(4, observation.SourceRows);
        Assert.Equal(3, observation.TargetActiveRows);
        Assert.Equal(1, observation.TargetSoftDeletedRows);
        Assert.Equal(1, observation.PlannedInsertRows);
        Assert.Equal(1, observation.PlannedUpdateRows);
        Assert.Equal(1, observation.PlannedReactivateRows);
        Assert.Equal(1, observation.TargetOnlyActiveRows);
        Assert.Equal(1, observation.PlannedNoChangeRows);
        Assert.True(observation.HasDrift);
        Assert.Equal(0, observation.BusinessDataWrites);
        Assert.False(observation.ApplyCheckpointPublished);
        Assert.False(observation.ExistingAutoSyncTouched);
        Assert.Empty(observation.Blockers);
    }

    [Fact]
    public void Planner_reports_matched_for_converged_live_and_qlhv_partition()
    {
        var route = Rt01ShadowRouteCatalog.Moto;
        var source = Source("M-1", "Moto");
        var snapshots = Snapshots(
            route,
            [source],
            [Target(source, route, isDeleted: false)],
            DateTime.UtcNow);

        var observation = Rt01ShadowPlanner.Build(
            route,
            snapshots,
            previousSourceFingerprint: null,
            detectionLatencyBudgetSeconds: 2,
            observedAtUtc: DateTime.UtcNow);

        Assert.Equal(Rt01ShadowStatuses.Matched, observation.Status);
        Assert.False(observation.HasDrift);
        Assert.Equal(1, observation.PlannedNoChangeRows);
        Assert.Equal(0, observation.BusinessDataWrites);
        Assert.False(observation.ApplyCheckpointPublished);
    }

    [Fact]
    public void Planner_blocks_wrong_database_and_duplicate_live_identity()
    {
        var route = Rt01ShadowRouteCatalog.Oto;
        var snapshots = new Rt01ShadowSnapshots(
            new QlhvImportSourceSnapshot
            {
                SourceDatabaseName = "CSDL_OTO_BAK",
                HocVienRows = [Source("A", "An"), Source("a", "An duplicate")],
            },
            new QlhvImportTargetSnapshot(),
            DateTime.UtcNow,
            DateTime.UtcNow);

        var observation = Rt01ShadowPlanner.Build(
            route,
            snapshots,
            previousSourceFingerprint: null,
            detectionLatencyBudgetSeconds: 2,
            observedAtUtc: DateTime.UtcNow);

        Assert.Equal(Rt01ShadowStatuses.Blocked, observation.Status);
        Assert.Contains(
            observation.Blockers,
            blocker => blocker.Contains("bat buoc phai la CSDL_OTO", StringComparison.Ordinal));
        Assert.Contains(
            observation.Blockers,
            blocker => blocker.Contains("SourceMaDK bi trung", StringComparison.Ordinal));
        Assert.Equal(1, observation.DuplicateSourceIdentityGroups);
        Assert.Equal(0, observation.BusinessDataWrites);
    }

    [Fact]
    public async Task Worker_detects_live_fingerprint_change_on_next_two_second_pass()
    {
        var probe = new SequenceProbe();
        var sink = new InMemoryRt01ShadowObservationSink();
        var worker = new Rt01ShadowWorker(
            probe,
            sink,
            Options.Create(new Rt01ShadowOptions
            {
                Enabled = true,
                Mode = Rt01ShadowModes.Shadow,
                PollIntervalSeconds = 2,
            }));

        var first = await worker.RunPassAsync();
        var second = await worker.RunPassAsync();

        Assert.All(first, observation =>
            Assert.False(observation.SourceChangedSincePreviousObservation));
        Assert.All(second, observation =>
        {
            Assert.True(observation.SourceChangedSincePreviousObservation);
            Assert.Equal(2, observation.DetectionLatencyBudgetSeconds);
            Assert.Equal(0, observation.BusinessDataWrites);
            Assert.False(observation.ApplyCheckpointPublished);
            Assert.False(observation.ExistingAutoSyncTouched);
        });
        Assert.Equal(["OTO", "MOTO", "OTO", "MOTO"], probe.Calls);
        Assert.Equal(2, sink.GetLatest().Count);
    }

    [Fact]
    public async Task Worker_default_is_disabled_and_is_not_registered_by_application_root()
    {
        var defaults = new Rt01ShadowOptions();
        Assert.False(defaults.Enabled);
        Assert.Equal(Rt01ShadowModes.Shadow, defaults.Mode);

        var probe = new SequenceProbe();
        var worker = new Rt01ShadowWorker(
            probe,
            new InMemoryRt01ShadowObservationSink(),
            Options.Create(defaults));
        await worker.RunAsync(CancellationToken.None);
        Assert.Empty(probe.Calls);

        var services = new ServiceCollection();
        services.AddApplication();
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ImplementationType == typeof(Rt01ShadowWorker));
    }

    [Fact]
    public void Options_and_worker_contract_have_no_apply_or_writer_capability()
    {
        var validator = new Rt01ShadowOptionsValidator();
        Assert.True(validator.Validate(null, new Rt01ShadowOptions()).Succeeded);
        Assert.False(validator.Validate(null, new Rt01ShadowOptions
        {
            Mode = "APPLY",
            PollIntervalSeconds = 2,
        }).Succeeded);
        Assert.False(validator.Validate(null, new Rt01ShadowOptions
        {
            Mode = Rt01ShadowModes.Shadow,
            PollIntervalSeconds = 6,
        }).Succeeded);

        var constructorDependencies = typeof(Rt01ShadowWorker)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.DoesNotContain(typeof(IQlhvImportWriteRepository), constructorDependencies);
        Assert.DoesNotContain(
            constructorDependencies,
            type => type.FullName?.Contains(
                ".Sync.Realtime.",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Production_shadow_compare_is_select_only_and_opt_in()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("QLHV_RUN_RT01_SHADOW_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            _output.WriteLine(
                "RT-01 production shadow probe not requested; no production connection opened.");
            return;
        }

        var runtimeConfig =
            Environment.GetEnvironmentVariable("QLHV_RT01_RUNTIME_CONFIG") ??
            @"D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json";
        Assert.True(File.Exists(runtimeConfig), $"Missing runtime config: {runtimeConfig}");

        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(runtimeConfig, optional: false, reloadOnChange: false);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplication();
        services.AddInfrastructureCore(configuration, @"D:\QLHV_APP");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var importReads = scope.ServiceProvider.GetRequiredService<QlhvImportReadRepository>();
        var probe = new Rt01ShadowProbe(
            new Rt01QlhvShadowSnapshotReader(importReads));

        var results = new List<Rt01ShadowObservation>();
        foreach (var route in Rt01ShadowRouteCatalog.Ordered)
        {
            results.Add(await probe.ObserveAsync(
                route,
                previousSourceFingerprint: null,
                detectionLatencyBudgetSeconds: 2));
        }

        var verificationResults = new List<Rt01ShadowObservation>();
        foreach (var (route, firstResult) in Rt01ShadowRouteCatalog.Ordered.Zip(results))
        {
            verificationResults.Add(await probe.ObserveAsync(
                route,
                firstResult.SourceFingerprint,
                detectionLatencyBudgetSeconds: 2));
        }

        Assert.Collection(
            results,
            oto =>
            {
                Assert.Equal("OTO", oto.SourceType);
                Assert.Equal("CSDL_OTO", oto.SourceDatabaseName);
                Assert.Equal(152, oto.SourceRows);
                Assert.Equal(152, oto.TargetActiveRows);
                Assert.Equal(0, oto.DuplicateSourceIdentityGroups);
                Assert.Equal(0, oto.DuplicateTargetIdentityGroups);
            },
            moto =>
            {
                Assert.Equal("MOTO", moto.SourceType);
                Assert.Equal("CSDL_MOTO", moto.SourceDatabaseName);
                Assert.Equal(5, moto.SourceRows);
                Assert.Equal(5, moto.TargetActiveRows);
                Assert.Equal(0, moto.DuplicateSourceIdentityGroups);
                Assert.Equal(0, moto.DuplicateTargetIdentityGroups);
            });
        Assert.All(results, result =>
        {
            Assert.Equal(0, result.BusinessDataWrites);
            Assert.False(result.ApplyCheckpointPublished);
            Assert.False(result.ExistingAutoSyncTouched);
        });
        Assert.Equal(results.Count, verificationResults.Count);
        for (var index = 0; index < results.Count; index++)
        {
            var first = results[index];
            var verification = verificationResults[index];
            Assert.False(verification.SourceChangedSincePreviousObservation);
            Assert.Equal(first.SourceFingerprint, verification.SourceFingerprint);
            Assert.Equal(first.TargetFingerprint, verification.TargetFingerprint);
            Assert.Equal(first.Status, verification.Status);
            Assert.Equal(first.PlannedInsertRows, verification.PlannedInsertRows);
            Assert.Equal(first.PlannedUpdateRows, verification.PlannedUpdateRows);
            Assert.Equal(first.PlannedReactivateRows, verification.PlannedReactivateRows);
            Assert.Equal(first.TargetOnlyActiveRows, verification.TargetOnlyActiveRows);
            Assert.Equal(0, verification.BusinessDataWrites);
            Assert.False(verification.ApplyCheckpointPublished);
            Assert.False(verification.ExistingAutoSyncTouched);
        }

        _output.WriteLine(JsonSerializer.Serialize(new
        {
            FirstRead = results,
            VerificationRead = verificationResults,
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    [Fact]
    public async Task Production_rt01a_oto_drift_proof_is_select_only_and_opt_in()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("QLHV_RUN_RT01A_OTO_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            _output.WriteLine(
                "RT-01A production OTO proof not requested; no production connection opened.");
            return;
        }

        var runtimeConfig =
            Environment.GetEnvironmentVariable("QLHV_RT01_RUNTIME_CONFIG") ??
            @"D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json";
        Assert.True(File.Exists(runtimeConfig), $"Missing runtime config: {runtimeConfig}");

        var configuration = new ConfigurationManager();
        configuration.AddJsonFile(runtimeConfig, optional: false, reloadOnChange: false);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplication();
        services.AddInfrastructureCore(configuration, @"D:\QLHV_APP");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var reader = new Rt01aOtoDriftEvidenceReader(
            scope.ServiceProvider.GetRequiredService<QlhvImportReadRepository>(),
            scope.ServiceProvider.GetRequiredService<ICsdtConnectionProfileRepository>(),
            scope.ServiceProvider.GetRequiredService<IConnectionPasswordProtector>(),
            scope.ServiceProvider.GetRequiredService<
                QLHV.Application.Sync.Connections.IConnectionSettingsProvider>(),
            scope.ServiceProvider.GetRequiredService<IOptions<SyncOptions>>());
        var hmacKey = RandomNumberGenerator.GetBytes(32);
        var evidence = new List<Rt01aProbeEvidence>();
        for (var probeNumber = 0; probeNumber < 3; probeNumber++)
        {
            evidence.Add(Rt01aDriftClassifier.Classify(
                await reader.ReadAsync(),
                hmacKey));
        }

        var first = evidence[0];
        Assert.Equal(152, first.SourceActiveRows);
        Assert.Equal(152, first.TargetActiveRows);
        Assert.Equal(151, first.IntersectionRows);
        Assert.Equal(150, first.NoChangeRows);
        Assert.Equal(1, first.WouldInsertRows);
        Assert.Equal(1, first.WouldUpdateRows);
        Assert.Equal(1, first.TargetOnlyActiveRows);
        var insert = Assert.Single(
            first.Candidates,
            candidate => candidate.CandidateType == "WOULD_INSERT");
        Assert.Equal("SOURCE_ONLY_NEW_ROW", insert.Classification);
        Assert.Equal("WOULD_INSERT_SAFE_AFTER_APPROVAL", insert.SafeDisposition);
        Assert.False(insert.SoftDeletedCounterpart);
        Assert.False(insert.OtherProfileCounterpart);
        Assert.False(insert.SqlCollationEqualCounterpart);
        var update = Assert.Single(
            first.Candidates,
            candidate => candidate.CandidateType == "WOULD_UPDATE");
        Assert.Equal("STALE_IMPORTED_VALUE", update.Classification);
        Assert.Equal(
            "WOULD_UPDATE_SOURCE_OWNED_FIELDS_AFTER_APPROVAL",
            update.SafeDisposition);
        Assert.Equal("HoTen", Assert.Single(update.FieldDifferences).FieldCategory);
        var targetOnly = Assert.Single(
            first.Candidates,
            candidate => candidate.CandidateType == "TARGET_ONLY_ACTIVE");
        Assert.Equal("SOURCE_ROW_REMOVED", targetOnly.Classification);
        Assert.Equal("MANUAL_REVIEW_REQUIRED", targetOnly.SafeDisposition);
        Assert.True(targetOnly.ExistingAutoSyncAttribution);
        Assert.False(targetOnly.RawSourceRepresentationExists);
        Assert.All(evidence, item =>
        {
            Assert.Equal(first.MappingFingerprint, item.MappingFingerprint);
            Assert.Equal(first.SourceSchemaFingerprint, item.SourceSchemaFingerprint);
            Assert.Equal(first.TargetSchemaFingerprint, item.TargetSchemaFingerprint);
            Assert.Equal(first.SourceKeySetHash, item.SourceKeySetHash);
            Assert.Equal(first.TargetKeySetHash, item.TargetKeySetHash);
            Assert.Equal(first.IntersectionHash, item.IntersectionHash);
            Assert.Equal(first.SourceOnlyHash, item.SourceOnlyHash);
            Assert.Equal(first.TargetOnlyHash, item.TargetOnlyHash);
            Assert.Equal(first.UpdateCandidateHash, item.UpdateCandidateHash);
            Assert.Equal(first.StageHash, item.StageHash);
            Assert.Equal(first.TargetComparisonHash, item.TargetComparisonHash);
            Assert.Equal(0, item.BusinessDataWrites);
            Assert.False(item.ApplyCheckpointPublished);
            Assert.False(item.ExistingAutoSyncTouched);
        });

        _output.WriteLine(JsonSerializer.Serialize(new
        {
            SampleStatus = Rt01aProofContract.ConsistencyLevel,
            ProbeCount = evidence.Count,
            Probes = evidence,
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static Rt01ShadowSnapshots Snapshots(
        Rt01ShadowRoute route,
        IReadOnlyList<V2HocVienSourceRow> sourceRows,
        IReadOnlyList<QlhvFullSyncTargetRow> targetRows,
        DateTime startedAtUtc)
        => new(
            new QlhvImportSourceSnapshot
            {
                SourceDatabaseName = route.SourceDatabaseName,
                GeneratedAtUtc = startedAtUtc,
                HocVienRows = sourceRows,
            },
            new QlhvImportTargetSnapshot
            {
                HocVienRows = targetRows,
            },
            startedAtUtc,
            startedAtUtc.AddMilliseconds(10));

    private static V2HocVienSourceRow Source(string maDk, string hoTen)
        => new()
        {
            MaDK = maDk,
            MaKhoaHoc = "K01",
            TenKH = "Khoa 01",
            HangDaoTao = "B2",
            TenHangDT = "B2",
            HoVaTen = hoTen,
            NgaySinh = new DateTime(2000, 1, 1),
            GioiTinh = "M",
            SoCMT = "012345678901",
            NoiTT = "Dia chi",
        };

    private static QlhvFullSyncTargetRow Target(
        V2HocVienSourceRow source,
        Rt01ShadowRoute route,
        bool isDeleted)
    {
        var mapped = QlhvImportHocVienMapper.MapAndValidate(
            source,
            new HocVienSourceIdentityContext(route.SourceProfileCode, "V2"));
        Assert.NotNull(mapped.Model);
        return new QlhvFullSyncTargetRow(
            source.MaDK,
            mapped.Model!.V2RowHash,
            isDeleted);
    }

    private sealed class SequenceProbe : IRt01ShadowProbe
    {
        private readonly Dictionary<string, int> _callsBySource =
            new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];

        public Task<Rt01ShadowObservation> ObserveAsync(
            Rt01ShadowRoute route,
            string? previousSourceFingerprint,
            int detectionLatencyBudgetSeconds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(route.SourceType);
            _callsBySource.TryGetValue(route.SourceType, out var priorCalls);
            var nextCalls = priorCalls + 1;
            _callsBySource[route.SourceType] = nextCalls;
            var fingerprint = $"{route.SourceType}-{nextCalls}";
            return Task.FromResult(new Rt01ShadowObservation
            {
                SourceType = route.SourceType,
                SourceProfileCode = route.SourceProfileCode,
                SourceDatabaseName = route.SourceDatabaseName,
                MaCsdt = route.MaCsdt,
                Status = Rt01ShadowStatuses.DriftDetected,
                ObservedAtUtc = DateTime.UtcNow,
                ReadStartedAtUtc = DateTime.UtcNow,
                ReadCompletedAtUtc = DateTime.UtcNow,
                DetectionLatencyBudgetSeconds = detectionLatencyBudgetSeconds,
                SourceFingerprint = fingerprint,
                SourceChangedSincePreviousObservation =
                    !string.IsNullOrWhiteSpace(previousSourceFingerprint) &&
                    !string.Equals(
                        previousSourceFingerprint,
                        fingerprint,
                        StringComparison.Ordinal),
                BusinessDataWrites = 0,
                ApplyCheckpointPublished = false,
                ExistingAutoSyncTouched = false,
            });
        }
    }
}

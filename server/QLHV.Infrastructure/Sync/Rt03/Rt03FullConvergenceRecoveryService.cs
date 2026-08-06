using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;
using QLHV.Application.Sync.Rt03;
using QLHV.Application.Sync.VehicleRealtime;
using QLHV.Infrastructure.Sync.Rt01;

namespace QLHV.Infrastructure.Sync.Rt03;

internal sealed class Rt03FullConvergenceRecoveryService :
    IRt03FullConvergenceRecoveryService
{
    private readonly ITimeAuthorityService _timeAuthority;
    private readonly IRt03FullConvergenceStateStore _state;
    private readonly IQlhvDirectRealtimeGlobalLock _globalLock;
    private readonly IQlhvSourceOperationLock _sourceLock;
    private readonly Rt03FullConvergenceLockFactory _recoveryLocks;
    private readonly Rt03FullConvergenceSourceBarrierFactory _barriers;
    private readonly IQlhvFreshnessSourceRepository _sources;
    private readonly IQlhvImportReadRepository _targetReader;
    private readonly IQlhvImportWriteRepository _targetWriter;
    private readonly IVehicleFullConvergenceTargetStore _vehicleTarget;
    private readonly Rt01aOtoDriftEvidenceReader _reviewRawReader;
    private readonly Rt03ReviewedRetainedEvidenceReader _reviewedRetained;
    private readonly IConnectionSettingsProvider _connections;

    public Rt03FullConvergenceRecoveryService(
        ITimeAuthorityService timeAuthority,
        IRt03FullConvergenceStateStore state,
        IQlhvDirectRealtimeGlobalLock globalLock,
        IQlhvSourceOperationLock sourceLock,
        Rt03FullConvergenceLockFactory recoveryLocks,
        Rt03FullConvergenceSourceBarrierFactory barriers,
        IQlhvFreshnessSourceRepository sources,
        IQlhvImportReadRepository targetReader,
        IQlhvImportWriteRepository targetWriter,
        IVehicleFullConvergenceTargetStore vehicleTarget,
        Rt01aOtoDriftEvidenceReader reviewRawReader,
        Rt03ReviewedRetainedEvidenceReader reviewedRetained,
        IConnectionSettingsProvider connections)
    {
        _timeAuthority = timeAuthority;
        _state = state;
        _globalLock = globalLock;
        _sourceLock = sourceLock;
        _recoveryLocks = recoveryLocks;
        _barriers = barriers;
        _sources = sources;
        _targetReader = targetReader;
        _targetWriter = targetWriter;
        _vehicleTarget = vehicleTarget;
        _reviewRawReader = reviewRawReader;
        _reviewedRetained = reviewedRetained;
        _connections = connections;
    }

    public async Task<Rt03FullConvergenceRecoveryResult> ExecuteAsync(
        Rt03FullConvergenceRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var sourceType =
            QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(
                request.SourceProfileCode);
        var operationSource =
            QlhvOperationSourceCatalog.GetRequired(sourceType);
        var route =
            VehicleRealtimeRouteCatalog.GetRequired(request.SourceProfileCode);

        var time = await _timeAuthority.GetWriteAuthorizationAsync(cancellationToken);
        if (!TimeAuthorityPolicy.IsMutationAllowed(time))
        {
            throw new Rt03SafetyException(
                Rt03Errors.TimeAuthorityBlocked,
                "RT03 recovery requires an available SQL SYSUTCDATETIME() probe.");
        }

        var preflight = await _state.ReadPreflightAsync(
            request.SourceProfileCode,
            cancellationToken);
        RequirePreflight(request, route, preflight);

        await using var globalLease =
            await _globalLock.TryAcquireAsync(cancellationToken) ??
            throw new Rt03SafetyException(
                Rt03Errors.AutoSyncActive,
                "Global Auto Sync/recovery lock is unavailable.");
        await using var recoveryLease =
            await _recoveryLocks.TryAcquireProfileAsync(
                request.SourceProfileCode,
                cancellationToken) ??
            throw new Rt03SafetyException(
                Rt03Errors.AutoSyncActive,
                "Profile recovery lock is unavailable.");
        await using var sourceLease =
            await _sourceLock.TryAcquireAsync(
                operationSource,
                cancellationToken) ??
            throw new Rt03SafetyException(
                Rt03Errors.AutoSyncActive,
                "Profile full-sync/multiple-writer lock is unavailable.");
        if (!await recoveryLease.TryAcquireDomainsAsync(cancellationToken))
        {
            throw new Rt03SafetyException(
                Rt03Errors.AutoSyncActive,
                "Ordered recovery domain lock is unavailable.");
        }

        var lockedPreflight = await _state.ReadPreflightAsync(
            request.SourceProfileCode,
            cancellationToken);
        RequirePreflight(request, route, lockedPreflight);
        await using var barrier = await _barriers.AcquireAsync(
            request.SourceProfileCode,
            lockedPreflight.CheckpointVersion,
            cancellationToken);
        if (barrier.SourceDatabaseGuid != route.ExpectedProductionDatabaseGuid)
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "Source barrier database identity changed.");
        }

        var mappingFingerprint = ComputeMappingFingerprint(
            request.ExpectedArtifactSha256);
        await _state.BeginOrResumeAsync(
            request,
            barrier.SourceDatabaseGuid,
            barrier.AnchorVersion,
            mappingFingerprint,
            barrier.SourceSchemaFingerprint,
            cancellationToken);

        var sourceRequest = new QlhvImportRequest
        {
            SourceProfileCode = request.SourceProfileCode,
            MaCSDT = operationSource.MaCsdt,
        };
        var source = await _sources.ReadLiveSourceAsync(
            sourceRequest,
            cancellationToken);
        var vehicleSource = await barrier.ReadVehiclesAsync(cancellationToken);
        var payload = BuildPayload(source, sourceRequest);
        var reviewedBefore = await EvaluateReviewedRetainedAsync(
            barrier,
            lockedPreflight.CheckpointVersion,
            Rt03ReviewedRetainedContext.FullConvergence,
            cancellationToken);
        RequireSafeReviewedRetained(reviewedBefore, expectedIdentities: null);
        payload = payload with
        {
            ReviewedRetainedSourceBusinessIdentityHashes =
                reviewedBefore.SafeSourceBusinessIdentityHashes,
        };
        var domainResults = new List<Rt03RecoveryDomainResult>(5);

        domainResults.Add(await ExecuteMappedDomainAsync(
            request.RecoveryId,
            request.SourceProfileCode,
            payload,
            QlhvImportDomains.KhoaHoc,
            Rt03FullConvergenceDomains.Course,
            sequenceOrder: 1,
            cancellationToken));
        domainResults.Add(await ExecuteMappedDomainAsync(
            request.RecoveryId,
            request.SourceProfileCode,
            payload,
            QlhvImportDomains.GiaoVien,
            Rt03FullConvergenceDomains.Teacher,
            sequenceOrder: 2,
            cancellationToken));

        var vehicleInventory = await _vehicleTarget.ReadInventoryAsync(
            cancellationToken);
        var vehiclePlan = VehicleFullConvergencePlanner.Build(
            request.RecoveryId,
            route,
            barrier.SourceDatabaseGuid,
            barrier.AnchorVersion,
            barrier.SourceSchemaFingerprint,
            vehicleSource,
            vehicleInventory);
        var vehicleWrite = await _vehicleTarget.CommitAsync(
            vehiclePlan,
            vehicleInventory,
            cancellationToken);
        var vehicleResult = new Rt03RecoveryDomainResult(
            Rt03FullConvergenceDomains.Vehicle,
            SequenceOrder: 3,
            vehicleWrite.SourceRows,
            vehicleWrite.InsertedRows,
            vehicleWrite.UpdatedRows,
            vehicleWrite.InactiveRows,
            vehicleWrite.MissingRows,
            vehicleWrite.ManualReviewRows,
            vehicleWrite.NoChangeRows,
            Hash(
                "VEHICLE",
                vehicleWrite.PlanToken,
                vehicleWrite.SourceRows.ToString(CultureInfo.InvariantCulture)));
        await _state.RecordDomainAsync(
            request.RecoveryId,
            vehicleResult,
            cancellationToken);
        domainResults.Add(vehicleResult);

        domainResults.Add(await ExecuteMappedDomainAsync(
            request.RecoveryId,
            request.SourceProfileCode,
            payload,
            QlhvImportDomains.HocVien,
            Rt03FullConvergenceDomains.Learner,
            sequenceOrder: 4,
            cancellationToken));
        domainResults.Add(await ExecuteMappedDomainAsync(
            request.RecoveryId,
            request.SourceProfileCode,
            payload,
            QlhvImportDomains.Relation,
            Rt03FullConvergenceDomains.Relation,
            sequenceOrder: 5,
            cancellationToken));

        var reviewedAfter = await EvaluateReviewedRetainedAsync(
            barrier,
            lockedPreflight.CheckpointVersion,
            Rt03ReviewedRetainedContext.RecoveryVerification,
            cancellationToken);
        RequireSafeReviewedRetained(
            reviewedAfter,
            reviewedBefore.SafeSourceBusinessIdentityHashes);
        await VerifyMappedTargetsAsync(
            sourceRequest,
            payload,
            vehicleSource,
            vehiclePlan,
            reviewedAfter.SafeSourceBusinessIdentityHashes,
            cancellationToken);
        var verificationHash = Hash(
            new[]
            {
                "RT03_V5_VERIFIED",
                request.RecoveryId.ToString("D"),
                request.SourceProfileCode,
                barrier.SourceDatabaseGuid.ToString("D"),
                barrier.AnchorVersion.ToString(CultureInfo.InvariantCulture),
                barrier.SourceSchemaFingerprint,
                mappingFingerprint,
            }
            .Concat(domainResults.Select(result => result.VerificationHash))
            .ToArray());
        await _state.MarkVerifiedAsync(
            request.RecoveryId,
            cancellationToken);
        await _state.FinalizeAsync(
            request.RecoveryId,
            verificationHash,
            cancellationToken);

        var currentVersion = await barrier.ReadCurrentVersionAsync(
            cancellationToken);
        return new(
            request.RecoveryId,
            request.SourceProfileCode,
            request.ExpectedCheckpoint,
            barrier.AnchorVersion,
            Rt03RecoverySessionStatuses.Completed,
            domainResults,
            verificationHash,
            Math.Max(0, currentVersion - barrier.AnchorVersion));
    }

    private async Task<Rt03RecoveryDomainResult> ExecuteMappedDomainAsync(
        Guid recoveryId,
        string sourceProfileCode,
        QlhvImportFullSyncPayload payload,
        string repositoryDomain,
        string recoveryDomain,
        int sequenceOrder,
        CancellationToken cancellationToken)
    {
        var write = await _targetWriter.FullSyncRecoveryDomainAsync(
            sourceProfileCode,
            payload,
            repositoryDomain,
            cancellationToken);
        if (!write.Committed)
        {
            throw new Rt03SafetyException(
                Rt03Errors.OwnershipProofRejected,
                $"{recoveryDomain} did not commit a verified domain result.");
        }

        var result = new Rt03RecoveryDomainResult(
            recoveryDomain,
            sequenceOrder,
            write.Counts.SourceRows,
            write.Counts.Inserted,
            write.Counts.Updated + write.Counts.Reactivated,
            InactiveRows: 0,
            MissingRows: write.Counts.SoftDeleted,
            ManualReviewRows: 0,
            NoChangeRows: write.Counts.Skipped,
            VerificationHash: Hash(
                recoveryDomain,
                write.Status,
                write.Counts.SourceRows.ToString(CultureInfo.InvariantCulture),
                write.Counts.Inserted.ToString(CultureInfo.InvariantCulture),
                write.Counts.Updated.ToString(CultureInfo.InvariantCulture),
                write.Counts.Reactivated.ToString(CultureInfo.InvariantCulture),
                write.Counts.SoftDeleted.ToString(CultureInfo.InvariantCulture),
                write.Counts.Skipped.ToString(CultureInfo.InvariantCulture)));
        await _state.RecordDomainAsync(
            recoveryId,
            result,
            cancellationToken);
        return result;
    }

    private async Task VerifyMappedTargetsAsync(
        QlhvImportRequest request,
        QlhvImportFullSyncPayload payload,
        IReadOnlyCollection<VehicleSourceRow> vehicleSource,
        VehicleFullConvergencePlan expectedVehiclePlan,
        IReadOnlySet<string> reviewedRetainedBusinessIdentities,
        CancellationToken cancellationToken)
    {
        var target = await _targetReader.ReadTargetAsync(
            request,
            payload.HocVienRows.Select(row => row.SourceMaDK).ToArray(),
            cancellationToken);
        if (target.DuplicateHocVienTargetIdentityRows != 0 ||
            target.DuplicateKhoaHocTargetIdentityRows != 0 ||
            target.DuplicateGiaoVienTargetIdentityRows != 0 ||
            target.DuplicateRelationTargetIdentityRows != 0)
        {
            throw new Rt03SafetyException(
                Rt03Errors.OwnershipProofRejected,
                "Target exact identity verification found duplicates.");
        }

        VerifyEntity(
            payload.KhoaHocRows.Select(row => (row.SourceMaKhoaHoc, row.SourceHash)),
            target.KhoaHocRows,
            Rt03FullConvergenceDomains.Course);
        VerifyEntity(
            payload.GiaoVienRows.Select(row => (row.SourceMaGV, row.SourceHash)),
            target.GiaoVienRows,
            Rt03FullConvergenceDomains.Teacher);
        VerifyEntity(
            payload.RelationRows.Select(row =>
                (row.SourceMaLichLV.ToString(CultureInfo.InvariantCulture), row.SourceHash)),
            target.RelationRows,
            Rt03FullConvergenceDomains.Relation);
        VerifyLearners(
            request.SourceProfileCode,
            payload.HocVienRows,
            target.HocVienRows,
            reviewedRetainedBusinessIdentities);

        var vehicleInventory = await _vehicleTarget.ReadInventoryAsync(
            cancellationToken);
        var route = VehicleRealtimeRouteCatalog.GetRequired(
            request.SourceProfileCode);
        var convergedVehiclePlan = VehicleFullConvergencePlanner.Build(
            expectedVehiclePlan.RecoveryId,
            route,
            expectedVehiclePlan.SourceDatabaseGuid,
            expectedVehiclePlan.AnchorVersion,
            expectedVehiclePlan.SourceSchemaFingerprint,
            vehicleSource,
            vehicleInventory);
        if (convergedVehiclePlan.Rows.Any(row =>
                row.Action is not VehicleRealtimeActions.NoChange and
                    not VehicleRealtimeActions.ManualReview))
        {
            throw new Rt03SafetyException(
                Rt03Errors.OwnershipProofRejected,
                "VEHICLE exact identity/hash/lifecycle verification failed.");
        }
    }

    private static void VerifyEntity(
        IEnumerable<(string Key, string Hash)> sourceRows,
        IReadOnlyCollection<QlhvEntityFullSyncTargetRow> targetRows,
        string domain)
    {
        var source = sourceRows.ToDictionary(
            row => row.Key.Trim(),
            row => row.Hash,
            StringComparer.OrdinalIgnoreCase);
        var target = targetRows.ToDictionary(
            row => row.SourceKey.Trim(),
            StringComparer.OrdinalIgnoreCase);
        if (target.Values.Any(row =>
                !row.IsDeleted && !source.ContainsKey(row.SourceKey.Trim())) ||
            source.Any(row =>
                !target.TryGetValue(row.Key, out var match) ||
                match.IsDeleted ||
                !string.Equals(
                    row.Value,
                    match.SourceHash,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new Rt03SafetyException(
                Rt03Errors.OwnershipProofRejected,
                $"{domain} exact identity/hash verification failed.");
        }
    }

    private static void VerifyLearners(
        string sourceProfileCode,
        IReadOnlyCollection<QlhvImportHocVienWriteModel> sourceRows,
        IReadOnlyCollection<QlhvFullSyncTargetRow> targetRows,
        IReadOnlySet<string> reviewedRetainedBusinessIdentities)
    {
        var source = sourceRows.ToDictionary(
            row => row.SourceMaDK.Trim(),
            row => row.V2RowHash,
            StringComparer.OrdinalIgnoreCase);
        var target = targetRows.ToDictionary(
            row => row.SourceMaDK.Trim(),
            StringComparer.OrdinalIgnoreCase);
        if (target.Values.Any(row =>
                !row.IsDeleted && !source.ContainsKey(row.SourceMaDK.Trim())) ||
            source.Any(row =>
                !target.TryGetValue(row.Key, out var match) ||
                match.IsDeleted ||
                (!string.Equals(
                     row.Value,
                     match.V2RowHash,
                     StringComparison.OrdinalIgnoreCase) &&
                 !reviewedRetainedBusinessIdentities.Contains(
                     Rt03ReviewedRetainedFingerprints.SourceBusinessIdentity(
                         sourceProfileCode,
                         row.Key)))))
        {
            throw new Rt03SafetyException(
                Rt03Errors.OwnershipProofRejected,
                "LEARNER exact identity/hash verification failed.");
        }
    }

    private async Task<Rt03ReviewedRetainedSummary> EvaluateReviewedRetainedAsync(
        Rt03FullConvergenceSourceBarrier barrier,
        long checkpointVersion,
        Rt03ReviewedRetainedContext context,
        CancellationToken cancellationToken)
    {
        var route = Rt01ShadowRouteCatalog.Ordered.Single(item =>
            string.Equals(
                item.SourceProfileCode,
                barrier.Route.SourceProfileCode,
                StringComparison.Ordinal));
        var raw = await _reviewRawReader.ReadAsync(route, cancellationToken);
        var drift = Rt01aDriftClassifier.Classify(
            raw,
            RandomNumberGenerator.GetBytes(32),
            route.SourceProfileCode);
        var learnerAudits = barrier.Audits.Where(item =>
                item.TableName is "NguoiLX" or "NguoiLX_HoSo" &&
                item.ChangeTrackingEnabled &&
                item.MinimumValidVersion.HasValue)
            .ToArray();
        if (learnerAudits.Length != 2)
        {
            throw new Rt03SafetyException(
                Rt03Errors.ChangeTrackingWindowRejected,
                "Reviewed-retained recovery cannot classify the learner CT window.");
        }

        var targetSettings = await _connections.GetQlhvAppConnectionAsync(
            cancellationToken);
        if (!targetSettings.IsUsable ||
            string.IsNullOrWhiteSpace(targetSettings.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "QLHV_APP connection is unavailable for reviewed-retained recovery.");
        }

        await using var targetConnection = new SqlConnection(
            targetSettings.ConnectionString);
        await targetConnection.OpenAsync(cancellationToken);
        return await _reviewedRetained.EvaluateAsync(
            targetConnection,
            barrier.SourceConnectionString,
            route.SourceProfileCode,
            raw,
            drift,
            checkpointVersion,
            barrier.AnchorVersion,
            learnerAudits.Min(item => item.MinimumValidVersion!.Value),
            context,
            cancellationToken);
    }

    private static void RequireSafeReviewedRetained(
        Rt03ReviewedRetainedSummary summary,
        IReadOnlySet<string>? expectedIdentities)
    {
        if (summary.ActiveReviewCount != summary.ReviewedRetainedCount ||
            summary.StaleReviewCount != 0 ||
            summary.SafeSourceBusinessIdentityHashes.Count !=
                summary.ReviewedRetainedCount ||
            (expectedIdentities is not null &&
             !expectedIdentities.SetEquals(
                 summary.SafeSourceBusinessIdentityHashes)))
        {
            var reason = summary.Evaluations.FirstOrDefault(item =>
                             !item.IsSafeSteadyState)?.ReasonCode ??
                         Rt03ReviewedRetainedReasonCodes.ReviewStale;
            throw new Rt03SafetyException(
                Rt03Errors.TargetDrift,
                $"Reviewed-retained recovery verification rejected: {reason}.");
        }
    }

    private static QlhvImportFullSyncPayload BuildPayload(
        QlhvImportSourceSnapshot source,
        QlhvImportRequest request)
    {
        var blockers = source.KhoaHocBlockers
            .Concat(source.GiaoVienBlockers)
            .Concat(source.RelationBlockers)
            .ToList();
        var learnerIdentity = new HocVienSourceIdentityContext(
            request.SourceProfileCode,
            "V2");
        var learners = new List<QlhvImportHocVienWriteModel>(source.HocVienRows.Count);
        foreach (var row in source.HocVienRows)
        {
            var mapped = QlhvImportHocVienMapper.MapAndValidate(
                row,
                learnerIdentity);
            blockers.AddRange(mapped.Blockers);
            if (!mapped.ShouldSkip && mapped.Model is not null)
            {
                learners.Add(mapped.Model);
            }
        }

        var courses = source.KhoaHocSourceRows
            .Select(row => QlhvImportCourseTeacherMapper.MapKhoaHoc(
                row,
                request.SourceProfileCode))
            .ToArray();
        var teachers = source.GiaoVienRows
            .Select(row => QlhvImportCourseTeacherMapper.MapGiaoVien(
                row,
                request.SourceProfileCode))
            .ToArray();
        var relations = source.KhoaHocGiaoVienRows
            .Select(row => QlhvImportCourseTeacherMapper.MapRelation(
                row,
                request.SourceProfileCode))
            .ToArray();
        blockers.AddRange(courses.SelectMany(row => row.Blockers));
        blockers.AddRange(teachers.SelectMany(row => row.Blockers));
        blockers.AddRange(relations.SelectMany(row => row.Blockers));
        if (blockers.Count != 0 ||
            courses.Any(row => row.Model is null) ||
            teachers.Any(row => row.Model is null) ||
            relations.Any(row => row.Model is null) ||
            learners.Count != source.HocVienRows.Count)
        {
            throw new Rt03SafetyException(
                Rt03Errors.OwnershipProofRejected,
                "Live source snapshot contains an unclassified mapping.");
        }

        var courseModels = courses.Select(row => row.Model!).ToArray();
        var teacherModels = teachers.Select(row => row.Model!).ToArray();
        var relationModels = relations.Select(row => row.Model!).ToArray();
        RequireUnique(
            courseModels.Select(row => row.SourceMaKhoaHoc),
            Rt03FullConvergenceDomains.Course);
        RequireUnique(
            teacherModels.Select(row => row.SourceMaGV),
            Rt03FullConvergenceDomains.Teacher);
        RequireUnique(
            relationModels.Select(row =>
                row.SourceMaLichLV.ToString(CultureInfo.InvariantCulture)),
            Rt03FullConvergenceDomains.Relation);
        RequireUnique(
            learners.Select(row => row.SourceMaDK),
            Rt03FullConvergenceDomains.Learner);

        var courseKeys = courseModels
            .Select(row => row.SourceMaKhoaHoc)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teacherKeys = teacherModels
            .Select(row => row.SourceMaGV)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (learners.Any(row =>
                string.IsNullOrWhiteSpace(row.MaKhoa) ||
                !courseKeys.Contains(row.MaKhoa.Trim())) ||
            relationModels.Any(row =>
                !courseKeys.Contains(row.SourceMaKhoaHoc) ||
                !teacherKeys.Contains(row.SourceMaGV)))
        {
            throw new Rt03SafetyException(
                Rt03Errors.LearnerCourseNotConvergent,
                "Course/teacher dependencies are not present in the same snapshot.");
        }

        return new(
            courseModels,
            teacherModels,
            relationModels,
            learners,
            BackupSnapshotToken: string.Empty,
            QlhvImportDomains.Ordered);
    }

    private static void RequireUnique(
        IEnumerable<string> values,
        string domain)
    {
        if (values
            .Select(value => value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Skip(1).Any()))
        {
            throw new Rt03SafetyException(
                Rt03Errors.OwnershipProofRejected,
                $"{domain} source exact identity is duplicated.");
        }
    }

    private static void RequirePreflight(
        Rt03FullConvergenceRecoveryRequest request,
        VehicleRealtimeRoute route,
        Rt03RecoveryPreflightState preflight)
    {
        if (!preflight.RecoverySchemaReady ||
            !preflight.AutoSyncInactive ||
            !preflight.FullSyncInactive ||
            preflight.CheckpointVersion != request.ExpectedCheckpoint ||
            preflight.SourceDatabaseGuid != route.ExpectedProductionDatabaseGuid)
        {
            throw new Rt03SafetyException(
                Rt03Errors.CheckpointConflict,
                "Fresh recovery schema/writer/checkpoint/source identity preflight failed.");
        }
    }

    private static void ValidateRequest(
        Rt03FullConvergenceRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(
            request.SourceProfileCode);
        if (request.RecoveryId == Guid.Empty ||
            request.ExpectedCheckpoint < 0 ||
            request.ExpectedArtifactSha256.Length != 64 ||
            request.ExpectedArtifactSha256.Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Recovery id/checkpoint/artifact SHA-256 is invalid.",
                nameof(request));
        }
    }

    private static string ComputeMappingFingerprint(string artifactSha256)
        => Hash(
            "RT03_V5_FULL_CONVERGENCE_MAPPING",
            artifactSha256.ToLowerInvariant(),
            VehicleSourceMapper.ComputeMappingFingerprint(),
            "COURSE_SOURCE_OWNED_V1",
            "TEACHER_TRAINING_SOURCE_OWNED_DOSSIER_QLHV_OWNED_V1",
            "LEARNER_SOURCE_OWNED_ASSIGNMENT_QLHV_OWNED_V1",
            "RELATION_SOURCE_OWNED_V1");

    private static string Hash(params string[] values)
    {
        var canonical = string.Join(
            "|",
            values.Select(value => $"{value.Length}:{value}"));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

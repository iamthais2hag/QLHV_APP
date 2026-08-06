using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.Rt01;
using QLHV.Application.Sync.Rt03;
using QLHV.Infrastructure.Sync.Rt01;

namespace QLHV.Infrastructure.Sync.Rt03;

/// <summary>
/// Builds one immutable full-comparison plan from the live profile, revalidates
/// its CT window and target state, converges all supported course events before
/// dependent learner work, and publishes the checkpoint only from a committed
/// verified marker. Deletes and unsupported drift remain fail closed.
/// </summary>
public sealed class Rt03ProductionRealtimeCycleProcessor :
    IRt03ProductionRealtimeCycleProcessor
{
    private readonly Rt01aOtoDriftEvidenceReader _reader;
    private readonly Rt03ReviewedRetainedEvidenceReader _reviewedRetained;
    private readonly ICsdtConnectionProfileRepository _profiles;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _syncOptions;
    private readonly QlhvAutoSyncOptions _autoSyncOptions;
    private readonly Rt03ProductionOptions _options;

    public Rt03ProductionRealtimeCycleProcessor(
        Rt01aOtoDriftEvidenceReader reader,
        Rt03ReviewedRetainedEvidenceReader reviewedRetained,
        ICsdtConnectionProfileRepository profiles,
        IConnectionPasswordProtector passwordProtector,
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> syncOptions,
        IOptions<QlhvAutoSyncOptions> autoSyncOptions,
        IOptions<Rt03ProductionOptions> options)
    {
        _reader = reader;
        _reviewedRetained = reviewedRetained;
        _profiles = profiles;
        _passwordProtector = passwordProtector;
        _connections = connections;
        _syncOptions = syncOptions.Value;
        _autoSyncOptions = autoSyncOptions.Value;
        _options = options.Value;
    }

    public async Task<Rt03ProductionCycleResult> ProcessAsync(
        string sourceProfileCode,
        string workerInstanceId,
        CancellationToken cancellationToken = default)
    {
        var route = Rt01ShadowRouteCatalog.Ordered.SingleOrDefault(item =>
            string.Equals(item.SourceProfileCode, sourceProfileCode, StringComparison.Ordinal))
            ?? throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "RT-03 source profile is outside the OTO/MOTO live allowlist.");
        if (_autoSyncOptions.Enabled || _autoSyncOptions.RunOnServerStartup)
        {
            throw new Rt03SafetyException(
                Rt03Errors.AutoSyncActive,
                "Existing Auto Sync configuration is not paused in the worker host.");
        }

        var sourceConnectionString = await ResolveSourceAsync(route, cancellationToken);
        var capabilityBefore = await ReadCapabilityAsync(
            sourceConnectionString, route, cancellationToken);
        var key = RandomNumberGenerator.GetBytes(32);
        var raw = await _reader.ReadAsync(route, cancellationToken);
        var evidence = Rt01aDriftClassifier.Classify(raw, key, route.SourceProfileCode);
        var capabilityAfter = await ReadCapabilityAsync(
            sourceConnectionString, route, cancellationToken);
        if (!capabilityBefore.Equals(capabilityAfter))
        {
            throw new Rt03SafetyException(
                Rt03Errors.SourceChangedDuringPlan,
                "Source identity or Change Tracking window changed during plan construction.");
        }

        ValidateFingerprints(route, raw, evidence);
        ValidateCapability(route, capabilityAfter);
        var targetSettings = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!targetSettings.IsUsable || string.IsNullOrWhiteSpace(targetSettings.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "QLHV_APP connection is unavailable for RT-03.");
        }

        await using var targetConnection = new SqlConnection(targetSettings.ConnectionString);
        await targetConnection.OpenAsync(cancellationToken);
        var feature = await targetConnection.QuerySingleAsync<Rt03ProductionFeatureState>(
            new CommandDefinition(
                Rt03ProductionRuntimeStateStore.FeatureStateSql,
                commandTimeout: _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken));
        ValidateFeature(feature);
        var profile = (await targetConnection.QueryAsync<Rt03ProductionProfileState>(
            new CommandDefinition(
                Rt03ProductionRuntimeStateStore.ProfileStateSql,
                commandTimeout: _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken)))
            .Single(item => item.SourceProfileCode == sourceProfileCode);
        ValidateProfile(route, profile, raw, evidence);

        var checkpoint = await ReadCheckpointAsync(
            targetConnection, route.SourceProfileCode, evidence.MappingFingerprint,
            cancellationToken);
        ValidateCheckpoint(checkpoint, capabilityAfter, evidence.MappingFingerprint);

        var changeBatch = await ReadNextChangeBatchAsync(
            sourceConnectionString,
            checkpoint.SourceVersion,
            capabilityAfter.CurrentVersion,
            cancellationToken);

        var recovered = await TryRecoverCheckpointAsync(
            targetConnection, checkpoint, capabilityAfter.CurrentVersion,
            cancellationToken);
        if (recovered is not null)
        {
            return new Rt03ProductionCycleResult(
                route.SourceProfileCode,
                Rt03CycleStatuses.RecoveredCheckpoint,
                recovered.CycleId,
                checkpoint.SourceVersion,
                recovered.SourceVersion,
                recovered.InsertedRows,
                recovered.UpdatedRows,
                recovered.RetainedRows,
                0,
                await CountDuplicatesAsync(targetConnection, cancellationToken),
                await ReadTargetDatabaseUtcNowAsync(targetConnection, cancellationToken));
        }

        if (capabilityAfter.CurrentVersion == checkpoint.SourceVersion)
        {
            var reviewed = await _reviewedRetained.EvaluateNoChangeAsync(
                targetConnection,
                sourceConnectionString,
                route.SourceProfileCode,
                raw,
                evidence,
                checkpoint.SourceVersion,
                capabilityAfter.CurrentVersion,
                capabilityAfter.MinimumValidVersion,
                cancellationToken);
            var exactSteadyState =
                evidence.WouldInsertRows == 0 &&
                evidence.WouldReactivateRows == 0 &&
                evidence.TargetOnlyActiveRows == 0 &&
                evidence.ConflictRows == 0 &&
                evidence.WouldUpdateRows == reviewed.ReviewedRetainedCount &&
                evidence.NoChangeRows + reviewed.ReviewedRetainedCount ==
                    evidence.SourceActiveRows &&
                reviewed.NewDriftCount == 0 &&
                reviewed.StaleReviewCount == 0;
            if (!exactSteadyState)
            {
                var reason = reviewed.Evaluations.FirstOrDefault(item =>
                                 !item.IsSafeSteadyState)?.ReasonCode ??
                             Rt03ReviewedRetainedReasonCodes.NewTargetDrift;
                throw new Rt03SafetyException(
                    Rt03Errors.TargetDrift,
                    $"RT-03 reviewed-retained steady-state rejected: {reason}.");
            }

            var status = reviewed.ReviewedRetainedCount > 0
                ? Rt03CycleStatuses.HealthyReviewedRetained
                : Rt03CycleStatuses.HealthyNoChange;
            return new Rt03ProductionCycleResult(
                route.SourceProfileCode,
                status,
                Guid.NewGuid(),
                checkpoint.SourceVersion,
                checkpoint.SourceVersion,
                0, 0, 0, 0,
                await CountDuplicatesAsync(targetConnection, cancellationToken),
                await ReadTargetDatabaseUtcNowAsync(targetConnection, cancellationToken))
            {
                ReviewedRetainedCount = reviewed.ReviewedRetainedCount,
                ReviewedRetainedDomains = reviewed.ReviewedRetainedDomains,
                ActiveReviewCount = reviewed.ActiveReviewCount,
                StaleReviewCount = reviewed.StaleReviewCount,
                NewDriftCount = reviewed.NewDriftCount,
                OldestActiveReviewUtc = reviewed.OldestActiveReviewUtc,
                NewestActiveReviewUtc = reviewed.NewestActiveReviewUtc,
                CycleOutcome = status,
            };
        }

        var courseChanges = changeBatch
            .Where(change => change.TableName == "dbo.KhoaHoc")
            .ToArray();
        var courseOperations = await BuildCourseOperationsAsync(
            targetConnection,
            sourceConnectionString,
            route.SourceProfileCode,
            courseChanges,
            cancellationToken);
        var nonCourseChanges = changeBatch
            .Where(change => change.TableName != "dbo.KhoaHoc")
            .ToArray();
        var operation = courseOperations.Count > 0 && nonCourseChanges.Length == 0
            ? PlannedOperation.CourseOnly(
                changeBatch[0].ChangeVersion,
                courseOperations)
            : BuildOperation(route, raw, evidence, key, checkpoint,
                capabilityAfter.CurrentVersion, nonCourseChanges) with
                {
                    Courses = courseOperations,
                };
        if (operation.Kind == PlannedOperationKind.None &&
            capabilityAfter.CurrentVersion == checkpoint.SourceVersion)
        {
            return new Rt03ProductionCycleResult(
                route.SourceProfileCode,
                Rt03CycleStatuses.HealthyNoChange,
                Guid.NewGuid(),
                checkpoint.SourceVersion,
                checkpoint.SourceVersion,
                0, 0, 0, 0,
                await CountDuplicatesAsync(targetConnection, cancellationToken),
                await ReadTargetDatabaseUtcNowAsync(targetConnection, cancellationToken));
        }

        var plan = BuildPlan(route, raw, evidence, checkpoint, capabilityAfter, operation);
        var applyResult = await ApplyAsync(
            targetConnection,
            sourceConnectionString,
            route,
            plan,
            operation,
            workerInstanceId,
            cancellationToken);
        return applyResult;
    }

    private void ValidateFingerprints(
        Rt01ShadowRoute route,
        Rt01aRawProbe raw,
        Rt01aProbeEvidence evidence)
    {
        var expectedSource = route.SourceProfileCode == Rt03Profiles.Oto
            ? _options.ExpectedOtoSourceSchemaFingerprint
            : _options.ExpectedMotoSourceSchemaFingerprint;
        if (!string.Equals(evidence.MappingFingerprint,
                _options.ExpectedMappingFingerprint, StringComparison.Ordinal) ||
            !string.Equals(raw.SourceSchemaFingerprint,
                expectedSource, StringComparison.Ordinal) ||
            !string.Equals(raw.TargetSchemaFingerprint,
                _options.ExpectedTargetSchemaFingerprint, StringComparison.Ordinal))
        {
            throw new Rt03SafetyException(
                Rt03Errors.SourceDrift,
                "RT-03 mapping/source/target fingerprint guard rejected the cycle.");
        }
    }

    private static void ValidateCapability(
        Rt01ShadowRoute route,
        SourceCapability capability)
    {
        var expected = Rt03ProductionCatalog.RequiredDatabases.Single(item =>
            item.Role == (route.SourceProfileCode == Rt03Profiles.Oto
                ? "SOURCE_OTO"
                : "SOURCE_MOTO"));
        if (capability.ServerIdentity != Rt03ProductionCatalog.ServerIdentity ||
            capability.DatabaseName != expected.DatabaseName ||
            capability.DatabaseId != expected.DatabaseId ||
            capability.DatabaseGuid != expected.DatabaseGuid ||
            capability.CurrentVersion < 0 ||
            capability.MinimumValidVersion < 0 ||
            capability.MinimumValidVersion > capability.CurrentVersion ||
            capability.TrackedTables != 9 ||
            !capability.SnapshotEnabled || capability.RcsiEnabled)
        {
            throw new Rt03SafetyException(
                Rt03Errors.ChangeTrackingWindowRejected,
                $"RT-03 source capability guard rejected {route.SourceProfileCode}.");
        }
    }

    private static void ValidateFeature(Rt03ProductionFeatureState feature)
    {
        if (!feature.EnableProductionRealtime ||
            !feature.EnableProductionShadow ||
            !feature.EnableProductionWrites ||
            feature.EnableProductionCanary ||
            !feature.EnableControlledCutover ||
            feature.EnableProductionDeletes)
        {
            throw new Rt03SafetyException(
                Rt03Errors.FeatureStateRejected,
                "RT-03 feature state is not the exact controlled-cutover state.");
        }
    }

    private static void ValidateProfile(
        Rt01ShadowRoute route,
        Rt03ProductionProfileState profile,
        Rt01aRawProbe raw,
        Rt01aProbeEvidence evidence)
    {
        if (!profile.Enabled ||
            profile.SequenceOrder != (route.SourceProfileCode == Rt03Profiles.Oto ? 1 : 2) ||
            profile.ExpectedMappingFingerprint != evidence.MappingFingerprint ||
            profile.ExpectedSourceSchemaFingerprint != raw.SourceSchemaFingerprint ||
            profile.ExpectedTargetSchemaFingerprint != raw.TargetSchemaFingerprint)
        {
            throw new Rt03SafetyException(
                Rt03Errors.ConfigurationRejected,
                $"RT-03 profile registration does not match {route.SourceProfileCode}.");
        }
    }

    private static void ValidateCheckpoint(
        CheckpointRow checkpoint,
        SourceCapability capability,
        string mappingFingerprint)
    {
        if (checkpoint.SourceDatabaseGuid != capability.DatabaseGuid ||
            checkpoint.MappingFingerprint != mappingFingerprint ||
            checkpoint.SourceVersion < capability.MinimumValidVersion ||
            checkpoint.SourceVersion > capability.CurrentVersion)
        {
            throw new Rt03SafetyException(
                Rt03Errors.CheckpointConflict,
                "RT-03 checkpoint identity/version is outside the valid CT window.");
        }
    }

    private static PlannedOperation BuildOperation(
        Rt01ShadowRoute route,
        Rt01aRawProbe raw,
        Rt01aProbeEvidence evidence,
        byte[] key,
        CheckpointRow checkpoint,
        long currentVersion,
        IReadOnlyList<TrackedChangeRow> changeBatch)
    {
        if (evidence.ConflictRows != 0 || evidence.WouldReactivateRows != 0 ||
            evidence.TargetOnlyActiveRows != 0)
        {
            throw new Rt03SafetyException(
                Rt03Errors.UnsupportedDrift,
                "Conflict, reactivation, or target-only drift is not writable.");
        }

        if (currentVersion == checkpoint.SourceVersion)
        {
            if (evidence.NoChangeRows != evidence.SourceActiveRows ||
                evidence.WouldInsertRows != 0 || evidence.WouldUpdateRows != 0)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.TargetDrift,
                    "RT-03 no-change classification is internally inconsistent.");
            }

            return PlannedOperation.NoChange(checkpoint.SourceVersion);
        }

        if (currentVersion < checkpoint.SourceVersion || changeBatch.Count == 0)
        {
            throw new Rt03SafetyException(
                Rt03Errors.UnsupportedDrift,
                "The pending CT window does not contain an exact next event batch.");
        }

        var nextVersion = changeBatch[0].ChangeVersion;
        if (changeBatch.Any(change => change.ChangeVersion != nextVersion))
        {
            throw new Rt03SafetyException(
                Rt03Errors.UnsupportedDrift,
                "The CT reader returned more than one source version in a bounded cycle.");
        }

        var eventClassifications = changeBatch.Select(change =>
            Rt03ChangeTrackingEventClassifier.Classify(
                change.TableName,
                change.Operation,
                change.ChangedColumnSet)).ToArray();
        if (eventClassifications.Any(classification =>
                classification == Rt03ChangeTrackingClassifications.MultiFieldPhotoDrift) &&
            eventClassifications.All(classification =>
                classification is Rt03ChangeTrackingClassifications.MultiFieldPhotoDrift or
                    Rt03ChangeTrackingClassifications.NoMappedChange))
        {
            var reviews = BuildPhotoManualReviews(
                route, raw, evidence, key, changeBatch, eventClassifications);
            return new PlannedOperation(
                PlannedOperationKind.RetainPhotoManualReview,
                null,
                null,
                string.Join(",", reviews.Select(review => review.IdentityHmac)),
                nextVersion,
                reviews,
                []);
        }

        if (eventClassifications.All(classification =>
                classification == Rt03ChangeTrackingClassifications.NoMappedChange) &&
            evidence.Candidates.Count == 0)
        {
            return new PlannedOperation(
                PlannedOperationKind.AdvanceNoMappedChange,
                null,
                null,
                string.Empty,
                nextVersion,
                [],
                []);
        }

        var batchIdentityHmacs = changeBatch
            .Where(change => change.TableName is "dbo.NguoiLX" or "dbo.NguoiLX_HoSo")
            .Select(change => Rt01IdentityHmac(key, route.SourceProfileCode, change.Key1))
            .ToHashSet(StringComparer.Ordinal);
        var selectedCandidates = evidence.Candidates
            .Where(candidate => batchIdentityHmacs.Contains(candidate.IdentityHmac))
            .ToArray();
        var learnerReplayDisposition = changeBatch.All(change =>
                change.TableName is "dbo.NguoiLX" or "dbo.NguoiLX_HoSo")
            ? Rt03LearnerReplayRules.ClassifyConvergedReplay(
                route.SourceProfileCode,
                changeBatch.Select(change => new Rt03LearnerReplayEvent(
                    change.TableName,
                    change.Operation,
                    change.Key1)).ToArray(),
                raw.MappedSourceRows.Select(row => new Rt03LearnerReplayIdentity(
                    row.SourceProfileCode,
                    row.SourceMaDK,
                    row.V2RowHash)).ToArray(),
                raw.TargetRows
                    .Where(row => row.SourceProfileCode is not null &&
                                  row.SourceMaDK is not null)
                    .Select(row => new Rt03LearnerReplayIdentity(
                        row.SourceProfileCode!,
                        row.SourceMaDK!,
                        row.V2RowHash ?? string.Empty,
                        row.IsDeleted)).ToArray())
            : Rt03LearnerReplayDisposition.Blocked;
        if (selectedCandidates.Length == 0 &&
            learnerReplayDisposition is
                Rt03LearnerReplayDisposition.Converged or
                Rt03LearnerReplayDisposition.IdempotentDeleteAlreadyAbsent)
        {
            return new PlannedOperation(
                learnerReplayDisposition ==
                    Rt03LearnerReplayDisposition.IdempotentDeleteAlreadyAbsent
                    ? PlannedOperationKind.AdvanceIdempotentDeleteNoChange
                    : PlannedOperationKind.AdvanceNoMappedChange,
                null,
                null,
                string.Empty,
                nextVersion,
                [],
                []);
        }

        if (selectedCandidates.Length != 1)
        {
            throw new Rt03SafetyException(
                Rt03Errors.UnsupportedDrift,
                "RT-03 requires one exact CT-batch learner candidate for a writable cycle.");
        }

        var candidate = selectedCandidates[0];
        var source = raw.MappedSourceRows.Single(row =>
            Rt01IdentityHmac(key, row.SourceProfileCode, row.SourceMaDK) ==
            candidate.IdentityHmac);
        if (candidate.CandidateType == "WOULD_INSERT" &&
            candidate.Classification == "SOURCE_ONLY_NEW_ROW" &&
            candidate.SafeDisposition == "WOULD_INSERT_SAFE_AFTER_APPROVAL" &&
            !candidate.SqlCollationEqualCounterpart &&
            !candidate.AlternateImportedKeyEvidence &&
            !candidate.SoftDeletedCounterpart &&
            !candidate.OtherProfileCounterpart &&
            !candidate.ManualReviewRequired)
        {
            return new PlannedOperation(
                PlannedOperationKind.Insert, source, null, candidate.IdentityHmac,
                nextVersion, [], []);
        }

        if (candidate.CandidateType == "WOULD_UPDATE" &&
            candidate.Classification == "STALE_IMPORTED_VALUE" &&
            candidate.SafeDisposition == "WOULD_UPDATE_SOURCE_OWNED_FIELDS_AFTER_APPROVAL" &&
            candidate.FieldDifferences.Count == 1 &&
            candidate.FieldDifferences[0].FieldCategory == "HoTen" &&
            candidate.FieldDifferences[0].SafeUpdateEligible)
        {
            var target = raw.TargetRows.Single(row =>
                row.SourceProfileCode == route.SourceProfileCode &&
                string.Equals(row.SourceMaDK?.Trim(), source.SourceMaDK.Trim(),
                    StringComparison.OrdinalIgnoreCase) && !row.IsDeleted);
            return new PlannedOperation(
                PlannedOperationKind.UpdateHoTen, source, target, candidate.IdentityHmac,
                nextVersion, [], []);
        }

        throw new Rt03SafetyException(
            Rt03Errors.UnsupportedDrift,
            "Only source-only insert or exact HoTen-only update is supported.");
    }

    private async Task<IReadOnlyList<CourseOperation>> BuildCourseOperationsAsync(
        SqlConnection targetConnection,
        string sourceConnectionString,
        string sourceProfileCode,
        IReadOnlyList<TrackedChangeRow> courseChanges,
        CancellationToken cancellationToken)
    {
        if (courseChanges.Count == 0)
        {
            return [];
        }

        var operations = new List<CourseOperation>(courseChanges.Count);
        foreach (var change in courseChanges
                     .OrderBy(item => item.Key1, StringComparer.Ordinal))
        {
            var classification = Rt03ChangeTrackingEventClassifier.Classify(
                change.TableName,
                change.Operation,
                change.ChangedColumnSet);
            if (classification ==
                Rt03ChangeTrackingClassifications.UnclassifiedForwardColumn)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.UnclassifiedForwardColumn,
                    $"KhoaHoc {sourceProfileCode}/{change.Key1} changed an " +
                    "unreviewed forward column.");
            }

            if (classification is not
                (Rt03ChangeTrackingClassifications.KhoaHocSourceInsert or
                 Rt03ChangeTrackingClassifications.KhoaHocSourceUpdate))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.UnsupportedDrift,
                    $"KhoaHoc {sourceProfileCode}/{change.Key1} operation " +
                    $"{change.Operation} is not an approved source-owned INSERT/UPDATE.");
            }

            var source = await ReadMappedCourseAsync(
                sourceConnectionString,
                sourceProfileCode,
                change.Key1,
                cancellationToken);
            var exactRows = (await targetConnection.QueryAsync<CourseTargetRow>(
                new CommandDefinition(
                    Rt03ProductionSql.RecheckExactCourse,
                    new
                    {
                        source.SourceProfileCode,
                        source.SourceMaKhoaHoc,
                    },
                    commandTimeout: _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken))).ToArray();
            var sameMaKhoaRows =
                (await targetConnection.QueryAsync<Rt03CourseTargetIdentity>(
                    new CommandDefinition(
                        Rt03ProductionSql.RecheckSameMaKhoaCourses,
                        new { source.MaKhoa },
                        commandTimeout: _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken))).ToArray();
            var businessPlan = Rt03CourseBusinessRules.Plan(
                source,
                exactRows.Select(ToCourseIdentity).ToArray(),
                sameMaKhoaRows);
            var target = exactRows.SingleOrDefault();
            if (businessPlan.Action == Rt03CourseBusinessActions.NoChange &&
                (target is null || !CourseTargetMatchesSource(target, source)))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.TargetDrift,
                    $"KhoaHoc {sourceProfileCode}/{change.Key1} has a matching " +
                    "SourceHash but its explicit source-owned projection differs.");
            }

            operations.Add(new CourseOperation(
                businessPlan.Action,
                source,
                target?.KhoaHocId,
                target?.RowVersion,
                target is null ? string.Empty : CourseQlhvOwnedHash(target)));
        }

        return operations;
    }

    private async Task<QlhvImportKhoaHocWriteModel> ReadMappedCourseAsync(
        string sourceConnectionString,
        string sourceProfileCode,
        string sourceMaKhoaHoc,
        CancellationToken cancellationToken)
    {
        await using var sourceConnection = new SqlConnection(sourceConnectionString);
        await sourceConnection.OpenAsync(cancellationToken);
        var rows = (await sourceConnection.QueryAsync<QlhvKhoaHocSourceRow>(
            new CommandDefinition(
                SourceCourseSql,
                new { SourceMaKhoaHoc = sourceMaKhoaHoc },
                commandTimeout: _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
        if (rows.Length != 1)
        {
            throw new Rt03SafetyException(
                Rt03Errors.SourceChangedDuringPlan,
                $"KhoaHoc {sourceProfileCode}/{sourceMaKhoaHoc} disappeared " +
                "or became ambiguous before apply.");
        }

        var mapped = QlhvImportCourseTeacherMapper.MapKhoaHoc(
            rows[0],
            sourceProfileCode);
        if (mapped.Model is null || mapped.Blockers.Count != 0)
        {
            throw new Rt03SafetyException(
                Rt03Errors.UnsupportedDrift,
                $"KhoaHoc {sourceProfileCode}/{sourceMaKhoaHoc} source mapping " +
                $"is blocked: {string.Join("; ", mapped.Blockers)}");
        }

        return mapped.Model;
    }

    private static IReadOnlyList<ManualReviewOperation> BuildPhotoManualReviews(
        Rt01ShadowRoute route,
        Rt01aRawProbe raw,
        Rt01aProbeEvidence evidence,
        byte[] key,
        IReadOnlyList<TrackedChangeRow> changeBatch,
        IReadOnlyList<string> classifications)
    {
        var photoKeys = changeBatch
            .Zip(classifications)
            .Where(item => item.Second ==
                Rt03ChangeTrackingClassifications.MultiFieldPhotoDrift)
            .Select(item => item.First.Key1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reviews = new List<ManualReviewOperation>(photoKeys.Length);
        foreach (var sourceKey in photoKeys)
        {
            var sourceRows = raw.MappedSourceRows.Where(row =>
                string.Equals(row.SourceMaDK.Trim(), sourceKey.Trim(),
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var targetRows = raw.TargetRows.Where(row =>
                !row.IsDeleted &&
                string.Equals(row.SourceProfileCode, route.SourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(row.SourceMaDK?.Trim(), sourceKey.Trim(),
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (sourceRows.Length != 1 || targetRows.Length != 1)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.UnsupportedDrift,
                    "A photo manual-review event does not have one live source/target identity.");
            }

            var identityHmac = Rt01IdentityHmac(
                key, route.SourceProfileCode, sourceRows[0].SourceMaDK);
            var candidate = evidence.Candidates.SingleOrDefault(item =>
                item.IdentityHmac == identityHmac);
            if (candidate is not null &&
                (candidate.Classification != "MULTI_FIELD_PHOTO_DRIFT" ||
                 candidate.SafeDisposition != "MANUAL_REVIEW_REQUIRED" ||
                 !candidate.ManualReviewRequired ||
                 candidate.FieldDifferences.Count == 0 ||
                 candidate.FieldDifferences.Any(field => field.FieldCategory is not
                     ("AnhRelativePath" or "ChatLuongAnh" or "NgayThuNhanAnh"))))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.UnsupportedDrift,
                    "Current mapped evidence is not an exact photo-only manual-review drift.");
            }

            var rollbackImageHash = Rt03Hash.Sha256(string.Join("|",
                "RT03-PHOTO-RETAIN-v1", identityHmac,
                sourceRows[0].V2RowHash, targetRows[0].V2RowHash));
            reviews.Add(new ManualReviewOperation(
                sourceRows[0].SourceMaDK,
                identityHmac,
                Rt03ChangeTrackingClassifications.MultiFieldPhotoDrift,
                rollbackImageHash));
        }

        return reviews.OrderBy(review => review.IdentityHmac, StringComparer.Ordinal).ToArray();
    }

    private static ImmutablePlan BuildPlan(
        Rt01ShadowRoute route,
        Rt01aRawProbe raw,
        Rt01aProbeEvidence evidence,
        CheckpointRow checkpoint,
        SourceCapability capability,
        PlannedOperation operation)
    {
        var cycleId = Guid.NewGuid();
        var learnerOperationToken = operation.Kind switch
        {
            PlannedOperationKind.None => "NO_CHANGE",
            PlannedOperationKind.AdvanceNoMappedChange => "NO_MAPPED_CHANGE",
            PlannedOperationKind.AdvanceIdempotentDeleteNoChange =>
                "IDEMPOTENT_DELETE_ALREADY_ABSENT",
            PlannedOperationKind.CourseOnly => "COURSE_ONLY",
            PlannedOperationKind.Insert => $"INSERT|{operation.IdentityHmac}|{operation.Source!.V2RowHash}",
            PlannedOperationKind.UpdateHoTen =>
                $"UPDATE_HOTEN|{operation.IdentityHmac}|{operation.Target!.HocVienId}|" +
                $"{operation.Target.V2RowHash}|{operation.Source!.V2RowHash}",
            PlannedOperationKind.RetainPhotoManualReview =>
                "PHOTO_MANUAL_REVIEW|" + string.Join(",", operation.ManualReviews.Select(review =>
                    $"{review.IdentityHmac}:{review.RollbackImageHash}")),
            _ => throw new InvalidOperationException("Unknown RT-03 operation."),
        };
        var courseOperationToken = string.Join(
            ",",
            operation.Courses
                .OrderBy(item => item.Source.SourceMaKhoaHoc, StringComparer.Ordinal)
                .Select(item => string.Join(":",
                    item.Action,
                    item.Source.SourceProfileCode,
                    item.Source.SourceMaKhoaHoc,
                    item.Source.SourceHash,
                    item.TargetKhoaHocId?.ToString(CultureInfo.InvariantCulture) ?? "<NULL>",
                    item.ExpectedRowVersion is null
                        ? "<NULL>"
                        : Convert.ToHexString(item.ExpectedRowVersion))));
        var operationToken =
            $"{learnerOperationToken}|COURSES|{courseOperationToken}";
        var planHash = Rt03Hash.Sha256(string.Join("|",
            "RT03-PRODUCTION-v1", cycleId, route.SourceProfileCode,
            evidence.MappingFingerprint, raw.SourceSchemaFingerprint,
            raw.TargetSchemaFingerprint, checkpoint.SourceVersion,
            operation.ToVersion, evidence.StageHash,
            evidence.TargetComparisonHash, operationToken));
        return new ImmutablePlan(
            cycleId,
            route.SourceProfileCode,
            capability.DatabaseGuid,
            checkpoint.SourceVersion,
            operation.ToVersion,
            evidence.MappingFingerprint,
            raw.SourceSchemaFingerprint,
            raw.TargetSchemaFingerprint,
            evidence.StageHash,
            evidence.TargetComparisonHash,
            planHash,
            Rt03Hash.Sha256(operationToken));
    }

    private async Task<Rt03ProductionCycleResult> ApplyAsync(
        SqlConnection targetConnection,
        string sourceConnectionString,
        Rt01ShadowRoute route,
        ImmutablePlan plan,
        PlannedOperation operation,
        string workerInstanceId,
        CancellationToken cancellationToken)
    {
        var committedAt = default(DateTime);
        var markerHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{plan.CycleId:D}|{plan.PlanHash}|{plan.DispositionHash}|{plan.ToVersion}"));
        var inserted = 0;
        var updated = 0;
        var retained = 0;
        long? insertedId = null;
        await using (var transaction = (SqlTransaction)await targetConnection.BeginTransactionAsync(
                         System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            try
            {
                committedAt = await targetConnection.ExecuteScalarAsync<DateTime>(
                    new CommandDefinition(
                        "SELECT CONVERT(datetime2(7),SYSUTCDATETIME());",
                        transaction: transaction,
                        commandTimeout: _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken));
                var identity = await targetConnection.QueryAsync(
                    new CommandDefinition(
                        Rt03ProductionSql.RevalidateTargetIdentity,
                        transaction: transaction,
                        commandTimeout: _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken));
                if (identity.Count() != 1)
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.ProductionIdentityRejected,
                        "QLHV_APP identity revalidation failed.");
                }

                await targetConnection.ExecuteAsync(new CommandDefinition(
                    Rt03ProductionSql.AcquireProductionProfileLock,
                    new { ExactProfileLockName = $"QLHV:RT03:{route.SourceProfileCode}" },
                    transaction,
                    _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken));
                await targetConnection.ExecuteAsync(new CommandDefinition(
                    Rt03ProductionSql.RejectActiveAutoSync,
                    transaction: transaction,
                    commandTimeout: _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken));
                await RequireFeatureAndProfileAsync(
                    targetConnection, transaction, route, plan, cancellationToken);
                var currentCheckpoint = await targetConnection.QuerySingleAsync<CheckpointRow>(
                    new CommandDefinition(
                        CheckpointForUpdateSql,
                        new
                        {
                            SourceProfileCode = route.SourceProfileCode,
                            plan.MappingFingerprint,
                        },
                        transaction,
                        _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken));
                if (currentCheckpoint.SourceVersion != plan.FromVersion ||
                    currentCheckpoint.SourceDatabaseGuid != plan.SourceDatabaseGuid)
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.CheckpointConflict,
                        "Checkpoint changed after immutable plan construction.");
                }

                var capability = await ReadCapabilityAsync(
                    sourceConnectionString, route, cancellationToken);
                if (capability.CurrentVersion < plan.ToVersion ||
                    capability.MinimumValidVersion > plan.FromVersion)
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.ChangeTrackingWindowRejected,
                        "Source CT window changed before target transaction apply.");
                }

                var beforeRows = (await targetConnection.QueryAsync<QlhvOwnedRow>(
                    new CommandDefinition(
                        QlhvOwnedRowsSql,
                        new { SourceProfileCode = route.SourceProfileCode },
                        transaction,
                        _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken))).ToArray();
                var beforeHash = QlhvOwnedHash(beforeRows, null);

                var courseCounts = await ApplyCourseOperationsAsync(
                    targetConnection,
                    transaction,
                    sourceConnectionString,
                    operation.Courses,
                    committedAt,
                    cancellationToken);
                inserted += courseCounts.Inserted;
                updated += courseCounts.Updated;

                if (operation.Kind == PlannedOperationKind.Insert)
                {
                    var learnerCourseKey = operation.Source!.MaKhoa;
                    if (string.IsNullOrWhiteSpace(learnerCourseKey))
                    {
                        throw new Rt03SafetyException(
                            Rt03Errors.LearnerCourseNotConvergent,
                            "Learner insert has no exact source course identity.");
                    }

                    var courseCandidates =
                        (await targetConnection.QueryAsync<Rt03CourseTargetIdentity>(
                            new CommandDefinition(
                                Rt03ProductionSql.ResolveLearnerCourse,
                                new
                                {
                                    operation.Source!.SourceProfileCode,
                                    SourceMaKhoaHoc = learnerCourseKey,
                                },
                                transaction,
                                _syncOptions.TimeoutSeconds,
                                cancellationToken: cancellationToken))).ToArray();
                    Rt03CourseBusinessRules.RequireLearnerCourse(
                        operation.Source!.SourceProfileCode,
                        learnerCourseKey,
                        courseCandidates);

                    var collision = await targetConnection.ExecuteScalarAsync<int>(
                        new CommandDefinition(
                            ExactIdentityCollisionSql,
                            new { operation.Source!.SourceMaDK },
                            transaction,
                            _syncOptions.TimeoutSeconds,
                            cancellationToken: cancellationToken));
                    if (collision != 0)
                    {
                        throw new Rt03SafetyException(
                            Rt03Errors.TargetDrift,
                            "Insert identity appeared after immutable plan construction.");
                    }

                    insertedId = await targetConnection.ExecuteScalarAsync<long>(
                        new CommandDefinition(
                            Rt03ProductionSql.InsertProductionLearner +
                            "\nSELECT CONVERT(bigint, SCOPE_IDENTITY());",
                            InsertParameters(operation.Source!, committedAt),
                            transaction,
                            _syncOptions.TimeoutSeconds,
                            cancellationToken: cancellationToken));
                    inserted++;
                }
                else if (operation.Kind == PlannedOperationKind.UpdateHoTen)
                {
                    await targetConnection.ExecuteAsync(new CommandDefinition(
                        Rt03ProductionSql.UpdateExactHoTen,
                        new
                        {
                            DesiredHoTen = operation.Source!.HoTen,
                            SourceRowHash = operation.Source.V2RowHash,
                            TargetHocVienId = operation.Target!.HocVienId,
                            operation.Source.SourceProfileCode,
                            operation.Source.SourceMaDK,
                            ExpectedMappedHash = operation.Target.V2RowHash,
                        },
                        transaction,
                        _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken));
                    updated++;
                }
                else if (operation.Kind == PlannedOperationKind.RetainPhotoManualReview)
                {
                    foreach (var review in operation.ManualReviews)
                    {
                        var retainedTargets = (await targetConnection.QueryAsync<QlhvOwnedRow>(
                            new CommandDefinition(
                                Rt03ProductionSql.RecheckExactLearner,
                                new
                                {
                                    SourceProfileCode = route.SourceProfileCode,
                                    SourceMaDK = review.SourceMaDK,
                                },
                                transaction,
                                _syncOptions.TimeoutSeconds,
                                cancellationToken: cancellationToken))).ToArray();
                        if (retainedTargets.Length != 1 || retainedTargets[0].IsDeleted)
                        {
                            throw new Rt03SafetyException(
                                Rt03Errors.TargetDrift,
                                "A photo manual-review target changed after plan construction.");
                        }

                        await targetConnection.ExecuteAsync(new CommandDefinition(
                            Rt03ProductionSql.InsertManualReview,
                            new
                            {
                                plan.CycleId,
                                plan.PlanHash,
                                CandidateId = $"PHOTO-CT-{plan.ToVersion}-{retained + 1}",
                                SourceProfileCode = route.SourceProfileCode,
                                review.IdentityHmac,
                                review.Classification,
                                review.RollbackImageHash,
                                CommittedAtUtc = committedAt,
                            },
                            transaction,
                            _syncOptions.TimeoutSeconds,
                            cancellationToken: cancellationToken));
                        retained++;
                    }
                }

                var afterRows = (await targetConnection.QueryAsync<QlhvOwnedRow>(
                    new CommandDefinition(
                        QlhvOwnedRowsSql,
                        new { SourceProfileCode = route.SourceProfileCode },
                        transaction,
                        _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken))).ToArray();
                if (beforeHash != QlhvOwnedHash(afterRows, insertedId) ||
                    await CountDuplicatesAsync(targetConnection, transaction, cancellationToken) != 0)
                {
                    throw new Rt03SafetyException(
                        Rt03Errors.TargetDrift,
                        "QLHV-owned or duplicate invariant changed inside the transaction.");
                }

                await targetConnection.ExecuteAsync(new CommandDefinition(
                    Rt03ProductionSql.InsertApplyMarker,
                    new
                    {
                        plan.CycleId,
                        SourceProfileCode = route.SourceProfileCode,
                        plan.PlanHash,
                        MarkerHash = markerHash,
                        plan.DispositionHash,
                        SourceDatabaseGuid = plan.SourceDatabaseGuid,
                        SourceChangeTrackingVersion = plan.ToVersion,
                        InsertedRows = inserted,
                        UpdatedRows = updated,
                        RetainedRows = retained,
                        PreservedQlhvOwnedHash = beforeHash,
                        CommittedAtUtc = committedAt,
                    },
                    transaction,
                    _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        await PublishCheckpointAsync(
            targetConnection, plan, markerHash, cancellationToken);
        var duplicateRows = await CountDuplicatesAsync(targetConnection, cancellationToken);
        return new Rt03ProductionCycleResult(
            route.SourceProfileCode,
            operation.Kind == PlannedOperationKind.None
                ? Rt03CycleStatuses.HealthyNoChange
                : Rt03CycleStatuses.Applied,
            plan.CycleId,
            plan.FromVersion,
            plan.ToVersion,
            inserted,
            updated,
            retained,
            0,
            duplicateRows,
            await ReadTargetDatabaseUtcNowAsync(targetConnection, cancellationToken));
    }

    private async Task<(int Inserted, int Updated)> ApplyCourseOperationsAsync(
        SqlConnection targetConnection,
        SqlTransaction transaction,
        string sourceConnectionString,
        IReadOnlyList<CourseOperation> operations,
        DateTime committedAt,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var updated = 0;
        foreach (var operation in operations)
        {
            var currentSource = await ReadMappedCourseAsync(
                sourceConnectionString,
                operation.Source.SourceProfileCode,
                operation.Source.SourceMaKhoaHoc,
                cancellationToken);
            Rt03CourseBusinessRules.RequireStableSource(
                operation.Source,
                currentSource);

            var exactRows = (await targetConnection.QueryAsync<CourseTargetRow>(
                new CommandDefinition(
                    Rt03ProductionSql.RecheckExactCourse,
                    new
                    {
                        operation.Source.SourceProfileCode,
                        operation.Source.SourceMaKhoaHoc,
                    },
                    transaction,
                    _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken))).ToArray();
            var sameMaKhoaRows =
                (await targetConnection.QueryAsync<Rt03CourseTargetIdentity>(
                    new CommandDefinition(
                        Rt03ProductionSql.RecheckSameMaKhoaCourses,
                        new { operation.Source.MaKhoa },
                        transaction,
                        _syncOptions.TimeoutSeconds,
                        cancellationToken: cancellationToken))).ToArray();
            var currentPlan = Rt03CourseBusinessRules.Plan(
                operation.Source,
                exactRows.Select(ToCourseIdentity).ToArray(),
                sameMaKhoaRows);
            var currentTarget = exactRows.SingleOrDefault();
            if (!string.Equals(
                    currentPlan.Action,
                    operation.Action,
                    StringComparison.Ordinal) ||
                currentTarget?.KhoaHocId != operation.TargetKhoaHocId ||
                !BytesEqual(currentTarget?.RowVersion, operation.ExpectedRowVersion))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.TargetDrift,
                    $"KhoaHoc {operation.Source.SourceProfileCode}/" +
                    $"{operation.Source.SourceMaKhoaHoc} changed after plan construction.");
            }

            if (currentTarget is not null)
            {
                Rt03CourseBusinessRules.RequireQlhvOwnedFingerprintUnchanged(
                    operation.ExpectedQlhvOwnedHash,
                    CourseQlhvOwnedHash(currentTarget));
            }

            if (operation.Action == Rt03CourseBusinessActions.Insert)
            {
                await targetConnection.ExecuteAsync(new CommandDefinition(
                    Rt03ProductionSql.InsertProductionCourse,
                    CourseParameters(operation.Source, committedAt, null, null),
                    transaction,
                    _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken));
                inserted++;
            }
            else if (operation.Action == Rt03CourseBusinessActions.Update)
            {
                await targetConnection.ExecuteAsync(new CommandDefinition(
                    Rt03ProductionSql.UpdateProductionCourse,
                    CourseParameters(
                        operation.Source,
                        committedAt,
                        operation.TargetKhoaHocId,
                        operation.ExpectedRowVersion),
                    transaction,
                    _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken));
                updated++;
            }

            var verifiedRows = (await targetConnection.QueryAsync<CourseTargetRow>(
                new CommandDefinition(
                    Rt03ProductionSql.RecheckExactCourse,
                    new
                    {
                        operation.Source.SourceProfileCode,
                        operation.Source.SourceMaKhoaHoc,
                    },
                    transaction,
                    _syncOptions.TimeoutSeconds,
                    cancellationToken: cancellationToken))).ToArray();
            if (verifiedRows.Length != 1 ||
                !CourseTargetMatchesSource(verifiedRows[0], operation.Source) ||
                currentTarget is not null &&
                !string.Equals(
                    CourseQlhvOwnedHash(verifiedRows[0]),
                    operation.ExpectedQlhvOwnedHash,
                    StringComparison.Ordinal))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.TargetDrift,
                    $"KhoaHoc {operation.Source.SourceProfileCode}/" +
                    $"{operation.Source.SourceMaKhoaHoc} failed post-write verification.");
            }
        }

        return (inserted, updated);
    }

    private async Task RequireFeatureAndProfileAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Rt01ShadowRoute route,
        ImmutablePlan plan,
        CancellationToken cancellationToken)
    {
        var valid = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            FeatureAndProfileGuardSql,
            new
            {
                SourceProfileCode = route.SourceProfileCode,
                plan.MappingFingerprint,
                ExpectedSourceSchemaFingerprint = plan.SourceSchemaFingerprint,
                ExpectedTargetSchemaFingerprint = plan.TargetSchemaFingerprint,
            },
            transaction,
            _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (valid != 1)
        {
            throw new Rt03SafetyException(
                Rt03Errors.FeatureStateRejected,
                "Control-plane feature/profile state changed before apply.");
        }
    }

    private async Task PublishCheckpointAsync(
        SqlConnection connection,
        ImmutablePlan plan,
        byte[] markerHash,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                PublishCheckpointSql,
                new
                {
                    plan.SourceProfileCode,
                    plan.MappingFingerprint,
                    plan.SourceDatabaseGuid,
                    FromVersion = plan.FromVersion,
                    SourceChangeTrackingVersion = plan.ToVersion,
                    plan.CycleId,
                    plan.PlanHash,
                    MarkerHash = markerHash,
                },
                transaction,
                _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken));
            if (affected != 1)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.CheckpointConflict,
                    "Checkpoint optimistic update failed after committed marker.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<RecoveredMarker?> TryRecoverCheckpointAsync(
        SqlConnection connection,
        CheckpointRow checkpoint,
        long currentVersion,
        CancellationToken cancellationToken)
    {
        var markers = (await connection.QueryAsync<RecoveredMarker>(new CommandDefinition(
            UncheckpointedMarkerSql,
            new
            {
                checkpoint.SourceProfileCode,
                checkpoint.MappingFingerprint,
                CheckpointVersion = checkpoint.SourceVersion,
                CurrentVersion = currentVersion,
            },
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToArray();
        if (markers.Length == 0)
        {
            return null;
        }

        if (markers.Length != 1 || markers[0].DeletedOrDeactivatedRows != 0 ||
            await CountDuplicatesAsync(connection, cancellationToken) != 0)
        {
            throw new Rt03SafetyException(
                Rt03Errors.CheckpointConflict,
                "Crash recovery found ambiguous markers or failed integrity.");
        }

        var marker = markers[0];
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            RecoverCheckpointSql,
            new
            {
                checkpoint.SourceProfileCode,
                checkpoint.MappingFingerprint,
                CheckpointVersion = checkpoint.SourceVersion,
                RecoveredVersion = marker.SourceVersion,
                marker.CycleId,
                marker.PlanHash,
                marker.MarkerHash,
            },
            transaction,
            _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new Rt03SafetyException(
                Rt03Errors.CheckpointConflict,
                "Crash recovery checkpoint update conflicted.");
        }

        await transaction.CommitAsync(cancellationToken);
        return marker;
    }

    private async Task<CheckpointRow> ReadCheckpointAsync(
        SqlConnection connection,
        string sourceProfileCode,
        string mappingFingerprint,
        CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<CheckpointRow>(new CommandDefinition(
            CheckpointSql,
            new { SourceProfileCode = sourceProfileCode, MappingFingerprint = mappingFingerprint },
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));

    private async Task<DateTime> ReadTargetDatabaseUtcNowAsync(
        SqlConnection connection,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            "SELECT CONVERT(datetime2(7),SYSUTCDATETIME());",
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));

    private async Task<string> ResolveSourceAsync(
        Rt01ShadowRoute route,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByCodeAsync(route.SourceProfileCode, cancellationToken);
        if (profile is null || !profile.IsActive ||
            string.IsNullOrWhiteSpace(profile.ServerName) ||
            profile.DatabaseName != route.SourceDatabaseName)
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                $"{route.SourceProfileCode} does not resolve to the exact live database.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.ServerName,
            InitialCatalog = profile.DatabaseName,
            ConnectTimeout = Math.Clamp(_syncOptions.TimeoutSeconds, 5, 30),
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
        };
        if (string.Equals(profile.AuthMode, "SqlLogin", StringComparison.OrdinalIgnoreCase))
        {
            if (!profile.IsPasswordConfigured || profile.PasswordCipherText is null ||
                string.IsNullOrWhiteSpace(profile.UserName) || !_passwordProtector.IsAvailable)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.ProductionIdentityRejected,
                    "Live source credentials are unavailable.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = profile.UserName;
            builder.Password = _passwordProtector.Unprotect(profile.PasswordCipherText);
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private async Task<SourceCapability> ReadCapabilityAsync(
        string connectionString,
        Rt01ShadowRoute route,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<SourceCapability>(new CommandDefinition(
            SourceCapabilitySql,
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private async Task<IReadOnlyList<TrackedChangeRow>> ReadNextChangeBatchAsync(
        string connectionString,
        long checkpointVersion,
        long sealedCurrentVersion,
        CancellationToken cancellationToken)
    {
        if (sealedCurrentVersion <= checkpointVersion)
        {
            return [];
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<TrackedChangeRow>(new CommandDefinition(
            NextChangeBatchSql,
            new
            {
                CheckpointVersion = checkpointVersion,
                SealedCurrentVersion = sealedCurrentVersion,
            },
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToArray();
        return rows;
    }

    private async Task<int> CountDuplicatesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
        => await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            DuplicateSql,
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));

    private async Task<int> CountDuplicatesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
        => await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            DuplicateSql,
            transaction: transaction,
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));

    private static object InsertParameters(
        QlhvImportHocVienWriteModel row,
        DateTime committedAt)
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

    private static object CourseParameters(
        QlhvImportKhoaHocWriteModel row,
        DateTime committedAt,
        long? khoaHocId,
        byte[]? expectedRowVersion)
        => new
        {
            row.SourceProfileCode,
            row.SourceMaKhoaHoc,
            row.SourceHash,
            row.MaKhoa,
            row.TenKhoa,
            row.MaCSDT,
            row.MaSoGTVT,
            row.HangGPLX,
            row.HangDaoTao,
            row.SoQuyetDinhKhaiGiang,
            row.NgayQuyetDinhKhaiGiang,
            row.NgayKhaiGiang,
            row.NgayBeGiang,
            row.MucTieuDaoTao,
            row.NgayThi,
            row.NgaySatHach,
            row.TongSoHocVien,
            row.SoHocVienTotNghiep,
            row.SoHocVienDuocCapGPLX,
            row.ThoiGianDaoTao,
            row.SoNgayOnKiemTra,
            row.SoNgayThucHoc,
            row.SoNgayNghiLe,
            row.TongSoNgay,
            row.GhiChu,
            row.TrangThaiNguon,
            row.TtXuLy,
            row.HinhThucDaoTao,
            KhoaHocId = khoaHocId,
            ExpectedRowVersion = expectedRowVersion,
            CommittedAtUtc = committedAt,
        };

    private static Rt03CourseTargetIdentity ToCourseIdentity(CourseTargetRow row)
        => new(
            row.KhoaHocId,
            row.SourceProfileCode,
            row.SourceMaKhoaHoc,
            row.SourceHash,
            row.MaKhoa,
            row.TrangThaiNguon == true,
            row.IsDeleted);

    private static bool CourseTargetMatchesSource(
        CourseTargetRow target,
        QlhvImportKhoaHocWriteModel source)
        => !target.IsDeleted &&
           string.Equals(target.SourceProfileCode, source.SourceProfileCode,
               StringComparison.Ordinal) &&
           string.Equals(target.SourceMaKhoaHoc, source.SourceMaKhoaHoc,
               StringComparison.OrdinalIgnoreCase) &&
           string.Equals(target.SourceHash, source.SourceHash,
               StringComparison.Ordinal) &&
           string.Equals(target.V2RowHash, source.SourceHash,
               StringComparison.Ordinal) &&
           string.Equals(target.SourceOfTruth, "V2", StringComparison.Ordinal) &&
           Equal(target.MaKhoa, source.MaKhoa) &&
           Equal(target.TenKhoa, source.TenKhoa) &&
           Equal(target.MaCSDT, source.MaCSDT) &&
           Equal(target.MaSoGTVT, source.MaSoGTVT) &&
           Equal(target.HangGPLX, source.HangGPLX) &&
           Equal(target.HangDaoTao, source.HangDaoTao) &&
           Equal(target.SoQuyetDinhKhaiGiang, source.SoQuyetDinhKhaiGiang) &&
           EqualDate(target.NgayQuyetDinhKhaiGiang,
               source.NgayQuyetDinhKhaiGiang) &&
           EqualDate(target.NgayKhaiGiang, source.NgayKhaiGiang) &&
           EqualDate(target.NgayBeGiang, source.NgayBeGiang) &&
           Equal(target.MucTieuDaoTao, source.MucTieuDaoTao) &&
           EqualDate(target.NgayThi, source.NgayThi) &&
           EqualDate(target.NgaySatHach, source.NgaySatHach) &&
           target.TongSoHocVien == source.TongSoHocVien &&
           target.SoHocVienTotNghiep == source.SoHocVienTotNghiep &&
           target.SoHocVienDuocCapGPLX == source.SoHocVienDuocCapGPLX &&
           target.ThoiGianDaoTao == source.ThoiGianDaoTao &&
           target.SoNgayOnKiemTra == source.SoNgayOnKiemTra &&
           target.SoNgayThucHoc == source.SoNgayThucHoc &&
           target.SoNgayNghiLe == source.SoNgayNghiLe &&
           target.TongSoNgay == source.TongSoNgay &&
           Equal(target.GhiChuV2, source.GhiChu) &&
           target.TrangThaiNguon == source.TrangThaiNguon &&
           target.TtXuLy == source.TtXuLy &&
           target.HinhThucDaoTao == source.HinhThucDaoTao;

    private static string CourseQlhvOwnedHash(CourseTargetRow row)
        => Rt03Hash.Sha256(string.Join("|",
            "RT03-KHOAHOC-QLHV-OWNED-v1",
            row.KhoaHocId.ToString(CultureInfo.InvariantCulture),
            Safe(row.GhiChuNoiBo),
            Safe(row.TrangThai),
            row.NgayBatDauThucHanh?.ToString(
                "O",
                CultureInfo.InvariantCulture) ?? "<NULL>",
            row.LuuLuongDaoTao?.ToString(CultureInfo.InvariantCulture) ?? "<NULL>",
            row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            Safe(row.CreatedBy)));

    private static bool Equal(string? left, string? right)
        => string.Equals(left, right, StringComparison.Ordinal);

    private static bool EqualDate(DateTime? left, DateTime? right)
        => left?.Date == right?.Date;

    private static bool BytesEqual(byte[]? left, byte[]? right)
        => left is null
            ? right is null
            : right is not null && left.AsSpan().SequenceEqual(right);

    private static string QlhvOwnedHash(
        IEnumerable<QlhvOwnedRow> rows,
        long? excludedId)
    {
        var canonical = string.Join("\n", rows
            .Where(row => row.HocVienId != excludedId)
            .OrderBy(row => row.HocVienId)
            .Select(row => string.Join("|",
                row.HocVienId.ToString(CultureInfo.InvariantCulture),
                Safe(row.SourceProfileCode), Safe(row.SourceMaDK),
                row.IsDeleted ? "1" : "0", Safe(row.GhiChuNoiBo),
                row.DaDoiChieuCccd ? "1" : "0", row.DaInThe ? "1" : "0",
                row.DaTaoXml ? "1" : "0", Safe(row.CreatedBy),
                Safe(row.UpdatedBy), Safe(row.DeletedBy), Safe(row.DeleteReason))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"RT03-QLHV-OWNED-v1|{canonical}")));
    }

    private static string Rt01IdentityHmac(
        byte[] key,
        string? profile,
        string? identity)
    {
        using var hmac = new HMACSHA256(key);
        var canonical = $"{Rt01aProofContract.HmacVersion}|identity|" +
                        $"{(profile ?? string.Empty).Trim().ToUpperInvariant()}|" +
                        $"{(identity ?? string.Empty).Trim()}";
        return $"{Rt01aProofContract.HmacVersion}:" +
               Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
                   .ToLowerInvariant();
    }

    private static string Safe(string? value) => value ?? "<NULL>";

    private enum PlannedOperationKind
    {
        None,
        AdvanceNoMappedChange,
        AdvanceIdempotentDeleteNoChange,
        CourseOnly,
        Insert,
        UpdateHoTen,
        RetainPhotoManualReview,
    }

    private sealed record PlannedOperation(
        PlannedOperationKind Kind,
        QlhvImportHocVienWriteModel? Source,
        Rt01aTargetHocVienRow? Target,
        string IdentityHmac,
        long ToVersion,
        IReadOnlyList<ManualReviewOperation> ManualReviews,
        IReadOnlyList<CourseOperation> Courses)
    {
        public static PlannedOperation NoChange(long version) =>
            new(PlannedOperationKind.None, null, null, string.Empty, version, [], []);

        public static PlannedOperation CourseOnly(
            long version,
            IReadOnlyList<CourseOperation> courses) =>
            new(
                PlannedOperationKind.CourseOnly,
                null,
                null,
                string.Empty,
                version,
                [],
                courses);
    }

    private sealed record CourseOperation(
        string Action,
        QlhvImportKhoaHocWriteModel Source,
        long? TargetKhoaHocId,
        byte[]? ExpectedRowVersion,
        string ExpectedQlhvOwnedHash);

    private sealed record ManualReviewOperation(
        string SourceMaDK,
        string IdentityHmac,
        string Classification,
        string RollbackImageHash);

    private sealed record ImmutablePlan(
        Guid CycleId,
        string SourceProfileCode,
        Guid SourceDatabaseGuid,
        long FromVersion,
        long ToVersion,
        string MappingFingerprint,
        string SourceSchemaFingerprint,
        string TargetSchemaFingerprint,
        string StageHash,
        string TargetComparisonHash,
        string PlanHash,
        string DispositionHash);

    private sealed class SourceCapability
    {
        public string ServerIdentity { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public int DatabaseId { get; init; }
        public Guid DatabaseGuid { get; init; }
        public long CurrentVersion { get; init; }
        public long MinimumValidVersion { get; init; }
        public int TrackedTables { get; init; }
        public bool SnapshotEnabled { get; init; }
        public bool RcsiEnabled { get; init; }

        public override bool Equals(object? obj)
            => obj is SourceCapability other &&
               ServerIdentity == other.ServerIdentity &&
               DatabaseName == other.DatabaseName &&
               DatabaseId == other.DatabaseId && DatabaseGuid == other.DatabaseGuid &&
               CurrentVersion == other.CurrentVersion &&
               MinimumValidVersion == other.MinimumValidVersion &&
               TrackedTables == other.TrackedTables &&
               SnapshotEnabled == other.SnapshotEnabled && RcsiEnabled == other.RcsiEnabled;

        public override int GetHashCode()
            => HashCode.Combine(
                HashCode.Combine(ServerIdentity, DatabaseName, DatabaseId, DatabaseGuid,
                    CurrentVersion, MinimumValidVersion, TrackedTables, SnapshotEnabled),
                RcsiEnabled);
    }

    private sealed class CheckpointRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public string MappingFingerprint { get; init; } = string.Empty;
        public Guid SourceDatabaseGuid { get; init; }
        public long SourceVersion { get; init; }
        public Guid CycleId { get; init; }
        public string PlanHash { get; init; } = string.Empty;
        public byte[] MarkerHash { get; init; } = Array.Empty<byte>();
    }

    private sealed class RecoveredMarker
    {
        public Guid CycleId { get; init; }
        public string PlanHash { get; init; } = string.Empty;
        public byte[] MarkerHash { get; init; } = Array.Empty<byte>();
        public long SourceVersion { get; init; }
        public int InsertedRows { get; init; }
        public int UpdatedRows { get; init; }
        public int RetainedRows { get; init; }
        public int DeletedOrDeactivatedRows { get; init; }
    }

    private sealed class TrackedChangeRow
    {
        public string TableName { get; init; } = string.Empty;
        public long ChangeVersion { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string Key1 { get; init; } = string.Empty;
        public string? Key2 { get; init; }
        public string? ChangedColumns { get; init; }

        public IReadOnlyCollection<string> ChangedColumnSet =>
            string.IsNullOrWhiteSpace(ChangedColumns)
                ? Array.Empty<string>()
                : ChangedColumns.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                            StringSplitOptions.TrimEntries);
    }

    private sealed class QlhvOwnedRow
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

    private sealed class CourseTargetRow
    {
        public long KhoaHocId { get; init; }
        public string? SourceProfileCode { get; init; }
        public string? SourceMaKhoaHoc { get; init; }
        public string? SourceHash { get; init; }
        public string? V2RowHash { get; init; }
        public string? SourceOfTruth { get; init; }
        public string MaKhoa { get; init; } = string.Empty;
        public string? TenKhoa { get; init; }
        public string? MaCSDT { get; init; }
        public string? MaSoGTVT { get; init; }
        public string? HangGPLX { get; init; }
        public string? HangDaoTao { get; init; }
        public string? SoQuyetDinhKhaiGiang { get; init; }
        public DateTime? NgayQuyetDinhKhaiGiang { get; init; }
        public DateTime? NgayKhaiGiang { get; init; }
        public DateTime? NgayBeGiang { get; init; }
        public string? MucTieuDaoTao { get; init; }
        public DateTime? NgayThi { get; init; }
        public DateTime? NgaySatHach { get; init; }
        public int? TongSoHocVien { get; init; }
        public int? SoHocVienTotNghiep { get; init; }
        public int? SoHocVienDuocCapGPLX { get; init; }
        public int? ThoiGianDaoTao { get; init; }
        public int? SoNgayOnKiemTra { get; init; }
        public int? SoNgayThucHoc { get; init; }
        public int? SoNgayNghiLe { get; init; }
        public int? TongSoNgay { get; init; }
        public string? GhiChuV2 { get; init; }
        public bool? TrangThaiNguon { get; init; }
        public int? TtXuLy { get; init; }
        public int? HinhThucDaoTao { get; init; }
        public bool IsDeleted { get; init; }
        public string? GhiChuNoiBo { get; init; }
        public string? TrangThai { get; init; }
        public DateTime? NgayBatDauThucHanh { get; init; }
        public int? LuuLuongDaoTao { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? CreatedBy { get; init; }
        public byte[] RowVersion { get; init; } = Array.Empty<byte>();
    }

    private const string SourceCourseSql = """
        SELECT TOP (2)
            MaKH, MaCSDT, MaSoGTVT, TenKH, HangGPLX, HangDT,
            SoQD_KhaiGiang, NgayQD_KhaiGiang, NgayKG, NgayBG,
            MucTieuDT, NgayThi, NgaySH, TongSoHV, SoHVTotNghiep,
            SoHVDuocCapGPLX, ThoiGianDT, SoNgayOnKT, SoNgayThucHoc,
            SoNgayNghiLe, TongSoNgay, GhiChu, TrangThai, TT_Xuly,
            HTDaoTao
        FROM dbo.KhoaHoc
        WHERE MaKH = @SourceMaKhoaHoc
        ORDER BY MaKH;
        """;

    // The direct V2->V1 checkpoint only owns the five tables read by
    // NextChangeBatchSql. Teacher/vehicle projections have independent,
    // per-domain checkpoints and must not shorten this checkpoint's CT window.
    // TrackedTables still verifies that all nine approved tables have CT enabled.
    internal const string SourceCapabilitySql = """
        SELECT CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
               DB_NAME() AS DatabaseName, DB_ID() AS DatabaseId,
               identityRow.database_guid AS DatabaseGuid,
               CONVERT(bigint, CHANGE_TRACKING_CURRENT_VERSION()) AS CurrentVersion,
               CONVERT(bigint,
                 (SELECT MAX(valueItem.MinVersion)
                  FROM (VALUES
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.KhoaHoc'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.DM_HangDT'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.DM_DVHC')))
                  ) valueItem(MinVersion))) AS MinimumValidVersion,
               (SELECT COUNT(1) FROM sys.change_tracking_tables) AS TrackedTables,
               CONVERT(bit, CASE WHEN databaseRow.snapshot_isolation_state=1 THEN 1 ELSE 0 END)
                   AS SnapshotEnabled,
               CONVERT(bit, databaseRow.is_read_committed_snapshot_on) AS RcsiEnabled
        FROM sys.database_recovery_status identityRow
        INNER JOIN sys.databases databaseRow ON databaseRow.database_id=identityRow.database_id
        WHERE identityRow.database_id=DB_ID();
        """;

    private const string CheckpointSql = """
        SELECT SourceProfileCode, MappingFingerprint, SourceDatabaseGuid,
               SourceChangeTrackingVersion AS SourceVersion,
               CycleId, PlanHash, MarkerHash
        FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
        WHERE SourceProfileCode=@SourceProfileCode
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND MappingFingerprint=@MappingFingerprint
          AND EnvironmentId=N'PRODUCTION';
        """;

    internal const string NextChangeBatchSql = """
        ;WITH PendingChanges AS
        (
            SELECT N'dbo.NguoiLX' AS TableName,
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION) AS ChangeVersion,
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION) AS Operation,
                   CONVERT(nvarchar(200), changeRow.MaDK) AS Key1,
                   CONVERT(nvarchar(200), NULL) AS Key2,
                   CONVERT(nvarchar(max), NULL) AS ChangedColumns
            FROM CHANGETABLE(CHANGES dbo.NguoiLX, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.NguoiLX_HoSo',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.MaDK),
                   CONVERT(nvarchar(200), NULL),
                   NULLIF(CONCAT_WS(N',',
                       CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                           COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'), N'TT_XuLy', N'ColumnId'),
                           changeRow.SYS_CHANGE_COLUMNS) = 1 THEN N'TT_XuLy' END,
                       CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                           COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'), N'DuongDanAnh', N'ColumnId'),
                           changeRow.SYS_CHANGE_COLUMNS) = 1 THEN N'DuongDanAnh' END,
                       CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                           COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'), N'ChatLuongAnh', N'ColumnId'),
                           changeRow.SYS_CHANGE_COLUMNS) = 1 THEN N'ChatLuongAnh' END,
                       CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                           COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'), N'NgayThuNhanAnh', N'ColumnId'),
                           changeRow.SYS_CHANGE_COLUMNS) = 1 THEN N'NgayThuNhanAnh' END,
                       CASE WHEN CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                           COLUMNPROPERTY(OBJECT_ID(N'dbo.NguoiLX_HoSo'), N'NguoiThuNhanAnh', N'ColumnId'),
                           changeRow.SYS_CHANGE_COLUMNS) = 1 THEN N'NguoiThuNhanAnh' END), N'')
            FROM CHANGETABLE(CHANGES dbo.NguoiLX_HoSo, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.KhoaHoc',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.MaKH),
                   CONVERT(nvarchar(200), NULL),
                   NULLIF(
                       (
                           SELECT STRING_AGG(
                               CONVERT(nvarchar(max),
                                   CASE
                                       WHEN columnRow.name IN
                                       (
                                           N'MaKH', N'MaCSDT', N'MaSoGTVT',
                                           N'TenKH', N'HangGPLX', N'HangDT',
                                           N'SoQD_KhaiGiang', N'NgayQD_KhaiGiang',
                                           N'NgayKG', N'NgayBG', N'MucTieuDT',
                                           N'NgayThi', N'NgaySH', N'TongSoHV',
                                           N'SoHVTotNghiep', N'SoHVDuocCapGPLX',
                                           N'ThoiGianDT', N'SoNgayOnKT',
                                           N'SoNgayThucHoc', N'SoNgayNghiLe',
                                           N'TongSoNgay', N'GhiChu', N'TrangThai',
                                           N'NguoiTao', N'NguoiSua', N'NgayTao',
                                           N'NgaySua', N'TT_Xuly', N'HTDaoTao'
                                       )
                                       THEN columnRow.name
                                       ELSE N'__UNCLASSIFIED_FORWARD_COLUMN__'
                                   END),
                               N',') WITHIN GROUP (ORDER BY columnRow.column_id)
                           FROM sys.columns columnRow
                           WHERE columnRow.object_id = OBJECT_ID(N'dbo.KhoaHoc')
                             AND CHANGE_TRACKING_IS_COLUMN_IN_MASK(
                                 columnRow.column_id,
                                 changeRow.SYS_CHANGE_COLUMNS) = 1
                       ),
                       N'')
            FROM CHANGETABLE(CHANGES dbo.KhoaHoc, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.DM_HangDT',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.MaHangDT),
                   CONVERT(nvarchar(200), NULL),
                   CONVERT(nvarchar(max), NULL)
            FROM CHANGETABLE(CHANGES dbo.DM_HangDT, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.DM_DVHC',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.MaDvhc),
                   CONVERT(nvarchar(200), changeRow.MaDVQL),
                   CONVERT(nvarchar(max), NULL)
            FROM CHANGETABLE(CHANGES dbo.DM_DVHC, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.GiaoVien',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.MaGV),
                   CONVERT(nvarchar(200), NULL),
                   CONVERT(nvarchar(max), NULL)
            FROM CHANGETABLE(CHANGES dbo.GiaoVien, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.XeTap',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.BienSoXe),
                   CONVERT(nvarchar(200), NULL),
                   CONVERT(nvarchar(max), NULL)
            FROM CHANGETABLE(CHANGES dbo.XeTap, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.KhoaHoc_GiaoVien',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.MaLichLV),
                   CONVERT(nvarchar(200), NULL),
                   CONVERT(nvarchar(max), NULL)
            FROM CHANGETABLE(CHANGES dbo.KhoaHoc_GiaoVien, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion

            UNION ALL

            SELECT N'dbo.KhoaHoc_XeTap',
                   CONVERT(bigint, changeRow.SYS_CHANGE_VERSION),
                   CONVERT(nvarchar(1), changeRow.SYS_CHANGE_OPERATION),
                   CONVERT(nvarchar(200), changeRow.MaLichSD),
                   CONVERT(nvarchar(200), NULL),
                   CONVERT(nvarchar(max), NULL)
            FROM CHANGETABLE(CHANGES dbo.KhoaHoc_XeTap, @CheckpointVersion) changeRow
            WHERE changeRow.SYS_CHANGE_VERSION <= @SealedCurrentVersion
        ),
        NextVersion AS
        (
            SELECT MIN(ChangeVersion) AS Value
            FROM PendingChanges
        )
        SELECT changeRow.TableName, changeRow.ChangeVersion, changeRow.Operation,
               changeRow.Key1, changeRow.Key2, changeRow.ChangedColumns
        FROM PendingChanges changeRow
        CROSS JOIN NextVersion nextVersion
        WHERE changeRow.ChangeVersion = nextVersion.Value
        ORDER BY changeRow.TableName, changeRow.Key1, changeRow.Key2;
        """;

    private const string CheckpointForUpdateSql = """
        SELECT SourceProfileCode, MappingFingerprint, SourceDatabaseGuid,
               SourceChangeTrackingVersion AS SourceVersion,
               CycleId, PlanHash, MarkerHash
        FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint WITH (UPDLOCK,HOLDLOCK)
        WHERE SourceProfileCode=@SourceProfileCode
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND MappingFingerprint=@MappingFingerprint
          AND EnvironmentId=N'PRODUCTION';
        """;

    private const string FeatureAndProfileGuardSql = """
        SELECT COUNT(1)
        FROM dbo.App_QlhvDirectRealtimeFeatureState featureState
        CROSS JOIN dbo.App_QlhvDirectRealtimeProfileState profileState
        WHERE featureState.FeatureStateId=1
          AND featureState.EnableProductionRealtime=1
          AND featureState.EnableProductionShadow=1
          AND featureState.EnableProductionWrites=1
          AND featureState.EnableProductionCanary=0
          AND featureState.EnableControlledCutover=1
          AND featureState.EnableProductionDeletes=0
          AND profileState.SourceProfileCode=@SourceProfileCode
          AND profileState.Enabled=1
          AND profileState.ExpectedMappingFingerprint=@MappingFingerprint
          AND profileState.ExpectedSourceSchemaFingerprint=@ExpectedSourceSchemaFingerprint
          AND profileState.ExpectedTargetSchemaFingerprint=@ExpectedTargetSchemaFingerprint;
        """;

    private const string ExactIdentityCollisionSql = """
        SELECT COUNT(1)
        FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
        WHERE SourceMaDK=@SourceMaDK;
        """;

    private const string QlhvOwnedRowsSql = """
        SELECT HocVienId, SourceProfileCode, SourceMaDK, IsDeleted,
               GhiChuNoiBo, DaDoiChieuCCCD AS DaDoiChieuCccd,
               DaInThe, DaTaoXML AS DaTaoXml, CreatedBy, UpdatedBy,
               DeletedBy, DeleteReason
        FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
        WHERE SourceProfileCode=@SourceProfileCode
        ORDER BY HocVienId;
        """;

    private const string DuplicateSql = """
        SELECT COUNT(1)
        FROM
        (
            SELECT SourceProfileCode, SourceMaDK
            FROM dbo.App_HocVien
            WHERE IsDeleted=0
            GROUP BY SourceProfileCode, SourceMaDK
            HAVING COUNT(1)>1
        ) duplicateRow;
        """;

    private const string PublishCheckpointSql = """
        IF NOT EXISTS
        (
            SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyMarker
            WHERE CycleId=@CycleId AND SourceProfileCode=@SourceProfileCode
              AND PlanHash=@PlanHash AND MarkerHash=@MarkerHash
              AND SourceDatabaseGuid=@SourceDatabaseGuid
              AND SourceChangeTrackingVersion=@SourceChangeTrackingVersion
        ) THROW 527593, 'RT03_COMMITTED_MARKER_MISSING', 1;

        UPDATE dbo.App_QlhvDirectRealtimeApplyCheckpoint WITH (UPDLOCK,HOLDLOCK)
        SET SourceDatabaseGuid=@SourceDatabaseGuid,
            SourceChangeTrackingVersion=@SourceChangeTrackingVersion,
            CycleId=@CycleId, PlanHash=@PlanHash, MarkerHash=@MarkerHash,
            PublishedAtUtc=SYSUTCDATETIME()
        WHERE SourceProfileCode=@SourceProfileCode
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND MappingFingerprint=@MappingFingerprint
          AND EnvironmentId=N'PRODUCTION'
          AND SourceChangeTrackingVersion=@FromVersion;
        """;

    private const string UncheckpointedMarkerSql = """
        SELECT marker.CycleId, marker.PlanHash, marker.MarkerHash,
               marker.SourceChangeTrackingVersion AS SourceVersion,
               marker.InsertedRows, marker.UpdatedRows, marker.RetainedRows,
               CONVERT(int,0) AS DeletedOrDeactivatedRows
        FROM dbo.App_QlhvDirectRealtimeApplyMarker marker
        WHERE marker.SourceProfileCode=@SourceProfileCode
          AND marker.SourceChangeTrackingVersion>@CheckpointVersion
          AND marker.SourceChangeTrackingVersion<=@CurrentVersion
          AND NOT EXISTS
          (
              SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint checkpointItem
              WHERE checkpointItem.SourceProfileCode=@SourceProfileCode
                AND checkpointItem.Mode=N'DIRECT_REALTIME_APPLY'
                AND checkpointItem.MappingFingerprint=@MappingFingerprint
                AND checkpointItem.EnvironmentId=N'PRODUCTION'
                AND checkpointItem.CycleId=marker.CycleId
          )
        ORDER BY marker.SourceChangeTrackingVersion, marker.CommittedAtUtc;
        """;

    private const string RecoverCheckpointSql = """
        UPDATE dbo.App_QlhvDirectRealtimeApplyCheckpoint WITH (UPDLOCK,HOLDLOCK)
        SET SourceChangeTrackingVersion=@RecoveredVersion, CycleId=@CycleId,
            PlanHash=@PlanHash, MarkerHash=@MarkerHash,
            PublishedAtUtc=SYSUTCDATETIME()
        WHERE SourceProfileCode=@SourceProfileCode
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND MappingFingerprint=@MappingFingerprint
          AND EnvironmentId=N'PRODUCTION'
          AND SourceChangeTrackingVersion=@CheckpointVersion;
        """;
}

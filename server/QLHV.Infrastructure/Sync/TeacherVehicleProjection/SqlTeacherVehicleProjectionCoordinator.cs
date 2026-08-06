using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Application.Sync.TeacherVehicleProjection;
using QLHV.Application.Sync.VehicleRealtime;
using QLHV.Infrastructure.Sync.VehicleRealtime;

namespace QLHV.Infrastructure.Sync.TeacherVehicleProjection;

internal sealed class SqlTeacherVehicleProjectionCoordinator :
    ITeacherVehicleProjectionCoordinator
{
    private readonly QlhvOperationConnectionResolver _sourceConnections;
    private readonly IConnectionSettingsProvider _targetConnections;
    private readonly IVehicleFullConvergenceTargetStore _vehicleFullTarget;
    private readonly VehicleRealtimeCycleProcessor _vehicleRealtime;
    private readonly SyncOptions _options;
    private readonly IConfiguration _configuration;

    public SqlTeacherVehicleProjectionCoordinator(
        QlhvOperationConnectionResolver sourceConnections,
        IConnectionSettingsProvider targetConnections,
        IVehicleFullConvergenceTargetStore vehicleFullTarget,
        VehicleRealtimeCycleProcessor vehicleRealtime,
        IOptions<SyncOptions> options,
        IConfiguration configuration)
    {
        _sourceConnections = sourceConnections;
        _targetConnections = targetConnections;
        _vehicleFullTarget = vehicleFullTarget;
        _vehicleRealtime = vehicleRealtime;
        _options = options.Value;
        _configuration = configuration;
    }

    public async Task<TeacherVehicleProjectionBacklog> ReadBacklogAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        var route = VehicleRealtimeRouteCatalog.GetRequired(sourceProfileCode);
        var source = await OpenSourceAsync(route, cancellationToken);
        await using (source.Connection)
        {
            var capability = await ReadCapabilityAsync(
                source.Connection, transaction: null, route, cancellationToken);
            var checkpoints = await ReadCheckpointsAsync(
                route.SourceProfileCode, cancellationToken);
            return new(
                route.SourceProfileCode,
                capability.CurrentVersion,
                checkpoints);
        }
    }

    public async Task<TeacherVehicleProjectionCycleResult> ProcessPendingAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        var route = VehicleRealtimeRouteCatalog.GetRequired(sourceProfileCode);
        var vehicleRoute = ResolveVehicleRoute(route);
        var results = new List<TeacherVehicleProjectionDomainResult>();

        results.Add(await ProcessMappedDomainAsync(
            route, TeacherVehicleProjectionDomains.Teacher, cancellationToken));
        results.Add(await ProcessVehicleAsync(vehicleRoute, cancellationToken));
        results.Add(await ProcessMappedDomainAsync(
            route, TeacherVehicleProjectionDomains.CourseTeacher, cancellationToken));
        results.Add(await ProcessMappedDomainAsync(
            route, TeacherVehicleProjectionDomains.CourseVehicle, cancellationToken));

        return new(route.SourceProfileCode, results);
    }

    public async Task<TeacherVehicleProjectionCycleResult> BootstrapAsync(
        TeacherVehicleProjectionBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BootstrapId == Guid.Empty ||
            request.ArtifactSha256.Length != 64 ||
            !request.ArtifactSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("TVP_BOOTSTRAP_IDENTITY_INVALID");
        }

        var route = VehicleRealtimeRouteCatalog.GetRequired(request.SourceProfileCode);
        var vehicleRoute = ResolveVehicleRoute(route);
        await RequireBootstrapGateAsync(route.SourceProfileCode, cancellationToken);
        var results = new List<TeacherVehicleProjectionDomainResult>
        {
            await BootstrapMappedDomainAsync(
                route, TeacherVehicleProjectionDomains.Teacher,
                request.BootstrapId, request.ArtifactSha256, cancellationToken),
            await BootstrapVehicleAsync(
                vehicleRoute, request.BootstrapId, request.ArtifactSha256, cancellationToken),
            await BootstrapMappedDomainAsync(
                route, TeacherVehicleProjectionDomains.CourseTeacher,
                request.BootstrapId, request.ArtifactSha256, cancellationToken),
            await BootstrapMappedDomainAsync(
                route, TeacherVehicleProjectionDomains.CourseVehicle,
                request.BootstrapId, request.ArtifactSha256, cancellationToken),
        };
        return new(route.SourceProfileCode, results);
    }

    private async Task<TeacherVehicleProjectionDomainResult> ProcessMappedDomainAsync(
        VehicleRealtimeRoute route,
        string domain,
        CancellationToken cancellationToken)
    {
        var checkpoint = await ReadCheckpointAsync(
            route.SourceProfileCode, domain, cancellationToken)
            ?? throw new InvalidOperationException(
                $"TVP_CHECKPOINT_NOT_INITIALIZED:{route.SourceProfileCode}:{domain}");
        var batch = await ReadMappedBatchAsync(
            route, domain, checkpoint.LastCtVersion, bootstrap: false,
            cancellationToken);
        if (!string.Equals(checkpoint.MappingFingerprint, batch.MappingFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(checkpoint.SourceSchemaFingerprint, batch.SchemaFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            checkpoint.SourceDatabaseGuid != batch.SourceDatabaseGuid)
        {
            throw new InvalidOperationException(
                $"TVP_CHECKPOINT_CONTRACT_MISMATCH:{route.SourceProfileCode}:{domain}");
        }

        if (checkpoint.LastCtVersion == batch.AnchorVersion)
        {
            return NoChange(domain, checkpoint.LastCtVersion);
        }

        return await ApplyMappedBatchAsync(
            route, batch, checkpoint, bootstrap: false,
            Guid.NewGuid(), artifactSha256: null, cancellationToken);
    }

    private async Task<TeacherVehicleProjectionDomainResult> BootstrapMappedDomainAsync(
        VehicleRealtimeRoute route,
        string domain,
        Guid bootstrapId,
        string artifactSha256,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCheckpointAsync(
            route.SourceProfileCode, domain, cancellationToken);
        if (existing is not null)
        {
            return await ProcessMappedDomainAsync(route, domain, cancellationToken);
        }

        var batch = await ReadMappedBatchAsync(
            route, domain, checkpointVersion: null, bootstrap: true,
            cancellationToken);
        return await ApplyMappedBatchAsync(
            route, batch, checkpoint: null, bootstrap: true,
            bootstrapId, artifactSha256, cancellationToken);
    }

    private async Task<TeacherVehicleProjectionDomainResult> ProcessVehicleAsync(
        VehicleRealtimeRoute route,
        CancellationToken cancellationToken)
    {
        var before = await ReadVehicleCheckpointAsync(
            route.SourceProfileCode, cancellationToken)
            ?? throw new InvalidOperationException(
                $"TVP_VEHICLE_CHECKPOINT_NOT_INITIALIZED:{route.SourceProfileCode}");
        var inserted = 0;
        var updated = 0;
        var inactive = 0;
        var noChange = 0;
        var after = before.LastCtVersion;
        for (var cycle = 0; cycle < 512; cycle++)
        {
            var result = await _vehicleRealtime.ProcessAsync(route, cancellationToken);
            inserted += result.InsertedRows;
            updated += result.UpdatedRows;
            inactive += result.InactiveRows + result.MissingRows;
            noChange += result.NoChangeRows;
            after = result.CheckpointAfter;
            if (result.ManualReviewRows != 0)
            {
                throw new InvalidOperationException(
                    $"TVP_VEHICLE_MANUAL_REVIEW_REQUIRED:{route.SourceProfileCode}");
            }

            if (result.CheckpointAfter == result.CheckpointBefore &&
                result.InsertedRows + result.UpdatedRows + result.InactiveRows +
                result.MissingRows + result.ManualReviewRows + result.NoChangeRows == 0)
            {
                return new(
                    TeacherVehicleProjectionDomains.Vehicle,
                    before.LastCtVersion,
                    after,
                    inserted + updated + inactive + noChange,
                    inserted,
                    updated,
                    inactive,
                    noChange,
                    result.PlanToken,
                    inserted + updated + inactive == 0
                        ? "HEALTHY_NO_CHANGE"
                        : "HEALTHY");
            }
        }

        throw new InvalidOperationException(
            $"TVP_VEHICLE_BACKLOG_LIMIT_EXCEEDED:{route.SourceProfileCode}:{after}");
    }

    private async Task<TeacherVehicleProjectionDomainResult> BootstrapVehicleAsync(
        VehicleRealtimeRoute route,
        Guid bootstrapId,
        string artifactSha256,
        CancellationToken cancellationToken)
    {
        var existing = await ReadVehicleCheckpointAsync(
            route.SourceProfileCode, cancellationToken);
        if (existing is not null)
        {
            var current = await ReadVehicleBootstrapAsync(route, cancellationToken);
            if (existing.LastCtVersion == current.AnchorVersion &&
                existing.SourceDatabaseGuid == current.DatabaseGuid &&
                string.Equals(existing.SourceSchemaFingerprint,
                    current.SchemaFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return NoChange(
                    TeacherVehicleProjectionDomains.Vehicle,
                    existing.LastCtVersion);
            }
            return await ProcessVehicleAsync(route, cancellationToken);
        }

        var source = await ReadVehicleBootstrapAsync(route, cancellationToken);
        var inventory = await _vehicleFullTarget.ReadInventoryAsync(cancellationToken);
        var plan = VehicleFullConvergencePlanner.Build(
            bootstrapId,
            route,
            source.DatabaseGuid,
            source.AnchorVersion,
            source.SchemaFingerprint,
            source.Rows,
            inventory);
        if (plan.Rows.Any(row => row.RequiresManualReview))
        {
            throw new InvalidOperationException(
                $"TVP_VEHICLE_BOOTSTRAP_AMBIGUOUS:{route.SourceProfileCode}");
        }

        var committed = await _vehicleFullTarget.CommitAsync(
            plan, inventory, cancellationToken);
        var verified = await _vehicleFullTarget.ReadInventoryAsync(cancellationToken);
        var expectedHashes = plan.Rows
            .Where(row => row.Source is not null)
            .ToDictionary(
                row => row.SourceBienSoXe,
                row => row.Source!.SourceRowHash,
                StringComparer.OrdinalIgnoreCase);
        var exact = verified.Where(row =>
                string.Equals(row.SourceProfileCode, route.SourceProfileCode,
                    StringComparison.Ordinal))
            .ToArray();
        if (exact.Length != expectedHashes.Count || exact.Any(row =>
                row.SourceBienSoXe is null ||
                !expectedHashes.TryGetValue(row.SourceBienSoXe, out var hash) ||
                !string.Equals(row.SourceRowHash, hash, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"TVP_VEHICLE_BOOTSTRAP_VERIFY_FAILED:{route.SourceProfileCode}");
        }

        await PublishVehicleCheckpointAsync(
            route,
            source,
            plan.MappingFingerprint,
            plan.PlanToken,
            bootstrapId,
            artifactSha256,
            committed,
            cancellationToken);
        return new(
            TeacherVehicleProjectionDomains.Vehicle,
            source.AnchorVersion,
            source.AnchorVersion,
            committed.SourceRows,
            committed.InsertedRows,
            committed.UpdatedRows,
            committed.InactiveRows + committed.MissingRows,
            committed.NoChangeRows,
            plan.PlanToken,
            "BOOTSTRAP_VERIFIED");
    }

    private async Task<MappedBatch> ReadMappedBatchAsync(
        VehicleRealtimeRoute route,
        string domain,
        long? checkpointVersion,
        bool bootstrap,
        CancellationToken cancellationToken)
    {
        var resolved = await OpenSourceAsync(route, cancellationToken);
        await using var connection = resolved.Connection;
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Snapshot, cancellationToken);
        try
        {
            var capability = await ReadCapabilityAsync(
                connection, transaction, route, cancellationToken);
            var table = DomainTable(domain);
            var tableCapability = capability.Tables.Single(item => item.TableName == table);
            if (!bootstrap && checkpointVersion < tableCapability.MinimumValidVersion)
            {
                throw new InvalidOperationException(
                    $"TVP_CT_WINDOW_EXPIRED:{route.SourceProfileCode}:{domain}");
            }

            var schemaFingerprint = await ReadSchemaFingerprintAsync(
                connection, transaction, table, cancellationToken);
            var mappingFingerprint = MappingFingerprint(domain);
            object rows;
            IReadOnlyList<string> deletedKeys;
            switch (domain)
            {
                case TeacherVehicleProjectionDomains.Teacher:
                {
                    var sourceRows = await ReadTeacherRowsAsync(
                        connection, transaction, checkpointVersion, capability.CurrentVersion,
                        bootstrap, cancellationToken);
                    var mapped = sourceRows.CurrentRows.Select(row =>
                        QlhvImportCourseTeacherMapper.MapGiaoVien(
                            row, route.SourceProfileCode)).ToArray();
                    RequireSafe(mapped.SelectMany(item => item.Blockers), route, domain);
                    rows = mapped.Select(item => item.Model!).ToArray();
                    deletedKeys = sourceRows.DeletedKeys;
                    break;
                }
                case TeacherVehicleProjectionDomains.CourseTeacher:
                {
                    var sourceRows = await ReadCourseTeacherRowsAsync(
                        connection, transaction, checkpointVersion, capability.CurrentVersion,
                        bootstrap, cancellationToken);
                    var mapped = sourceRows.CurrentRows.Select(row =>
                        QlhvImportCourseTeacherMapper.MapRelation(
                            row, route.SourceProfileCode)).ToArray();
                    RequireSafe(mapped.SelectMany(item => item.Blockers), route, domain);
                    rows = mapped.Select(item => item.Model!).ToArray();
                    deletedKeys = sourceRows.DeletedKeys;
                    break;
                }
                case TeacherVehicleProjectionDomains.CourseVehicle:
                {
                    var sourceRows = await ReadCourseVehicleRowsAsync(
                        connection, transaction, checkpointVersion, capability.CurrentVersion,
                        bootstrap, cancellationToken);
                    var mapped = sourceRows.CurrentRows.Select(row =>
                        QlhvKhoaHocXeTapMapper.Map(row, route.SourceProfileCode)).ToArray();
                    RequireSafe(mapped.SelectMany(item => item.Blockers), route, domain);
                    rows = mapped.Select(item => item.Model!).ToArray();
                    deletedKeys = sourceRows.DeletedKeys;
                    break;
                }
                default:
                    throw new InvalidOperationException($"TVP_DOMAIN_UNSUPPORTED:{domain}");
            }

            await transaction.CommitAsync(cancellationToken);
            return new(
                domain,
                capability.DatabaseGuid,
                capability.CurrentVersion,
                schemaFingerprint,
                mappingFingerprint,
                rows,
                deletedKeys);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<TeacherVehicleProjectionDomainResult> ApplyMappedBatchAsync(
        VehicleRealtimeRoute route,
        MappedBatch batch,
        ProjectionCheckpoint? checkpoint,
        bool bootstrap,
        Guid cycleId,
        string? artifactSha256,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(target);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var lockResult = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                AcquireDomainLockSql,
                new { route.SourceProfileCode, batch.Domain },
                transaction,
                _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            if (lockResult < 0)
                throw new InvalidOperationException("TVP_DOMAIN_LOCK_UNAVAILABLE");

            var current = await ReadLockedCheckpointAsync(
                connection, transaction, route.SourceProfileCode, batch.Domain,
                cancellationToken);
            if (bootstrap)
            {
                if (current is not null)
                    throw new InvalidOperationException("TVP_BOOTSTRAP_CHECKPOINT_RACE");
            }
            else if (current is null || checkpoint is null ||
                     current.LastCtVersion != checkpoint.LastCtVersion ||
                     !current.RowVersion.SequenceEqual(checkpoint.RowVersion))
            {
                throw new InvalidOperationException("TVP_CHECKPOINT_CAS_FAILED");
            }

            var actions = batch.Domain switch
            {
                TeacherVehicleProjectionDomains.Teacher =>
                    await ApplyTeacherAsync(connection, transaction, route, batch,
                        bootstrap, cancellationToken),
                TeacherVehicleProjectionDomains.CourseTeacher =>
                    await ApplyCourseTeacherAsync(connection, transaction, route, batch,
                        bootstrap, cancellationToken),
                TeacherVehicleProjectionDomains.CourseVehicle =>
                    await ApplyCourseVehicleAsync(connection, transaction, route, batch,
                        bootstrap, cancellationToken),
                _ => throw new InvalidOperationException("TVP_DOMAIN_APPLY_UNSUPPORTED"),
            };
            var verificationHash = await VerifyDomainAsync(
                connection, transaction, route, batch, cancellationToken);
            await PublishProjectionCheckpointAsync(
                connection,
                transaction,
                route,
                batch,
                checkpoint,
                cycleId,
                artifactSha256,
                actions,
                verificationHash,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new(
                batch.Domain,
                checkpoint?.LastCtVersion ?? batch.AnchorVersion,
                batch.AnchorVersion,
                batch.SourceCount,
                actions.Count(value => value == "INSERT"),
                actions.Count(value => value is "UPDATE" or "REACTIVATE"),
                actions.Count(value => value == "SOFT_DELETE"),
                Math.Max(0, batch.SourceCount - actions.Count(value =>
                    value is "INSERT" or "UPDATE" or "REACTIVATE")),
                verificationHash,
                bootstrap ? "BOOTSTRAP_VERIFIED" :
                actions.Count == 0 ? "HEALTHY_NO_CHANGE" : "HEALTHY");
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<IReadOnlyList<string>> ApplyTeacherAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleRealtimeRoute route,
        MappedBatch batch,
        bool bootstrap,
        CancellationToken cancellationToken)
    {
        var rows = (IReadOnlyList<QlhvImportGiaoVienWriteModel>)batch.Rows;
        await CreateAndCopyAsync(
            connection, transaction,
            QlhvCourseTeacherFullSnapshotSyncSql.CreateGiaoVienStagingTable,
            QlhvCourseTeacherFullSnapshotSyncSql.GiaoVienStagingTableName,
            rows, cancellationToken);
        var guard = await connection.QuerySingleAsync<IncrementalGuard>(new CommandDefinition(
            TeacherGuardSql,
            new { route.SourceProfileCode },
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (guard.HasConflicts)
            throw new InvalidOperationException("TVP_TEACHER_TARGET_CONFLICT");
        var actions = (await connection.QueryAsync<string>(new CommandDefinition(
            QlhvCourseTeacherFullSnapshotSyncSql.MergeGiaoVien,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToList();
        actions.AddRange(await SoftDeleteKeysAsync(
            connection, transaction, TeacherDeleteSql,
            route.SourceProfileCode, batch.DeletedKeys, bootstrap,
            QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteGiaoVien,
            cancellationToken));
        return actions;
    }

    private async Task<IReadOnlyList<string>> ApplyCourseTeacherAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleRealtimeRoute route,
        MappedBatch batch,
        bool bootstrap,
        CancellationToken cancellationToken)
    {
        var rows = (IReadOnlyList<QlhvImportKhoaHocGiaoVienWriteModel>)batch.Rows;
        await CreateAndCopyAsync(
            connection, transaction,
            QlhvCourseTeacherFullSnapshotSyncSql.CreateRelationStagingTable,
            QlhvCourseTeacherFullSnapshotSyncSql.RelationStagingTableName,
            rows, cancellationToken);
        var guard = await connection.QuerySingleAsync<IncrementalGuard>(new CommandDefinition(
            CourseTeacherGuardSql,
            new { route.SourceProfileCode },
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (guard.HasConflicts)
            throw new InvalidOperationException("TVP_COURSE_TEACHER_TARGET_CONFLICT");
        var actions = (await connection.QueryAsync<string>(new CommandDefinition(
            QlhvCourseTeacherFullSnapshotSyncSql.MergeRelation,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToList();
        actions.AddRange(await SoftDeleteKeysAsync(
            connection, transaction, CourseTeacherDeleteSql,
            route.SourceProfileCode, batch.DeletedKeys, bootstrap,
            QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteRelation,
            cancellationToken));
        return actions;
    }

    private async Task<IReadOnlyList<string>> ApplyCourseVehicleAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleRealtimeRoute route,
        MappedBatch batch,
        bool bootstrap,
        CancellationToken cancellationToken)
    {
        var rows = (IReadOnlyList<QlhvKhoaHocXeTapWriteModel>)batch.Rows;
        await CreateAndCopyAsync(
            connection, transaction,
            CreateCourseVehicleStagingSql,
            "#TVP_CourseVehicle",
            rows, cancellationToken);
        var guard = await connection.QuerySingleAsync<IncrementalGuard>(new CommandDefinition(
            CourseVehicleGuardSql,
            new { route.SourceProfileCode },
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (guard.HasConflicts)
            throw new InvalidOperationException("TVP_COURSE_VEHICLE_TARGET_CONFLICT");
        var actions = (await connection.QueryAsync<string>(new CommandDefinition(
            MergeCourseVehicleSql,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToList();
        actions.AddRange(await SoftDeleteKeysAsync(
            connection, transaction, CourseVehicleDeleteSql,
            route.SourceProfileCode, batch.DeletedKeys, bootstrap,
            SoftDeleteCourseVehicleSnapshotSql,
            cancellationToken));
        return actions;
    }

    private async Task<string> VerifyDomainAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleRealtimeRoute route,
        MappedBatch batch,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<VerificationRow>(new CommandDefinition(
            batch.Domain switch
            {
                TeacherVehicleProjectionDomains.Teacher => VerifyTeacherSql,
                TeacherVehicleProjectionDomains.CourseTeacher => VerifyCourseTeacherSql,
                TeacherVehicleProjectionDomains.CourseVehicle => VerifyCourseVehicleSql,
                _ => throw new InvalidOperationException("TVP_VERIFY_DOMAIN_UNSUPPORTED"),
            },
            new { route.SourceProfileCode },
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (row.DuplicateGroups != 0 || row.MismatchRows != 0)
            throw new InvalidOperationException($"TVP_VERIFY_FAILED:{batch.Domain}");
        return HashText($"{route.SourceProfileCode}|{batch.Domain}|{batch.AnchorVersion}|{row.ExactRows}|{row.HashAggregate}");
    }

    private async Task PublishProjectionCheckpointAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleRealtimeRoute route,
        MappedBatch batch,
        ProjectionCheckpoint? checkpoint,
        Guid cycleId,
        string? artifactSha256,
        IReadOnlyCollection<string> actions,
        string verificationHash,
        CancellationToken cancellationToken)
    {
        var args = new
        {
            CycleId = cycleId,
            route.SourceProfileCode,
            batch.Domain,
            ContractVersion = TeacherVehicleProjectionDomains.ContractVersion,
            batch.SourceDatabaseGuid,
            FromVersion = checkpoint?.LastCtVersion ?? batch.AnchorVersion,
            ToVersion = batch.AnchorVersion,
            batch.MappingFingerprint,
            SourceSchemaFingerprint = batch.SchemaFingerprint,
            batch.SourceCount,
            InsertedRows = actions.Count(value => value == "INSERT"),
            UpdatedRows = actions.Count(value => value is "UPDATE" or "REACTIVATE"),
            InactiveRows = actions.Count(value => value == "SOFT_DELETE"),
            NoChangeRows = Math.Max(0, batch.SourceCount - actions.Count(value =>
                value is "INSERT" or "UPDATE" or "REACTIVATE")),
            VerificationHash = verificationHash,
            ArtifactSha256 = artifactSha256,
        };
        await connection.ExecuteAsync(new CommandDefinition(
            checkpoint is null ? InsertCheckpointAndCycleSql : UpdateCheckpointAndCycleSql,
            args,
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private async Task PublishVehicleCheckpointAsync(
        VehicleRealtimeRoute route,
        VehicleBootstrapSource source,
        string mappingFingerprint,
        string verificationHash,
        Guid cycleId,
        string artifactSha256,
        VehicleFullConvergenceResult committed,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(target);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM dbo.App_XeTap_RealtimeCheckpoint WITH(UPDLOCK,HOLDLOCK) WHERE SourceProfileCode=@SourceProfileCode;",
                new { route.SourceProfileCode }, transaction,
                _options.TimeoutSeconds, cancellationToken: cancellationToken));
            if (count != 0)
                throw new InvalidOperationException("TVP_VEHICLE_CHECKPOINT_RACE");
            await connection.ExecuteAsync(new CommandDefinition(
                InsertVehicleCheckpointAndCycleSql,
                new
                {
                    CycleId = cycleId,
                    route.SourceProfileCode,
                    Domain = TeacherVehicleProjectionDomains.Vehicle,
                    ContractVersion = TeacherVehicleProjectionDomains.ContractVersion,
                    SourceDatabaseGuid = source.DatabaseGuid,
                    AnchorVersion = source.AnchorVersion,
                    MappingFingerprint = mappingFingerprint,
                    SourceSchemaFingerprint = source.SchemaFingerprint,
                    committed.SourceRows,
                    committed.InsertedRows,
                    committed.UpdatedRows,
                    InactiveRows = committed.InactiveRows + committed.MissingRows,
                    committed.NoChangeRows,
                    VerificationHash = verificationHash,
                    ArtifactSha256 = artifactSha256,
                },
                transaction,
                _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task RequireBootstrapGateAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(target);
        await connection.OpenAsync(cancellationToken);
        var gate = await connection.QuerySingleAsync<BootstrapGate>(new CommandDefinition(
            BootstrapGateSql,
            new { SourceProfileCode = sourceProfileCode },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (!string.Equals(gate.MasterState, "OFF", StringComparison.Ordinal) ||
            gate.AutoSyncActive != 0 || gate.WriterCount != 0)
        {
            throw new InvalidOperationException("TVP_BOOTSTRAP_GATE_REJECTED");
        }
    }

    private async Task<VehicleBootstrapSource> ReadVehicleBootstrapAsync(
        VehicleRealtimeRoute route,
        CancellationToken cancellationToken)
    {
        var resolved = await OpenSourceAsync(route, cancellationToken);
        await using var connection = resolved.Connection;
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Snapshot, cancellationToken);
        try
        {
            var capability = await ReadCapabilityAsync(
                connection, transaction, route, cancellationToken);
            var schema = await ReadVehicleSchemaFingerprintAsync(
                connection, transaction, cancellationToken);
            var rows = (await connection.QueryAsync<VehicleSourceRow>(new CommandDefinition(
                VehicleBootstrapRowsSql,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
            await transaction.CommitAsync(cancellationToken);
            return new(capability.DatabaseGuid, capability.CurrentVersion, schema, rows);
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ResolvedSource> OpenSourceAsync(
        VehicleRealtimeRoute route,
        CancellationToken cancellationToken)
    {
        var profile = await _sourceConnections.ResolveAsync(
            route.SourceProfileCode, route.SourceDatabaseName, cancellationToken);
        var connection = new SqlConnection(profile.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return new(connection);
    }

    private async Task<string> ResolveTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _targetConnections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
            throw new InvalidOperationException("TVP_TARGET_CONNECTION_UNAVAILABLE");
        return target.ConnectionString;
    }

    private async Task<SourceCapability> ReadCapabilityAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        VehicleRealtimeRoute route,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<CapabilityRow>(new CommandDefinition(
            CapabilitySql,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToArray();
        if (rows.Length != 4 || rows.Any(row => !row.ChangeTrackingEnabled ||
                                                !row.TrackColumnsUpdated ||
                                                row.MinimumValidVersion is null) ||
            rows.Any(row => row.DatabaseGuid != ExpectedDatabaseGuid(connection, route) ||
                            !string.Equals(row.DatabaseName, route.SourceDatabaseName,
                                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"TVP_SOURCE_CAPABILITY_REJECTED:{route.SourceProfileCode}");
        }
        return new(
            rows[0].DatabaseGuid,
            rows[0].CurrentVersion,
            rows.Select(row => new TableCapability(
                row.TableName, row.MinimumValidVersion!.Value)).ToArray());
    }

    private Guid ExpectedDatabaseGuid(
        SqlConnection connection,
        VehicleRealtimeRoute route)
    {
        if (!string.Equals(
                connection.DataSource.Trim(),
                @"CSDLTTTC\QLHVRT02",
                StringComparison.OrdinalIgnoreCase) ||
            !_configuration.GetValue<bool>(
                "TeacherVehicleProjection:DisposableRehearsalEnabled"))
        {
            return route.ExpectedProductionDatabaseGuid;
        }

        var configured = _configuration[
            $"TeacherVehicleProjection:DisposableSourceDatabaseGuids:{route.SourceProfileCode}"];
        if (!Guid.TryParse(configured, out var value) || value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"TVP_DISPOSABLE_DATABASE_GUID_MISSING:{route.SourceProfileCode}");
        }

        return value;
    }

    private VehicleRealtimeRoute ResolveVehicleRoute(VehicleRealtimeRoute route)
    {
        if (!IsDisposableTargetConfigured())
            return route;

        var configured = _configuration[
            $"TeacherVehicleProjection:DisposableSourceDatabaseGuids:{route.SourceProfileCode}"];
        if (!Guid.TryParse(configured, out var value) || value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"TVP_DISPOSABLE_DATABASE_GUID_MISSING:{route.SourceProfileCode}");
        }
        return route with { ExpectedProductionDatabaseGuid = value };
    }

    private bool IsDisposableTargetConfigured()
    {
        if (!_configuration.GetValue<bool>(
                "TeacherVehicleProjection:DisposableRehearsalEnabled"))
            return false;
        var connectionString = _configuration.GetConnectionString("QLHV_APP");
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;
        try
        {
            return string.Equals(
                new SqlConnectionStringBuilder(connectionString).DataSource.Trim(),
                @"CSDLTTTC\QLHVRT02",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<string> ReadSchemaFingerprintAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<SchemaRow>(new CommandDefinition(
            SchemaFingerprintSql,
            new { TableName = tableName },
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToArray();
        if (rows.Length == 0)
            throw new InvalidOperationException($"TVP_SOURCE_TABLE_MISSING:{tableName}");
        return HashText(string.Join("|", rows.OrderBy(row => row.ColumnId).Select(row =>
            string.Join(":", row.ColumnId, row.ColumnName, row.TypeName,
                row.MaxLength, row.IsNullable ? 1 : 0, row.PrimaryKeyOrdinal ?? 0))));
    }

    private async Task<string> ReadVehicleSchemaFingerprintAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<VehicleSchemaRow>(new CommandDefinition(
            SqlVehicleRealtimeSourceFeed.SourceMetadataSql,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).OrderBy(row => row.ColumnId).ToArray();
        if (rows.Length == 0)
            throw new InvalidOperationException("TVP_SOURCE_TABLE_MISSING:XeTap");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows)
        {
            Append(row.ColumnId.ToString(CultureInfo.InvariantCulture));
            Append(row.Name);
            Append(row.SqlType);
            Append(row.MaxLength.ToString(CultureInfo.InvariantCulture));
            Append(row.Precision.ToString(CultureInfo.InvariantCulture));
            Append(row.Scale.ToString(CultureInfo.InvariantCulture));
            Append(row.IsNullable ? "1" : "0");
            Append(row.CollationName);
            Append(row.PrimaryKeyOrdinal?.ToString(CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Append(string? value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
    }

    private async Task<IReadOnlyDictionary<string, long>> ReadCheckpointsAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(target);
        await connection.OpenAsync(cancellationToken);
        var generic = (await connection.QueryAsync<ProjectionCheckpoint>(new CommandDefinition(
            ReadAllProjectionCheckpointsSql,
            new
            {
                SourceProfileCode = sourceProfileCode,
                ContractVersion = TeacherVehicleProjectionDomains.ContractVersion,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToArray();
        var vehicle = await connection.QuerySingleOrDefaultAsync<VehicleCheckpointRow>(
            new CommandDefinition(
                "SELECT SourceProfileCode,LastCtVersion,SourceDatabaseGuid,MappingFingerprint,SourceSchemaFingerprint,State,RowVersion FROM dbo.App_XeTap_RealtimeCheckpoint WHERE SourceProfileCode=@SourceProfileCode;",
                new { SourceProfileCode = sourceProfileCode },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
        var map = generic.ToDictionary(row => row.DomainName, row => row.LastCtVersion,
            StringComparer.Ordinal);
        if (!map.ContainsKey(TeacherVehicleProjectionDomains.Teacher) ||
            !map.ContainsKey(TeacherVehicleProjectionDomains.CourseTeacher) ||
            !map.ContainsKey(TeacherVehicleProjectionDomains.CourseVehicle) ||
            vehicle is null)
        {
            throw new InvalidOperationException(
                $"TVP_CHECKPOINT_SET_INCOMPLETE:{sourceProfileCode}");
        }
        map[TeacherVehicleProjectionDomains.Vehicle] = vehicle.LastCtVersion;
        return map;
    }

    private async Task<ProjectionCheckpoint?> ReadCheckpointAsync(
        string sourceProfileCode,
        string domain,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(target);
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ProjectionCheckpoint>(
            new CommandDefinition(
                ReadProjectionCheckpointSql,
                new
                {
                    SourceProfileCode = sourceProfileCode,
                    DomainName = domain,
                    ContractVersion = TeacherVehicleProjectionDomains.ContractVersion,
                },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
    }

    private static async Task<ProjectionCheckpoint?> ReadLockedCheckpointAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sourceProfileCode,
        string domain,
        CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<ProjectionCheckpoint>(
            new CommandDefinition(
                ReadLockedProjectionCheckpointSql,
                new
                {
                    SourceProfileCode = sourceProfileCode,
                    DomainName = domain,
                    ContractVersion = TeacherVehicleProjectionDomains.ContractVersion,
                },
                transaction,
                cancellationToken: cancellationToken));

    private async Task<VehicleCheckpointRow?> ReadVehicleCheckpointAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(target);
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<VehicleCheckpointRow>(
            new CommandDefinition(
                "SELECT SourceProfileCode,LastCtVersion,SourceDatabaseGuid,MappingFingerprint,SourceSchemaFingerprint,State,RowVersion FROM dbo.App_XeTap_RealtimeCheckpoint WHERE SourceProfileCode=@SourceProfileCode;",
                new { SourceProfileCode = sourceProfileCode },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
    }

    private async Task CreateAndCopyAsync<T>(
        SqlConnection connection,
        SqlTransaction transaction,
        string createSql,
        string tableName,
        IReadOnlyList<T> rows,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            createSql,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (rows.Count == 0) return;
        var properties = typeof(T).GetProperties();
        using var table = new DataTable();
        foreach (var property in properties)
            table.Columns.Add(property.Name,
                Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        foreach (var row in rows)
            table.Rows.Add(properties.Select(property =>
                property.GetValue(row) ?? DBNull.Value).ToArray());
        using var bulk = new SqlBulkCopy(
            connection, SqlBulkCopyOptions.CheckConstraints, transaction)
        {
            DestinationTableName = tableName,
            BatchSize = Math.Max(1, _options.BatchSize),
            BulkCopyTimeout = _options.TimeoutSeconds,
        };
        foreach (DataColumn column in table.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> SoftDeleteKeysAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string incrementalDeleteSql,
        string sourceProfileCode,
        IReadOnlyList<string> deletedKeys,
        bool bootstrap,
        string bootstrapDeleteSql,
        CancellationToken cancellationToken)
    {
        if (bootstrap)
            return (await connection.QueryAsync<string>(new CommandDefinition(
                bootstrapDeleteSql,
                new { SourceProfileCode = sourceProfileCode },
                transaction,
                _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
        if (deletedKeys.Count == 0) return Array.Empty<string>();
        return (await connection.QueryAsync<string>(new CommandDefinition(
            incrementalDeleteSql,
            new { SourceProfileCode = sourceProfileCode, DeletedKeys = deletedKeys },
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToArray();
    }

    private static async Task<SourceRows<QlhvGiaoVienSourceRow>> ReadTeacherRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long? checkpoint,
        long anchor,
        bool bootstrap,
        CancellationToken cancellationToken)
    {
        if (bootstrap)
            return new((await connection.QueryAsync<QlhvGiaoVienSourceRow>(
                new CommandDefinition(TeacherRowsSql, transaction: transaction,
                    cancellationToken: cancellationToken))).ToArray(), []);
        var rows = (await connection.QueryAsync<TeacherChangeRow>(new CommandDefinition(
            TeacherChangesSql,
            new { CheckpointVersion = checkpoint, AnchorVersion = anchor },
            transaction,
            cancellationToken: cancellationToken))).ToArray();
        return new(
            rows.Where(row => row.CurrentExists).Select(row => row.ToSource()).ToArray(),
            rows.Where(row => !row.CurrentExists).Select(row => row.SourceKey).ToArray());
    }

    private static async Task<SourceRows<QlhvKhoaHocGiaoVienSourceRow>> ReadCourseTeacherRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long? checkpoint,
        long anchor,
        bool bootstrap,
        CancellationToken cancellationToken)
    {
        if (bootstrap)
            return new((await connection.QueryAsync<QlhvKhoaHocGiaoVienSourceRow>(
                new CommandDefinition(CourseTeacherRowsSql, transaction: transaction,
                    cancellationToken: cancellationToken))).ToArray(), []);
        var rows = (await connection.QueryAsync<CourseTeacherChangeRow>(new CommandDefinition(
            CourseTeacherChangesSql,
            new { CheckpointVersion = checkpoint, AnchorVersion = anchor },
            transaction,
            cancellationToken: cancellationToken))).ToArray();
        return new(
            rows.Where(row => row.CurrentExists).Select(row => row.ToSource()).ToArray(),
            rows.Where(row => !row.CurrentExists).Select(row => row.SourceKey).ToArray());
    }

    private static async Task<SourceRows<QlhvKhoaHocXeTapSourceRow>> ReadCourseVehicleRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long? checkpoint,
        long anchor,
        bool bootstrap,
        CancellationToken cancellationToken)
    {
        if (bootstrap)
            return new((await connection.QueryAsync<QlhvKhoaHocXeTapSourceRow>(
                new CommandDefinition(CourseVehicleRowsSql, transaction: transaction,
                    cancellationToken: cancellationToken))).ToArray(), []);
        var rows = (await connection.QueryAsync<CourseVehicleChangeRow>(new CommandDefinition(
            CourseVehicleChangesSql,
            new { CheckpointVersion = checkpoint, AnchorVersion = anchor },
            transaction,
            cancellationToken: cancellationToken))).ToArray();
        return new(
            rows.Where(row => row.CurrentExists).Select(row => row.ToSource()).ToArray(),
            rows.Where(row => !row.CurrentExists).Select(row => row.SourceKey).ToArray());
    }

    private static void RequireSafe(
        IEnumerable<string> blockers,
        VehicleRealtimeRoute route,
        string domain)
    {
        var values = blockers.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (values.Length != 0)
            throw new InvalidOperationException(
                $"TVP_MAPPING_BLOCKED:{route.SourceProfileCode}:{domain}:{values[0]}");
    }

    private static string DomainTable(string domain) => domain switch
    {
        TeacherVehicleProjectionDomains.Teacher => "GiaoVien",
        TeacherVehicleProjectionDomains.CourseTeacher => "KhoaHoc_GiaoVien",
        TeacherVehicleProjectionDomains.CourseVehicle => "KhoaHoc_XeTap",
        TeacherVehicleProjectionDomains.Vehicle => "XeTap",
        _ => throw new InvalidOperationException($"TVP_DOMAIN_UNSUPPORTED:{domain}"),
    };

    private static string MappingFingerprint(string domain) => HashText(domain switch
    {
        TeacherVehicleProjectionDomains.Teacher =>
            "TVP_V1|GiaoVien|profile+MaGV|QlhvImportCourseTeacherMapper",
        TeacherVehicleProjectionDomains.CourseTeacher =>
            "TVP_V1|KhoaHoc_GiaoVien|profile+MaLichLV|QlhvImportCourseTeacherMapper",
        TeacherVehicleProjectionDomains.CourseVehicle =>
            QlhvKhoaHocXeTapMapper.MappingFingerprint(),
        _ => throw new InvalidOperationException($"TVP_MAPPING_DOMAIN_UNSUPPORTED:{domain}"),
    });

    private static string HashText(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static TeacherVehicleProjectionDomainResult NoChange(
        string domain,
        long checkpoint) => new(
            domain, checkpoint, checkpoint, 0, 0, 0, 0, 0,
            HashText($"{domain}|{checkpoint}|NO_CHANGE"), "HEALTHY_NO_CHANGE");

    private sealed record ResolvedSource(SqlConnection Connection);
    private sealed record SourceRows<T>(IReadOnlyList<T> CurrentRows, IReadOnlyList<string> DeletedKeys);
    private sealed record SourceCapability(Guid DatabaseGuid, long CurrentVersion, IReadOnlyList<TableCapability> Tables);
    private sealed record TableCapability(string TableName, long MinimumValidVersion);
    private sealed record MappedBatch(
        string Domain,
        Guid SourceDatabaseGuid,
        long AnchorVersion,
        string SchemaFingerprint,
        string MappingFingerprint,
        object Rows,
        IReadOnlyList<string> DeletedKeys)
    {
        public int SourceCount => Rows switch
        {
            IReadOnlyCollection<QlhvImportGiaoVienWriteModel> rows => rows.Count,
            IReadOnlyCollection<QlhvImportKhoaHocGiaoVienWriteModel> rows => rows.Count,
            IReadOnlyCollection<QlhvKhoaHocXeTapWriteModel> rows => rows.Count,
            _ => 0,
        };
    }

    private sealed record VehicleBootstrapSource(
        Guid DatabaseGuid,
        long AnchorVersion,
        string SchemaFingerprint,
        IReadOnlyList<VehicleSourceRow> Rows);

    private sealed class ProjectionCheckpoint
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public string DomainName { get; init; } = string.Empty;
        public string ContractVersion { get; init; } = string.Empty;
        public Guid SourceDatabaseGuid { get; init; }
        public long LastCtVersion { get; init; }
        public string MappingFingerprint { get; init; } = string.Empty;
        public string SourceSchemaFingerprint { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public byte[] RowVersion { get; init; } = [];
    }

    private sealed class VehicleCheckpointRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public Guid SourceDatabaseGuid { get; init; }
        public long LastCtVersion { get; init; }
        public string MappingFingerprint { get; init; } = string.Empty;
        public string SourceSchemaFingerprint { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public byte[] RowVersion { get; init; } = [];
    }

    private sealed class CapabilityRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public Guid DatabaseGuid { get; init; }
        public long CurrentVersion { get; init; }
        public string TableName { get; init; } = string.Empty;
        public bool ChangeTrackingEnabled { get; init; }
        public bool TrackColumnsUpdated { get; init; }
        public long? MinimumValidVersion { get; init; }
    }

    private sealed class SchemaRow
    {
        public int ColumnId { get; init; }
        public string ColumnName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public short MaxLength { get; init; }
        public bool IsNullable { get; init; }
        public int? PrimaryKeyOrdinal { get; init; }
    }

    private sealed class VehicleSchemaRow
    {
        public int ColumnId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string SqlType { get; init; } = string.Empty;
        public short MaxLength { get; init; }
        public byte Precision { get; init; }
        public byte Scale { get; init; }
        public bool IsNullable { get; init; }
        public string? CollationName { get; init; }
        public int? PrimaryKeyOrdinal { get; init; }
    }

    private sealed class IncrementalGuard
    {
        public int DuplicateGroups { get; init; }
        public int NaturalKeyConflicts { get; init; }
        public int RelationConflicts { get; init; }
        public bool HasConflicts => DuplicateGroups != 0 || NaturalKeyConflicts != 0 || RelationConflicts != 0;
    }

    private sealed class VerificationRow
    {
        public long ExactRows { get; init; }
        public long DuplicateGroups { get; init; }
        public long MismatchRows { get; init; }
        public long HashAggregate { get; init; }
    }

    private sealed class BootstrapGate
    {
        public string MasterState { get; init; } = string.Empty;
        public int AutoSyncActive { get; init; }
        public int WriterCount { get; init; }
    }

    private sealed class TeacherChangeRow : QlhvGiaoVienSourceRow
    {
        public string SourceKey { get; init; } = string.Empty;
        public bool CurrentExists { get; init; }
        public QlhvGiaoVienSourceRow ToSource() => this;
    }

    private sealed class CourseTeacherChangeRow : QlhvKhoaHocGiaoVienSourceRow
    {
        public string SourceKey { get; init; } = string.Empty;
        public bool CurrentExists { get; init; }
        public QlhvKhoaHocGiaoVienSourceRow ToSource() => this;
    }

    private sealed record CourseVehicleChangeRow : QlhvKhoaHocXeTapSourceRow
    {
        public string SourceKey { get; init; } = string.Empty;
        public bool CurrentExists { get; init; }
        public QlhvKhoaHocXeTapSourceRow ToSource() => this;
    }

    private const string CapabilitySql = """
        SELECT DB_NAME() DatabaseName, recovery.database_guid DatabaseGuid,
               CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) CurrentVersion,
               tables.TableName,
               CONVERT(bit,CASE WHEN ct.object_id IS NULL THEN 0 ELSE 1 END) ChangeTrackingEnabled,
               CONVERT(bit,ISNULL(ct.is_track_columns_updated_on,0)) TrackColumnsUpdated,
               CONVERT(bigint,CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.'+tables.TableName,N'U'))) MinimumValidVersion
        FROM sys.database_recovery_status recovery
        CROSS JOIN (VALUES(N'GiaoVien'),(N'XeTap'),(N'KhoaHoc_GiaoVien'),(N'KhoaHoc_XeTap')) tables(TableName)
        LEFT JOIN sys.change_tracking_tables ct ON ct.object_id=OBJECT_ID(N'dbo.'+tables.TableName,N'U')
        WHERE recovery.database_id=DB_ID();
        """;

    private const string SchemaFingerprintSql = """
        SELECT c.column_id ColumnId,c.name ColumnName,t.name TypeName,c.max_length MaxLength,
               c.is_nullable IsNullable,ic.key_ordinal PrimaryKeyOrdinal
        FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        LEFT JOIN sys.indexes i ON i.object_id=c.object_id AND i.is_primary_key=1
        LEFT JOIN sys.index_columns ic ON ic.object_id=c.object_id AND ic.index_id=i.index_id AND ic.column_id=c.column_id
        WHERE c.object_id=OBJECT_ID(N'dbo.'+@TableName,N'U') ORDER BY c.column_id;
        """;

    private const string TeacherRowsSql = """
        SELECT MaGV,MaSoGTVT,MaCSDT,HoTenDem,TenGV,NgaySinh,AnhCD,SoCMT,NoiCT,
               NoiCT_MaDVHC,NoiCT_MaDVQL,GioiTinh,DienThoai,HinhThuc_TuyenDung,
               TrinhDo_VanHoa,TrinhDo_ChuyenMon,TrinhDo_SuPham,HangGPLX,NgayCapGPLX,
               ThamNien_LaiXe,SoQD_GCN,NgayQD_GCN,LoaiHinh_DaoTao,GhiChu,TrangThai,
               CacHangGPLXDuocDT,CauTaoSuaChua,DaoDucLaixe,NghiepVuVanTai,LuatGTDB,
               KyThuatLaixe,MaFileTiepNhanXML,ThoiGianTiepNhanXML,NgayHHGPLX,
               NoiCapGCN,CacMonHoc,LoaiGiaoVien,CacHangDaCo FROM dbo.GiaoVien ORDER BY MaGV;
        """;

    private const string TeacherChangesSql = """
        ;WITH keys AS(
          SELECT CONVERT(nvarchar(40),ct.MaGV) SourceKey,MAX(ct.SYS_CHANGE_VERSION) LastVersion
          FROM CHANGETABLE(CHANGES dbo.GiaoVien,@CheckpointVersion) AS ct
          WHERE ct.SYS_CHANGE_VERSION<=@AnchorVersion GROUP BY ct.MaGV)
        SELECT keys.SourceKey,CONVERT(bit,CASE WHEN gv.MaGV IS NULL THEN 0 ELSE 1 END) CurrentExists,
               gv.MaGV,gv.MaSoGTVT,gv.MaCSDT,gv.HoTenDem,gv.TenGV,gv.NgaySinh,gv.AnhCD,
               gv.SoCMT,gv.NoiCT,gv.NoiCT_MaDVHC,gv.NoiCT_MaDVQL,gv.GioiTinh,gv.DienThoai,
               gv.HinhThuc_TuyenDung,gv.TrinhDo_VanHoa,gv.TrinhDo_ChuyenMon,gv.TrinhDo_SuPham,
               gv.HangGPLX,gv.NgayCapGPLX,gv.ThamNien_LaiXe,gv.SoQD_GCN,gv.NgayQD_GCN,
               gv.LoaiHinh_DaoTao,gv.GhiChu,gv.TrangThai,gv.CacHangGPLXDuocDT,
               gv.CauTaoSuaChua,gv.DaoDucLaixe,gv.NghiepVuVanTai,gv.LuatGTDB,gv.KyThuatLaixe,
               gv.MaFileTiepNhanXML,gv.ThoiGianTiepNhanXML,gv.NgayHHGPLX,gv.NoiCapGCN,
               gv.CacMonHoc,gv.LoaiGiaoVien,gv.CacHangDaCo
        FROM keys LEFT JOIN dbo.GiaoVien gv ON gv.MaGV=keys.SourceKey ORDER BY keys.SourceKey;
        """;

    private const string CourseTeacherRowsSql = """
        SELECT MaLichLV,MaKH,MaGV,TenGV,BienSoXe,LoaiGV,SoHV,NgayHL,NgayHetHL,GhiChu,
               TrangThai,NgayBD,NgayKT,IsKhoaHocGiaoVien,MaMonHoc,TenMonHoc
        FROM dbo.KhoaHoc_GiaoVien ORDER BY MaLichLV;
        """;

    private const string CourseTeacherChangesSql = """
        ;WITH keys AS(
          SELECT CONVERT(nvarchar(40),ct.MaLichLV) SourceKey,ct.MaLichLV,MAX(ct.SYS_CHANGE_VERSION) LastVersion
          FROM CHANGETABLE(CHANGES dbo.KhoaHoc_GiaoVien,@CheckpointVersion) AS ct
          WHERE ct.SYS_CHANGE_VERSION<=@AnchorVersion GROUP BY ct.MaLichLV)
        SELECT keys.SourceKey,CONVERT(bit,CASE WHEN r.MaLichLV IS NULL THEN 0 ELSE 1 END) CurrentExists,
               r.MaLichLV,r.MaKH,r.MaGV,r.TenGV,r.BienSoXe,r.LoaiGV,r.SoHV,r.NgayHL,
               r.NgayHetHL,r.GhiChu,r.TrangThai,r.NgayBD,r.NgayKT,r.IsKhoaHocGiaoVien,
               r.MaMonHoc,r.TenMonHoc
        FROM keys LEFT JOIN dbo.KhoaHoc_GiaoVien r ON r.MaLichLV=keys.MaLichLV ORDER BY keys.MaLichLV;
        """;

    private const string CourseVehicleRowsSql = """
        SELECT MaLichSD,MaKH,BienSoXe,MaGV,MaHV,DiaDiem,GhiChu,TrangThai,NgayBD,NgayKT,
               IsKhoaHocXeTap,TenHV,TenGV FROM dbo.KhoaHoc_XeTap ORDER BY MaLichSD;
        """;

    private const string CourseVehicleChangesSql = """
        ;WITH keys AS(
          SELECT CONVERT(nvarchar(40),ct.MaLichSD) SourceKey,ct.MaLichSD,MAX(ct.SYS_CHANGE_VERSION) LastVersion
          FROM CHANGETABLE(CHANGES dbo.KhoaHoc_XeTap,@CheckpointVersion) AS ct
          WHERE ct.SYS_CHANGE_VERSION<=@AnchorVersion GROUP BY ct.MaLichSD)
        SELECT keys.SourceKey,CONVERT(bit,CASE WHEN r.MaLichSD IS NULL THEN 0 ELSE 1 END) CurrentExists,
               r.MaLichSD,r.MaKH,r.BienSoXe,r.MaGV,r.MaHV,r.DiaDiem,r.GhiChu,r.TrangThai,
               r.NgayBD,r.NgayKT,r.IsKhoaHocXeTap,r.TenHV,r.TenGV
        FROM keys LEFT JOIN dbo.KhoaHoc_XeTap r ON r.MaLichSD=keys.MaLichSD ORDER BY keys.MaLichSD;
        """;

    private const string VehicleBootstrapRowsSql = """
        SELECT BienSoXe,MaSoGTVT,MaCSDT,SoDK,SoHuu,NhanHieu,LoaiXe,MacXe,HangXe,MauXe,
               SoDongCo,SoKhung,GiayPhepXTL,SoGPXTL,CoQuanCapGPXTL,NgayCapGPXTL,
               NgayHHGPXTL,NamSX,HeThongPP,NgayCapGCNKD,NgayHHGCNKD,BaoHiem,TuyenDuong,
               ChatLuong,GhiChu,TrangThai,NguoiTao,NguoiSua,NgayTao,NgaySua,DuongDanAnh,
               HangGPLXXe,MaFileTiepNhanXML,ThoiGianTiepNhanXML FROM dbo.XeTap ORDER BY BienSoXe;
        """;

    private const string TeacherGuardSql = """
        SELECT
          (SELECT COUNT(1) FROM(SELECT SourceProfileCode,SourceMaGV FROM dbo.App_GiaoVien WITH(UPDLOCK,HOLDLOCK) WHERE SourceMaGV IS NOT NULL GROUP BY SourceProfileCode,SourceMaGV HAVING COUNT_BIG(*)>1)d) DuplicateGroups,
          (SELECT COUNT(1) FROM #QlhvFullSync_GiaoVien s JOIN dbo.App_GiaoVien t WITH(UPDLOCK,HOLDLOCK) ON t.MaGV=s.MaGV WHERE t.SourceProfileCode IS NULL OR t.SourceProfileCode<>s.SourceProfileCode OR ISNULL(t.SourceMaGV,N'')<>s.SourceMaGV) NaturalKeyConflicts,
          CONVERT(int,0) RelationConflicts;
        """;

    private const string CourseTeacherGuardSql = """
        SELECT
          (SELECT COUNT(1) FROM(SELECT SourceProfileCode,SourceMaLichLV FROM dbo.App_KhoaHoc_GiaoVien WITH(UPDLOCK,HOLDLOCK) WHERE SourceMaLichLV IS NOT NULL GROUP BY SourceProfileCode,SourceMaLichLV HAVING COUNT_BIG(*)>1)d) DuplicateGroups,
          CONVERT(int,0) NaturalKeyConflicts,
          (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHocGiaoVien r
            LEFT JOIN dbo.App_KhoaHoc k WITH(UPDLOCK,HOLDLOCK) ON k.SourceProfileCode=r.SourceProfileCode AND k.SourceMaKhoaHoc=r.SourceMaKhoaHoc AND k.IsDeleted=0
            LEFT JOIN dbo.App_GiaoVien g WITH(UPDLOCK,HOLDLOCK) ON g.SourceProfileCode=r.SourceProfileCode AND g.SourceMaGV=r.SourceMaGV AND g.IsDeleted=0
            WHERE k.KhoaHocId IS NULL OR g.GiaoVienId IS NULL) RelationConflicts;
        """;

    private const string CreateCourseVehicleStagingSql = """
        CREATE TABLE #TVP_CourseVehicle(
          SourceProfileCode nvarchar(50) NOT NULL,SourceMaLichSD bigint NOT NULL,
          SourceMaKhoaHoc nvarchar(50) NOT NULL,SourceBienSoXe nvarchar(20) NOT NULL,
          SourceHash nvarchar(64) NOT NULL,MaKhoa nvarchar(50) NOT NULL,
          BienSoXe nvarchar(20) NOT NULL,MaGV nvarchar(20) NULL,
          SourceMaHocVien nvarchar(50) NULL,DiaDiem nvarchar(255) NULL,
          TenHocVien nvarchar(50) NULL,TenGV nvarchar(255) NULL,
          NgayBatDau date NULL,NgayKetThuc date NULL,GhiChu nvarchar(500) NULL,
          IsKhoaHocXeTap bit NOT NULL,TrangThaiNguon bit NOT NULL,
          PRIMARY KEY(SourceProfileCode,SourceMaLichSD));
        """;

    private const string CourseVehicleGuardSql = """
        SELECT
          (SELECT COUNT(1) FROM(SELECT SourceProfileCode,SourceMaLichSD FROM dbo.App_KhoaHoc_XeTap WITH(UPDLOCK,HOLDLOCK) WHERE SourceMaLichSD IS NOT NULL GROUP BY SourceProfileCode,SourceMaLichSD HAVING COUNT_BIG(*)>1)d) DuplicateGroups,
          CONVERT(int,0) NaturalKeyConflicts,
          (SELECT COUNT(1) FROM #TVP_CourseVehicle r
            LEFT JOIN dbo.App_KhoaHoc k WITH(UPDLOCK,HOLDLOCK) ON k.SourceProfileCode=r.SourceProfileCode AND k.SourceMaKhoaHoc=r.SourceMaKhoaHoc AND k.IsDeleted=0
            LEFT JOIN dbo.App_XeTap x WITH(UPDLOCK,HOLDLOCK) ON x.SourceProfileCode=r.SourceProfileCode AND x.SourceBienSoXe=r.SourceBienSoXe AND x.IsDeleted=0
            LEFT JOIN dbo.App_GiaoVien g WITH(UPDLOCK,HOLDLOCK) ON g.SourceProfileCode=r.SourceProfileCode AND g.MaGV=r.MaGV AND g.IsDeleted=0
            WHERE k.KhoaHocId IS NULL OR x.XeTapId IS NULL OR (r.MaGV IS NOT NULL AND g.GiaoVienId IS NULL)) RelationConflicts;
        """;

    private const string MergeCourseVehicleSql = """
        MERGE dbo.App_KhoaHoc_XeTap WITH(HOLDLOCK) t USING #TVP_CourseVehicle s
          ON t.SourceProfileCode=s.SourceProfileCode AND t.SourceMaLichSD=s.SourceMaLichSD
        WHEN MATCHED AND (t.IsDeleted=1 OR ISNULL(t.SourceHash,N'')<>s.SourceHash) THEN UPDATE SET
          t.SourceMaKhoaHoc=s.SourceMaKhoaHoc,t.SourceBienSoXe=s.SourceBienSoXe,
          t.SourceHash=s.SourceHash,t.MaKhoa=s.MaKhoa,t.BienSoXe=s.BienSoXe,t.MaGV=s.MaGV,
          t.SourceMaHocVien=s.SourceMaHocVien,t.DiaDiem=s.DiaDiem,
          t.TenHocVien=s.TenHocVien,t.TenGV=s.TenGV,
          t.NgayBatDau=s.NgayBatDau,t.NgayKetThuc=s.NgayKetThuc,
          t.GhiChu=s.GhiChu,t.IsKhoaHocXeTap=s.IsKhoaHocXeTap,t.TrangThaiNguon=s.TrangThaiNguon,
          t.TrangThai=CASE WHEN s.TrangThaiNguon=1 THEN N'ACTIVE' ELSE N'INACTIVE' END,
          t.SourceOfTruth=N'V2',t.V2RowHash=s.SourceHash,t.LastSyncFromV2At=SYSUTCDATETIME(),
          t.IsDeleted=0,t.DeletedAt=NULL,t.DeletedBy=NULL,t.DeleteReason=NULL,
          t.UpdatedAt=SYSUTCDATETIME(),t.UpdatedAtUtc=SYSUTCDATETIME(),t.UpdatedBy=N'TeacherVehicleProjectionWorker'
        WHEN NOT MATCHED THEN INSERT(SourceProfileCode,SourceMaLichSD,SourceMaKhoaHoc,SourceBienSoXe,SourceHash,MaKhoa,BienSoXe,MaGV,SourceMaHocVien,DiaDiem,TenHocVien,TenGV,NgayBatDau,NgayKetThuc,GhiChu,IsKhoaHocXeTap,TrangThaiNguon,TrangThai,SourceOfTruth,V2RowHash,LastSyncFromV2At,IsDeleted,CreatedAt,CreatedAtUtc,CreatedBy)
          VALUES(s.SourceProfileCode,s.SourceMaLichSD,s.SourceMaKhoaHoc,s.SourceBienSoXe,s.SourceHash,s.MaKhoa,s.BienSoXe,s.MaGV,s.SourceMaHocVien,s.DiaDiem,s.TenHocVien,s.TenGV,s.NgayBatDau,s.NgayKetThuc,s.GhiChu,s.IsKhoaHocXeTap,s.TrangThaiNguon,CASE WHEN s.TrangThaiNguon=1 THEN N'ACTIVE' ELSE N'INACTIVE' END,N'V2',s.SourceHash,SYSUTCDATETIME(),0,SYSUTCDATETIME(),SYSUTCDATETIME(),N'TeacherVehicleProjectionWorker')
        OUTPUT $action;
        """;

    private const string TeacherDeleteSql = """
        UPDATE dbo.App_GiaoVien SET IsDeleted=1,TrangThaiNguon=0,TrangThai=N'INACTIVE',
          DeletedAt=SYSUTCDATETIME(),DeletedBy=N'TeacherVehicleProjectionWorker',DeleteReason=N'SOURCE_DELETED',
          UpdatedAt=SYSUTCDATETIME(),UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'TeacherVehicleProjectionWorker'
        OUTPUT N'SOFT_DELETE' WHERE SourceProfileCode=@SourceProfileCode AND SourceMaGV IN @DeletedKeys AND IsDeleted=0;
        """;
    private const string CourseTeacherDeleteSql = """
        UPDATE dbo.App_KhoaHoc_GiaoVien SET IsDeleted=1,TrangThaiNguon=0,TrangThai=N'INACTIVE',
          DeletedAt=SYSUTCDATETIME(),DeletedBy=N'TeacherVehicleProjectionWorker',DeleteReason=N'SOURCE_DELETED',
          UpdatedAt=SYSUTCDATETIME(),UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'TeacherVehicleProjectionWorker'
        OUTPUT N'SOFT_DELETE' WHERE SourceProfileCode=@SourceProfileCode AND CONVERT(nvarchar(40),SourceMaLichLV) IN @DeletedKeys AND IsDeleted=0;
        """;
    private const string CourseVehicleDeleteSql = """
        UPDATE dbo.App_KhoaHoc_XeTap SET IsDeleted=1,TrangThaiNguon=0,TrangThai=N'INACTIVE',
          DeletedAt=SYSUTCDATETIME(),DeletedBy=N'TeacherVehicleProjectionWorker',DeleteReason=N'SOURCE_DELETED',
          UpdatedAt=SYSUTCDATETIME(),UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'TeacherVehicleProjectionWorker'
        OUTPUT N'SOFT_DELETE' WHERE SourceProfileCode=@SourceProfileCode AND CONVERT(nvarchar(40),SourceMaLichSD) IN @DeletedKeys AND IsDeleted=0;
        """;
    private const string SoftDeleteCourseVehicleSnapshotSql = """
        UPDATE t SET IsDeleted=1,TrangThaiNguon=0,TrangThai=N'INACTIVE',DeletedAt=SYSUTCDATETIME(),
          DeletedBy=N'TeacherVehicleProjectionWorker',DeleteReason=N'MISSING_FROM_SOURCE_SNAPSHOT',
          UpdatedAt=SYSUTCDATETIME(),UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'TeacherVehicleProjectionWorker'
        OUTPUT N'SOFT_DELETE' FROM dbo.App_KhoaHoc_XeTap t
        WHERE t.SourceProfileCode=@SourceProfileCode AND t.IsDeleted=0 AND NOT EXISTS(
          SELECT 1 FROM #TVP_CourseVehicle s WHERE s.SourceProfileCode=t.SourceProfileCode AND s.SourceMaLichSD=t.SourceMaLichSD);
        """;

    private const string VerifyTeacherSql = """
        SELECT (SELECT COUNT_BIG(*) FROM #QlhvFullSync_GiaoVien) ExactRows,
          (SELECT COUNT_BIG(*) FROM(SELECT SourceProfileCode,SourceMaGV FROM dbo.App_GiaoVien WHERE SourceMaGV IS NOT NULL GROUP BY SourceProfileCode,SourceMaGV HAVING COUNT_BIG(*)>1)d) DuplicateGroups,
          (SELECT COUNT_BIG(*) FROM #QlhvFullSync_GiaoVien s LEFT JOIN dbo.App_GiaoVien t ON t.SourceProfileCode=s.SourceProfileCode AND t.SourceMaGV=s.SourceMaGV WHERE t.GiaoVienId IS NULL OR t.IsDeleted=1 OR ISNULL(t.SourceHash,N'')<>s.SourceHash) MismatchRows,
          ISNULL((SELECT CHECKSUM_AGG(BINARY_CHECKSUM(SourceProfileCode,SourceMaGV,SourceHash)) FROM dbo.App_GiaoVien WHERE SourceProfileCode=@SourceProfileCode),0) HashAggregate;
        """;
    private const string VerifyCourseTeacherSql = """
        SELECT (SELECT COUNT_BIG(*) FROM #QlhvFullSync_KhoaHocGiaoVien) ExactRows,
          (SELECT COUNT_BIG(*) FROM(SELECT SourceProfileCode,SourceMaLichLV FROM dbo.App_KhoaHoc_GiaoVien WHERE SourceMaLichLV IS NOT NULL GROUP BY SourceProfileCode,SourceMaLichLV HAVING COUNT_BIG(*)>1)d) DuplicateGroups,
          (SELECT COUNT_BIG(*) FROM #QlhvFullSync_KhoaHocGiaoVien s LEFT JOIN dbo.App_KhoaHoc_GiaoVien t ON t.SourceProfileCode=s.SourceProfileCode AND t.SourceMaLichLV=s.SourceMaLichLV WHERE t.Id IS NULL OR t.IsDeleted=1 OR ISNULL(t.SourceHash,N'')<>s.SourceHash) MismatchRows,
          ISNULL((SELECT CHECKSUM_AGG(BINARY_CHECKSUM(SourceProfileCode,SourceMaLichLV,SourceHash)) FROM dbo.App_KhoaHoc_GiaoVien WHERE SourceProfileCode=@SourceProfileCode),0) HashAggregate;
        """;
    private const string VerifyCourseVehicleSql = """
        SELECT (SELECT COUNT_BIG(*) FROM #TVP_CourseVehicle) ExactRows,
          (SELECT COUNT_BIG(*) FROM(SELECT SourceProfileCode,SourceMaLichSD FROM dbo.App_KhoaHoc_XeTap WHERE SourceMaLichSD IS NOT NULL GROUP BY SourceProfileCode,SourceMaLichSD HAVING COUNT_BIG(*)>1)d) DuplicateGroups,
          (SELECT COUNT_BIG(*) FROM #TVP_CourseVehicle s LEFT JOIN dbo.App_KhoaHoc_XeTap t ON t.SourceProfileCode=s.SourceProfileCode AND t.SourceMaLichSD=s.SourceMaLichSD WHERE t.Id IS NULL OR t.IsDeleted=1 OR ISNULL(t.SourceHash,N'')<>s.SourceHash) MismatchRows,
          ISNULL((SELECT CHECKSUM_AGG(BINARY_CHECKSUM(SourceProfileCode,SourceMaLichSD,SourceHash)) FROM dbo.App_KhoaHoc_XeTap WHERE SourceProfileCode=@SourceProfileCode),0) HashAggregate;
        """;

    private const string ReadProjectionCheckpointSql = """
        SELECT SourceProfileCode,DomainName,ContractVersion,SourceDatabaseGuid,LastCtVersion,
               MappingFingerprint,SourceSchemaFingerprint,State,RowVersion
        FROM dbo.App_TeacherVehicleProjectionCheckpoint
        WHERE SourceProfileCode=@SourceProfileCode AND DomainName=@DomainName AND ContractVersion=@ContractVersion;
        """;
    private const string ReadLockedProjectionCheckpointSql = """
        SELECT SourceProfileCode,DomainName,ContractVersion,SourceDatabaseGuid,LastCtVersion,
               MappingFingerprint,SourceSchemaFingerprint,State,RowVersion
        FROM dbo.App_TeacherVehicleProjectionCheckpoint WITH(UPDLOCK,HOLDLOCK)
        WHERE SourceProfileCode=@SourceProfileCode AND DomainName=@DomainName AND ContractVersion=@ContractVersion;
        """;
    private const string ReadAllProjectionCheckpointsSql = """
        SELECT SourceProfileCode,DomainName,ContractVersion,SourceDatabaseGuid,LastCtVersion,
               MappingFingerprint,SourceSchemaFingerprint,State,RowVersion
        FROM dbo.App_TeacherVehicleProjectionCheckpoint
        WHERE SourceProfileCode=@SourceProfileCode AND ContractVersion=@ContractVersion;
        """;
    private const string AcquireDomainLockSql = """
        DECLARE @result int,@resource nvarchar(255)=N'QLHV:TVP:'+@SourceProfileCode+N':'+@Domain;
        EXEC @result=sys.sp_getapplock
          @Resource=@resource,
          @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0; SELECT @result;
        """;

    private const string InsertCheckpointAndCycleSql = """
        INSERT dbo.App_TeacherVehicleProjectionCycle(CycleId,SourceProfileCode,DomainName,ContractVersion,SourceDatabaseGuid,FromCtVersion,ToCtVersion,MappingFingerprint,SourceSchemaFingerprint,SourceRows,InsertedRows,UpdatedRows,InactiveRows,NoChangeRows,VerificationHash,ArtifactSha256,Outcome,StartedAtUtc,CompletedAtUtc)
        VALUES(@CycleId,@SourceProfileCode,@Domain,@ContractVersion,@SourceDatabaseGuid,@FromVersion,@ToVersion,@MappingFingerprint,@SourceSchemaFingerprint,@SourceCount,@InsertedRows,@UpdatedRows,@InactiveRows,@NoChangeRows,@VerificationHash,@ArtifactSha256,N'BOOTSTRAP_VERIFIED',SYSUTCDATETIME(),SYSUTCDATETIME());
        INSERT dbo.App_TeacherVehicleProjectionCheckpoint(SourceProfileCode,DomainName,ContractVersion,SourceDatabaseGuid,LastCtVersion,MappingFingerprint,SourceSchemaFingerprint,State,LastCycleId,UpdatedAtUtc)
        VALUES(@SourceProfileCode,@Domain,@ContractVersion,@SourceDatabaseGuid,@ToVersion,@MappingFingerprint,@SourceSchemaFingerprint,N'ACTIVE',@CycleId,SYSUTCDATETIME());
        """;
    private const string UpdateCheckpointAndCycleSql = """
        INSERT dbo.App_TeacherVehicleProjectionCycle(CycleId,SourceProfileCode,DomainName,ContractVersion,SourceDatabaseGuid,FromCtVersion,ToCtVersion,MappingFingerprint,SourceSchemaFingerprint,SourceRows,InsertedRows,UpdatedRows,InactiveRows,NoChangeRows,VerificationHash,ArtifactSha256,Outcome,StartedAtUtc,CompletedAtUtc)
        VALUES(@CycleId,@SourceProfileCode,@Domain,@ContractVersion,@SourceDatabaseGuid,@FromVersion,@ToVersion,@MappingFingerprint,@SourceSchemaFingerprint,@SourceCount,@InsertedRows,@UpdatedRows,@InactiveRows,@NoChangeRows,@VerificationHash,NULL,CASE WHEN @InsertedRows+@UpdatedRows+@InactiveRows=0 THEN N'HEALTHY_NO_CHANGE' ELSE N'HEALTHY' END,SYSUTCDATETIME(),SYSUTCDATETIME());
        UPDATE dbo.App_TeacherVehicleProjectionCheckpoint SET LastCtVersion=@ToVersion,LastCycleId=@CycleId,UpdatedAtUtc=SYSUTCDATETIME()
        WHERE SourceProfileCode=@SourceProfileCode AND DomainName=@Domain AND ContractVersion=@ContractVersion AND LastCtVersion=@FromVersion AND State=N'ACTIVE';
        IF @@ROWCOUNT<>1 THROW 532601,'TVP_CHECKPOINT_ADVANCE_FAILED',1;
        """;
    private const string InsertVehicleCheckpointAndCycleSql = """
        INSERT dbo.App_TeacherVehicleProjectionCycle(CycleId,SourceProfileCode,DomainName,ContractVersion,SourceDatabaseGuid,FromCtVersion,ToCtVersion,MappingFingerprint,SourceSchemaFingerprint,SourceRows,InsertedRows,UpdatedRows,InactiveRows,NoChangeRows,VerificationHash,ArtifactSha256,Outcome,StartedAtUtc,CompletedAtUtc)
        VALUES(@CycleId,@SourceProfileCode,@Domain,@ContractVersion,@SourceDatabaseGuid,@AnchorVersion,@AnchorVersion,@MappingFingerprint,@SourceSchemaFingerprint,@SourceRows,@InsertedRows,@UpdatedRows,@InactiveRows,@NoChangeRows,@VerificationHash,@ArtifactSha256,N'BOOTSTRAP_VERIFIED',SYSUTCDATETIME(),SYSUTCDATETIME());
        INSERT dbo.App_XeTap_RealtimeCheckpoint(SourceProfileCode,SourceDatabaseGuid,LastCtVersion,MappingFingerprint,SourceSchemaFingerprint,State,LastCycleId,LastPlanToken,UpdatedAt)
        VALUES(@SourceProfileCode,@SourceDatabaseGuid,@AnchorVersion,@MappingFingerprint,@SourceSchemaFingerprint,N'ACTIVE',@CycleId,@VerificationHash,SYSUTCDATETIME());
        """;

    private const string BootstrapGateSql = """
        SELECT (SELECT State FROM dbo.App_Rt03RealtimeControl WHERE ControlId=1) MasterState,
          (SELECT COUNT(1)
           FROM dbo.App_QlhvAutoSyncRun
           WHERE ActiveSlot=1
             AND Status IN(N'QUEUED',N'RUNNING')
             AND CompletedAtUtc IS NULL) AutoSyncActive,
          (SELECT COUNT(1)
           FROM dbo.App_QlhvSyncOperationHistory
           WHERE Status IN(N'QUEUED',N'RUNNING')) WriterCount;
        """;
}

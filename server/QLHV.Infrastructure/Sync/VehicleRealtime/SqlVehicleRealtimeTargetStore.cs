using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.VehicleRealtime;

namespace QLHV.Infrastructure.Sync.VehicleRealtime;

internal sealed class SqlVehicleRealtimeTargetStore : IVehicleRealtimeTargetStore
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly IConfiguration _configuration;

    public SqlVehicleRealtimeTargetStore(
        IConnectionSettingsProvider connections,
        IConfiguration configuration)
    {
        _connections = connections;
        _configuration = configuration;
    }

    public async Task<VehicleTargetPlanningSnapshot> ReadPlanningSnapshotAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        VehicleRealtimeRouteCatalog.GetRequired(sourceProfileCode);
        var resolved = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!resolved.IsUsable || string.IsNullOrWhiteSpace(resolved.ConnectionString))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.NotInitialized,
                "QLHV_APP connection is unavailable for vehicle realtime.");
        }

        await using var connection = new SqlConnection(resolved.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await ValidateTargetIdentityAsync(
            connection, transaction: null, cancellationToken,
            ExpectedTargetGuid(connection));
        var checkpoint = await connection.QuerySingleOrDefaultAsync<CheckpointRow>(
            new CommandDefinition(
                ReadCheckpointSql,
                new { SourceProfileCode = sourceProfileCode },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        if (checkpoint is null)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.NotInitialized,
                "Vehicle realtime has no operator-sealed checkpoint.");
        }

        var vehicles = (await connection.QueryAsync<VehicleTargetSnapshot>(
            new CommandDefinition(
                ReadInventorySql,
                commandTimeout: 30,
                cancellationToken: cancellationToken))).ToArray();
        return new VehicleTargetPlanningSnapshot(checkpoint.ToContract(), vehicles);
    }

    public async Task<VehicleRealtimeCycleResult> CommitAsync(
        VehicleRealtimeSealedPlan plan,
        VehicleTargetPlanningSnapshot expectedTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(expectedTarget);
        var route = VehicleRealtimeRouteCatalog.GetRequired(plan.SourceProfileCode);
        if (plan.SourceDatabaseGuid != ExpectedSourceGuid(route) ||
            !string.Equals(
                plan.MappingFingerprint,
                VehicleSourceMapper.ComputeMappingFingerprint(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "Vehicle sealed plan identity/fingerprint is invalid.");
        }

        var resolved = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!resolved.IsUsable || string.IsNullOrWhiteSpace(resolved.ConnectionString))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.NotInitialized,
                "QLHV_APP connection is unavailable for vehicle realtime commit.");
        }

        await using var connection = new SqlConnection(resolved.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await ValidateTargetIdentityAsync(
                connection, transaction, cancellationToken,
                ExpectedTargetGuid(connection));
            var lockResult = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    AcquireVehicleLockSql,
                    transaction: transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            if (lockResult < 0)
            {
                throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.TargetChangedDuringPlan,
                    "The global vehicle realtime transaction lock is unavailable.");
            }

            var currentCheckpoint =
                await connection.QuerySingleOrDefaultAsync<CheckpointRow>(
                    new CommandDefinition(
                        LockCheckpointSql,
                        new { SourceProfileCode = plan.SourceProfileCode },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken))
                ?? throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.NotInitialized,
                    "Vehicle checkpoint disappeared before commit.");
            ValidateCheckpointCas(
                plan,
                expectedTarget.Checkpoint,
                currentCheckpoint.ToContract());

            var lockedVehicles = (await connection.QueryAsync<VehicleTargetSnapshot>(
                new CommandDefinition(
                    ReadLockedInventorySql,
                    transaction: transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken))).ToArray();
            RevalidatePlans(plan, lockedVehicles);

            if (plan.Rows.Count == 0 &&
                plan.CheckpointAfter == plan.CheckpointBefore)
            {
                await transaction.CommitAsync(cancellationToken);
                return new VehicleRealtimeCycleResult(
                    plan.CycleId,
                    plan.SourceProfileCode,
                    plan.CheckpointBefore,
                    plan.CheckpointAfter,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    plan.PlanToken);
            }

            var now = DateTime.UtcNow;
            var inserted = 0;
            var updated = 0;
            var inactive = 0;
            var missing = 0;
            var manual = 0;
            var noChange = 0;
            foreach (var row in plan.Rows
                         .OrderBy(item => item.SourceBienSoXe,
                             StringComparer.OrdinalIgnoreCase))
            {
                long? targetXeTapId = row.TargetXeTapId;
                switch (row.Action)
                {
                    case VehicleRealtimeActions.InsertSourceRow:
                        targetXeTapId = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition(
                                InsertVehicleSql,
                                WriteParameters(row, now),
                                transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                        inserted++;
                        break;
                    case VehicleRealtimeActions.UpdateSourceOwnedFields:
                    case VehicleRealtimeActions.MarkSourceInactive:
                        var affected = await connection.ExecuteAsync(
                            new CommandDefinition(
                                UpdateVehicleSql,
                                WriteParameters(row, now),
                                transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                        RequireOne(affected);
                        if (row.Action == VehicleRealtimeActions.MarkSourceInactive)
                        {
                            inactive++;
                        }
                        else
                        {
                            updated++;
                        }

                        break;
                    case VehicleRealtimeActions.MarkSourceMissing:
                        var missingAffected = await connection.ExecuteAsync(
                            new CommandDefinition(
                                MarkMissingSql,
                                new
                                {
                                    row.TargetXeTapId,
                                    row.ExpectedTargetRowVersion,
                                    row.SourceProfileCode,
                                    row.SourceBienSoXe,
                                    row.SourceCtVersion,
                                    Now = now,
                                },
                                transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                        RequireOne(missingAffected);
                        missing++;
                        break;
                    case VehicleRealtimeActions.ManualReview:
                        await InsertManualReviewAsync(
                            connection,
                            transaction,
                            plan,
                            row,
                            lockedVehicles,
                            now,
                            cancellationToken);
                        manual++;
                        break;
                    case VehicleRealtimeActions.NoChange:
                        noChange++;
                        break;
                    default:
                        throw new VehicleRealtimeSafetyException(
                            VehicleRealtimeErrorCodes.UnsafePlan,
                            "Vehicle sealed plan contains an unknown action.");
                }

                var eventRows = await connection.ExecuteAsync(
                    new CommandDefinition(
                        InsertEventSql,
                        new
                        {
                            plan.CycleId,
                            row.SourceProfileCode,
                            row.SourceCtVersion,
                            row.SourceBienSoXe,
                            ChangeKind = row.ChangeKind == VehicleSourceChangeKind.Delete
                                ? "DELETE"
                                : "UPSERT",
                            row.Action,
                            SourceRowHash = row.Source?.SourceRowHash,
                            TargetXeTapId = targetXeTapId,
                            plan.PlanToken,
                            AppliedAt = now,
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                RequireOne(eventRows);
            }

            var checkpointRows = await connection.ExecuteAsync(
                new CommandDefinition(
                    AdvanceCheckpointSql,
                    new
                    {
                        plan.SourceProfileCode,
                        plan.SourceDatabaseGuid,
                        plan.CheckpointBefore,
                        LastCtVersion = plan.CheckpointAfter,
                        plan.MappingFingerprint,
                        plan.SourceSchemaFingerprint,
                        plan.CycleId,
                        plan.PlanToken,
                        ExpectedRowVersion = expectedTarget.Checkpoint.RowVersion,
                        UpdatedAt = now,
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            RequireOne(checkpointRows);
            await transaction.CommitAsync(cancellationToken);
            return new VehicleRealtimeCycleResult(
                plan.CycleId,
                plan.SourceProfileCode,
                plan.CheckpointBefore,
                plan.CheckpointAfter,
                inserted,
                updated,
                inactive,
                missing,
                manual,
                noChange,
                plan.PlanToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    internal static void RevalidatePlans(
        VehicleRealtimeSealedPlan sealedPlan,
        IReadOnlyList<VehicleTargetSnapshot> lockedVehicles)
    {
        foreach (var expected in sealedPlan.Rows)
        {
            if (expected.ChangeKind == VehicleSourceChangeKind.Upsert &&
                expected.Source is null)
            {
                if (!expected.RequiresManualReview)
                {
                    throw Changed();
                }

                continue;
            }

            VehicleRealtimePlan current;
            if (expected.ChangeKind == VehicleSourceChangeKind.Delete)
            {
                current = VehicleRealtimePlanner.PlanDelete(
                    VehicleSourceIdentity.Create(
                        expected.SourceProfileCode,
                        expected.SourceBienSoXe),
                    lockedVehicles,
                    expected.SourceCtVersion);
            }
            else
            {
                current = VehicleRealtimePlanner.PlanUpsert(
                    new VehicleMappingResult(
                        expected.Source,
                        Array.Empty<string>(),
                        Array.Empty<string>()),
                    lockedVehicles,
                    expected.SourceCtVersion);
            }

            if (Equivalent(expected, current))
            {
                continue;
            }

            if (expected.RequiresManualReview &&
                expected.ConflictingXeTapId is null &&
                expected.ReviewCode is
                    VehicleRealtimeReviewCodes.PlateCollision or
                    VehicleRealtimeReviewCodes.RegistrationCollision or
                    VehicleRealtimeReviewCodes.ChassisCollision or
                    VehicleRealtimeReviewCodes.EngineCollision &&
                HasMatchingWithinBatchCollision(expected, sealedPlan.Rows))
            {
                continue;
            }

            throw Changed();
        }

        static VehicleRealtimeSafetyException Changed()
            => new(
                VehicleRealtimeErrorCodes.TargetChangedDuringPlan,
                "App_XeTap identity, RowVersion, assignment or collision evidence changed.");
    }

    private static bool Equivalent(
        VehicleRealtimePlan expected,
        VehicleRealtimePlan current)
        => string.Equals(expected.Action, current.Action, StringComparison.Ordinal) &&
           string.Equals(expected.Lifecycle, current.Lifecycle, StringComparison.Ordinal) &&
           expected.TargetXeTapId == current.TargetXeTapId &&
           expected.ConflictingXeTapId == current.ConflictingXeTapId &&
           string.Equals(expected.ReviewCode, current.ReviewCode, StringComparison.Ordinal) &&
           string.Equals(
               expected.CollisionField,
               current.CollisionField,
               StringComparison.Ordinal) &&
           EqualBytes(
               expected.ExpectedTargetRowVersion,
               current.ExpectedTargetRowVersion);

    private static bool HasMatchingWithinBatchCollision(
        VehicleRealtimePlan expected,
        IEnumerable<VehicleRealtimePlan> rows)
    {
        var source = expected.Source;
        if (source is null)
        {
            return false;
        }

        return rows.Any(other =>
        {
            if (ReferenceEquals(expected, other) ||
                other.Source is null ||
                string.Equals(
                    expected.SourceBienSoXe,
                    other.SourceBienSoXe,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return expected.CollisionField switch
            {
                VehicleRealtimeCollisionFields.BienSoXe =>
                    Equal(source.NormalizedBienSoXe, other.Source.NormalizedBienSoXe),
                VehicleRealtimeCollisionFields.SoDK =>
                    Equal(source.NormalizedSoDK, other.Source.NormalizedSoDK),
                VehicleRealtimeCollisionFields.SoKhung =>
                    Equal(source.NormalizedSoKhung, other.Source.NormalizedSoKhung),
                VehicleRealtimeCollisionFields.SoDongCo =>
                    Equal(source.NormalizedSoDongCo, other.Source.NormalizedSoDongCo),
                _ => false,
            };
        });

        static bool Equal(string? left, string? right)
            => left is not null && right is not null &&
               string.Equals(left, right, StringComparison.Ordinal);
    }

    private static async Task InsertManualReviewAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleRealtimeSealedPlan plan,
        VehicleRealtimePlan row,
        IReadOnlyCollection<VehicleTargetSnapshot> targets,
        DateTime detectedAt,
        CancellationToken cancellationToken)
    {
        var target = row.TargetXeTapId.HasValue
            ? targets.Single(item => item.XeTapId == row.TargetXeTapId.Value)
            : null;
        var conflict = row.ConflictingXeTapId.HasValue
            ? targets.Single(item => item.XeTapId == row.ConflictingXeTapId.Value)
            : null;
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                InsertManualReviewSql,
                new
                {
                    plan.CycleId,
                    row.SourceProfileCode,
                    row.SourceCtVersion,
                    row.SourceBienSoXe,
                    row.ReviewCode,
                    row.CollisionField,
                    row.TargetXeTapId,
                    row.ConflictingXeTapId,
                    SourceRowHash = row.Source?.SourceRowHash,
                    HasActiveAssignment =
                        target?.HasActiveAssignments == true ||
                        conflict?.HasActiveAssignments == true,
                    DetectedAt = detectedAt,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        RequireOne(affected);
    }

    internal static object WriteParameters(VehicleRealtimePlan row, DateTime now)
    {
        var source = row.Source ??
                     throw new VehicleRealtimeSafetyException(
                         VehicleRealtimeErrorCodes.UnsafePlan,
                         "A vehicle write action has no mapped source row.");
        return new
        {
            row.TargetXeTapId,
            row.ExpectedTargetRowVersion,
            row.SourceCtVersion,
            Now = now,
            source.Identity.SourceProfileCode,
            source.Identity.SourceBienSoXe,
            source.NormalizedBienSoXe,
            source.SourceRowHash,
            source.SourceOfTruth,
            source.MaCSDT,
            source.MaSoGTVT,
            source.BienSoXe,
            source.SoDK,
            source.NormalizedSoDK,
            source.SoHuu,
            source.XeCuaCoSoDaoTao,
            source.XeHopDong,
            source.NhanHieu,
            source.LoaiXe,
            source.MacXe,
            source.HangXe,
            source.HangGPLXXe,
            source.MauXe,
            source.NamSX,
            source.SoDongCo,
            source.NormalizedSoDongCo,
            source.SoKhung,
            source.NormalizedSoKhung,
            source.GiayPhepXTL,
            source.SoGPXTL,
            source.CoQuanCapGPXTL,
            source.NgayCapGPXTL,
            source.NgayHetHanGPXTL,
            source.HeThongPhanhPhu,
            source.BaoHiem,
            source.TuyenDuong,
            source.ChatLuong,
            source.NgayCapGCNKD,
            source.NgayHetHanGCNKD,
            source.GhiChuV2,
            source.SourceTrangThai,
            source.SourceLifecycle,
            source.SourceCreatedBy,
            source.SourceUpdatedBy,
            source.SourceCreatedAt,
            source.SourceUpdatedAt,
            source.SourceImagePathHash,
            source.SourceMaFileTiepNhanXml,
            source.SourceThoiGianTiepNhanXml,
        };
    }

    private static void ValidateCheckpointCas(
        VehicleRealtimeSealedPlan plan,
        VehicleRealtimeCheckpoint expected,
        VehicleRealtimeCheckpoint current)
    {
        if (!string.Equals(
                current.SourceProfileCode,
                plan.SourceProfileCode,
                StringComparison.Ordinal) ||
            current.SourceDatabaseGuid != plan.SourceDatabaseGuid ||
            current.LastCtVersion != plan.CheckpointBefore ||
            !string.Equals(
                current.MappingFingerprint,
                plan.MappingFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                current.SourceSchemaFingerprint,
                plan.SourceSchemaFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.State, "ACTIVE", StringComparison.Ordinal) ||
            !EqualBytes(current.RowVersion, expected.RowVersion))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.TargetChangedDuringPlan,
                "Vehicle checkpoint CAS/revalidation failed.");
        }
    }

    private static bool EqualBytes(byte[]? left, byte[]? right)
        => left is null
            ? right is null
            : right is not null && left.AsSpan().SequenceEqual(right);

    private Guid ExpectedSourceGuid(VehicleRealtimeRoute route)
    {
        if (!IsDisposableTargetConfigured())
            return route.ExpectedProductionDatabaseGuid;
        var value = _configuration[
            $"TeacherVehicleProjection:DisposableSourceDatabaseGuids:{route.SourceProfileCode}"];
        return Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "Disposable vehicle source GUID is missing.");
    }

    private Guid ExpectedTargetGuid(SqlConnection connection)
    {
        if (!IsDisposableTargetConfigured() ||
            !string.Equals(connection.DataSource.Trim(), @"CSDLTTTC\QLHVRT02",
                StringComparison.OrdinalIgnoreCase))
            return VehicleRealtimeTargetDatabase.ExpectedProductionDatabaseGuid;
        var value = _configuration[
            "TeacherVehicleProjection:DisposableTargetDatabaseGuid"];
        return Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceIdentityRejected,
                "Disposable vehicle target GUID is missing.");
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

    internal static void RequireOne(int affected)
    {
        if (affected != 1)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.TargetChangedDuringPlan,
                "Vehicle realtime affected-row assertion failed.");
        }
    }

    internal static async Task ValidateTargetIdentityAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken,
        Guid? expectedDatabaseGuid = null)
    {
        var identity = await connection.QuerySingleAsync<TargetIdentityRow>(
            new CommandDefinition(
                TargetIdentitySql,
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        if (!string.Equals(
                identity.DatabaseName,
                VehicleRealtimeTargetDatabase.Name,
                StringComparison.Ordinal) ||
            identity.DatabaseGuid != (expectedDatabaseGuid ??
                VehicleRealtimeTargetDatabase.ExpectedProductionDatabaseGuid))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceIdentityRejected,
                "Vehicle realtime target is not the exact QLHV_APP production database.");
        }
    }

    private sealed class TargetIdentityRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public Guid DatabaseGuid { get; init; }
    }

    private sealed class CheckpointRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public Guid SourceDatabaseGuid { get; init; }
        public long LastCtVersion { get; init; }
        public string MappingFingerprint { get; init; } = string.Empty;
        public string SourceSchemaFingerprint { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public byte[] RowVersion { get; init; } = [];

        public VehicleRealtimeCheckpoint ToContract()
            => new(
                SourceProfileCode,
                SourceDatabaseGuid,
                LastCtVersion,
                MappingFingerprint,
                SourceSchemaFingerprint,
                State,
                RowVersion.ToArray());
    }

    internal const string TargetIdentitySql = """
        SELECT DB_NAME() AS DatabaseName, identityRow.database_guid AS DatabaseGuid
        FROM sys.database_recovery_status identityRow
        WHERE identityRow.database_id=DB_ID();
        """;

    internal const string ReadCheckpointSql = """
        SELECT SourceProfileCode,SourceDatabaseGuid,LastCtVersion,
               MappingFingerprint,SourceSchemaFingerprint,State,RowVersion
        FROM dbo.App_XeTap_RealtimeCheckpoint
        WHERE SourceProfileCode=@SourceProfileCode;
        """;

    internal const string LockCheckpointSql = """
        SELECT SourceProfileCode,SourceDatabaseGuid,LastCtVersion,
               MappingFingerprint,SourceSchemaFingerprint,State,RowVersion
        FROM dbo.App_XeTap_RealtimeCheckpoint WITH(UPDLOCK,HOLDLOCK)
        WHERE SourceProfileCode=@SourceProfileCode;
        """;

    internal const string AcquireVehicleLockSql = """
        DECLARE @Result int;
        EXEC @Result=sys.sp_getapplock
             @Resource=N'QLHV:RT03:VEHICLE:GLOBAL',
             @LockMode=N'Exclusive',
             @LockOwner=N'Transaction',
             @LockTimeout=0;
        SELECT @Result;
        """;

    internal const string ReadInventorySql = """
        CREATE TABLE #AssignedVehicle(XeTapId bigint NOT NULL PRIMARY KEY);
        IF OBJECT_ID(N'dbo.App_HocVien_PhanCong',N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_HocVien_PhanCong',N'XeTapId') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_HocVien_PhanCong',N'XeBaiSo10Id') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_HocVien_PhanCong',N'IsCurrent') IS NOT NULL
            EXEC sys.sp_executesql N'
                INSERT INTO #AssignedVehicle(XeTapId)
                SELECT valueId
                FROM
                (
                    SELECT XeTapId AS valueId
                    FROM dbo.App_HocVien_PhanCong
                    WHERE IsCurrent=1 AND XeTapId IS NOT NULL
                    UNION
                    SELECT XeBaiSo10Id
                    FROM dbo.App_HocVien_PhanCong
                    WHERE IsCurrent=1 AND XeBaiSo10Id IS NOT NULL
                ) sourceRows
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM #AssignedVehicle targetRows
                    WHERE targetRows.XeTapId=sourceRows.valueId
                );';
        IF OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao',N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_KhoaHoc_NhomDaoTao',N'XeTapId') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_KhoaHoc_NhomDaoTao',N'XeBaiSo10Id') IS NOT NULL
            EXEC sys.sp_executesql N'
                INSERT INTO #AssignedVehicle(XeTapId)
                SELECT valueId
                FROM
                (
                    SELECT XeTapId AS valueId
                    FROM dbo.App_KhoaHoc_NhomDaoTao WHERE XeTapId IS NOT NULL
                    UNION
                    SELECT XeBaiSo10Id
                    FROM dbo.App_KhoaHoc_NhomDaoTao WHERE XeBaiSo10Id IS NOT NULL
                ) sourceRows
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM #AssignedVehicle targetRows
                    WHERE targetRows.XeTapId=sourceRows.valueId
                );';
        IF OBJECT_ID(N'dbo.App_KhoaHoc_XeTap',N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'XeTapId') IS NOT NULL
            EXEC sys.sp_executesql N'
                INSERT INTO #AssignedVehicle(XeTapId)
                SELECT DISTINCT sourceRows.XeTapId
                FROM dbo.App_KhoaHoc_XeTap sourceRows
                WHERE sourceRows.XeTapId IS NOT NULL
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM #AssignedVehicle targetRows
                      WHERE targetRows.XeTapId=sourceRows.XeTapId
                  );';
        SELECT vehicle.XeTapId,vehicle.SourceProfileCode,vehicle.SourceBienSoXe,
               vehicle.BienSoXe,vehicle.NormalizedBienSoXe,vehicle.SourceRowHash,
               vehicle.SourceTrangThai,vehicle.SourceLifecycle,
               vehicle.ManualReviewCode,vehicle.SoDK,vehicle.NormalizedSoDK,
               vehicle.SoKhung,vehicle.NormalizedSoKhung,
               vehicle.SoDongCo,vehicle.NormalizedSoDongCo,
               vehicle.IsDeleted,
               CONVERT(bit,CASE WHEN assignment.XeTapId IS NULL THEN 0 ELSE 1 END)
                   AS HasActiveAssignments,
               vehicle.RowVersion
        FROM dbo.App_XeTap vehicle
        LEFT JOIN #AssignedVehicle assignment ON assignment.XeTapId=vehicle.XeTapId;
        """;

    internal const string ReadLockedInventorySql = """
        CREATE TABLE #AssignedVehicle(XeTapId bigint NOT NULL PRIMARY KEY);
        IF OBJECT_ID(N'dbo.App_HocVien_PhanCong',N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_HocVien_PhanCong',N'XeTapId') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_HocVien_PhanCong',N'XeBaiSo10Id') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_HocVien_PhanCong',N'IsCurrent') IS NOT NULL
            EXEC sys.sp_executesql N'
                INSERT INTO #AssignedVehicle(XeTapId)
                SELECT valueId FROM
                (
                    SELECT XeTapId AS valueId
                    FROM dbo.App_HocVien_PhanCong WITH(HOLDLOCK)
                    WHERE IsCurrent=1 AND XeTapId IS NOT NULL
                    UNION
                    SELECT XeBaiSo10Id
                    FROM dbo.App_HocVien_PhanCong WITH(HOLDLOCK)
                    WHERE IsCurrent=1 AND XeBaiSo10Id IS NOT NULL
                ) sourceRows;';
        IF OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao',N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_KhoaHoc_NhomDaoTao',N'XeTapId') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_KhoaHoc_NhomDaoTao',N'XeBaiSo10Id') IS NOT NULL
            EXEC sys.sp_executesql N'
                INSERT INTO #AssignedVehicle(XeTapId)
                SELECT valueId FROM
                (
                    SELECT XeTapId AS valueId
                    FROM dbo.App_KhoaHoc_NhomDaoTao WITH(HOLDLOCK)
                    WHERE XeTapId IS NOT NULL
                    UNION
                    SELECT XeBaiSo10Id
                    FROM dbo.App_KhoaHoc_NhomDaoTao WITH(HOLDLOCK)
                    WHERE XeBaiSo10Id IS NOT NULL
                ) sourceRows
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM #AssignedVehicle targetRows
                    WHERE targetRows.XeTapId=sourceRows.valueId
                );';
        IF OBJECT_ID(N'dbo.App_KhoaHoc_XeTap',N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'XeTapId') IS NOT NULL
            EXEC sys.sp_executesql N'
                INSERT INTO #AssignedVehicle(XeTapId)
                SELECT DISTINCT sourceRows.XeTapId
                FROM dbo.App_KhoaHoc_XeTap sourceRows WITH(HOLDLOCK)
                WHERE sourceRows.XeTapId IS NOT NULL
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM #AssignedVehicle targetRows
                      WHERE targetRows.XeTapId=sourceRows.XeTapId
                  );';
        SELECT vehicle.XeTapId,vehicle.SourceProfileCode,vehicle.SourceBienSoXe,
               vehicle.BienSoXe,vehicle.NormalizedBienSoXe,vehicle.SourceRowHash,
               vehicle.SourceTrangThai,vehicle.SourceLifecycle,
               vehicle.ManualReviewCode,vehicle.SoDK,vehicle.NormalizedSoDK,
               vehicle.SoKhung,vehicle.NormalizedSoKhung,
               vehicle.SoDongCo,vehicle.NormalizedSoDongCo,
               vehicle.IsDeleted,
               CONVERT(bit,CASE WHEN assignment.XeTapId IS NULL THEN 0 ELSE 1 END)
                   AS HasActiveAssignments,
               vehicle.RowVersion
        FROM dbo.App_XeTap vehicle WITH(UPDLOCK,HOLDLOCK)
        LEFT JOIN #AssignedVehicle assignment ON assignment.XeTapId=vehicle.XeTapId;
        """;

    internal const string InsertVehicleSql = """
        INSERT INTO dbo.App_XeTap
        (
            BienSoXe,SoDK,SoHuu,XeCuaCoSoDaoTao,XeHopDong,NhanHieu,LoaiXe,
            MacXe,HangXe,HangGPLXXe,MauXe,NamSX,SoDongCo,SoKhung,
            GiayPhepXTL,SoGPXTL,CoQuanCapGPXTL,NgayCapGPXTL,NgayHetHanGPXTL,
            HeThongPhanhPhu,BaoHiem,TuyenDuong,ChatLuong,NgayCapGCNKD,
            NgayHetHanGCNKD,GhiChuV2,SourceOfTruth,
            SourceProfileCode,SourceBienSoXe,NormalizedBienSoXe,NormalizedSoDK,
            NormalizedSoKhung,NormalizedSoDongCo,MaCSDT,MaSoGTVT,
            SourceRowHash,SourceTrangThai,SourceLifecycle,SourceCtVersion,
            SourceLastSeenAt,SourceMissingSince,ManualReviewCode,ManualReviewAt,
            SourceCreatedBy,SourceUpdatedBy,SourceCreatedAt,SourceUpdatedAt,
            SourceImagePathHash,SourceMaFileTiepNhanXml,
            SourceThoiGianTiepNhanXml
        )
        OUTPUT INSERTED.XeTapId
        VALUES
        (
            @BienSoXe,@SoDK,@SoHuu,@XeCuaCoSoDaoTao,@XeHopDong,@NhanHieu,@LoaiXe,
            @MacXe,@HangXe,@HangGPLXXe,@MauXe,@NamSX,@SoDongCo,@SoKhung,
            @GiayPhepXTL,@SoGPXTL,@CoQuanCapGPXTL,@NgayCapGPXTL,@NgayHetHanGPXTL,
            @HeThongPhanhPhu,@BaoHiem,@TuyenDuong,@ChatLuong,@NgayCapGCNKD,
            @NgayHetHanGCNKD,@GhiChuV2,@SourceOfTruth,
            @SourceProfileCode,@SourceBienSoXe,@NormalizedBienSoXe,@NormalizedSoDK,
            @NormalizedSoKhung,@NormalizedSoDongCo,@MaCSDT,@MaSoGTVT,
            @SourceRowHash,@SourceTrangThai,@SourceLifecycle,@SourceCtVersion,
            @Now,NULL,NULL,NULL,@SourceCreatedBy,@SourceUpdatedBy,
            @SourceCreatedAt,@SourceUpdatedAt,@SourceImagePathHash,
            @SourceMaFileTiepNhanXml,@SourceThoiGianTiepNhanXml
        );
        """;

    internal const string UpdateVehicleSql = """
        UPDATE dbo.App_XeTap
        SET BienSoXe=@BienSoXe,SoDK=@SoDK,SoHuu=@SoHuu,
            XeCuaCoSoDaoTao=@XeCuaCoSoDaoTao,XeHopDong=@XeHopDong,
            NhanHieu=@NhanHieu,LoaiXe=@LoaiXe,MacXe=@MacXe,HangXe=@HangXe,
            HangGPLXXe=@HangGPLXXe,MauXe=@MauXe,NamSX=@NamSX,
            SoDongCo=@SoDongCo,SoKhung=@SoKhung,GiayPhepXTL=@GiayPhepXTL,
            SoGPXTL=@SoGPXTL,CoQuanCapGPXTL=@CoQuanCapGPXTL,
            NgayCapGPXTL=@NgayCapGPXTL,NgayHetHanGPXTL=@NgayHetHanGPXTL,
            HeThongPhanhPhu=@HeThongPhanhPhu,BaoHiem=@BaoHiem,
            TuyenDuong=@TuyenDuong,ChatLuong=@ChatLuong,
            NgayCapGCNKD=@NgayCapGCNKD,NgayHetHanGCNKD=@NgayHetHanGCNKD,
            GhiChuV2=@GhiChuV2,SourceOfTruth=@SourceOfTruth,
            NormalizedBienSoXe=@NormalizedBienSoXe,NormalizedSoDK=@NormalizedSoDK,
            NormalizedSoKhung=@NormalizedSoKhung,
            NormalizedSoDongCo=@NormalizedSoDongCo,
            MaCSDT=@MaCSDT,MaSoGTVT=@MaSoGTVT,SourceRowHash=@SourceRowHash,
            SourceTrangThai=@SourceTrangThai,SourceLifecycle=@SourceLifecycle,
            SourceCtVersion=@SourceCtVersion,SourceLastSeenAt=@Now,
            SourceMissingSince=NULL,ManualReviewCode=NULL,ManualReviewAt=NULL,
            SourceCreatedBy=@SourceCreatedBy,SourceUpdatedBy=@SourceUpdatedBy,
            SourceCreatedAt=@SourceCreatedAt,SourceUpdatedAt=@SourceUpdatedAt,
            SourceImagePathHash=@SourceImagePathHash,
            SourceMaFileTiepNhanXml=@SourceMaFileTiepNhanXml,
            SourceThoiGianTiepNhanXml=@SourceThoiGianTiepNhanXml
        WHERE XeTapId=@TargetXeTapId
          AND SourceProfileCode=@SourceProfileCode
          AND SourceBienSoXe=@SourceBienSoXe
          AND RowVersion=@ExpectedTargetRowVersion;
        """;

    internal const string MarkMissingSql = """
        UPDATE dbo.App_XeTap
        SET SourceTrangThai=0,SourceLifecycle=N'SOURCE_MISSING',
            SourceCtVersion=@SourceCtVersion,
            SourceMissingSince=COALESCE(SourceMissingSince,@Now)
        WHERE XeTapId=@TargetXeTapId
          AND SourceProfileCode=@SourceProfileCode
          AND SourceBienSoXe=@SourceBienSoXe
          AND RowVersion=@ExpectedTargetRowVersion
          AND IsDeleted=0;
        """;

    internal const string InsertManualReviewSql = """
        INSERT INTO dbo.App_XeTap_RealtimeManualReview
        (
            CycleId,SourceProfileCode,SourceCtVersion,SourceBienSoXe,
            ReviewCode,CollisionField,TargetXeTapId,ConflictingXeTapId,
            SourceRowHash,HasActiveAssignment,Status,DetectedAt
        )
        VALUES
        (
            @CycleId,@SourceProfileCode,@SourceCtVersion,@SourceBienSoXe,
            @ReviewCode,@CollisionField,@TargetXeTapId,@ConflictingXeTapId,
            @SourceRowHash,@HasActiveAssignment,N'OPEN',@DetectedAt
        );
        """;

    internal const string InsertEventSql = """
        INSERT INTO dbo.App_XeTap_RealtimeEvent
        (
            CycleId,SourceProfileCode,SourceCtVersion,SourceBienSoXe,
            ChangeKind,Action,SourceRowHash,TargetXeTapId,PlanToken,AppliedAt
        )
        VALUES
        (
            @CycleId,@SourceProfileCode,@SourceCtVersion,@SourceBienSoXe,
            @ChangeKind,@Action,@SourceRowHash,@TargetXeTapId,@PlanToken,@AppliedAt
        );
        """;

    internal const string AdvanceCheckpointSql = """
        UPDATE dbo.App_XeTap_RealtimeCheckpoint
        SET SourceDatabaseGuid=@SourceDatabaseGuid,LastCtVersion=@LastCtVersion,
            MappingFingerprint=@MappingFingerprint,
            SourceSchemaFingerprint=@SourceSchemaFingerprint,
            LastCycleId=@CycleId,LastPlanToken=@PlanToken,
            LastErrorCode=NULL,LastErrorAt=NULL,UpdatedAt=@UpdatedAt
        WHERE SourceProfileCode=@SourceProfileCode
          AND LastCtVersion=@CheckpointBefore
          AND RowVersion=@ExpectedRowVersion
          AND State=N'ACTIVE';
        """;
}

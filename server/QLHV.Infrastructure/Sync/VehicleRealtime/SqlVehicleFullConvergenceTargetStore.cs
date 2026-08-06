using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.VehicleRealtime;

namespace QLHV.Infrastructure.Sync.VehicleRealtime;

internal sealed class SqlVehicleFullConvergenceTargetStore :
    IVehicleFullConvergenceTargetStore
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly IConfiguration _configuration;

    public SqlVehicleFullConvergenceTargetStore(
        IConnectionSettingsProvider connections,
        IConfiguration configuration)
    {
        _connections = connections;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<VehicleTargetSnapshot>> ReadInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(cancellationToken);
        await using var connection = new SqlConnection(resolved);
        await connection.OpenAsync(cancellationToken);
        await SqlVehicleRealtimeTargetStore.ValidateTargetIdentityAsync(
            connection,
            transaction: null,
            cancellationToken,
            ExpectedTargetGuid(connection));
        return (await connection.QueryAsync<VehicleTargetSnapshot>(
            new CommandDefinition(
                SqlVehicleRealtimeTargetStore.ReadInventorySql,
                commandTimeout: 30,
                cancellationToken: cancellationToken))).ToArray();
    }

    public async Task<VehicleFullConvergenceResult> CommitAsync(
        VehicleFullConvergencePlan plan,
        IReadOnlyList<VehicleTargetSnapshot> expectedInventory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(expectedInventory);
        var route = VehicleRealtimeRouteCatalog.GetRequired(plan.SourceProfileCode);
        if (plan.RecoveryId == Guid.Empty ||
            plan.SourceDatabaseGuid != ExpectedSourceGuid(route) ||
            plan.AnchorVersion < 0 ||
            !string.Equals(
                plan.MappingFingerprint,
                VehicleSourceMapper.ComputeMappingFingerprint(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "Vehicle recovery plan identity/fingerprint is invalid.");
        }

        var resolved = await ResolveAsync(cancellationToken);
        await using var connection = new SqlConnection(resolved);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        try
        {
            await SqlVehicleRealtimeTargetStore.ValidateTargetIdentityAsync(
                connection,
                transaction,
                cancellationToken,
                ExpectedTargetGuid(connection));
            var lockResult = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    SqlVehicleRealtimeTargetStore.AcquireVehicleLockSql,
                    transaction: transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
            if (lockResult < 0)
            {
                throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.TargetChangedDuringPlan,
                    "Vehicle full-convergence domain lock is unavailable.");
            }

            var lockedInventory =
                (await connection.QueryAsync<VehicleTargetSnapshot>(
                    new CommandDefinition(
                        SqlVehicleRealtimeTargetStore.ReadLockedInventorySql,
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken))).ToArray();
            RequireInventoryUnchanged(expectedInventory, lockedInventory);
            SqlVehicleRealtimeTargetStore.RevalidatePlans(
                new VehicleRealtimeSealedPlan(
                    plan.RecoveryId,
                    plan.SourceProfileCode,
                    plan.SourceDatabaseGuid,
                    CheckpointBefore: plan.AnchorVersion,
                    CheckpointAfter: plan.AnchorVersion,
                    SealedSourceVersion: plan.AnchorVersion,
                    plan.MappingFingerprint,
                    plan.SourceSchemaFingerprint,
                    plan.PlanToken,
                    plan.Rows),
                lockedInventory);

            var now = DateTime.UtcNow;
            var inserted = 0;
            var updated = 0;
            var inactive = 0;
            var missing = 0;
            var manual = 0;
            var noChange = 0;
            foreach (var row in plan.Rows.OrderBy(
                         item => item.SourceBienSoXe,
                         StringComparer.OrdinalIgnoreCase))
            {
                switch (row.Action)
                {
                    case VehicleRealtimeActions.InsertSourceRow:
                        _ = await connection.ExecuteScalarAsync<long>(
                            new CommandDefinition(
                                SqlVehicleRealtimeTargetStore.InsertVehicleSql,
                                SqlVehicleRealtimeTargetStore.WriteParameters(row, now),
                                transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                        inserted++;
                        break;
                    case VehicleRealtimeActions.UpdateSourceOwnedFields:
                    case VehicleRealtimeActions.MarkSourceInactive:
                        var affected = await connection.ExecuteAsync(
                            new CommandDefinition(
                                SqlVehicleRealtimeTargetStore.UpdateVehicleSql,
                                SqlVehicleRealtimeTargetStore.WriteParameters(row, now),
                                transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                        SqlVehicleRealtimeTargetStore.RequireOne(affected);
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
                                SqlVehicleRealtimeTargetStore.MarkMissingSql,
                                new
                                {
                                    row.TargetXeTapId,
                                    row.ExpectedTargetRowVersion,
                                    row.SourceProfileCode,
                                    row.SourceBienSoXe,
                                    SourceCtVersion = plan.AnchorVersion,
                                    Now = now,
                                },
                                transaction,
                                commandTimeout: 30,
                                cancellationToken: cancellationToken));
                        SqlVehicleRealtimeTargetStore.RequireOne(missingAffected);
                        missing++;
                        break;
                    case VehicleRealtimeActions.ManualReview:
                        await InsertManualReviewAsync(
                            connection,
                            transaction,
                            plan,
                            row,
                            lockedInventory,
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
                            "Vehicle full-convergence plan has an unknown action.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new VehicleFullConvergenceResult(
                plan.RecoveryId,
                plan.SourceProfileCode,
                plan.AnchorVersion,
                plan.Rows.Count(row => row.Source is not null),
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

    private static void RequireInventoryUnchanged(
        IReadOnlyCollection<VehicleTargetSnapshot> expected,
        IReadOnlyCollection<VehicleTargetSnapshot> current)
    {
        var expectedToken = InventoryToken(expected);
        var currentToken = InventoryToken(current);
        if (!string.Equals(expectedToken, currentToken, StringComparison.Ordinal))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.TargetChangedDuringPlan,
                "Vehicle inventory/assignment evidence changed before commit.");
        }
    }

    private static string InventoryToken(
        IEnumerable<VehicleTargetSnapshot> rows)
        => string.Join(
            "|",
            rows.OrderBy(row => row.XeTapId).Select(row =>
                string.Join(
                    ":",
                    row.XeTapId,
                    Convert.ToHexString(row.RowVersion),
                    row.HasActiveAssignments ? "1" : "0",
                    row.SourceProfileCode ?? string.Empty,
                    row.SourceBienSoXe ?? string.Empty)));

    private static async Task InsertManualReviewAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VehicleFullConvergencePlan plan,
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
        await connection.ExecuteAsync(new CommandDefinition(
            InsertRecoveryManualReviewSql,
            new
            {
                CycleId = plan.RecoveryId,
                row.SourceProfileCode,
                SourceCtVersion = plan.AnchorVersion,
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
    }

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
        if (!string.Equals(
                connection.DataSource.Trim(),
                @"CSDLTTTC\QLHVRT02",
                StringComparison.OrdinalIgnoreCase) ||
            !_configuration.GetValue<bool>(
                "TeacherVehicleProjection:DisposableRehearsalEnabled"))
        {
            return VehicleRealtimeTargetDatabase.ExpectedProductionDatabaseGuid;
        }

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

    private async Task<string> ResolveAsync(CancellationToken cancellationToken)
    {
        var resolved =
            await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!resolved.IsUsable || string.IsNullOrWhiteSpace(resolved.ConnectionString))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.NotInitialized,
                "QLHV_APP connection is unavailable for vehicle recovery.");
        }

        return resolved.ConnectionString;
    }

    internal const string InsertRecoveryManualReviewSql = """
        IF EXISTS
        (
            SELECT 1
            FROM dbo.App_XeTap_RealtimeManualReview WITH(UPDLOCK,HOLDLOCK)
            WHERE SourceProfileCode=@SourceProfileCode
              AND SourceCtVersion=@SourceCtVersion
              AND SourceBienSoXe=@SourceBienSoXe
              AND CycleId<>@CycleId
        )
            THROW 528560,'VEHICLE_RECOVERY_MANUAL_REVIEW_CONFLICT',1;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.App_XeTap_RealtimeManualReview WITH(UPDLOCK,HOLDLOCK)
            WHERE SourceProfileCode=@SourceProfileCode
              AND SourceCtVersion=@SourceCtVersion
              AND SourceBienSoXe=@SourceBienSoXe
              AND CycleId=@CycleId
        )
            INSERT dbo.App_XeTap_RealtimeManualReview
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
}

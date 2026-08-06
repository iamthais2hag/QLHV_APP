using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

public sealed class Rt03ProductionRuntimeStateStore : IRt03ProductionRuntimeStateStore
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _syncOptions;

    public Rt03ProductionRuntimeStateStore(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> syncOptions)
    {
        _connections = connections;
        _syncOptions = syncOptions.Value;
    }

    public async Task<Rt03ProductionFeatureState> ReadFeatureStateAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<Rt03ProductionFeatureState>(
            new CommandDefinition(
                FeatureStateSql,
                commandTimeout: _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Rt03ProductionProfileState>> ReadProfileStatesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return (await connection.QueryAsync<Rt03ProductionProfileState>(
            new CommandDefinition(
                ProfileStateSql,
                commandTimeout: _syncOptions.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
    }

    public async Task RecordWorkerAsync(
        string instanceId,
        string status,
        string? currentProfile,
        bool cycleActive,
        string? lastErrorCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            RecordWorkerSql,
            new
            {
                InstanceId = instanceId,
                Status = status,
                CurrentProfile = currentProfile,
                CycleActive = cycleActive,
                LastErrorCode = SafeError(lastErrorCode),
            },
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    public async Task RecordCycleAsync(
        string instanceId,
        Rt03ProductionCycleResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                RecordCycleSql,
                new
                {
                    InstanceId = instanceId,
                    result.CycleId,
                    result.SourceProfileCode,
                    result.Status,
                    result.CheckpointBefore,
                    result.CheckpointAfter,
                    result.InsertedRows,
                    result.UpdatedRows,
                    result.DeletedOrDeactivatedRows,
                    result.DuplicateActiveRows,
                    result.ReviewedRetainedCount,
                    ReviewedRetainedDomains = string.Join(",", result.ReviewedRetainedDomains),
                    result.ActiveReviewCount,
                    result.StaleReviewCount,
                    result.NewDriftCount,
                    result.OldestActiveReviewUtc,
                    result.NewestActiveReviewUtc,
                    CycleOutcome = string.IsNullOrWhiteSpace(result.CycleOutcome)
                        ? result.Status
                        : result.CycleOutcome,
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

    public async Task RecordIdleAsync(
        string instanceId,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            RecordIdleSql,
            new { InstanceId = instanceId, Outcome = SafeError(outcome) },
            commandTimeout: _syncOptions.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "QLHV_APP connection is unavailable for RT-03 runtime state.");
        }

        var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string? SafeError(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= 100 ? value : value[..100];

    internal const string FeatureStateSql = """
        SELECT EnableProductionRealtime, EnableProductionShadow,
               EnableProductionWrites, EnableProductionCanary,
               EnableControlledCutover, EnableProductionDeletes
        FROM dbo.App_QlhvDirectRealtimeFeatureState
        WHERE FeatureStateId = 1;
        """;

    internal const string ProfileStateSql = """
        SELECT SourceProfileCode, Enabled, CONVERT(int, SequenceOrder) AS SequenceOrder,
               ExpectedMappingFingerprint, ExpectedSourceSchemaFingerprint,
               ExpectedTargetSchemaFingerprint, LastStatus
        FROM dbo.App_QlhvDirectRealtimeProfileState
        ORDER BY SequenceOrder;
        """;

    internal const string RecordWorkerSql = """
        DECLARE @NowUtc datetime2(7)=SYSUTCDATETIME();
        UPDATE dbo.App_QlhvDirectRealtimeWorkerState WITH (UPDLOCK, HOLDLOCK)
        SET InstanceId=@InstanceId, Status=@Status, CurrentProfile=@CurrentProfile,
            CycleActive=@CycleActive, LastHeartbeatUtc=@NowUtc,
            LastErrorCode=@LastErrorCode,
            StartedAtUtc=CASE WHEN InstanceId<>@InstanceId THEN @NowUtc ELSE StartedAtUtc END,
            StoppedAtUtc=CASE WHEN @Status=N'STOPPED' THEN @NowUtc ELSE NULL END,
            CurrentCycleStartedAtUtc=CASE
                WHEN @CycleActive=1 AND CycleActive=0 THEN @NowUtc
                WHEN @CycleActive=0 THEN NULL
                ELSE CurrentCycleStartedAtUtc END,
            LastCycleFailedAtUtc=CASE
                WHEN @Status=N'BLOCKED' THEN @NowUtc
                ELSE LastCycleFailedAtUtc END
        WHERE WorkerStateId=1;
        IF @@ROWCOUNT<>1 THROW 527590, 'RT03_WORKER_STATE_MISSING', 1;
        """;

    internal const string RecordCycleSql = """
        DECLARE @DatabaseUtcNow datetime2(7)=SYSUTCDATETIME();
        INSERT INTO dbo.App_QlhvDirectRealtimeCycleHistory
        (
            CycleId, WorkerInstanceId, SourceProfileCode, Status,
            CheckpointBefore, CheckpointAfter, InsertedRows, UpdatedRows,
            DeletedOrDeactivatedRows, DuplicateActiveRows, CompletedAtUtc,
            ReviewedRetainedCount, ReviewedRetainedDomains, ActiveReviewCount,
            StaleReviewCount, NewDriftCount, OldestActiveReviewUtc,
            NewestActiveReviewUtc, CycleOutcome
        )
        VALUES
        (
            @CycleId, @InstanceId, @SourceProfileCode, @Status,
            @CheckpointBefore, @CheckpointAfter, @InsertedRows, @UpdatedRows,
            @DeletedOrDeactivatedRows, @DuplicateActiveRows, @DatabaseUtcNow,
            @ReviewedRetainedCount, @ReviewedRetainedDomains, @ActiveReviewCount,
            @StaleReviewCount, @NewDriftCount, @OldestActiveReviewUtc,
            @NewestActiveReviewUtc, @CycleOutcome
        );

        UPDATE dbo.App_QlhvDirectRealtimeProfileState
        SET LastStatus=@Status, LastSuccessfulCycleId=@CycleId,
            LastCheckpointVersion=@CheckpointAfter,
            LastCycleCompletedAtUtc=@DatabaseUtcNow
        WHERE SourceProfileCode=@SourceProfileCode;
        IF @@ROWCOUNT<>1 THROW 527591, 'RT03_PROFILE_STATE_MISSING', 1;

        UPDATE dbo.App_QlhvDirectRealtimeWorkerState
        SET Status=N'HEALTHY', CurrentProfile=NULL, CycleActive=0,
            LastHeartbeatUtc=@DatabaseUtcNow, LastSuccessfulCycleId=@CycleId,
            LastErrorCode=NULL,
            ReviewedRetainedCount=@ReviewedRetainedCount,
            ReviewedRetainedDomains=@ReviewedRetainedDomains,
            ActiveReviewCount=@ActiveReviewCount,
            StaleReviewCount=@StaleReviewCount,
            NewDriftCount=@NewDriftCount,
            OldestActiveReviewUtc=@OldestActiveReviewUtc,
            NewestActiveReviewUtc=@NewestActiveReviewUtc,
            CycleOutcome=@CycleOutcome
        WHERE WorkerStateId=1 AND InstanceId=@InstanceId;
        IF @@ROWCOUNT<>1 THROW 527592, 'RT03_WORKER_INSTANCE_CONFLICT', 1;
        """;

    internal const string RecordIdleSql = """
        UPDATE dbo.App_QlhvDirectRealtimeWorkerState
        SET Status=N'HEALTHY_IDLE', CurrentProfile=NULL, CycleActive=0,
            LastHeartbeatUtc=SYSUTCDATETIME(), LastErrorCode=NULL,
            CycleOutcome=@Outcome
        WHERE WorkerStateId=1 AND InstanceId=@InstanceId;
        IF @@ROWCOUNT<>1 THROW 527592, 'RT03_WORKER_INSTANCE_CONFLICT', 1;
        """;
}

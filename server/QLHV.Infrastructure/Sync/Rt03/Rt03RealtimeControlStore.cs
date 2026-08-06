using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

public sealed class Rt03RealtimeControlStore : IRt03RealtimeControlStore
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public Rt03RealtimeControlStore(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<Rt03RealtimeControlRecord> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadControlAsync(connection, transaction: null, cancellationToken);
    }

    public async Task<Rt03RealtimeControlRecord> ChangeStateAsync(
        string state,
        string actor,
        string? reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!Rt03RealtimeControlStates.IsValid(state) ||
            string.Equals(state, Rt03RealtimeControlStates.Blocked, StringComparison.Ordinal))
        {
            throw new ArgumentException("Only OFF or ON can be selected by an operator.",
                nameof(state));
        }

        var safeActor = SafeActor(actor);
        var safeReason = SafeReason(reason);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var before = await ReadControlForUpdateAsync(
                connection, transaction, cancellationToken);
            if (!before.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
            {
                throw new Rt03RealtimeControlConcurrencyException();
            }

            if (string.Equals(before.State, state, StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken);
                return before;
            }

            var after = await connection.QuerySingleAsync<ControlRow>(new CommandDefinition(
                ChangeStateSql,
                new { State = state, Actor = safeActor, Reason = safeReason },
                transaction,
                _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await InsertAuditAsync(connection, transaction, before, after,
                safeActor, "OPERATOR_CONTROL_CHANGE", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(after);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Rt03RealtimeControlRecord> TransitionToBlockedAsync(
        string actor,
        string redactedReasonCode,
        CancellationToken cancellationToken = default)
    {
        var safeActor = SafeActor(actor);
        var safeReason = SafeReason(redactedReasonCode);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var before = await ReadControlForUpdateAsync(
                connection, transaction, cancellationToken);
            if (string.Equals(before.State, Rt03RealtimeControlStates.Blocked,
                    StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken);
                return before;
            }

            var after = await connection.QuerySingleAsync<ControlRow>(new CommandDefinition(
                ChangeStateSql,
                new
                {
                    State = Rt03RealtimeControlStates.Blocked,
                    Actor = safeActor,
                    Reason = safeReason,
                },
                transaction,
                _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await InsertAuditAsync(connection, transaction, before, after,
                safeActor, "WORKER_FAIL_CLOSED", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(after);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Rt03RealtimeRunRequest> QueueRunOnceAsync(
        string actor,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var control = await ReadControlForUpdateAsync(
                connection, transaction, cancellationToken);
            if (!string.Equals(control.State, Rt03RealtimeControlStates.On,
                    StringComparison.Ordinal))
            {
                throw new Rt03SafetyException(
                    Rt03RealtimeMasterErrors.InvalidControlState,
                    "Run-once is available only while Master Realtime is ON.");
            }

            var existing = await connection.QuerySingleOrDefaultAsync<RunRequestRow>(
                new CommandDefinition(
                    ReadActiveRunSql,
                    transaction: transaction,
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken));
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ToRunRequest(existing);
            }

            var created = await connection.QuerySingleAsync<RunRequestRow>(
                new CommandDefinition(
                    InsertRunSql,
                    new { Actor = SafeActor(actor) },
                    transaction,
                    _options.TimeoutSeconds,
                    cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return ToRunRequest(created);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Rt03RealtimeRunRequest?> TryClaimRunOnceAsync(
        string workerInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RunRequestRow>(
            new CommandDefinition(
                ClaimRunSql,
                new { WorkerInstanceId = SafeActor(workerInstanceId) },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
        return row is null ? null : ToRunRequest(row);
    }

    public async Task CompleteRunOnceAsync(
        Guid runRequestId,
        string status,
        string outcome,
        string? redactedReasonCode,
        CancellationToken cancellationToken = default)
    {
        if (status is not (Rt03RealtimeRunRequestStatuses.Completed or
            Rt03RealtimeRunRequestStatuses.Blocked))
        {
            throw new ArgumentException("Run-once terminal status is invalid.", nameof(status));
        }

        await using var connection = await OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            CompleteRunSql,
            new
            {
                RunRequestId = runRequestId,
                Status = status,
                Outcome = SafeReason(outcome),
                Reason = SafeReason(redactedReasonCode),
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (rows != 1)
        {
            throw new Rt03SafetyException(
                Rt03RealtimeMasterErrors.ControlConcurrencyConflict,
                "Run-once request is no longer owned by this worker.");
        }
    }

    public async Task<Rt03RealtimeRunRequest?> ReadActiveRunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RunRequestRow>(
            new CommandDefinition(
                ReadActiveRunSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
        return row is null ? null : ToRunRequest(row);
    }

    public async Task<Rt03RealtimeWorkerSnapshot> ReadWorkerSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<Rt03RealtimeWorkerSnapshot>(
            new CommandDefinition(
                ReadWorkerSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03RealtimeMasterErrors.ControlUnavailable,
                "QLHV_APP realtime control connection is unavailable.");
        }

        var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task<Rt03RealtimeControlRecord> ReadControlAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<ControlRow>(new CommandDefinition(
            ReadControlSql,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        return ToRecord(row);
    }

    private async Task<Rt03RealtimeControlRecord> ReadControlForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<ControlRow>(new CommandDefinition(
            ReadControlForUpdateSql,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        return ToRecord(row);
    }

    private async Task InsertAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Rt03RealtimeControlRecord before,
        ControlRow after,
        string actor,
        string action,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            InsertAuditSql,
            new
            {
                BeforeState = before.State,
                AfterState = after.State,
                Actor = actor,
                Action = action,
                Reason = after.Reason,
                BeforeRowVersion = before.RowVersion,
                AfterRowVersion = after.RowVersion,
            },
            transaction,
            _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private static Rt03RealtimeControlRecord ToRecord(ControlRow row) =>
        new(row.State, row.UpdatedAtUtc, row.UpdatedBy, row.Reason, row.RowVersion);

    private static Rt03RealtimeRunRequest ToRunRequest(RunRequestRow row) =>
        new(row.RunRequestId, row.Status, row.RequestedBy, row.RequestedAtUtc,
            row.WorkerInstanceId);

    private static string SafeActor(string value) =>
        string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim()[..Math.Min(100, value.Trim().Length)];

    private static string? SafeReason(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(100, value.Trim().Length)];

    private sealed class ControlRow
    {
        public string State { get; init; } = string.Empty;
        public DateTime UpdatedAtUtc { get; init; }
        public string UpdatedBy { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public byte[] RowVersion { get; init; } = [];
    }

    private sealed class RunRequestRow
    {
        public Guid RunRequestId { get; init; }
        public string Status { get; init; } = string.Empty;
        public string RequestedBy { get; init; } = string.Empty;
        public DateTime RequestedAtUtc { get; init; }
        public string? WorkerInstanceId { get; init; }
    }

    internal const string ReadControlSql = """
        SELECT State, UpdatedAtUtc, UpdatedBy, Reason, RowVersion
        FROM dbo.App_Rt03RealtimeControl
        WHERE ControlId=1;
        """;

    internal const string ReadControlForUpdateSql = """
        SELECT State, UpdatedAtUtc, UpdatedBy, Reason, RowVersion
        FROM dbo.App_Rt03RealtimeControl WITH (UPDLOCK,HOLDLOCK)
        WHERE ControlId=1;
        """;

    internal const string ChangeStateSql = """
        UPDATE dbo.App_Rt03RealtimeControl
        SET State=@State, UpdatedAtUtc=SYSUTCDATETIME(), UpdatedBy=@Actor, Reason=@Reason
        OUTPUT inserted.State, inserted.UpdatedAtUtc, inserted.UpdatedBy,
               inserted.Reason, inserted.RowVersion
        WHERE ControlId=1;
        """;

    internal const string InsertAuditSql = """
        INSERT dbo.App_Rt03RealtimeControlAudit
        (
            BeforeState, AfterState, Action, Actor, Reason, OccurredAtUtc,
            BeforeRowVersion, AfterRowVersion
        )
        VALUES
        (
            @BeforeState, @AfterState, @Action, @Actor, @Reason,
            SYSUTCDATETIME(), @BeforeRowVersion, @AfterRowVersion
        );
        """;

    internal const string ReadActiveRunSql = """
        SELECT TOP (1) RunRequestId, Status, RequestedBy, RequestedAtUtc,
               WorkerInstanceId
        FROM dbo.App_Rt03RealtimeRunRequest WITH (UPDLOCK,HOLDLOCK)
        WHERE ActiveSlot=1
        ORDER BY RequestedAtUtc, RunRequestId;
        """;

    internal const string InsertRunSql = """
        DECLARE @RunRequestId uniqueidentifier=NEWID();
        INSERT dbo.App_Rt03RealtimeRunRequest
        (
            RunRequestId, Status, RequestedBy, RequestedAtUtc, ActiveSlot
        )
        VALUES
        (
            @RunRequestId, N'PENDING', @Actor, SYSUTCDATETIME(), 1
        );
        SELECT RunRequestId, Status, RequestedBy, RequestedAtUtc, WorkerInstanceId
        FROM dbo.App_Rt03RealtimeRunRequest
        WHERE RunRequestId=@RunRequestId;
        """;

    internal const string ClaimRunSql = """
        DECLARE @Claimed TABLE
        (
            RunRequestId uniqueidentifier, Status nvarchar(20),
            RequestedBy nvarchar(100), RequestedAtUtc datetime2(7),
            WorkerInstanceId nvarchar(64)
        );
        ;WITH candidate AS
        (
            SELECT TOP (1) *
            FROM dbo.App_Rt03RealtimeRunRequest WITH (UPDLOCK,READPAST,ROWLOCK)
            WHERE Status=N'PENDING' AND ActiveSlot=1
            ORDER BY RequestedAtUtc, RunRequestId
        )
        UPDATE candidate
        SET Status=N'RUNNING', StartedAtUtc=SYSUTCDATETIME(),
            WorkerInstanceId=@WorkerInstanceId
        OUTPUT inserted.RunRequestId, inserted.Status, inserted.RequestedBy,
               inserted.RequestedAtUtc, inserted.WorkerInstanceId
        INTO @Claimed;
        SELECT * FROM @Claimed;
        """;

    internal const string CompleteRunSql = """
        UPDATE dbo.App_Rt03RealtimeRunRequest
        SET Status=@Status, CompletedAtUtc=SYSUTCDATETIME(), Outcome=@Outcome,
            Reason=@Reason, ActiveSlot=NULL
        WHERE RunRequestId=@RunRequestId AND Status=N'RUNNING' AND ActiveSlot=1;
        """;

    internal const string ReadWorkerSql = """
        SELECT worker.Status, worker.InstanceId, worker.CurrentProfile,
               worker.CycleActive, worker.LastHeartbeatUtc,
               history.CompletedAtUtc AS LastSuccessfulCycleUtc,
               worker.CycleOutcome AS LastCycleOutcome,
               worker.LastErrorCode
        FROM dbo.App_QlhvDirectRealtimeWorkerState worker
        OUTER APPLY
        (
            SELECT TOP (1) CompletedAtUtc
            FROM dbo.App_QlhvDirectRealtimeCycleHistory
            WHERE Status<>N'BLOCKED'
            ORDER BY CompletedAtUtc DESC
        ) history
        WHERE worker.WorkerStateId=1;
        """;
}

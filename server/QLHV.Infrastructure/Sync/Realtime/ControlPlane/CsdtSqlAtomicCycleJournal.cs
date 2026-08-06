using System.Data;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Infrastructure.Sync.Realtime.ControlPlane;

/// <summary>
/// Owns only its short journal transactions. The underlying repository always
/// receives and never commits a caller-owned transaction.
/// </summary>
internal sealed class CsdtSqlAtomicCycleJournal : ICsdtAtomicCycleJournal
{
    private readonly string _targetConnectionString;
    private readonly ICsdtRealtimeTargetControlPlaneRepository _repository;

    internal CsdtSqlAtomicCycleJournal(
        string targetConnectionString,
        ICsdtRealtimeTargetControlPlaneRepository repository)
    {
        _targetConnectionString = targetConnectionString;
        _repository = repository;
    }

    public Task CreatePreparingAsync(
        CsdtAtomicCycleRequest request,
        long watermark,
        CancellationToken cancellationToken = default)
        => WriteAsync(
            async (connection, transaction) =>
            {
                await _repository.CreateCycleAsync(
                    connection,
                    transaction,
                    new CreateSyncCycleRequest(
                        request.CycleId,
                        Route(request, CsdtAtomicCoreDomains.ApplyOrder[0]),
                        request.StartSourceVersion,
                        watermark,
                        CsdtAtomicCoreDomains.ApplyOrder.Count,
                        request.MappingFingerprint,
                        request.RouteFingerprint,
                        request.SourceSchemaFingerprint,
                        request.TargetSchemaFingerprint),
                    cancellationToken);
            },
            cancellationToken);

    public Task MarkStagedAsync(
        CsdtStagedCycle stagedCycle,
        CancellationToken cancellationToken = default)
        => WriteAsync(
            async (connection, transaction) =>
            {
                var changed = await _repository.MarkCycleStagedAsync(
                    connection,
                    transaction,
                    stagedCycle.CycleId,
                    stagedCycle.StagedKeySetHash,
                    cancellationToken);
                if (!changed)
                {
                    await RequireStatusAsync(
                        connection,
                        transaction,
                        stagedCycle.CycleId,
                        SyncCycleStatus.Staged,
                        cancellationToken);
                }
            },
            cancellationToken);

    public Task MarkValidatedAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            cycleId,
            SyncCycleStatus.Validated,
            (connection, transaction) => _repository.MarkCycleValidatedAsync(
                connection,
                transaction,
                cycleId,
                cancellationToken),
            cancellationToken);

    public Task MarkFailedOrConflictAsync(
        Guid cycleId,
        SyncCycleStatus status,
        string errorCode,
        CancellationToken cancellationToken = default)
        => WriteAsync(
            async (connection, transaction) =>
            {
                var changed = await _repository.MarkCycleFailedOrConflictAsync(
                    connection,
                    transaction,
                    cycleId,
                    status,
                    errorCode,
                    cancellationToken);
                if (!changed)
                {
                    await RequireStatusAsync(
                        connection,
                        transaction,
                        cycleId,
                        status,
                        cancellationToken);
                }
            },
            cancellationToken);

    public Task<CsdtTargetCycleCommitMarker?> ReadMarkerAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => ReadAsync(
            (connection, transaction) =>
                _repository.ReadCycleMarkerAsync(
                    connection,
                    transaction,
                    cycleId,
                    cancellationToken),
            cancellationToken);

    public Task MarkCheckpointPublishedAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            cycleId,
            SyncCycleStatus.CheckpointPublished,
            (connection, transaction) =>
                _repository.MarkCheckpointPublishedAsync(
                    connection,
                    transaction,
                    cycleId,
                    cancellationToken),
            cancellationToken);

    public Task MarkCompleteAsync(
        Guid cycleId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            cycleId,
            SyncCycleStatus.Complete,
            (connection, transaction) => _repository.MarkCycleCompleteAsync(
                connection,
                transaction,
                cycleId,
                cancellationToken),
            cancellationToken);

    private Task TransitionAsync(
        Guid cycleId,
        SyncCycleStatus expected,
        Func<SqlConnection, SqlTransaction, Task<bool>> transition,
        CancellationToken cancellationToken)
        => WriteAsync(
            async (connection, transaction) =>
            {
                if (!await transition(connection, transaction))
                {
                    await RequireStatusAsync(
                        connection,
                        transaction,
                        cycleId,
                        expected,
                        cancellationToken);
                }
            },
            cancellationToken);

    private async Task RequireStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid cycleId,
        SyncCycleStatus expected,
        CancellationToken cancellationToken)
    {
        var marker = await _repository.ReadCycleMarkerAsync(
            connection,
            transaction,
            cycleId,
            cancellationToken);
        if (marker?.Status != expected)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
        }
    }

    private async Task WriteAsync(
        Func<SqlConnection, SqlTransaction, Task> action,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await action(connection, transaction);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private async Task<T> ReadAsync<T>(
        Func<SqlConnection, SqlTransaction, Task<T>> read,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            var result = await read(connection, transaction);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private static MembershipRoute Route(
        CsdtAtomicCycleRequest request,
        string tableName)
        => new(
            request.Route.TargetProfileCode,
            request.Route.SourceProfileCode,
            request.Route.StreamCode,
            request.Route.MaCSDT,
            tableName);

    private static async Task SafeRollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the journal failure that caused rollback.
        }
    }
}

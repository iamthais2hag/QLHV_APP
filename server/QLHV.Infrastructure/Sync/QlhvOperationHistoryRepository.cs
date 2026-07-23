using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvOperationHistoryRepository : IQlhvOperationHistoryRepository
{
    private readonly IConnectionSettingsProvider _connections;

    public QlhvOperationHistoryRepository(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<bool> TryCreateAsync(
        QlhvOperationHistoryCreate entry,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var active = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                ActiveExistsSql,
                new { entry.Source.SourceType },
                transaction,
                cancellationToken: cancellationToken));
            if (active > 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return false;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                InsertSql,
                new
                {
                    entry.OperationId,
                    entry.Source.SourceType,
                    entry.OperationType,
                    entry.Status,
                    Actor = QlhvOperationActors.NormalizeInternal(entry.Actor),
                    entry.Source.LiveDatabaseName,
                    entry.Source.BackupDatabaseName,
                    MaCSDT = entry.Source.MaCsdt,
                    entry.Source.SourceProfileCode,
                    entry.CreatedAtUtc,
                    entry.StartedAtUtc,
                },
                transaction,
                cancellationToken: cancellationToken));
            // Once INSERT has completed, finish the durable hand-off even if the HTTP request
            // disconnects. The caller can then terminalize it with a non-cancelable cleanup.
            await transaction.CommitAsync(CancellationToken.None);
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
    }

    public async Task MarkRunningAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                MarkRunningSql,
                new { OperationId = operationId, StartedAtUtc = DateTime.UtcNow },
                cancellationToken: cancellationToken));
            if (changed != 1)
            {
                throw new InvalidOperationException("Queued refresh operation khong con o trang thai QUEUED.");
            }
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
    }

    public async Task CompleteAsync(
        QlhvOperationHistoryCompletion completion,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                CompleteSql,
                new
                {
                    completion.OperationId,
                    completion.Status,
                    completion.CompletedAtUtc,
                    completion.SourceRows,
                    completion.InsertedRows,
                    completion.UpdatedRows,
                    completion.ReactivatedRows,
                    completion.SoftDeletedRows,
                    completion.SkippedRows,
                    completion.SnapshotToken,
                    ErrorMessage = Truncate(completion.ErrorMessage, 2000),
                    completion.DetailJson,
                    completion.LiveRows,
                    completion.BackupRows,
                    completion.TargetActiveRows,
                },
                cancellationToken: cancellationToken));
            if (changed != 1)
            {
                throw new InvalidOperationException("Operation history khong ton tai hoac da ket thuc.");
            }
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
    }

    public async Task<IReadOnlyList<QlhvOperationHistoryDto>> SearchAsync(
        string sourceType,
        int take,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            var rows = await connection.QueryAsync<QlhvOperationHistoryDto>(new CommandDefinition(
                SearchSql,
                new { SourceType = sourceType, Take = Math.Clamp(take, 1, 200) },
                cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
    }

    public Task<QlhvOperationHistoryDto?> GetActiveAsync(
        string sourceType,
        CancellationToken cancellationToken = default)
        => GetSingleAsync(ActiveSql, new { SourceType = sourceType }, cancellationToken);

    public Task<QlhvOperationHistoryDto?> GetByOperationIdAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
        => GetSingleAsync(
            ByOperationIdSql,
            new { OperationId = operationId },
            cancellationToken);

    public Task<QlhvOperationHistoryDto?> GetLatestCompletedAsync(
        string sourceType,
        string operationType,
        CancellationToken cancellationToken = default)
        => GetSingleAsync(
            LatestCompletedSql,
            new { SourceType = sourceType, OperationType = operationType },
            cancellationToken);

    private async Task<QlhvOperationHistoryDto?> GetSingleAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            return await connection.QuerySingleOrDefaultAsync<QlhvOperationHistoryDto>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
    }

    private async Task<string> ResolveTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new QlhvOperationsStoreUnavailableException(
                "QLHV_APP chua co connection dung duoc cho lich su van hanh.");
        }

        return target.ConnectionString;
    }

    private static bool IsMissingStore(SqlException exception)
        => exception.Number is 207 or 208;

    private static QlhvOperationsStoreUnavailableException StoreUnavailable(SqlException inner)
        => new(
            "Lich su van hanh chua san sang; can chay cac patch QLHV operation history va Auto Sync.",
            inner);

    private static string? Truncate(string? value, int length)
        => string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length];

    private const string ActiveExistsSql = @"
SELECT COUNT(1)
FROM dbo.App_QlhvSyncOperationHistory WITH (UPDLOCK, HOLDLOCK)
WHERE SourceType = @SourceType
  AND Status IN (N'QUEUED', N'RUNNING');";

    private const string InsertSql = @"
INSERT INTO dbo.App_QlhvSyncOperationHistory
(
    OperationId, SourceType, OperationType, Status, Actor,
    LiveDatabaseName, BackupDatabaseName, MaCSDT, SourceProfileCode,
    CreatedAtUtc, StartedAtUtc, CompletedAtUtc, UpdatedAtUtc
)
VALUES
(
    @OperationId, @SourceType, @OperationType, @Status, @Actor,
    @LiveDatabaseName, @BackupDatabaseName, @MaCSDT, @SourceProfileCode,
    @CreatedAtUtc, @StartedAtUtc, NULL, @CreatedAtUtc
);";

    private const string MarkRunningSql = @"
UPDATE dbo.App_QlhvSyncOperationHistory
SET Status = N'RUNNING',
    StartedAtUtc = @StartedAtUtc,
    UpdatedAtUtc = @StartedAtUtc
WHERE OperationId = @OperationId
  AND Status = N'QUEUED';";

    private const string CompleteSql = @"
UPDATE dbo.App_QlhvSyncOperationHistory
SET Status = @Status,
    CompletedAtUtc = @CompletedAtUtc,
    UpdatedAtUtc = @CompletedAtUtc,
    LiveRows = COALESCE(@LiveRows, LiveRows),
    BackupRows = COALESCE(@BackupRows, BackupRows),
    TargetActiveRows = COALESCE(@TargetActiveRows, TargetActiveRows),
    SourceRows = @SourceRows,
    InsertedRows = @InsertedRows,
    UpdatedRows = @UpdatedRows,
    ReactivatedRows = @ReactivatedRows,
    SoftDeletedRows = @SoftDeletedRows,
    SkippedRows = @SkippedRows,
    SnapshotToken = @SnapshotToken,
    ErrorMessage = @ErrorMessage,
    DetailJson = @DetailJson
WHERE OperationId = @OperationId
  AND Status IN (N'QUEUED', N'RUNNING');";

    private const string Projection = @"
OperationId,
SourceType,
OperationType,
Status,
Actor,
COALESCE(StartedAtUtc, CreatedAtUtc) AS StartedAtUtc,
CompletedAtUtc,
CAST(COALESCE(SourceRows, 0) AS int) AS SourceRows,
CAST(InsertedRows AS int) AS InsertedRows,
CAST(UpdatedRows AS int) AS UpdatedRows,
CAST(ReactivatedRows AS int) AS ReactivatedRows,
CAST(SoftDeletedRows AS int) AS SoftDeletedRows,
CAST(SkippedRows AS int) AS SkippedRows,
SnapshotToken,
ErrorMessage,
DetailJson";

    private const string ByOperationIdSql = "SELECT TOP (1) " + Projection + @"
FROM dbo.App_QlhvSyncOperationHistory
WHERE OperationId = @OperationId;";

    private const string SearchSql = "SELECT TOP (@Take) " + Projection + @"
FROM dbo.App_QlhvSyncOperationHistory
WHERE SourceType = @SourceType
ORDER BY CreatedAtUtc DESC, Id DESC;";

    private const string ActiveSql = "SELECT TOP (1) " + Projection + @"
FROM dbo.App_QlhvSyncOperationHistory
WHERE SourceType = @SourceType
  AND Status IN (N'QUEUED', N'RUNNING')
ORDER BY CreatedAtUtc DESC, Id DESC;";

    private const string LatestCompletedSql = "SELECT TOP (1) " + Projection + @"
FROM dbo.App_QlhvSyncOperationHistory
WHERE SourceType = @SourceType
  AND OperationType = @OperationType
  AND Status IN (N'SUCCEEDED', N'FAILED')
ORDER BY CompletedAtUtc DESC, Id DESC;";
}

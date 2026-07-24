using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvAutoSyncRunRepository : IQlhvAutoSyncRunRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionSettingsProvider _connections;

    public QlhvAutoSyncRunRepository(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<bool> TryCreateAsync(
        QlhvAutoSyncRunCreate entry,
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
                new
                {
                    entry.TriggerType,
                    entry.DedupeNotBeforeUtc,
                },
                transaction: transaction,
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
                    entry.RunId,
                    entry.TriggerType,
                    entry.Actor,
                    SourceOrderJson = JsonSerializer.Serialize(entry.SourceOrder, JsonOptions),
                    entry.CreatedAtUtc,
                },
                transaction,
                cancellationToken: cancellationToken));
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
        catch (SqlException ex)
        {
            throw StoreUnavailable(ex, "Tam thoi khong ghi duoc Auto Sync history.");
        }
    }

    public Task<QlhvAutoSyncRunRecord?> GetByIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
        => GetSingleAsync(ByIdSql, new { RunId = runId }, cancellationToken);

    public Task<QlhvAutoSyncRunRecord?> GetActiveAsync(
        CancellationToken cancellationToken = default)
        => GetSingleAsync(ActiveSql, null, cancellationToken);

    public Task<QlhvAutoSyncRunRecord?> GetLatestAsync(
        CancellationToken cancellationToken = default)
        => GetSingleAsync(LatestSql, null, cancellationToken);

    public Task<bool> MarkRunningAsync(
        Guid runId,
        DateTime startedAtUtc,
        CancellationToken cancellationToken = default)
        => ExecuteTransitionAsync(
            MarkRunningSql,
            new { RunId = runId, StartedAtUtc = startedAtUtc },
            cancellationToken);

    public async Task SetCurrentSourceAsync(
        Guid runId,
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        var source = QlhvOperationSourceCatalog.GetRequired(sourceType);
        await ExecuteRequiredUpdateAsync(
            SetCurrentSourceSql,
            new { RunId = runId, source.SourceType, UpdatedAtUtc = DateTime.UtcNow },
            cancellationToken);
    }

    public async Task SetCurrentStageAsync(
        Guid runId,
        string stage,
        CancellationToken cancellationToken = default)
    {
        var normalized = stage?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 32)
        {
            throw new ArgumentException("Auto Sync stage khong hop le.", nameof(stage));
        }

        await ExecuteRequiredUpdateAsync(
            SetCurrentStageSql,
            new { RunId = runId, Stage = normalized, UpdatedAtUtc = DateTime.UtcNow },
            cancellationToken);
    }

    public async Task SetSourceResultAsync(
        Guid runId,
        QlhvAutoSyncSourceResultDto result,
        CancellationToken cancellationToken = default)
    {
        var source = QlhvOperationSourceCatalog.GetRequired(result.SourceType);
        var sql = string.Equals(source.SourceType, "OTO", StringComparison.Ordinal)
            ? SetOtoResultSql
            : SetMotoResultSql;
        await ExecuteRequiredUpdateAsync(
            sql,
            new
            {
                RunId = runId,
                ResultJson = JsonSerializer.Serialize(result, JsonOptions),
                UpdatedAtUtc = DateTime.UtcNow,
            },
            cancellationToken);
    }

    public async Task CompleteAsync(
        Guid runId,
        QlhvAutoSyncOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        if (outcome.Status is not (
                QlhvAutoSyncConstants.Succeeded or
                QlhvAutoSyncConstants.PartialSuccess or
                QlhvAutoSyncConstants.PartialFailed or
                QlhvAutoSyncConstants.Failed))
        {
            throw new ArgumentException(
                "Auto Sync chi duoc hoan tat bang trang thai terminal.",
                nameof(outcome));
        }

        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                CompleteSql,
                new
                {
                    RunId = runId,
                    outcome.Status,
                    ErrorMessage = Truncate(outcome.ErrorMessage, 2000),
                    outcome.CompletedAtUtc,
                },
                cancellationToken: cancellationToken));
            if (changed != 1)
            {
                throw new InvalidOperationException("Auto Sync run khong ton tai hoac da ket thuc.");
            }
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
        catch (SqlException ex)
        {
            throw StoreUnavailable(ex, "Tam thoi khong hoan tat duoc Auto Sync history.");
        }
    }

    public Task<bool> RequeueInterruptedAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
        => ExecuteTransitionAsync(
            RequeueInterruptedSql,
            new { RunId = runId, UpdatedAtUtc = DateTime.UtcNow },
            cancellationToken);

    private async Task<QlhvAutoSyncRunRecord?> GetSingleAsync(
        string sql,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            var row = await connection.QuerySingleOrDefaultAsync<RunRow>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
            return row is null ? null : Map(row);
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
        catch (SqlException ex)
        {
            throw StoreUnavailable(ex, "Tam thoi khong doc duoc Auto Sync history.");
        }
    }

    private async Task<bool> ExecuteTransitionAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        try
        {
            await using var connection = new SqlConnection(connectionString);
            return await connection.ExecuteAsync(new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken)) == 1;
        }
        catch (SqlException ex) when (IsMissingStore(ex))
        {
            throw StoreUnavailable(ex);
        }
        catch (SqlException ex)
        {
            throw StoreUnavailable(ex, "Tam thoi khong cap nhat duoc Auto Sync history.");
        }
    }

    private async Task ExecuteRequiredUpdateAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        if (!await ExecuteTransitionAsync(sql, parameters, cancellationToken))
        {
            throw new InvalidOperationException("Auto Sync run khong con o trang thai RUNNING.");
        }
    }

    private async Task<string> ResolveTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new QlhvAutoSyncStoreUnavailableException(
                "QLHV_APP chua co connection dung duoc cho Auto Sync history.");
        }

        return target.ConnectionString;
    }

    private static QlhvAutoSyncRunRecord Map(RunRow row)
        => new()
        {
            RunId = row.RunId,
            TriggerType = row.TriggerType,
            Actor = row.Actor,
            Status = row.Status,
            SourceOrder = Deserialize<string[]>(row.SourceOrderJson) ?? Array.Empty<string>(),
            CurrentSourceType = row.CurrentSourceType,
            CurrentStage = row.CurrentStage,
            CreatedAtUtc = row.CreatedAtUtc,
            StartedAtUtc = row.StartedAtUtc,
            CompletedAtUtc = row.CompletedAtUtc,
            Oto = Deserialize<QlhvAutoSyncSourceResultDto>(row.OtoResultJson),
            Moto = Deserialize<QlhvAutoSyncSourceResultDto>(row.MotoResultJson),
            ErrorMessage = row.ErrorMessage,
        };

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool IsMissingStore(SqlException exception)
        => exception.Number is 207 or 208;

    private static QlhvAutoSyncStoreUnavailableException StoreUnavailable(SqlException inner)
        => new(
            "Auto Sync history chua san sang; can chay patch tao dbo.App_QlhvAutoSyncRun.",
            inner);

    private static QlhvAutoSyncStoreUnavailableException StoreUnavailable(
        SqlException inner,
        string message)
        => new(message, inner);

    private static string? Truncate(string? value, int length)
        => string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length];

    private const string Projection = @"
RunId,
TriggerType,
Actor,
Status,
SourceOrderJson,
CurrentSourceType,
CurrentStage,
CreatedAtUtc,
StartedAtUtc,
CompletedAtUtc,
OtoResultJson,
MotoResultJson,
ErrorMessage";

    private const string ActiveExistsSql = @"
SELECT COUNT(1)
FROM dbo.App_QlhvAutoSyncRun WITH (UPDLOCK, HOLDLOCK)
WHERE ActiveSlot = 1
   OR
   (
       @DedupeNotBeforeUtc IS NOT NULL
       AND TriggerType = @TriggerType
       AND CreatedAtUtc >= @DedupeNotBeforeUtc
   );";

    private const string InsertSql = @"
INSERT INTO dbo.App_QlhvAutoSyncRun
(
    RunId, TriggerType, Actor, Status, SourceOrderJson,
    CurrentSourceType, CurrentStage, ActiveSlot,
    CreatedAtUtc, StartedAtUtc, CompletedAtUtc, UpdatedAtUtc
)
VALUES
(
    @RunId, @TriggerType, @Actor, N'QUEUED', @SourceOrderJson,
    NULL, N'CONNECTING', 1, @CreatedAtUtc, NULL, NULL, @CreatedAtUtc
);";

    private const string ByIdSql = "SELECT TOP (1) " + Projection + @"
FROM dbo.App_QlhvAutoSyncRun
WHERE RunId = @RunId;";

    private const string ActiveSql = "SELECT TOP (1) " + Projection + @"
FROM dbo.App_QlhvAutoSyncRun
WHERE ActiveSlot = 1
ORDER BY CreatedAtUtc, Id;";

    private const string LatestSql = "SELECT TOP (1) " + Projection + @"
FROM dbo.App_QlhvAutoSyncRun
ORDER BY CreatedAtUtc DESC, Id DESC;";

    private const string MarkRunningSql = @"
UPDATE dbo.App_QlhvAutoSyncRun
SET Status = N'RUNNING',
    ActiveSlot = 1,
    CurrentStage = N'CONNECTING',
    StartedAtUtc = @StartedAtUtc,
    UpdatedAtUtc = @StartedAtUtc
WHERE RunId = @RunId
  AND Status = N'QUEUED'
  AND ActiveSlot = 1;";

    private const string SetCurrentSourceSql = @"
UPDATE dbo.App_QlhvAutoSyncRun
SET CurrentSourceType = @SourceType,
    UpdatedAtUtc = @UpdatedAtUtc
WHERE RunId = @RunId
  AND Status = N'RUNNING'
  AND ActiveSlot = 1;";

    private const string SetCurrentStageSql = @"
UPDATE dbo.App_QlhvAutoSyncRun
SET CurrentStage = @Stage,
    UpdatedAtUtc = @UpdatedAtUtc
WHERE RunId = @RunId
  AND Status = N'RUNNING'
  AND ActiveSlot = 1;";

    private const string SetOtoResultSql = @"
UPDATE dbo.App_QlhvAutoSyncRun
SET OtoResultJson = @ResultJson,
    UpdatedAtUtc = @UpdatedAtUtc
WHERE RunId = @RunId
  AND Status = N'RUNNING'
  AND ActiveSlot = 1;";

    private const string SetMotoResultSql = @"
UPDATE dbo.App_QlhvAutoSyncRun
SET MotoResultJson = @ResultJson,
    UpdatedAtUtc = @UpdatedAtUtc
WHERE RunId = @RunId
  AND Status = N'RUNNING'
  AND ActiveSlot = 1;";

    private const string CompleteSql = @"
UPDATE dbo.App_QlhvAutoSyncRun
SET Status = @Status,
    ActiveSlot = NULL,
    CurrentSourceType = NULL,
    CurrentStage = CASE
        WHEN @Status IN (N'SUCCEEDED', N'PARTIAL_SUCCESS') THEN N'COMPLETED'
        ELSE N'FAILED'
    END,
    CompletedAtUtc = @CompletedAtUtc,
    UpdatedAtUtc = @CompletedAtUtc,
    ErrorMessage = @ErrorMessage
WHERE RunId = @RunId
  AND Status IN (N'QUEUED', N'RUNNING')
  AND ActiveSlot = 1;";

    private const string RequeueInterruptedSql = @"
UPDATE dbo.App_QlhvAutoSyncRun
SET Status = N'QUEUED',
    ActiveSlot = 1,
    CurrentSourceType = NULL,
    CurrentStage = N'CONNECTING',
    StartedAtUtc = NULL,
    UpdatedAtUtc = @UpdatedAtUtc,
    ErrorMessage = N'Auto Sync bi gian doan va duoc thu lai sau khi host khoi dong.'
WHERE RunId = @RunId
  AND Status = N'RUNNING'
  AND ActiveSlot = 1;";

    private sealed class RunRow
    {
        public Guid RunId { get; init; }
        public string TriggerType { get; init; } = string.Empty;
        public string Actor { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string SourceOrderJson { get; init; } = "[]";
        public string? CurrentSourceType { get; init; }
        public string? CurrentStage { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public string? OtoResultJson { get; init; }
        public string? MotoResultJson { get; init; }
        public string? ErrorMessage { get; init; }
    }
}

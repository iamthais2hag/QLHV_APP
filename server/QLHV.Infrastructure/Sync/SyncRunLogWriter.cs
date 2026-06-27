using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Writes one sanitized sync run summary row to dbo.App_DongBoLog.
/// Never log raw CCCD/GPLX values, passwords, tokens, or full connection strings.
/// </summary>
public sealed class SyncRunLogWriter : ISyncRunLogWriter
{
    internal const string InsertSql = @"
INSERT INTO dbo.App_DongBoLog
    (JobName, EntityType, SourceSystem, StartedAt, EndedAt, DurationMs, Status,
     TotalRead, TotalInserted, TotalUpdated, TotalSkipped, TotalError, RetryCount,
     ErrorMessage, DetailJson, CreatedBy)
OUTPUT INSERTED.DongBoLogId
VALUES
    (@JobName, @EntityType, @SourceSystem, @StartedAt, @EndedAt, @DurationMs, @Status,
     @TotalRead, @TotalInserted, @TotalUpdated, @TotalSkipped, @TotalError, @RetryCount,
     @ErrorMessage, @DetailJson, @CreatedBy);
";

    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;
    private readonly SyncExecutionOptions _execution;

    public SyncRunLogWriter(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options,
        IOptions<SyncExecutionOptions> execution)
    {
        _connections = connections;
        _options = options.Value;
        _execution = execution.Value;
    }

    public async Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (_options.DryRun)
        {
            throw new InvalidOperationException(
                "Ghi nhat ky dong bo bi chan: Sync:DryRun = true.");
        }

        if (!_execution.EnableTargetWrites)
        {
            throw new InvalidOperationException(
                "Ghi nhat ky dong bo bi chan: SyncExecution.EnableTargetWrites = false.");
        }

        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc de ghi nhat ky dong bo.");
        }

        await using var connection = new SqlConnection(target.ConnectionString);
        var command = new CommandDefinition(
            InsertSql,
            new
            {
                entry.JobName,
                entry.EntityType,
                entry.SourceSystem,
                entry.StartedAt,
                entry.EndedAt,
                entry.DurationMs,
                entry.Status,
                entry.TotalRead,
                entry.TotalInserted,
                entry.TotalUpdated,
                entry.TotalSkipped,
                entry.TotalError,
                entry.RetryCount,
                ErrorMessage = Sanitize(entry.ErrorMessage),
                DetailJson = Sanitize(entry.DetailJson),
                entry.CreatedBy,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<long>(command);
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value
            .Replace("Password=", "Password=<masked>;", StringComparison.OrdinalIgnoreCase)
            .Replace("Pwd=", "Pwd=<masked>;", StringComparison.OrdinalIgnoreCase);
    }
}

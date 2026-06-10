using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>Writes one sanitized sync run summary row into dbo.App_DongBoLog.</summary>
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
    private readonly SyncOptions _options;

    public SyncRunLogWriter(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableTargetWrites)
        {
            throw new InvalidOperationException("EnableTargetWrites=false. Sync run logging is locked with target writes.");
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
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

    private async Task<string> ResolveUsableTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc (thieu hoac dang la placeholder).");
        }

        return target.ConnectionString;
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // Keep this conservative: log summaries should contain counts/codes only, never raw credentials.
        return value
            .Replace("Password=", "Password=<masked>;", StringComparison.OrdinalIgnoreCase)
            .Replace("Pwd=", "Pwd=<masked>;", StringComparison.OrdinalIgnoreCase);
    }
}

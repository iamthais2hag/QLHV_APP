using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Ghi nhật ký một lần chạy đồng bộ vào dbo.App_DongBoLog.
///
/// Chỉ ghi các trường tóm tắt an toàn (số đếm, trạng thái, thời gian, thông điệp đã làm sạch).
/// KHÔNG ghi CCCD/GPLX thô, KHÔNG ghi mật khẩu/chuỗi kết nối/token.
/// Chỉ được gọi ở luồng execute (không gọi trong dry-run).
/// </summary>
public sealed class SyncRunLogWriter : ISyncRunLogWriter
{
    /// <summary>Câu lệnh INSERT tham số hóa cho App_DongBoLog.</summary>
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

    public SyncRunLogWriter(IConnectionSettingsProvider connections, IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default)
    {
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
                entry.ErrorMessage,
                entry.DetailJson,
                entry.CreatedBy,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<long>(command);
    }
}

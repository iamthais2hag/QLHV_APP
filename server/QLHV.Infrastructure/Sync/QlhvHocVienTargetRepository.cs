using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Ghi/đối chiếu học viên tại đích QLHV_APP.dbo.App_HocVien.
///
/// PHASE B3A:
///   - Thao tác CHỈ ĐỌC (CountAsync, GetExistingKeysAsync) đã hiện thực bằng Dapper để dựng kế hoạch.
///   - Thao tác GHI (UpsertBatchAsync) CHƯA hiện thực: ném NotSupportedException để chặn ghi ngoài ý muốn.
///     Cấu trúc SqlBulkCopy + MERGE chuẩn bị tại HocVienTargetMergeSql (không thực thi).
///   - Kết nối lấy qua IConnectionSettingsProvider; nếu chưa cấu hình/placeholder thì ném lỗi an toàn
///     (không lộ chuỗi kết nối).
/// </summary>
public sealed class QlhvHocVienTargetRepository : IQlhvHocVienTargetRepository
{
    private const string WriteNotImplementedMessage =
        "Ghi du lieu vao QLHV_APP se duoc hien thuc o Phase B3B. Phase B3A khong ghi SQL Server.";

    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public QlhvHocVienTargetRepository(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.App_HocVien WHERE IsDeleted = 0;",
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int>(command);
    }

    public async Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default)
    {
        if (maDks is null || maDks.Count == 0)
        {
            return Array.Empty<string>();
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            "SELECT MaDK FROM dbo.App_HocVien WHERE MaDK IN @MaDks;",
            new { MaDks = maDks },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        var keys = await connection.QueryAsync<string>(command);
        return keys.ToList();
    }

    public Task<int> UpsertBatchAsync(
        IReadOnlyList<V2HocVienSourceRow> rows,
        bool dryRun,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(WriteNotImplementedMessage);

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
}

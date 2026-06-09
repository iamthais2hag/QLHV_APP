using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Đọc học viên từ nguồn CSDT_V2 bằng Dapper.
///
/// PHASE B2: CHỈ ĐỌC (SELECT/COUNT). Tuyệt đối không INSERT/UPDATE/DELETE/MERGE,
/// không SqlBulkCopy, không ghi vào QLHV_APP. Kết nối lấy qua IConnectionSettingsProvider;
/// nếu CSDT_V2 chưa cấu hình hoặc còn placeholder thì ném InvalidOperationException
/// (người gọi phải kiểm tra cấu hình trước qua dry-run). Không bao giờ lộ chuỗi kết nối.
/// </summary>
public sealed class V2HocVienSourceRepository : IV2HocVienSourceRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public V2HocVienSourceRepository(
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<int> CountAsync(
        HocVienSourceFilter filter,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableV2ConnectionAsync(cancellationToken);
        var (sql, parameters) = HocVienV2SqlBuilder.BuildCount(filter.Normalized());

        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            sql,
            parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int>(command);
    }

    public async Task<IReadOnlyList<V2HocVienSourceRow>> ReadPageAsync(
        HocVienSourceFilter filter,
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            offset = 0;
        }

        if (pageSize < 1)
        {
            pageSize = 1;
        }

        var connectionString = await ResolveUsableV2ConnectionAsync(cancellationToken);
        var (sql, parameters) = HocVienV2SqlBuilder.BuildPage(filter.Normalized(), offset, pageSize);

        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            sql,
            parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<V2HocVienSourceRow>(command);
        return rows.ToList();
    }

    /// <summary>
    /// Phân giải kết nối CSDT_V2 và bảo đảm có thể dùng được. Ném lỗi an toàn (không lộ bí mật)
    /// nếu chưa cấu hình hoặc còn placeholder.
    /// </summary>
    private async Task<string> ResolveUsableV2ConnectionAsync(CancellationToken cancellationToken)
    {
        var v2 = await _connections.GetSourceConnectionAsync(SourceSystem.V2, cancellationToken);
        if (!v2.IsUsable || string.IsNullOrWhiteSpace(v2.ConnectionString))
        {
            throw new InvalidOperationException(
                "CSDT_V2 chua co cau hinh ket noi dung duoc (thieu hoac dang la placeholder).");
        }

        return v2.ConnectionString;
    }
}

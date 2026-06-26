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

        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            var command = new CommandDefinition(
                sql,
                parameters,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct);

            return await connection.ExecuteScalarAsync<int>(command);
        }, cancellationToken);
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

        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            var command = new CommandDefinition(
                sql,
                parameters,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct);

            var rows = await connection.QueryAsync<V2HocVienSourceRow>(command);
            return (IReadOnlyList<V2HocVienSourceRow>)rows.ToList();
        }, cancellationToken);
    }

    public async Task<V2HocVienSourceDiagnosticsDto> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableV2ConnectionAsync(cancellationToken);
        var sql = HocVienV2SqlBuilder.BuildDiagnostics();

        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            var command = new CommandDefinition(
                sql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct);

            using var grid = await connection.QueryMultipleAsync(command);
            var summary = await grid.ReadSingleAsync<SourceDiagnosticsSummaryRow>();
            var duplicate = await grid.ReadSingleAsync<DuplicateMaDkDiagnosticsRow>();
            var gioiTinh = (await grid.ReadAsync<SourceValueDistributionDto>()).ToList();
            var soCmt = (await grid.ReadAsync<SourceValueDistributionDto>()).ToList();

            return new V2HocVienSourceDiagnosticsDto
            {
                CheckedAtUtc = DateTime.UtcNow,
                SourceRows = summary.SourceRows,
                DuplicateMaDkCount = duplicate.DuplicateMaDkCount,
                DuplicateMaDkRowCount = duplicate.DuplicateMaDkRowCount,
                MissingMaDkCount = summary.MissingMaDkCount,
                MissingHoTenCount = summary.MissingHoTenCount,
                GioiTinhDistribution = gioiTinh,
                SoCmtLength = new SoCmtLengthDiagnosticsDto
                {
                    NineDigits = GetBucket(soCmt, "9"),
                    TwelveDigits = GetBucket(soCmt, "12"),
                    Other = GetBucket(soCmt, "other"),
                    NullOrEmpty = GetBucket(soCmt, "null-empty"),
                },
                MissingNgaySinhCount = summary.MissingNgaySinhCount,
                NgaySinhParseIssueCount = summary.NgaySinhParseIssueCount,
                MissingHangDaoTaoCount = summary.MissingHangDaoTaoCount,
                HangDaoTaoUnmatchedDmHangDtCount = summary.HangDaoTaoUnmatchedDmHangDtCount,
                MissingNoiTTCodesCount = summary.MissingNoiTTCodesCount,
                NoiTTUnmatchedDmDvhcCount = summary.NoiTTUnmatchedDmDvhcCount,
                MissingMaKhoaHocCount = summary.MissingMaKhoaHocCount,
                MaKhoaHocUnmatchedKhoaHocCount = summary.MaKhoaHocUnmatchedKhoaHocCount,
            };
        }, cancellationToken);
    }

    private static int GetBucket(IEnumerable<SourceValueDistributionDto> rows, string value)
        => rows.FirstOrDefault(r => string.Equals(r.Value, value, StringComparison.OrdinalIgnoreCase))?.Total ?? 0;

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

    private sealed class SourceDiagnosticsSummaryRow
    {
        public int SourceRows { get; init; }
        public int MissingMaDkCount { get; init; }
        public int MissingHoTenCount { get; init; }
        public int MissingNgaySinhCount { get; init; }
        public int NgaySinhParseIssueCount { get; init; }
        public int MissingHangDaoTaoCount { get; init; }
        public int HangDaoTaoUnmatchedDmHangDtCount { get; init; }
        public int MissingNoiTTCodesCount { get; init; }
        public int NoiTTUnmatchedDmDvhcCount { get; init; }
        public int MissingMaKhoaHocCount { get; init; }
        public int MaKhoaHocUnmatchedKhoaHocCount { get; init; }
    }

    private sealed class DuplicateMaDkDiagnosticsRow
    {
        public int DuplicateMaDkCount { get; init; }
        public int DuplicateMaDkRowCount { get; init; }
    }
}

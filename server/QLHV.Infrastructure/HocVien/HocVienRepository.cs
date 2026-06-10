using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;
using QLHV.Application.Sync.Connections;
using QLHV.Shared.Paging;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.HocVien;

/// <summary>
/// Cài đặt truy vấn học viên chỉ đọc từ QLHV_APP.dbo.App_HocVien.
/// </summary>
public sealed class HocVienRepository : IHocVienRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;

    public HocVienRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var countQuery = HocVienSearchSqlBuilder.BuildCount(normalized);
        var totalItems = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            countQuery.Sql,
            countQuery.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        if (totalItems == 0)
        {
            return PagedResult<HocVienListItemDto>.Empty(normalized.Page, normalized.PageSize);
        }

        var pageQuery = HocVienSearchSqlBuilder.BuildPage(normalized);
        var rows = await connection.QueryAsync<HocVienReadRow>(new CommandDefinition(
            pageQuery.Sql,
            pageQuery.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return new PagedResult<HocVienListItemDto>
        {
            Items = rows.Select(ToDto).ToList(),
            Page = normalized.Page,
            PageSize = normalized.PageSize,
            TotalItems = totalItems,
        };
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

    private static HocVienListItemDto ToDto(HocVienReadRow row) => new()
    {
        MaDK = row.MaDK,
        HoTen = row.HoTen ?? string.Empty,
        NgaySinh = row.NgaySinh.HasValue ? DateOnly.FromDateTime(row.NgaySinh.Value) : null,
        GioiTinh = row.GioiTinh,
        SoCCCD = row.SoCCCD,
        DiaChiThuongTru = row.DiaChiThuongTru,
        SoGPLXDaCo = row.SoGPLXDaCo,
        HangGPLXDaCo = row.HangGPLXDaCo,
        NguoiNhanHoSo = row.NguoiNhanHoSo,
        TenKhoa = row.TenKhoa,
        MaKhoa = row.MaKhoa,
        SourceOfTruth = row.SourceOfTruth,
        LastSyncStatus = row.LastSyncStatus,
    };

    private sealed class HocVienReadRow
    {
        public string MaDK { get; init; } = string.Empty;
        public string? HoTen { get; init; }
        public DateTime? NgaySinh { get; init; }
        public string? GioiTinh { get; init; }
        public string? SoCCCD { get; init; }
        public string? DiaChiThuongTru { get; init; }
        public string? SoGPLXDaCo { get; init; }
        public string? HangGPLXDaCo { get; init; }
        public string? NguoiNhanHoSo { get; init; }
        public string? TenKhoa { get; init; }
        public string? MaKhoa { get; init; }
        public string? SourceOfTruth { get; init; }
        public string? LastSyncStatus { get; init; }
    }
}

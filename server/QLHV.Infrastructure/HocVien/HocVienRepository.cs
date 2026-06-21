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

    public async Task<HocVienListItemDto?> GetByIdAsync(
        int hocVienId,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var query = HocVienSearchSqlBuilder.BuildById(hocVienId);
        var row = await connection.QuerySingleOrDefaultAsync<HocVienReadRow>(new CommandDefinition(
            query.Sql,
            query.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return row is null ? null : ToDto(row);
    }

    public async Task<IReadOnlyList<HocVienListItemDto>> GetByIdsAsync(
        IReadOnlyList<int> hocVienIds,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        if (hocVienIds.Count == 0)
        {
            return [];
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var query = HocVienSearchSqlBuilder.BuildByIds(hocVienIds, maxRows);
        var rows = await connection.QueryAsync<HocVienReadRow>(new CommandDefinition(
            query.Sql,
            query.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<HocVienListItemDto>> GetByCourseAsync(
        string maKhoa,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var query = HocVienSearchSqlBuilder.BuildByCourse(maKhoa, maxRows);
        var rows = await connection.QueryAsync<HocVienReadRow>(new CommandDefinition(
            query.Sql,
            query.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<HocVienListItemDto>> ExportRowsAsync(
        HocVienSearchRequest request,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var exportQuery = HocVienSearchSqlBuilder.BuildExport(normalized, maxRows);
        var rows = await connection.QueryAsync<HocVienReadRow>(new CommandDefinition(
            exportQuery.Sql,
            exportQuery.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<HocVienKhoaLookupDto>> SearchKhoaLookupsAsync(
        string? keyword,
        string? maHangDT,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var query = HocVienSearchSqlBuilder.BuildKhoaLookup(keyword, limit, maHangDT);
        var rows = await connection.QueryAsync<HocVienKhoaLookupDto>(new CommandDefinition(
            query.Sql,
            query.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<IReadOnlyList<HocVienHangHocLookupDto>> SearchHangHocLookupsAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var query = HocVienSearchSqlBuilder.BuildHangHocLookup(keyword, limit);
        var rows = await connection.QueryAsync<HocVienHangHocLookupDto>(new CommandDefinition(
            query.Sql,
            query.Parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.ToList();
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
        HocVienId = row.HocVienId,
        MaDangKy = row.MaDangKy,
        HoVaTen = row.HoVaTen ?? string.Empty,
        NgaySinh = row.NgaySinh.HasValue ? DateOnly.FromDateTime(row.NgaySinh.Value) : null,
        GioiTinh = HocVienGender.ToDisplayValue(row.GioiTinh),
        SoCccd = row.SoCccd,
        DiaChiThuongTru = row.DiaChiThuongTru,
        AnhRelativePath = ToSafeRelativePath(row.AnhRelativePath),
        SoGplxDaCo = row.SoGplxDaCo,
        MaHangDT = row.MaHangDT,
        HangGplxHoc = row.HangGplxHoc,
        HangGplxDaCo = row.HangGplxDaCo,
        NguoiNhanHoSo = row.NguoiNhanHoSo,
        TenKhoa = row.TenKhoa,
        MaKhoa = row.MaKhoa,
        LastSyncStatus = row.LastSyncStatus,
    };

    private static string? ToSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.StartsWith(@"\", StringComparison.Ordinal) ||
            trimmed.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

    private sealed class HocVienReadRow
    {
        public int HocVienId { get; init; }
        public string MaDangKy { get; init; } = string.Empty;
        public string? HoVaTen { get; init; }
        public DateTime? NgaySinh { get; init; }
        public string? GioiTinh { get; init; }
        public string? SoCccd { get; init; }
        public string? DiaChiThuongTru { get; init; }
        public string? AnhRelativePath { get; init; }
        public string? SoGplxDaCo { get; init; }
        public string? MaHangDT { get; init; }
        public string? HangGplxHoc { get; init; }
        public string? HangGplxDaCo { get; init; }
        public string? NguoiNhanHoSo { get; init; }
        public string? TenKhoa { get; init; }
        public string? MaKhoa { get; init; }
        public string? LastSyncStatus { get; init; }
    }
}

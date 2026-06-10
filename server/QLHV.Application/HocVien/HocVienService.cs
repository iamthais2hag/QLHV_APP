using QLHV.Application.HocVien.Dtos;
using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien;

/// <summary>
/// Cài đặt nghiệp vụ tìm kiếm học viên. Chuẩn hóa tham số rồi ủy quyền cho repository.
/// </summary>
public sealed class HocVienService : IHocVienService
{
    private const int DefaultLookupLimit = 20;
    private const int MaxLookupLimit = 50;
    private const int MaxExportRows = 10_000;

    private readonly IHocVienRepository _repository;

    public HocVienService(IHocVienRepository repository)
    {
        _repository = repository;
    }

    public Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        return _repository.SearchAsync(normalized, cancellationToken);
    }

    public async Task<HocVienExportFileDto> ExportExcelAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var rows = await _repository.ExportRowsAsync(normalized, MaxExportRows + 1, cancellationToken);
        if (rows.Count > MaxExportRows)
        {
            throw new InvalidOperationException(
                $"Ket qua xuat Excel vuot qua gioi han an toan {MaxExportRows:N0} dong.");
        }

        var content = HocVienExcelExporter.CreateWorkbook(rows, rows.Count);
        return new HocVienExportFileDto
        {
            FileName = $"HocVien_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            Content = content,
        };
    }

    public Task<IReadOnlyList<HocVienKhoaLookupDto>> SearchKhoaLookupsAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _repository.SearchKhoaLookupsAsync(
            NormalizeLookupKeyword(keyword),
            NormalizeLookupLimit(limit),
            cancellationToken);
    }

    public Task<IReadOnlyList<HocVienHangHocLookupDto>> SearchHangHocLookupsAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _repository.SearchHangHocLookupsAsync(
            NormalizeLookupKeyword(keyword),
            NormalizeLookupLimit(limit),
            cancellationToken);
    }

    private static string? NormalizeLookupKeyword(string? keyword)
        => string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

    private static int NormalizeLookupLimit(int limit)
        => Math.Clamp(limit <= 0 ? DefaultLookupLimit : limit, 1, MaxLookupLimit);
}

using QLHV.Application.HocVien.Dtos;
using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien;

/// <summary>
/// Cài đặt nghiệp vụ tìm kiếm học viên. Chuẩn hóa tham số rồi ủy quyền cho repository.
/// </summary>
public sealed class HocVienService : IHocVienService
{
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

    public Task<IReadOnlyList<HocVienKhoaLookupDto>> LookupKhoaAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default)
        => _repository.LookupKhoaAsync(NormalizeKeyword(keyword), NormalizeLimit(limit), cancellationToken);

    public Task<IReadOnlyList<HocVienHangHocLookupDto>> LookupHangHocAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default)
        => _repository.LookupHangHocAsync(NormalizeKeyword(keyword), NormalizeLimit(limit), cancellationToken);

    private static string? NormalizeKeyword(string? keyword)
        => string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

    private static int NormalizeLimit(int limit) => Math.Clamp(limit <= 0 ? 20 : limit, 1, 50);
}

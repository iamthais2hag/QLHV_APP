using QLHV.Application.HocVien.Dtos;
using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien;

/// <summary>
/// Nghiệp vụ tìm kiếm học viên (chỉ đọc).
/// </summary>
public interface IHocVienService
{
    /// <summary>Tìm kiếm danh sách học viên theo điều kiện đã chuẩn hóa.</summary>
    Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienKhoaLookupDto>> LookupKhoaAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienHangHocLookupDto>> LookupHangHocAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default);
}

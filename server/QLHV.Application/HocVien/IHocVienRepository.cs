using QLHV.Application.HocVien.Dtos;
using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien;

/// <summary>
/// Truy vấn dữ liệu học viên (chỉ đọc). Cài đặt cụ thể nằm ở tầng Infrastructure.
/// </summary>
public interface IHocVienRepository
{
    /// <summary>Tìm kiếm học viên theo điều kiện, trả về kết quả phân trang.</summary>
    Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<HocVienListItemDto?> GetByIdAsync(
        int hocVienId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienListItemDto>> GetByIdsAsync(
        IReadOnlyList<int> hocVienIds,
        int maxRows,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienListItemDto>> GetByCourseAsync(
        string maKhoa,
        int maxRows,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienListItemDto>> ExportRowsAsync(
        HocVienSearchRequest request,
        int maxRows,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienKhoaLookupDto>> SearchKhoaLookupsAsync(
        string? keyword,
        string? maHangDT,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HocVienHangHocLookupDto>> SearchHangHocLookupsAsync(
        string? keyword,
        int limit,
        CancellationToken cancellationToken = default);
}

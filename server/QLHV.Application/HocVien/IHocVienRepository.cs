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

    Task<IReadOnlyList<HocVienListItemDto>> ExportRowsAsync(
        HocVienSearchRequest request,
        int maxRows,
        CancellationToken cancellationToken = default);
}

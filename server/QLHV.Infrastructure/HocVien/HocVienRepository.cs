using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;
using QLHV.Shared.Paging;

namespace QLHV.Infrastructure.HocVien;

/// <summary>
/// Cài đặt truy vấn học viên.
/// Nguồn dữ liệu thật (V2, chỉ đọc) sẽ được nối ở bước sau. Hiện trả về kết quả rỗng an toàn,
/// không kết nối SQL Server.
/// </summary>
public sealed class HocVienRepository : IHocVienRepository
{
    public Task<PagedResult<HocVienListItemDto>> SearchAsync(
        HocVienSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // Chưa nối nguồn dữ liệu. Trả về danh sách rỗng theo đúng tham số phân trang.
        var result = PagedResult<HocVienListItemDto>.Empty(request.Page, request.PageSize);
        return Task.FromResult(result);
    }
}

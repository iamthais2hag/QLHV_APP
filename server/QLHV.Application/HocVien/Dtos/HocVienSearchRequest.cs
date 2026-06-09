using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien.Dtos;

/// <summary>
/// Tham số tìm kiếm danh sách học viên (chỉ đọc).
/// </summary>
public sealed class HocVienSearchRequest
{
    /// <summary>Từ khóa tìm kiếm (họ tên, mã đăng ký, số CCCD...).</summary>
    public string? Keyword { get; set; }

    /// <summary>Lọc theo mã khóa học.</summary>
    public string? MaKhoa { get; set; }

    /// <summary>Lọc theo hạng GPLX.</summary>
    public string? HangGplx { get; set; }

    /// <summary>Lọc theo giới tính.</summary>
    public string? GioiTinh { get; set; }

    /// <summary>Trang hiện tại (bắt đầu từ 1).</summary>
    public int Page { get; set; } = PagingDefaults.DefaultPage;

    /// <summary>Số phần tử mỗi trang.</summary>
    public int PageSize { get; set; } = PagingDefaults.DefaultPageSize;

    /// <summary>Trả về bản sao đã chuẩn hóa giá trị phân trang.</summary>
    public HocVienSearchRequest Normalized() => new()
    {
        Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
        MaKhoa = string.IsNullOrWhiteSpace(MaKhoa) ? null : MaKhoa.Trim(),
        HangGplx = string.IsNullOrWhiteSpace(HangGplx) ? null : HangGplx.Trim(),
        GioiTinh = string.IsNullOrWhiteSpace(GioiTinh) ? null : GioiTinh.Trim(),
        Page = PagingDefaults.NormalizePage(Page),
        PageSize = PagingDefaults.NormalizePageSize(PageSize),
    };
}

namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Bộ lọc đọc học viên từ nguồn CSDT_V2 (chỉ đọc).
/// Các giá trị được truyền vào truy vấn Dapper dưới dạng tham số (parameterized) để chống SQL injection.
/// </summary>
public sealed class HocVienSourceFilter
{
    /// <summary>Từ khóa tìm kiếm (họ tên, mã đăng ký, số CCCD).</summary>
    public string? Keyword { get; init; }

    /// <summary>Lọc theo mã khóa (NguoiLX_HoSo.MaKhoaHoc / KhoaHoc.MaKH).</summary>
    public string? MaKhoa { get; init; }

    /// <summary>Lọc theo hạng đào tạo của khóa (NguoiLX_HoSo.HangDaoTao hoặc DM_HangDT.TenHangDT).</summary>
    public string? HangGPLX { get; init; }

    /// <summary>Lọc theo giới tính (NguoiLX.GioiTinh, char(1)).</summary>
    public string? GioiTinh { get; init; }

    /// <summary>Trả về bản sao đã chuẩn hóa (trim, rỗng → null).</summary>
    public HocVienSourceFilter Normalized() => new()
    {
        Keyword = Clean(Keyword),
        MaKhoa = Clean(MaKhoa),
        HangGPLX = Clean(HangGPLX),
        GioiTinh = Clean(GioiTinh),
    };

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Bộ lọc rỗng (không điều kiện).</summary>
    public static HocVienSourceFilter Empty => new();
}

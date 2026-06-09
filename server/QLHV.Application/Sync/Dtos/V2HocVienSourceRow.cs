namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Một dòng học viên đọc từ nguồn CSDT_V2 (chỉ đọc).
/// Tên thuộc tính ánh xạ tới cột nguồn V2 thực tế (xem docs/sync-v2-design.md).
/// Việc đọc thực tế bằng Dapper sẽ được hiện thực ở Phase B.
/// </summary>
public sealed class V2HocVienSourceRow
{
    /// <summary>Mã đăng ký (V2: NguoiLX.MaDK / NguoiLX_HoSo.MaDK).</summary>
    public string MaDK { get; init; } = string.Empty;

    /// <summary>Mã khóa học (V2: NguoiLX_HoSo.MaKhoaHoc).</summary>
    public string? MaKhoaHoc { get; init; }

    /// <summary>Tên khóa (V2: KhoaHoc.TenKH).</summary>
    public string? TenKH { get; init; }

    /// <summary>Hạng đào tạo của khóa (V2: KhoaHoc.HangGPLX).</summary>
    public string? HangGPLX { get; init; }

    /// <summary>Họ và tên (V2: NguoiLX.HoVaTen).</summary>
    public string? HoVaTen { get; init; }

    /// <summary>Ngày sinh (V2: NguoiLX.NgaySinh).</summary>
    public DateTime? NgaySinh { get; init; }

    /// <summary>Số CCCD (V2: NguoiLX.SoCMT).</summary>
    public string? SoCMT { get; init; }

    /// <summary>Giới tính (V2: cột nguồn cần xác nhận - xem mục "Điểm chưa chắc chắn").</summary>
    public string? GioiTinh { get; init; }

    /// <summary>Địa chỉ thường trú (V2: cột nguồn cần xác nhận).</summary>
    public string? DiaChiThuongTru { get; init; }

    /// <summary>Số GPLX đã có (V2: cột nguồn cần xác nhận, có thể từ NguoiLX_GPLX).</summary>
    public string? SoGPLXDaCo { get; init; }

    /// <summary>Hạng GPLX đã có (V2: cột nguồn cần xác nhận, có thể từ NguoiLX_GPLX).</summary>
    public string? HangGPLXDaCo { get; init; }

    /// <summary>Người nhận hồ sơ (V2: cột nguồn cần xác nhận).</summary>
    public string? NguoiNhanHoSo { get; init; }
}

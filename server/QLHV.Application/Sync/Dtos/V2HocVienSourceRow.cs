namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Một dòng học viên đọc từ nguồn CSDT_V2 (chỉ đọc).
/// Tên thuộc tính ánh xạ tới cột nguồn V2 thực tế (xem docs/sync-v2-design.md).
/// </summary>
public sealed class V2HocVienSourceRow
{
    /// <summary>Mã đăng ký (V2: NguoiLX.MaDK / NguoiLX_HoSo.MaDK).</summary>
    public string MaDK { get; init; } = string.Empty;

    /// <summary>Mã khóa học (V2: NguoiLX_HoSo.MaKhoaHoc).</summary>
    public string? MaKhoaHoc { get; init; }

    /// <summary>Tên khóa (V2: KhoaHoc.TenKH).</summary>
    public string? TenKH { get; init; }

    /// <summary>Mã hạng đào tạo của khóa (V2: NguoiLX_HoSo.HangDaoTao).</summary>
    public string? HangDaoTao { get; init; }

    /// <summary>Tên hạng đào tạo của khóa (V2: DM_HangDT.TenHangDT).</summary>
    public string? TenHangDT { get; init; }

    /// <summary>Hạng GPLX trong hồ sơ V2. Không dùng cho App_HocVien.HangGPLXHoc.</summary>
    public string? HangGPLX { get; init; }

    /// <summary>Họ và tên (V2: NguoiLX.HoVaTen).</summary>
    public string? HoVaTen { get; init; }

    /// <summary>Ngày sinh (V2: NguoiLX.NgaySinh).</summary>
    public DateTime? NgaySinh { get; init; }

    /// <summary>Số CCCD (V2: NguoiLX.SoCMT).</summary>
    public string? SoCMT { get; init; }

    /// <summary>Giới tính (V2: NguoiLX.GioiTinh; giữ nguyên raw value).</summary>
    public string? GioiTinh { get; init; }

    /// <summary>Địa chỉ thường trú nhập tay ở V2 (V2: NguoiLX.NoiTT), dùng fallback khi không join được DM_DVHC.</summary>
    public string? NoiTT { get; init; }

    /// <summary>Địa chỉ thường trú đầy đủ từ danh mục hành chính (V2: DM_DVHC.TenDayDu).</summary>
    public string? NoiTTTenDayDu { get; init; }

    /// <summary>Thuộc tính tương thích cũ; không được SELECT mới, chỉ dùng fallback cho test/caller cũ.</summary>
    public string? DiaChiThuongTru { get; init; }

    /// <summary>Số GPLX đã có (V2: NguoiLX_HoSo.SoGPLXDaCo).</summary>
    public string? SoGPLXDaCo { get; init; }

    /// <summary>Hạng GPLX đã có (V2: NguoiLX_HoSo.HangGPLXDaCo).</summary>
    public string? HangGPLXDaCo { get; init; }

    /// <summary>Người nhận hồ sơ (V2: NguoiLX_HoSo.NguoiNhanHSo).</summary>
    public string? NguoiNhanHoSo { get; init; }
}

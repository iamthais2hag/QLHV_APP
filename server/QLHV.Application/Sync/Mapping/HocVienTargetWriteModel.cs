namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Mô hình giá trị sẽ ghi vào QLHV_APP.dbo.App_HocVien (CHƯA thực thi ở Phase B3A).
/// Giá trị đã áp dụng quy tắc dữ liệu: chỉ trim, bảo toàn nguyên bản, không tự chuyển đổi.
/// </summary>
public sealed class HocVienTargetWriteModel
{
    /// <summary>Khóa nghiệp vụ (App_HocVien.MaDK).</summary>
    public string MaDK { get; init; } = string.Empty;

    public string? MaKhoa { get; init; }
    public string? TenKhoa { get; init; }

    /// <summary>Hạng đào tạo của khóa (App_HocVien.HangGPLXHoc).</summary>
    public string? HangGPLXHoc { get; init; }

    public string? HoTen { get; init; }
    public DateTime? NgaySinh { get; init; }

    /// <summary>Giới tính - giữ nguyên giá trị gốc (char(1) đã trim).</summary>
    public string? GioiTinh { get; init; }

    /// <summary>Số CCCD - giữ nguyên giá trị gốc (chỉ trim), KHÔNG tự chuyển CMND→CCCD.</summary>
    public string? SoCCCD { get; init; }

    public string? DiaChiThuongTru { get; init; }
    public string? SoGPLXDaCo { get; init; }
    public string? HangGPLXDaCo { get; init; }
    public string? NguoiNhanHoSo { get; init; }

    /// <summary>Luôn = "V2" cho đồng bộ một chiều.</summary>
    public string SourceOfTruth { get; init; } = HocVienDataRules.SourceOfTruthV2;
}

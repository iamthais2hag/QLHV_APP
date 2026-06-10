namespace QLHV.Application.HocVien.Dtos;

/// <summary>
/// Một dòng học viên trong danh sách (chỉ đọc). Dữ liệu gốc từ nguồn V2 là chỉ đọc.
/// </summary>
public sealed class HocVienListItemDto
{
    /// <summary>Mã đăng ký (MaDK) - giá trị đầy đủ, có thể rất dài.</summary>
    public string MaDK { get; init; } = string.Empty;

    /// <summary>Họ và tên học viên.</summary>
    public string HoTen { get; init; } = string.Empty;

    /// <summary>Ngày sinh.</summary>
    public DateOnly? NgaySinh { get; init; }

    /// <summary>Giới tính.</summary>
    public string? GioiTinh { get; init; }

    /// <summary>Số CCCD.</summary>
    public string? SoCCCD { get; init; }

    /// <summary>Địa chỉ thường trú.</summary>
    public string? DiaChiThuongTru { get; init; }

    /// <summary>Số GPLX đã có.</summary>
    public string? SoGPLXDaCo { get; init; }

    /// <summary>Hạng GPLX đã có.</summary>
    public string? HangGPLXDaCo { get; init; }

    /// <summary>Người nhận hồ sơ.</summary>
    public string? NguoiNhanHoSo { get; init; }

    /// <summary>Tên khóa học.</summary>
    public string? TenKhoa { get; init; }

    /// <summary>Mã khóa học.</summary>
    public string? MaKhoa { get; init; }

    /// <summary>Nguồn dữ liệu đang làm nguồn sự thật cho bản ghi.</summary>
    public string? SourceOfTruth { get; init; }

    /// <summary>Trạng thái đồng bộ gần nhất từ V2.</summary>
    public string? LastSyncStatus { get; init; }
}

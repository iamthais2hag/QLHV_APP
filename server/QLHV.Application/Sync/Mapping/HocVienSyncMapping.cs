namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Kế hoạch ánh xạ đồng bộ học viên CSDT_V2 -> QLHV_APP (dbo.App_HocVien).
///
/// Cột đích lấy từ schema thật: database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql.
/// Trường nguồn V2 được suy ra từ các script chuyển dữ liệu hiện có
/// (NguoiLX, NguoiLX_HoSo, NguoiLX_GPLX, KhoaHoc) và CẦN xác nhận lại với schema V2 thật ở Phase B.
/// "STT" là số thứ tự hiển thị, không phải cột lưu trữ nên không tham gia đồng bộ.
/// </summary>
public static class HocVienSyncMapping
{
    public static IReadOnlyList<HocVienFieldMapping> Fields { get; } = new List<HocVienFieldMapping>
    {
        new()
        {
            ColumnLabel = "Mã đăng ký",
            TargetColumn = "MaDK",
            SourceFieldPlanned = "NguoiLX_HoSo.MaDK",
            Confidence = MappingConfidence.Inferred,
            Note = "Khóa nghiệp vụ; UNIQUE trong App_HocVien.",
        },
        new()
        {
            ColumnLabel = "Họ và tên",
            TargetColumn = "HoTen",
            SourceFieldPlanned = "NguoiLX.HoVaTen",
            Confidence = MappingConfidence.Inferred,
        },
        new()
        {
            ColumnLabel = "Ngày sinh",
            TargetColumn = "NgaySinh",
            SourceFieldPlanned = "NguoiLX.NgaySinh",
            Confidence = MappingConfidence.Inferred,
        },
        new()
        {
            ColumnLabel = "Giới tính",
            TargetColumn = "GioiTinh",
            SourceFieldPlanned = "NguoiLX.GioiTinh (cần xác nhận)",
            Confidence = MappingConfidence.Unknown,
        },
        new()
        {
            ColumnLabel = "Số CCCD",
            TargetColumn = "SoCCCD",
            SourceFieldPlanned = "NguoiLX.SoCMT (cần xác nhận CCCD vs CMT)",
            Confidence = MappingConfidence.Unknown,
            Note = "Script kiểm tra dùng SoCMT làm CCCD; cần xác nhận cột CCCD chuẩn ở V2.",
        },
        new()
        {
            ColumnLabel = "Địa chỉ thường trú",
            TargetColumn = "DiaChiThuongTru",
            SourceFieldPlanned = "NguoiLX.DiaChiThuongTru (cần xác nhận)",
            Confidence = MappingConfidence.Unknown,
        },
        new()
        {
            ColumnLabel = "Số GPLX đã có",
            TargetColumn = "SoGPLXDaCo",
            SourceFieldPlanned = "NguoiLX_GPLX.SoGPLX (cần xác nhận)",
            Confidence = MappingConfidence.Unknown,
        },
        new()
        {
            ColumnLabel = "Hạng GPLX đã có",
            TargetColumn = "HangGPLXDaCo",
            SourceFieldPlanned = "NguoiLX_GPLX.HangGPLX (cần xác nhận)",
            Confidence = MappingConfidence.Unknown,
        },
        new()
        {
            ColumnLabel = "Người nhận hồ sơ",
            TargetColumn = "NguoiNhanHoSo",
            SourceFieldPlanned = "NguoiLX_HoSo.NguoiNhanHoSo (cần xác nhận)",
            Confidence = MappingConfidence.Unknown,
        },
        new()
        {
            ColumnLabel = "Tên khóa",
            TargetColumn = "TenKhoa",
            SourceFieldPlanned = "KhoaHoc.TenKH",
            Confidence = MappingConfidence.Inferred,
        },
        new()
        {
            ColumnLabel = "Mã khóa",
            TargetColumn = "MaKhoa",
            SourceFieldPlanned = "KhoaHoc.MaKH",
            Confidence = MappingConfidence.Inferred,
        },
    };
}

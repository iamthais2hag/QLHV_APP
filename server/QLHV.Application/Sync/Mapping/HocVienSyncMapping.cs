namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Kế hoạch ánh xạ đồng bộ học viên CSDT_V2 -> QLHV_APP (dbo.App_HocVien).
///
/// Cột đích lấy từ schema thật: database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql.
/// Trường nguồn V2 đã đối chiếu với database/reference/V2_schema_full.sql.
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
            SourceFieldPlanned = "NguoiLX.MaDK / NguoiLX_HoSo.MaDK",
            Confidence = MappingConfidence.Confirmed,
            Note = "Khóa nghiệp vụ; UNIQUE trong App_HocVien.",
        },
        new()
        {
            ColumnLabel = "Họ và tên",
            TargetColumn = "HoTen",
            SourceFieldPlanned = "NguoiLX.HoVaTen",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Ngày sinh",
            TargetColumn = "NgaySinh",
            SourceFieldPlanned = "NguoiLX.NgaySinh",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Giới tính",
            TargetColumn = "GioiTinh",
            SourceFieldPlanned = "NguoiLX.GioiTinh",
            Confidence = MappingConfidence.Confirmed,
            Note = "Giữ nguyên raw value từ V2; quy đổi hiển thị sẽ chốt sau khi xác nhận dữ liệu thực.",
        },
        new()
        {
            ColumnLabel = "Số CCCD",
            TargetColumn = "SoCCCD",
            SourceFieldPlanned = "NguoiLX.SoCMT",
            Confidence = MappingConfidence.Confirmed,
            Note = "Trim/preserve only; không tự đổi CMND 9 số thành CCCD 12 số.",
        },
        new()
        {
            ColumnLabel = "Địa chỉ thường trú",
            TargetColumn = "DiaChiThuongTru",
            SourceFieldPlanned = "DM_DVHC.TenDayDu từ NguoiLX.NoiTT_MaDVQL + NguoiLX.NoiTT_MaDVHC = DM_DVHC.MaDV",
            Confidence = MappingConfidence.Confirmed,
            Note = "Fallback NguoiLX.NoiTT nếu không join được DM_DVHC.",
        },
        new()
        {
            ColumnLabel = "Số GPLX đã có",
            TargetColumn = "SoGPLXDaCo",
            SourceFieldPlanned = "NguoiLX_HoSo.SoGPLXDaCo",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Hạng GPLX đã có",
            TargetColumn = "HangGPLXDaCo",
            SourceFieldPlanned = "NguoiLX_HoSo.HangGPLXDaCo",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Người nhận hồ sơ",
            TargetColumn = "NguoiNhanHoSo",
            SourceFieldPlanned = "NguoiLX_HoSo.NguoiNhanHSo",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Tên khóa",
            TargetColumn = "TenKhoa",
            SourceFieldPlanned = "KhoaHoc.TenKH",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Mã khóa",
            TargetColumn = "MaKhoa",
            SourceFieldPlanned = "NguoiLX_HoSo.MaKhoaHoc / KhoaHoc.MaKH",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Mã hạng học",
            TargetColumn = "MaHangDT",
            SourceFieldPlanned = "NguoiLX_HoSo.HangDaoTao",
            Confidence = MappingConfidence.Confirmed,
        },
        new()
        {
            ColumnLabel = "Hạng học",
            TargetColumn = "HangGPLXHoc",
            SourceFieldPlanned = "NguoiLX_HoSo.HangDaoTao -> DM_HangDT.MaHangDT -> DM_HangDT.TenHangDT",
            Confidence = MappingConfidence.Confirmed,
            Note = "Không dùng trường hạng GPLX hồ sơ cho hạng học.",
        },
    };
}

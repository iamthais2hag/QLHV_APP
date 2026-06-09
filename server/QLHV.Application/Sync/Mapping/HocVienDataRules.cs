namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Hằng số tham chiếu cho quy tắc dữ liệu Học viên khi đồng bộ V2 → QLHV_APP.
/// Tài liệu chi tiết: docs/hoc-vien-data-rules.md.
///
/// PHASE B2.5: chỉ là hằng số tham chiếu, KHÔNG đổi hành vi runtime.
/// Tầng đồng bộ phải bảo toàn giá trị gốc (chỉ trim) và KHÔNG tự ý chuyển đổi:
///   - SoCMT/SoCCCD: lưu nguyên giá trị gốc, không chuyển CMND 9 số sang CCCD 12 số.
///   - GioiTinh: giữ nguyên char(1) gốc; không hardcode quy đổi Nam/Nữ khi tập giá trị V2 chưa xác nhận.
///   - TrangThai: mặc định đọc tất cả bản ghi cho tới khi quy tắc hủy/trạng thái được xác nhận.
/// </summary>
public static class HocVienDataRules
{
    /// <summary>Độ dài kỳ vọng của số CCCD chuẩn (12 chữ số).</summary>
    public const int CccdExpectedLength = 12;

    /// <summary>Độ dài CMND 9 số (giá trị cũ, KHÔNG tự chuyển đổi).</summary>
    public const int CmndOldLength = 9;

    /// <summary>
    /// Đánh dấu nguồn dữ liệu - dùng cho cột App_HocVien.SourceOfTruth.
    /// </summary>
    public const string SourceOfTruthV2 = "V2";

    /// <summary>
    /// Phase B2/B3 mặc định KHÔNG lọc theo TrangThai. Bộ lọc tùy chọn (nếu có) phải là
    /// cấu hình runtime, mặc định tắt, và được ghi nhật ký.
    /// </summary>
    public const bool ApplyTrangThaiFilterByDefault = false;
}

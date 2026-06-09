namespace QLHV.Application.Sync.Connections;

/// <summary>
/// Cung cấp chuỗi kết nối một cách an toàn cho các dịch vụ đồng bộ.
/// Mục tiêu thiết kế:
/// - Chuỗi kết nối KHÔNG hardcode trong mã nguồn.
/// - QLHV_APP: lấy từ biến môi trường / user-secrets / file cấu hình server được bảo vệ.
/// - CSDT_V1, CSDT_V2: cấu hình từ màn hình Admin "Cấu hình kết nối dữ liệu",
///   lưu mã hóa khi nghỉ (encrypted at rest); không phải sửa code khi đổi SQL Server/CSDL.
/// Cài đặt cụ thể nằm ở tầng Infrastructure.
/// </summary>
public interface IConnectionSettingsProvider
{
    /// <summary>Lấy kết nối tới database trung tâm QLHV_APP.</summary>
    Task<ResolvedConnection> GetQlhvAppConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Lấy kết nối tới hệ nguồn (CSDT_V1 hoặc CSDT_V2).</summary>
    Task<ResolvedConnection> GetSourceConnectionAsync(
        SourceSystem source,
        CancellationToken cancellationToken = default);

    /// <summary>Lấy bản hiển thị an toàn (đã che mật khẩu) cho màn hình cấu hình.</summary>
    Task<ConnectionSettingsView> GetViewAsync(
        SourceSystem source,
        CancellationToken cancellationToken = default);
}

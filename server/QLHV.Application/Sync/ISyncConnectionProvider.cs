namespace QLHV.Application.Sync;

/// <summary>
/// Cung cấp thông tin về trạng thái cấu hình chuỗi kết nối mà KHÔNG để lộ giá trị thật.
/// Dùng để kiểm tra cấu hình trong dry-run.
/// </summary>
public interface ISyncConnectionProvider
{
    /// <summary>
    /// Cho biết chuỗi kết nối với tên cho trước đã được cấu hình thật hay chưa
    /// (đã đặt giá trị và không còn là placeholder dạng "__...__").
    /// Không trả về giá trị chuỗi kết nối.
    /// </summary>
    bool IsConfigured(string connectionName);
}

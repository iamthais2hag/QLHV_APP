using Microsoft.Extensions.Configuration;
using QLHV.Application.Sync;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Kiểm tra trạng thái cấu hình chuỗi kết nối dựa trên <see cref="IConfiguration"/>.
/// Không bao giờ trả về giá trị chuỗi kết nối ra ngoài.
/// </summary>
public sealed class SyncConnectionProvider : ISyncConnectionProvider
{
    private readonly IConfiguration _configuration;

    public SyncConnectionProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsConfigured(string connectionName)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return false;
        }

        var value = _configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Giá trị mẫu dạng "__DB_SERVER__", "__DB_PASSWORD__"... được coi là CHƯA cấu hình.
        return !value.Contains("__", StringComparison.Ordinal);
    }
}

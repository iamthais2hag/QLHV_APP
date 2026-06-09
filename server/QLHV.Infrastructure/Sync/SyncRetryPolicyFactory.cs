using Polly;
using Polly.Retry;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Tạo policy retry (Polly) cho các thao tác đồng bộ.
/// Phase A: chỉ tạo cấu trúc policy; KHÔNG thực thi đồng bộ thật.
/// </summary>
public static class SyncRetryPolicyFactory
{
    /// <summary>
    /// Policy retry với backoff lũy thừa. Mặc định thử lại 3 lần (2s, 4s, 8s).
    /// Việc bao bọc thao tác thật bằng policy này sẽ thực hiện ở Phase B.
    /// </summary>
    public static AsyncRetryPolicy CreateDefault(int retryCount = 3)
    {
        return Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }
}

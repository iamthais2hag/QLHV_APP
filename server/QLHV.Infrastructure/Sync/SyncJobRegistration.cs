namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Cấu trúc đăng ký job Hangfire cho đồng bộ V2.
///
/// Phase A: CHỦ ĐÍCH chưa lập lịch bất kỳ recurring job nào. Lớp này chỉ định nghĩa
/// điểm móc để Phase B thêm lịch (ví dụ qua IRecurringJobManager) sau khi đã cấu hình
/// chuỗi kết nối thật và bật ghi dữ liệu.
/// </summary>
public static class SyncJobRegistration
{
    /// <summary>Hàng đợi Hangfire dành cho job đồng bộ.</summary>
    public const string Queue = "dong-bo-v2";

    /// <summary>
    /// Nơi sẽ khai báo recurring job ở Phase B. Hiện không làm gì để tránh chạy job tự động.
    /// </summary>
    public static void ConfigureRecurringJobs()
    {
        // Phase B: ví dụ
        // recurringJobManager.AddOrUpdate<IHocVienSyncJob>(
        //     IHocVienSyncJob.JobName, job => job.ExecuteAsync(CancellationToken.None), Cron.Daily);
        //
        // Phase A: không lập lịch.
    }
}

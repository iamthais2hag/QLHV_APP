namespace QLHV.Application.Sync;

/// <summary>
/// Hợp đồng cho job đồng bộ học viên (sẽ chạy nền qua Hangfire ở Phase B).
/// Phase A: chỉ đăng ký cấu trúc job, KHÔNG lập lịch và KHÔNG thực thi đồng bộ thật.
/// </summary>
public interface IHocVienSyncJob
{
    /// <summary>Tên job dùng cho Hangfire và nhật ký đồng bộ (App_DongBoLog.JobName).</summary>
    static string JobName => "DongBoV2.HocVien";

    /// <summary>
    /// Điểm vào của job. Ở Phase A chỉ thực hiện dry-run an toàn.
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}

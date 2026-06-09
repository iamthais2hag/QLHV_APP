using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Ghi nhật ký một lần chạy đồng bộ vào dbo.App_DongBoLog.
/// PHASE B3A: chỉ khai báo hợp đồng. KHÔNG ghi trong dry-run và CHƯA hiện thực ghi thật
/// (sẽ làm ở Phase B3B cùng luồng thực thi đồng bộ).
/// </summary>
public interface ISyncRunLogWriter
{
    /// <summary>Ghi một bản ghi nhật ký đồng bộ (chỉ gọi khi KHÔNG phải dry-run, ở Phase B3B).</summary>
    Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default);
}

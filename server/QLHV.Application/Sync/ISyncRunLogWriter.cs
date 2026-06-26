using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Ghi nhật ký một lần chạy đồng bộ vào dbo.App_DongBoLog.
/// Không ghi trong dry-run. Implementation phải chặn ghi khi EnableTargetWrites=false.
/// </summary>
public interface ISyncRunLogWriter
{
    /// <summary>Ghi một bản ghi nhật ký đồng bộ sau khi execution guard đã được xác nhận.</summary>
    Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default);
}

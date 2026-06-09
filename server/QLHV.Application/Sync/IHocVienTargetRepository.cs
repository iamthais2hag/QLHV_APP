namespace QLHV.Application.Sync;

/// <summary>
/// Ghi dữ liệu học viên vào QLHV_APP (dbo.App_HocVien).
/// Cài đặt nằm ở tầng Infrastructure và CHƯA được triển khai trong Phase A
/// (không ghi SQL Server, không dùng SqlBulkCopy ở giai đoạn này).
/// </summary>
public interface IHocVienTargetRepository
{
    /// <summary>
    /// Cập nhật/chèn theo lô (upsert) vào App_HocVien. Sẽ triển khai ở Phase B bằng SqlBulkCopy + MERGE.
    /// </summary>
    Task<int> UpsertBatchAsync(CancellationToken cancellationToken = default);
}

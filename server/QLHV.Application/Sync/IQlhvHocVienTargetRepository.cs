using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Ghi/đối chiếu dữ liệu học viên tại đích QLHV_APP (dbo.App_HocVien).
/// Thao tác GHI sẽ chỉ được hiện thực ở Phase B (SqlBulkCopy/merge có giao dịch).
/// Phase A: chỉ khai báo hợp đồng, không ghi vào SQL Server.
/// </summary>
public interface IQlhvHocVienTargetRepository
{
    /// <summary>Đếm số học viên hiện có tại đích (chỉ đọc).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Nạp một lô học viên từ nguồn V2 vào đích theo cơ chế đồng bộ một chiều.
    /// CHƯA hiện thực ở Phase A. Khi gọi ở Phase A sẽ ném <see cref="NotImplementedException"/>
    /// để bảo đảm không có thao tác ghi ngoài ý muốn.
    /// </summary>
    Task<int> UpsertBatchAsync(
        IReadOnlyList<V2HocVienSourceRow> rows,
        bool dryRun,
        CancellationToken cancellationToken = default);
}

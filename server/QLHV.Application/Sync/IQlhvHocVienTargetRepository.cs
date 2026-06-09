using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Ghi/đối chiếu dữ liệu học viên tại đích QLHV_APP (dbo.App_HocVien).
///
/// PHASE B3A: cho phép thao tác CHỈ ĐỌC (đếm, lấy tập khóa MaDK đã có) để dựng kế hoạch dry-run.
/// Thao tác GHI (<see cref="UpsertBatchAsync"/>) CHƯA hiện thực; gọi sẽ ném lỗi để chặn ghi ngoài ý muốn.
/// </summary>
public interface IQlhvHocVienTargetRepository
{
    /// <summary>Đếm số học viên hiện có tại đích (CHỈ ĐỌC).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy tập khóa MaDK đã tồn tại ở đích trong số các khóa cho trước (CHỈ ĐỌC).
    /// Dùng để phân loại Insert/Update khi dựng kế hoạch dry-run.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật/chèn theo lô (upsert) vào App_HocVien bằng staging + MERGE keyed on MaDK.
    /// CHƯA hiện thực ở Phase B3A; gọi sẽ ném <see cref="NotSupportedException"/>.
    /// </summary>
    Task<int> UpsertBatchAsync(
        IReadOnlyList<V2HocVienSourceRow> rows,
        bool dryRun,
        CancellationToken cancellationToken = default);
}

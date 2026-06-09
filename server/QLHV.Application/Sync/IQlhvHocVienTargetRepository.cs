using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Ghi/đối chiếu dữ liệu học viên tại đích QLHV_APP (dbo.App_HocVien).
///
/// Thao tác CHỈ ĐỌC (CountAsync, GetExistingKeysAsync) dùng cho dry-run/kế hoạch.
/// Thao tác GHI (UpsertBatchAsync) chỉ chạy ở luồng execute có kiểm soát: staging + MERGE keyed on MaDK,
/// trong transaction, rollback khi lỗi, KHÔNG xóa vật lý. Có gác công tắc EnableTargetWrites (defense-in-depth).
/// </summary>
public interface IQlhvHocVienTargetRepository
{
    /// <summary>Đếm số học viên hiện có tại đích (CHỈ ĐỌC).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Lấy tập khóa MaDK đã tồn tại ở đích trong số các khóa cho trước (CHỈ ĐỌC).</summary>
    Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert một lô vào App_HocVien bằng SqlBulkCopy vào bảng tạm rồi MERGE keyed on MaDK
    /// trong một transaction (rollback khi lỗi). Chỉ UPDATE khi V2RowHash khác; INSERT khi chưa có;
    /// KHÔNG xóa vật lý. Ném lỗi nếu EnableTargetWrites=false.
    /// </summary>
    Task<UpsertCounts> UpsertBatchAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default);
}

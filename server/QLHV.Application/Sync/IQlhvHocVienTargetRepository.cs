using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

<<<<<<< HEAD
/// <summary>Read and guarded write access to QLHV_APP.dbo.App_HocVien.</summary>
=======
/// <summary>
/// Ghi/đối chiếu dữ liệu học viên tại đích QLHV_APP (dbo.App_HocVien).
///
/// Thao tác CHỈ ĐỌC (CountAsync, GetExistingKeysAsync) dùng cho dry-run/kế hoạch.
/// Thao tác GHI (UpsertBatchAsync) chỉ chạy ở luồng execute có kiểm soát: staging + MERGE keyed on MaDK,
/// trong transaction, rollback khi lỗi, KHÔNG xóa vật lý. Có gác công tắc EnableTargetWrites (defense-in-depth).
/// </summary>
>>>>>>> task5-phase-b3b-guarded-write-path
public interface IQlhvHocVienTargetRepository
{
    /// <summary>Counts current non-deleted target rows. Read-only.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

<<<<<<< HEAD
    /// <summary>Returns target keys already present for the supplied MaDK values. Read-only.</summary>
=======
    /// <summary>Lấy tập khóa MaDK đã tồn tại ở đích trong số các khóa cho trước (CHỈ ĐỌC).</summary>
>>>>>>> task5-phase-b3b-guarded-write-path
    Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default);

    /// <summary>
<<<<<<< HEAD
    /// Upserts source rows into App_HocVien using transaction, temp staging, SqlBulkCopy, and MERGE.
    /// The caller is responsible for checking execution guards before calling with dryRun=false.
    /// </summary>
    Task<HocVienUpsertResultDto> UpsertBatchAsync(
        IReadOnlyList<V2HocVienSourceRow> rows,
        bool dryRun,
=======
    /// Upsert một lô vào App_HocVien bằng SqlBulkCopy vào bảng tạm rồi MERGE keyed on MaDK
    /// trong một transaction (rollback khi lỗi). Chỉ UPDATE khi V2RowHash khác; INSERT khi chưa có;
    /// KHÔNG xóa vật lý. Ném lỗi nếu EnableTargetWrites=false.
    /// </summary>
    Task<UpsertCounts> UpsertBatchAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
>>>>>>> task5-phase-b3b-guarded-write-path
        CancellationToken cancellationToken = default);
}

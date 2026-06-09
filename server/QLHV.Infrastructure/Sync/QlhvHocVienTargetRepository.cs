using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Ghi/đối chiếu học viên tại đích QLHV_APP.dbo.App_HocVien.
///
/// Phase A: KHÔNG ghi vào SQL Server. Thao tác ghi (SqlBulkCopy + merge có giao dịch, rollback khi lỗi)
/// sẽ được hiện thực ở Phase B. <see cref="UpsertBatchAsync"/> hiện ném
/// <see cref="NotSupportedException"/> để chặn mọi thao tác ghi ngoài ý muốn.
/// </summary>
public sealed class QlhvHocVienTargetRepository : IQlhvHocVienTargetRepository
{
    private const string PhaseBMessage =
        "Ghi dữ liệu vào QLHV_APP sẽ được hiện thực ở Phase B. Phase A không ghi SQL Server.";

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(PhaseBMessage);

    public Task<int> UpsertBatchAsync(
        IReadOnlyList<V2HocVienSourceRow> rows,
        bool dryRun,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(PhaseBMessage);
}

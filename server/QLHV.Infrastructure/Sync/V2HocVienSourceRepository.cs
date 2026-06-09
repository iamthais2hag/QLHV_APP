using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Đọc học viên từ nguồn CSDT_V2 bằng Dapper (chỉ đọc).
///
/// Phase A: KHÔNG mở kết nối tới SQL Server. Các phương thức đọc thật sẽ được hiện thực ở Phase B
/// (truy vấn Dapper trên CSDT_V2.dbo.NguoiLX / NguoiLX_HoSo / KhoaHoc). Hiện ném
/// <see cref="NotSupportedException"/> để bảo đảm không có kết nối ngoài ý muốn.
/// </summary>
public sealed class V2HocVienSourceRepository : IV2HocVienSourceRepository
{
    private const string PhaseBMessage =
        "Đọc dữ liệu nguồn V2 sẽ được hiện thực ở Phase B. Phase A không kết nối SQL Server.";

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(PhaseBMessage);

    public Task<IReadOnlyList<V2HocVienSourceRow>> ReadBatchAsync(
        int offset,
        int batchSize,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(PhaseBMessage);
}

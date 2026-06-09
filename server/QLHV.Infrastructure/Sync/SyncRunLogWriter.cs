using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Ghi nhật ký đồng bộ vào dbo.App_DongBoLog.
///
/// PHASE B3A: CHƯA hiện thực ghi. <see cref="WriteAsync"/> ném <see cref="NotSupportedException"/>
/// để bảo đảm dry-run/Phase B3A không ghi vào SQL Server. Câu lệnh INSERT chuẩn bị sẵn (không thực thi)
/// nằm tại <see cref="InsertSql"/> để dùng ở Phase B3B.
/// </summary>
public sealed class SyncRunLogWriter : ISyncRunLogWriter
{
    /// <summary>
    /// Câu lệnh INSERT chuẩn bị cho App_DongBoLog. CHƯA THỰC THI ở Phase B3A.
    /// Tham số hóa đầy đủ; không nhúng giá trị trực tiếp.
    /// </summary>
    internal const string InsertSql = @"
INSERT INTO dbo.App_DongBoLog
    (JobName, EntityType, SourceSystem, StartedAt, EndedAt, DurationMs, Status,
     TotalRead, TotalInserted, TotalUpdated, TotalSkipped, TotalError, RetryCount,
     ErrorMessage, DetailJson, CreatedBy)
OUTPUT INSERTED.DongBoLogId
VALUES
    (@JobName, @EntityType, @SourceSystem, @StartedAt, @EndedAt, @DurationMs, @Status,
     @TotalRead, @TotalInserted, @TotalUpdated, @TotalSkipped, @TotalError, @RetryCount,
     @ErrorMessage, @DetailJson, @CreatedBy);
";

    private const string PhaseBMessage =
        "Ghi App_DongBoLog se duoc hien thuc o Phase B3B. Phase B3A khong ghi SQL Server.";

    public Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(PhaseBMessage);
}

using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Kết quả của thao tác chạy thử (dry-run) đồng bộ học viên từ V2.
/// Tuyệt đối không ghi dữ liệu, không mở kết nối thật, không lộ chuỗi kết nối.
/// </summary>
public sealed class DryRunResultDto
{
    /// <summary>Luôn là true với dry-run.</summary>
    public bool IsDryRun => true;

    /// <summary>Có đủ điều kiện để chạy đồng bộ thật ở Phase B hay không.</summary>
    public bool CanRun { get; init; }

    /// <summary>Trạng thái tổng quát, ví dụ "SanSang", "ThieuCauHinh".</summary>
    public string Status { get; init; } = "ThieuCauHinh";

    /// <summary>Danh sách vấn đề cần xử lý trước khi chạy thật (an toàn, không chứa bí mật).</summary>
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    /// <summary>Kiểm tra kết nối đích QLHV_APP.</summary>
    public ConnectionCheckDto Target { get; init; } = new();

    /// <summary>Kiểm tra kết nối nguồn CSDT_V2.</summary>
    public ConnectionCheckDto Source { get; init; } = new();

    /// <summary>Tóm tắt dự kiến (các số đếm bằng 0 vì chưa chạy thật).</summary>
    public SyncSummaryDto PlannedSummary { get; init; } = new();

    /// <summary>Kế hoạch ánh xạ trường.</summary>
    public IReadOnlyList<HocVienFieldMapping> Mapping { get; init; } = Array.Empty<HocVienFieldMapping>();

    /// <summary>Tham số cấu hình đang áp dụng (không bí mật).</summary>
    public int BatchSize { get; init; }

    /// <summary>Thời gian chờ (giây).</summary>
    public int TimeoutSeconds { get; init; }
}

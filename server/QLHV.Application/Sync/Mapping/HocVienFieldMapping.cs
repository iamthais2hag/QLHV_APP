namespace QLHV.Application.Sync.Mapping;

/// <summary>Mức độ chắc chắn của một ánh xạ trường.</summary>
public enum MappingConfidence
{
    /// <summary>Đã xác nhận theo schema thật.</summary>
    Confirmed = 1,

    /// <summary>Suy ra từ script chuyển dữ liệu, cần xác nhận lại với schema V2 thật.</summary>
    Inferred = 2,

    /// <summary>Chưa rõ nguồn, cần làm rõ ở Phase B.</summary>
    Unknown = 3,
}

/// <summary>
/// Một dòng trong kế hoạch ánh xạ trường đồng bộ học viên từ CSDT_V2 sang QLHV_APP.
/// </summary>
public sealed class HocVienFieldMapping
{
    /// <summary>Nhãn cột hiển thị trong danh sách Học viên.</summary>
    public string ColumnLabel { get; init; } = string.Empty;

    /// <summary>Cột đích trong dbo.App_HocVien (QLHV_APP) - đã xác nhận theo schema.</summary>
    public string TargetColumn { get; init; } = string.Empty;

    /// <summary>Trường nguồn dự kiến ở CSDT_V2 (có thể cần xác nhận).</summary>
    public string SourceFieldPlanned { get; init; } = string.Empty;

    /// <summary>Mức độ chắc chắn của ánh xạ.</summary>
    public MappingConfidence Confidence { get; init; }

    /// <summary>Ghi chú thêm.</summary>
    public string? Note { get; init; }
}

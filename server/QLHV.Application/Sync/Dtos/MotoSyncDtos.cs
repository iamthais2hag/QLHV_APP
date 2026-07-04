namespace QLHV.Application.Sync.Dtos;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MotoSyncDirection
{
    V1_TO_V2 = 1,
    V2_TO_V1 = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MotoSyncMode
{
    INSERT_ONLY = 1,
    INSERT_AND_UPDATE = 2,
}

public sealed class MotoSyncPlanRequest
{
    public MotoSyncDirection Direction { get; set; }

    public string SourceProfileCode { get; set; } = string.Empty;

    public string TargetProfileCode { get; set; } = string.Empty;

    public string? MaKhoaHoc { get; set; }

    public bool AllowDirtyData { get; set; }
}

public sealed class MotoSyncKhoaHocOptionsQuery
{
    public MotoSyncDirection Direction { get; set; }

    public string SourceProfileCode { get; set; } = string.Empty;

    public string TargetProfileCode { get; set; } = string.Empty;

    public string? Search { get; set; }

    public int Take { get; set; } = 50;
}

public sealed class MotoSyncKhoaHocOptionDto
{
    public string MaKhoaHoc { get; init; } = string.Empty;

    public string? TenKhoaHoc { get; init; }

    public string? HangDaoTao { get; init; }

    public string? HangGPLX { get; init; }

    public string? NgayKhaiGiang { get; init; }

    public long SourceHocVienCount { get; init; }

    public long TargetHocVienCount { get; init; }

    public bool SourceKhoaHocExists { get; init; }

    public bool TargetKhoaHocExists { get; init; }

    public bool HasTargetKhoaHoc { get; init; }

    public long SourceOnlyHocVienCount { get; init; }

    public long TargetOnlyHocVienCount { get; init; }
}

public class MotoCenterTransferPlanRequest
{
    public string SourceProfileCode { get; set; } = "CSDT_V1";

    public string TargetProfileCode { get; set; } = "CSDT_V2";

    public string? MaKhoaHocCu { get; set; }

    public string? MaCSDTCu { get; set; }

    public string? MaCSDTMoi { get; set; }

    public string? MaSoGTVTMoi { get; set; }
}

public sealed class MotoCenterTransferTestRequest : MotoCenterTransferPlanRequest
{
    public string? ConfirmText { get; set; }
}

public sealed class MotoCenterTransferPlanDto
{
    public bool IsReadOnly { get; init; } = true;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string MaKhoaHocCu { get; init; } = string.Empty;

    public string MaKhoaHocMoi { get; init; } = string.Empty;

    public string MaCSDTCu { get; init; } = string.Empty;

    public string MaCSDTMoi { get; init; } = string.Empty;

    public string MaSoGTVTMoi { get; init; } = string.Empty;

    public bool TargetMaCSDTMoiExists { get; init; }

    public string? TargetMaCSDTMoiTenDV { get; init; }

    public bool TargetMaSoGTVTMoiExists { get; init; }

    public string? TargetMaSoGTVTMoiTenDV { get; init; }

    public long SourceKhoaHocCount { get; init; }

    public long SourceBaoCaoICount { get; init; }

    public long SourceNguoiLXCount { get; init; }

    public long SourceNguoiLXHoSoCount { get; init; }

    public long SourceNguoiLXHSGiayToCount { get; init; }

    public long TargetKhoaHocCuCount { get; init; }

    public long TargetKhoaHocMoiCount { get; init; }

    public long TargetBaoCaoICuCount { get; init; }

    public long TargetBaoCaoIMoiCount { get; init; }

    public long TargetNguoiLXHoSoCuCount { get; init; }

    public long TargetNguoiLXHoSoMoiCount { get; init; }

    public long TargetNguoiLXHSGiayToCuCount { get; init; }

    public long TargetNguoiLXHSGiayToMoiCount { get; init; }

    public long PlannedCopyNguoiLXHSGiayTo { get; init; }

    public bool Executable { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class MotoCenterTransferExecuteResultDto
{
    public bool Executed { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public MotoCenterTransferPlanDto? Plan { get; init; }

    public MotoCenterTransferSummaryDto? Summary { get; init; }
}

public sealed class MotoCenterTransferSummaryDto
{
    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string MaKhoaHocCu { get; init; } = string.Empty;

    public string MaKhoaHocMoi { get; init; } = string.Empty;

    public long CopiedKhoaHoc { get; init; }

    public long CopiedBaoCaoI { get; init; }

    public long CopiedNguoiLX { get; init; }

    public long CopiedNguoiLXHoSo { get; init; }

    public long CopiedNguoiLXHSGiayTo { get; init; }

    public long UpdatedNguoiLXHoSo { get; init; }

    public long UpdatedNguoiLX { get; init; }

    public long UpdatedKhoaHoc { get; init; }

    public long UpdatedBaoCaoI { get; init; }

    public long UpdatedGiayTo { get; init; }

    public long UpdatedNguoiLXHSGiayTo { get; init; }

    public long TargetKhoaHocMoiCountAfter { get; init; }

    public long TargetBaoCaoIMoiCountAfter { get; init; }

    public long TargetNguoiLXHoSoMoiCountAfter { get; init; }

    public long TargetNguoiLXHSGiayToMoiCountAfter { get; init; }

    public long TargetNguoiLXMoiCountAfter { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    public long DurationMs { get; init; }
}

public sealed class MotoSyncTestExecuteRequest
{
    public MotoSyncDirection Direction { get; set; }

    public MotoSyncMode SyncMode { get; set; } = MotoSyncMode.INSERT_ONLY;

    public string SourceProfileCode { get; set; } = string.Empty;

    public string TargetProfileCode { get; set; } = string.Empty;

    public string? MaKhoaHoc { get; set; }

    public string? ConfirmText { get; set; }
}

public sealed class MotoSyncPlanDto
{
    public bool IsReadOnly { get; init; } = true;

    public MotoSyncDirection Direction { get; init; }

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public bool AllowDirtyData { get; init; }

    public long SourceRows { get; init; }

    public long TargetRows { get; init; }

    public long ExactMaDkOverlap { get; init; }

    public long SourceOnly { get; init; }

    public long TargetOnly { get; init; }

    public long DuplicateBusinessKeyGroups { get; init; }

    public long ShortFullMaDkPairs { get; init; }

    public long MissingKhoaHocDependencies { get; init; }

    public long PlannedInsertKhoaHoc { get; init; }

    public long PlannedInsertBaoCaoI { get; init; }

    public long PlannedInsertNguoiLX { get; init; }

    public long PlannedInsertNguoiLXGPLX { get; init; }

    public long PlannedInsertNguoiLXHoSo { get; init; }

    public long PlannedInsertGiayTo { get; init; }

    public long PlannedUpdate { get; init; }

    public long PlannedUpdateNguoiLX { get; init; }

    public long PlannedUpdateNguoiLXHoSo { get; init; }

    public IReadOnlyList<MotoSyncUpdateSampleDto> UpdateSamples { get; init; } = Array.Empty<MotoSyncUpdateSampleDto>();

    public bool Executable { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();
}

public sealed class MotoSyncUpdateSampleDto
{
    public string MaDK { get; init; } = string.Empty;

    public string TableName { get; init; } = string.Empty;

    public IReadOnlyList<string> ChangedColumnNames { get; init; } = Array.Empty<string>();
}

public sealed class MotoSyncExecuteResultDto
{
    public bool Executed { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public MotoSyncExecuteSummaryDto? Summary { get; init; }

    public MotoSyncPlanDto? Plan { get; init; }

    public MotoSyncPlanDto? BeforePlan { get; init; }

    public MotoSyncPlanDto? AfterPlan { get; init; }

    public bool HasRemainingWork { get; init; }
}

public sealed class MotoSyncExecuteSummaryDto
{
    public MotoSyncDirection Direction { get; init; }

    public MotoSyncMode SyncMode { get; init; } = MotoSyncMode.INSERT_ONLY;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public long InsertedKhoaHoc { get; init; }

    public long InsertedBaoCaoI { get; init; }

    public long InsertedNguoiLX { get; init; }

    public long InsertedNguoiLXGPLX { get; init; }

    public long InsertedNguoiLXHoSo { get; init; }

    public long InsertedGiayTo { get; init; }

    public long UpdatedNguoiLX { get; init; }

    public long UpdatedNguoiLXHoSo { get; init; }

    public long UpdatedRows { get; init; }

    public long DeletedRows { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    public long DurationMs { get; init; }
}

public sealed class MotoSyncRunHistoryQuery
{
    public string? MaKhoaHoc { get; set; }

    public MotoSyncDirection? Direction { get; set; }

    public MotoSyncMode? SyncMode { get; set; }

    public int Take { get; set; } = 50;
}

public class MotoSyncRunHistoryListItemDto
{
    public long Id { get; init; }

    public DateTime CreatedAt { get; init; }

    public MotoSyncDirection Direction { get; init; }

    public MotoSyncMode SyncMode { get; init; }

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public bool Executed { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public long InsertedTotal { get; init; }

    public long UpdatedRows { get; init; }

    public long DeletedRows { get; init; }

    public long DurationMs { get; init; }

    public bool HasRemainingWork { get; init; }
}

public sealed class MotoSyncRunHistoryDetailDto : MotoSyncRunHistoryListItemDto
{
    public bool ConfirmTextMatched { get; init; }

    public long InsertedKhoaHoc { get; init; }

    public long InsertedBaoCaoI { get; init; }

    public long InsertedNguoiLX { get; init; }

    public long InsertedNguoiLXGPLX { get; init; }

    public long InsertedNguoiLXHoSo { get; init; }

    public long InsertedGiayTo { get; init; }

    public long UpdatedNguoiLX { get; init; }

    public long UpdatedNguoiLXHoSo { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    public string? BeforePlanJson { get; init; }

    public string? AfterPlanJson { get; init; }
}

public sealed class MotoSyncRunHistoryCreateDto
{
    public MotoSyncDirection Direction { get; init; }

    public MotoSyncMode SyncMode { get; init; }

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public bool ConfirmTextMatched { get; init; }

    public bool Executed { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public long InsertedKhoaHoc { get; init; }

    public long InsertedBaoCaoI { get; init; }

    public long InsertedNguoiLX { get; init; }

    public long InsertedNguoiLXGPLX { get; init; }

    public long InsertedNguoiLXHoSo { get; init; }

    public long InsertedGiayTo { get; init; }

    public long UpdatedNguoiLX { get; init; }

    public long UpdatedNguoiLXHoSo { get; init; }

    public long UpdatedRows { get; init; }

    public long DeletedRows { get; init; }

    public long DurationMs { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    public bool HasRemainingWork { get; init; }

    public string? BeforePlanJson { get; init; }

    public string? AfterPlanJson { get; init; }
}

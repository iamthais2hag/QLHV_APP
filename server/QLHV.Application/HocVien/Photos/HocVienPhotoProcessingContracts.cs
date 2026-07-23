using QLHV.Shared.Paging;

namespace QLHV.Application.HocVien.Photos;

public static class HocVienPhotoProcessingStatuses
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Succeeded = "SUCCEEDED";
    public const string ReviewRequired = "REVIEW_REQUIRED";
    public const string Failed = "FAILED";
    public const string Approved = "APPROVED";

    public static bool IsKnown(string? value) =>
        value is Pending or Processing or Succeeded or ReviewRequired or Failed or Approved;
}

public static class HocVienSourcePhotoStatuses
{
    public const string Found = "FOUND";
    public const string Missing = "MISSING";
    public const string InvalidPath = "INVALID_PATH";
}

public static class HocVienSourcePhotoPathKinds
{
    public const string Current = "CURRENT_PATH";
    public const string Legacy = "LEGACY_PATH";
    public const string Fallback = "FALLBACK_PATH";
}

public sealed record HocVienPhotoProcessingSource(
    string SourceProfileCode,
    string SourceMaDK,
    string? MaKhoa,
    string? SourceImagePath,
    bool SourceImagePathInvalid = false);

public sealed record HocVienSourcePhotoResolution(
    string Status,
    string PathKind,
    string? FullPath,
    string? RelativePath,
    bool UsedFallback)
{
    public bool Found => string.Equals(Status, HocVienSourcePhotoStatuses.Found, StringComparison.Ordinal);
}

public sealed record HocVienPhotoOutputResolution(
    bool IsSafe,
    string? FullPath,
    string? RelativePath,
    string? ErrorCode);

public sealed record BackgroundRemovalEngineReadiness(
    bool IsReady,
    string Status,
    string Engine,
    string? ModelSha256,
    string Message);

public sealed record BackgroundRemovalResult(
    byte[] Content,
    string ContentType,
    string Extension,
    double Confidence);

public sealed record HocVienPhotoProcessingWorkItem(
    string SourceProfileCode,
    string SourceMaDK,
    string? MaKhoa,
    string? SourceImagePath,
    bool SourceImagePathInvalid,
    string Actor);

public sealed record HocVienPhotoQueueBatchResult(
    int Requested,
    int Queued,
    int Skipped,
    int Failed);

public sealed record HocVienPhotoPlanDto(
    int Found,
    int Missing,
    int Pending,
    int ToReprocess,
    int ReviewRequired);

public sealed class HocVienPhotoSearchRequest
{
    public string? Status { get; init; }

    public string? SourceProfileCode { get; init; }

    public bool? ReviewRequired { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public HocVienPhotoSearchRequest Normalized()
    {
        var status = string.IsNullOrWhiteSpace(Status) ? null : Status.Trim().ToUpperInvariant();
        if (status is not null && !HocVienPhotoProcessingStatuses.IsKnown(status))
        {
            throw new InvalidOperationException("Trang thai xu ly anh khong hop le.");
        }

        var profile = string.IsNullOrWhiteSpace(SourceProfileCode)
            ? null
            : SourceProfileCode.Trim().ToUpperInvariant();
        if (profile is not null && profile is not ("CSDT_OTO" or "CSDT_MOTO"))
        {
            throw new InvalidOperationException("SourceProfileCode chi ho tro CSDT_OTO hoac CSDT_MOTO.");
        }

        return new HocVienPhotoSearchRequest
        {
            Status = status,
            SourceProfileCode = profile,
            ReviewRequired = ReviewRequired,
            Page = Math.Max(1, Page),
            PageSize = Math.Clamp(PageSize <= 0 ? 20 : PageSize, 1, 100),
        };
    }
}

public sealed class HocVienPhotoRecordDto
{
    public long Id { get; init; }
    public string SourceProfileCode { get; init; } = string.Empty;
    public string SourceMaDK { get; init; } = string.Empty;
    public string? StudentName { get; init; }
    public string? MaKhoaHoc { get; init; }
    public string? SourceImagePath { get; init; }
    public string? OutputImagePath { get; init; }
    public string? SourceFileHash { get; init; }
    public string SourcePathStatus { get; init; } = string.Empty;
    public string SourcePathKind { get; init; } = string.Empty;
    public string ProcessingStatus { get; init; } = string.Empty;
    public double? ProcessingConfidence { get; init; }
    public DateTime? ProcessedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ReviewRequired { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public string? SourcePreviewUrl =>
        SourcePathStatus == HocVienSourcePhotoStatuses.Found
            ? $"/api/dong-bo-v2/qlhv/photos/{Id}/source-preview"
            : null;
    public string? OutputPreviewUrl =>
        string.IsNullOrWhiteSpace(OutputImagePath)
            ? null
            : $"/api/dong-bo-v2/qlhv/photos/{Id}/output-preview";
}

public sealed class HocVienPhotoProcessingCountsDto
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Processing { get; init; }
    public int Succeeded { get; init; }
    public int ReviewRequired { get; init; }
    public int Failed { get; init; }
    public int Approved { get; init; }
}

public sealed class HocVienPhotoProcessingPageDto
{
    public IReadOnlyList<HocVienPhotoRecordDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
    public bool EngineReady { get; init; }
    public string? ReadinessMessage { get; init; }
    public HocVienPhotoProcessingCountsDto Counts { get; init; } = new();
}

public sealed record HocVienPhotoContent(byte[] Content, string ContentType);

public sealed record HocVienPhotoPrintSelection(
    bool CanPrint,
    string Status,
    string Message,
    HocVienPhotoContent? Image);

public interface IHocVienSourcePhotoPathResolver
{
    HocVienSourcePhotoResolution Resolve(
        string? sourceImagePath,
        string? maKhoa,
        string sourceMaDk,
        bool sourceImagePathInvalid = false);
}

public interface IHocVienPhotoOutputPathResolver
{
    HocVienPhotoOutputResolution Resolve(
        string sourceProfileCode,
        string? maKhoa,
        string sourceMaDk);

    HocVienPhotoOutputResolution ResolveStored(string? relativePath);
}

public interface IBackgroundRemovalEngine
{
    Task<BackgroundRemovalEngineReadiness> GetReadinessAsync(
        CancellationToken cancellationToken = default);

    Task<BackgroundRemovalResult> RemoveBackgroundAsync(
        ReadOnlyMemory<byte> sourceContent,
        string backgroundColor,
        CancellationToken cancellationToken = default);
}

public interface IHocVienPhotoProcessingQueue
{
    int PendingCount { get; }

    bool TryEnqueue(HocVienPhotoProcessingWorkItem item);

    ValueTask<HocVienPhotoProcessingWorkItem> DequeueAsync(
        CancellationToken cancellationToken = default);
}

public interface IHocVienPhotoProcessingRepository
{
    Task<HocVienPhotoRecordDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<HocVienPhotoRecordDto?> GetByIdentityAsync(
        string sourceProfileCode,
        string sourceMaDk,
        CancellationToken cancellationToken = default);

    Task<PagedResult<HocVienPhotoRecordDto>> SearchAsync(
        HocVienPhotoSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoProcessingCountsDto> GetCountsAsync(
        string? sourceProfileCode,
        CancellationToken cancellationToken = default);

    Task<long> UpsertPendingAsync(
        HocVienPhotoProcessingSource source,
        HocVienSourcePhotoResolution resolution,
        string actor,
        CancellationToken cancellationToken = default);

    Task MarkProcessingAsync(long id, string actor, CancellationToken cancellationToken = default);

    Task CompleteAsync(
        long id,
        string sourceFileHash,
        string outputImagePath,
        string status,
        double confidence,
        bool reviewRequired,
        string actor,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        long id,
        string safeError,
        string actor,
        CancellationToken cancellationToken = default);

    Task<bool> ApproveAsync(
        long id,
        long userId,
        string actor,
        CancellationToken cancellationToken = default);
}

public interface IHocVienPhotoProcessingService
{
    Task<BackgroundRemovalEngineReadiness> GetReadinessAsync(
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoPlanDto> BuildPlanAsync(
        IReadOnlyList<HocVienPhotoProcessingSource> sources,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoQueueBatchResult> QueueAfterSyncAsync(
        IReadOnlyList<HocVienPhotoProcessingSource> sources,
        string actor,
        CancellationToken cancellationToken = default);

    Task ProcessAsync(
        HocVienPhotoProcessingWorkItem item,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoProcessingPageDto> SearchAsync(
        HocVienPhotoSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoRecordDto?> ApproveAsync(
        long id,
        long userId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoRecordDto?> ReprocessAsync(
        long id,
        string actor,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoContent?> GetSourceImageAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoContent?> GetDerivedImageAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<HocVienPhotoPrintSelection> GetPrintSelectionAsync(
        string sourceProfileCode,
        string sourceMaDk,
        CancellationToken cancellationToken = default);
}

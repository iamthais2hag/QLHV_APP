using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.HocVien.Photos;
using QLHV.Application.Sync.Connections;
using QLHV.Shared.Paging;

namespace QLHV.Infrastructure.HocVien.Photos;

public sealed class HocVienPhotoProcessingRepository : IHocVienPhotoProcessingRepository
{
    private readonly IConnectionSettingsProvider _connections;

    public HocVienPhotoProcessingRepository(IConnectionSettingsProvider connections)
    {
        _connections = connections;
    }

    public async Task<HocVienPhotoRecordDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        return await connection.QuerySingleOrDefaultAsync<HocVienPhotoRecordDto>(new CommandDefinition(
            SelectColumns + " WHERE p.Id = @Id;",
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<HocVienPhotoRecordDto?> GetByIdentityAsync(
        string sourceProfileCode,
        string sourceMaDk,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        return await connection.QuerySingleOrDefaultAsync<HocVienPhotoRecordDto>(new CommandDefinition(
            SelectColumns + @"
 WHERE p.SourceProfileCode = @SourceProfileCode
   AND p.SourceMaDK = @SourceMaDK;",
            new
            {
                SourceProfileCode = NormalizeProfile(sourceProfileCode),
                SourceMaDK = NormalizeRequired(sourceMaDk, nameof(sourceMaDk)),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<PagedResult<HocVienPhotoRecordDto>> SearchAsync(
        HocVienPhotoSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var connectionString = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            @"
SELECT COUNT_BIG(1)
FROM dbo.App_HocVienPhoto
WHERE (@Status IS NULL OR ProcessingStatus = @Status)
  AND (@SourceProfileCode IS NULL OR SourceProfileCode = @SourceProfileCode)
  AND (@ReviewRequired IS NULL OR ReviewRequired = @ReviewRequired);

" + SelectColumns + @"
 WHERE (@Status IS NULL OR p.ProcessingStatus = @Status)
   AND (@SourceProfileCode IS NULL OR p.SourceProfileCode = @SourceProfileCode)
   AND (@ReviewRequired IS NULL OR p.ReviewRequired = @ReviewRequired)
 ORDER BY p.UpdatedAtUtc DESC, p.Id DESC
 OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            new
            {
                normalized.Status,
                normalized.SourceProfileCode,
                normalized.ReviewRequired,
                Offset = (normalized.Page - 1) * normalized.PageSize,
                normalized.PageSize,
            },
            cancellationToken: cancellationToken));
        var total = await grid.ReadSingleAsync<long>();
        var items = (await grid.ReadAsync<HocVienPhotoRecordDto>()).ToArray();
        return new PagedResult<HocVienPhotoRecordDto>
        {
            Items = items,
            TotalItems = total > int.MaxValue ? int.MaxValue : (int)total,
            Page = normalized.Page,
            PageSize = normalized.PageSize,
        };
    }

    public async Task<HocVienPhotoProcessingCountsDto> GetCountsAsync(
        string? sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        var profile = string.IsNullOrWhiteSpace(sourceProfileCode)
            ? null
            : NormalizeProfile(sourceProfileCode);
        var connectionString = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        return await connection.QuerySingleAsync<HocVienPhotoProcessingCountsDto>(
            new CommandDefinition(
                @"
SELECT
    COUNT(1) AS Total,
    COALESCE(SUM(CASE WHEN ProcessingStatus = N'PENDING' THEN 1 ELSE 0 END), 0) AS Pending,
    COALESCE(SUM(CASE WHEN ProcessingStatus = N'PROCESSING' THEN 1 ELSE 0 END), 0) AS Processing,
    COALESCE(SUM(CASE WHEN ProcessingStatus = N'SUCCEEDED' THEN 1 ELSE 0 END), 0) AS Succeeded,
    COALESCE(SUM(CASE WHEN ProcessingStatus = N'REVIEW_REQUIRED' THEN 1 ELSE 0 END), 0) AS ReviewRequired,
    COALESCE(SUM(CASE WHEN ProcessingStatus = N'FAILED' THEN 1 ELSE 0 END), 0) AS Failed,
    COALESCE(SUM(CASE WHEN ProcessingStatus = N'APPROVED' THEN 1 ELSE 0 END), 0) AS Approved
FROM dbo.App_HocVienPhoto
WHERE (@SourceProfileCode IS NULL OR SourceProfileCode = @SourceProfileCode);",
                new { SourceProfileCode = profile },
                cancellationToken: cancellationToken));
    }

    public async Task<long> UpsertPendingAsync(
        HocVienPhotoProcessingSource source,
        HocVienSourcePhotoResolution resolution,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @Changed TABLE (Id bigint NOT NULL);
    MERGE dbo.App_HocVienPhoto WITH (HOLDLOCK) AS target
    USING (SELECT @SourceProfileCode AS SourceProfileCode, @SourceMaDK AS SourceMaDK) AS source
       ON target.SourceProfileCode = source.SourceProfileCode
      AND target.SourceMaDK = source.SourceMaDK
    WHEN MATCHED THEN UPDATE SET
        MaKhoa = @MaKhoa,
        SourceImagePath = @SourceImagePath,
        SourcePathStatus = @SourcePathStatus,
        SourcePathKind = @SourcePathKind,
        ProcessingStatus = N'PENDING',
        ProcessingConfidence = NULL,
        ProcessedAtUtc = NULL,
        ErrorMessage = NULL,
        ReviewRequired = 0,
        ApprovedAtUtc = NULL,
        ApprovedByUserId = NULL,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT (
        SourceProfileCode, SourceMaDK, MaKhoa, SourceImagePath,
        SourcePathStatus, SourcePathKind, ProcessingStatus,
        ReviewRequired, CreatedAtUtc, UpdatedAtUtc)
    VALUES (
        @SourceProfileCode, @SourceMaDK, @MaKhoa, @SourceImagePath,
        @SourcePathStatus, @SourcePathKind, N'PENDING',
        0, SYSUTCDATETIME(), SYSUTCDATETIME())
    OUTPUT inserted.Id INTO @Changed;

    DECLARE @Id bigint = (SELECT TOP (1) Id FROM @Changed);
    INSERT dbo.App_HocVienPhotoProcessingHistory (
        PhotoId, SourceProfileCode, SourceMaDK, ProcessingStatus,
        ErrorMessage, Actor, CreatedAtUtc)
    VALUES (
        @Id, @SourceProfileCode, @SourceMaDK, N'PENDING',
        NULL, @Actor, SYSUTCDATETIME());

    UPDATE dbo.App_DataVersion WITH (UPDLOCK)
    SET PhotoVersion = PhotoVersion + 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE VersionId = 1;
    IF @@ROWCOUNT <> 1
        THROW 527320, N'dbo.App_DataVersion singleton row is missing.', 1;

    COMMIT TRANSACTION;
    SELECT @Id;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;",
            new
            {
                SourceProfileCode = NormalizeProfile(source.SourceProfileCode),
                SourceMaDK = NormalizeRequired(source.SourceMaDK, nameof(source.SourceMaDK)),
                MaKhoa = Trim(source.MaKhoa),
                SourceImagePath = resolution.RelativePath,
                SourcePathStatus = resolution.Status,
                SourcePathKind = resolution.PathKind,
                Actor = NormalizeActor(actor),
            },
            cancellationToken: cancellationToken));
    }

    public Task MarkProcessingAsync(
        long id,
        string actor,
        CancellationToken cancellationToken = default) =>
        UpdateStateAsync(
            id,
            HocVienPhotoProcessingStatuses.Processing,
            sourceFileHash: null,
            outputImagePath: null,
            confidence: null,
            reviewRequired: false,
            safeError: null,
            actor,
            cancellationToken);

    public Task CompleteAsync(
        long id,
        string sourceFileHash,
        string outputImagePath,
        string status,
        double confidence,
        bool reviewRequired,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (status is not (
                HocVienPhotoProcessingStatuses.Succeeded or
                HocVienPhotoProcessingStatuses.ReviewRequired))
        {
            throw new ArgumentException("Photo completion status is invalid.", nameof(status));
        }

        return UpdateStateAsync(
            id,
            status,
            NormalizeRequired(sourceFileHash, nameof(sourceFileHash)),
            NormalizeRequired(outputImagePath, nameof(outputImagePath)),
            Math.Clamp(confidence, 0d, 1d),
            reviewRequired,
            safeError: null,
            actor,
            cancellationToken);
    }

    public Task FailAsync(
        long id,
        string safeError,
        string actor,
        CancellationToken cancellationToken = default) =>
        UpdateStateAsync(
            id,
            HocVienPhotoProcessingStatuses.Failed,
            sourceFileHash: null,
            outputImagePath: null,
            confidence: null,
            reviewRequired: true,
            NormalizeSafeError(safeError),
            actor,
            cancellationToken);

    public async Task<bool> ApproveAsync(
        long id,
        long userId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0 || userId <= 0)
        {
            return false;
        }

        var connectionString = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var affected = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    UPDATE dbo.App_HocVienPhoto
    SET ProcessingStatus = N'APPROVED',
        ReviewRequired = 0,
        ApprovedAtUtc = SYSUTCDATETIME(),
        ApprovedByUserId = @UserId,
        ErrorMessage = NULL,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id
      AND OutputImagePath IS NOT NULL
      AND ProcessingStatus IN (N'SUCCEEDED', N'REVIEW_REQUIRED');
    DECLARE @Affected int = @@ROWCOUNT;
    IF @Affected > 0
    BEGIN
        INSERT dbo.App_HocVienPhotoProcessingHistory (
            PhotoId, SourceProfileCode, SourceMaDK, ProcessingStatus,
            ProcessingConfidence, SourceFileHash, OutputImagePath,
            ErrorMessage, Actor, CreatedAtUtc)
        SELECT
            Id, SourceProfileCode, SourceMaDK, N'APPROVED',
            ProcessingConfidence, SourceFileHash, OutputImagePath,
            NULL, @Actor, SYSUTCDATETIME()
        FROM dbo.App_HocVienPhoto
        WHERE Id = @Id;

        UPDATE dbo.App_DataVersion WITH (UPDLOCK)
        SET PhotoVersion = PhotoVersion + 1,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE VersionId = 1;
        IF @@ROWCOUNT <> 1
            THROW 527320, N'dbo.App_DataVersion singleton row is missing.', 1;
    END;
    COMMIT TRANSACTION;
    SELECT @Affected;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;",
            new { Id = id, UserId = userId, Actor = NormalizeActor(actor) },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    private async Task UpdateStateAsync(
        long id,
        string status,
        string? sourceFileHash,
        string? outputImagePath,
        double? confidence,
        bool reviewRequired,
        string? safeError,
        string actor,
        CancellationToken cancellationToken)
    {
        var connectionString = await ResolveTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    UPDATE dbo.App_HocVienPhoto
    SET ProcessingStatus = @Status,
        SourceFileHash = COALESCE(@SourceFileHash, SourceFileHash),
        OutputImagePath = COALESCE(@OutputImagePath, OutputImagePath),
        ProcessingConfidence = @Confidence,
        ProcessedAtUtc = CASE WHEN @Status IN (N'SUCCEEDED', N'REVIEW_REQUIRED', N'FAILED')
                              THEN SYSUTCDATETIME() ELSE ProcessedAtUtc END,
        ErrorMessage = @ErrorMessage,
        ReviewRequired = @ReviewRequired,
        ApprovedAtUtc = CASE WHEN @Status = N'APPROVED' THEN ApprovedAtUtc ELSE NULL END,
        ApprovedByUserId = CASE WHEN @Status = N'APPROVED' THEN ApprovedByUserId ELSE NULL END,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE Id = @Id;
    IF @@ROWCOUNT = 0
        THROW 51901, N'Photo metadata row was not found.', 1;

    INSERT dbo.App_HocVienPhotoProcessingHistory (
        PhotoId, SourceProfileCode, SourceMaDK, ProcessingStatus,
        ProcessingConfidence, SourceFileHash, OutputImagePath,
        ErrorMessage, Actor, CreatedAtUtc)
    SELECT
        Id, SourceProfileCode, SourceMaDK, @Status,
        @Confidence, COALESCE(@SourceFileHash, SourceFileHash),
        COALESCE(@OutputImagePath, OutputImagePath),
        @ErrorMessage, @Actor, SYSUTCDATETIME()
    FROM dbo.App_HocVienPhoto
    WHERE Id = @Id;

    UPDATE dbo.App_DataVersion WITH (UPDLOCK)
    SET PhotoVersion = PhotoVersion + 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE VersionId = 1;
    IF @@ROWCOUNT <> 1
        THROW 527320, N'dbo.App_DataVersion singleton row is missing.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;",
            new
            {
                Id = id,
                Status = status,
                SourceFileHash = sourceFileHash,
                OutputImagePath = outputImagePath,
                Confidence = confidence,
                ErrorMessage = safeError,
                ReviewRequired = reviewRequired,
                Actor = NormalizeActor(actor),
            },
            cancellationToken: cancellationToken));
    }

    private async Task<string> ResolveTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc.");
        }

        return target.ConnectionString;
    }

    private static string NormalizeProfile(string value)
    {
        var profile = NormalizeRequired(value, nameof(value)).ToUpperInvariant();
        return profile is "CSDT_OTO" or "CSDT_MOTO"
            ? profile
            : throw new InvalidOperationException(
                "Photo metadata chi ho tro partition CSDT_OTO hoac CSDT_MOTO.");
    }

    private static string NormalizeActor(string? actor)
    {
        var normalized = string.IsNullOrWhiteSpace(actor)
            ? "SYSTEM_PHOTO_WORKER"
            : actor.Trim();
        return normalized[..Math.Min(normalized.Length, 100)];
    }

    private static string NormalizeSafeError(string? value)
    {
        var safe = string.IsNullOrWhiteSpace(value) ? "PHOTO_PROCESSING_FAILED" : value.Trim();
        return safe[..Math.Min(safe.Length, 1000)];
    }

    private static string NormalizeRequired(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Required value is missing.", name)
            : value.Trim();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string SelectColumns = @"
SELECT
    p.Id,
    p.SourceProfileCode,
    p.SourceMaDK,
    hv.HoTen AS StudentName,
    p.MaKhoa AS MaKhoaHoc,
    p.SourceImagePath,
    p.OutputImagePath,
    p.SourceFileHash,
    p.SourcePathStatus,
    p.SourcePathKind,
    p.ProcessingStatus,
    CONVERT(float, p.ProcessingConfidence) AS ProcessingConfidence,
    p.ProcessedAtUtc,
    p.ErrorMessage,
    p.ReviewRequired,
    p.ApprovedAtUtc,
    u.DisplayName AS ApprovedBy,
    p.CreatedAtUtc,
    p.UpdatedAtUtc
FROM dbo.App_HocVienPhoto AS p
LEFT JOIN dbo.App_HocVien AS hv
  ON hv.SourceProfileCode = p.SourceProfileCode
 AND hv.SourceMaDK = p.SourceMaDK
 AND hv.IsDeleted = 0
LEFT JOIN dbo.App_User AS u
  ON u.UserId = p.ApprovedByUserId";
}

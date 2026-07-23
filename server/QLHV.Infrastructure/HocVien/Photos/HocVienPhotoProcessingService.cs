using System.Security.Cryptography;
using ImageMagick;
using Microsoft.Extensions.Options;
using QLHV.Application.HocVien.Photos;

namespace QLHV.Infrastructure.HocVien.Photos;

public sealed class HocVienPhotoProcessingService : IHocVienPhotoProcessingService
{
    private readonly HocVienPhotoProcessingOptions _options;
    private readonly IHocVienSourcePhotoPathResolver _sourceResolver;
    private readonly IHocVienPhotoOutputPathResolver _outputResolver;
    private readonly IBackgroundRemovalEngine _engine;
    private readonly IHocVienPhotoProcessingQueue _queue;
    private readonly IHocVienPhotoProcessingRepository _repository;

    public HocVienPhotoProcessingService(
        IOptions<HocVienPhotoProcessingOptions> options,
        IHocVienSourcePhotoPathResolver sourceResolver,
        IHocVienPhotoOutputPathResolver outputResolver,
        IBackgroundRemovalEngine engine,
        IHocVienPhotoProcessingQueue queue,
        IHocVienPhotoProcessingRepository repository)
    {
        _options = options.Value;
        _sourceResolver = sourceResolver;
        _outputResolver = outputResolver;
        _engine = engine;
        _queue = queue;
        _repository = repository;
    }

    public Task<BackgroundRemovalEngineReadiness> GetReadinessAsync(
        CancellationToken cancellationToken = default)
        => !_options.Enabled
            ? Task.FromResult(new BackgroundRemovalEngineReadiness(
                false,
                "DISABLED",
                "onnxruntime",
                null,
                "Photo processing is disabled."))
            : _engine.GetReadinessAsync(cancellationToken);

    public async Task<HocVienPhotoPlanDto> BuildPlanAsync(
        IReadOnlyList<HocVienPhotoProcessingSource> sources,
        CancellationToken cancellationToken = default)
    {
        var found = 0;
        var missing = 0;
        var pending = 0;
        var toReprocess = 0;
        var reviewRequired = 0;

        foreach (var source in DistinctSources(sources))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = Resolve(source);
            if (!resolution.Found || resolution.FullPath is null)
            {
                missing++;
                continue;
            }

            found++;
            var current = await _repository.GetByIdentityAsync(
                source.SourceProfileCode,
                source.SourceMaDK,
                cancellationToken);
            if (current is null)
            {
                toReprocess++;
                continue;
            }

            if (current.ProcessingStatus is
                HocVienPhotoProcessingStatuses.Pending or
                HocVienPhotoProcessingStatuses.Processing)
            {
                pending++;
            }

            if (current.ReviewRequired ||
                current.ProcessingStatus == HocVienPhotoProcessingStatuses.ReviewRequired)
            {
                reviewRequired++;
            }

            if (current.ProcessingStatus == HocVienPhotoProcessingStatuses.Failed ||
                !OutputExists(current) ||
                !await HasCurrentSourceHashAsync(current, resolution.FullPath, cancellationToken))
            {
                toReprocess++;
            }
        }

        return new HocVienPhotoPlanDto(
            found,
            missing,
            pending,
            toReprocess,
            reviewRequired);
    }

    public async Task<HocVienPhotoQueueBatchResult> QueueAfterSyncAsync(
        IReadOnlyList<HocVienPhotoProcessingSource> sources,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var distinct = DistinctSources(sources);
        if (!_options.Enabled || !_options.AutoProcessAfterSync)
        {
            return new HocVienPhotoQueueBatchResult(
                distinct.Count,
                0,
                distinct.Count,
                0);
        }

        var readiness = await GetReadinessAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            return new HocVienPhotoQueueBatchResult(
                distinct.Count,
                0,
                distinct.Count,
                0);
        }

        var queued = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var source in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = Resolve(source);
            if (!resolution.Found || resolution.FullPath is null)
            {
                var id = await _repository.UpsertPendingAsync(
                    source,
                    resolution,
                    actor,
                    cancellationToken);
                await _repository.FailAsync(
                    id,
                    resolution.Status == HocVienSourcePhotoStatuses.InvalidPath
                        ? "SOURCE_PATH_INVALID"
                        : "SOURCE_PHOTO_MISSING",
                    actor,
                    cancellationToken);
                failed++;
                continue;
            }

            var current = await _repository.GetByIdentityAsync(
                source.SourceProfileCode,
                source.SourceMaDK,
                cancellationToken);
            if (current is not null &&
                IsCompleted(current) &&
                OutputExists(current) &&
                await HasCurrentSourceHashAsync(current, resolution.FullPath, cancellationToken))
            {
                skipped++;
                continue;
            }

            await _repository.UpsertPendingAsync(
                source,
                resolution,
                actor,
                cancellationToken);

            var workItem = ToWorkItem(source, actor);
            if (_queue.TryEnqueue(workItem))
            {
                queued++;
                continue;
            }

            // PENDING metadata is the durable backlog. The worker reconciles
            // it after draining the bounded RAM queue and after a restart.
            queued++;
        }

        return new HocVienPhotoQueueBatchResult(
            distinct.Count,
            queued,
            skipped,
            failed);
    }

    public async Task ProcessAsync(
        HocVienPhotoProcessingWorkItem item,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var readiness = await _engine.GetReadinessAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            return;
        }

        var source = new HocVienPhotoProcessingSource(
            item.SourceProfileCode,
            item.SourceMaDK,
            item.MaKhoa,
            item.SourceImagePath,
            item.SourceImagePathInvalid);
        var resolution = Resolve(source);
        var current = await _repository.GetByIdentityAsync(
            source.SourceProfileCode,
            source.SourceMaDK,
            cancellationToken);
        if (resolution.Found &&
            resolution.FullPath is not null &&
            current is not null &&
            IsCompleted(current) &&
            OutputExists(current) &&
            await HasCurrentSourceHashAsync(
                current,
                resolution.FullPath,
                cancellationToken))
        {
            return;
        }

        var id = current?.Id ?? await _repository.UpsertPendingAsync(
            source,
            resolution,
            item.Actor,
            cancellationToken);

        if (!resolution.Found || resolution.FullPath is null)
        {
            await _repository.FailAsync(
                id,
                resolution.Status == HocVienSourcePhotoStatuses.InvalidPath
                    ? "SOURCE_PATH_INVALID"
                    : "SOURCE_PHOTO_MISSING",
                item.Actor,
                cancellationToken);
            return;
        }

        var output = _outputResolver.Resolve(
            source.SourceProfileCode,
            source.MaKhoa,
            source.SourceMaDK);
        if (!output.IsSafe || output.FullPath is null || output.RelativePath is null)
        {
            await _repository.FailAsync(
                id,
                $"OUTPUT_PATH_INVALID:{SafeCode(output.ErrorCode)}",
                item.Actor,
                cancellationToken);
            return;
        }

        await _repository.MarkProcessingAsync(id, item.Actor, cancellationToken);
        try
        {
            var sourceBytes = await ReadBoundedAsync(resolution.FullPath, cancellationToken);
            var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes))
                .ToLowerInvariant();
            var result = await _engine.RemoveBackgroundAsync(
                sourceBytes,
                _options.BackgroundColor,
                cancellationToken);
            if (!string.Equals(result.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.Extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                result.Content.Length == 0)
            {
                throw new InvalidDataException("PHOTO_ENGINE_OUTPUT_INVALID");
            }

            await WriteDerivedAtomicallyAsync(output, result.Content, cancellationToken);
            var threshold = Math.Clamp(_options.MinimumAutoApprovalConfidence, 0d, 1d);
            var review = result.Confidence < threshold;
            await _repository.CompleteAsync(
                id,
                sourceHash,
                output.RelativePath,
                review
                    ? HocVienPhotoProcessingStatuses.ReviewRequired
                    : HocVienPhotoProcessingStatuses.Succeeded,
                result.Confidence,
                review,
                item.Actor,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                InvalidOperationException or
                MagickException)
        {
            await _repository.FailAsync(
                id,
                SafeFailureCode(exception),
                item.Actor,
                cancellationToken);
        }
    }

    public async Task<HocVienPhotoProcessingPageDto> SearchAsync(
        HocVienPhotoSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalized();
        var page = await _repository.SearchAsync(normalized, cancellationToken);
        var counts = await _repository.GetCountsAsync(
            normalized.SourceProfileCode,
            cancellationToken);
        var readiness = await GetReadinessAsync(cancellationToken);
        return new HocVienPhotoProcessingPageDto
        {
            Items = page.Items,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalItems = page.TotalItems,
            TotalPages = page.TotalPages,
            EngineReady = readiness.IsReady,
            ReadinessMessage = readiness.Message,
            Counts = counts,
        };
    }

    public async Task<HocVienPhotoRecordDto?> ApproveAsync(
        long id,
        long userId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var readiness = await GetReadinessAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException(
                $"PHOTO_ENGINE_NOT_READY:{SafeCode(readiness.Status)}");
        }

        var current = await _repository.GetByIdAsync(id, cancellationToken);
        if (current is null ||
            current.ProcessingStatus is not (
                HocVienPhotoProcessingStatuses.Succeeded or
                HocVienPhotoProcessingStatuses.ReviewRequired or
                HocVienPhotoProcessingStatuses.Approved) ||
            !OutputExists(current))
        {
            return null;
        }

        if (current.ProcessingStatus == HocVienPhotoProcessingStatuses.Approved)
        {
            return current;
        }

        if (!await _repository.ApproveAsync(id, userId, actor, cancellationToken))
        {
            return null;
        }

        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<HocVienPhotoRecordDto?> ReprocessAsync(
        long id,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var current = await _repository.GetByIdAsync(id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var readiness = await GetReadinessAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException(
                $"PHOTO_ENGINE_NOT_READY:{SafeCode(readiness.Status)}");
        }

        var source = new HocVienPhotoProcessingSource(
            current.SourceProfileCode,
            current.SourceMaDK,
            current.MaKhoaHoc,
            current.SourceImagePath,
            current.SourcePathStatus == HocVienSourcePhotoStatuses.InvalidPath);
        var resolution = Resolve(source);
        await _repository.UpsertPendingAsync(source, resolution, actor, cancellationToken);
        if (!resolution.Found)
        {
            await _repository.FailAsync(
                id,
                resolution.Status == HocVienSourcePhotoStatuses.InvalidPath
                    ? "SOURCE_PATH_INVALID"
                    : "SOURCE_PHOTO_MISSING",
                actor,
                cancellationToken);
            return await _repository.GetByIdAsync(id, cancellationToken);
        }

        if (!_queue.TryEnqueue(ToWorkItem(source, actor)))
        {
            // Keep the row PENDING for durable reconciliation rather than
            // recording a false processing failure when the RAM queue is full.
            return await _repository.GetByIdAsync(id, cancellationToken);
        }

        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<HocVienPhotoContent?> GetSourceImageAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var current = await _repository.GetByIdAsync(id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var resolution = _sourceResolver.Resolve(
            current.SourceImagePath,
            current.MaKhoaHoc,
            current.SourceMaDK,
            current.SourcePathStatus == HocVienSourcePhotoStatuses.InvalidPath);
        if (!resolution.Found || resolution.FullPath is null)
        {
            return null;
        }

        try
        {
            var sourceBytes = await ReadBoundedAsync(resolution.FullPath, cancellationToken);
            using var image = new MagickImage(sourceBytes);
            image.AutoOrient();
            image.Strip();
            image.Format = MagickFormat.Jpeg;
            image.Quality = 90;
            return new HocVienPhotoContent(image.ToByteArray(), "image/jpeg");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or MagickException)
        {
            return null;
        }
    }

    public async Task<HocVienPhotoContent?> GetDerivedImageAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var current = await _repository.GetByIdAsync(id, cancellationToken);
        return current is null
            ? null
            : await ReadDerivedAsync(current, cancellationToken);
    }

    public async Task<HocVienPhotoPrintSelection> GetPrintSelectionAsync(
        string sourceProfileCode,
        string sourceMaDk,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new HocVienPhotoPrintSelection(
                false,
                "NO_METADATA",
                "Xu ly anh dang tat; su dung anh goc neu co.",
                null);
        }

        var current = await _repository.GetByIdentityAsync(
            sourceProfileCode,
            sourceMaDk,
            cancellationToken);
        if (current is null)
        {
            return new HocVienPhotoPrintSelection(
                false,
                "NO_METADATA",
                "Chua co metadata xu ly anh.",
                null);
        }

        var allowed = current.ProcessingStatus == HocVienPhotoProcessingStatuses.Approved ||
                      current.ProcessingStatus == HocVienPhotoProcessingStatuses.Succeeded &&
                      !current.ReviewRequired &&
                      current.ProcessingConfidence >=
                      Math.Clamp(_options.MinimumAutoApprovalConfidence, 0d, 1d);
        if (!allowed)
        {
            return new HocVienPhotoPrintSelection(
                false,
                current.ProcessingStatus,
                "Anh dan xuat chua dat dieu kien in.",
                null);
        }

        var image = await ReadDerivedAsync(current, cancellationToken);
        return image is null
            ? new HocVienPhotoPrintSelection(
                false,
                "DERIVED_MISSING",
                "Khong tim thay anh dan xuat da duyet.",
                null)
            : new HocVienPhotoPrintSelection(
                true,
                current.ProcessingStatus,
                "Anh dan xuat du dieu kien in.",
                image);
    }

    private HocVienSourcePhotoResolution Resolve(HocVienPhotoProcessingSource source) =>
        _sourceResolver.Resolve(
            source.SourceImagePath,
            source.MaKhoa,
            source.SourceMaDK,
            source.SourceImagePathInvalid);

    private bool OutputExists(HocVienPhotoRecordDto current)
    {
        var output = _outputResolver.ResolveStored(current.OutputImagePath);
        return output.IsSafe &&
               output.FullPath is not null &&
               File.Exists(output.FullPath);
    }

    private async Task<HocVienPhotoContent?> ReadDerivedAsync(
        HocVienPhotoRecordDto current,
        CancellationToken cancellationToken)
    {
        var output = _outputResolver.ResolveStored(current.OutputImagePath);
        if (!output.IsSafe || output.FullPath is null || !File.Exists(output.FullPath))
        {
            return null;
        }

        try
        {
            return new HocVienPhotoContent(
                await ReadBoundedAsync(output.FullPath, cancellationToken),
                "image/jpeg");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private async Task<bool> HasCurrentSourceHashAsync(
        HocVienPhotoRecordDto current,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(current.SourceFileHash))
        {
            return false;
        }

        try
        {
            var bytes = await ReadBoundedAsync(sourcePath, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return string.Equals(hash, current.SourceFileHash, StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private async Task<byte[]> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        var maximum = Math.Clamp(_options.MaxSourceBytes, 1L, 100L * 1024 * 1024);
        if (!info.Exists || info.Length <= 0 || info.Length > maximum)
        {
            throw new InvalidDataException("PHOTO_SOURCE_SIZE_INVALID");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private async Task WriteDerivedAtomicallyAsync(
        HocVienPhotoOutputResolution output,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var fullPath = output.FullPath!;
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("OUTPUT_DIRECTORY_INVALID");
        Directory.CreateDirectory(directory);

        var rechecked = _outputResolver.ResolveStored(output.RelativePath);
        if (!rechecked.IsSafe ||
            !string.Equals(rechecked.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("OUTPUT_PATH_RECHECK_FAILED");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsCompleted(HocVienPhotoRecordDto current) =>
        current.ProcessingStatus is
            HocVienPhotoProcessingStatuses.Succeeded or
            HocVienPhotoProcessingStatuses.ReviewRequired or
            HocVienPhotoProcessingStatuses.Approved;

    private static HocVienPhotoProcessingWorkItem ToWorkItem(
        HocVienPhotoProcessingSource source,
        string actor) =>
        new(
            source.SourceProfileCode,
            source.SourceMaDK,
            source.MaKhoa,
            source.SourceImagePath,
            source.SourceImagePathInvalid,
            actor);

    private static IReadOnlyList<HocVienPhotoProcessingSource> DistinctSources(
        IReadOnlyList<HocVienPhotoProcessingSource> sources) =>
        sources
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.SourceProfileCode) &&
                !string.IsNullOrWhiteSpace(source.SourceMaDK))
            .GroupBy(
                source => (
                    source.SourceProfileCode.Trim().ToUpperInvariant(),
                    source.SourceMaDK.Trim()),
                source => source)
            .Select(group => group.Last())
            .ToArray();

    private static string SafeFailureCode(Exception exception) =>
        exception switch
        {
            UnauthorizedAccessException => "PHOTO_FILE_ACCESS_DENIED",
            IOException => "PHOTO_FILE_IO_FAILED",
            MagickException => "PHOTO_DECODE_FAILED",
            InvalidDataException invalidData when
                invalidData.Message.StartsWith("PHOTO_", StringComparison.Ordinal) =>
                SafeCode(invalidData.Message),
            InvalidDataException => "PHOTO_DATA_INVALID",
            InvalidOperationException => "PHOTO_ENGINE_FAILED",
            _ => "PHOTO_PROCESSING_FAILED",
        };

    private static string SafeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var safe = new string(value
            .Trim()
            .ToUpperInvariant()
            .Where(character =>
                character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or ':' or '-')
            .ToArray());
        return safe.Length == 0 ? "UNKNOWN" : safe[..Math.Min(safe.Length, 100)];
    }
}

using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QLHV.Api.Controllers;
using QLHV.Application.Auth;
using QLHV.Application.HocVien.Photos;
using QLHV.Infrastructure.HocVien.Photos;
using QLHV.Infrastructure.Sync;
using QLHV.Shared.Paging;

namespace QLHV.Tests.HocVien;

public sealed class HocVienPhotoProcessingTests
{
    [Fact]
    public void Source_resolver_supports_current_legacy_and_fallback_paths()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        paths.WriteSource("legacy", "old.jp2");
        var resolver = paths.CreateSourceResolver();

        var current = resolver.Resolve(
            @"IM_GPLX\KHOA-01\66029-001.jp2",
            "KHOA-01",
            "66029-001");
        var legacy = resolver.Resolve(
            @"legacy\old.jp2",
            "KHOA-01",
            "66029-001");
        var fallback = resolver.Resolve(
            null,
            "KHOA-01",
            "66029-001");

        Assert.Equal(HocVienSourcePhotoStatuses.Found, current.Status);
        Assert.Equal(HocVienSourcePhotoPathKinds.Current, current.PathKind);
        Assert.Equal(HocVienSourcePhotoStatuses.Found, legacy.Status);
        Assert.Equal(HocVienSourcePhotoPathKinds.Legacy, legacy.PathKind);
        Assert.Equal(HocVienSourcePhotoStatuses.Found, fallback.Status);
        Assert.Equal(HocVienSourcePhotoPathKinds.Fallback, fallback.PathKind);
    }

    [Theory]
    [InlineData(@"..\outside.jp2")]
    [InlineData(@"KHOA-01\..\outside.jp2")]
    [InlineData(@"KHOA-01\image.jpg")]
    public void Source_resolver_rejects_traversal_and_non_jp2_paths(string value)
    {
        using var paths = new PhotoTestPaths();
        var result = paths.CreateSourceResolver().Resolve(
            value,
            "KHOA-01",
            "66029-001");

        Assert.Equal(HocVienSourcePhotoStatuses.InvalidPath, result.Status);
        Assert.Null(result.FullPath);
    }

    [Fact]
    public void Output_resolver_partitions_oto_and_moto_and_rejects_overlap()
    {
        using var paths = new PhotoTestPaths();
        var resolver = paths.CreateOutputResolver();

        var oto = resolver.Resolve("CSDT_OTO", "KHOA-01", "66029-001");
        var moto = resolver.Resolve("CSDT_MOTO", "KHOA-01", "66030-001");

        Assert.True(oto.IsSafe);
        Assert.True(moto.IsSafe);
        Assert.StartsWith("CSDT_OTO", oto.RelativePath, StringComparison.Ordinal);
        Assert.StartsWith("CSDT_MOTO", moto.RelativePath, StringComparison.Ordinal);

        var overlap = new SecureHocVienPhotoOutputPathResolver(Options.Create(
            paths.CopyOptions(outputRoot: paths.SourceRoot)));
        Assert.False(overlap.Resolve("CSDT_OTO", "KHOA-01", "66029-001").IsSafe);
    }

    [Fact]
    public async Task Engine_without_local_model_reports_not_ready_without_downloading()
    {
        using var paths = new PhotoTestPaths();
        using var engine = new OnnxBackgroundRemovalEngine(Options.Create(
            paths.CopyOptions(
                modelPath: Path.Combine(paths.Root, "missing.onnx"),
                modelLicense: "Apache-2.0",
                modelSha256: new string('a', 64))));

        var readiness = await engine.GetReadinessAsync();

        Assert.False(readiness.IsReady);
        Assert.Equal("MODEL_MISSING", readiness.Status);
    }

    [Fact]
    public async Task Engine_rejects_unaccepted_model_license_before_loading_model()
    {
        using var paths = new PhotoTestPaths();
        using var engine = new OnnxBackgroundRemovalEngine(Options.Create(
            paths.CopyOptions(
                modelPath: Path.Combine(paths.Root, "missing.onnx"),
                modelLicense: "Proprietary-Unreviewed",
                modelSha256: new string('a', 64))));

        var readiness = await engine.GetReadinessAsync();

        Assert.False(readiness.IsReady);
        Assert.Equal("MODEL_LICENSE_NOT_ACCEPTED", readiness.Status);
    }

    [Fact]
    public async Task Engine_requires_a_checksum_verified_license_manifest()
    {
        using var paths = new PhotoTestPaths();
        var modelPath = Path.Combine(paths.Root, "test.onnx");
        await File.WriteAllBytesAsync(modelPath, [1, 2, 3, 4]);
        var modelHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(modelPath))).ToLowerInvariant();
        var manifestPath = Path.Combine(paths.Root, "model-license.json");
        await File.WriteAllTextAsync(
            manifestPath,
            $$"""
            {
              "schemaVersion": 1,
              "licenseId": "Apache-2.0",
              "modelSha256": "{{modelHash}}",
              "modelSource": "https://example.invalid/reviewed-model",
              "reviewedBy": "unit-test",
              "reviewedAtUtc": "2026-07-01T00:00:00Z"
            }
            """);
        using var engine = new OnnxBackgroundRemovalEngine(Options.Create(
            paths.CopyOptions(
                modelPath: modelPath,
                modelLicense: "Apache-2.0",
                modelSha256: modelHash,
                modelLicenseManifestPath: manifestPath,
                modelLicenseManifestSha256: new string('0', 64))));

        var readiness = await engine.GetReadinessAsync();

        Assert.False(readiness.IsReady);
        Assert.Equal("LICENSE_MANIFEST_CHECKSUM_MISMATCH", readiness.Status);
    }

    [Fact]
    public async Task Engine_validates_manifest_license_and_model_binding_before_onnx_load()
    {
        using var paths = new PhotoTestPaths();
        var modelPath = Path.Combine(paths.Root, "test.onnx");
        await File.WriteAllBytesAsync(modelPath, [1, 2, 3, 4]);
        var modelHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(modelPath))).ToLowerInvariant();
        var manifestPath = Path.Combine(paths.Root, "model-license.json");
        await File.WriteAllTextAsync(
            manifestPath,
            $$"""
            {
              "schemaVersion": 1,
              "licenseId": "MIT",
              "modelSha256": "{{modelHash}}",
              "modelSource": "https://example.invalid/reviewed-model",
              "reviewedBy": "unit-test",
              "reviewedAtUtc": "2026-07-01T00:00:00Z"
            }
            """);
        var manifestHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(manifestPath))).ToLowerInvariant();
        using var engine = new OnnxBackgroundRemovalEngine(Options.Create(
            paths.CopyOptions(
                modelPath: modelPath,
                modelLicense: "Apache-2.0",
                modelSha256: modelHash,
                modelLicenseManifestPath: manifestPath,
                modelLicenseManifestSha256: manifestHash)));

        var readiness = await engine.GetReadinessAsync();

        Assert.False(readiness.IsReady);
        Assert.Equal("LICENSE_MANIFEST_LICENSE_MISMATCH", readiness.Status);
    }

    [Fact]
    public async Task Engine_accepts_valid_review_manifest_before_attempting_onnx_load()
    {
        using var paths = new PhotoTestPaths();
        var modelPath = Path.Combine(paths.Root, "test.onnx");
        await File.WriteAllBytesAsync(modelPath, [1, 2, 3, 4]);
        var modelHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(modelPath))).ToLowerInvariant();
        var manifestPath = Path.Combine(paths.Root, "model-license.json");
        await File.WriteAllTextAsync(
            manifestPath,
            $$"""
            {
              "schemaVersion": 1,
              "licenseId": "Apache-2.0",
              "modelSha256": "{{modelHash}}",
              "modelSource": "https://example.invalid/reviewed-model",
              "reviewedBy": "unit-test",
              "reviewedAtUtc": "2026-07-01T00:00:00Z"
            }
            """);
        var manifestHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(manifestPath))).ToLowerInvariant();
        using var engine = new OnnxBackgroundRemovalEngine(Options.Create(
            paths.CopyOptions(
                modelPath: modelPath,
                modelLicense: "Apache-2.0",
                modelSha256: modelHash,
                modelLicenseManifestPath: manifestPath,
                modelLicenseManifestSha256: manifestHash)));

        var readiness = await engine.GetReadinessAsync();

        Assert.False(readiness.IsReady);
        Assert.Equal("MODEL_LOAD_FAILED", readiness.Status);
    }

    [Fact]
    public void Photo_processing_defaults_are_fail_closed()
    {
        var defaults = new HocVienPhotoProcessingOptions();

        Assert.False(defaults.Enabled);
        Assert.False(defaults.AutoProcessAfterSync);
    }

    [Fact]
    public async Task Disabled_queue_skips_without_engine_or_repository_access()
    {
        using var paths = new PhotoTestPaths();
        paths.Options.Enabled = false;
        paths.Options.AutoProcessAfterSync = false;
        var repository = new FakeRepository();
        var engine = new FakeEngine(ready: true);
        var queue = new HocVienPhotoProcessingQueue(Options.Create(paths.Options));
        var service = paths.CreateService(repository, engine, queue);

        var result = await service.QueueAfterSyncAsync(
            [new("CSDT_OTO", "66029-001", "KHOA-01", null)],
            "test");

        Assert.Equal(1, result.Requested);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Queued);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, engine.ReadinessCalls);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Disabled_service_reports_disabled_without_probing_engine()
    {
        using var paths = new PhotoTestPaths();
        paths.Options.Enabled = false;
        var engine = new FakeEngine(ready: true);
        var service = paths.CreateService(
            new FakeRepository(),
            engine,
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var readiness = await service.GetReadinessAsync();

        Assert.False(readiness.IsReady);
        Assert.Equal("DISABLED", readiness.Status);
        Assert.Equal(0, engine.ReadinessCalls);
    }

    [Fact]
    public async Task Disabled_approval_fails_closed_before_metadata_version_or_history_access()
    {
        using var paths = new PhotoTestPaths();
        paths.Options.Enabled = false;
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.ReviewRequired,
                output: "unused.jpg",
                confidence: 0.50,
                reviewRequired: true),
        };
        var engine = new FakeEngine(ready: true);
        var service = paths.CreateService(
            repository,
            engine,
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(1, 42, "admin"));

        Assert.Contains("PHOTO_ENGINE_NOT_READY:DISABLED", exception.Message);
        Assert.Equal(0, engine.ReadinessCalls);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(0, repository.ApproveCalls);
    }

    [Fact]
    public async Task Not_ready_approval_fails_closed_before_metadata_version_or_history_access()
    {
        using var paths = new PhotoTestPaths();
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.ReviewRequired,
                output: "unused.jpg",
                confidence: 0.50,
                reviewRequired: true),
        };
        var engine = new FakeEngine(ready: false);
        var service = paths.CreateService(
            repository,
            engine,
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(1, 42, "admin"));

        Assert.Contains("PHOTO_ENGINE_NOT_READY:MODEL_MISSING", exception.Message);
        Assert.Equal(1, engine.ReadinessCalls);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(0, repository.ApproveCalls);
    }

    [Fact]
    public async Task Disabled_print_selection_preserves_original_photo_fallback()
    {
        using var paths = new PhotoTestPaths();
        paths.Options.Enabled = false;
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.Failed,
                output: null,
                confidence: null,
                reviewRequired: true),
        };
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: false),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var selection = await service.GetPrintSelectionAsync("CSDT_OTO", "66029-001");

        Assert.False(selection.CanPrint);
        Assert.Equal("NO_METADATA", selection.Status);
        Assert.Null(selection.Image);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public async Task Queue_after_sync_skips_without_metadata_writes_when_engine_is_not_ready()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var repository = new FakeRepository();
        var engine = new FakeEngine(ready: false);
        var service = paths.CreateService(
            repository,
            engine,
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var result = await service.QueueAfterSyncAsync(
            [new("CSDT_OTO", "66029-001", "KHOA-01", null)],
            "test");

        Assert.Equal(0, result.Queued);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, engine.ReadinessCalls);
        Assert.Null(repository.Current);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public async Task Queued_item_does_not_write_fake_failure_when_engine_becomes_not_ready()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var repository = new FakeRepository();
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: false),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        await service.ProcessAsync(new HocVienPhotoProcessingWorkItem(
            "CSDT_OTO",
            "66029-001",
            "KHOA-01",
            null,
            false,
            "test"));

        Assert.Null(repository.Current);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public async Task Auto_processing_off_skips_without_engine_or_repository_access()
    {
        using var paths = new PhotoTestPaths();
        paths.Options.AutoProcessAfterSync = false;
        var repository = new FakeRepository();
        var engine = new FakeEngine(ready: true);
        var service = paths.CreateService(
            repository,
            engine,
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var result = await service.QueueAfterSyncAsync(
            [new("CSDT_OTO", "66029-001", "KHOA-01", null)],
            "test");

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, engine.ReadinessCalls);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public async Task Low_confidence_output_is_partitioned_and_requires_review()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var repository = new FakeRepository();
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true, confidence: 0.20),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        await service.ProcessAsync(new HocVienPhotoProcessingWorkItem(
            "CSDT_OTO",
            "66029-001",
            "KHOA-01",
            null,
            false,
            "test"));

        Assert.NotNull(repository.Current);
        Assert.Equal(
            HocVienPhotoProcessingStatuses.ReviewRequired,
            repository.Current!.ProcessingStatus);
        Assert.True(repository.Current.ReviewRequired);
        Assert.StartsWith("CSDT_OTO", repository.Current.OutputImagePath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            paths.OutputRoot,
            repository.Current.OutputImagePath!.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task Unchanged_source_with_existing_output_is_skipped()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var sourcePath = Path.Combine(paths.SourceRoot, "KHOA-01", "66029-001.jp2");
        var sourceHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))).ToLowerInvariant();
        var output = paths.CreateOutputResolver()
            .Resolve("CSDT_OTO", "KHOA-01", "66029-001");
        Directory.CreateDirectory(Path.GetDirectoryName(output.FullPath!)!);
        await File.WriteAllBytesAsync(output.FullPath!, [1, 2, 3]);
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.Succeeded,
                output.RelativePath,
                confidence: 0.99,
                reviewRequired: false,
                sourceFileHash: sourceHash),
        };
        var queue = new HocVienPhotoProcessingQueue(Options.Create(paths.Options));
        var service = paths.CreateService(repository, new FakeEngine(ready: true), queue);

        var result = await service.QueueAfterSyncAsync(
            [new("CSDT_OTO", "66029-001", "KHOA-01", null)],
            "test");

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Queued);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Duplicate_recovered_work_item_is_idempotent_after_completion()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var sourcePath = Path.Combine(paths.SourceRoot, "KHOA-01", "66029-001.jp2");
        var sourceHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))).ToLowerInvariant();
        var output = paths.CreateOutputResolver()
            .Resolve("CSDT_OTO", "KHOA-01", "66029-001");
        Directory.CreateDirectory(Path.GetDirectoryName(output.FullPath!)!);
        await File.WriteAllBytesAsync(output.FullPath!, [1, 2, 3]);
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.Succeeded,
                output.RelativePath,
                confidence: 0.99,
                reviewRequired: false,
                sourceFileHash: sourceHash),
        };
        var engine = new FakeEngine(ready: true);
        var service = paths.CreateService(
            repository,
            engine,
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        await service.ProcessAsync(new HocVienPhotoProcessingWorkItem(
            "CSDT_OTO",
            "66029-001",
            "KHOA-01",
            null,
            false,
            "SYSTEM_PHOTO_RECOVERY"));

        Assert.Equal(0, engine.ProcessingCalls);
        Assert.Equal(0, repository.WriteCalls);
        Assert.Equal(
            HocVienPhotoProcessingStatuses.Succeeded,
            repository.Current!.ProcessingStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changed_source_or_missing_output_is_queued_for_reprocessing(
        bool changeSource)
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var sourcePath = Path.Combine(paths.SourceRoot, "KHOA-01", "66029-001.jp2");
        var currentHash = Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))).ToLowerInvariant();
        var output = paths.CreateOutputResolver()
            .Resolve("CSDT_OTO", "KHOA-01", "66029-001");
        if (!changeSource)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output.FullPath!)!);
            // The missing-output case deliberately leaves no output file.
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output.FullPath!)!);
            await File.WriteAllBytesAsync(output.FullPath!, [1, 2, 3]);
        }

        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.Succeeded,
                output.RelativePath,
                confidence: 0.99,
                reviewRequired: false,
                sourceFileHash: changeSource ? new string('0', 64) : currentHash),
        };
        var queue = new HocVienPhotoProcessingQueue(Options.Create(paths.Options));
        var service = paths.CreateService(repository, new FakeEngine(ready: true), queue);

        var result = await service.QueueAfterSyncAsync(
            [new("CSDT_OTO", "66029-001", "KHOA-01", null)],
            "test");

        Assert.Equal(1, result.Queued);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(HocVienPhotoProcessingStatuses.Pending, repository.Current!.ProcessingStatus);
    }

    [Fact]
    public async Task Processing_reads_but_never_modifies_original_jp2()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var sourcePath = Path.Combine(paths.SourceRoot, "KHOA-01", "66029-001.jp2");
        var before = await File.ReadAllBytesAsync(sourcePath);
        var beforeWrite = File.GetLastWriteTimeUtc(sourcePath);
        var repository = new FakeRepository();
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        await service.ProcessAsync(new HocVienPhotoProcessingWorkItem(
            "CSDT_OTO", "66029-001", "KHOA-01", null, false, "test"));

        Assert.Equal(before, await File.ReadAllBytesAsync(sourcePath));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(sourcePath));
    }

    [Fact]
    public async Task Engine_failure_is_recorded_without_throwing_from_photo_service()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var repository = new FakeRepository();
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true, failProcessing: true),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        await service.ProcessAsync(new HocVienPhotoProcessingWorkItem(
            "CSDT_OTO", "66029-001", "KHOA-01", null, false, "test"));

        Assert.Equal(HocVienPhotoProcessingStatuses.Failed, repository.Current!.ProcessingStatus);
        Assert.Equal("PHOTO_ENGINE_FAILED", repository.Current.ErrorMessage);
    }

    [Fact]
    public async Task Print_policy_allows_approved_output_and_blocks_review_output()
    {
        using var paths = new PhotoTestPaths();
        var output = paths.CreateOutputResolver()
            .Resolve("CSDT_OTO", "KHOA-01", "66029-001");
        Directory.CreateDirectory(Path.GetDirectoryName(output.FullPath!)!);
        await File.WriteAllBytesAsync(output.FullPath!, [1, 2, 3]);
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.Approved,
                output.RelativePath,
                confidence: 0.99,
                reviewRequired: false),
        };
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var approved = await service.GetPrintSelectionAsync("CSDT_OTO", "66029-001");
        repository.Current = FakeRepository.Photo(
            HocVienPhotoProcessingStatuses.ReviewRequired,
            output.RelativePath,
            confidence: 0.50,
            reviewRequired: true);
        var review = await service.GetPrintSelectionAsync("CSDT_OTO", "66029-001");

        Assert.True(approved.CanPrint);
        Assert.NotNull(approved.Image);
        Assert.False(review.CanPrint);
        Assert.Null(review.Image);
    }

    [Fact]
    public async Task Approving_an_already_approved_photo_is_idempotent()
    {
        using var paths = new PhotoTestPaths();
        var output = paths.CreateOutputResolver()
            .Resolve("CSDT_OTO", "KHOA-01", "66029-001");
        Directory.CreateDirectory(Path.GetDirectoryName(output.FullPath!)!);
        await File.WriteAllBytesAsync(output.FullPath!, [1, 2, 3]);
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.Approved,
                output.RelativePath,
                confidence: 0.99,
                reviewRequired: false),
        };
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));

        var result = await service.ApproveAsync(1, 42, "admin");

        Assert.NotNull(result);
        Assert.Equal(HocVienPhotoProcessingStatuses.Approved, result.ProcessingStatus);
        Assert.Equal(0, repository.ApproveCalls);
    }

    [Fact]
    public void Queue_is_bounded_and_reports_pending_count()
    {
        using var paths = new PhotoTestPaths();
        var queue = new HocVienPhotoProcessingQueue(Options.Create(
            paths.CopyOptions(queueCapacity: 1)));
        var first = new HocVienPhotoProcessingWorkItem(
            "CSDT_OTO", "1", "K", null, false, "test");
        var second = first with { SourceMaDK = "2" };

        Assert.True(queue.TryEnqueue(first));
        Assert.False(queue.TryEnqueue(second));
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task Queue_capacity_overflow_remains_pending_for_durable_recovery()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        paths.WriteSource("KHOA-01", "66029-002.jp2");
        var repository = new FakeRepository();
        var queue = new HocVienPhotoProcessingQueue(Options.Create(
            paths.CopyOptions(queueCapacity: 1)));
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true),
            queue);

        var result = await service.QueueAfterSyncAsync(
            [
                new("CSDT_OTO", "66029-001", "KHOA-01", null),
                new("CSDT_OTO", "66029-002", "KHOA-01", null),
            ],
            "test");

        Assert.Equal(2, result.Queued);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(
            HocVienPhotoProcessingStatuses.Pending,
            repository.Current!.ProcessingStatus);
        Assert.Null(repository.Current.ErrorMessage);
    }

    [Fact]
    public async Task Reprocess_capacity_overflow_remains_pending_for_durable_recovery()
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                HocVienPhotoProcessingStatuses.Failed,
                output: null,
                confidence: null,
                reviewRequired: true),
        };
        var queue = new HocVienPhotoProcessingQueue(Options.Create(
            paths.CopyOptions(queueCapacity: 1)));
        Assert.True(queue.TryEnqueue(new HocVienPhotoProcessingWorkItem(
            "CSDT_OTO",
            "queue-slot",
            "KHOA-01",
            null,
            false,
            "test")));
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true),
            queue);

        var result = await service.ReprocessAsync(1, "admin");

        Assert.NotNull(result);
        Assert.Equal(HocVienPhotoProcessingStatuses.Pending, result.ProcessingStatus);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, queue.PendingCount);
    }

    [Theory]
    [InlineData(HocVienPhotoProcessingStatuses.Pending)]
    [InlineData(HocVienPhotoProcessingStatuses.Processing)]
    public async Task Worker_recovers_durable_metadata_after_process_restart(string status)
    {
        using var paths = new PhotoTestPaths();
        paths.WriteSource("KHOA-01", "66029-001.jp2");
        var repository = new FakeRepository
        {
            Current = FakeRepository.Photo(
                status,
                output: null,
                confidence: null,
                reviewRequired: false),
        };
        var queue = new HocVienPhotoProcessingQueue(Options.Create(paths.Options));
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true),
            queue);
        using var provider = new ServiceCollection()
            .AddScoped<IHocVienPhotoProcessingRepository>(_ => repository)
            .AddScoped<IHocVienPhotoProcessingService>(_ => service)
            .BuildServiceProvider();
        var worker = new HocVienPhotoProcessingWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HocVienPhotoProcessingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => repository.Current?.ProcessingStatus ==
                HocVienPhotoProcessingStatuses.Succeeded);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(
            HocVienPhotoProcessingStatuses.Succeeded,
            repository.Current!.ProcessingStatus);
        Assert.True(repository.WriteCalls >= 2);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Controller_requires_read_role_and_admin_for_mutations()
    {
        var controllerPolicy = typeof(QlhvPhotosController)
            .GetCustomAttributes<AuthorizeAttribute>()
            .Single()
            .Policy;
        var approvePolicy = typeof(QlhvPhotosController)
            .GetMethod(nameof(QlhvPhotosController.Approve))!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Single()
            .Policy;
        var reprocessPolicy = typeof(QlhvPhotosController)
            .GetMethod(nameof(QlhvPhotosController.Reprocess))!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Single()
            .Policy;

        Assert.Equal(AuthPolicies.CanViewBusinessData, controllerPolicy);
        Assert.Equal(AuthPolicies.CanImportData, approvePolicy);
        Assert.Equal(AuthPolicies.CanImportData, reprocessPolicy);
    }

    [Fact]
    public async Task Controller_returns_conflict_when_approval_engine_is_disabled()
    {
        using var paths = new PhotoTestPaths();
        paths.Options.Enabled = false;
        var repository = new FakeRepository();
        var service = paths.CreateService(
            repository,
            new FakeEngine(ready: true),
            new HocVienPhotoProcessingQueue(Options.Create(paths.Options)));
        var controller = new QlhvPhotosController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "42"),
                        new Claim(ClaimTypes.Name, "admin"),
                    ], "test")),
                },
            },
        };

        var action = await controller.Approve(1, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(0, repository.ReadCalls);
        Assert.Equal(0, repository.WriteCalls);
    }

    [Fact]
    public void Sql_patch_is_transactional_idempotent_and_stores_metadata_only()
    {
        var sql = File.ReadAllText(FindWorkspaceFile(
            "database",
            "patches",
            "20260723_add_hocvien_photo_processing.sql"));

        Assert.Contains("USE [QLHV_APP]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OBJECT_ID(N'dbo.App_HocVienPhoto'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OBJECT_ID(N'dbo.App_DataVersion'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COL_LENGTH(N'dbo.App_DataVersion', N'PhotoVersion')", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UX_App_HocVienPhoto_SourceIdentity", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sourceIdentity.is_unique = 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FK_App_HocVienPhotoHistory_Photo", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP CONSTRAINT CK_App_HocVienPhoto_ProcessingStatus", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP INDEX IX_App_HocVienPhoto_StatusReview", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DROP INDEX IX_App_HocVienPhotoHistory_PhotoCreated", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProcessingStatus = N'APPROVED'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReviewRequired = 0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WITH CHECK CHECK CONSTRAINT ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VARBINARY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_committed_photo_metadata_transition_bumps_photo_version_atomically()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "HocVien",
            "Photos",
            "HocVienPhotoProcessingRepository.cs"));
        var pending = Slice(source, "public async Task<long> UpsertPendingAsync", "public Task MarkProcessingAsync");
        var approve = Slice(source, "public async Task<bool> ApproveAsync", "private async Task UpdateStateAsync");
        var state = Slice(source, "private async Task UpdateStateAsync", "private async Task<string> ResolveTargetAsync");

        AssertAtomicPhotoVersionBump(pending);
        AssertAtomicPhotoVersionBump(approve);
        AssertAtomicPhotoVersionBump(state);
        Assert.Equal(3, CountOccurrences(
            source,
            "PhotoVersion = PhotoVersion + 1"));
        Assert.Contains(
            "ApprovedAtUtc = CASE WHEN @Status = N'APPROVED' THEN ApprovedAtUtc ELSE NULL END",
            state,
            StringComparison.Ordinal);
        Assert.Contains(
            "ApprovedByUserId = CASE WHEN @Status = N'APPROVED' THEN ApprovedByUserId ELSE NULL END",
            state,
            StringComparison.Ordinal);
        Assert.True(
            approve.IndexOf("IF @Affected > 0", StringComparison.Ordinal) <
            approve.IndexOf("PhotoVersion = PhotoVersion + 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Full_sync_transaction_does_not_increment_photo_version()
    {
        Assert.DoesNotContain(
            "PhotoVersion",
            QlhvDataVersionSql.IncrementAfterSuccessfulFullSync,
            StringComparison.Ordinal);
        Assert.Contains(
            "HocVienVersion = HocVienVersion + 1",
            QlhvDataVersionSql.IncrementAfterSuccessfulFullSync,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Benchmark_harness_is_explicit_local_source_read_only_and_api_free()
    {
        var script = File.ReadAllText(FindWorkspaceFile(
            "scripts",
            "windows",
            "qlhv-lan",
            "Invoke-QLHV-PhotoBenchmark.ps1"));
        var harness = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Tests",
            "HocVien",
            "PhotoProcessingOptInBenchmarkTests.cs"));
        var guide = File.ReadAllText(FindWorkspaceFile(
            "docs",
            "deployment",
            "qlhv-photo-model-and-benchmark.md"));

        Assert.Contains("RUN_LOCAL_READ_ONLY_PHOTO_BENCHMARK", script, StringComparison.Ordinal);
        Assert.Contains("Path.GetTempPath()", harness, StringComparison.Ordinal);
        Assert.Contains("CaptureSourceStateAsync", harness, StringComparison.Ordinal);
        Assert.Contains("OnnxBackgroundRemovalEngine", harness, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-RestMethod", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlcmd", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlConnection", harness, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", harness, StringComparison.Ordinal);
        Assert.Contains("BENCHMARK CHƯA THỂ THỰC HIỆN", guide, StringComparison.Ordinal);
        Assert.Contains("Not measured", guide, StringComparison.Ordinal);
    }

    private static string FindWorkspaceFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Cannot locate workspace file.",
            Path.Combine(pathParts));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static void AssertAtomicPhotoVersionBump(string section)
    {
        var begin = section.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal);
        var bump = section.IndexOf("PhotoVersion = PhotoVersion + 1", StringComparison.Ordinal);
        var commit = section.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal);
        var rollback = section.IndexOf("ROLLBACK TRANSACTION", StringComparison.Ordinal);

        Assert.True(begin >= 0);
        Assert.True(bump > begin);
        Assert.True(commit > bump);
        Assert.True(rollback > commit);
        Assert.Contains("IF @@ROWCOUNT <> 1", section, StringComparison.Ordinal);
        Assert.Contains("THROW 527320", section, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private sealed class PhotoTestPaths : IDisposable
    {
        public PhotoTestPaths()
        {
            Root = Path.Combine(Path.GetTempPath(), "qlhv-photo-tests", Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "IM_GPLX");
            OutputRoot = Path.Combine(Root, "derived");
            Directory.CreateDirectory(SourceRoot);
            Options = new HocVienPhotoProcessingOptions
            {
                Enabled = true,
                SourceRoot = SourceRoot,
                OutputRoot = OutputRoot,
                AutoProcessAfterSync = true,
                MinimumAutoApprovalConfidence = 0.85,
                QueueCapacity = 4,
            };
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string OutputRoot { get; }
        public HocVienPhotoProcessingOptions Options { get; }

        public void WriteSource(string directory, string fileName)
        {
            var path = Path.Combine(SourceRoot, directory, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3, 4]);
        }

        public SecureHocVienSourcePhotoPathResolver CreateSourceResolver() =>
            new(Microsoft.Extensions.Options.Options.Create(Options));

        public SecureHocVienPhotoOutputPathResolver CreateOutputResolver() =>
            new(Microsoft.Extensions.Options.Options.Create(Options));

        public HocVienPhotoProcessingService CreateService(
            FakeRepository repository,
            IBackgroundRemovalEngine engine,
            IHocVienPhotoProcessingQueue queue) =>
            new(
                Microsoft.Extensions.Options.Options.Create(Options),
                CreateSourceResolver(),
                CreateOutputResolver(),
                engine,
                queue,
                repository);

        public HocVienPhotoProcessingOptions CopyOptions(
            string? outputRoot = null,
            string? modelPath = null,
            string? modelLicense = null,
            string? modelSha256 = null,
            string? modelLicenseManifestPath = null,
            string? modelLicenseManifestSha256 = null,
            int? queueCapacity = null) =>
            new()
            {
                Enabled = Options.Enabled,
                SourceRoot = Options.SourceRoot,
                OutputRoot = outputRoot ?? Options.OutputRoot,
                ModelPath = modelPath ?? Options.ModelPath,
                ModelLicense = modelLicense ?? Options.ModelLicense,
                ModelSha256 = modelSha256 ?? Options.ModelSha256,
                ModelLicenseManifestPath =
                    modelLicenseManifestPath ?? Options.ModelLicenseManifestPath,
                ModelLicenseManifestSha256 =
                    modelLicenseManifestSha256 ?? Options.ModelLicenseManifestSha256,
                BackgroundColor = Options.BackgroundColor,
                AutoProcessAfterSync = Options.AutoProcessAfterSync,
                MinimumAutoApprovalConfidence = Options.MinimumAutoApprovalConfidence,
                QueueCapacity = queueCapacity ?? Options.QueueCapacity,
                InputWidth = Options.InputWidth,
                InputHeight = Options.InputHeight,
                InputName = Options.InputName,
                OutputName = Options.OutputName,
                JpegQuality = Options.JpegQuality,
                MaxSourceBytes = Options.MaxSourceBytes,
            };

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FakeEngine : IBackgroundRemovalEngine
    {
        private readonly bool _ready;
        private readonly double _confidence;
        private readonly bool _failProcessing;

        public int ReadinessCalls { get; private set; }

        public int ProcessingCalls { get; private set; }

        public FakeEngine(
            bool ready,
            double confidence = 0.99,
            bool failProcessing = false)
        {
            _ready = ready;
            _confidence = confidence;
            _failProcessing = failProcessing;
        }

        public Task<BackgroundRemovalEngineReadiness> GetReadinessAsync(
            CancellationToken cancellationToken = default)
        {
            ReadinessCalls++;
            return Task.FromResult(new BackgroundRemovalEngineReadiness(
                _ready,
                _ready ? "READY" : "MODEL_MISSING",
                "fake",
                null,
                _ready ? "Ready." : "Model missing."));
        }

        public Task<BackgroundRemovalResult> RemoveBackgroundAsync(
            ReadOnlyMemory<byte> sourceContent,
            string backgroundColor,
            CancellationToken cancellationToken = default)
        {
            ProcessingCalls++;
            if (_failProcessing)
            {
                throw new InvalidOperationException("Synthetic engine failure.");
            }

            return Task.FromResult(new BackgroundRemovalResult(
                [0xFF, 0xD8, 0xFF, 0xD9],
                "image/jpeg",
                ".jpg",
                _confidence));
        }
    }

    private sealed class FakeRepository : IHocVienPhotoProcessingRepository
    {
        public HocVienPhotoRecordDto? Current { get; set; }

        public int ApproveCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public Task<HocVienPhotoRecordDto?> GetByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(Current?.Id == id ? Current : null);
        }

        public Task<HocVienPhotoRecordDto?> GetByIdentityAsync(
            string sourceProfileCode,
            string sourceMaDk,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(
                Current?.SourceProfileCode == sourceProfileCode &&
                Current.SourceMaDK == sourceMaDk
                    ? Current
                    : null);
        }

        public Task<PagedResult<HocVienPhotoRecordDto>> SearchAsync(
            HocVienPhotoSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            var matches = Current is not null &&
                          (request.Status is null ||
                           Current.ProcessingStatus == request.Status) &&
                          (request.SourceProfileCode is null ||
                           Current.SourceProfileCode == request.SourceProfileCode) &&
                          (request.ReviewRequired is null ||
                           Current.ReviewRequired == request.ReviewRequired);
            return Task.FromResult(new PagedResult<HocVienPhotoRecordDto>
            {
                Items = matches ? [Current!] : [],
                Page = 1,
                PageSize = 20,
                TotalItems = matches ? 1 : 0,
            });
        }

        public Task<HocVienPhotoProcessingCountsDto> GetCountsAsync(
            string? sourceProfileCode,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(new HocVienPhotoProcessingCountsDto
            {
                Total = Current is null ? 0 : 1,
            });
        }

        public Task<long> UpsertPendingAsync(
            HocVienPhotoProcessingSource source,
            HocVienSourcePhotoResolution resolution,
            string actor,
            CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            Current = new HocVienPhotoRecordDto
            {
                Id = Current?.Id ?? 1,
                SourceProfileCode = source.SourceProfileCode,
                SourceMaDK = source.SourceMaDK,
                MaKhoaHoc = source.MaKhoa,
                SourceImagePath = resolution.RelativePath,
                OutputImagePath = Current?.OutputImagePath,
                SourceFileHash = Current?.SourceFileHash,
                SourcePathStatus = resolution.Status,
                SourcePathKind = resolution.PathKind,
                ProcessingStatus = HocVienPhotoProcessingStatuses.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            return Task.FromResult(Current.Id);
        }

        public Task MarkProcessingAsync(
            long id,
            string actor,
            CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            Current = Copy(HocVienPhotoProcessingStatuses.Processing);
            return Task.CompletedTask;
        }

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
            WriteCalls++;
            Current = Copy(
                status,
                outputImagePath,
                sourceFileHash,
                confidence,
                reviewRequired,
                null);
            return Task.CompletedTask;
        }

        public Task FailAsync(
            long id,
            string safeError,
            string actor,
            CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            Current = Copy(
                HocVienPhotoProcessingStatuses.Failed,
                errorMessage: safeError,
                reviewRequired: true);
            return Task.CompletedTask;
        }

        public Task<bool> ApproveAsync(
            long id,
            long userId,
            string actor,
            CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            ApproveCalls++;
            Current = Copy(HocVienPhotoProcessingStatuses.Approved);
            return Task.FromResult(true);
        }

        public static HocVienPhotoRecordDto Photo(
            string status,
            string? output,
            double? confidence,
            bool reviewRequired,
            string? sourceFileHash = null) =>
            new()
            {
                Id = 1,
                SourceProfileCode = "CSDT_OTO",
                SourceMaDK = "66029-001",
                MaKhoaHoc = "KHOA-01",
                SourcePathStatus = HocVienSourcePhotoStatuses.Found,
                SourcePathKind = HocVienSourcePhotoPathKinds.Current,
                ProcessingStatus = status,
                OutputImagePath = output,
                SourceFileHash = sourceFileHash,
                ProcessingConfidence = confidence,
                ReviewRequired = reviewRequired,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };

        private HocVienPhotoRecordDto Copy(
            string status,
            string? outputImagePath = null,
            string? sourceFileHash = null,
            double? confidence = null,
            bool? reviewRequired = null,
            string? errorMessage = null)
        {
            var current = Assert.IsType<HocVienPhotoRecordDto>(Current);
            return new HocVienPhotoRecordDto
            {
                Id = current.Id,
                SourceProfileCode = current.SourceProfileCode,
                SourceMaDK = current.SourceMaDK,
                StudentName = current.StudentName,
                MaKhoaHoc = current.MaKhoaHoc,
                SourceImagePath = current.SourceImagePath,
                OutputImagePath = outputImagePath ?? current.OutputImagePath,
                SourceFileHash = sourceFileHash ?? current.SourceFileHash,
                SourcePathStatus = current.SourcePathStatus,
                SourcePathKind = current.SourcePathKind,
                ProcessingStatus = status,
                ProcessingConfidence = confidence ?? current.ProcessingConfidence,
                ErrorMessage = errorMessage,
                ReviewRequired = reviewRequired ?? current.ReviewRequired,
                CreatedAtUtc = current.CreatedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow,
            };
        }
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QLHV.Application.HocVien.Photos;
using QLHV.Infrastructure.HocVien.Photos;
using Xunit.Abstractions;

namespace QLHV.Tests.HocVien;

public sealed class PhotoBenchmarkFactAttribute : FactAttribute
{
    public const string OptInPhrase = "RUN_LOCAL_READ_ONLY_PHOTO_BENCHMARK";

    public PhotoBenchmarkFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("QLHV_PHOTO_BENCHMARK_OPT_IN"),
                OptInPhrase,
                StringComparison.Ordinal))
        {
            Skip =
                "BENCHMARK CHUA THE THUC HIEN: explicit local opt-in, reviewed model, " +
                "license manifest, and real JP2 fixtures were not supplied.";
        }
    }
}

/// <summary>
/// Local-only benchmark harness. It never starts the API or opens a database connection.
/// The test is skipped unless the operator explicitly opts in and supplies a reviewed model
/// and a real JP2 fixture directory through process-scoped environment variables.
/// </summary>
public sealed class PhotoProcessingOptInBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PhotoProcessingOptInBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [PhotoBenchmarkFact]
    [Trait("Category", "PhotoBenchmark")]
    public async Task Run_reviewed_model_against_read_only_jp2_fixtures()
    {
        var modelPath = GetRequiredFullFilePath("QLHV_PHOTO_BENCHMARK_MODEL_PATH", ".onnx");
        var modelSha256 = GetRequiredSha256("QLHV_PHOTO_BENCHMARK_MODEL_SHA256");
        var licenseId = GetRequired("QLHV_PHOTO_BENCHMARK_LICENSE_ID");
        var manifestPath = GetRequiredFullFilePath(
            "QLHV_PHOTO_BENCHMARK_LICENSE_MANIFEST_PATH",
            ".json");
        var manifestSha256 = GetRequiredSha256(
            "QLHV_PHOTO_BENCHMARK_LICENSE_MANIFEST_SHA256");
        var inputRoot = GetRequiredFullDirectoryPath("QLHV_PHOTO_BENCHMARK_INPUT_ROOT");
        var maxImages = GetBoundedInt("QLHV_PHOTO_BENCHMARK_MAX_IMAGES", 20, 1, 200);
        var minimumConfidence = GetBoundedDouble(
            "QLHV_PHOTO_BENCHMARK_MINIMUM_CONFIDENCE",
            0.85d,
            0d,
            1d);

        Assert.Equal(
            modelSha256,
            await ComputeSha256Async(modelPath),
            ignoreCase: true);
        Assert.Equal(
            manifestSha256,
            await ComputeSha256Async(manifestPath),
            ignoreCase: true);

        var sourceFiles = EnumerateJp2WithoutFollowingReparsePoints(inputRoot, maxImages);
        Assert.NotEmpty(sourceFiles);
        var sourceBefore = await CaptureSourceStateAsync(sourceFiles);

        var benchmarkRoot = Path.Combine(
            Path.GetTempPath(),
            "qlhv-photo-benchmark",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
            Guid.NewGuid().ToString("N"));
        var derivedRoot = Path.Combine(benchmarkRoot, "derived");
        Directory.CreateDirectory(derivedRoot);
        Assert.False(IsSameOrDescendant(benchmarkRoot, inputRoot));

        var options = new HocVienPhotoProcessingOptions
        {
            Enabled = true,
            AutoProcessAfterSync = false,
            SourceRoot = inputRoot,
            OutputRoot = derivedRoot,
            ModelPath = modelPath,
            ModelSha256 = modelSha256,
            ModelLicense = licenseId,
            ModelLicenseManifestPath = manifestPath,
            ModelLicenseManifestSha256 = manifestSha256,
            MinimumAutoApprovalConfidence = minimumConfidence,
        };

        using var engine = new OnnxBackgroundRemovalEngine(Options.Create(options));
        var readiness = await engine.GetReadinessAsync();
        Assert.True(readiness.IsReady, $"{readiness.Status}: {readiness.Message}");

        var measurements = new List<BenchmarkMeasurement>(sourceFiles.Count);
        var failures = new List<BenchmarkFailure>();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        foreach (var sourceFile in sourceFiles)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                if (new FileInfo(sourceFile).Length > options.MaxSourceBytes)
                {
                    throw new InvalidDataException("SOURCE_PHOTO_TOO_LARGE");
                }

                var sourceBytes = await File.ReadAllBytesAsync(sourceFile);
                var result = await engine.RemoveBackgroundAsync(
                    sourceBytes,
                    options.BackgroundColor);
                timer.Stop();

                Assert.Equal("image/jpeg", result.ContentType);
                Assert.Equal(".jpg", result.Extension);
                Assert.NotEmpty(result.Content);
                var outputPath = Path.Combine(
                    derivedRoot,
                    $"{measurements.Count + failures.Count + 1:D4}.jpg");
                await File.WriteAllBytesAsync(outputPath, result.Content);
                measurements.Add(new BenchmarkMeasurement(
                    Path.GetFileName(sourceFile),
                    timer.Elapsed.TotalMilliseconds,
                    result.Confidence,
                    result.Confidence < minimumConfidence,
                    sourceBytes.LongLength,
                    result.Content.LongLength));
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    InvalidOperationException or
                    ImageMagick.MagickException or
                    Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
            {
                timer.Stop();
                failures.Add(new BenchmarkFailure(
                    Path.GetFileName(sourceFile),
                    timer.Elapsed.TotalMilliseconds,
                    exception.GetType().Name));
            }
        }

        var allocatedBytes = Math.Max(
            0,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
        var process = Process.GetCurrentProcess();
        var orderedTimes = measurements
            .Select(item => item.ElapsedMilliseconds)
            .Order()
            .ToArray();
        var report = new BenchmarkReport(
            SchemaVersion: 1,
            MeasuredAtUtc: DateTime.UtcNow,
            Engine: OnnxBackgroundRemovalEngine.EngineName,
            ModelSha256: modelSha256,
            LicenseId: licenseId,
            LicenseManifestSha256: manifestSha256,
            SourceRoot: inputRoot,
            OutputRoot: derivedRoot,
            RequestedImages: sourceFiles.Count,
            SuccessfulImages: measurements.Count,
            FailedImages: failures.Count,
            P50Milliseconds: Percentile(orderedTimes, 0.50d),
            P95Milliseconds: Percentile(orderedTimes, 0.95d),
            TotalAllocatedBytes: allocatedBytes,
            ProcessPeakWorkingSetBytes: process.PeakWorkingSet64,
            AverageConfidence: measurements.Count == 0
                ? null
                : measurements.Average(item => item.Confidence),
            ReviewRequiredImages: measurements.Count(item => item.ReviewRequired),
            Measurements: measurements,
            Failures: failures);

        var reportPath = Path.Combine(benchmarkRoot, "benchmark-report.json");
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));

        var sourceAfter = await CaptureSourceStateAsync(sourceFiles);
        Assert.True(
            sourceBefore.SequenceEqual(sourceAfter),
            "At least one source JP2 hash, size, timestamp, or path changed during the benchmark.");
        _output.WriteLine($"Measured benchmark report: {reportPath}");
        _output.WriteLine(
            $"Processed {measurements.Count}/{sourceFiles.Count}; " +
            $"failed {failures.Count}; p50 {report.P50Milliseconds:F2} ms; " +
            $"p95 {report.P95Milliseconds:F2} ms.");
        Assert.Empty(failures);
    }

    private static string GetRequired(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName)?.Trim();
        Assert.False(string.IsNullOrWhiteSpace(value), $"{variableName} is required.");
        return value!;
    }

    private static string GetRequiredFullFilePath(string variableName, string extension)
    {
        var value = GetRequired(variableName);
        Assert.True(Path.IsPathRooted(value), $"{variableName} must be an absolute path.");
        var path = Path.GetFullPath(value);
        Assert.Equal(extension, Path.GetExtension(path), ignoreCase: true);
        Assert.True(File.Exists(path), $"{variableName} does not exist.");
        return path;
    }

    private static string GetRequiredFullDirectoryPath(string variableName)
    {
        var value = GetRequired(variableName);
        Assert.True(Path.IsPathRooted(value), $"{variableName} must be an absolute path.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        Assert.True(Directory.Exists(path), $"{variableName} does not exist.");
        return path;
    }

    private static string GetRequiredSha256(string variableName)
    {
        var value = GetRequired(variableName).ToLowerInvariant();
        Assert.True(
            value.Length == 64 &&
            value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
            $"{variableName} must be a lowercase SHA-256 value.");
        return value;
    }

    private static int GetBoundedInt(
        string variableName,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        Assert.True(int.TryParse(raw, out var value), $"{variableName} must be an integer.");
        Assert.InRange(value, minimum, maximum);
        return value;
    }

    private static double GetBoundedDouble(
        string variableName,
        double defaultValue,
        double minimum,
        double maximum)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        Assert.True(
            double.TryParse(
                raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value),
            $"{variableName} must be an invariant floating-point number.");
        Assert.InRange(value, minimum, maximum);
        return value;
    }

    private static IReadOnlyList<string> EnumerateJp2WithoutFollowingReparsePoints(
        string root,
        int maximum)
    {
        var result = new List<string>(maximum);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0 && result.Count < maximum)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "*.jp2"))
            {
                var fullPath = Path.GetFullPath(file);
                if (IsSameOrDescendant(fullPath, root))
                {
                    result.Add(fullPath);
                    if (result.Count == maximum)
                    {
                        break;
                    }
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(info.FullName);
                }
            }
        }

        return result
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<SourceState>> CaptureSourceStateAsync(
        IReadOnlyList<string> paths)
    {
        var states = new List<SourceState>(paths.Count);
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            states.Add(new SourceState(
                path,
                info.Length,
                info.LastWriteTimeUtc.Ticks,
                await ComputeSha256Async(path)));
        }

        return states;
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream))
            .ToLowerInvariant();
    }

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        var normalizedCandidate = Path.GetFullPath(candidate);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(
                   normalizedCandidate,
                   normalizedRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static double? Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 0)
        {
            return null;
        }

        var index = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }

    private sealed record SourceState(
        string Path,
        long Length,
        long LastWriteTimeUtcTicks,
        string Sha256);

    private sealed record BenchmarkMeasurement(
        string SourceFileName,
        double ElapsedMilliseconds,
        double Confidence,
        bool ReviewRequired,
        long InputBytes,
        long OutputBytes);

    private sealed record BenchmarkFailure(
        string SourceFileName,
        double ElapsedMilliseconds,
        string FailureType);

    private sealed record BenchmarkReport(
        int SchemaVersion,
        DateTime MeasuredAtUtc,
        string Engine,
        string ModelSha256,
        string LicenseId,
        string LicenseManifestSha256,
        string SourceRoot,
        string OutputRoot,
        int RequestedImages,
        int SuccessfulImages,
        int FailedImages,
        double? P50Milliseconds,
        double? P95Milliseconds,
        long TotalAllocatedBytes,
        long ProcessPeakWorkingSetBytes,
        double? AverageConfidence,
        int ReviewRequiredImages,
        IReadOnlyList<BenchmarkMeasurement> Measurements,
        IReadOnlyList<BenchmarkFailure> Failures);
}

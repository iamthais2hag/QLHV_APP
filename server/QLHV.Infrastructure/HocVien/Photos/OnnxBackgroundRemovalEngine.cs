using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using QLHV.Application.HocVien.Photos;

namespace QLHV.Infrastructure.HocVien.Photos;

/// <summary>
/// CPU-only, local MODNet-compatible ONNX portrait matting engine. The model is deliberately
/// external to Git. Readiness requires an explicit license declaration and SHA-256 checksum.
/// </summary>
public sealed class OnnxBackgroundRemovalEngine : IBackgroundRemovalEngine, IDisposable
{
    public const string EngineName = "MODNet-compatible ONNX Runtime CPU";
    private const int MaximumLicenseManifestBytes = 1024 * 1024;

    public static IReadOnlySet<string> AcceptedModelLicenseIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Apache-2.0",
            "MIT",
            "BSD-2-Clause",
            "BSD-3-Clause",
        };

    private readonly HocVienPhotoProcessingOptions _options;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private InferenceSession? _session;
    private string? _loadedModelHash;
    private bool _disposed;

    public OnnxBackgroundRemovalEngine(IOptions<HocVienPhotoProcessingOptions> options)
    {
        _options = options.Value;
    }

    public async Task<BackgroundRemovalEngineReadiness> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return NotReady("DISABLED", "Photo processing is disabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.ModelLicense))
        {
            return NotReady(
                "MODEL_LICENSE_NOT_DECLARED",
                "A locally reviewed SPDX model license identifier must be declared before the engine can run.");
        }

        var licenseId = _options.ModelLicense.Trim();
        if (!AcceptedModelLicenseIds.Contains(licenseId))
        {
            return NotReady(
                "MODEL_LICENSE_NOT_ACCEPTED",
                "The configured model license identifier is not in the locally accepted allowlist.");
        }

        if (string.IsNullOrWhiteSpace(_options.ModelPath) ||
            !Path.IsPathRooted(_options.ModelPath))
        {
            return NotReady("MODEL_PATH_INVALID", "The local ONNX model path is not configured.");
        }

        string modelPath;
        try
        {
            modelPath = Path.GetFullPath(_options.ModelPath.Trim());
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return NotReady("MODEL_PATH_INVALID", "The local ONNX model path is not valid.");
        }
        if (!string.Equals(Path.GetExtension(modelPath), ".onnx", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(modelPath))
        {
            return NotReady("MODEL_MISSING", "The configured local ONNX model is missing.");
        }

        var expectedHash = NormalizeSha256(_options.ModelSha256);
        if (expectedHash is null)
        {
            return NotReady("CHECKSUM_MISSING", "A valid SHA-256 checksum is required for the local model.");
        }

        string actualHash;
        try
        {
            await using var stream = new FileStream(
                modelPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            actualHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NotReady("MODEL_UNREADABLE", "The configured local ONNX model cannot be read.");
        }

        if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
        {
            return new BackgroundRemovalEngineReadiness(
                false,
                "CHECKSUM_MISMATCH",
                EngineName,
                actualHash,
                "The local ONNX model checksum does not match configuration.");
        }

        var manifestReadiness = await ValidateLicenseManifestAsync(
            licenseId,
            expectedHash,
            cancellationToken);
        if (manifestReadiness is not null)
        {
            return manifestReadiness;
        }

        if (_options.InputWidth is < 64 or > 2048 ||
            _options.InputHeight is < 64 or > 2048 ||
            string.IsNullOrWhiteSpace(_options.InputName) ||
            string.IsNullOrWhiteSpace(_options.OutputName))
        {
            return new BackgroundRemovalEngineReadiness(
                false,
                "MODEL_IO_INVALID",
                EngineName,
                actualHash,
                "The configured model input/output contract is invalid.");
        }

        try
        {
            await EnsureSessionAsync(modelPath, actualHash, cancellationToken);
            return new BackgroundRemovalEngineReadiness(
                true,
                "READY",
                EngineName,
                actualHash,
                "The local ONNX engine is ready.");
        }
        catch (Exception ex) when (
            ex is OnnxRuntimeException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new BackgroundRemovalEngineReadiness(
                false,
                "MODEL_LOAD_FAILED",
                EngineName,
                actualHash,
                "The local ONNX model could not be loaded with the configured contract.");
        }
    }

    public async Task<BackgroundRemovalResult> RemoveBackgroundAsync(
        ReadOnlyMemory<byte> sourceContent,
        string backgroundColor,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var readiness = await GetReadinessAsync(cancellationToken);
        if (!readiness.IsReady || _session is null)
        {
            throw new InvalidOperationException($"Photo engine is not ready: {readiness.Status}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var source = new MagickImage(sourceContent.ToArray());
        source.AutoOrient();
        source.ColorSpace = ColorSpace.sRGB;
        var originalWidth = source.Width;
        var originalHeight = source.Height;
        if (originalWidth == 0 || originalHeight == 0)
        {
            throw new InvalidDataException("Source photo has invalid dimensions.");
        }

        using var inputImage = source.Clone();
        inputImage.Depth = 8;
        inputImage.Resize(new MagickGeometry(
            (uint)_options.InputWidth,
            (uint)_options.InputHeight)
        {
            IgnoreAspectRatio = true,
        });
        inputImage.Format = MagickFormat.Rgb;
        var rgb = inputImage.ToByteArray();
        var expectedBytes = _options.InputWidth * _options.InputHeight * 3;
        if (rgb.Length != expectedBytes)
        {
            throw new InvalidDataException("Source photo could not be converted to the ONNX RGB tensor.");
        }

        var tensor = new DenseTensor<float>(
            new[] { 1, 3, _options.InputHeight, _options.InputWidth });
        var pixels = _options.InputWidth * _options.InputHeight;
        for (var index = 0; index < pixels; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rgbIndex = index * 3;
            var y = index / _options.InputWidth;
            var x = index % _options.InputWidth;
            tensor[0, 0, y, x] = rgb[rgbIndex] / 127.5f - 1f;
            tensor[0, 1, y, x] = rgb[rgbIndex + 1] / 127.5f - 1f;
            tensor[0, 2, y, x] = rgb[rgbIndex + 2] / 127.5f - 1f;
        }

        var input = NamedOnnxValue.CreateFromTensor(_options.InputName.Trim(), tensor);
        using var results = _session.Run(new[] { input }, new[] { _options.OutputName.Trim() });
        var output = results.Single().AsTensor<float>();
        var maskValues = output.ToArray();
        if (maskValues.Length != pixels)
        {
            throw new InvalidDataException(
                "The ONNX output is not a single-channel mask matching the configured input size.");
        }

        var maskBytes = new byte[pixels];
        double certaintyTotal = 0d;
        var foregroundPixels = 0;
        for (var index = 0; index < maskValues.Length; index++)
        {
            var probability = Math.Clamp(maskValues[index], 0f, 1f);
            maskBytes[index] = (byte)Math.Round(probability * 255f);
            certaintyTotal += Math.Abs(probability - 0.5f) * 2d;
            if (probability >= 0.5f)
            {
                foregroundPixels++;
            }
        }

        var foregroundRatio = foregroundPixels / (double)pixels;
        var confidence = certaintyTotal / pixels;
        if (foregroundRatio is < 0.08d or > 0.90d)
        {
            confidence *= 0.5d;
        }

        var maskSettings = new MagickReadSettings
        {
            Format = MagickFormat.Gray,
            Width = (uint)_options.InputWidth,
            Height = (uint)_options.InputHeight,
            Depth = 8,
        };
        using var mask = new MagickImage(maskBytes, maskSettings);
        mask.Resize(new MagickGeometry(originalWidth, originalHeight)
        {
            IgnoreAspectRatio = true,
        });

        using var foreground = source.Clone();
        foreground.Alpha(AlphaOption.On);
        foreground.Composite(mask, CompositeOperator.CopyAlpha);

        var color = ParseBackgroundColor(backgroundColor);
        using var composite = new MagickImage(color, originalWidth, originalHeight);
        composite.Composite(foreground, CompositeOperator.Over);
        composite.Strip();
        composite.Format = MagickFormat.Jpeg;
        composite.Quality = (uint)Math.Clamp(_options.JpegQuality, 70, 100);

        return new BackgroundRemovalResult(
            composite.ToByteArray(),
            "image/jpeg",
            ".jpg",
            Math.Clamp(confidence, 0d, 1d));
    }

    private async Task EnsureSessionAsync(
        string modelPath,
        string modelHash,
        CancellationToken cancellationToken)
    {
        if (_session is not null &&
            string.Equals(_loadedModelHash, modelHash, StringComparison.Ordinal))
        {
            return;
        }

        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null &&
                string.Equals(_loadedModelHash, modelHash, StringComparison.Ordinal))
            {
                return;
            }

            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            };
            var replacement = new InferenceSession(modelPath, sessionOptions);
            if (!replacement.InputMetadata.ContainsKey(_options.InputName.Trim()) ||
                !replacement.OutputMetadata.ContainsKey(_options.OutputName.Trim()))
            {
                replacement.Dispose();
                throw new InvalidOperationException("Configured ONNX input/output names were not found.");
            }

            _session?.Dispose();
            _session = replacement;
            _loadedModelHash = modelHash;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private static MagickColor ParseBackgroundColor(string value)
    {
        try
        {
            return new MagickColor(string.IsNullOrWhiteSpace(value) ? "#0067B1" : value.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or MagickException)
        {
            throw new InvalidOperationException("PhotoProcessing.BackgroundColor is invalid.", ex);
        }
    }

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length == 64 &&
               normalized.All(character =>
                   character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? normalized
            : null;
    }

    private async Task<BackgroundRemovalEngineReadiness?> ValidateLicenseManifestAsync(
        string licenseId,
        string modelSha256,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ModelLicenseManifestPath) ||
            !Path.IsPathRooted(_options.ModelLicenseManifestPath))
        {
            return NotReady(
                "LICENSE_MANIFEST_PATH_INVALID",
                "The reviewed local model license manifest path is not configured.");
        }

        string manifestPath;
        try
        {
            manifestPath = Path.GetFullPath(_options.ModelLicenseManifestPath.Trim());
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return NotReady(
                "LICENSE_MANIFEST_PATH_INVALID",
                "The reviewed local model license manifest path is invalid.");
        }

        if (!string.Equals(
                Path.GetExtension(manifestPath),
                ".json",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(manifestPath))
        {
            return NotReady(
                "LICENSE_MANIFEST_MISSING",
                "The reviewed local model license manifest is missing.");
        }

        var expectedManifestHash = NormalizeSha256(_options.ModelLicenseManifestSha256);
        if (expectedManifestHash is null)
        {
            return NotReady(
                "LICENSE_MANIFEST_CHECKSUM_MISSING",
                "A valid SHA-256 checksum is required for the local model license manifest.");
        }

        byte[] manifestBytes;
        try
        {
            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumLicenseManifestBytes)
            {
                return NotReady(
                    "LICENSE_MANIFEST_INVALID",
                    "The reviewed local model license manifest has an invalid size.");
            }

            manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NotReady(
                "LICENSE_MANIFEST_UNREADABLE",
                "The reviewed local model license manifest cannot be read.");
        }

        var actualManifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes))
            .ToLowerInvariant();
        if (!string.Equals(
                expectedManifestHash,
                actualManifestHash,
                StringComparison.Ordinal))
        {
            return NotReady(
                "LICENSE_MANIFEST_CHECKSUM_MISMATCH",
                "The local model license manifest checksum does not match configuration.");
        }

        try
        {
            using var manifest = JsonDocument.Parse(manifestBytes);
            var root = manifest.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var schemaVersion) ||
                schemaVersion.ValueKind != JsonValueKind.Number ||
                !schemaVersion.TryGetInt32(out var version) ||
                version != 1 ||
                !TryGetRequiredString(root, "licenseId", out var manifestLicenseId) ||
                !TryGetRequiredString(root, "modelSha256", out var manifestModelSha256) ||
                !TryGetRequiredString(root, "modelSource", out _) ||
                !TryGetRequiredString(root, "reviewedBy", out _) ||
                !TryGetRequiredString(root, "reviewedAtUtc", out var reviewedAtValue) ||
                !DateTimeOffset.TryParse(
                    reviewedAtValue,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var reviewedAtUtc) ||
                reviewedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return NotReady(
                    "LICENSE_MANIFEST_INVALID",
                    "The reviewed local model license manifest is incomplete or invalid.");
            }

            if (!AcceptedModelLicenseIds.Contains(manifestLicenseId) ||
                !string.Equals(
                    licenseId,
                    manifestLicenseId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return NotReady(
                    "LICENSE_MANIFEST_LICENSE_MISMATCH",
                    "The manifest license identifier does not match the accepted configured license.");
            }

            if (!string.Equals(
                    modelSha256,
                    NormalizeSha256(manifestModelSha256),
                    StringComparison.Ordinal))
            {
                return NotReady(
                    "LICENSE_MANIFEST_MODEL_MISMATCH",
                    "The manifest model checksum does not match the configured ONNX model.");
            }
        }
        catch (JsonException)
        {
            return NotReady(
                "LICENSE_MANIFEST_INVALID",
                "The reviewed local model license manifest is not valid JSON.");
        }

        return null;
    }

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static BackgroundRemovalEngineReadiness NotReady(string status, string message) =>
        new(false, status, EngineName, null, message);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session?.Dispose();
        _sessionGate.Dispose();
    }
}

using ImageMagick;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;
using QLHV.Application.HocVien.Photos;

namespace QLHV.Infrastructure.HocVien;

public sealed class HocVienPhotoService : IHocVienPhotoService
{
    private readonly HocVienPhotoPathResolver _resolver;
    private readonly IHocVienPhotoProcessingService? _processing;
    private readonly IHocVienSourcePhotoPathResolver? _sourceResolver;

    public HocVienPhotoService(HocVienPhotoPathResolver resolver)
    {
        _resolver = resolver;
    }

    public HocVienPhotoService(
        HocVienPhotoPathResolver resolver,
        IHocVienPhotoProcessingService processing,
        IHocVienSourcePhotoPathResolver sourceResolver)
    {
        _resolver = resolver;
        _processing = processing;
        _sourceResolver = sourceResolver;
    }

    public async Task<HocVienPhotoPreviewDto?> GetPreviewAsync(
        HocVienListItemDto hocVien,
        CancellationToken cancellationToken = default)
    {
        var processed = await GetProcessedSelectionAsync(hocVien, cancellationToken);
        if (processed is { CanPrint: true, Image: not null })
        {
            return DecodeToJpeg(processed.Image.Content);
        }

        if (processed is not null &&
            processed.Status != "NO_METADATA")
        {
            return null;
        }

        if (_sourceResolver is not null)
        {
            var source = _sourceResolver.Resolve(
                hocVien.AnhRelativePath,
                hocVien.MaKhoa,
                hocVien.MaDangKy);
            if (!source.Found || source.FullPath is null)
            {
                return null;
            }

            return DecodeToJpeg(
                await File.ReadAllBytesAsync(source.FullPath, cancellationToken));
        }

        if (!_resolver.TryResolve(hocVien.MaKhoa, hocVien.MaDangKy, out var fullPath) ||
            !File.Exists(fullPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        return DecodeToJpeg(bytes);
    }

    public async Task<HocVienPhotoInspectionDto> InspectAsync(
        HocVienListItemDto hocVien,
        bool validateDecode,
        CancellationToken cancellationToken = default)
    {
        var expectedRelativePath = BuildExpectedRelativePath(hocVien.MaKhoa, hocVien.MaDangKy);
        var processed = await GetProcessedSelectionAsync(hocVien, cancellationToken);
        if (processed is { CanPrint: true })
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = true,
                PhotoStatus = processed.Status == HocVienPhotoProcessingStatuses.Approved
                    ? "Approved"
                    : "Succeeded",
                Message = processed.Message,
            };
        }

        if (processed is not null && processed.Status != "NO_METADATA")
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = false,
                PhotoStatus = MapProcessingStatus(processed.Status),
                Message = processed.Message,
            };
        }

        if (_sourceResolver is not null)
        {
            var source = _sourceResolver.Resolve(
                hocVien.AnhRelativePath,
                hocVien.MaKhoa,
                hocVien.MaDangKy);
            if (!source.Found || source.FullPath is null)
            {
                return new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = false,
                    PhotoStatus = source.Status == HocVienSourcePhotoStatuses.InvalidPath
                        ? "UnsafePath"
                        : "Missing",
                    Message = source.Status == HocVienSourcePhotoStatuses.InvalidPath
                        ? "Photo path could not be resolved safely."
                        : "Expected photo file was not found.",
                };
            }

            if (!validateDecode)
            {
                return new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = true,
                    PhotoStatus = "HasPhoto",
                    Message = "Photo file exists. Decode was not checked.",
                };
            }

            try
            {
                DecodeToJpeg(await File.ReadAllBytesAsync(source.FullPath, cancellationToken));
                return new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = true,
                    PhotoStatus = "HasPhoto",
                    Message = "Photo file exists and can be decoded.",
                };
            }
            catch (NotSupportedException)
            {
                return new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = true,
                    PhotoStatus = "Unsupported",
                    Message = "Photo file exists but the image format is not supported by the server decoder.",
                };
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = true,
                    PhotoStatus = "Invalid",
                    Message = "Photo file exists but could not be read or decoded.",
                };
            }
        }

        if (!_resolver.TryResolve(hocVien.MaKhoa, hocVien.MaDangKy, out var fullPath))
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = false,
                PhotoStatus = "UnsafePath",
                Message = "Photo path could not be resolved safely.",
            };
        }

        if (!File.Exists(fullPath))
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = false,
                PhotoStatus = "Missing",
                Message = "Expected photo file was not found.",
            };
        }

        if (!validateDecode)
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = true,
                PhotoStatus = "HasPhoto",
                Message = "Photo file exists. Decode was not checked.",
            };
        }

        try
        {
            var preview = await GetPreviewAsync(hocVien, cancellationToken);
            return preview is null
                ? new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = false,
                    PhotoStatus = "Missing",
                    Message = "Expected photo file was not found.",
                }
                : new HocVienPhotoInspectionDto
                {
                    ExpectedRelativePath = expectedRelativePath,
                    HasPhoto = true,
                    PhotoStatus = "HasPhoto",
                    Message = "Photo file exists and can be decoded.",
                };
        }
        catch (NotSupportedException)
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = true,
                PhotoStatus = "Unsupported",
                Message = "Photo file exists but the image format is not supported by the server decoder.",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = expectedRelativePath,
                HasPhoto = true,
                PhotoStatus = "Invalid",
                Message = "Photo file exists but could not be read or decoded.",
            };
        }
    }

    private static HocVienPhotoPreviewDto DecodeToJpeg(byte[] bytes)
    {
        try
        {
            using var image = new MagickImage(bytes);
            image.AutoOrient();
            image.Format = MagickFormat.Jpeg;
            image.Quality = 90;
            var width = image.Width;
            var height = image.Height;
            return new HocVienPhotoPreviewDto
            {
                ContentType = "image/jpeg",
                Content = image.ToByteArray(),
                PixelWidth = (int)Math.Min(width, int.MaxValue),
                PixelHeight = (int)Math.Min(height, int.MaxValue),
            };
        }
        catch (MagickException ex)
        {
            if (ex.GetType().Name.Contains("MissingDelegate", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Photo format is not supported by the server decoder.", ex);
            }

            throw new InvalidDataException("Photo file exists but could not be decoded.", ex);
        }
    }

    private static string BuildExpectedRelativePath(string? maKhoa, string? maDangKy)
    {
        if (!HocVienPhotoPathResolver.IsSafeSegment(maKhoa) ||
            !HocVienPhotoPathResolver.IsSafeSegment(maDangKy))
        {
            return string.Empty;
        }

        return $"{maKhoa!.Trim()}/{maDangKy!.Trim()}.jp2";
    }

    private Task<HocVienPhotoPrintSelection?> GetProcessedSelectionAsync(
        HocVienListItemDto hocVien,
        CancellationToken cancellationToken)
    {
        if (_processing is null ||
            string.IsNullOrWhiteSpace(hocVien.SourceProfileCode) ||
            string.IsNullOrWhiteSpace(hocVien.MaDangKy) ||
            hocVien.SourceProfileCode.Trim().ToUpperInvariant() is not ("CSDT_OTO" or "CSDT_MOTO"))
        {
            return Task.FromResult<HocVienPhotoPrintSelection?>(null);
        }

        return GetSelectionCoreAsync(hocVien, cancellationToken);
    }

    private async Task<HocVienPhotoPrintSelection?> GetSelectionCoreAsync(
        HocVienListItemDto hocVien,
        CancellationToken cancellationToken) =>
        await _processing!.GetPrintSelectionAsync(
            hocVien.SourceProfileCode!,
            hocVien.MaDangKy,
            cancellationToken);

    private static string MapProcessingStatus(string status) =>
        status switch
        {
            HocVienPhotoProcessingStatuses.ReviewRequired => "ReviewRequired",
            HocVienPhotoProcessingStatuses.Failed => "ProcessingFailed",
            HocVienPhotoProcessingStatuses.Pending or
                HocVienPhotoProcessingStatuses.Processing => "Processing",
            "DERIVED_MISSING" => "DerivedMissing",
            _ => "ProcessingFailed",
        };

}
